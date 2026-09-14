using System.Text.Json.Nodes;

namespace ScreenEnglish;

public sealed class Settings
{
    public string BaseUrl { get; set; } = "https://api.openai.com/v1";
    public string Model { get; set; } = "";
    public int Timeout { get; set; } = 45;
    public int AppearanceVersion { get; set; } = 2;
    public JsonObject Extra { get; set; } = new();
    public string TranslateShortcut { get; set; } = "Ctrl+Shift+T";
    public string RewriteShortcut { get; set; } = "Ctrl+Shift+R";
    public string HoverModifier { get; set; } = "Ctrl";
    public int HoverDelay { get; set; } = 500;
    public string Theme { get; set; } = "Dark";
    public string Layout { get; set; } = "Vertical";
    public string Preset { get; set; } = "Learning";
    public int FontSize { get; set; } = 13;
    public int Opacity { get; set; } = 100;
    public string Dictionary { get; set; } = "Automatic";
    public Dictionary<string, List<string>> Visible { get; set; } = Fields.All.ToDictionary(p => p.Key, p => p.Value.ToList());
    public List<string> Buttons { get; set; } = ["Copy", "Rewrite", "Translate", "Explain"];
}

public static class Fields
{
    public static readonly Dictionary<string, string[]> All = new() {
        ["translate"] = ["source_text", "translation", "source_language", "target_language"],
        ["rewrite"] = ["original_english", "natural_english", "chinese_explanation", "simple_english_explanation", "key_changes"],
        ["hover_lookup"] = ["chinese_meaning", "simple_english_meaning", "part_of_speech", "grammar_role", "example_sentence", "context_explanation"]
    };
    public static readonly Dictionary<string, string> Labels = new() {
        ["source_text"] = "Original English", ["translation"] = "Chinese translation", ["source_language"] = "Source language", ["target_language"] = "Target language",
        ["original_english"] = "Original English", ["natural_english"] = "Improved English",
        ["chinese_explanation"] = "Chinese explanation", ["simple_english_explanation"] = "Simple English explanation", ["key_changes"] = "Key changes",
        ["chinese_meaning"] = "Chinese meaning", ["simple_english_meaning"] = "English meaning", ["part_of_speech"] = "Part of speech",
        ["grammar_role"] = "Grammar role", ["example_sentence"] = "Example", ["context_explanation"] = "In this context"
    };
    public static string Text(JsonNode? value) => value is JsonArray array ? string.Join("\n", array.Select(v => "• " + v?.ToString())) : value?.ToString() ?? "";
}

public sealed record Word(string Text, double[] Box, string Context);
public sealed record ActiveCapture(string Text, List<Word> Words, Native.RECT Bounds, byte[] Image)
{
    public int Hit(int x, int y) => Words.FindIndex(word => x - Bounds.Left >= word.Box[0] && y - Bounds.Top >= word.Box[1] && x - Bounds.Left <= word.Box[2] && y - Bounds.Top <= word.Box[3]);
    public (string Term, string Context) Phrase(int first, int last) {
        var words = Words.Skip(Math.Min(first, last)).Take(Math.Abs(last - first) + 1).ToList();
        return (string.Join(" ", words.Select(w => w.Text)), string.Join("\n", words.Select(w => w.Context).Distinct()));
    }
}
