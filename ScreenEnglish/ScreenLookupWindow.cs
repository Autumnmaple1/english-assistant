using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using System.Text.Json.Nodes;

namespace ScreenEnglish;

public sealed class ScreenLookupWindow : Window
{
    private readonly Grid root = new();
    private readonly Canvas layer = new();
    private readonly Rectangle highlight = new() { Stroke = UI.Brush(40, 40, 40), StrokeThickness = 1, Fill = UI.Brush(255, 255, 255, 55), IsHitTestVisible = false, Visibility = Visibility.Collapsed };
    private readonly TextBlock meaning = UI.Label("Reading screen locally…", 13);
    private readonly TextBlock title = UI.Label("Screen lookup", 14, true);
    private readonly Border popup;
    private readonly DispatcherQueueTimer timer;
    private readonly LocalDictionary dictionary = new();
    private readonly AiClient ai = new();
    private readonly Dictionary<string, string> cache = [];
    private CancellationTokenSource cancel = new();
    private List<Word> words = [];
    private Native.RECT bounds;
    private int hit = -1, version;
    private long entered;
    private bool requested;
    public bool Alive { get; private set; } = true;
    internal static bool Held() => Native.Held("Ctrl") && Native.Held("Shift") && (Native.GetAsyncKeyState(0x45) & 0x8000) != 0;
    private readonly Func<bool> isHeld;
    public ScreenLookupWindow() : this(Held) { }
    internal ScreenLookupWindow(Func<bool> isHeld) {
        this.isHeld = isHeld;
        Title = "Screen lookup"; Content = root; Native.Borderless(this);
        var content = new StackPanel { Spacing = 7 }; content.Children.Add(title); content.Children.Add(meaning);
        meaning.Foreground = title.Foreground = UI.Ink(false);
        popup = new Border { Child = content, Background = UI.Surface(false), Padding = new Thickness(12), CornerRadius = new CornerRadius(6), Width = 300, IsHitTestVisible = false };
        layer.Children.Add(highlight); layer.Children.Add(popup); Canvas.SetLeft(popup, 20); Canvas.SetTop(popup, 20);
        timer = DispatcherQueue.CreateTimer(); timer.Interval = TimeSpan.FromMilliseconds(30); timer.Tick += (_, _) => Tick();
        Closed += (_, _) => { Alive = false; version++; timer.Stop(); cancel.Cancel(); ai.Dispose(); };
    }
    public async Task Start(byte[] screenshot, Native.RECT monitor) {
        bounds = monitor;
        var image = await UI.Image(screenshot);
        if (!isHeld()) { Close(); return; }
        root.Children.Add(new Image { Source = image, Stretch = Stretch.Fill }); root.Children.Add(layer);
        Native.Place(this, bounds); timer.Start();
        try {
            using var ocr = new OcrService();
            var result = await ocr.Read(screenshot);
            if (!Alive) return;
            words = result.Words; meaning.Text = words.Count == 0 ? "No English words found. Release the shortcut to exit." : "Hover over a word · Release Ctrl, Shift or E to exit";
        } catch { if (Alive) meaning.Text = "Could not read this screen. Release the shortcut and try again."; }
    }
    private void Tick() {
        if (!isHeld() || (Native.GetAsyncKeyState(0x1B) & 0x8000) != 0) { Close(); return; }
        var point = Native.Cursor;
        int next = words.FindIndex(w => point.X - bounds.Left >= w.Box[0] && point.X - bounds.Left <= w.Box[2] && point.Y - bounds.Top >= w.Box[1] && point.Y - bounds.Top <= w.Box[3]);
        if (next < 0 && hit >= 0) {
            var box = words[hit].Box;
            if (point.X - bounds.Left >= box[0] - 4 && point.X - bounds.Left <= box[2] + 4 && point.Y - bounds.Top >= box[1] - 4 && point.Y - bounds.Top <= box[3] + 4) next = hit;
        }
        if (next != hit) {
            hit = next; version++; cancel.Cancel(); cancel.Dispose(); cancel = new(); requested = false; entered = Environment.TickCount64;
            popup.Visibility = highlight.Visibility = Visibility.Collapsed;
            if (hit >= 0) {
                var box = words[hit].Box; double sx = root.ActualWidth / bounds.Width, sy = root.ActualHeight / bounds.Height;
                Canvas.SetLeft(highlight, box[0] * sx); Canvas.SetTop(highlight, box[1] * sy); highlight.Width = (box[2] - box[0]) * sx; highlight.Height = (box[3] - box[1]) * sy; highlight.Visibility = Visibility.Visible;
            }
        }
        if (hit >= 0 && !requested && Environment.TickCount64 - entered >= 200) { requested = true; _ = Define(words[hit], version, cancel.Token); }
    }
    private void Display(Word word, string text) {
        title.Text = word.Text; meaning.Text = text; meaning.MaxHeight = 240;
        popup.Width = Math.Min(300, root.ActualWidth); popup.Measure(new Windows.Foundation.Size(popup.Width, double.PositiveInfinity));
        double x = word.Box[0] * root.ActualWidth / bounds.Width, y = word.Box[3] * root.ActualHeight / bounds.Height + 8;
        if (y + popup.DesiredSize.Height > root.ActualHeight) y = word.Box[1] * root.ActualHeight / bounds.Height - popup.DesiredSize.Height - 8;
        Canvas.SetLeft(popup, Math.Clamp(x, 0, Math.Max(0, root.ActualWidth - popup.Width))); Canvas.SetTop(popup, Math.Max(0, y)); popup.Visibility = Visibility.Visible;
    }
    private async Task Define(Word word, int expected, CancellationToken token) {
        bool Current() => Alive && expected == version && isHeld() && !token.IsCancellationRequested;
        string cacheKey = word.Text + "\0" + word.Context;
        if (cache.TryGetValue(cacheKey, out var saved)) { if (Current()) Display(word, saved); return; }
        Display(word, "Looking up…");
        try {
            var local = await dictionary.Lookup(word.Text);
            if (!Current()) return;
            string? text = local?["simple_english_meaning"]?.ToString();
            if (string.IsNullOrWhiteSpace(text)) {
                var settings = SettingsStore.ForRequest(); settings.Visible["hover_lookup"] = ["simple_english_meaning"];
                var result = await ai.Request(settings, Credentials.Get(settings.BaseUrl), "hover_lookup", word.Context, word.Text, token);
                text = result["simple_english_meaning"]?.ToString();
            }
            if (!Current()) return;
            text = string.IsNullOrWhiteSpace(text) ? "No English definition found." : text;
            cache[cacheKey] = text; Display(word, text);
        } catch (OperationCanceledException) { }
        catch { if (Current()) Display(word, "No local definition; online lookup unavailable."); }
    }
}

