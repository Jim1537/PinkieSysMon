using SkiaSharp;
using CanonicalWidgetDefinition = global::PinkieSysMon.DashboardModel.WidgetDefinition;

namespace PinkieSysMon;

// Shared renderer for file/icon image presentation. Ordinary Image/Canvas content
// defaults a blank SourceType to file; state-profile content defaults it to icon.
internal sealed class ImageVisual : IDisposable
{
    private enum FitMode
    {
        Contain,
        Cover,
        Stretch
    }

    private enum BlankSourceTypeMeaning
    {
        File,
        Icon
    }

    private readonly FitMode _fit;
    private readonly string _fitName;
    private readonly bool _loop;
    private readonly float _opacity;
    private readonly IIconAsset? _icon;
    private readonly SKColor _iconColor;
    private readonly ImageAsset? _image;
    private readonly SKPaint? _paint;

    private ImageVisual(
        FitMode fit,
        string fitName,
        bool loop,
        float opacity,
        IIconAsset? icon,
        SKColor iconColor,
        ImageAsset? image,
        SKPaint? paint)
    {
        _fit = fit;
        _fitName = fitName;
        _loop = loop;
        _opacity = opacity;
        _icon = icon;
        _iconColor = iconColor;
        _image = image;
        _paint = paint;
    }

    public bool IsAnimated => _icon?.Capabilities.IsAnimated ?? _image?.IsAnimated ?? false;

    public static ImageVisual Create(
        ImageAssetCache images,
        IconAssetCache icons,
        string? sourceType,
        string source,
        string? fit,
        bool loop,
        float opacity,
        string? color,
        bool antialias) =>
        CreateCore(
            images,
            icons,
            sourceType,
            source,
            fit,
            loop,
            opacity,
            color ?? "#FFFFFFFF",
            antialias,
            BlankSourceTypeMeaning.File);

    public static ImageVisual CreateState(
        ImageAssetCache images,
        IconAssetCache icons,
        CanonicalWidgetDefinition widget,
        StateVisualProfile profile,
        string? fit,
        bool loop,
        bool antialias)
    {
        if (StateVisualProfileContract.IsValueSource(profile.SourceType))
            throw new InvalidOperationException("Value-backed state visuals must be rendered by the owning widget.");

        var color = string.IsNullOrWhiteSpace(profile.Color)
            ? widget.Color
            : profile.Color;
        // Preserve the historical state-visual fallback: blank/"icon" selects the
        // icon pipeline, while any other non-value token falls through to file.
        var stateSourceType = StateVisualProfileContract.IsIconSource(profile.SourceType)
            ? StateVisualProfileContract.SourceIcon
            : StateVisualProfileContract.SourceFile;

        return CreateCore(
            images,
            icons,
            stateSourceType,
            profile.Source,
            fit,
            loop,
            profile.Opacity,
            color,
            antialias,
            BlankSourceTypeMeaning.Icon);
    }

    public static bool IsStateSourceAnimated(
        ImageAssetCache images,
        IconAssetCache icons,
        StateVisualProfile profile)
    {
        ArgumentNullException.ThrowIfNull(images);
        ArgumentNullException.ThrowIfNull(icons);

        if (string.IsNullOrWhiteSpace(profile.Source) ||
            StateVisualProfileContract.IsValueSource(profile.SourceType))
        {
            return false;
        }

        if (StateVisualProfileContract.IsFileSource(profile.SourceType))
            return images.Get(profile.Source).IsAnimated;

        return StateVisualProfileContract.IsIconSource(profile.SourceType) &&
               icons.Get(profile.Source).Capabilities.IsAnimated;
    }

