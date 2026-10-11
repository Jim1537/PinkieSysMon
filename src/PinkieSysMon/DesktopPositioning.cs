using System.Drawing;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace PinkieSysMon;

internal static class DesktopPlacement
{
    // PMv2 virtual desktop coordinates are physical pixels; negative X/Y is valid.
    public static Point Recover(Point desired, Size canvas, IReadOnlyList<Rectangle> workingAreas, Rectangle primary)
    {
        if (canvas.Width <= 0 || canvas.Height <= 0)
            throw new ArgumentOutOfRangeException(nameof(canvas));
        ArgumentNullException.ThrowIfNull(workingAreas);
        var bounds = new Rectangle(desired, canvas);
        foreach (var area in workingAreas)
        {
            var intersection = Rectangle.Intersect(bounds, area);
            if (intersection.Width >= Math.Min(canvas.Width, 96) &&
                intersection.Height >= Math.Min(canvas.Height, 48))
                return desired;
        }
        // Keep original X/Y in the file in case a detached screen returns.
        return primary.Location;
    }
}

internal static class DesktopPositionStore
{
    // Preserve all existing private or future app.json fields. Modify X/Y only.
    public static void Save(string configPath, string outputId, Point location)
    {
        var root = JsonNode.Parse(File.ReadAllText(configPath),
            new JsonNodeOptions { PropertyNameCaseInsensitive = true },
            new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true })
            as JsonObject ?? throw new InvalidDataException("Invalid app.json.");
        var outputs = Get(root, "Outputs") as JsonObject
            ?? throw new InvalidDataException("Missing Outputs.");
        var targets = Get(outputs, "Targets") as JsonArray
            ?? throw new InvalidDataException("Missing Targets.");
        var matches = targets.OfType<JsonObject>()
            .Where(item => string.Equals(Get(item, "Id")?.GetValue<string>(),
                outputId, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (matches.Length != 1 ||
            !string.Equals(Get(matches[0], "Kind")?.GetValue<string>(),
                OutputTargetContract.DesktopKind, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Desktop target identity changed.");
        var desktop = Get(matches[0], "Desktop") as JsonObject
            ?? throw new InvalidDataException("Missing Desktop settings.");
        Put(desktop, "X", location.X);
        Put(desktop, "Y", location.Y);
        var temp = configPath + ".desktop-" + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temp,
                root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) +
                Environment.NewLine);
            File.Move(temp, configPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp))
                File.Delete(temp);
        }
    }

    private static JsonNode? Get(JsonObject obj, string name) =>
        obj.FirstOrDefault(p => p.Key.Equals(name, StringComparison.OrdinalIgnoreCase)).Value;

    private static void Put(JsonObject obj, string name, int value)
    {
        var actual = obj.Select(p => p.Key)
            .FirstOrDefault(k => k.Equals(name, StringComparison.OrdinalIgnoreCase));
        obj[actual ?? name] = value;
    }
}
