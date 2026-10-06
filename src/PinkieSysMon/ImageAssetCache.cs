using CanonicalDashboard = PinkieSysMon.DashboardModel;
using SkiaSharp;

namespace PinkieSysMon;

internal sealed class ImageAssetCache : IDisposable
{
    private const long MaximumAggregateDecodedBytes = 256L * 1024L * 1024L;

    private readonly string _baseDirectory;
    private readonly Dictionary<string, ImageAsset> _assetsBySource = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ImageAsset> _assetsByPath = new(StringComparer.OrdinalIgnoreCase);
    private long _aggregateDecodedBytes;

    public ImageAssetCache(string baseDirectory)
    {
        _baseDirectory = baseDirectory;
    }


    public void Preload(IEnumerable<CanonicalDashboard.WidgetDefinition> widgets)
    {
        foreach (var widget in widgets)
        {
            switch (widget)
            {
                case CanonicalDashboard.ImageWidgetDefinition image
                    when CanonicalDashboard.ImageAssetSourceType.IsFile(image.Asset.SourceType) &&
                         !string.IsNullOrWhiteSpace(image.Asset.Source):
                    _ = Get(image.Asset.Source!);
                    break;

                case CanonicalDashboard.BarWidgetDefinition bar
                    when BarImageContract.IsImageMode(bar.ContentMode) &&
                         !string.IsNullOrWhiteSpace(bar.Image.Source):
                    _ = Get(bar.Image.Source!);
                    break;

                case CanonicalDashboard.StateVisualWidgetDefinition stateWidget:
                    foreach (var profile in stateWidget.Profiles.Values)
                    {
                        if (CanonicalDashboard.StateContentType.IsImage(profile.ContentType) &&
                            CanonicalDashboard.ImageAssetSourceType.IsFile(profile.Asset.SourceType) &&
                            !string.IsNullOrWhiteSpace(profile.Asset.Source))
                        {
                            _ = Get(profile.Asset.Source!);
                        }
                    }
                    break;
            }
        }
    }

    public bool HasAnimatedAssets => _assetsByPath.Values.Any(asset => asset.IsAnimated);

    public ImageAsset Get(string source)
    {
        var sourceKey = NormalizeSourceKey(source);
        if (_assetsBySource.TryGetValue(sourceKey, out var cachedBySource))
            return cachedBySource;

        var fullPath = AssetPathResolver.ResolveExistingFile(_baseDirectory, sourceKey, "Dashboard image asset");
        if (!_assetsByPath.TryGetValue(fullPath, out var asset))
        {
            asset = ImageAsset.Load(fullPath);
            if (asset.DecodedByteCount > MaximumAggregateDecodedBytes - _aggregateDecodedBytes)
            {
                var projectedBytes = _aggregateDecodedBytes + asset.DecodedByteCount;
                asset.Dispose();
                throw new InvalidDataException(
                    $"Dashboard image assets would require about {projectedBytes / (1024d * 1024d):F1} MiB decoded memory; " +
                    $"maximum supported per dashboard is {MaximumAggregateDecodedBytes / (1024 * 1024)} MiB.");
            }

            _assetsByPath[fullPath] = asset;
            _aggregateDecodedBytes += asset.DecodedByteCount;
        }

        _assetsBySource[sourceKey] = asset;
        return asset;
    }

    public void Dispose()
    {
        foreach (var asset in _assetsByPath.Values.Distinct())
            asset.Dispose();

        _assetsBySource.Clear();
        _assetsByPath.Clear();
        _aggregateDecodedBytes = 0;
    }

    private static string NormalizeSourceKey(string source)
    {
        if (string.IsNullOrWhiteSpace(source))
            throw new InvalidDataException("Dashboard image asset path must not be empty.");

        return source.Trim();
    }
}

internal sealed record ImageAssetCapabilities(
    string Format,
    bool UsesIntrinsicColor,
    bool IsAnimated,
    bool SupportsAlpha,
    bool HasFrameTiming);