    private static ImageVisual CreateCore(
        ImageAssetCache images,
        IconAssetCache icons,
        string? sourceType,
        string source,
        string? fit,
        bool loop,
        float opacity,
        string? color,
        bool antialias,
        BlankSourceTypeMeaning blankSourceTypeMeaning)
    {
        ArgumentNullException.ThrowIfNull(images);
        ArgumentNullException.ThrowIfNull(icons);
        if (string.IsNullOrWhiteSpace(source))
            throw new InvalidDataException("Image source must not be empty.");

        var normalizedFit = NormalizeFit(fit);
        var fitName = normalizedFit switch
        {
            FitMode.Stretch => StateVisualProfileContract.FitStretch,
            FitMode.Cover => StateVisualProfileContract.FitCover,
            _ => StateVisualProfileContract.FitContain
        };
        var normalizedSourceType = NormalizeSourceType(sourceType, blankSourceTypeMeaning);

        if (string.Equals(
                normalizedSourceType,
                StateVisualProfileContract.SourceIcon,
                StringComparison.OrdinalIgnoreCase))
        {
            return new ImageVisual(
                normalizedFit,
                fitName,
                loop,
                opacity,
                icons.Get(source),
                ColorParser.Parse(color ?? "#FFFFFFFF"),
                null,
                null);
        }

        if (!string.Equals(
                normalizedSourceType,
                StateVisualProfileContract.SourceFile,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"Unsupported image source type '{sourceType}'.");
        }

        var image = images.Get(source);
        SKPaint? paint = null;
        if (opacity < 0.999f)
        {
            paint = new SKPaint
            {
                Color = new SKColor(
                    255,
                    255,
                    255,
                    (byte)Math.Clamp((int)Math.Round(Math.Clamp(opacity, 0f, 1f) * 255f), 0, 255)),
                IsAntialias = antialias
            };
        }

        return new ImageVisual(
            normalizedFit,
            fitName,
            loop,
            opacity,
            null,
            default,
            image,
            paint);
    }

    public void Draw(SKCanvas canvas, SKRect bounds, bool antialias) =>
        DrawCore(canvas, bounds, antialias, elapsedMs: null);

    public void DrawAtElapsed(
        SKCanvas canvas,
        SKRect bounds,
        bool antialias,
        long elapsedMs) =>
        DrawCore(canvas, bounds, antialias, Math.Max(0L, elapsedMs));

    private void DrawCore(
        SKCanvas canvas,
        SKRect bounds,
        bool antialias,
        long? elapsedMs)
    {
        ArgumentNullException.ThrowIfNull(canvas);

        if (_icon is not null)
        {
            _icon.Draw(
                canvas,
                bounds,
                _iconColor,
                _opacity,
                _fitName,
                _loop,
                antialias,
                elapsedMs);
            return;
        }

        var image = _image ?? throw new InvalidOperationException("Image visual has no renderable asset.");
        var bitmap = elapsedMs.HasValue
            ? image.GetFrameAtElapsed(elapsedMs.Value, _loop)
            : image.GetFrame(_loop);
        var destination = CalculateDestination(bounds, image.Width, image.Height, _fit);

        switch (_fit)
        {
            case FitMode.Stretch:
                canvas.DrawBitmap(bitmap, bounds, SKSamplingOptions.Default, _paint);
                break;

            case FitMode.Cover:
                canvas.Save();
                try
                {
                    canvas.ClipRect(bounds);
                    canvas.DrawBitmap(bitmap, destination, SKSamplingOptions.Default, _paint);
                }
                finally
                {
                    canvas.Restore();
                }
                break;

            default:
                canvas.DrawBitmap(bitmap, destination, SKSamplingOptions.Default, _paint);
                break;
        }
    }

    private static string NormalizeSourceType(
        string? sourceType,
        BlankSourceTypeMeaning blankSourceTypeMeaning)
    {
        var normalized = sourceType?.Trim();
        if (!string.IsNullOrWhiteSpace(normalized))
            return normalized;

        return blankSourceTypeMeaning == BlankSourceTypeMeaning.Icon
            ? StateVisualProfileContract.SourceIcon
            : StateVisualProfileContract.SourceFile;
    }

    private static FitMode NormalizeFit(string? fit)
    {
        var normalized = fit?.Trim();
        if (string.Equals(normalized, StateVisualProfileContract.FitStretch, StringComparison.OrdinalIgnoreCase))
            return FitMode.Stretch;
        if (string.Equals(normalized, StateVisualProfileContract.FitCover, StringComparison.OrdinalIgnoreCase))
            return FitMode.Cover;
        return FitMode.Contain;
    }

    private static SKRect CalculateDestination(SKRect bounds, int width, int height, FitMode fit)
    {
        if (fit == FitMode.Stretch)
            return bounds;

        var scale = fit == FitMode.Cover
            ? Math.Max(bounds.Width / width, bounds.Height / height)
            : Math.Min(bounds.Width / width, bounds.Height / height);
        var renderWidth = width * scale;
        var renderHeight = height * scale;
        return new SKRect(
            bounds.MidX - renderWidth / 2f,
            bounds.MidY - renderHeight / 2f,
            bounds.MidX + renderWidth / 2f,
            bounds.MidY + renderHeight / 2f);
    }

    public void Dispose() => _paint?.Dispose();
}
