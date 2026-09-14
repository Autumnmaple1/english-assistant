using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;
using Windows.Storage.Streams;
using Windows.System;

namespace ScreenEnglish;

public static class UI
{
    public static SolidColorBrush Brush(byte r, byte g, byte b, byte alpha = 255) => new(Windows.UI.Color.FromArgb(alpha, r, g, b));
    public static SolidColorBrush Surface(bool dark) => dark ? Brush(32, 32, 32) : Brush(250, 250, 249);
    public static SolidColorBrush Card(bool dark) => dark ? Brush(41, 41, 41) : Brush(240, 240, 238);
    public static SolidColorBrush Line(bool dark) => dark ? Brush(56, 56, 56) : Brush(222, 222, 219);
    public static SolidColorBrush Ink(bool dark) => dark ? Brush(231, 231, 229) : Brush(37, 37, 37);
    public static SolidColorBrush Muted(bool dark) => dark ? Brush(150, 150, 148) : Brush(116, 116, 113);
    public static Button Button(string text, Action click, bool primary = false) {
        var button = new Button { Content = text, Style = (Style)Application.Current.Resources[primary ? "PrimaryButton" : "QuietButton"] };
        button.Click += (_, _) => click(); return button;
    }
    public static Button Icon(string glyph, string tooltip, Action click) {
        var button = Button("", click); button.Content = new FontIcon { Glyph = glyph, FontSize = 12 }; button.Width = 28; button.Height = 28; button.Padding = new Thickness(0);
        ToolTipService.SetToolTip(button, tooltip); Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, tooltip); return button;
    }
    public static TextBlock Label(string text, double size = 13, bool strong = false) => new() {
        Text = text, FontSize = size, FontFamily = new FontFamily("Segoe UI"), FontWeight = strong ? FontWeights.SemiBold : FontWeights.Normal, TextWrapping = TextWrapping.Wrap
    };
    public static void Draggable(UIElement handle, Window window) => handle.PointerPressed += (_, e) => {
        if (e.GetCurrentPoint(handle).Properties.IsLeftButtonPressed) { Native.ReleaseCapture(); Native.SendMessage(Native.Hwnd(window), 0xA1, 2, 0); }
    };
    public static void Escape(FrameworkElement root, Action close) {
        root.KeyboardAcceleratorPlacementMode = KeyboardAcceleratorPlacementMode.Hidden;
        var escape = new KeyboardAccelerator { Key = VirtualKey.Escape };
        escape.Invoked += (_, e) => { close(); e.Handled = true; }; root.KeyboardAccelerators.Add(escape);
    }
    public static async Task<BitmapImage> Image(byte[] bytes) {
        using var stream = new InMemoryRandomAccessStream();
        using (var writer = new DataWriter(stream.GetOutputStreamAt(0))) { writer.WriteBytes(bytes); await writer.StoreAsync(); }
        stream.Seek(0); var bitmap = new BitmapImage(); await bitmap.SetSourceAsync(stream); return bitmap;
    }
    public static void Clipboard(string text) { var package = new DataPackage(); package.SetText(text); Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(package); }
    public static void Cambridge(string term) => System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("https://dictionary.cambridge.org/dictionary/english/" + Uri.EscapeDataString(term.Trim())) { UseShellExecute = true });
}

public sealed class LearningWindow : Window
{
    public event Action<string>? ActionRequested;
    public event Action<string, string>? HoverRequested;
    public event Action? HoverLeft;
    public event Action? Dismissed;
    private readonly Grid root = new();
    private readonly Border shell = new();
    private readonly StackPanel actions = new() { Orientation = Orientation.Horizontal, Spacing = 2 };
    private readonly Grid body = new();
    private readonly StackPanel notice = new() { Spacing = 7, Visibility = Visibility.Collapsed, Margin = new Thickness(0, 0, 0, 12) };
    private readonly TextBlock status = UI.Label("", 12);
    private readonly TextBlock heading = UI.Label("Screen English", 12, true);
    private readonly TextBlock mode = UI.Label("Translate", 11);
    private readonly TextBlock shortcut = UI.Label("Esc to close", 10);
    private readonly ProgressBar progress = new() { IsIndeterminate = true, Height = 2, Visibility = Visibility.Collapsed, MinWidth = 10 };
    private readonly Button pin;
    private readonly Border headerLine = new() { BorderThickness = new Thickness(0, 0, 0, 1) };
    private readonly Border footerLine = new() { BorderThickness = new Thickness(0, 1, 0, 0) };
    private Settings settings;
    private JsonObject? result;
    private bool pinned;
    public bool Alive { get; private set; } = true;
    public bool HoverEnabled => Alive && Native.IsWindowVisible(Native.Hwnd(this)) && !Native.IsIconic(Native.Hwnd(this));

