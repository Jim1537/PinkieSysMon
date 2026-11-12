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

    private readonly EditorArtworkRecovery? _recovery;
    private ImageAsset? _unavailable;

    public ImageAssetCache(string baseDirectory, EditorArtworkRecovery? recovery = null)
    {
        _baseDirectory = baseDirectory;
        _recovery = recovery;
    }


    public void Preload(IEnumerable<CanonicalDashboard.WidgetDefinition> widgets)
    {
        foreach (var widget in widgets)
        {
            try
            {
                foreach (var layer in new[] { widget.PanelBackground, widget.PanelForeground })
                {
                    if (layer.Mode == CanonicalDashboard.WidgetPanelLayerMode.Image &&
                        !string.IsNullOrWhiteSpace(layer.Image.Source))
                        _ = Get(layer.Image.Source!);
                }

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

                    case CanonicalDashboard.GraphWidgetDefinition graph
                        when CanonicalDashboard.ImageAssetSourceType.IsFile(graph.Image.Asset.SourceType) &&
                             !string.IsNullOrWhiteSpace(graph.Image.Asset.Source):
                        _ = Get(graph.Image.Asset.Source!);
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
            catch (Exception error) { RenderFailureContext.Add(error, "image.preload", widget.Id); throw; }
        }
    }

    internal int ResourceCount => _assetsByPath.Count;
    internal long DecodedBytes => _aggregateDecodedBytes;
    public bool HasAnimatedAssets => _assetsByPath.Values.Any(asset => asset.IsAnimated);

    public ImageAsset Get(string source)
    {
        try { return GetCore(source); }
        catch (Exception error) { RenderFailureContext.Add(error, "image.load"); throw; }
    }

    private ImageAsset GetCore(string source)
    {
        var sourceKey = NormalizeSourceKey(source);
        if (_assetsBySource.TryGetValue(sourceKey, out var cachedBySource))
            return cachedBySource;

        var fullPath = CanonicalDashboard.ImageAssetPresentationDefinition.ResolveReference(_baseDirectory,
            CanonicalDashboard.ImageAssetSourceType.File, sourceKey, "Dashboard artwork", _recovery is not null);
        if (fullPath is null) return CacheUnavailable(sourceKey);
        if (!_assetsByPath.TryGetValue(fullPath, out var asset))
        {
            asset = _recovery is null ? ImageAsset.Load(fullPath) : _recovery.Decode(fullPath, ImageAsset.Load);
            if (asset is null) return CacheUnavailable(sourceKey);
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

    private ImageAsset CacheUnavailable(string key)
    {
        _recovery!.Record(key);
        return _assetsBySource[key] = _unavailable ??= ImageAsset.CreateTransparent();
    }

    public void Dispose()
    {
        _unavailable?.Dispose();
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
    // Derived raster frames are separate from the decoded source budget.
    // One quarter of each asset, capped at 16 MiB, bounds total derived memory
    // to 25% of the corresponding decoded frames across all image/icon caches.
    private const long MaximumScaledCacheBytes = 16L * 1024L * 1024L;
    private static readonly SKSamplingOptions LinearSampling = new(SKFilterMode.Linear);

    private sealed record ScaledFrame(
        SKBitmap Bitmap,
        LinkedListNode<(int Frame, int Width, int Height)> Node,
        long Bytes);

    private readonly object _scaledSync = new();
    private readonly Dictionary<(int Frame, int Width, int Height), ScaledFrame> _scaledFrames = new();
    private readonly LinkedList<(int Frame, int Width, int Height)> _scaledLru = new();
    private long _scaledBytes;

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

    internal long ScaledFrameBudget => Math.Min(MaximumScaledCacheBytes, _decodedByteCount / 4);
    internal long ScaledFrameBytes
    {
        get { lock (_scaledSync) return _scaledBytes; }
    }

    internal int ScaledFrameCount
    {
        get { lock (_scaledSync) return _scaledFrames.Count; }
    }

    // Takes ownership only after validating the same per-image decoded-memory contract.
    internal static ImageAsset FromBitmap(SKBitmap bitmap)
    {
        ArgumentNullException.ThrowIfNull(bitmap);
        var bytes = checked((long)bitmap.RowBytes * bitmap.Height);
        if (bitmap.Width <= 0 || bitmap.Height <= 0 || bytes > MaximumDecodedBytes)
            throw new InvalidDataException("Extracted image exceeds the supported image dimensions or memory budget.");
        return new ImageAsset([bitmap], [0], 0, bytes);
    }

    internal static ImageAsset CreateTransparent()
    {
        var pixel = new SKBitmap(1, 1);
        pixel.Erase(SKColors.Transparent);
        return FromBitmap(pixel);
    }

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

                if (result != SKCodecResult.Success)
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


    // Every file-backed image and raster icon, including Canvas/Panel/Graph/state
    // layers and Bar progress images, enters the same CPU raster sampling path.
    // This performs explicit low-pass preparation instead of relying on Skia to
    // synthesize mipmaps for SKBitmap drawing.
    internal void Draw(
        SKCanvas canvas,
        SKRect destination,
        bool loop,
        SKPaint? paint = null,
        long? elapsedMs = null)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        if (destination.Width <= 0f || destination.Height <= 0f)
            return;

        var elapsed = elapsedMs ?? Math.Max(0L, Environment.TickCount64 - _loadedAtMs);
        var index = _frames.Length == 1 ? 0 : ResolveFrameIndex(_frameEndMs, _totalDurationMs, elapsed, loop);
        var matrix = canvas.TotalMatrix;
        var renderedWidth = destination.Width * MathF.Sqrt(
            matrix.ScaleX * matrix.ScaleX + matrix.SkewY * matrix.SkewY);
        var renderedHeight = destination.Height * MathF.Sqrt(
            matrix.SkewX * matrix.SkewX + matrix.ScaleY * matrix.ScaleY);
        var width = PrefilterDimension(Width, renderedWidth);
        var height = PrefilterDimension(Height, renderedHeight);

        // Drawing while holding this asset's lock prevents a concurrent cache
        // eviction or Dispose from freeing the SKBitmap used by Skia.
        lock (_scaledSync)
        {
            var original = _frames[index];
            // If a prepared frame would exceed the bounded cache, prefer
            // direct bilinear drawing over repeated uncached CPU rescaling.
            if (checked((long)width * height * original.Info.BytesPerPixel) > ScaledFrameBudget)
            {
                width = original.Width;
                height = original.Height;
            }
            var bitmap = original;
            var temporary = false;
            if (width != original.Width || height != original.Height)
            {
                var key = (Frame: index, Width: width, Height: height);
                if (_scaledFrames.TryGetValue(key, out var cached))
                {
                    _scaledLru.Remove(cached.Node);
                    _scaledLru.AddFirst(cached.Node);
                    bitmap = cached.Bitmap;
                }
                else
                {
                    bitmap = PrepareScaledFrame(original, width, height);
                    var bytes = checked((long)bitmap.RowBytes * bitmap.Height);
                    var budget = ScaledFrameBudget;
                    if (bytes > budget)
                    {
                        temporary = true;
                    }
                    else
                    {
                        while (_scaledBytes > budget - bytes)
                        {
                            var oldest = _scaledLru.Last!;
                            var evicted = _scaledFrames[oldest.Value];
                            _scaledFrames.Remove(oldest.Value);
                            _scaledLru.RemoveLast();
                            _scaledBytes -= evicted.Bytes;
                            evicted.Bitmap.Dispose();
                        }

                        var node = _scaledLru.AddFirst(key);
                        _scaledFrames.Add(key, new ScaledFrame(bitmap, node, bytes));
                        _scaledBytes += bytes;
                    }
                }
            }

            try
            {
                // A native-resolution, integer-aligned blit should not blur
                // pixel art or trigger an unnecessary resampling pass.
                var sampling = ReferenceEquals(bitmap, original) &&
                    IsAlignedNativeCopy(matrix, destination, original.Width, original.Height)
                    ? SKSamplingOptions.Default
                    : LinearSampling;
                canvas.DrawBitmap(bitmap, destination, sampling, paint);
            }
            finally
            {
                if (temporary)
                    bitmap.Dispose();
            }
        }
    }

    private static int PrefilterDimension(int sourcePixels, float devicePixels)
    {
        if (!float.IsFinite(devicePixels) || devicePixels <= 0f)
            return sourcePixels;

        // A final bilinear draw is sufficient for mild reductions. Explicit
        // multi-stage low-pass filtering is reserved for strong reductions,
        // where a prepared frame can be reused at a bounded memory cost.
        if (sourcePixels < 4f * devicePixels)
            return sourcePixels;

        var width = sourcePixels;
        while (width > 1 && width > 1.5f * devicePixels)
            width = Math.Max(1, width / 2);
        return width;
    }

    private static SKBitmap PrepareScaledFrame(SKBitmap source, int width, int height)
    {
        var current = source;
        try
        {
            while (current.Width > width || current.Height > height)
            {
                var nextWidth = Math.Max(width, (current.Width + 1) / 2);
                var nextHeight = Math.Max(height, (current.Height + 1) / 2);
                var next = new SKBitmap(current.Info.WithSize(nextWidth, nextHeight));
                if (!current.ScalePixels(next, LinearSampling))
                {
                    next.Dispose();
                    throw new InvalidOperationException("Skia failed to prepare a reduced raster frame.");
                }

                if (!ReferenceEquals(current, source))
                    current.Dispose();
                current = next;
            }

            return current;
        }
        catch
        {
            if (!ReferenceEquals(current, source))
                current.Dispose();
            throw;
        }
    }

    private static bool IsAlignedNativeCopy(SKMatrix matrix, SKRect destination, int width, int height)
    {
        if (MathF.Abs(matrix.SkewX) > 0.0001f || MathF.Abs(matrix.SkewY) > 0.0001f)
            return false;

        var x = matrix.ScaleX * destination.Left + matrix.TransX;
        var y = matrix.ScaleY * destination.Top + matrix.TransY;
        return MathF.Abs(MathF.Abs(matrix.ScaleX * destination.Width) - width) < 0.001f &&
               MathF.Abs(MathF.Abs(matrix.ScaleY * destination.Height) - height) < 0.001f &&
               MathF.Abs(x - MathF.Round(x)) < 0.001f &&
               MathF.Abs(y - MathF.Round(y)) < 0.001f;
    }

    public void Dispose()
    {
        lock (_scaledSync)
        {
            foreach (var scaled in _scaledFrames.Values)
                scaled.Bitmap.Dispose();
            _scaledFrames.Clear();
            _scaledLru.Clear();
            _scaledBytes = 0;

            foreach (var frame in _frames)
                frame.Dispose();
        }
    }
}

