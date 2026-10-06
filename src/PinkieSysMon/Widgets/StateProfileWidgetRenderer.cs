using SkiaSharp;
using CanonicalWidgetDefinition = global::PinkieSysMon.DashboardModel.WidgetDefinition;
using CanonicalDashboard = PinkieSysMon.DashboardModel;

namespace PinkieSysMon.Widgets;

internal abstract class StateProfileWidgetRenderer : IWidgetRenderer, IWidgetShadowCacheKeyProvider, IDisposable
{
    private readonly StateIconVisualCache _visuals;
    private readonly TextContentRenderer _text = new();
    private readonly StateAnimationPlaybackTracker _playback = new();
    private readonly Func<long> _clock;

    protected StateProfileWidgetRenderer(
        string type,
        ImageAssetCache images,
        IconAssetCache icons,
        Func<long>? clock = null)
    {
        Type = type;
        _visuals = new StateIconVisualCache(images, icons);
        _clock = clock ?? (() => Environment.TickCount64);
    }

    public string Type { get; }


    public void Render(SKCanvas canvas, CanonicalWidgetDefinition widget, WidgetRenderContext context)
    {
        if (!WidgetTypeContract.Is(widget.Type, Type) ||
            !context.TryResolveStateVisualProfile(widget, out var stateKey, out var profile))
        {
            _playback.Deactivate(widget);
            return;
        }

        if (StateVisualProfileContract.IsValueSource(profile.SourceType))
        {
            _playback.Deactivate(widget);
            var stateWidget = widget as CanonicalDashboard.StateVisualWidgetDefinition
                ?? throw new InvalidDataException($"State renderer received non-state widget type '{widget.Type}'.");
            _text.Render(
                canvas,
                widget,
                context,
                context.ResolveStateValueText(widget, stateKey),
                profile.Color,
                profile.Opacity,
                stateWidget.UsesOnlyValueSources()
                    ? TextContentLayoutMode.AutoHeightContainer
                    : TextContentLayoutMode.Container,
                profile.Text ?? throw new InvalidDataException(
                    $"{StateVisualProfileContract.GetWidgetDisplayName(Type)} widget '{widget.Id}' active value state requires a text presentation profile."));
            return;
        }

        var fit = profile.Fit ?? StateVisualProfileContract.FitContain;
        var loop = profile.Loop ?? true;
        if (_visuals.UsesActivationRelativePlayback(profile))
        {
            var elapsedMs = _playback.GetElapsedMs(
                widget,
                stateKey,
                profile.Source,
                _clock());
            _visuals.DrawAtElapsed(
                canvas,
                widget,
                context,
                profile,
                elapsedMs,
                fit,
                loop);
            return;
        }

        _playback.Deactivate(widget);
        _visuals.Draw(
            canvas,
            widget,
            context,
            profile,
            fit,
            loop);
    }

    public bool TryGetShadowContentKey(
        global::PinkieSysMon.DashboardModel.WidgetDefinition widget,
        WidgetRenderContext context,
        out WidgetRenderContext.ShadowContentKey key)
    {
        if (!WidgetTypeContract.Is(widget.Type, Type) ||
            !context.TryResolveStateVisualProfile(widget, out var stateKey, out var profile))
        {
            key = WidgetRenderContext.ShadowContentKey.Unavailable;
            return true;
        }

        if (StateVisualProfileContract.IsValueSource(profile.SourceType))
        {
            if (profile.Text is { } text && ValueOverflowContract.IsAnimated(text.OverflowMode))
            {
                key = default;
                return false;
            }

            key = WidgetTypeContract.Is(widget.Type, WidgetTypeContract.Binary)
                ? WidgetRenderContext.ShadowContentKey.ForStateText(
                    stateKey,
                    context.ResolveStateValueText(widget, stateKey))
                : WidgetRenderContext.ShadowContentKey.ForState(stateKey);
            return true;
        }

        if (_visuals.IsAnimated(profile))
        {
            key = default;
            return false;
        }

        key = WidgetRenderContext.ShadowContentKey.ForState(stateKey);
        return true;
    }

    public void Dispose()
    {
        _playback.Clear();
        _text.Dispose();
        _visuals.Dispose();
    }
}
