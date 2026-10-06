using System.Globalization;
using SkiaSharp;
using PinkieSysMon;
using CanonicalWidgetDefinition = global::PinkieSysMon.DashboardModel.WidgetDefinition;
using CanonicalDashboard = PinkieSysMon.DashboardModel;

namespace PinkieSysMon.Widgets;

internal sealed class WidgetRenderContext : IDisposable
{
    private const long MaximumShadowCacheBytes = 64L * 1024L * 1024L;

    private readonly TypefaceCache _typefaces;
    private readonly Dictionary<CanonicalWidgetDefinition, ShadowRenderResources?> _shadowResources = new();
    private readonly Dictionary<CanonicalWidgetDefinition, ContainerRenderResources> _containerResources = new();
    private readonly Dictionary<CanonicalWidgetDefinition, string> _formattedMetricCache = new();
    private readonly Dictionary<NumericMetricCacheKey, double?> _doubleMetricCache = new();
    private IReadOnlyDictionary<string, object?> _metrics;
    private long _shadowCacheBytes;

    public WidgetRenderContext(
        IReadOnlyDictionary<string, object?> metrics,
        bool antialias,
        TypefaceCache typefaces)
    {
        _metrics = metrics;
        Antialias = antialias;
        _typefaces = typefaces;
    }

    public IReadOnlyDictionary<string, object?> Metrics => _metrics;
    public bool Antialias { get; }

    public void UpdateMetrics(IReadOnlyDictionary<string, object?> metrics)
    {
        // MetricStore snapshots are immutable after publication. Reuse formatted/numeric
        // conversions while the same snapshot instance is rendered across multiple frames.
        if (ReferenceEquals(_metrics, metrics))
            return;

        _metrics = metrics;
        _formattedMetricCache.Clear();
        _doubleMetricCache.Clear();
    }



    public bool TryResolveStateVisualProfile(
        CanonicalWidgetDefinition widget,
        out string stateKey,
        out StateVisualProfile profile)
    {
        return widget switch
        {
            CanonicalDashboard.BinaryWidgetDefinition binary =>
                TryResolveBinaryVisualProfile(binary, out stateKey, out profile),
            CanonicalDashboard.PowerWidgetDefinition power =>
                TryResolvePowerVisualProfile(power, out stateKey, out profile),
            CanonicalDashboard.MediaSystemWidgetDefinition mediaSystem =>
                TryResolveMediaSystemVisualProfile(mediaSystem, out stateKey, out profile),
            CanonicalDashboard.MediaPlayerWidgetDefinition mediaPlayer =>
                TryResolveMediaPlayerVisualProfile(mediaPlayer, out stateKey, out profile),
            _ => FailStateProfile(out stateKey, out profile)
        };
    }

    private static bool FailStateProfile(out string stateKey, out StateVisualProfile profile)
    {
        stateKey = string.Empty;
        profile = default;
        return false;
    }

    public string ResolveStateValueText(CanonicalWidgetDefinition widget, string stateKey) =>
        widget is CanonicalDashboard.BinaryWidgetDefinition binary
            ? FormatMetric(binary)
            : stateKey;

    public bool TryResolveBinaryVisualProfile(
        CanonicalDashboard.BinaryWidgetDefinition widget,
        out StateVisualProfile profile) =>
        TryResolveBinaryVisualProfile(widget, out _, out profile);

    public bool TryResolveBinaryVisualProfile(
        CanonicalDashboard.BinaryWidgetDefinition widget,
        out string stateKey,
        out StateVisualProfile profile)
    {
        stateKey = string.Empty;
        profile = default;
        if (string.IsNullOrWhiteSpace(widget.Metric) ||
            !Metrics.TryGetValue(widget.Metric, out var raw))
        {
            return false;
        }

        object? evaluationValue = raw;
        if (string.Equals(
                widget.EvaluationMode,
                BinarySignalContract.EvaluationModeSetpoint,
                StringComparison.OrdinalIgnoreCase))
        {
            var descriptor = MetricContract.GetDescriptor(widget.Metric);
            if (descriptor is not null && MetricContract.IsNumericValueKind(descriptor.ValueKind))
            {
                if (!TryGetMetricDouble(widget, out var converted))
                    return false;
                evaluationValue = converted;
            }
        }

        if (!BinarySignalContract.TryResolve(
                evaluationValue,
                widget.EvaluationMode,
                widget.Setpoint,
                widget.TrueIf,
                out var state))
        {
            return false;
        }

        stateKey = state ? BinarySignalContract.TrueKey : BinarySignalContract.FalseKey;
        profile = widget.GetStateVisualProfile(stateKey);
        return true;
    }


