using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ScreenEnglish;

public sealed class Controller : IDisposable
{
    private readonly App app;
    private readonly Window host = new() { Title = "Screen English tray", Content = new Grid() };
    private readonly Native.SubclassProc callback;
    private Native.NOTIFYICONDATA tray;
    private readonly uint taskbarCreated = Native.RegisterWindowMessage("TaskbarCreated");
    private readonly OcrService ocr = new();
    private readonly AiClient ai = new();
    private readonly LocalDictionary dictionary = new();
    private readonly DispatcherQueueTimer timer;
    private readonly Dictionary<int, (uint Modifiers, uint Key)> hotkeys = [];
    private Settings settings;
    private LearningWindow? panel;
    private SettingsWindow? preferences;
    private HoverWindow? hover;
    private ScreenLookupWindow? screenLookup;
    private bool startingScreenLookup;
    private ActiveCapture? active;
    private CancellationTokenSource operationCancel = new(), lookupCancel = new();
    private int operation, lookupVersion;
    private bool capturing, disposed, manualLookup;
    private string candidate = "", context = "", sent = "";
    private long candidateAt, leftAt;
    private Native.POINT candidatePoint;
    private readonly Dictionary<string, JsonObject> lookupCache = [];

    public Controller(App app) {
        this.app = app; settings = SettingsStore.Load(out var warning);
        RuntimeLog.Write($"startup settingsLoaded={warning is null} modelConfigured={!string.IsNullOrWhiteSpace(settings.Model)}");
        Native.ToolWindow(host); callback = WindowMessage;
        if (!Native.SetWindowSubclass(Native.Hwnd(host), callback, 1, 0)) throw new Exception("Could not initialize tray messages.");
        tray = new Native.NOTIFYICONDATA { Size = (uint)Marshal.SizeOf<Native.NOTIFYICONDATA>(), Window = Native.Hwnd(host), Id = 1, Flags = 1 | 2 | 4,
            Callback = 0x8001, Icon = Native.TrayIcon(), Tip = "Screen English · Capture, translate, learn", Info = "", Title = "" };
        if (!Native.Shell_NotifyIcon(0, ref tray)) throw new Exception("The Windows system tray is unavailable.");
        try { Register(settings); } catch (Exception error) { Notify(error.Message); }
        if (warning is not null) Notify(warning);
        timer = host.DispatcherQueue.CreateTimer(); timer.Interval = TimeSpan.FromMilliseconds(60); timer.Tick += (_, _) => PollHover(); timer.Start();
    }
    private nint WindowMessage(nint hwnd, uint message, nuint wParam, nint lParam, nuint id, nuint data) {
        if (message == taskbarCreated) { Native.Shell_NotifyIcon(0, ref tray); return 0; }
        if (message == 0x0312) { if ((int)wParam == 3) _ = StartScreenLookup(); else _ = Capture((int)wParam == 1 ? "translate" : "rewrite"); return 0; }
        if (message == 0x8001) {
            if ((int)lParam is 0x0205 or 0x007B) Menu();
            else if ((int)lParam == 0x0203) OpenSettings();
            return 0;
        }
        return Native.DefSubclassProc(hwnd, message, wParam, lParam);
    }
    private void Menu() {
        nint menu = Native.CreatePopupMenu();
        try {
            Native.AppendMenu(menu, 0, 1, "Capture and translate\t" + settings.TranslateShortcut);
            Native.AppendMenu(menu, 0, 2, "Capture and rewrite\t" + settings.RewriteShortcut);
            Native.AppendMenu(menu, panel?.Alive == true ? 0u : 1u, 3, "Show learning panel");
            Native.AppendMenu(menu, 0x800, 0, ""); Native.AppendMenu(menu, 0, 4, "Settings…");
            Native.AppendMenu(menu, Native.StartupEnabled ? 8u : 0u, 5, "Start with Windows");
            Native.AppendMenu(menu, 0x800, 0, ""); Native.AppendMenu(menu, 0, 6, "Exit");
            Native.SetForegroundWindow(Native.Hwnd(host)); var point = Native.Cursor;
            uint selected = Native.TrackPopupMenu(menu, 0x100 | 2, point.X, point.Y, 0, Native.Hwnd(host), 0);
            Native.PostMessage(Native.Hwnd(host), 0, 0, 0);
            switch (selected) {
                case 1: _ = Capture("translate"); break;
                case 2: _ = Capture("rewrite"); break;
                case 3: if (panel?.Alive == true) { Native.ShowWindow(Native.Hwnd(panel), 9); panel.Activate(); } break;
                case 4: OpenSettings(); break;
                case 5: try { Native.SetStartup(!Native.StartupEnabled); } catch { Notify("Could not update Start with Windows."); } break;
                case 6: Dispose(); app.Exit(); break;
            }
        } finally { Native.DestroyMenu(menu); }
    }
    private void Notify(string message) {
        tray.Flags |= 0x10; tray.Info = message.Length > 255 ? message[..255] : message; tray.Title = "Screen English"; tray.InfoFlags = 1;
        Native.Shell_NotifyIcon(1, ref tray); tray.Flags &= ~0x10u; tray.Info = "";
    }
    private void Register(Settings next) {
        var desired = new Dictionary<int, (uint Modifiers, uint Key)> { [1] = Native.ParseHotkey(next.TranslateShortcut), [2] = Native.ParseHotkey(next.RewriteShortcut), [3] = Native.ParseHotkey("Ctrl+Shift+E") };
        if (desired.Values.Distinct().Count() != desired.Count) throw new Exception("Capture shortcuts must differ from each other and Ctrl+Shift+E (screen lookup).");
        var previous = hotkeys.ToDictionary(p => p.Key, p => p.Value); Unregister();
        try {
            foreach (var pair in desired) {
                if (!Native.RegisterHotKey(Native.Hwnd(host), pair.Key, pair.Value.Modifiers | 0x4000, pair.Value.Key)) throw new Exception($"The {(pair.Key == 1 ? "Translate" : pair.Key == 2 ? "Rewrite" : "Ctrl+Shift+E screen lookup")} shortcut is already in use.");
                hotkeys[pair.Key] = pair.Value;
            }
        } catch {
            Unregister(); foreach (var pair in previous) if (Native.RegisterHotKey(Native.Hwnd(host), pair.Key, pair.Value.Modifiers | 0x4000, pair.Value.Key)) hotkeys[pair.Key] = pair.Value;
            throw;
        }
    }
    private void Unregister() { foreach (int id in hotkeys.Keys) Native.UnregisterHotKey(Native.Hwnd(host), id); hotkeys.Clear(); }
    internal bool IsTrayOnly => !Native.IsWindowVisible(Native.Hwnd(host)) && panel is null && preferences is null;
    internal int RegisteredShortcutCount => hotkeys.Count;
    private void OpenSettings() {
        StopHover();
        if (preferences?.Alive != true) preferences = new SettingsWindow(settings, SaveSettings);
        Native.Owner(preferences, panel?.Alive == true ? panel : host);
        preferences.Activate(); Native.SetForegroundWindow(Native.Hwnd(preferences));
    }
    private Task SaveSettings(Settings next, string? key, bool startup) {
        SettingsStore.Validate(next); Register(next);
        try {
            if (key is not null) Credentials.Set(next.BaseUrl, key);
            Native.SetStartup(startup); SettingsStore.Save(next);
        } catch { Register(settings); throw; }
        settings = next; CancelOperation(); StopHover(); lookupCache.Clear();
        if (panel?.Alive == true) { panel.Apply(settings); panel.Message("Settings updated. Use Translate or Rewrite to refresh the result."); }
        return Task.CompletedTask;
    }
    private void EnsurePanel() {
        if (panel?.Alive == true) return;
        panel = new LearningWindow(settings);
        panel.ActionRequested += action => { if (action == "explain") { if (active is not null) BeginManualLookup(active.Text, active.Text); } else _ = RunAction(action); };
        panel.HoverRequested += SetCandidate; panel.HoverLeft += LeaveCandidate;
        panel.Dismissed += () => { if (preferences?.Alive == true) Native.Owner(preferences, host); CancelOperation(); StopHover(); };
    }
    private int CancelOperation() { operationCancel.Cancel(); operationCancel.Dispose(); operationCancel = new(); return ++operation; }
    private async Task StartScreenLookup() {
        if (disposed || capturing || startingScreenLookup || screenLookup?.Alive == true || !ScreenLookupWindow.Held()) return;
        startingScreenLookup = true; StopHover();
        try {
            var bounds = Native.Monitor(Native.Cursor);
            byte[] screenshot = Native.Screenshot(bounds);
            if (!ScreenLookupWindow.Held()) return;
            screenLookup = new ScreenLookupWindow();
            await screenLookup.Start(screenshot, bounds);
        } catch { if (screenLookup?.Alive == true) screenLookup.Close(); Notify("Could not start screen lookup. Release the shortcut and try again."); }
        finally { startingScreenLookup = false; }
    }
    private async Task Capture(string action) {
        RuntimeLog.Write($"capture action={action} busy={capturing} disposed={disposed}");
        if (capturing || disposed || startingScreenLookup || screenLookup?.Alive == true) return;
        capturing = true; int version = CancelOperation(); StopHover();
        var mouse = Native.Cursor; var bounds = Native.Monitor(mouse);
        bool panelVisible = panel?.Alive == true && panel.HoverEnabled;
        bool settingsVisible = preferences?.Alive == true && Native.IsWindowVisible(Native.Hwnd(preferences));
        if (settingsVisible) Native.ShowWindow(Native.Hwnd(preferences!), 0);
        if (panelVisible) Native.ShowWindow(Native.Hwnd(panel!), 0);
        try {
            await Task.Delay(100);
            byte[] screenshot = Native.Screenshot(bounds);
            var overlay = new CaptureWindow(); var crop = await overlay.Select(screenshot, bounds, action);
            RuntimeLog.Write($"capture selected={crop is not null}");
            if (crop is null) { if (panelVisible && panel?.Alive == true) { Native.ShowWindow(Native.Hwnd(panel), 5); panel.Message("Capture canceled."); } return; }
            byte[] image = Native.Crop(screenshot, crop.Value);
            var selected = new Native.RECT(bounds.Left + crop.Value.Left, bounds.Top + crop.Value.Top, bounds.Left + crop.Value.Right, bounds.Top + crop.Value.Bottom);
            EnsurePanel(); panel!.ShowNear(selected); panel.Busy("Reading text locally…");
            var result = await ocr.Read(image);
            RuntimeLog.Write($"ocr complete characters={result.Text.Length}");
            if (version != operation || disposed || panel?.Alive != true) return;
            active = new ActiveCapture(result.Text, result.Words, selected, image); lookupCache.Clear();
            var placeholder = action == "translate" ? new JsonObject { ["action"] = "translate", ["source_text"] = result.Text, ["translation"] = "", ["source_language"] = "en", ["target_language"] = "zh-CN" }
                : new JsonObject { ["action"] = "rewrite", ["original_english"] = result.Text, ["natural_english"] = "" };
            panel.Render(placeholder);
            capturing = false; _ = RunAction(action);
        } catch (Exception error) {
            if (version == operation && !disposed) { EnsurePanel(); panel!.ShowNear(bounds); panel.Message(error.Message); }
        } finally {
            capturing = false;
            if (settingsVisible && preferences?.Alive == true) { Native.ShowWindow(Native.Hwnd(preferences), 5); preferences.Activate(); }
        }
    }
    private async Task RunAction(string action) {
        RuntimeLog.Write($"panel action={action} hasCapture={active is not null} alive={panel?.Alive == true}");
        if (active is null || panel?.Alive != true) return;
        int version = CancelOperation(); StopHover(); var token = operationCancel.Token;
        panel.Busy(action == "rewrite" ? "Rewriting the English…" : "Translating into Chinese…");
        try {
            var requestSettings = SettingsStore.ForRequest();
            var result = await ai.Request(requestSettings, Credentials.Get(requestSettings.BaseUrl), action, active.Text, cancellation: token);
            if (version == operation && panel?.Alive == true && !disposed) panel.Render(result);
        } catch (OperationCanceledException) { RuntimeLog.Write("panel request canceled"); }
        catch (Exception error) { RuntimeLog.Write($"panel request failed type={error.GetType().Name}"); if (version == operation && panel?.Alive == true) panel.Message(error.Message); }
    }
    private void SetCandidate(string term, string sentence) {
        if (!Native.Held(settings.HoverModifier) || panel?.HoverEnabled != true || capturing) return;
        leftAt = 0; manualLookup = false; var point = Native.Cursor;
        if (candidate == term && context == sentence) {
            return;
        }
        InvalidateLookup(); candidate = term; context = sentence; sent = ""; candidatePoint = point; candidateAt = Environment.TickCount64;
    }
    private void LeaveCandidate() { if (leftAt == 0) leftAt = Environment.TickCount64; }
    private bool Over(Window? window, Native.POINT point) => window is not null && Native.IsWindowVisible(Native.Hwnd(window)) && Native.GetWindowRect(Native.Hwnd(window), out var rect) && rect.Contains(point);
    private void PollHover() {
        if (disposed) return;
        if (startingScreenLookup || screenLookup?.Alive == true) { StopHover(); return; }
        if (preferences?.Alive == true && Native.IsWindowVisible(Native.Hwnd(preferences))) { StopHover(); return; }
        if (!capturing && panel?.HoverEnabled == true && (Native.GetAsyncKeyState(0x1B) & 0x8000) != 0) { panel.Close(); return; }
        if (capturing || active is null || panel?.HoverEnabled != true) { StopHover(); return; }
        if (!Native.Held(settings.HoverModifier)) { if (!manualLookup) StopHover(); return; }
        var point = Native.Cursor; bool overHover = hover?.Alive == true && Over(hover, point);
        if (overHover) { leftAt = 0; return; }
        if (!Over(panel, point) && !manualLookup) LeaveCandidate();
        if (leftAt != 0 && Environment.TickCount64 - leftAt > 240) { InvalidateLookup(); candidate = sent = ""; leftAt = 0; return; }
        if (candidate.Length > 0 && candidate != sent && Environment.TickCount64 - candidateAt >= settings.HoverDelay) { sent = candidate; _ = Lookup(candidate, context); }
    }
    private void InvalidateLookup() {
        lookupVersion++; lookupCancel.Cancel(); lookupCancel.Dispose(); lookupCancel = new();
        if (hover?.Alive == true) Native.ShowWindow(Native.Hwnd(hover), 0);
    }
    private void StopHover() { if (candidate.Length > 0 || hover?.Alive == true && Native.IsWindowVisible(Native.Hwnd(hover))) InvalidateLookup(); candidate = context = sent = ""; leftAt = 0; manualLookup = false; }
    private void BeginManualLookup(string term, string sentence) { StopHover(); manualLookup = true; candidate = sent = term; context = sentence; _ = Lookup(term, sentence); }
    private async Task Lookup(string term, string sentence) {
        int version = ++lookupVersion; var token = lookupCancel.Token; var snapshot = SettingsStore.Clone(settings);
        if (snapshot.Visible["hover_lookup"].Count == 0) return;
        bool Current() => !disposed && version == lookupVersion && panel?.HoverEnabled == true && (manualLookup || Native.Held(settings.HoverModifier));
        void Display(JsonObject? data, string source) {
            if (!Current()) return;
            if (hover?.Alive != true) { hover = new HoverWindow(); hover.Dismissed += StopHover; } hover.Render(term, data, snapshot, source);
        }
        string cacheKey = term + "\0" + sentence;
        if (lookupCache.TryGetValue(cacheKey, out var cached)) { Display(cached, "AI explanation · cached for this capture"); return; }
        if (snapshot.Dictionary == "Cambridge (browser)") { Display(null, "Click below to look up this term on Cambridge."); return; }
        JsonObject? local = null; Display(null, "Looking up…");
        if (snapshot.Dictionary is "Local WordNet" or "Automatic") {
            try {
                local = await dictionary.Lookup(term); if (!Current()) return;
                bool sufficient = !string.IsNullOrWhiteSpace(local?["simple_english_meaning"]?.ToString());
                if (snapshot.Dictionary == "Local WordNet" || snapshot.Model.Length == 0 || sufficient) {
                    if (local is not null) { if (lookupCache.Count >= 100) lookupCache.Clear(); lookupCache[cacheKey] = local; }
                    Display(local, local is null ? "No entry in the local dictionary." : "WordNet · general meanings"); return;
                }
            } catch (Exception error) { Display(null, error.Message); }
            if (snapshot.Dictionary == "Local WordNet" || snapshot.Model.Length == 0) return;
        }
        try {
            snapshot = SettingsStore.ForRequest();
            var result = await ai.Request(snapshot, Credentials.Get(snapshot.BaseUrl), "hover_lookup", sentence, term, token);
            if (!Current()) return;
            if (lookupCache.Count >= 100) lookupCache.Clear(); lookupCache[cacheKey] = result; Display(result, "AI explanation · check against the original context");
        } catch (OperationCanceledException) { }
        catch (Exception error) { Display(local, error.Message + (local is null ? "" : " Showing general WordNet meanings.")); }
    }
    public void Dispose() {
        if (disposed) return; disposed = true; timer.Stop(); operationCancel.Cancel(); lookupCancel.Cancel();
        Native.Shell_NotifyIcon(2, ref tray); Native.DestroyIcon(tray.Icon); Unregister(); Native.RemoveWindowSubclass(Native.Hwnd(host), callback, 1);
        ai.Dispose(); if (hover?.Alive == true) hover.Close();
        if (screenLookup?.Alive == true) screenLookup.Close();
        if (preferences?.Alive == true) preferences.Close(); if (panel?.Alive == true) panel.Close(); host.Close();
    }
}


