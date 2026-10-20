using PinkieSysMon.DashboardModel;
using CanonicalDashboardDefinition = PinkieSysMon.DashboardModel.DashboardDefinition;
using CanonicalWidgetDefinition = PinkieSysMon.DashboardModel.WidgetDefinition;
using CanonicalTextPresentationDefinition = PinkieSysMon.DashboardModel.TextPresentationDefinition;

namespace PinkieSysMon;

/// <summary>
/// Creates editable, current-schema dashboard documents from arbitrary safe canvas geometry.
/// Editor resolution presets are deliberately not part of this factory.
/// </summary>
internal static class AdaptiveDashboardFactory
{
    // DashboardRenderer holds logical and wire BGRA surfaces, and may allocate an
    // additional BGRA bitmap for Desktop. At four bytes/pixel, this bounds those
    // three simultaneously resident buffers to approximately 48 MiB in total.
    // This is a per-generated-dashboard safety budget, not a transport capability.
    internal const long MaximumPixels = 4_194_304;

    public static CanonicalDashboardDefinition Create(int width, int height, int orientation, string baseDirectory)
    {
        if (orientation is not (0 or 90 or 180 or 270))
            throw new ArgumentOutOfRangeException(nameof(orientation), orientation,
                "Orientation must be exactly 0, 90, 180, or 270 degrees.");
        var logical = FrameGeometry.GetLogicalSize(width, height, orientation);
        if ((long)width * height > MaximumPixels)
            throw new ArgumentOutOfRangeException(nameof(width),
                $"Canvas {width}x{height} exceeds the generated dashboard's raster memory budget ({MaximumPixels} pixels).");

        // Three required rows must remain readable even at the smallest geometry.
        // These bounds reflect the existing fixed-width Roboto Value widget layout,
        // with an independent text container for the title, clock and host name.
        if (logical.Width < 240 || logical.Height < 180)
            throw new ArgumentOutOfRangeException(nameof(width),
                $"Canvas {width}x{height} at {orientation}° leaves only {logical.Width}x{logical.Height} logical pixels. " +
                "At least 240x180 logical pixels are needed for the clock, computer name and title.");

        var w = (float)logical.Width;
        var h = (float)logical.Height;
        var pad = Math.Clamp(Math.Min(w, h) / 20f, 12f, 28f);
        var gap = Math.Clamp(pad * 0.4f, 6f, 12f);
        var available = w - 2f * pad;
        var headerHeight = Math.Clamp(h * 0.11f, 26f, 48f);
        var bodyTop = pad + headerHeight + gap;
        var bodyHeight = h - pad - bodyTop;
        var widgets = new List<CanonicalWidgetDefinition>
        {
            Value("brand", "Application title", null, "Pinkie's System Monitor",
                pad, pad, available, headerHeight, Math.Clamp(available / 19f, 16f, 28f),
                "#FF8ED9FF", "#FF1A3042", ValueOverflowContract.Ellipsis, "left")
        };

        var panoramic = w >= 700f && w / h >= 1.7f && bodyHeight >= 220f;
        var verticalExtras = !panoramic && w >= 400f && h >= 520f;
        var primaryWidth = panoramic ? available * 0.58f : available;
        var primaryHeight = verticalExtras ? bodyHeight * 0.72f - gap : bodyHeight;
        var clockHeight = primaryHeight * 0.64f;
        var nameHeight = primaryHeight - clockHeight - gap;

        widgets.Add(Value("clock", "Current time", SystemMetricContract.DateTime, null,
            pad, bodyTop, primaryWidth, clockHeight,
            Math.Clamp(Math.Min(primaryWidth / 7f, clockHeight * 0.62f), 36f, 104f),
            "#FFFFFFFF", "#FF183F58", ValueOverflowContract.ShrinkToFit, "center", "HH:mm:ss"));
        widgets.Add(Value("computer", "Computer name", SystemMetricContract.PcName, null,
            pad, bodyTop + clockHeight + gap, primaryWidth, nameHeight,
            Math.Clamp(Math.Min(primaryWidth / 10f, nameHeight * 0.32f), 17f, 32f),
            "#FFE2F1FA", "#FF243747", ValueOverflowContract.Ellipsis, "center"));

        // Optional content is selected by free area, never by membership in a
        // resolution-preset list. Every metric is part of the built-in System provider.
        if (panoramic)
        {
            var sideX = pad + primaryWidth + gap;
            var sideWidth = available - primaryWidth - gap;
            var metrics = new (string Id, string Name, string Metric, string Prefix, string? Unit, string? Format)[]
            {
                ("processor", "Processor", SystemMetricContract.PcProcessor, "CPU  ", null, null),
                ("ram", "Installed memory", SystemMetricContract.PcRam, "RAM  ", "GiB", "0.0"),
                ("edition", "Windows edition", SystemMetricContract.Edition, "OS  ", null, null),
                ("uptime", "Uptime", SystemMetricContract.Uptime, "UP  ", "auto", null)
            };
            var count = bodyHeight >= 280f && sideWidth >= 240f ? 4 : 2;
            var tileHeight = (bodyHeight - (count - 1) * gap) / count;
            for (var i = 0; i < count; i++)
            {
                var entry = metrics[i];
                widgets.Add(Value(entry.Id, entry.Name, entry.Metric, null,
                    sideX, bodyTop + i * (tileHeight + gap), sideWidth, tileHeight,
                    Math.Clamp(tileHeight * 0.28f, 16f, 24f),
                    "#FFD7E8F5", "#FF243747", ValueOverflowContract.Ellipsis, "left",
                    entry.Format, entry.Prefix, entry.Unit));
            }
        }
        else if (verticalExtras)
        {
            var extraTop = bodyTop + primaryHeight + gap;
            var extraHeight = bodyHeight - primaryHeight - gap;
            var half = (available - gap) / 2f;
            widgets.Add(Value("processor", "Processor", SystemMetricContract.PcProcessor, null,
                pad, extraTop, half, extraHeight, 18f,
                "#FFD7E8F5", "#FF243747", ValueOverflowContract.Ellipsis, "center",
                prefix: "CPU  "));
            widgets.Add(Value("edition", "Windows edition", SystemMetricContract.Edition, null,
                pad + half + gap, extraTop, half, extraHeight, 18f,
                "#FFD7E8F5", "#FF243747", ValueOverflowContract.Ellipsis, "center",
                prefix: "OS  "));
        }

        return new CanonicalDashboardDefinition
        {
            SchemaVersion = DashboardSchemaMigration.CurrentSchemaVersion,
            BaseDirectory = baseDirectory,
            Canvas = new CanvasDefinition
            {
                Width = width,
                Height = height,
                Orientation = orientation,
                BackgroundColor = "#FF0C1926"
            },
            Widgets = widgets
        };
    }

    private static ValueWidgetDefinition Value(
        string id, string name, string? metric, string? text,
        float x, float y, float width, float height, float fontSize,
        string color, string background, string overflow, string alignment,
        string? format = null, string prefix = "", string? unit = null) => new()
    {
        Id = id,
        Name = name,
        SourceKind = metric is null ? ValueSourceKind.Text : ValueSourceKind.Metric,
        Metric = metric,
        Text = text ?? string.Empty,
        Format = format,
        Unit = unit,
        Prefix = prefix,
        X = x,
        // Value widgets use Y as the text's vertical alignment anchor, not a tile top.
        // Height=0 is intentional: their visual bounds are intrinsically text-sized.
        Y = y + height / 2f,
        Width = width,
        Height = 0f,
        Color = color,
        BackgroundColor = background,
        CornerRadius = 10f,
        TextPresentation = new CanonicalTextPresentationDefinition
        {
            FontFamily = "Roboto",
            FontSize = fontSize,
            FontWeight = id is "clock" or "brand" ? 700 : 500,
            Align = alignment,
            VerticalAlign = "middle",
            OverflowMode = overflow
        }
    };
}
