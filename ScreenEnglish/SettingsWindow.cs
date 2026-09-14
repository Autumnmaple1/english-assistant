using System.Text.Json.Nodes;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace ScreenEnglish;

public sealed class SettingsWindow : Window
{
    private readonly Settings original;
    private readonly Func<Settings, string?, bool, Task> save;
    private readonly Grid root = new();
    private readonly TextBlock error = UI.Label("", 11);
    private readonly TextBlock pageTitle = UI.Label("Connection", 21, true);
    private readonly TextBlock pageSubtitle = UI.Label("Choose the model you use to translate and learn.", 12);
    private readonly ContentControl pageContent = new() { HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch };
    private readonly Dictionary<string, FieldPicker> pickers = new();
    private readonly Dictionary<string, CheckBox> buttonChecks = new();
    private readonly Dictionary<string, StackPanel> pages = new();
    private readonly Dictionary<string, Button> navigation = new();
    private readonly TextBox endpoint, model, extra, translate, rewrite;
    private readonly PasswordBox key = new() { PlaceholderText = "Leave blank to keep the saved key" };
    private readonly CheckBox clearKey = new() { Content = "Remove saved key" };
    private readonly NumberBox timeout, delay, font, opacity;
    private readonly ComboBox modifier, theme, layout, preset, dictionary;
    private readonly ToggleSwitch startup = new() { OnContent = "On", OffContent = "Off", MinWidth = 90 };
    private readonly bool dark;
    private readonly Button saveButton;
    public bool Alive { get; private set; } = true;

