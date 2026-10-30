using SkiaSharp;

namespace PinkieSysMon;

internal sealed record IconAssetCapabilities(
    string Format,
    bool Tintable,
    bool UsesIntrinsicColor,
    bool IsAnimated,
    bool SupportsAlpha,
    bool HasFrameTiming);

internal interface IIconAsset : IDisposable
{
    IconAssetCapabilities Capabilities { get; }
    long DecodedByteCount { get; }

    void Draw(
        SKCanvas canvas,
        SKRect bounds,
        SKColor color,
        float opacity,
        string fit,
        bool loop,
        bool antialias,
        long? elapsedMs = null);
}

internal static class IconAssetLoader
{
    private sealed record CapabilityCacheEntry(
        long Length,
        DateTime LastWriteTimeUtc,
        IconAssetCapabilities Capabilities);

    private static readonly object CapabilityCacheSync = new();
    private static readonly Dictionary<string, CapabilityCacheEntry> CapabilityCache =
        new(StringComparer.OrdinalIgnoreCase);

    public static IIconAsset Load(string path)
    {
        if (!GlobalAssetResolver.IsSupportedIconPath(path))
            throw new InvalidDataException($"Unsupported global icon format '{Path.GetExtension(path)}' for '{path}'.");

        return Path.GetExtension(path).Equals(".svg", StringComparison.OrdinalIgnoreCase)
            ? SvgIconAsset.Load(path)
            : RasterIconAsset.Load(path);
    }

    public static IconAssetCapabilities ReadCapabilities(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var info = new FileInfo(fullPath);
        if (!info.Exists)
            throw new FileNotFoundException($"Global icon asset was not found: '{fullPath}'.", fullPath);

        lock (CapabilityCacheSync)
        {
            if (CapabilityCache.TryGetValue(fullPath, out var cached) &&
                cached.Length == info.Length &&
                cached.LastWriteTimeUtc == info.LastWriteTimeUtc)
            {
                return cached.Capabilities;
            }
        }

        var capabilities = ProbeCapabilities(fullPath);
        lock (CapabilityCacheSync)
        {
            CapabilityCache[fullPath] = new CapabilityCacheEntry(
                info.Length,
                info.LastWriteTimeUtc,
                capabilities);
        }

        return capabilities;
    }

    public static bool TryReadCapabilitiesFromRoot(
        string? applicationRoot,
        string? logicalName,
        out IconAssetCapabilities capabilities)
    {
        capabilities = InferCapabilitiesFromLogicalName(logicalName);
        if (string.IsNullOrWhiteSpace(logicalName))
            return false;

        if (string.IsNullOrWhiteSpace(applicationRoot))
            return true;

        try
        {
            var path = GlobalAssetResolver.ResolveIconFromRoot(applicationRoot, logicalName);
            capabilities = ReadCapabilities(path);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static IconAssetCapabilities ProbeCapabilities(string path)
    {
        var extension = Path.GetExtension(path).ToLowerInvariant();
        if (!GlobalAssetResolver.IsSupportedIconExtension(extension))
            throw new InvalidDataException($"Unsupported global icon format '{extension}' for '{path}'.");

        if (extension == ".svg")
        {
            // Parsing the file here deliberately validates the same SVG subset used by rendering.
            using var asset = SvgIconAsset.Load(path);
            return asset.Capabilities;
        }

        using var stream = File.OpenRead(path);
        using var codec = SKCodec.Create(stream)
            ?? throw new InvalidDataException($"Skia could not decode global icon asset '{path}'.");

        if (codec.Info.Width <= 0 || codec.Info.Height <= 0)
            throw new InvalidDataException($"Global icon asset '{path}' has invalid dimensions.");

        var frameCount = Math.Max(1, codec.FrameCount);
        var isAnimated = frameCount > 1;
        return new IconAssetCapabilities(
            FormatLabel(extension),
            Tintable: false,
            UsesIntrinsicColor: true,
            IsAnimated: isAnimated,
            SupportsAlpha: !codec.Info.IsOpaque,
            HasFrameTiming: isAnimated);
    }

    private static IconAssetCapabilities InferCapabilitiesFromLogicalName(string? logicalName)
    {
        var extension = Path.GetExtension(logicalName ?? string.Empty).ToLowerInvariant();
        if (extension == ".svg" || string.IsNullOrWhiteSpace(extension))
        {
            return new IconAssetCapabilities(
                "SVG",
                Tintable: true,
                UsesIntrinsicColor: false,
                IsAnimated: false,
                SupportsAlpha: true,
                HasFrameTiming: false);
        }

        var potentiallyAnimated = extension is ".gif" or ".webp";
        return new IconAssetCapabilities(
            FormatLabel(extension),
            Tintable: false,
            UsesIntrinsicColor: true,
            IsAnimated: potentiallyAnimated,
            SupportsAlpha: extension is not (".jpg" or ".jpeg" or ".bmp"),
            HasFrameTiming: potentiallyAnimated);
    }

    private static string FormatLabel(string extension) => extension.ToLowerInvariant() switch
    {
        ".svg" => "SVG",
        ".png" => "PNG",
        ".jpg" or ".jpeg" => "JPEG",
        ".bmp" => "BMP",
        ".gif" => "GIF",
        ".ico" => "ICO",
        ".webp" => "WebP",
        _ => extension.TrimStart('.').ToUpperInvariant()
    };
}

internal sealed class RasterIconAsset : IIconAsset
{
    private readonly ImageAsset _image;

    private RasterIconAsset(ImageAsset image, IconAssetCapabilities capabilities)
    {
        _image = image;
        Capabilities = capabilities;
    }

    public IconAssetCapabilities Capabilities { get; }
    public long DecodedByteCount => _image.DecodedByteCount;

    public static RasterIconAsset Load(string path)
    {
        var capabilities = IconAssetLoader.ReadCapabilities(path);
        var image = ImageAsset.Load(path);
        return new RasterIconAsset(image, capabilities);
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
        _ = color; // Raster icon assets preserve their intrinsic colors.

        var bitmap = elapsedMs.HasValue
            ? _image.GetFrameAtElapsed(Math.Max(0L, elapsedMs.Value), loop)
            : _image.GetFrame(loop);
        var destination = CalculateDestination(bounds, _image.Width, _image.Height, fit);
        using var paint = new SKPaint
        {
            IsAntialias = antialias,
            Color = new SKColor(
                255,
                255,
                255,
                (byte)Math.Clamp((int)Math.Round(Math.Clamp(opacity, 0f, 1f) * 255f), 0, 255))
        };

        var cover = string.Equals(fit, "cover", StringComparison.OrdinalIgnoreCase);
        if (cover)
        {
            canvas.Save();
            try
            {
                canvas.ClipRect(bounds);
                canvas.DrawBitmap(bitmap, destination, SKSamplingOptions.Default, paint);
            }
            finally
            {
                canvas.Restore();
            }

            return;
        }

        canvas.DrawBitmap(bitmap, destination, SKSamplingOptions.Default, paint);
    }

    public void Dispose() => _image.Dispose();

    private static SKRect CalculateDestination(SKRect bounds, int width, int height, string fit)
    {
        if (string.Equals(fit, "stretch", StringComparison.OrdinalIgnoreCase))
            return bounds;

        var cover = string.Equals(fit, "cover", StringComparison.OrdinalIgnoreCase);
        var scale = cover
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
}
