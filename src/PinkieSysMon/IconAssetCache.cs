using CanonicalDashboard = PinkieSysMon.DashboardModel;
using System.Globalization;
using System.Xml.Linq;
using SkiaSharp;

namespace PinkieSysMon;

internal sealed class IconAssetCache : IDisposable
{
    private readonly string _dashboardBaseDirectory;
    private const long MaximumAggregateDecodedBytes = 256L * 1024L * 1024L;

    private readonly Dictionary<string, IIconAsset> _assetsByLogicalName = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, IIconAsset> _assetsByPath = new(StringComparer.OrdinalIgnoreCase);
    private long _aggregateDecodedBytes;

    public IconAssetCache(string dashboardBaseDirectory)
    {
        _dashboardBaseDirectory = dashboardBaseDirectory;
    }


    public void Preload(IEnumerable<CanonicalDashboard.WidgetDefinition> widgets)
    {
        foreach (var widget in widgets)
        {
            switch (widget)
            {
                case CanonicalDashboard.ImageWidgetDefinition image
                    when CanonicalDashboard.ImageAssetSourceType.IsIcon(image.Asset.SourceType) &&
                         !string.IsNullOrWhiteSpace(image.Asset.Source):
                    _ = Get(image.Asset.Source!);
                    break;

                case CanonicalDashboard.StateVisualWidgetDefinition stateWidget:
                    foreach (var profile in stateWidget.Profiles.Values)
                    {
                        if (CanonicalDashboard.StateContentType.IsImage(profile.ContentType) &&
                            CanonicalDashboard.ImageAssetSourceType.IsIcon(profile.Asset.SourceType) &&
                            !string.IsNullOrWhiteSpace(profile.Asset.Source))
                        {
                            _ = Get(profile.Asset.Source!);
                        }
                    }
                    break;
            }
        }
    }

    public bool HasAnimatedAssets => _assetsByPath.Values.Any(asset => asset.Capabilities.IsAnimated);

    public IIconAsset Get(string logicalName)
    {
        if (string.IsNullOrWhiteSpace(logicalName))
            throw new InvalidDataException("Icon source must not be empty.");

        var logicalKey = logicalName.Trim();
        if (_assetsByLogicalName.TryGetValue(logicalKey, out var cachedByName))
            return cachedByName;

        var path = GlobalAssetResolver.ResolveIcon(_dashboardBaseDirectory, logicalKey);
        if (!_assetsByPath.TryGetValue(path, out var asset))
        {
            asset = IconAssetLoader.Load(path);
            if (asset.DecodedByteCount > MaximumAggregateDecodedBytes - _aggregateDecodedBytes)
            {
                var projectedBytes = _aggregateDecodedBytes + asset.DecodedByteCount;
                asset.Dispose();
                throw new InvalidDataException(
                    $"Global icon assets would require about {projectedBytes / (1024d * 1024d):F1} MiB decoded memory; " +
                    $"maximum supported per dashboard is {MaximumAggregateDecodedBytes / (1024 * 1024)} MiB.");
            }

            _assetsByPath[path] = asset;
            _aggregateDecodedBytes += asset.DecodedByteCount;
        }

        _assetsByLogicalName[logicalKey] = asset;
        return asset;
    }

    public void Dispose()
    {
        foreach (var asset in _assetsByPath.Values.Distinct())
            asset.Dispose();
        _assetsByLogicalName.Clear();
        _assetsByPath.Clear();
        _aggregateDecodedBytes = 0;
    }
}

internal sealed class SvgIconAsset : IIconAsset
{
    private sealed record Element(SKPath Path, SvgStyle Style);

    private readonly List<Element> _elements;
    private readonly SKRect _viewBox;
    private readonly SKPaint _fillPaint = new() { Style = SKPaintStyle.Fill };
    private readonly SKPaint _strokePaint = new() { Style = SKPaintStyle.Stroke };

    private SvgIconAsset(SKRect viewBox, List<Element> elements)
    {
        _viewBox = viewBox;
        _elements = elements;
    }

    public IconAssetCapabilities Capabilities { get; } = new(
        "SVG",
        Tintable: true,
        UsesIntrinsicColor: false,
        IsAnimated: false,
        SupportsAlpha: true,
        HasFrameTiming: false);

    public long DecodedByteCount => 0;

