namespace ScreenEnglish;

internal static class RuntimeLog
{
    private static readonly object Gate = new();
    public static void Write(string message) {
        try {
            lock (Gate) {
                Directory.CreateDirectory(SettingsStore.DirectoryPath);
                string path = Path.Combine(SettingsStore.DirectoryPath, "runtime.log");
                if (File.Exists(path) && new FileInfo(path).Length > 512_000) File.Move(path, path + ".previous", true);
                File.AppendAllText(path, $"{DateTimeOffset.Now:O} pid={Environment.ProcessId} {message}{Environment.NewLine}");
            }
        } catch { /* Diagnostics must never interrupt capture or requests. */ }
    }
}
