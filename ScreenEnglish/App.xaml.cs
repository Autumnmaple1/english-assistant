using Microsoft.UI.Xaml;

namespace ScreenEnglish;

public partial class App : Application
{
    private Controller? controller;
    private Mutex? instance;
    public App() { InitializeComponent(); }
    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        if (Environment.GetCommandLineArgs().Contains("--check-settings")) {
            var loaded = SettingsStore.Load(out var warning);
            var cloned = SettingsStore.Clone(loaded);
            File.WriteAllText(Path.Combine(Environment.CurrentDirectory, "settings-check.json"), System.Text.Json.JsonSerializer.Serialize(new { path = SettingsStore.FilePath, warning, loaded.Model, clonedModel = cloned.Model, hasSavedKey = !string.IsNullOrWhiteSpace(Credentials.Get(loaded.BaseUrl)) }));
            Exit(); return;
        }
        if (Environment.GetCommandLineArgs().Contains("--self-test")) { await SelfTests.Run(); Exit(); return; }
        if (Environment.GetCommandLineArgs().Contains("--check-provider")) { await SelfTests.CheckProvider(); Exit(); return; }
        instance = new Mutex(true, "Local\\ScreenEnglish.WinUI3", out bool first);
        if (!first) { Exit(); return; }
        try { controller = new Controller(this); }
        catch (Exception error) { Native.MessageBox(0, error.Message, "Screen English could not start", 0x10); Exit(); }
    }
}