    public SettingsWindow(Settings settings, Func<Settings, string?, bool, Task> save) {
        original = SettingsStore.Clone(settings); this.save = save; dark = settings.Theme == "Dark"; Title = "Screen English · Settings";
        root.Background = UI.Surface(dark); root.RequestedTheme = dark ? ElementTheme.Dark : ElementTheme.Light;
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(42) }); root.RowDefinitions.Add(new RowDefinition()); root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var titlebar = new Grid { Padding = new Thickness(17, 0, 8, 0) }; titlebar.ColumnDefinitions.Add(new ColumnDefinition()); titlebar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var title = UI.Label("Screen English", 12, true); title.VerticalAlignment = VerticalAlignment.Center; title.Foreground = UI.Muted(dark); titlebar.Children.Add(title); UI.Draggable(title, this);
        var close = UI.Icon("\uE8BB", "Close settings · Esc", Close); Grid.SetColumn(close, 1); titlebar.Children.Add(close); root.Children.Add(titlebar);
        var middle = new Grid(); middle.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(156) }); middle.ColumnDefinitions.Add(new ColumnDefinition()); Grid.SetRow(middle, 1); root.Children.Add(middle);
        var rail = new Grid { Background = dark ? UI.Brush(25, 25, 25) : UI.Brush(241, 241, 239) }; rail.RowDefinitions.Add(new RowDefinition()); rail.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var nav = new StackPanel { Spacing = 3, Padding = new Thickness(10, 17, 10, 12) }; rail.Children.Add(nav);
        var railCaption = UI.Label("Settings", 11, true); railCaption.Foreground = UI.Muted(dark); railCaption.Margin = new Thickness(10, 0, 0, 12); nav.Children.Add(railCaption);
        foreach (var (name, glyph) in new[] { ("Connection", "\uE774"), ("Shortcuts", "\uE765"), ("Appearance", "\uE790"), ("Content", "\uE8A5"), ("Dictionary", "\uE736") }) {
            var button = UI.Button("", () => SelectPage(name)); button.HorizontalAlignment = HorizontalAlignment.Stretch; button.HorizontalContentAlignment = HorizontalAlignment.Left; button.Padding = new Thickness(11, 9, 11, 9);
            var label = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 9 }; label.Children.Add(new FontIcon { Glyph = glyph, FontSize = 13 }); label.Children.Add(UI.Label(name, 12)); button.Content = label;
            navigation[name] = button; nav.Children.Add(button);
        }
        var local = UI.Label("Local OCR\nText-only AI requests", 10); local.Foreground = UI.Muted(dark); local.Margin = new Thickness(20, 12, 15, 20); Grid.SetRow(local, 1); rail.Children.Add(local); middle.Children.Add(rail);
        var detail = new Grid { Padding = new Thickness(26, 18, 26, 18) }; detail.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); detail.RowDefinitions.Add(new RowDefinition());
        var intro = new StackPanel { Spacing = 6, Margin = new Thickness(0, 0, 0, 24) }; intro.Children.Add(pageTitle); pageSubtitle.Foreground = UI.Muted(dark); intro.Children.Add(pageSubtitle); detail.Children.Add(intro);
        Grid.SetRow(pageContent, 1); detail.Children.Add(pageContent); Grid.SetColumn(detail, 1); middle.Children.Add(detail);
        var service = Page("Connection");
        endpoint = Text(service, "Base URL", settings.BaseUrl); endpoint.PlaceholderText = "https://api.deepseek.com";
        model = Text(service, "Model", settings.Model); model.PlaceholderText = "deepseek-v4-flash";
        Field(service, "API key", key); clearKey.Foreground = UI.Muted(dark); service.Children.Add(clearKey);
        var security = UI.Label("Stored securely in Windows Credential Manager.", 11); security.Foreground = UI.Muted(dark); security.Margin = new Thickness(0, -7, 0, 4); service.Children.Add(security);
        var advanced = new StackPanel { Spacing = 14, Padding = new Thickness(0, 12, 0, 0) };
        timeout = Number(advanced, "Request timeout · seconds", settings.Timeout, 5, 300);
        extra = Text(advanced, "Additional parameters · JSON", settings.Extra.ToJsonString()); extra.AcceptsReturn = true; extra.MinHeight = 72;
        var expander = new Expander { Header = "Advanced", Content = advanced, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch, FontSize = 12, Padding = new Thickness(0) }; service.Children.Add(expander);
        var behavior = Page("Shortcuts");
        translate = Text(behavior, "Capture and translate", settings.TranslateShortcut); rewrite = Text(behavior, "Capture and rewrite", settings.RewriteShortcut);
        modifier = Choice(behavior, "Hold to look up", ["Ctrl", "Alt", "Shift"], settings.HoverModifier); delay = Number(behavior, "Hover delay · milliseconds", settings.HoverDelay, 200, 2000);
        startup.IsOn = Native.StartupEnabled; Field(behavior, "Start with Windows", startup);
        var escapeHint = UI.Label("Esc closes the learning panel. During capture, it cancels the selection.", 11); escapeHint.Foreground = UI.Muted(dark); behavior.Children.Add(escapeHint);
        var appearance = Page("Appearance");
        preset = Choice(appearance, "Preset", ["Quick", "Learning", "Custom"], settings.Preset); theme = Choice(appearance, "Theme", ["Light", "Dark"], settings.Theme);
        layout = Choice(appearance, "Layout", ["Compact", "Vertical", "Side by side", "Tabs"], settings.Layout); font = Number(appearance, "Text size", settings.FontSize, 10, 26); opacity = Number(appearance, "Opacity · %", settings.Opacity, 50, 100);
        var toolbar = new StackPanel { Spacing = 3 }; Field(appearance, "Toolbar", toolbar);
        foreach (string button in new[] { "Copy", "Rewrite", "Translate", "Explain" }) { var check = new CheckBox { Content = button, IsChecked = settings.Buttons.Contains(button) }; buttonChecks[button] = check; toolbar.Children.Add(check); }
        var content = Page("Content");
        foreach (var entry in Fields.All) { var picker = new FieldPicker(entry.Key, settings.Visible[entry.Key], dark); pickers[entry.Key] = picker; Field(content, entry.Key switch { "translate" => "Translation", "rewrite" => "Rewrite", _ => "Words and phrases" }, picker); }
        var dict = Page("Dictionary"); dictionary = Choice(dict, "Source", ["Automatic", "Local WordNet", "AI", "Cambridge (browser)"], settings.Dictionary);
        foreach (var (label, description) in new[] { ("Automatic", "Local English definitions first, then AI meaning and grammar in context."), ("Local WordNet", "English definitions, parts of speech, and examples. Works offline."), ("AI", "Chinese meanings and explanations tailored to the selected sentence."), ("Cambridge", "Opens the dictionary website. Definitions are never scraped.") }) {
            var text = new StackPanel { Spacing = 4 }; text.Children.Add(UI.Label(label, 12, true)); var hint = UI.Label(description, 12); hint.Foreground = UI.Muted(dark); text.Children.Add(hint); dict.Children.Add(text);
        }
        preset.SelectionChanged += (_, _) => ApplyPreset();
        var footer = new Grid { Padding = new Thickness(18, 12, 18, 12), ColumnSpacing = 12 }; footer.ColumnDefinitions.Add(new ColumnDefinition()); footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        error.Foreground = dark ? UI.Brush(233, 169, 159) : UI.Brush(150, 65, 55); error.VerticalAlignment = VerticalAlignment.Center; footer.Children.Add(error);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 7 }; buttons.Children.Add(UI.Button("Cancel", Close)); saveButton = UI.Button("Save changes", async () => await Save(), true); buttons.Children.Add(saveButton); Grid.SetColumn(buttons, 1); footer.Children.Add(buttons);
        var footerBorder = new Border { Child = footer, BorderBrush = UI.Line(dark), BorderThickness = new Thickness(0, 1, 0, 0) }; Grid.SetRow(footerBorder, 2); root.Children.Add(footerBorder);
        Content = root; Native.Borderless(this, false); UI.Escape(root, Close);
        var work = Native.Monitor(Native.Cursor, true); double scale = Native.Scale(Native.Cursor); int width = Math.Min(work.Width, (int)(730 * scale)), height = Math.Min(work.Height, (int)(590 * scale));
        AppWindow.MoveAndResize(new Windows.Graphics.RectInt32(work.Left + (work.Width - width) / 2, work.Top + (work.Height - height) / 2, width, height));
        Closed += (_, _) => Alive = false; SelectPage("Connection");
    }
    private StackPanel Page(string name) { var panel = new StackPanel { Spacing = 17 }; pages[name] = panel; return panel; }
    internal void SelectPage(string name) {
        pageTitle.Text = name; pageSubtitle.Text = name switch { "Connection" => "Choose the model you use to translate and learn.", "Shortcuts" => "Capture a thought without leaving what you’re reading.", "Appearance" => "Make a little room for the way you learn.", "Content" => "Choose what appears, and in what order.", _ => "Find the meaning that fits the moment." };
        foreach (var pair in navigation) pair.Value.Background = pair.Key == name ? UI.Card(dark) : new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        if (pageContent.Content is ScrollViewer previous) previous.Content = null;
        pageContent.Content = new ScrollViewer { Content = pages[name], HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalContentAlignment = HorizontalAlignment.Stretch };
    }
    private static void Field(StackPanel parent, string label, FrameworkElement control) { var stack = new StackPanel { Spacing = 7 }; stack.Children.Add(UI.Label(label, 12, true)); stack.Children.Add(control); parent.Children.Add(stack); }
    private static TextBox Text(StackPanel parent, string header, string value) { var box = new TextBox { Text = value }; Field(parent, header, box); return box; }
    private static NumberBox Number(StackPanel parent, string header, int value, int min, int max) { var box = new NumberBox { Value = value, Minimum = min, Maximum = max, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact, Width = 170, HorizontalAlignment = HorizontalAlignment.Left }; Field(parent, header, box); return box; }
    private static ComboBox Choice(StackPanel parent, string header, string[] choices, string value) { var box = new ComboBox { ItemsSource = choices, SelectedItem = value, HorizontalAlignment = HorizontalAlignment.Stretch }; Field(parent, header, box); return box; }
    private void ApplyPreset() {
        string value = preset.SelectedItem?.ToString() ?? "Custom"; if (value == "Custom") return; layout.SelectedItem = value == "Quick" ? "Compact" : "Vertical";
        foreach (var pair in pickers) pair.Value.Reset(value == "Quick" ? pair.Key switch { "translate" => new[] { "translation" }, "rewrite" => new[] { "natural_english", "key_changes" }, _ => new[] { "chinese_meaning", "simple_english_meaning" } } : Fields.All[pair.Key]);
    }
    private async Task Save() {
        saveButton.IsEnabled = false;
        try {
            var settings = SettingsStore.Clone(original); settings.BaseUrl = endpoint.Text; settings.Model = model.Text;
            settings.Extra = JsonNode.Parse(extra.Text) as JsonObject ?? throw new Exception("Additional parameters must be a JSON object.");
            settings.Timeout = checked((int)timeout.Value); settings.TranslateShortcut = translate.Text; settings.RewriteShortcut = rewrite.Text; settings.HoverModifier = modifier.SelectedItem.ToString()!; settings.HoverDelay = checked((int)delay.Value);
            settings.Theme = theme.SelectedItem.ToString()!; settings.Layout = layout.SelectedItem.ToString()!; settings.Preset = preset.SelectedItem.ToString()!;
            settings.FontSize = checked((int)font.Value); settings.Opacity = checked((int)opacity.Value); settings.Dictionary = dictionary.SelectedItem.ToString()!;
            settings.Visible = pickers.ToDictionary(p => p.Key, p => p.Value.Selected()); settings.Buttons = buttonChecks.Where(p => p.Value.IsChecked == true).Select(p => p.Key).ToList(); SettingsStore.Validate(settings);
            string? newKey = clearKey.IsChecked == true ? "" : string.IsNullOrWhiteSpace(key.Password) ? null : key.Password.Trim(); await save(settings, newKey, startup.IsOn); Close();
        } catch (System.Text.Json.JsonException) { error.Text = "Additional parameters must be valid JSON."; }
        catch (Exception exception) { error.Text = exception.Message; }
        finally { saveButton.IsEnabled = true; }
    }
}

