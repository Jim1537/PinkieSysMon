using System.Text.Json.Serialization;

namespace PinkieSysMon;

internal readonly record struct StateVisualProfile(
    string SourceType,
    string Source,
    string? Color,
    float Opacity,
    string? Fit,
    bool? Loop,
    TextPresentation? Text);

internal sealed class StateVisualProfileDefinition
{
    [JsonPropertyName("sourceType")]
    public string SourceType { get; set; } = "icon";

    [JsonPropertyName("source")]
    public string Source { get; set; } = string.Empty;

    [JsonPropertyName("color")]
    public string? Color { get; set; }

    [JsonPropertyName("opacity")]
    public float Opacity { get; set; } = 1f;

    [JsonPropertyName("fit")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public string? Fit { get; set; }

    [JsonPropertyName("loop")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool? Loop { get; set; }

    [JsonPropertyName("text")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public TextPresentationDefinition? Text { get; set; }

    internal StateVisualProfile ToRenderProfile() =>
        new(SourceType, Source ?? string.Empty, Color, Opacity, Fit, Loop, Text?.ToRenderProfile());
}

internal sealed record StateVisualProfileSpec(
    string Key,
    string Category,
    int Order,
    string DefaultSource,
    string DefaultSourceType = "icon",
    string? DefaultColor = null,
    float DefaultOpacity = 1f,
    string? DefaultFit = null,
    bool? DefaultLoop = null,
    bool IncludeTextPresentation = false)
{
    public StateVisualProfileDefinition CreateDefault() => new()
    {
        SourceType = DefaultSourceType,
        Source = DefaultSource,
        Color = DefaultColor,
        Opacity = DefaultOpacity,
        Fit = DefaultFit,
        Loop = DefaultLoop,
        Text = IncludeTextPresentation ? new TextPresentationDefinition() : null
    };
}

internal static class StateVisualProfileContract
{
    public const string SourceIcon = "icon";
    public const string SourceFile = "file";
    public const string SourceValue = "value";
    public const string UnavailableKey = "unavailable";
    public const string FitContain = "contain";
    public const string FitCover = "cover";
    public const string FitStretch = "stretch";

    public static readonly IReadOnlyList<string> SupportedFits =
        [FitContain, FitCover, FitStretch];

    private static readonly string[] DefaultSourceTypes = [SourceIcon, SourceFile];
    private static readonly string[] StatefulContentSourceTypes = [SourceIcon, SourceFile, SourceValue];

    public static bool SupportsValueSource(string? widgetType) =>
        WidgetTypeContract.Is(widgetType, WidgetTypeContract.Binary) ||
        WidgetTypeContract.Is(widgetType, WidgetTypeContract.Power) ||
        WidgetTypeContract.Is(widgetType, WidgetTypeContract.MediaSystem) ||
        WidgetTypeContract.Is(widgetType, WidgetTypeContract.MediaPlayer);

    public static string GetWidgetDisplayName(string? widgetType)
    {
        if (WidgetTypeContract.Is(widgetType, WidgetTypeContract.Binary))
            return "Binary";
        if (WidgetTypeContract.Is(widgetType, WidgetTypeContract.Power))
            return "Power";
        if (WidgetTypeContract.Is(widgetType, WidgetTypeContract.MediaSystem))
            return "Media System";
        if (WidgetTypeContract.Is(widgetType, WidgetTypeContract.MediaPlayer))
            return "Media Player";
        return widgetType?.Trim() ?? string.Empty;
    }

    public static IReadOnlyList<string> GetSupportedSourceTypes(string? widgetType) =>
        SupportsValueSource(widgetType)
            ? StatefulContentSourceTypes
            : DefaultSourceTypes;

    public static bool IsIconSource(string? sourceType) =>
        string.IsNullOrWhiteSpace(sourceType) ||
        string.Equals(sourceType.Trim(), SourceIcon, StringComparison.OrdinalIgnoreCase);

    public static bool IsFileSource(string? sourceType) =>
        string.Equals(sourceType?.Trim(), SourceFile, StringComparison.OrdinalIgnoreCase);

    public static bool IsValueSource(string? sourceType) =>
        string.Equals(sourceType?.Trim(), SourceValue, StringComparison.OrdinalIgnoreCase);

    public static bool UsesOnlyValueSources(WidgetDefinition widget)
    {
        EnsureProfiles(widget);
        return widget.Profiles is { Count: > 0 } &&
               widget.Profiles.Values.All(profile => IsValueSource(profile.SourceType));
    }

    private static readonly IReadOnlyDictionary<string, StateVisualProfileSpec[]> Contracts =
        new Dictionary<string, StateVisualProfileSpec[]>(StringComparer.OrdinalIgnoreCase)
        {
            [WidgetTypeContract.Power] =
            [
                new(PowerMetricContract.StateOnline, "State: Online", 0, "lucide:battery-full", DefaultFit: "contain", DefaultLoop: true, IncludeTextPresentation: true),
                new(PowerMetricContract.StateOnBattery, "State: On Battery", 1, "lucide:battery", DefaultFit: "contain", DefaultLoop: true, IncludeTextPresentation: true),
                new(PowerMetricContract.StateCharging, "State: Charging", 2, "lucide:battery-charging", DefaultFit: "contain", DefaultLoop: true, IncludeTextPresentation: true),
                new(PowerMetricContract.StateLow, "State: Low", 3, "lucide:battery-low", DefaultFit: "contain", DefaultLoop: true, IncludeTextPresentation: true),
                new(PowerMetricContract.StateCritical, "State: Critical", 4, "lucide:battery-warning", DefaultFit: "contain", DefaultLoop: true, IncludeTextPresentation: true),
                new(PowerMetricContract.StateFullyCharged, "State: Fully Charged", 5, "lucide:battery-full", DefaultFit: "contain", DefaultLoop: true, IncludeTextPresentation: true),
                new(PowerMetricContract.StateNormal, "State: Normal", 6, "lucide:battery", DefaultFit: "contain", DefaultLoop: true, IncludeTextPresentation: true),
                new(PowerMetricContract.StateUnknown, "State: Unknown", 7, "lucide:battery-warning", DefaultFit: "contain", DefaultLoop: true, IncludeTextPresentation: true),
                new(PowerMetricContract.StateUnavailable, "State: Unavailable", 8, "lucide:battery-warning", DefaultFit: "contain", DefaultLoop: true, IncludeTextPresentation: true)
            ],
            [WidgetTypeContract.MediaSystem] =
            [
                new(MediaMetricContract.EndpointRemoteNetwork, "Type: Remote Network", 0, "lucide:router", DefaultFit: "contain", DefaultLoop: true, IncludeTextPresentation: true),
                new(MediaMetricContract.EndpointSpeakers, "Type: Speakers", 1, "lucide:speaker", DefaultFit: "contain", DefaultLoop: true, IncludeTextPresentation: true),
                new(MediaMetricContract.EndpointLineLevel, "Type: Line Level", 2, "lucide:audio-lines", DefaultFit: "contain", DefaultLoop: true, IncludeTextPresentation: true),
                new(MediaMetricContract.EndpointHeadphones, "Type: Headphones", 3, "lucide:headphones", DefaultFit: "contain", DefaultLoop: true, IncludeTextPresentation: true),
                new(MediaMetricContract.EndpointMicrophone, "Type: Microphone", 4, "lucide:mic", DefaultFit: "contain", DefaultLoop: true, IncludeTextPresentation: true),
                new(MediaMetricContract.EndpointHandset, "Type: Handset", 5, "lucide:phone", DefaultFit: "contain", DefaultLoop: true, IncludeTextPresentation: true),
                new(MediaMetricContract.EndpointDigitalPassthrough, "Type: Digital Passthrough", 6, "lucide:audio-lines", DefaultFit: "contain", DefaultLoop: true, IncludeTextPresentation: true),
                new(MediaMetricContract.EndpointSpdif, "Type: S/PDIF", 7, "lucide:cable", DefaultFit: "contain", DefaultLoop: true, IncludeTextPresentation: true),
                new(MediaMetricContract.EndpointDisplayAudio, "Type: Display Audio", 8, "lucide:monitor-speaker", DefaultFit: "contain", DefaultLoop: true, IncludeTextPresentation: true),
                new(MediaMetricContract.EndpointUnknown, "Type: Unknown", 9, "lucide:circle-help", DefaultFit: "contain", DefaultLoop: true, IncludeTextPresentation: true),
                new(UnavailableKey, "State: Unavailable", 10, "lucide:circle-slash", DefaultFit: "contain", DefaultLoop: true, IncludeTextPresentation: true)
            ],
            [WidgetTypeContract.MediaPlayer] =
            [
                new(MediaMetricContract.StatusPlaying, "State: Playing", 0, "lucide:play", DefaultFit: "contain", DefaultLoop: true, IncludeTextPresentation: true),
                new(MediaMetricContract.StatusPaused, "State: Paused", 1, "lucide:pause", DefaultFit: "contain", DefaultLoop: true, IncludeTextPresentation: true),
                new(MediaMetricContract.StatusStopped, "State: Stopped", 2, "lucide:square", DefaultFit: "contain", DefaultLoop: true, IncludeTextPresentation: true),
                new(UnavailableKey, "State: Unavailable", 3, "lucide:circle-slash", DefaultFit: "contain", DefaultLoop: true, IncludeTextPresentation: true)
            ],
            [WidgetTypeContract.Binary] =
            [
                new(BinarySignalContract.TrueKey, "State: True", 0, "lucide:check", DefaultColor: "#FFFFFFFF", DefaultFit: "contain", DefaultLoop: true, IncludeTextPresentation: true),
                new(BinarySignalContract.FalseKey, "State: False", 1, "lucide:check", DefaultColor: "#FFFFFFFF", DefaultOpacity: 0.5f, DefaultFit: "contain", DefaultLoop: true, IncludeTextPresentation: true)
            ]
        };

    public static bool IsStateVisualWidget(string? widgetType) =>
        !string.IsNullOrWhiteSpace(widgetType) && Contracts.ContainsKey(widgetType);

    public static IReadOnlyList<StateVisualProfileSpec> GetSpecs(string? widgetType) =>
        !string.IsNullOrWhiteSpace(widgetType) && Contracts.TryGetValue(widgetType, out var specs)
            ? specs
            : Array.Empty<StateVisualProfileSpec>();

    public static void EnsureProfiles(WidgetDefinition widget)
    {
        var specs = GetSpecs(widget.Type);
        if (specs.Count == 0)
            return;

        var existing = widget.Profiles;
        if (existing is not null &&
            specs.All(spec =>
                existing.TryGetValue(spec.Key, out var profile) &&
                profile is not null))
        {
            return;
        }

        var normalized = new Dictionary<string, StateVisualProfileDefinition>(
            StringComparer.OrdinalIgnoreCase);

        if (existing is not null)
        {
            foreach (var pair in existing)
            {
                var canonical = specs.FirstOrDefault(
                    x => string.Equals(x.Key, pair.Key, StringComparison.OrdinalIgnoreCase));
                normalized[canonical?.Key ?? pair.Key] = pair.Value;
            }
        }

        foreach (var spec in specs)
        {
            if (!normalized.TryGetValue(spec.Key, out var profile) || profile is null)
                normalized[spec.Key] = spec.CreateDefault();
        }

        widget.Profiles = normalized;
    }

    public static void ValidateKeys(WidgetDefinition widget)
    {
        var specs = GetSpecs(widget.Type);
        if (specs.Count == 0)
        {
            if (widget.Profiles is { Count: > 0 })
                throw new InvalidDataException(
                    $"Widget '{widget.Id}' of type '{widget.Type}' does not support state visual profiles.");
            return;
        }

        EnsureProfiles(widget);
        var allowed = new HashSet<string>(specs.Select(x => x.Key), StringComparer.OrdinalIgnoreCase);
        var unknown = widget.Profiles!.Keys.FirstOrDefault(key => !allowed.Contains(key));
        if (unknown is not null)
            throw new InvalidDataException(
                $"Widget '{widget.Id}' contains unsupported state visual profile '{unknown}'.");
    }

    public static string ResolveKey(string widgetType, object? stateOrType)
    {
        if (WidgetTypeContract.Is(widgetType, WidgetTypeContract.Power))
            return PowerMetricContract.NormalizeState(stateOrType);

        if (WidgetTypeContract.Is(widgetType, WidgetTypeContract.MediaSystem))
        {
            var text = Convert.ToString(stateOrType)?.Trim();
            if (string.Equals(text, UnavailableKey, StringComparison.OrdinalIgnoreCase))
                return UnavailableKey;
            return MediaMetricContract.NormalizeEndpointType(text);
        }

        if (WidgetTypeContract.Is(widgetType, WidgetTypeContract.MediaPlayer))
        {
            return MediaMetricContract.NormalizePlaybackState(stateOrType) switch
            {
                MediaMetricContract.StatusPlaying => MediaMetricContract.StatusPlaying,
                MediaMetricContract.StatusPaused => MediaMetricContract.StatusPaused,
                MediaMetricContract.StatusStopped or MediaMetricContract.StatusClosed =>
                    MediaMetricContract.StatusStopped,
                _ => UnavailableKey
            };
        }

        if (WidgetTypeContract.Is(widgetType, WidgetTypeContract.Binary))
        {
            var key = Convert.ToString(stateOrType)?.Trim();
            if (string.Equals(key, BinarySignalContract.TrueKey, StringComparison.OrdinalIgnoreCase))
                return BinarySignalContract.TrueKey;
            if (string.Equals(key, BinarySignalContract.FalseKey, StringComparison.OrdinalIgnoreCase))
                return BinarySignalContract.FalseKey;

            throw new InvalidDataException(
                $"Binary widget state '{stateOrType}' is not true or false.");
        }

        throw new InvalidOperationException(
            $"Widget type '{widgetType}' does not define state visual profiles.");
    }

    public static StateVisualProfile GetProfile(WidgetDefinition widget, object? stateOrType)
    {
        var key = ResolveKey(widget.Type, stateOrType);
        if (widget.Profiles is not null &&
            widget.Profiles.TryGetValue(key, out var profile) &&
            profile is not null)
        {
            return profile.ToRenderProfile();
        }

        var spec = GetSpecs(widget.Type).FirstOrDefault(
            x => string.Equals(x.Key, key, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidDataException(
                $"Widget '{widget.Id}' has no state visual profile contract for '{key}'.");
        return spec.CreateDefault().ToRenderProfile();
    }

    public static IEnumerable<StateVisualProfile> EnumerateProfiles(WidgetDefinition widget)
    {
        foreach (var spec in GetSpecs(widget.Type).OrderBy(x => x.Order))
        {
            if (widget.Profiles is not null &&
                widget.Profiles.TryGetValue(spec.Key, out var profile) &&
                profile is not null)
            {
                yield return profile.ToRenderProfile();
            }
            else
            {
                yield return spec.CreateDefault().ToRenderProfile();
            }
        }
    }

    public static bool TryGetCategoryOrder(
        string widgetType,
        string category,
        out int order)
    {
        var spec = GetSpecs(widgetType).FirstOrDefault(
            x => string.Equals(x.Category, category, StringComparison.Ordinal));
        if (spec is null)
        {
            order = default;
            return false;
        }

        order = spec.Order;
        return true;
    }
}