    public bool TryResolvePowerVisualProfile(
        CanonicalDashboard.PowerWidgetDefinition widget,
        out StateVisualProfile profile) =>
        TryResolvePowerVisualProfile(widget, out _, out profile);

    public bool TryResolvePowerVisualProfile(
        CanonicalDashboard.PowerWidgetDefinition widget,
        out string stateKey,
        out StateVisualProfile profile)
    {
        var stateMetric = PowerMetricContract.StateMetric(widget.PowerSource);
        Metrics.TryGetValue(stateMetric, out var raw);
        stateKey = PowerMetricContract.NormalizeState(raw);
        profile = widget.GetStateVisualProfile(stateKey);
        return true;
    }


    public bool TryResolveMediaSystemVisualProfile(
        CanonicalDashboard.MediaSystemWidgetDefinition widget,
        out StateVisualProfile profile) =>
        TryResolveMediaSystemVisualProfile(widget, out _, out profile);

    public bool TryResolveMediaSystemVisualProfile(
        CanonicalDashboard.MediaSystemWidgetDefinition widget,
        out string stateKey,
        out StateVisualProfile profile)
    {
        var input = string.Equals(
            widget.MediaSource,
            MediaMetricContract.InputSource,
            StringComparison.OrdinalIgnoreCase);
        var availableMetric = input
            ? MediaMetricContract.InputAvailable
            : MediaMetricContract.OutputAvailable;
        var typeMetric = input
            ? MediaMetricContract.InputType
            : MediaMetricContract.OutputType;

        var available = Metrics.TryGetValue(availableMetric, out var availableValue) &&
                        availableValue is true;
        Metrics.TryGetValue(typeMetric, out var rawType);
        stateKey = available
            ? StateVisualProfileContract.ResolveKey(
                widget.Type,
                rawType ?? MediaMetricContract.EndpointUnknown)
            : StateVisualProfileContract.UnavailableKey;
        profile = widget.GetStateVisualProfile(stateKey);
        return true;
    }


    public bool TryResolveMediaPlayerVisualProfile(
        CanonicalDashboard.MediaPlayerWidgetDefinition widget,
        out StateVisualProfile profile) =>
        TryResolveMediaPlayerVisualProfile(widget, out _, out profile);

    public bool TryResolveMediaPlayerVisualProfile(
        CanonicalDashboard.MediaPlayerWidgetDefinition widget,
        out string stateKey,
        out StateVisualProfile profile)
    {
        Metrics.TryGetValue(MediaMetricContract.PlaybackStatus, out var raw);
        stateKey = StateVisualProfileContract.ResolveKey(widget.Type, raw);
        profile = widget.GetStateVisualProfile(stateKey);
        return true;
    }


    public string ResolveValueText(CanonicalDashboard.ValueWidgetDefinition widget) =>
        CanonicalDashboard.ValueSourceKind.IsText(widget.SourceKind)
            ? FormatLiteralText(widget)
            : FormatMetric(widget);

    private static string FormatLiteralText(CanonicalDashboard.ValueWidgetDefinition widget)
    {
        var literal = widget.Text ?? string.Empty;
        if (!LiteralNumericContract.TryParse(literal, out var numericValue))
            return widget.Prefix + literal + widget.Suffix;

        try
        {
            var descriptor = LiteralNumericContract.GetDescriptor(widget.SourceUnit);
            var text = MetricValueFormatter.Format(
                numericValue,
                descriptor,
                widget.Unit,
                widget.Format,
                CultureInfo.CurrentCulture);
            return widget.Prefix + text + widget.Suffix;
        }
        catch (FormatException)
        {
            return widget.Prefix + widget.Fallback + widget.Suffix;
        }
        catch (InvalidCastException)
        {
            return widget.Prefix + widget.Fallback + widget.Suffix;
        }
        catch (OverflowException)
        {
            return widget.Prefix + widget.Fallback + widget.Suffix;
        }
        catch (InvalidOperationException)
        {
            return widget.Prefix + widget.Fallback + widget.Suffix;
        }
    }