    public LearningWindow(Settings settings) {
        this.settings = settings; Title = "Screen English";
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var title = new Grid { Height = 42, Padding = new Thickness(16, 0, 8, 0), Background = new SolidColorBrush(Colors.Transparent), ColumnSpacing = 8 };
        title.ColumnDefinitions.Add(new ColumnDefinition()); title.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var identity = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 9, VerticalAlignment = VerticalAlignment.Center };
        identity.Children.Add(new Image { Source = new BitmapImage(new Uri(Path.Combine(AppContext.BaseDirectory, "Assets", "ScreenEnglish.png"))), Width = 20, Height = 20 }); identity.Children.Add(heading); identity.Children.Add(UI.Label("/", 12)); identity.Children.Add(mode);
        var dragArea = new Border { Background = new SolidColorBrush(Colors.Transparent), Child = identity };
        title.Children.Add(dragArea); UI.Draggable(dragArea, this);
        var controls = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 1, VerticalAlignment = VerticalAlignment.Center };
        pin = UI.Icon("\uE718", "Pin panel", () => { pinned = !pinned; if (AppWindow.Presenter is OverlappedPresenter presenter) presenter.IsAlwaysOnTop = pinned; if (pin is not null) { pin.Opacity = pinned ? 1 : 0.6; ToolTipService.SetToolTip(pin, pinned ? "Unpin panel" : "Pin panel"); } }); pin.Opacity = 0.6;
        controls.Children.Add(pin); controls.Children.Add(UI.Icon("\uE921", "Minimize", () => { HoverLeft?.Invoke(); Native.ShowWindow(Native.Hwnd(this), 6); }));
        controls.Children.Add(UI.Icon("\uE8BB", "Close · Esc", Close)); Grid.SetColumn(controls, 1); title.Children.Add(controls);
        headerLine.Child = title; root.Children.Add(headerLine);
        body.Padding = new Thickness(18, 16, 18, 16); body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); body.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        notice.Children.Add(status); notice.Children.Add(progress); body.Children.Add(notice); Grid.SetRow(body, 1); root.Children.Add(body);
        var footer = new Grid { Padding = new Thickness(9, 7, 14, 7) }; footer.ColumnDefinitions.Add(new ColumnDefinition()); footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        footer.Children.Add(actions); shortcut.VerticalAlignment = VerticalAlignment.Center; Grid.SetColumn(shortcut, 1); footer.Children.Add(shortcut);
        footerLine.Child = footer; Grid.SetRow(footerLine, 2); root.Children.Add(footerLine);
        shell.Child = root; Content = shell; Native.Borderless(this, false);
        if (AppWindow.Presenter is OverlappedPresenter p) p.IsMinimizable = true;
        UI.Escape(root, Close);
        Closed += (_, _) => { Alive = false; Dismissed?.Invoke(); }; Apply(settings);
    }
    public void Apply(Settings preferences) {
        settings = preferences; bool dark = settings.Theme == "Dark";
        shell.RequestedTheme = dark ? ElementTheme.Dark : ElementTheme.Light; root.Background = UI.Surface(dark); shell.BorderBrush = UI.Line(dark);
        headerLine.BorderBrush = footerLine.BorderBrush = UI.Line(dark); mode.Foreground = status.Foreground = shortcut.Foreground = UI.Muted(dark);
        Native.Opacity(this, settings.Opacity); actions.Children.Clear();
        foreach (string name in settings.Buttons) {
            var button = UI.Button(name, () => {
                if (name == "Copy") { if (result is not null) UI.Clipboard(Fields.Text(result[result["action"]?.ToString() == "rewrite" ? "natural_english" : "translation"])); }
                else ActionRequested?.Invoke(name.ToLowerInvariant());
            });
            var label = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5 };
            label.Children.Add(new FontIcon { FontSize = 11, Glyph = name switch { "Copy" => "\uE8C8", "Rewrite" => "\uE70F", "Translate" => "\uE8F2", _ => "\uE8BD" } }); label.Children.Add(UI.Label(name, 11)); button.Content = label; actions.Children.Add(button);
        }
        if (result is not null) Render(result);
    }
    public void Busy(string message) {
        status.Text = message; notice.Visibility = Visibility.Visible; progress.Visibility = Visibility.Visible;
        foreach (var button in actions.Children.OfType<Button>()) button.IsEnabled = false;
    }
    public void Message(string message) {
        status.Text = message; notice.Visibility = string.IsNullOrWhiteSpace(message) ? Visibility.Collapsed : Visibility.Visible; progress.Visibility = Visibility.Collapsed;
        foreach (var button in actions.Children.OfType<Button>()) button.IsEnabled = true;
    }
    public void ShowNear(Native.RECT bounds) {
        int width = settings.Layout == "Side by side" ? 760 : settings.Layout == "Compact" ? 440 : 510;
        int height = settings.Layout == "Compact" ? 350 : 500;
        double scale = Native.Scale(new Native.POINT(bounds.Left, bounds.Top)); Native.Place(this, Native.Near(bounds, (int)(width * scale), (int)(height * scale)));
        if (result is not null) Render(result);
    }
    public void Render(JsonObject data) {
        result = (JsonObject)data.DeepClone(); string action = data["action"]!.ToString(); bool dark = settings.Theme == "Dark";
        mode.Text = action == "rewrite" ? "Rewrite" : "Translate";
        while (body.Children.Count > 1) body.Children.RemoveAt(1);
        var sections = new List<(string Key, FrameworkElement View)>();
        foreach (string field in settings.Visible[action].Where(f => f is not ("source_language" or "target_language")).OrderBy(f => f is "translation" or "natural_english" ? 0 : 1)) {
            string text = Fields.Text(data[field]); if (string.IsNullOrWhiteSpace(text)) continue;
            bool primary = field is "translation" or "natural_english";
            bool source = field is "source_text" or "original_english";
            var section = new StackPanel { Spacing = 7, Margin = new Thickness(0, 0, 0, 16) };
            var caption = UI.Label(Fields.Labels[field], 11, true); caption.Foreground = UI.Muted(dark); section.Children.Add(caption);
            var block = EnglishText(text); block.Foreground = primary ? UI.Ink(dark) : source ? UI.Muted(dark) : UI.Ink(dark);
            if (field is "source_language" or "target_language") block.FontSize = 11;
            if (primary) section.Children.Add(new Border { Child = block, Background = UI.Card(dark), CornerRadius = new CornerRadius(6), Padding = new Thickness(13, 11, 13, 11) });
            else section.Children.Add(block);
            sections.Add((field, section));
        }
        FrameworkElement rendered;
        if (settings.Layout == "Tabs" && sections.Count > 0) {
            var tabs = new Grid(); tabs.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); tabs.RowDefinitions.Add(new RowDefinition());
            var strip = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 3, Margin = new Thickness(0, 0, 0, 12) }; var page = new ContentControl { HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch };
            Grid.SetRow(page, 1); tabs.Children.Add(new ScrollViewer { Content = strip, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollMode = ScrollMode.Enabled, VerticalScrollMode = ScrollMode.Disabled }); tabs.Children.Add(page);
            foreach (var section in sections) { var tab = UI.Button(Fields.Labels[section.Key], () => { if (page.Content is ScrollViewer previous) previous.Content = null; page.Content = Scroll(section.View); }); tab.FontSize = 11; strip.Children.Add(tab); }
            page.Content = Scroll(sections[0].View); rendered = tabs;
        } else if (settings.Layout == "Side by side") {
            var grid = new Grid { ColumnSpacing = 22 }; grid.ColumnDefinitions.Add(new ColumnDefinition()); grid.ColumnDefinitions.Add(new ColumnDefinition());
            var left = new StackPanel(); var right = new StackPanel(); foreach (var section in sections) (section.Key is "source_text" or "original_english" ? left : right).Children.Add(section.View);
            grid.Children.Add(Scroll(left)); var scroll = Scroll(right); Grid.SetColumn(scroll, 1); grid.Children.Add(scroll); rendered = grid;
        } else {
            var stack = new StackPanel(); foreach (var section in sections) stack.Children.Add(section.View);
            if (sections.Count == 0) stack.Children.Add(UI.Label("Choose the content to display in Settings.", 12)); rendered = Scroll(stack);
        }
        Grid.SetRow(rendered, 1); body.Children.Add(rendered); Message("");
        FitContent(sections);
        ToolTipService.SetToolTip(heading, $"Hold {settings.HoverModifier} over a word to look it up. Drag to select a phrase.");
    }
    private void FitContent(List<(string Key, FrameworkElement View)> sections) {
        if (!Native.GetWindowRect(Native.Hwnd(this), out var current)) return;
        var work = Native.Monitor(new Native.POINT(current.Left, current.Top), true);
        double scale = Native.GetDpiForWindow(Native.Hwnd(this)) / 96.0;
        double availableWidth = work.Width / scale, availableHeight = work.Height / scale;
        bool columns = settings.Layout == "Side by side";
        double width = Math.Min(availableWidth, columns ? 760 : settings.Layout == "Compact" ? 440 : 510);
        double HeightAt(double w) {
            double contentWidth = Math.Max(1, columns ? (w - 58) / 2 : w - 36);
            foreach (var section in sections) section.View.Measure(new Size(contentWidth, double.PositiveInfinity));
            double height = settings.Layout == "Tabs" ? sections.Select(s => s.View.DesiredSize.Height).DefaultIfEmpty().Max() + 44
                : columns ? Math.Max(sections.Where(s => s.Key is "source_text" or "original_english").Sum(s => s.View.DesiredSize.Height), sections.Where(s => s.Key is not ("source_text" or "original_english")).Sum(s => s.View.DesiredSize.Height))
                : sections.Sum(s => s.View.DesiredSize.Height);
            return height + 132; // Header, footer, content padding and layout rounding.
        }
        double height = HeightAt(width);
        double maximumWidth = Math.Min(availableWidth, columns ? 900 : 640);
        while (height > availableHeight && width < maximumWidth) { width = Math.Min(width + 80, maximumWidth); height = HeightAt(width); }
        int pixelsWide = Math.Min(work.Width, (int)Math.Ceiling(width * scale));
        int pixelsHigh = Math.Min(work.Height, (int)Math.Ceiling(Math.Max(220, height) * scale));
        int x = Math.Clamp(current.Left, work.Left, work.Right - pixelsWide), y = Math.Clamp(current.Top, work.Top, work.Bottom - pixelsHigh);
        Native.SetWindowPos(Native.Hwnd(this), 0, x, y, pixelsWide, pixelsHigh, 0x14); // Preserve focus and stacking.
    }
    private static ScrollViewer Scroll(FrameworkElement content) => new() { Content = content, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalContentAlignment = HorizontalAlignment.Stretch };
    private RichTextBlock EnglishText(string text) {
        text = Regex.Replace(text, @"\r?\n(?:[\t ]*\r?\n)+", "\n");
        var block = new RichTextBlock { FontSize = settings.FontSize, LineHeight = settings.FontSize * 1.35, LineStackingStrategy = LineStackingStrategy.BlockLineHeight, IsTextSelectionEnabled = true, TextWrapping = TextWrapping.Wrap };
        var run = new Run { Text = text }; var paragraph = new Paragraph(); paragraph.Inlines.Add(run); block.Blocks.Add(paragraph);
        block.PointerMoved += (_, e) => {
            if (!Native.Held(settings.HoverModifier) || e.GetCurrentPoint(block).Properties.IsLeftButtonPressed) return;
            string? term = TermAt(block, run, text, e.GetCurrentPoint(block).Position); if (term is not null) HoverRequested?.Invoke(term, text); else HoverLeft?.Invoke();
        };
        block.PointerReleased += (_, _) => { if (Native.Held(settings.HoverModifier) && !string.IsNullOrWhiteSpace(block.SelectedText)) HoverRequested?.Invoke(block.SelectedText, text); };
        block.PointerExited += (_, _) => HoverLeft?.Invoke(); return block;
    }
    internal static string? TermAt(RichTextBlock block, Run run, string text, Point point) {
        var pointer = block.GetPositionFromPoint(point); if (pointer is null) return null; int index = pointer.Offset - run.ContentStart.Offset;
        var rect = pointer.GetCharacterRect(LogicalDirection.Forward);
        if (point.Y < rect.Top - 3 || point.Y > rect.Bottom + 3 || point.X < rect.Left - 12 || point.X > rect.Right + 12) return null;
        return Regex.Matches(text, @"[A-Za-z]+(?:['’\-][A-Za-z]+)*").Cast<Match>().FirstOrDefault(m => index >= m.Index && index < m.Index + m.Length)?.Value;
    }
}

