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
        var minimumWidth = Math.Min(canvas.Width, 96);
        var minimumHeight = Math.Min(canvas.Height, 48);
        var visible = new List<Rectangle>(workingAreas.Count);
        foreach (var area in workingAreas)
        {
            var intersection = Rectangle.Intersect(bounds, area);
            if (intersection.Width <= 0 || intersection.Height <= 0)
                continue;
            if (intersection.Width >= minimumWidth && intersection.Height >= minimumHeight)
                return desired;
            visible.Add(intersection);
        }

        // An Output straddling two displays can be usable even if neither working
        // area alone meets the minimum. Count actual visible spans, not the
        // bounding rectangle (which can include gaps between monitor edges).
        if (visible.Count > 1 &&
            CoveredLength(visible.Select(area => (area.Left, area.Right))) >= minimumWidth &&
            CoveredLength(visible.Select(area => (area.Top, area.Bottom))) >= minimumHeight &&
            visible.Sum(area => (long)area.Width * area.Height) >= (long)minimumWidth * minimumHeight)
            return desired;

        // Keep original X/Y in the file in case a detached screen returns.
        return primary.Location;
    }

    private static long CoveredLength(IEnumerable<(int Start, int End)> ranges)
    {
        long covered = 0;
        var lastEnd = int.MinValue;
        foreach (var (start, end) in ranges.OrderBy(range => range.Start))
        {
            if (end <= lastEnd)
                continue;
            covered += (long)end - Math.Max(start, lastEnd);
            lastEnd = end;
        }
        return covered;
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