    public ContainerRenderResources GetContainerResources(CanonicalWidgetDefinition widget)
    {
        if (_containerResources.TryGetValue(widget, out var cached))
            return cached;

        var created = ContainerRenderResources.Create(widget, Antialias);
        _containerResources.Add(widget, created);
        return created;
    }

    public ShadowRenderResources? GetShadowResources(CanonicalWidgetDefinition widget)
    {
        if (_shadowResources.TryGetValue(widget, out var cached))
            return cached;

        var created = ShadowRenderResources.Create(this, widget, Antialias);
        _shadowResources.Add(widget, created);
        return created;
    }

    public void InvalidateGeometry(CanonicalWidgetDefinition widget)
    {
        if (!_shadowResources.Remove(widget, out var shadow))
            return;

        shadow?.Dispose();
    }

    private bool TryResizeShadowCache(long previousBytes, long nextBytes)
    {
        var projected = _shadowCacheBytes - previousBytes + nextBytes;
        if (projected < 0 || projected > MaximumShadowCacheBytes)
            return false;

        _shadowCacheBytes = projected;
        return true;
    }

    private void ReleaseShadowCache(long bytes)
    {
        if (bytes <= 0)
            return;

        _shadowCacheBytes = Math.Max(0, _shadowCacheBytes - bytes);
    }


    public bool TryGetShadowContentKey(
        CanonicalWidgetDefinition widget,
        IWidgetRenderer renderer,
        out ShadowContentKey key)
    {
        if (renderer is IWidgetShadowCacheKeyProvider provider)
            return provider.TryGetShadowContentKey(widget, this, out key);

        switch (widget)
        {
            case CanonicalDashboard.ValueWidgetDefinition value:
                if (ValueOverflowContract.IsAnimated(value.TextPresentation.OverflowMode))
                {
                    key = default;
                    return false;
                }

                key = ShadowContentKey.ForText(ResolveValueText(value));
                return true;

            case CanonicalDashboard.BarWidgetDefinition bar:
                if (BarImageContract.IsImageMode(bar.ContentMode))
                {
                    key = default;
                    return false;
                }

                key = TryGetMetricDouble(bar, out var barValue)
                    ? ShadowContentKey.ForNumber(barValue)
                    : ShadowContentKey.Unavailable;
                return true;

            case CanonicalDashboard.GaugeWidgetDefinition gauge:
                key = TryGetMetricDouble(gauge, out var gaugeValue)
                    ? ShadowContentKey.ForNumber(gaugeValue)
                    : ShadowContentKey.Unavailable;
                return true;

            default:
                key = default;
                return false;
        }
    }

    public readonly record struct ShadowContentKey(byte Kind, string? Text, string? State, long NumberBits)
    {
        private const byte TextKind = 1;
        private const byte NumberKind = 2;
        private const byte UnavailableKind = 3;
        private const byte StateKind = 4;
        private const byte StateTextKind = 5;

        public static ShadowContentKey Unavailable { get; } = new(UnavailableKind, null, null, 0);
        public static ShadowContentKey ForText(string text) => new(TextKind, text, null, 0);
        public static ShadowContentKey ForNumber(double value) =>
            new(NumberKind, null, null, BitConverter.DoubleToInt64Bits(value));
        public static ShadowContentKey ForState(string state) => new(StateKind, null, state, 0);
        public static ShadowContentKey ForStateText(string state, string text) =>
            new(StateTextKind, text, state, 0);
    }

    public void Dispose()
    {
        foreach (var resources in _shadowResources.Values)
            resources?.Dispose();
        _shadowResources.Clear();
        _shadowCacheBytes = 0;

        foreach (var resources in _containerResources.Values)
            resources.Dispose();
        _containerResources.Clear();
    }

