using CanonicalWidgetDefinition = global::PinkieSysMon.DashboardModel.WidgetDefinition;
namespace PinkieSysMon.Widgets;

internal sealed class StateAnimationPlaybackTracker
{
    private readonly Dictionary<CanonicalWidgetDefinition, PlaybackState> _active = new();

    public long GetElapsedMs(
        CanonicalWidgetDefinition widget,
        string stateKey,
        string source,
        long nowMs)
    {
        var normalizedStateKey = stateKey?.Trim() ?? string.Empty;
        var normalizedSource = source?.Trim() ?? string.Empty;

        if (!_active.TryGetValue(widget, out var active) ||
            !string.Equals(active.StateKey, normalizedStateKey, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(active.Source, normalizedSource, StringComparison.OrdinalIgnoreCase) ||
            nowMs < active.StartedAtMs)
        {
            _active[widget] = new PlaybackState(normalizedStateKey, normalizedSource, nowMs);
            return 0L;
        }

        return nowMs - active.StartedAtMs;
    }

    public void Deactivate(CanonicalWidgetDefinition widget) =>
        _active.Remove(widget);

    public void Clear() =>
        _active.Clear();

    private readonly record struct PlaybackState(
        string StateKey,
        string Source,
        long StartedAtMs);
}