internal static class ImageAssetCapabilityProbe
{
    private sealed record CacheEntry(
        long Length,
        DateTime LastWriteTimeUtc,
        ImageAssetCapabilities Capabilities);

    private static readonly object CacheSync = new();
    private static readonly Dictionary<string, CacheEntry> Cache = new(StringComparer.OrdinalIgnoreCase);

    public static ImageAssetCapabilities ReadCapabilities(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var info = new FileInfo(fullPath);
        if (!info.Exists)
            throw new FileNotFoundException($"Dashboard image asset was not found: '{fullPath}'.", fullPath);

        lock (CacheSync)
        {
            if (Cache.TryGetValue(fullPath, out var cached) &&
                cached.Length == info.Length &&
                cached.LastWriteTimeUtc == info.LastWriteTimeUtc)
            {
                return cached.Capabilities;
            }
        }

        using var stream = File.OpenRead(fullPath);
        using var codec = SKCodec.Create(stream)
            ?? throw new InvalidDataException($"Skia could not decode image '{fullPath}'.");
        if (codec.Info.Width <= 0 || codec.Info.Height <= 0)
            throw new InvalidDataException($"Image '{fullPath}' has invalid dimensions.");

        var extension = Path.GetExtension(fullPath).ToLowerInvariant();
        var animated = Math.Max(1, codec.FrameCount) > 1;
        var capabilities = new ImageAssetCapabilities(
            extension switch
            {
                ".png" => "PNG",
                ".jpg" or ".jpeg" => "JPEG",
                ".bmp" => "BMP",
                ".gif" => "GIF",
                ".ico" => "ICO",
                ".webp" => "WebP",
                _ => extension.TrimStart('.').ToUpperInvariant()
            },
            UsesIntrinsicColor: true,
            IsAnimated: animated,
            SupportsAlpha: !codec.Info.IsOpaque,
            HasFrameTiming: animated);

        lock (CacheSync)
        {
            Cache[fullPath] = new CacheEntry(info.Length, info.LastWriteTimeUtc, capabilities);
        }

        return capabilities;
    }

    public static bool TryReadCapabilities(
        string? dashboardBaseDirectory,
        string? source,
        out ImageAssetCapabilities capabilities)
    {
        capabilities = new ImageAssetCapabilities(
            string.Empty,
            UsesIntrinsicColor: true,
            IsAnimated: false,
            SupportsAlpha: true,
            HasFrameTiming: false);

        if (string.IsNullOrWhiteSpace(dashboardBaseDirectory) || string.IsNullOrWhiteSpace(source))
            return false;

        try
        {
            var path = AssetPathResolver.ResolveExistingFile(
                dashboardBaseDirectory,
                source,
                "Dashboard image asset");
            capabilities = ReadCapabilities(path);
            return true;
        }
        catch
        {
            return false;
        }
    }
}

internal sealed class ImageAsset : IDisposable
{
    private const int MaximumFrameCount = 1000;
    private const long MaximumDecodedBytes = 128L * 1024L * 1024L;

    private readonly SKBitmap[] _frames;
    private readonly int[] _frameEndMs;
    private readonly int _totalDurationMs;
    private readonly long _decodedByteCount;
    private readonly long _loadedAtMs;

    private ImageAsset(SKBitmap[] frames, int[] frameEndMs, int totalDurationMs, long decodedByteCount)
    {
        _frames = frames;
        _frameEndMs = frameEndMs;
        _totalDurationMs = totalDurationMs;
        _decodedByteCount = decodedByteCount;
        _loadedAtMs = Environment.TickCount64;
    }

    public int Width => _frames[0].Width;
    public int Height => _frames[0].Height;
    public int FrameCount => _frames.Length;
    public bool IsAnimated => _frames.Length > 1;
    public long DecodedByteCount => _decodedByteCount;

