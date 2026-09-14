using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace ScreenEnglish;

public static class SettingsStore
{
    public static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower, WriteIndented = true };
    public static string DirectoryPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ScreenEnglish");
    public static string FilePath => Path.Combine(DirectoryPath, "settings.json");
    public static Settings Load(out string? warning) {
        warning = null;
        if (!File.Exists(FilePath)) return new();
        try { return Parse(File.ReadAllText(FilePath)); }
        catch { warning = "Settings could not be read. Defaults are in use; the original file is preserved until you save."; return new(); }
    }
    internal static Settings Parse(string json) {
        var settings = JsonSerializer.Deserialize<Settings>(json, Json)!;
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("appearance_version", out var version) || version.GetInt32() < 2) {
            settings.Opacity = 100;
            settings.AppearanceVersion = 2;
        }
        // Legacy output_mode is intentionally ignored. Schemas now live only in the prompt.
        Validate(settings); return settings;
    }
    public static Settings Clone(Settings settings) => JsonSerializer.Deserialize<Settings>(JsonSerializer.Serialize(settings, Json), Json)!;
    public static Settings ForRequest() {
        var settings = Load(out var warning);
        if (warning is not null) throw new Exception(warning);
        return settings;
    }
    public static void Validate(Settings settings) {
        if (!Uri.TryCreate(settings.BaseUrl.Trim(), UriKind.Absolute, out var url) || url.Scheme is not ("https" or "http") || string.IsNullOrEmpty(url.Host) || url.UserInfo.Length > 0 || url.Query.Length > 0 || url.Fragment.Length > 0)
            throw new Exception("Enter an HTTP(S) base URL without credentials, query parameters, or a fragment.");
        settings.BaseUrl = settings.BaseUrl.Trim().TrimEnd('/');
        settings.Model = settings.Model.Trim();
        if (settings.Timeout is < 5 or > 300 || settings.HoverDelay is < 200 or > 2000 || settings.FontSize is < 10 or > 26 || settings.Opacity is < 50 or > 100) throw new Exception("A numeric preference is outside the allowed range.");
        if (!new[] { "Ctrl", "Alt", "Shift" }.Contains(settings.HoverModifier) || !new[] { "Dark", "Light" }.Contains(settings.Theme) || !new[] { "Compact", "Vertical", "Side by side", "Tabs" }.Contains(settings.Layout) || !new[] { "Quick", "Learning", "Custom" }.Contains(settings.Preset) || !new[] { "Automatic", "Local WordNet", "AI", "Cambridge (browser)" }.Contains(settings.Dictionary)) throw new Exception("An unsupported setting was selected.");
        var first = Native.ParseHotkey(settings.TranslateShortcut); var second = Native.ParseHotkey(settings.RewriteShortcut);
        if (first == second) throw new Exception("Translate and Rewrite must have different shortcuts.");
        var lookup = Native.ParseHotkey("Ctrl+Shift+E");
        if (first == lookup || second == lookup) throw new Exception("Ctrl+Shift+E is reserved for hold-to-look-up mode.");
        string[] reserved = ["model", "messages", "response_format", "stream", "n", "tools", "tool_choice", "api_key", "authorization"];
        if (settings.Extra is null || settings.Extra.Any(p => reserved.Contains(p.Key.ToLowerInvariant()))) throw new Exception("Additional parameters cannot override model, messages, response format, streaming, tools or credentials.");
        if (settings.Visible is null || settings.Visible.Count != Fields.All.Count) throw new Exception("Content preferences are incomplete.");
        foreach (var pair in Fields.All) {
            if (!settings.Visible.TryGetValue(pair.Key, out var fields) || fields is null || fields.Distinct().Count() != fields.Count || fields.Except(pair.Value).Any()) throw new Exception("Content fields are invalid.");
        }
        if (settings.Buttons is null || settings.Buttons.Except(new[] { "Copy", "Rewrite", "Translate", "Explain" }).Any()) throw new Exception("Panel button preferences are invalid.");
    }
    public static void Save(Settings settings) {
        Validate(settings); Directory.CreateDirectory(DirectoryPath);
        string temporary = FilePath + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(settings, Json)); File.Move(temporary, FilePath, true);
    }
}

public static class Credentials
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct CREDENTIAL {
        public uint Flags, Type; public string TargetName; public string? Comment; public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public uint BlobSize; public nint Blob; public uint Persist, AttributeCount; public nint Attributes; public string? TargetAlias, UserName;
    }
    [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool Read(string target, uint type, int flags, out nint credential);
    [DllImport("advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool Write(ref CREDENTIAL credential, uint flags);
    [DllImport("advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool Delete(string target, uint type, int flags);
    [DllImport("advapi32.dll")] private static extern void CredFree(nint pointer);
    private static string Target(string endpoint) => "ScreenEnglish:" + endpoint.TrimEnd('/');
    public static string Get(string endpoint) {
        if (!Read(Target(endpoint), 1, 0, out nint pointer)) {
            if (Marshal.GetLastWin32Error() == 1168) return "";
            throw new Exception("Windows Credential Manager could not read the API key.");
        }
        try {
            var credential = Marshal.PtrToStructure<CREDENTIAL>(pointer);
            byte[] bytes = new byte[credential.BlobSize]; Marshal.Copy(credential.Blob, bytes, 0, bytes.Length);
            try { return Encoding.UTF8.GetString(bytes); } finally { Array.Clear(bytes); }
        } finally { CredFree(pointer); }
    }
    public static void Set(string endpoint, string value) {
        if (value.Length == 0) {
            if (!Delete(Target(endpoint), 1, 0) && Marshal.GetLastWin32Error() != 1168) throw new Exception("Windows Credential Manager could not delete the API key.");
            return;
        }
        byte[] bytes = Encoding.UTF8.GetBytes(value);
        if (bytes.Length > 2560) throw new Exception("The API key is too long for Windows Credential Manager.");
        nint pointer = Marshal.AllocHGlobal(bytes.Length);
        try {
            Marshal.Copy(bytes, 0, pointer, bytes.Length);
            var credential = new CREDENTIAL { Type = 1, TargetName = Target(endpoint), Blob = pointer, BlobSize = (uint)bytes.Length, Persist = 2, UserName = "api_key" };
            if (!Write(ref credential, 0)) throw new Exception("Windows Credential Manager could not save the API key.");
        } finally { Array.Clear(bytes); Marshal.Copy(bytes, 0, pointer, bytes.Length); Marshal.FreeHGlobal(pointer); }
    }
}