public sealed class FieldPicker : StackPanel
{
    private readonly string action;
    private readonly List<CheckBox> fields = [];
    private readonly StackPanel rows = new() { Spacing = 0 };
    private readonly bool dark;
    public FieldPicker(string action, IEnumerable<string> selected, bool dark = false) {
        this.action = action; this.dark = dark; Spacing = 5;
        Children.Add(rows); var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 };
        buttons.Children.Add(UI.Button("All", () => { foreach (var check in fields) check.IsChecked = true; })); buttons.Children.Add(UI.Button("None", () => { foreach (var check in fields) check.IsChecked = false; })); buttons.Children.Add(UI.Button("Reset", () => Reset(Fields.All[action])));
        Children.Add(buttons); Reset(selected);
    }
    private void Move(CheckBox field, int delta) { int index = fields.IndexOf(field); if (index + delta < 0 || index + delta >= fields.Count) return; fields.RemoveAt(index); fields.Insert(index + delta, field); RenderRows(); }
    private void RenderRows() {
        foreach (var row in rows.Children.OfType<Grid>()) row.Children.Clear(); rows.Children.Clear();
        foreach (var field in fields) {
            var row = new Grid { Height = 34 }; row.ColumnDefinitions.Add(new ColumnDefinition()); row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            field.VerticalAlignment = VerticalAlignment.Center; row.Children.Add(field);
            var up = UI.Icon("\uE70E", "Move " + field.Content + " up", () => Move(field, -1)); up.Opacity = .55; up.IsEnabled = fields.IndexOf(field) > 0; Grid.SetColumn(up, 1); row.Children.Add(up);
            var down = UI.Icon("\uE70D", "Move " + field.Content + " down", () => Move(field, 1)); down.Opacity = .55; down.IsEnabled = fields.IndexOf(field) < fields.Count - 1; Grid.SetColumn(down, 2); row.Children.Add(down); rows.Children.Add(row);
        }
    }
    public void Reset(IEnumerable<string> selected) {
        var enabled = selected.ToList(); fields.Clear();
        foreach (string key in enabled.Concat(Fields.All[action].Except(enabled))) {
            var check = new CheckBox { Content = Fields.Labels[key], Tag = key, IsChecked = enabled.Contains(key), Foreground = UI.Ink(dark) };
            foreach (string state in new[] { "", "PointerOver", "Pressed" }) {
                check.Resources["CheckBoxCheckBackgroundFillChecked" + state] = UI.Ink(dark);
                check.Resources["CheckBoxCheckBackgroundStrokeChecked" + state] = UI.Ink(dark);
                check.Resources["CheckBoxCheckGlyphForegroundChecked" + state] = UI.Surface(dark);
                check.Resources["CheckBoxForegroundChecked" + state] = UI.Ink(dark);
                check.Resources["CheckBoxForegroundUnchecked" + state] = UI.Ink(dark);
            }
            fields.Add(check);
        }
        RenderRows();
    }
    public List<string> Selected() => fields.Where(c => c.IsChecked == true).Select(c => c.Tag.ToString()!).ToList();
}