    internal sealed class ContainerRenderResources : IDisposable
    {
        private ContainerRenderResources(SKPaint? backgroundPaint, SKPaint? borderPaint, SKRoundRect clipRoundRect)
        {
            BackgroundPaint = backgroundPaint;
            BorderPaint = borderPaint;
            ClipRoundRect = clipRoundRect;
        }

        public SKPaint? BackgroundPaint { get; }
        public SKPaint? BorderPaint { get; }
        public SKRoundRect ClipRoundRect { get; }

        public static ContainerRenderResources Create(CanonicalWidgetDefinition widget, bool antialias)
        {
            SKPaint? background = null;
            SKPaint? border = null;
            SKRoundRect? clipRoundRect = null;
            try
            {
                var backgroundColor = ColorParser.Parse(widget.BackgroundColor);
                if (backgroundColor.Alpha > 0)
                {
                    background = new SKPaint
                    {
                        IsAntialias = antialias,
                        Color = backgroundColor,
                        Style = SKPaintStyle.Fill
                    };
                }

                if (widget.BorderWidth > 0f)
                {
                    var borderColor = ColorParser.Parse(widget.BorderColor);
                    if (borderColor.Alpha > 0)
                    {
                        border = new SKPaint
                        {
                            IsAntialias = antialias,
                            Color = borderColor,
                            Style = SKPaintStyle.Stroke,
                            StrokeWidth = widget.BorderWidth,
                            StrokeJoin = SKStrokeJoin.Round
                        };
                    }
                }

                clipRoundRect = new SKRoundRect();
                return new ContainerRenderResources(background, border, clipRoundRect);
            }
            catch
            {
                clipRoundRect?.Dispose();
                background?.Dispose();
                border?.Dispose();
                throw;
            }
        }

        public void Dispose()
        {
            ClipRoundRect.Dispose();
            BackgroundPaint?.Dispose();
            BorderPaint?.Dispose();
        }
    }

    internal sealed class ShadowRenderResources : IDisposable
    {
        private readonly WidgetRenderContext _owner;
        private SKImage? _cachedComposite;
        private SKRect _cachedBounds;
        private ShadowContentKey _cachedKey;
        private bool _hasCachedKey;
        private float _cachedWidgetX;
        private float _cachedWidgetY;
        private long _cachedBytes;

        private ShadowRenderResources(WidgetRenderContext owner, SKImageFilter filter, SKPaint layerPaint)
        {
            _owner = owner;
            Filter = filter;
            LayerPaint = layerPaint;
        }

        private SKImageFilter Filter { get; }
        public SKPaint LayerPaint { get; }

        public static ShadowRenderResources? Create(
            WidgetRenderContext owner,
            CanonicalWidgetDefinition widget,
            bool antialias)
        {
            if (!widget.ShadowEnabled || widget.ShadowOpacity <= 0f)
                return null;

            var source = ColorParser.Parse(widget.ShadowColor);
            if (source.Alpha == 0)
                return null;

            var alpha = (byte)Math.Clamp(
                (int)Math.Round(source.Alpha * widget.ShadowOpacity),
                0,
                255);
            var effectiveColor = new SKColor(source.Red, source.Green, source.Blue, alpha);
            var filter = SKImageFilter.CreateDropShadow(
                widget.ShadowOffsetX,
                widget.ShadowOffsetY,
                widget.ShadowBlur,
                widget.ShadowBlur,
                effectiveColor)
                ?? throw new InvalidOperationException("Skia could not create the widget shadow filter.");
            SKPaint? paint = null;
            try
            {
                paint = new SKPaint
                {
                    IsAntialias = antialias,
                    ImageFilter = filter
                };
                return new ShadowRenderResources(owner, filter, paint!);
            }
            catch
            {
                paint?.Dispose();
                filter.Dispose();
                throw;
            }
        }