    public static SvgIconAsset Load(string path)
    {
        try
        {
            var document = XDocument.Load(path, LoadOptions.None);
            var root = document.Root ?? throw new InvalidDataException("SVG document has no root element.");
            if (!root.Name.LocalName.Equals("svg", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("SVG root element is not <svg>.");

            var viewBox = ParseViewBox(root.Attribute("viewBox")?.Value);
            var initialStyle = SvgStyle.Default.Apply(root);
            var elements = new List<Element>();
            ParseChildren(root, initialStyle, elements);
            if (elements.Count == 0)
                throw new InvalidDataException("SVG contains no supported drawable elements.");

            return new SvgIconAsset(viewBox, elements);
        }
        catch (Exception ex) when (ex is not InvalidDataException)
        {
            throw new InvalidDataException($"Could not load SVG icon '{path}': {ex.Message}", ex);
        }
    }

    public void Draw(
        SKCanvas canvas,
        SKRect bounds,
        SKColor color,
        float opacity,
        string fit,
        bool loop,
        bool antialias,
        long? elapsedMs = null)
    {
        _ = loop;
        _ = elapsedMs;
        Draw(canvas, bounds, color, opacity, fit, antialias);
    }

    public void Draw(SKCanvas canvas, SKRect bounds, SKColor color, float opacity, string fit, bool antialias)
    {
        var alpha = (byte)Math.Clamp((int)Math.Round(color.Alpha * Math.Clamp(opacity, 0f, 1f)), 0, 255);
        var drawColor = color.WithAlpha(alpha);
        var sx = bounds.Width / _viewBox.Width;
        var sy = bounds.Height / _viewBox.Height;

        var contain = string.Equals(fit, "contain", StringComparison.OrdinalIgnoreCase);
        var cover = string.Equals(fit, "cover", StringComparison.OrdinalIgnoreCase);
        if (contain || cover)
        {
            var scale = cover ? Math.Max(sx, sy) : Math.Min(sx, sy);
            sx = scale;
            sy = scale;
        }

        var renderedWidth = _viewBox.Width * sx;
        var renderedHeight = _viewBox.Height * sy;
        var left = bounds.MidX - renderedWidth / 2f;
        var top = bounds.MidY - renderedHeight / 2f;

        canvas.Save();
        try
        {
            canvas.ClipRect(bounds);
            canvas.Translate(left, top);
            canvas.Scale(sx, sy);
            canvas.Translate(-_viewBox.Left, -_viewBox.Top);

            foreach (var element in _elements)
            {
                var style = element.Style;
                if (style.FillEnabled)
                {
                    _fillPaint.Color = drawColor.WithAlpha(ScaleAlpha(drawColor.Alpha, style.Opacity));
                    _fillPaint.IsAntialias = antialias;
                    canvas.DrawPath(element.Path, _fillPaint);
                }

                if (style.StrokeEnabled && style.StrokeWidth > 0f)
                {
                    _strokePaint.Color = drawColor.WithAlpha(ScaleAlpha(drawColor.Alpha, style.Opacity));
                    _strokePaint.StrokeWidth = style.StrokeWidth;
                    _strokePaint.StrokeCap = style.LineCap;
                    _strokePaint.StrokeJoin = style.LineJoin;
                    _strokePaint.IsAntialias = antialias;
                    canvas.DrawPath(element.Path, _strokePaint);
                }
            }
        }
        finally
        {
            canvas.Restore();
        }
    }

    public void Dispose()
    {
        foreach (var element in _elements)
            element.Path.Dispose();
        _elements.Clear();
        _strokePaint.Dispose();
        _fillPaint.Dispose();
    }

    private static void ParseChildren(XElement parent, SvgStyle inherited, List<Element> result)
    {
        foreach (var child in parent.Elements())
        {
            var style = inherited.Apply(child);
            var localName = child.Name.LocalName.ToLowerInvariant();
            if (localName == "g")
            {
                ParseChildren(child, style, result);
                continue;
            }

            var path = CreatePath(child, localName);
            if (path is not null)
                result.Add(new Element(path, style));
        }
    }

    private static SKPath? CreatePath(XElement element, string localName)
    {
        switch (localName)
        {
            case "path":
            {
                var d = element.Attribute("d")?.Value;
                return string.IsNullOrWhiteSpace(d) ? null : SKPath.ParseSvgPathData(d);
            }
            case "line":
            {
                using var builder = new SKPathBuilder();
                builder.MoveTo(F(element, "x1"), F(element, "y1"));
                builder.LineTo(F(element, "x2"), F(element, "y2"));
                return builder.Detach();
            }
            case "polyline":
            case "polygon":
            {
                var points = ParsePoints(element.Attribute("points")?.Value);
                if (points.Count == 0)
                    return null;
                using var builder = new SKPathBuilder();
                builder.MoveTo(points[0]);
                foreach (var point in points.Skip(1))
                    builder.LineTo(point);
                if (localName == "polygon")
                    builder.Close();
                return builder.Detach();
            }
            case "circle":
            {
                using var builder = new SKPathBuilder();
                builder.AddCircle(F(element, "cx"), F(element, "cy"), F(element, "r"));
                return builder.Detach();
            }
            case "ellipse":
            {
                var cx = F(element, "cx");
                var cy = F(element, "cy");
                var rx = F(element, "rx");
                var ry = F(element, "ry");
                using var builder = new SKPathBuilder();
                builder.AddOval(new SKRect(cx - rx, cy - ry, cx + rx, cy + ry));
                return builder.Detach();
            }
            case "rect":
            {
                var x = F(element, "x");
                var y = F(element, "y");
                var width = F(element, "width");
                var height = F(element, "height");
                var rx = F(element, "rx", 0f);
                var ry = F(element, "ry", rx);
                using var builder = new SKPathBuilder();
                if (rx > 0f || ry > 0f)
                    builder.AddRoundRect(new SKRect(x, y, x + width, y + height), rx, ry);
                else
                    builder.AddRect(new SKRect(x, y, x + width, y + height));
                return builder.Detach();
            }
            default:
                return null;
        }
    }

    private static SKRect ParseViewBox(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return new SKRect(0, 0, 24, 24);

        var values = value
            .Split([' ', ','], StringSplitOptions.RemoveEmptyEntries)
            .Select(x => float.Parse(x, CultureInfo.InvariantCulture))
            .ToArray();
        if (values.Length != 4 || values[2] <= 0 || values[3] <= 0)
            throw new InvalidDataException($"Unsupported SVG viewBox '{value}'.");

        return new SKRect(values[0], values[1], values[0] + values[2], values[1] + values[3]);
    }

    private static List<SKPoint> ParsePoints(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return [];
        var values = value
            .Split([' ', ','], StringSplitOptions.RemoveEmptyEntries)
            .Select(x => float.Parse(x, CultureInfo.InvariantCulture))
            .ToArray();
        if (values.Length % 2 != 0)
            throw new InvalidDataException($"Invalid SVG points list '{value}'.");
        var result = new List<SKPoint>(values.Length / 2);
        for (var i = 0; i < values.Length; i += 2)
            result.Add(new SKPoint(values[i], values[i + 1]));
        return result;
    }

    private static float F(XElement element, string name, float defaultValue = 0f)
    {
        var value = element.Attribute(name)?.Value;
        return string.IsNullOrWhiteSpace(value)
            ? defaultValue
            : float.Parse(value, CultureInfo.InvariantCulture);
    }

    private static byte ScaleAlpha(byte alpha, float opacity) =>
        (byte)Math.Clamp((int)Math.Round(alpha * Math.Clamp(opacity, 0f, 1f)), 0, 255);

    private sealed record SvgStyle(
        bool StrokeEnabled,
        bool FillEnabled,
        float StrokeWidth,
        SKStrokeCap LineCap,
        SKStrokeJoin LineJoin,
        float Opacity)
    {
        public static SvgStyle Default { get; } = new(
            StrokeEnabled: true,
            FillEnabled: false,
            StrokeWidth: 2f,
            LineCap: SKStrokeCap.Butt,
            LineJoin: SKStrokeJoin.Miter,
            Opacity: 1f);

        public SvgStyle Apply(XElement element)
        {
            var stroke = Attribute(element, "stroke");
            var fill = Attribute(element, "fill");
            var strokeWidth = Attribute(element, "stroke-width");
            var lineCap = Attribute(element, "stroke-linecap");
            var lineJoin = Attribute(element, "stroke-linejoin");
            var opacity = Attribute(element, "opacity");
            var strokeOpacity = Attribute(element, "stroke-opacity");
            var fillOpacity = Attribute(element, "fill-opacity");

            var effectiveOpacity = Opacity;
            if (TryFloat(opacity, out var generalOpacity))
                effectiveOpacity *= generalOpacity;
            if (TryFloat(strokeOpacity, out var so))
                effectiveOpacity *= so;
            if (TryFloat(fillOpacity, out var fo))
                effectiveOpacity *= fo;

            return this with
            {
                StrokeEnabled = stroke is null ? StrokeEnabled : !stroke.Equals("none", StringComparison.OrdinalIgnoreCase),
                FillEnabled = fill is null ? FillEnabled : !fill.Equals("none", StringComparison.OrdinalIgnoreCase),
                StrokeWidth = TryFloat(strokeWidth, out var sw) ? sw : StrokeWidth,
                LineCap = lineCap is null ? LineCap : ParseLineCap(lineCap),
                LineJoin = lineJoin is null ? LineJoin : ParseLineJoin(lineJoin),
                Opacity = Math.Clamp(effectiveOpacity, 0f, 1f)
            };
        }

        private static string? Attribute(XElement element, string name) => element.Attribute(name)?.Value?.Trim();
        private static bool TryFloat(string? value, out float parsed) =>
            float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed);

        private static SKStrokeCap ParseLineCap(string value) => value.ToLowerInvariant() switch
        {
            "round" => SKStrokeCap.Round,
            "square" => SKStrokeCap.Square,
            _ => SKStrokeCap.Butt
        };

        private static SKStrokeJoin ParseLineJoin(string value) => value.ToLowerInvariant() switch
        {
            "round" => SKStrokeJoin.Round,
            "bevel" => SKStrokeJoin.Bevel,
            _ => SKStrokeJoin.Miter
        };
    }
}