public sealed class HoverWindow : Window
{
    public event Action? Dismissed;
    private readonly StackPanel content = new() { Spacing = 8, Padding = new Thickness(12) };
    private readonly Border shell = new();
    public bool Alive { get; private set; } = true;
    public string Term { get; private set; } = "";
    public HoverWindow() {
        Title = "Word lookup"; shell.Child = new ScrollViewer { Content = content }; Content = shell;
        Native.Borderless(this); Native.ToolWindow(this, true); UI.Escape(shell, Dismiss); Closed += (_, _) => Alive = false;
    }
    private void Dismiss() { Native.ShowWindow(Native.Hwnd(this), 0); Dismissed?.Invoke(); }
    public void Render(string term, JsonObject? data, Settings settings, string source) {
        Term = term; content.Children.Clear(); bool dark = settings.Theme == "Dark";
        shell.RequestedTheme = dark ? ElementTheme.Dark : ElementTheme.Light; shell.BorderBrush = UI.Line(dark); content.Background = UI.Surface(dark);
        var header = new Grid(); header.ColumnDefinitions.Add(new ColumnDefinition()); header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.Children.Add(UI.Label(term, 16, true)); var close = UI.Icon("\uE8BB", "Close · Esc", Dismiss); Grid.SetColumn(close, 1); header.Children.Add(close); content.Children.Add(header);
        if (data is null) { var hint = UI.Label(source, 11); hint.Foreground = UI.Muted(dark); content.Children.Add(hint); }
        if (data is not null) foreach (string field in settings.Visible["hover_lookup"]) {
            string text = Fields.Text(data[field]); if (string.IsNullOrWhiteSpace(text)) continue;
            var value = UI.Label(text, Math.Min(settings.FontSize, 13)); value.IsTextSelectionEnabled = true;
            value.Foreground = field == "chinese_meaning" ? UI.Ink(dark) : UI.Muted(dark); content.Children.Add(value);
        }
        var link = UI.Button("Cambridge Dictionary  ↗", () => UI.Cambridge(term)); link.HorizontalAlignment = HorizontalAlignment.Left; link.FontSize = 11; link.Margin = new Thickness(-9, 0, 0, 0); content.Children.Add(link);
        Native.Opacity(this, settings.Opacity);
        content.Measure(new Windows.Foundation.Size(300, double.PositiveInfinity));
        int height = (int)Math.Clamp(content.DesiredSize.Height + 8, 100, 320);
        if (!Native.IsWindowVisible(Native.Hwnd(this))) {
            var mouse = Native.Cursor; var anchor = new Native.RECT(mouse.X + 10, mouse.Y + 16, mouse.X + 10, mouse.Y + 16); double scale = Native.Scale(mouse);
            Native.Place(this, Native.Near(anchor, (int)(300 * scale), (int)(height * scale)), false);
        } else if (Native.GetWindowRect(Native.Hwnd(this), out var rect)) {
            double scale = Native.GetDpiForWindow(Native.Hwnd(this)) / 96.0;
            var work = Native.Monitor(new Native.POINT(rect.Left, rect.Top), true);
            int h = Math.Min(work.Height, (int)(height * scale));
            Native.SetWindowPos(Native.Hwnd(this), 0, rect.Left, Math.Clamp(rect.Top, work.Top, work.Bottom - h), rect.Width, h, 0x14);
        }
    }
}