        public bool TryDrawCachedComposite(
            SKCanvas canvas,
            CanonicalWidgetDefinition widget,
            WidgetRenderContext context,
            IWidgetRenderer renderer)
        {
            if (!context.TryGetShadowContentKey(widget, renderer, out var key))
            {
                ClearCompositeCache();
                return false;
            }

            if (_cachedComposite is null || !_hasCachedKey || !_cachedKey.Equals(key))
            {
                if (!RebuildComposite(widget, context, renderer, key))
                    return false;
            }

            // Cached composites are rasterized in widget-local translation state. Editor drag and
            // alignment mutate X/Y without rebuilding the preview renderer, so draw the cached
            // image at the matching translated position instead of leaving it at the build origin.
            var drawX = _cachedBounds.Left + (widget.X - _cachedWidgetX);
            var drawY = _cachedBounds.Top + (widget.Y - _cachedWidgetY);
            canvas.DrawImage(_cachedComposite!, drawX, drawY, SKSamplingOptions.Default);
            return true;
        }

        private bool RebuildComposite(
            CanonicalWidgetDefinition widget,
            WidgetRenderContext context,
            IWidgetRenderer renderer,
            ShadowContentKey key)
        {
            var visualBounds = WidgetGeometry.GetRotatedVisualBounds(widget, context);
            var left = MathF.Floor(visualBounds.Left);
            var top = MathF.Floor(visualBounds.Top);
            var right = MathF.Ceiling(visualBounds.Right);
            var bottom = MathF.Ceiling(visualBounds.Bottom);
            var width = Math.Max(1, (int)(right - left));
            var height = Math.Max(1, (int)(bottom - top));
            long decodedBytes;
            try
            {
                decodedBytes = checked((long)width * height * 4L);
            }
            catch (OverflowException)
            {
                ClearCompositeCache();
                return false;
            }

            if (!_owner.TryResizeShadowCache(_cachedBytes, decodedBytes))
            {
                ClearCompositeCache();
                return false;
            }

            try
            {
                using var surface = SKSurface.Create(new SKImageInfo(
                width,
                height,
                SKColorType.Bgra8888,
                    SKAlphaType.Premul))
                    ?? throw new InvalidOperationException("Failed to create widget shadow cache surface.");

                var cacheCanvas = surface.Canvas;
                cacheCanvas.Clear(SKColors.Transparent);
                cacheCanvas.Translate(-left, -top);
                WidgetEffects.RenderShadowUncached(cacheCanvas, widget, context, renderer, LayerPaint);
                cacheCanvas.Flush();

                var replacement = surface.Snapshot();
                var previous = _cachedComposite;
                _cachedComposite = replacement;
                _cachedBounds = new SKRect(left, top, right, bottom);
                _cachedKey = key;
                _hasCachedKey = true;
                _cachedWidgetX = widget.X;
                _cachedWidgetY = widget.Y;
                _cachedBytes = decodedBytes;
                previous?.Dispose();
                return true;
            }
            catch
            {
                _owner.TryResizeShadowCache(decodedBytes, _cachedBytes);
                throw;
            }
        }

        private void ClearCompositeCache()
        {
            _cachedComposite?.Dispose();
            _cachedComposite = null;
            _hasCachedKey = false;
            _owner.ReleaseShadowCache(_cachedBytes);
            _cachedBytes = 0;
        }

        public void Dispose()
        {
            ClearCompositeCache();
            LayerPaint.Dispose();
            Filter.Dispose();
        }
    }

    public string FormatMetric(CanonicalDashboard.ValueWidgetDefinition widget) =>
        FormatMetricCore(widget, widget.Metric, widget.Unit, widget.Format, widget.Prefix, widget.Suffix, widget.Fallback);

    public string FormatMetric(CanonicalDashboard.BinaryWidgetDefinition widget) =>
        FormatMetricCore(widget, widget.Metric, widget.Unit, widget.Format, widget.Prefix, widget.Suffix, widget.Fallback);

