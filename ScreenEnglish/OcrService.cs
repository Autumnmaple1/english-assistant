using Tesseract;

namespace ScreenEnglish;

public sealed class OcrService : IDisposable
{
    private TesseractEngine? engine;
    private readonly object gate = new();
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
                iterator.Begin();
                do {
                    string word = iterator.GetText(PageIteratorLevel.Word)?.Trim() ?? "";
                    string context = iterator.GetText(PageIteratorLevel.TextLine)?.Trim() ?? word;
                    if (word.Length > 0 && iterator.TryGetBoundingBox(PageIteratorLevel.Word, out var box)) words.Add(new Word(word, [box.X1 / scale, box.Y1 / scale, box.X2 / scale, box.Y2 / scale], context));
                } while (iterator.Next(PageIteratorLevel.Word));
                string text = page.GetText().Trim();
                if (text.Length == 0 || words.Count == 0) throw new Exception("No readable English was found. Select a clearer or larger text region.");
                return (text, words);
            } catch (TesseractException) { throw new Exception("Local OCR could not start. Run setup.ps1 and check that the Microsoft Visual C++ 2015–2022 x64 runtime is installed."); }
        }
    });
    public void Dispose() { lock (gate) { engine?.Dispose(); engine = null; } }
}
