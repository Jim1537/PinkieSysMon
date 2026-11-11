using System.Text.Json;

namespace PinkieSysMon.RegressionTests;

internal static class Issue105ResourceLoggingTests
{
    public static void ResourceContextWithoutInnerIo()
    {
        Sandbox((root, log) =>
        {
            File.WriteAllText(Path.Combine(root, "invalid.png"), "invalid image fixture");
            using var images = new ImageAssetCache(root);
            Exception? original = null;
            try { images.Get("invalid.png"); }
            catch (Exception error) { original = error; }
            Check(original is not null && !File.Exists(log.Path) && !File.Exists(log.ErrorsPath),
                "Deep resource context must not do diagnostic I/O or replace a real load failure.");
            for (var i = 0; i < 20; i++) RenderFailureContext.Add(original!, "phase-" + i, new string('s', 200) + i);
            var context = RenderFailureContext.Find(original);
            Check(context is { Phases.Length: 8, LogicalSubjects.Length: 1, Omitted: > 0 } && context.LogicalSubjects[0].Length < 150,
                "Attached context is bounded before persistence.");
            log.Error("Resource operation failed.", original);
            var record = Records(log.ErrorsPath).Single();
            Check(record.GetProperty("renderContext").GetProperty("Phases").EnumerateArray()
                .Any(e => e.GetString() == "image.load") && record.GetProperty("exceptionType").GetString() == original!.GetType().FullName,
                "The outer error record retains the actual failure plus resource phase.");
            Check(!Text(log.Path).Contains("image.load") && !Text(log.Path).Contains("invalid.png"), "Session keeps only the error reference.");
        });
    }

    private static JsonElement[] Records(string path) => Text(path).Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
        .Select(line => { using var j = JsonDocument.Parse(line[line.IndexOf('{')..]); return j.RootElement.Clone(); }).ToArray();
    private static string Text(string path) => LogFileWriter.UnderGate(path, "write", () => File.Exists(path) ? File.ReadAllText(path) : "");
    private static void Sandbox(Action<string, FileLogger> action)
    {
        var root = Path.Combine(Path.GetTempPath(), "pinkie-resource-evidence-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try { action(root, new FileLogger(Path.Combine(root, "session.log"), "Diagnostic")); }
        finally { Directory.Delete(root, true); }
    }
    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
}