    private string FormatMetricCore(
        CanonicalWidgetDefinition cacheKey,
        string? metric,
        string? unit,
        string? format,
        string prefix,
        string suffix,
        string fallback)
    {
        if (_formattedMetricCache.TryGetValue(cacheKey, out var cached))
            return cached;

        string formatted;
        if (string.IsNullOrWhiteSpace(metric) || !Metrics.TryGetValue(metric, out var value) || value is null)
        {
            formatted = prefix + fallback + suffix;
        }
        else
        {
            var descriptor = MetricContract.GetDescriptor(metric);
            if (descriptor is null)
            {
                formatted = prefix + (Convert.ToString(value, CultureInfo.CurrentCulture) ?? fallback) + suffix;
            }
            else
            {
                try
                {
                    var text = MetricValueFormatter.Format(value, descriptor, unit, format, CultureInfo.CurrentCulture);
                    formatted = prefix + text + suffix;
                }
                catch (FormatException)
                {
                    formatted = prefix + fallback + suffix;
                }
                catch (InvalidCastException)
                {
                    formatted = prefix + fallback + suffix;
                }
                catch (OverflowException)
                {
                    formatted = prefix + fallback + suffix;
                }
                catch (InvalidOperationException)
                {
                    formatted = prefix + fallback + suffix;
                }
            }
        }

        _formattedMetricCache[cacheKey] = formatted;
        return formatted;
    }


    private static bool FailMetricDouble(out double value)
    {
        value = default;
        return false;
    }

    public bool TryGetMetricDouble(CanonicalDashboard.BinaryWidgetDefinition widget, out double value) =>
        TryGetMetricDouble(widget.Metric, widget.Unit, out value);

    public bool TryGetMetricDouble(CanonicalDashboard.QuantitativeWidgetDefinition widget, out double value) =>
        TryGetMetricDouble(widget.Metric, widget.Unit, out value);

    public bool TryGetMetricDouble(string? metric, string? unit, out double value)
    {
        if (string.IsNullOrWhiteSpace(metric))
        {
            value = default;
            return false;
        }

        var normalizedUnit = string.IsNullOrWhiteSpace(unit) ||
                             unit.Equals("raw", StringComparison.OrdinalIgnoreCase)
            ? null
            : unit.Trim();
        var key = new NumericMetricCacheKey(metric, normalizedUnit);
        if (_doubleMetricCache.TryGetValue(key, out var cached))
        {
            value = cached.GetValueOrDefault();
            return cached.HasValue;
        }

        double? converted = null;
        if (Metrics.TryGetValue(metric, out var raw) && raw is not null)
        {
            var descriptor = MetricContract.GetDescriptor(metric);
            if (descriptor is not null && MetricContract.IsNumericValueKind(descriptor.ValueKind))
            {
                if (MetricValueConverter.TryConvertNumeric(raw, descriptor, normalizedUnit, out var candidate))
                    converted = candidate;
            }
            else if (normalizedUnit is null)
            {
                try
                {
                    var candidate = Convert.ToDouble(raw, CultureInfo.InvariantCulture);
                    if (double.IsFinite(candidate))
                        converted = candidate;
                }
                catch (FormatException)
                {
                }
                catch (InvalidCastException)
                {
                }
                catch (OverflowException)
                {
                }
            }
        }

        _doubleMetricCache[key] = converted;
        value = converted.GetValueOrDefault();
        return converted.HasValue;
    }

    public bool TryGetMetricDouble(string metric, out double value) =>
        TryGetMetricDouble(metric, unit: null, out value);

    private readonly struct NumericMetricCacheKey : IEquatable<NumericMetricCacheKey>
    {
        public NumericMetricCacheKey(string metric, string? unit)
        {
            Metric = metric;
            Unit = unit;
        }

        public string Metric { get; }
        public string? Unit { get; }

        public bool Equals(NumericMetricCacheKey other) =>
            string.Equals(Metric, other.Metric, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(Unit, other.Unit, StringComparison.OrdinalIgnoreCase);

        public override bool Equals(object? obj) => obj is NumericMetricCacheKey other && Equals(other);

        public override int GetHashCode() =>
            HashCode.Combine(
                StringComparer.OrdinalIgnoreCase.GetHashCode(Metric),
                Unit is null ? 0 : StringComparer.OrdinalIgnoreCase.GetHashCode(Unit));
    }

    public SKTypeface GetTypeface(TextPresentation presentation) => _typefaces.Get(presentation);
}
