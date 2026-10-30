using CanonicalDashboard = PinkieSysMon.DashboardModel;

namespace PinkieSysMon;

/// <summary>
/// Rendering projection for the canonical schema-19 model. This is not a persistence alias:
/// it converts canonical presentation definitions into the small immutable render values used
/// by the existing Skia rendering engines.
/// </summary>
internal static class DashboardModelRenderContract
{
    public static TextPresentation GetTextPresentation(this CanonicalDashboard.WidgetDefinition widget) =>
        widget is CanonicalDashboard.ValueWidgetDefinition value
            ? value.TextPresentation.ToRenderProfile()
            : throw new InvalidOperationException($"Widget type '{widget.Type}' does not own direct text presentation.");

    public static TextPresentation ToRenderProfile(this CanonicalDashboard.TextPresentationDefinition definition) => new(
        definition.FontFamily,
        definition.FontSize,
        definition.FontWeight,
        definition.Italic,
        definition.Align,
        definition.VerticalAlign,
        definition.OutlineWidth,
        definition.OutlineColor,
        definition.OverflowMode,
        definition.ScrollSpeed,
        definition.BumpPauseMs);

    public static StateVisualProfile ToRenderProfile(this CanonicalDashboard.StateVisualProfileDefinition definition)
    {
        var valueContent = CanonicalDashboard.StateContentType.IsValue(definition.ContentType);
        return new StateVisualProfile(
            valueContent ? StateVisualProfileContract.SourceValue : definition.Asset.SourceType,
            valueContent ? string.Empty : definition.Asset.Source ?? string.Empty,
            definition.Color,
            definition.Opacity,
            valueContent ? null : definition.Asset.Fit,
            valueContent ? null : definition.Asset.Loop,
            definition.TextPresentation.ToRenderProfile());
    }

    public static StateVisualProfile GetStateVisualProfile(
        this CanonicalDashboard.StateVisualWidgetDefinition widget,
        string stateKey)
    {
        if (widget.Profiles.TryGetValue(stateKey, out var profile) && profile is not null)
            return profile.ToRenderProfile();

        throw new InvalidDataException(
            $"Widget '{widget.Id}' has no canonical state visual profile '{stateKey}'.");
    }

    public static IEnumerable<StateVisualProfile> EnumerateStateVisualProfiles(
        this CanonicalDashboard.StateVisualWidgetDefinition widget)
    {
        foreach (var spec in StateVisualProfileContract.GetSpecs(widget.Type).OrderBy(x => x.Order))
        {
            if (!widget.Profiles.TryGetValue(spec.Key, out var profile) || profile is null)
            {
                throw new InvalidDataException(
                    $"Widget '{widget.Id}' has no canonical state visual profile '{spec.Key}'.");
            }

            yield return profile.ToRenderProfile();
        }
    }

    public static bool UsesOnlyValueSources(this CanonicalDashboard.StateVisualWidgetDefinition widget) =>
        widget.Profiles.Count > 0 &&
        widget.Profiles.Values.All(profile => CanonicalDashboard.StateContentType.IsValue(profile.ContentType));

    public static CanonicalDashboard.StateVisualProfileDefinition GetStateVisualDefinition(
        this CanonicalDashboard.StateVisualWidgetDefinition widget,
        string stateKey)
    {
        if (widget.Profiles.TryGetValue(stateKey, out var profile) && profile is not null)
            return profile;

        throw new InvalidDataException(
            $"Widget '{widget.Id}' has no canonical state visual profile '{stateKey}'.");
    }
}
