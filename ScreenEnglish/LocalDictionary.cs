using System.IO.Compression;
using System.Text.Json.Nodes;

namespace ScreenEnglish;

public sealed class LocalDictionary
{
    private readonly Lazy<Dictionary<string, List<(string Pos, string Gloss)>>> entries = new(Load);
    private static Dictionary<string, List<(string Pos, string Gloss)>> Load() {
        string path = Path.Combine(AppContext.BaseDirectory, "data", "wordnet.zip");
        if (!File.Exists(path)) throw new Exception("The local dictionary is missing. Run setup.ps1 and rebuild.");
        var dictionary = new Dictionary<string, List<(string, string)>>(StringComparer.OrdinalIgnoreCase);
        using var zip = ZipFile.OpenRead(path);
        foreach (string part in new[] { "noun", "verb", "adj", "adv" }) {
            var entry = zip.GetEntry("wordnet/data." + part) ?? throw new Exception("The WordNet data is incomplete.");
            using var reader = new StreamReader(entry.Open());
            while (reader.ReadLine() is string line) {
                if (line.Length == 0 || !char.IsAsciiDigit(line[0])) continue;
                int divider = line.IndexOf('|'); if (divider < 0) continue;
                var columns = line[..divider].Split(' ', StringSplitOptions.RemoveEmptyEntries);
                int count = Convert.ToInt32(columns[3], 16);
                string pos = part switch { "adj" => "adjective", "adv" => "adverb", _ => part };
                string gloss = line[(divider + 1)..].Trim();
                for (int i = 0; i < count; i++) {
                    string word = columns[4 + i * 2].Replace('_', ' ');
                    if (!dictionary.TryGetValue(word, out var senses)) dictionary[word] = senses = [];
                    if (senses.Count < 3) senses.Add((pos, gloss));
                }
            }
        }
        return dictionary;
    }
    public Task<JsonObject?> Lookup(string term) => Task.Run(() => {
        string lookup = term.Trim().Trim('.', ',', ';', ':', '!', '?', '"', '\'').Replace('_', ' ');
        if (!entries.Value.TryGetValue(lookup, out var senses)) return null;
        var result = new JsonObject { ["schema_version"] = "1.0", ["action"] = "hover_lookup", ["term"] = term };
        foreach (string field in Fields.All["hover_lookup"]) result[field] = null;
        result["simple_english_meaning"] = string.Join("\n", senses.Select(sense => $"{Tag(sense.Pos)} {sense.Gloss.Split(';')[0]}"));
        result["part_of_speech"] = string.Join(", ", senses.Select(s => s.Pos).Distinct());
        var example = senses.Select(s => s.Gloss).FirstOrDefault(s => s.Contains('"'));
        if (example is not null) result["example_sentence"] = example.Split('"')[1];
        return result;
    });
    private static string Tag(string part) => part switch { "noun" => "n.", "verb" => "v.", "adjective" => "adj.", "adverb" => "adv.", _ => part + "." };
}
