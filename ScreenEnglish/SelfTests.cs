using System.Drawing;
using System.Drawing.Imaging;
using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Streams;

namespace ScreenEnglish;

// Integration checks run inside the real WinUI runtime: no test framework or live API account required.
public static class SelfTests
{
    public static readonly JsonObject Translation = JsonNode.Parse("""
        {"schema_version":"1.0","action":"translate","source_text":"I have been working here for three years.","translation":"我已经在这里工作三年了。","source_language":"en","target_language":"zh-CN"}
        """)!.AsObject();
    public static readonly JsonObject Rewrite = JsonNode.Parse("""
        {"schema_version":"1.0","action":"rewrite","original_english":"I have work here since three years.","natural_english":"I have been working here for three years.","chinese_explanation":"用现在完成进行时表达从过去持续到现在的动作。","simple_english_explanation":"Use have been working for an action that started in the past and continues now.","key_changes":["Changed have work to have been working.","Changed since three years to for three years."]}
        """)!.AsObject();
    private sealed class Handler(Func<int, HttpRequestMessage, HttpResponseMessage> reply) : HttpMessageHandler {
        public int Calls; public List<JsonObject> Bodies { get; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) {
            Bodies.Add(JsonNode.Parse(await request.Content!.ReadAsStringAsync(cancellationToken))!.AsObject());
            return reply(++Calls, request);
        }
    }
    private static HttpResponseMessage Envelope(string content) => new(HttpStatusCode.OK) { Content = new StringContent(new JsonObject { ["choices"] = new JsonArray(new JsonObject { ["message"] = new JsonObject { ["content"] = content } }) }.ToJsonString()) };
    public static async Task Run() {
        var keepAlive = new Window();
        string output = Path.Combine(Environment.CurrentDirectory, "test-output"); Directory.CreateDirectory(output);
        var checks = new List<object>();
        async Task Check(string name, Func<Task> action) {
            try { await action(); checks.Add(new { name, passed = true }); }
            catch (Exception error) { checks.Add(new { name, passed = false, error = error.ToString() }); }
        }
        void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }
        await Check("Strict contracts reject wrong actions, types and unknown fields", () => {
            AiClient.Validate(Translation.ToJsonString(), "translate", "source", "");
            foreach (string invalid in new[] { "{}", Translation.ToJsonString().Replace("translate", "rewrite"), Translation.ToJsonString().Replace("\"translation\":", "\"unexpected\":"), "{\"schema_version\":1}" }) {
                bool rejected = false; try { AiClient.Validate(invalid, "translate", "source", ""); } catch { rejected = true; }
                Assert(rejected, "Invalid contract accepted.");
            }
            Assert(AiClient.Validate(Translation.ToJsonString(), "translate", "real source", "")["source_text"]!.ToString() == "real source", "Source was not preserved.");
            return Task.CompletedTask;
        });
        await Check("Invalid AI JSON gets exactly one correction retry; request contains text only", async () => {
            var handler = new Handler((attempt, _) => Envelope(attempt == 1 ? "not json" : Translation.ToJsonString())); using var client = new AiClient(handler);
            var settings = new Settings { Model = "test-model" }; await client.Request(settings, "", "translate", "Hello world.");
            Assert(handler.Calls == 2, "Wrong retry count.");
            Assert(handler.Bodies[1]["messages"]!.AsArray().Count == 3, "Correction instruction missing.");
            Assert(!handler.Bodies[0].ContainsKey("response_format"), "API-level response format must not be sent.");
            Assert(handler.Bodies[0]["messages"]![0]!["content"]!.ToString().Contains(SchemaText("translate")), "The schema is missing from the prompt.");
            Assert(!handler.Bodies[0].ToJsonString().Contains("image_url"), "Image payload present.");
        });
        await Check("Two invalid outputs fail cleanly", async () => {
            var handler = new Handler((_, _) => Envelope("{}")); using var client = new AiClient(handler);
            bool failed = false; try { await client.Request(new Settings { Model = "test" }, "", "rewrite", "Hello."); } catch (Exception error) { failed = error.Message.Contains("twice"); }
            Assert(failed && handler.Calls == 2, "Invalid results were not bounded.");
        });
        await Check("Auth failures are not retried", async () => {
            var handler = new Handler((_, _) => new HttpResponseMessage(HttpStatusCode.Unauthorized)); using var client = new AiClient(handler);
            try { await client.Request(new Settings { Model = "test" }, "", "translate", "Hello."); } catch { }
            Assert(handler.Calls == 1, "Auth failure retried.");
        });
        await Check("Legacy format preferences cannot send response_format; DeepSeek model is preserved", async () => {
            foreach (string mode in new[] { "schema", "json", "prompt" }) {
                var handler = new Handler((_, _) => Envelope(Translation.ToJsonString())); using var client = new AiClient(handler);
                var legacy = JsonSerializer.SerializeToNode(new Settings { BaseUrl = "https://api.deepseek.com", Model = "deepseek-v4-flash" }, SettingsStore.Json)!.AsObject();
                legacy["output_mode"] = mode; legacy.Remove("appearance_version"); legacy["opacity"] = 98;
                var migrated = SettingsStore.Parse(legacy.ToJsonString()); Assert(migrated.Opacity == 100, "Old translucent appearance was not migrated.");
                await client.Request(migrated, "", "translate", "Hello.");
                Assert(!handler.Bodies[0].ContainsKey("response_format"), "Legacy switch leaked into API payload.");
                Assert(handler.Bodies[0]["model"]!.ToString() == "deepseek-v4-flash", "The user's model was changed.");
            }
        });
        await Check("Disabled lookup fields are omitted from requests and validation", async () => {
            var settings = new Settings { Model = "test" };
            settings.Visible["hover_lookup"] = ["chinese_meaning"];
            var result = new JsonObject { ["schema_version"] = "1.0", ["action"] = "hover_lookup", ["term"] = "practice", ["chinese_meaning"] = "练习" };
            var handler = new Handler((_, _) => Envelope(result.ToJsonString())); using var client = new AiClient(handler);
            await client.Request(settings, "", "hover_lookup", "Daily practice helps.", "practice");
            string prompt = handler.Bodies[0]["messages"]![0]!["content"]!.ToString();
            Assert(!prompt.Contains("example_sentence") && !prompt.Contains("grammar_role"), "Disabled fields were requested.");
            result["example_sentence"] = "Unexpected"; bool rejected = false;
            try { AiClient.Validate(result.ToJsonString(), "hover_lookup", "", "practice", settings); } catch (FormatException) { rejected = true; }
            Assert(rejected, "Unrequested fields passed validation.");
        });
        await Check("Translation joins screen wrapping and retains paragraph/list boundaries", () => {
            Assert(AiClient.JoinWrappedLines("if local\nvocabulary has the meaning") == "if local vocabulary has the meaning", "Wrapped sentence was split.");
            Assert(AiClient.JoinWrappedLines("First paragraph.\n\nSecond paragraph.\n- First item\n- Second item") == "First paragraph.\n\nSecond paragraph.\n- First item\n- Second item", "Paragraph/list boundaries changed.");
            return Task.CompletedTask;
        });
        await Check("Settings reject duplicate hotkeys and reserved request overrides", () => {
            var settings = new Settings { RewriteShortcut = "ctrl+shift+t" }; bool rejected = false;
            try { SettingsStore.Validate(settings); } catch { rejected = true; } Assert(rejected, "Duplicate shortcut accepted.");
            settings = new Settings { Extra = new JsonObject { ["messages"] = new JsonArray() } }; rejected = false;
            try { SettingsStore.Validate(settings); } catch { rejected = true; } Assert(rejected, "Reserved request override accepted.");
            Assert(!JsonSerializer.Serialize(new Settings(), SettingsStore.Json).Contains("api_key"), "Key appeared in settings."); return Task.CompletedTask;
        });
        await Check("Word hits and reverse phrase selection on a negative-coordinate monitor", () => {
            var capture = new ActiveCapture("Hello world", [new Word("Hello", [0, 0, 60, 24], "Hello world"), new Word("world", [70, 0, 140, 24], "Hello world")], new Native.RECT(-1900, 100, -1700, 200), []);
            Assert(capture.Hit(-1880, 110) == 0 && capture.Hit(-1800, 110) == 1 && capture.Hit(10, 10) == -1, "Coordinate hit failure.");
            Assert(capture.Phrase(1, 0).Term == "Hello world", "Reverse selection changed reading order."); return Task.CompletedTask;
        });
        byte[] fixture;
        using (var bitmap = new Bitmap(1000, 150)) {
            using (var graphics = Graphics.FromImage(bitmap)) { graphics.Clear(Color.White); using var font = new Font("Arial", 30, FontStyle.Regular, GraphicsUnit.Pixel); graphics.DrawString("Learning English takes practice every day.", font, Brushes.Black, 20, 40); }
            using var stream = new MemoryStream(); bitmap.Save(stream, ImageFormat.Png); fixture = stream.ToArray(); File.WriteAllBytes(Path.Combine(output, "ocr-fixture.png"), fixture);
        }
        await Check("Real local OCR returns readable text and distinct word coordinates", async () => {
            using var ocr = new OcrService(); var result = await ocr.Read(fixture);
            Assert(result.Text == "Learning English takes practice every day.", "Fixture text not recognized: " + result.Text);
            Assert(result.Words.Count == 6 && result.Words.All(w => w.Box[2] > w.Box[0] && w.Box[3] > w.Box[1] && w.Box[2] <= 1000), "Word boxes missing or not mapped back to source pixels.");
            File.WriteAllText(Path.Combine(output, "ocr-result.json"), JsonSerializer.Serialize(new { result.Text, result.Words }, SettingsStore.Json));
        });
        await Check("Screen lookup closes on release and ignores pending OCR completion", async () => {
            bool held = true;
            var window = new ScreenLookupWindow(() => held);
            Task running = window.Start(fixture, new Native.RECT(100, 100, 1100, 340));
            await Task.Delay(120); held = false;
            await Task.Delay(100);
            Assert(!window.Alive, "Releasing the shortcut did not dismiss screen lookup.");
            await running;
            Assert(!window.Alive, "OCR completion reopened a dismissed overlay.");
        });
        await Check("WordNet returns real local definitions", async () => { var result = await new LocalDictionary().Lookup("learn"); Assert(result?["simple_english_meaning"]?.ToString().Length > 10, "Dictionary entry missing."); });
        await Check("WinUI renders all eight theme/layout combinations and disables hover when minimized or closed", async () => {
            foreach (string theme in new[] { "Dark", "Light" }) foreach (string layout in new[] { "Compact", "Vertical", "Side by side", "Tabs" }) {
                var settings = new Settings { Theme = theme, Layout = layout };
                var window = new LearningWindow(settings); window.Render(Rewrite); window.ShowNear(new Native.RECT(100, 100, 100, 100));
                await Task.Delay(160); Assert(window.HoverEnabled, "Shown panel should permit hover.");
                await Render(window, Path.Combine(output, $"panel-{theme}-{layout.Replace(' ', '-')}.png"));
                Native.ShowWindow(Native.Hwnd(window), 6); await Task.Delay(80); Assert(!window.HoverEnabled, "Minimized panel permits hover.");
                window.Close(); Assert(!window.HoverEnabled, "Closed panel permits hover.");
            }
        });
        await Check("WinUI Settings and hover popup render", async () => {
            var settings = new Settings();
            foreach (string theme in new[] { "Light", "Dark" }) {
                var window = new SettingsWindow(new Settings { Theme = theme, BaseUrl = "https://api.deepseek.com", Model = "deepseek-v4-flash" }, (_, _, _) => Task.CompletedTask); window.Activate(); await Task.Delay(200);
                foreach (string page in new[] { "Connection", "Shortcuts", "Appearance", "Content", "Dictionary" }) {
                    window.SelectPage(page); await Task.Delay(100); await Render(window, Path.Combine(output, $"settings-{theme}-{page}.png"));
                }
                window.Close();
            }
            var hover = new HoverWindow(); hover.Render("working", new JsonObject { ["simple_english_meaning"] = "Doing a job or an activity.", ["part_of_speech"] = "verb", ["grammar_role"] = "Part of the present perfect continuous.", ["example_sentence"] = "I have been working here for three years." }, settings, "Test fixture");
            await Task.Delay(200); await Render(hover, Path.Combine(output, "hover.png")); hover.Close();
        });
        await Check("Clear capture selection renders and maps back to physical pixels", async () => {
            byte[] pageImage;
            using (var bitmap = new Bitmap(1200, 720)) {
                using var graphics = Graphics.FromImage(bitmap); graphics.Clear(Color.FromArgb(249, 249, 247));
                using var font = new Font("Segoe UI", 25, FontStyle.Regular, GraphicsUnit.Pixel);
                graphics.DrawString("Read a little. Learn something new.", font, Brushes.Black, 60, 125);
                graphics.DrawString("Learning English takes practice every day.", font, Brushes.DimGray, 60, 180);
                using var stream = new MemoryStream(); bitmap.Save(stream, ImageFormat.Png); pageImage = stream.ToArray();
            }
            var window = new CaptureWindow(); var selectionTask = window.Select(pageImage, new Native.RECT(80, 80, 1280, 800), "translate");
            await Task.Delay(250); var element = (FrameworkElement)window.Content;
            var from = new Windows.Foundation.Point(element.ActualWidth * .035, element.ActualHeight * .13);
            var to = new Windows.Foundation.Point(element.ActualWidth * .8, element.ActualHeight * .34);
            window.PreviewSelection(from, to); await Task.Delay(120); await Render(window, Path.Combine(output, "capture-selection.png")); window.CompleteSelection();
            var rectangle = await selectionTask; Assert(rectangle is not null && rectangle.Value.Width > 850 && rectangle.Value.Height > 140, "Incorrect physical selection dimensions.");
            var canceled = new CaptureWindow(); var canceledTask = canceled.Select(pageImage, new Native.RECT(80, 80, 1280, 800), "rewrite"); await Task.Delay(150); canceled.CancelSelection(); Assert(await canceledTask is null, "Canceled capture returned a region.");
        });
        await Check("Learning panel and lookup popup expose an Escape close shortcut", () => {
            var panel = new LearningWindow(new Settings()); var hover = new HoverWindow();
            try {
                Assert(Descendants(panel.Content).OfType<FrameworkElement>().Any(e => e.KeyboardAccelerators.Any(k => k.Key == Windows.System.VirtualKey.Escape)), "Panel Escape shortcut missing.");
                Assert(((FrameworkElement)hover.Content).KeyboardAccelerators.Any(k => k.Key == Windows.System.VirtualKey.Escape), "Popup Escape shortcut missing.");
            } finally { panel.Close(); hover.Close(); }
            return Task.CompletedTask;
        });
        await Check("Learning-panel word hit testing uses the rendered text positions", async () => {
            var window = new LearningWindow(new Settings());
            try {
                window.Render(Rewrite); window.ShowNear(new Native.RECT(100, 100, 100, 100)); await Task.Delay(180);
                var block = Descendants(window.Content).OfType<RichTextBlock>().First(b => ((Run)b.Blocks.Cast<Paragraph>().First().Inlines.First()).Text.Contains("been working"));
                var run = (Run)((Paragraph)block.Blocks[0]).Inlines[0];
                int index = run.Text.IndexOf("working", StringComparison.Ordinal) + 2;
                var pointer = run.ContentStart.GetPositionAtOffset(index, LogicalDirection.Forward);
                var rect = pointer.GetCharacterRect(LogicalDirection.Forward);
                string? term = LearningWindow.TermAt(block, run, run.Text, new Windows.Foundation.Point(rect.X + rect.Width / 2, rect.Y + rect.Height / 2));
                Assert(term == "working", "Rendered word hit returned: " + term);
            } finally { window.Close(); }
        });
        await Check("App starts tray-only and registers both global shortcuts", () => {
            using var controller = new Controller((App)Application.Current);
            Assert(controller.IsTrayOnly, "A normal app window appeared on startup.");
            Assert(controller.RegisteredShortcutCount == 3, "A default shortcut is occupied on this machine.");
            return Task.CompletedTask;
        });
        File.WriteAllText(Path.Combine(output, "results.json"), JsonSerializer.Serialize(checks, new JsonSerializerOptions { WriteIndented = true }));
        keepAlive.Close();
    }
    private static string SchemaText(string action) => AiClient.Schema(action).ToJsonString();
    public static async Task CheckProvider() {
        var keepAlive = new Window(); string output = Path.Combine(Environment.CurrentDirectory, "test-output"); Directory.CreateDirectory(output);
        var results = new List<object>();
        try {
            var settings = SettingsStore.ForRequest();
            string key = Credentials.Get(settings.BaseUrl); using var client = new AiClient(); settings.Timeout = 90;
            foreach (string action in new[] { "translate", "rewrite", "hover_lookup" }) {
                var watch = System.Diagnostics.Stopwatch.StartNew();
                try {
                    var result = await client.Request(settings, key, action, "Learning English takes practice every day.", action == "hover_lookup" ? "practice" : "");
                    results.Add(new { action, passed = true, model = settings.Model, seconds = watch.Elapsed.TotalSeconds, result });
                } catch (Exception error) { results.Add(new { action, passed = false, model = settings.Model, error = error.Message }); break; }
            }
        } catch (Exception error) { results.Add(new { passed = false, error = error.Message }); }
        File.WriteAllText(Path.Combine(output, "provider-check.json"), JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true })); keepAlive.Close();
    }
    private static async Task Render(Window window, string path) {
        var bitmap = new RenderTargetBitmap(); await bitmap.RenderAsync((FrameworkElement)window.Content);
        var pixels = await bitmap.GetPixelsAsync();
        var file = await StorageFile.GetFileFromPathAsync(Create(path));
        using var stream = await file.OpenAsync(FileAccessMode.ReadWrite);
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, (uint)bitmap.PixelWidth, (uint)bitmap.PixelHeight, 96, 96, pixels.ToArray()); await encoder.FlushAsync();
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject element) {
        yield return element;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(element); i++) foreach (var child in Descendants(VisualTreeHelper.GetChild(element, i))) yield return child;
    }
    private static string Create(string path) { File.WriteAllBytes(path, []); return path; }
}