    public static ImageAsset Load(string path)
    {
        using var stream = File.OpenRead(path);
        using var codec = SKCodec.Create(stream)
            ?? throw new InvalidDataException($"Skia could not decode image '{path}'.");

        var sourceInfo = codec.Info;
        if (sourceInfo.Width <= 0 || sourceInfo.Height <= 0)
            throw new InvalidDataException($"Image '{path}' has invalid dimensions.");

        var info = new SKImageInfo(
            sourceInfo.Width,
            sourceInfo.Height,
            SKColorType.Bgra8888,
            SKAlphaType.Premul);

        var frameCount = Math.Max(1, codec.FrameCount);
        if (frameCount > MaximumFrameCount)
        {
            throw new InvalidDataException(
                $"Image '{path}' contains {frameCount} frames; maximum supported is {MaximumFrameCount}.");
        }

        long decodedBytes;
        try
        {
            decodedBytes = checked((long)sourceInfo.Width * sourceInfo.Height * 4L * frameCount);
        }
        catch (OverflowException ex)
        {
            throw new InvalidDataException($"Image '{path}' decoded size is too large.", ex);
        }

        if (decodedBytes > MaximumDecodedBytes)
        {
            throw new InvalidDataException(
                $"Image '{path}' would require about {decodedBytes / (1024d * 1024d):F1} MiB decoded memory; " +
                $"maximum supported per asset is {MaximumDecodedBytes / (1024 * 1024)} MiB.");
        }

        var frames = new SKBitmap[frameCount];
        var frameEnds = new int[frameCount];
        long totalDuration = 0;

        try
        {
            for (var i = 0; i < frameCount; i++)
            {
                var bitmap = new SKBitmap(info);
                bitmap.Erase(SKColors.Transparent);

                var result = codec.GetPixels(
                    info,
                    bitmap.GetPixels(),
                    bitmap.RowBytes,
                    frameCount > 1 ? new SKCodecOptions(i) : SKCodecOptions.Default);

                if (result is not (SKCodecResult.Success or SKCodecResult.IncompleteInput))
                {
                    bitmap.Dispose();
                    throw new InvalidDataException(
                        $"Skia failed to decode frame {i} of '{path}': {result}.");
                }

                frames[i] = bitmap;

                var duration = 100;
                if (frameCount > 1 && codec.GetFrameInfo(i, out var frameInfo))
                    duration = frameInfo.Duration > 0 ? frameInfo.Duration : 100;

                totalDuration += duration;
                if (totalDuration > int.MaxValue)
                    throw new InvalidDataException($"Image '{path}' animation duration is too large.");
                frameEnds[i] = (int)totalDuration;
            }

            return new ImageAsset(frames, frameEnds, (int)totalDuration, decodedBytes);
        }
        catch
        {
            foreach (var frame in frames)
                frame?.Dispose();
            throw;
        }
    }

    public SKBitmap GetFrame(bool loop) =>
        GetFrameAtElapsed(Math.Max(0L, Environment.TickCount64 - _loadedAtMs), loop);

    public SKBitmap GetFrameAtElapsed(long elapsedMs, bool loop)
    {
        if (_frames.Length == 1)
            return _frames[0];

        return _frames[ResolveFrameIndex(_frameEndMs, _totalDurationMs, elapsedMs, loop)];
    }

    internal static int ResolveFrameIndex(
        IReadOnlyList<int> frameEndMs,
        int totalDurationMs,
        long elapsedMs,
        bool loop)
    {
        if (frameEndMs.Count == 0)
            throw new ArgumentException("Animation timeline must contain at least one frame.", nameof(frameEndMs));
        if (totalDurationMs <= 0)
            throw new ArgumentOutOfRangeException(nameof(totalDurationMs));

        var elapsed = Math.Max(0L, elapsedMs);
        int timeMs;
        if (loop)
            timeMs = (int)(elapsed % totalDurationMs);
        else
            timeMs = (int)Math.Min(elapsed, totalDurationMs - 1L);

        var low = 0;
        var high = frameEndMs.Count - 1;
        while (low < high)
        {
            var mid = low + (high - low) / 2;
            if (frameEndMs[mid] >= timeMs + 1)
                high = mid;
            else
                low = mid + 1;
        }

        return low;
    }

    public void Dispose()
    {
        foreach (var frame in _frames)
            frame.Dispose();
    }
}
