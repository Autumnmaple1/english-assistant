using System.Text;
using Tesseract;

namespace ScreenEnglish;

public sealed class OcrService : IDisposable
{
    // A placed character glyph in upscaled OCR pixels.
    public readonly record struct Glyph(char Character, int X1, int Y1, int X2, int Y2);

    private TesseractEngine? engine;
    private readonly object gate = new();

    // Apostrophes and hyphens stay inside a word (don't, well-known); every other mark ends it.
    private static bool Joiner(char character) => character is '\'' or '\u2018' or '\u2019' or '\u02BC' or '-' or '\u2010' or '\u2011' or '\u2013' or '\u2014';

    // Tesseract hands back whole tokens, so a token such as "day." or "yes/no" is cut at its punctuation
    // and every piece keeps its own glyph box. A joiner only joins when letters sit on both sides of it.
    internal static List<Word> Segment(IReadOnlyList<Glyph> glyphs, string line, double scale) {
        var words = new List<Word>();
        int start = -1, end = -1;
        void Flush() {
            if (start < 0) return;
            while (end > start && Joiner(glyphs[end].Character)) end--;
            var word = new StringBuilder(end - start + 1);
            double x1 = double.MaxValue, y1 = double.MaxValue, x2 = double.MinValue, y2 = double.MinValue;
            for (int i = start; i <= end; i++) {
                word.Append(glyphs[i].Character);
                x1 = Math.Min(x1, glyphs[i].X1); y1 = Math.Min(y1, glyphs[i].Y1);
                x2 = Math.Max(x2, glyphs[i].X2); y2 = Math.Max(y2, glyphs[i].Y2);
            }
            if (word.ToString().Any(char.IsLetter)) words.Add(new Word(word.ToString(), [x1 / scale, y1 / scale, x2 / scale, y2 / scale], line));
            start = end = -1;
        }
        for (int i = 0; i < glyphs.Count; i++) {
            char character = glyphs[i].Character;
            bool inside = char.IsLetter(character) || (start >= 0 && Joiner(character) && i + 1 < glyphs.Count && char.IsLetter(glyphs[i + 1].Character));
            if (inside) { if (start < 0) start = i; end = i; continue; }
            Flush();
        }
        Flush();
        return words;
    }

    public Task<(string Text, List<Word> Words)> Read(byte[] image) => Task.Run(() => {
        lock (gate) {
            string path = Path.Combine(AppContext.BaseDirectory, "data", "tessdata");
            if (!File.Exists(Path.Combine(path, "eng.traineddata"))) throw new Exception("The local OCR model is missing. Run setup.ps1, then rebuild.");
            try {
                engine ??= new TesseractEngine(path, "eng", EngineMode.LstmOnly);
                using var original = Pix.LoadFromMemory(image);
                float scale = original.Width <= 1600 && original.Height <= 1000 ? 2 : 1;
                using var pix = original.Scale(scale, scale);
                using var page = engine.Process(pix, PageSegMode.Auto);
                using var iterator = page.GetIterator();
                var words = new List<Word>();
                var glyphs = new List<Glyph>();
                string context = "";
                Rect line = default; bool onLine = false;
                Rect word = default; bool onWord = false;
                // Tesseract's symbol pass emits no spaces, so the containing word's box marks the word boundary.
                void FlushWord() { if (glyphs.Count > 0) words.AddRange(Segment(glyphs, context, scale)); glyphs.Clear(); }
                iterator.Begin();
                do {
                    string symbol = iterator.GetText(PageIteratorLevel.Symbol) ?? "";
                    // The line box, not its text, identifies the line: two lines can read the same.
                    if (iterator.TryGetBoundingBox(PageIteratorLevel.TextLine, out var currentLine) && (!onLine || currentLine.X1 != line.X1 || currentLine.Y1 != line.Y1 || currentLine.X2 != line.X2 || currentLine.Y2 != line.Y2)) {
                        FlushWord();
                        context = iterator.GetText(PageIteratorLevel.TextLine)?.Trim() ?? "";
                        line = currentLine; onLine = true; onWord = false;
                    }
                    bool boxed = iterator.TryGetBoundingBox(PageIteratorLevel.Symbol, out var box);
                    if (iterator.TryGetBoundingBox(PageIteratorLevel.Word, out var currentWord)) {
                        if (onWord && (currentWord.X1 != word.X1 || currentWord.Y1 != word.Y1 || currentWord.X2 != word.X2 || currentWord.Y2 != word.Y2)) FlushWord();
                        word = currentWord; onWord = true;
                        if (!boxed) { box = currentWord; boxed = true; }
                    }
                    if (symbol.Length > 0) {
                        foreach (char character in symbol) glyphs.Add(boxed && !char.IsWhiteSpace(character) ? new Glyph(character, box.X1, box.Y1, box.X2, box.Y2) : new Glyph(' ', 0, 0, 0, 0));
                    }
                } while (iterator.Next(PageIteratorLevel.Symbol));
                FlushWord();
                string text = page.GetText().Trim();
                if (text.Length == 0) throw new Exception("No readable English was found. Select a clearer or larger text region.");
                return (text, words);
            } catch (TesseractException) { throw new Exception("Local OCR could not start. Run setup.ps1 and check that the Microsoft Visual C++ 2015–2022 x64 runtime is installed."); }
        }
    });
    public void Dispose() { lock (gate) { engine?.Dispose(); engine = null; } }
}
