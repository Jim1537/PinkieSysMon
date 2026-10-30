namespace PinkieSysMon;

internal static class MediaMetricContract
{
    // Widget source identifiers are dashboard semantics, not telemetry metric IDs.
    public const string OutputSource = "media.output";
    public const string InputSource = "media.input";
    public const string PlaybackSource = "media.playback";

    public const string OutputMetricSource = "system.media.output";
    public const string InputMetricSource = "system.media.input";
    public const string PlaybackMetricSource = "system.media.playback";

    public const string OutputId = OutputMetricSource + ".id";
    public const string OutputName = OutputMetricSource + ".name";
    public const string OutputType = OutputMetricSource + ".type";
    public const string OutputVolume = OutputMetricSource + ".volume";
    public const string OutputMuted = OutputMetricSource + ".muted";
    public const string OutputAvailable = OutputMetricSource + ".available";

    public const string InputId = InputMetricSource + ".id";
    public const string InputName = InputMetricSource + ".name";
    public const string InputType = InputMetricSource + ".type";
    public const string InputVolume = InputMetricSource + ".volume";
    public const string InputMuted = InputMetricSource + ".muted";
    public const string InputActive = InputMetricSource + ".active";
    public const string InputAvailable = InputMetricSource + ".available";

    public const string PlaybackStatus = PlaybackMetricSource + ".status";
    public const string PlaybackTitle = PlaybackMetricSource + ".title";
    public const string PlaybackArtist = PlaybackMetricSource + ".artist";
    public const string PlaybackAlbum = PlaybackMetricSource + ".album";
    public const string PlaybackSourceMetric = PlaybackMetricSource + ".source";
    public const string PlaybackProgress = PlaybackMetricSource + ".progress";
    public const string PlaybackAvailable = PlaybackMetricSource + ".available";

    public const string EndpointRemoteNetwork = "remote-network";
    public const string EndpointSpeakers = "speakers";
    public const string EndpointLineLevel = "line-level";
    public const string EndpointHeadphones = "headphones";
    public const string EndpointMicrophone = "microphone";
    public const string EndpointHandset = "handset";
    public const string EndpointDigitalPassthrough = "digital-passthrough";
    public const string EndpointSpdif = "spdif";
    public const string EndpointDisplayAudio = "display-audio";
    public const string EndpointUnknown = "unknown";

    public const string PlaybackUnknownMetadata = "[unknown]";

    public const string StatusUnavailable = "unavailable";
    public const string StatusClosed = "closed";
    public const string StatusOpened = "opened";
    public const string StatusChanging = "changing";
    public const string StatusStopped = "stopped";
    public const string StatusPlaying = "playing";
    public const string StatusPaused = "paused";

    public static readonly string[] EndpointTypes =
    [
        EndpointRemoteNetwork,
        EndpointSpeakers,
        EndpointLineLevel,
        EndpointHeadphones,
        EndpointMicrophone,
        EndpointHandset,
        EndpointDigitalPassthrough,
        EndpointSpdif,
        EndpointDisplayAudio,
        EndpointUnknown
    ];

    public static readonly MetricDescriptor[] Descriptors =
    [
        new(OutputId, MetricValueKind.Text, MetricUnit.None),
        new(OutputName, MetricValueKind.Text, MetricUnit.None),
        new(OutputType, MetricValueKind.Text, MetricUnit.None),
        new(OutputVolume, MetricValueKind.Percent, MetricUnit.None),
        new(OutputMuted, MetricValueKind.Boolean, MetricUnit.None),
        new(OutputAvailable, MetricValueKind.Boolean, MetricUnit.None),
        new(InputId, MetricValueKind.Text, MetricUnit.None),
        new(InputName, MetricValueKind.Text, MetricUnit.None),
        new(InputType, MetricValueKind.Text, MetricUnit.None),
        new(InputVolume, MetricValueKind.Percent, MetricUnit.None),
        new(InputMuted, MetricValueKind.Boolean, MetricUnit.None),
        new(InputActive, MetricValueKind.Boolean, MetricUnit.None),
        new(InputAvailable, MetricValueKind.Boolean, MetricUnit.None),
        new(PlaybackStatus, MetricValueKind.Text, MetricUnit.None),
        new(PlaybackTitle, MetricValueKind.Text, MetricUnit.None),
        new(PlaybackArtist, MetricValueKind.Text, MetricUnit.None),
        new(PlaybackAlbum, MetricValueKind.Text, MetricUnit.None),
        new(PlaybackSourceMetric, MetricValueKind.Text, MetricUnit.None),
        new(PlaybackProgress, MetricValueKind.Percent, MetricUnit.None),
        new(PlaybackAvailable, MetricValueKind.Boolean, MetricUnit.None)
    ];

    public static readonly string[] MetricNames = Descriptors.Select(x => x.Id).ToArray();

    public static string NormalizePlaybackState(object? value)
    {
        var state = Convert.ToString(value)?.Trim().ToLowerInvariant();
        return state is StatusClosed or StatusOpened or StatusChanging or StatusStopped or StatusPlaying or StatusPaused
            ? state : StatusUnavailable;
    }

    public static string NormalizeEndpointType(object? value)
    {
        var type = Convert.ToString(value)?.Trim().ToLowerInvariant();
        return EndpointTypes.Contains(type, StringComparer.OrdinalIgnoreCase)
            ? type!
            : EndpointUnknown;
    }
}
