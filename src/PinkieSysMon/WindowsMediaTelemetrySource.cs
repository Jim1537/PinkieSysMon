using NAudio.CoreAudioApi;
using Windows.Media.Control;

namespace PinkieSysMon;

internal sealed class WindowsMediaTelemetrySource : IMetricSource, IDisposable
{
    private static readonly PropertyKey AudioEndpointFormFactorKey =
        new(new Guid("1DA5D803-D492-4EDD-8C23-E0C0FFEE7F0E"), 0);

    private static readonly TimeSpan ErrorLogThrottleInterval = TimeSpan.FromSeconds(30);

    private readonly object _sync = new();
    private readonly FileLogger _log;
    private readonly MMDeviceEnumerator _deviceEnumerator = new();
    private readonly MMDeviceNotificationClient _notificationClient;
    private MMDevice? _defaultOutputEndpoint;
    private MMDevice? _defaultInputEndpoint;
    private AudioSessionManager? _inputSessionManager;
    private int _inputSessionsRefreshPending;
    private int _endpointRefreshPending = 1;
    private GlobalSystemMediaTransportControlsSessionManager? _sessionManager;
    private GlobalSystemMediaTransportControlsSession? _session;
    private PlaybackSnapshot _playback = PlaybackSnapshot.Unavailable;
    private long _mediaPropertiesGeneration;
    private IReadOnlyDictionary<string, string> _endpointTypeOverrides =
        new Dictionary<string, string>(StringComparer.Ordinal);
    private bool _disposed;

    public WindowsMediaTelemetrySource(FileLogger log)
    {
        _log = log;

        // NAudio 3.1 intentionally hides the raw IMMNotificationClient COM interface.
        // Use the public managed notification client instead.
        _notificationClient = _deviceEnumerator.CreateNotificationClient(useSynchronizationContext: false);
        _notificationClient.DefaultDeviceChanged += OnDefaultDeviceChanged;
        _notificationClient.DeviceAdded += OnDeviceAdded;
        _notificationClient.DeviceRemoved += OnDeviceRemoved;
        _notificationClient.DeviceStateChanged += OnDeviceStateChanged;

        _ = InitializeSessionsAsync();
    }

    public string ProviderId => MetricProviderContract.System;
    public string Name => "windows-media";
    public IReadOnlyCollection<string> MetricNames => MediaMetricContract.MetricNames;

    public void SetEndpointTypeOverrides(IReadOnlyDictionary<string, string>? overrides) =>
        ApplyEndpointTypeOverrides(PrepareEndpointTypeOverrides(overrides));

    public static IReadOnlyDictionary<string, string> PrepareEndpointTypeOverrides(
        IReadOnlyDictionary<string, string>? overrides) =>
        overrides is null
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            : overrides
                .Where(x => !string.IsNullOrWhiteSpace(x.Key))
                .ToDictionary(
                    x => x.Key,
                    x => MediaMetricContract.NormalizeEndpointType(x.Value),
                    StringComparer.Ordinal);

    public void ApplyEndpointTypeOverrides(IReadOnlyDictionary<string, string> normalizedOverrides)
    {
        ArgumentNullException.ThrowIfNull(normalizedOverrides);
        lock (_sync)
            _endpointTypeOverrides = normalizedOverrides;
    }

    public IReadOnlyList<MediaEndpointInfo> GetActiveEndpoints()
    {
        lock (_sync)
        {
            var result = new Dictionary<string, MediaEndpointInfo>(StringComparer.Ordinal);
            try
            {
                using var endpoints = _deviceEnumerator.EnumerateAudioEndPoints(DataFlow.All, DeviceState.Active);
                foreach (var endpoint in endpoints)
                {
                    try
                    {
                        var id = endpoint.ID;
                        if (string.IsNullOrWhiteSpace(id))
                            continue;

                        var name = string.IsNullOrWhiteSpace(endpoint.FriendlyName)
                            ? "(unnamed endpoint)"
                            : endpoint.FriendlyName.Trim();

                        result[id] = new MediaEndpointInfo(id, name);
                    }
                    finally
                    {
                        endpoint.Dispose();
                    }
                }
            }
            catch (Exception ex)
            {
                _log.ErrorThrottled("media.endpoint-enumeration", ErrorLogThrottleInterval, "Audio endpoint enumeration failed", ex);
            }

            return result.Values
                .OrderBy(x => x.FriendlyName, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(x => x.EndpointId, StringComparer.Ordinal)
                .ToArray();
        }
    }

    public IReadOnlyDictionary<string, object?> Capture() =>
        Capture(MediaMetricContract.MetricNames);

    public IReadOnlyDictionary<string, object?> Capture(IReadOnlyCollection<string> requestedMetrics)
    {
        var requested = requestedMetrics as HashSet<string>
            ?? new HashSet<string>(requestedMetrics, StringComparer.OrdinalIgnoreCase);

        lock (_sync)
        {
            var wantsOutput = requested.Any(metric =>
                metric.StartsWith(MediaMetricContract.OutputMetricSource + ".", StringComparison.OrdinalIgnoreCase));
            var wantsInput = requested.Any(metric =>
                metric.StartsWith(MediaMetricContract.InputMetricSource + ".", StringComparison.OrdinalIgnoreCase));
            var wantsPlayback = requested.Any(metric =>
                metric.StartsWith(MediaMetricContract.PlaybackMetricSource + ".", StringComparison.OrdinalIgnoreCase));

            if ((wantsOutput || wantsInput) &&
                Interlocked.Exchange(ref _endpointRefreshPending, 0) != 0)
            {
                RefreshEndpointsLocked();
            }

            var result = new Dictionary<string, object?>(requested.Count, StringComparer.OrdinalIgnoreCase);

            if (wantsOutput)
                WriteEndpointMetrics(result, requested, MediaMetricContract.OutputMetricSource, _defaultOutputEndpoint);

            if (wantsInput)
            {
                WriteEndpointMetrics(result, requested, MediaMetricContract.InputMetricSource, _defaultInputEndpoint);
                if (requested.Contains(MediaMetricContract.InputActive))
                    result[MediaMetricContract.InputActive] = GetInputActiveLocked();
            }

            if (wantsPlayback)
            {
                AddIfRequested(result, requested, MediaMetricContract.PlaybackStatus, _playback.Status);
                AddIfRequested(result, requested, MediaMetricContract.PlaybackTitle, _playback.Title);
                AddIfRequested(result, requested, MediaMetricContract.PlaybackArtist, _playback.Artist);
                AddIfRequested(result, requested, MediaMetricContract.PlaybackAlbum, _playback.Album);
                AddIfRequested(result, requested, MediaMetricContract.PlaybackSourceMetric, _playback.Source);
                AddIfRequested(result, requested, MediaMetricContract.PlaybackAvailable, _playback.Available);
                if (requested.Contains(MediaMetricContract.PlaybackProgress))
                    result[MediaMetricContract.PlaybackProgress] = GetPlaybackProgressLocked(DateTimeOffset.UtcNow);
            }

            return result;
        }
    }

    private void MarkEndpointsDirty() => Interlocked.Exchange(ref _endpointRefreshPending, 1);

    private void OnDefaultDeviceChanged(object? sender, DefaultDeviceChangedEventArgs args)
    {
        if (args.Role == Role.Multimedia)
            MarkEndpointsDirty();
    }

    private void OnDeviceAdded(object? sender, DeviceNotificationEventArgs args) =>
        MarkEndpointsDirty();

    private void OnDeviceRemoved(object? sender, DeviceNotificationEventArgs args) =>
        MarkEndpointsDirty();

    private void OnDeviceStateChanged(object? sender, DeviceStateChangedEventArgs args) =>
        MarkEndpointsDirty();

    private void RefreshEndpointsLocked()
    {
        DetachInputSessionManagerLocked();
        _defaultOutputEndpoint?.Dispose();
        _defaultInputEndpoint?.Dispose();
        _defaultOutputEndpoint = TryGetDefaultEndpoint(DataFlow.Render);
        _defaultInputEndpoint = TryGetDefaultEndpoint(DataFlow.Capture);
        AttachInputSessionManagerLocked();
    }

    private void AttachInputSessionManagerLocked()
    {
        if (_defaultInputEndpoint is null)
            return;

        try
        {
            _inputSessionManager = _defaultInputEndpoint.AudioSessionManager;
            _inputSessionManager.OnSessionCreated += OnInputSessionCreated;

            // Close the small race between AudioSessionManager construction and event hookup.
            // The next telemetry capture rebuilds the enumerator once before relying on
            // OnSessionCreated to flag later additions.
            Interlocked.Exchange(ref _inputSessionsRefreshPending, 1);
        }
        catch (Exception ex)
        {
            _inputSessionManager = null;
            Interlocked.Exchange(ref _inputSessionsRefreshPending, 0);
            _log.ErrorThrottled("media.input-session-manager", ErrorLogThrottleInterval, "Default input audio-session manager initialization failed", ex);
        }
    }

    private void DetachInputSessionManagerLocked()
    {
        if (_inputSessionManager is not null)
            _inputSessionManager.OnSessionCreated -= OnInputSessionCreated;

        _inputSessionManager = null;
        Interlocked.Exchange(ref _inputSessionsRefreshPending, 0);
    }

    private void OnInputSessionCreated(object sender, AudioSessionControl newSession)
    {
        // The callback's session wrapper is borrowed from NAudio; the durable work is to
        // rebuild the endpoint enumerator on the next normal telemetry capture.
        Interlocked.Exchange(ref _inputSessionsRefreshPending, 1);
    }

    private bool? GetInputActiveLocked()
    {
        if (_defaultInputEndpoint is null || _inputSessionManager is null)
            return null;

        try
        {
            if (Interlocked.Exchange(ref _inputSessionsRefreshPending, 0) != 0)
                _inputSessionManager.RefreshSessions();

            var sessions = _inputSessionManager.Sessions;
            for (var i = 0; i < sessions.Count; i++)
            {
                using var session = sessions[i];
                if ((int)session.State == 1) // AudioSessionStateActive
                    return true;
            }

            return false;
        }
        catch (Exception ex)
        {
            // A live endpoint can be invalidated or reconfigured between notifications.
            // Preserve the semantic distinction between "inactive" and "unknown".
            Interlocked.Exchange(ref _inputSessionsRefreshPending, 1);
            _log.ErrorThrottled("media.input-session-state", ErrorLogThrottleInterval, "Default input audio-session state query failed", ex);
            return null;
        }
    }

    private MMDevice? TryGetDefaultEndpoint(DataFlow flow)
    {
        try
        {
            return _deviceEnumerator.TryGetDefaultAudioEndpoint(flow, Role.Multimedia, out var device)
                ? device
                : null;
        }
        catch (Exception ex)
        {
            _log.ErrorThrottled($"media.default-endpoint.{flow}", ErrorLogThrottleInterval, $"Default audio endpoint query failed for {flow}", ex);
            return null;
        }
    }

    private void WriteEndpointMetrics(
        IDictionary<string, object?> result,
        ISet<string> requested,
        string source,
        MMDevice? device)
    {
        var idMetric = $"{source}.id";
        var nameMetric = $"{source}.name";
        var typeMetric = $"{source}.type";
        var volumeMetric = $"{source}.volume";
        var mutedMetric = $"{source}.muted";
        var availableMetric = $"{source}.available";

        if (device is null)
        {
            AddIfRequested(result, requested, idMetric, null);
            AddIfRequested(result, requested, nameMetric, null);
            AddIfRequested(result, requested, typeMetric, MediaMetricContract.EndpointUnknown);
            AddIfRequested(result, requested, volumeMetric, null);
            AddIfRequested(result, requested, mutedMetric, null);
            AddIfRequested(result, requested, availableMetric, false);
            return;
        }

        if (requested.Contains(idMetric))
            result[idMetric] = device.ID;
        if (requested.Contains(nameMetric))
            result[nameMetric] = device.FriendlyName;

        if (requested.Contains(typeMetric))
        {
            var detectedType = EndpointType(device);
            result[typeMetric] = _endpointTypeOverrides.TryGetValue(device.ID, out var overrideType)
                ? MediaMetricContract.NormalizeEndpointType(overrideType)
                : detectedType;
        }

        if (requested.Contains(volumeMetric) || requested.Contains(mutedMetric))
        {
            try
            {
                if (requested.Contains(volumeMetric))
                {
                    result[volumeMetric] =
                        Math.Clamp(device.AudioEndpointVolume.MasterVolumeLevelScalar * 100.0, 0.0, 100.0);
                }

                if (requested.Contains(mutedMetric))
                    result[mutedMetric] = device.AudioEndpointVolume.Mute;
            }
            catch
            {
                if (requested.Contains(volumeMetric))
                    result[volumeMetric] = null;
                if (requested.Contains(mutedMetric))
                    result[mutedMetric] = null;
            }
        }

        if (requested.Contains(availableMetric))
            result[availableMetric] = true;
    }

    private static void AddIfRequested(
        IDictionary<string, object?> result,
        ISet<string> requested,
        string metric,
        object? value)
    {
        if (requested.Contains(metric))
            result[metric] = value;
    }

    private static string EndpointType(MMDevice device)
    {
        try
        {
            if (device.Properties.TryGetValue<uint>(AudioEndpointFormFactorKey, out var formFactor))
            {
                return formFactor switch
                {
                    0 => MediaMetricContract.EndpointRemoteNetwork,
                    1 => MediaMetricContract.EndpointSpeakers,
                    2 => MediaMetricContract.EndpointLineLevel,
                    3 => MediaMetricContract.EndpointHeadphones,
                    4 => MediaMetricContract.EndpointMicrophone,
                    5 => MediaMetricContract.EndpointHeadphones, // Windows Headset is intentionally merged with Headphones.
                    6 => MediaMetricContract.EndpointHandset,
                    7 => MediaMetricContract.EndpointDigitalPassthrough,
                    8 => MediaMetricContract.EndpointSpdif,
                    9 => MediaMetricContract.EndpointDisplayAudio,
                    _ => MediaMetricContract.EndpointUnknown
                };
            }
        }
        catch
        {
            // Form-factor metadata is optional driver data. Unknown is a valid result.
        }

        return MediaMetricContract.EndpointUnknown;
    }

    private async Task InitializeSessionsAsync()
    {
        try
        {
            var manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();

            lock (_sync)
            {
                if (_disposed)
                    return;

                _sessionManager = manager;
                _sessionManager.CurrentSessionChanged += OnCurrentSessionChanged;
                _sessionManager.SessionsChanged += OnSessionsChanged;
                AttachSessionLocked(manager.GetCurrentSession());
            }
        }
        catch (Exception ex)
        {
            _log.Error("Windows media session initialization failed", ex);
        }
    }

    private void OnCurrentSessionChanged(
        GlobalSystemMediaTransportControlsSessionManager sender,
        CurrentSessionChangedEventArgs args)
    {
        lock (_sync)
        {
            if (!_disposed)
                AttachSessionLocked(sender.GetCurrentSession());
        }
    }

    private void OnSessionsChanged(
        GlobalSystemMediaTransportControlsSessionManager sender,
        SessionsChangedEventArgs args)
    {
        lock (_sync)
        {
            if (!_disposed)
                AttachSessionLocked(sender.GetCurrentSession());
        }
    }

    private void AttachSessionLocked(GlobalSystemMediaTransportControlsSession? next)
    {
        _mediaPropertiesGeneration++;
        if (_session is not null)
        {
            _session.PlaybackInfoChanged -= OnPlaybackInfoChanged;
            _session.MediaPropertiesChanged -= OnMediaPropertiesChanged;
            _session.TimelinePropertiesChanged -= OnTimelinePropertiesChanged;
        }

        _session = next;
        if (_session is null)
        {
            ResetPlaybackLocked();
            return;
        }

        _session.PlaybackInfoChanged += OnPlaybackInfoChanged;
        _session.MediaPropertiesChanged += OnMediaPropertiesChanged;
        _session.TimelinePropertiesChanged += OnTimelinePropertiesChanged;

        if (!RefreshPlaybackInfoLocked())
            return;

        RefreshTimelineLocked();
        QueueMediaPropertiesRefreshLocked(_session, resetMetadata: true);
    }

    private void OnPlaybackInfoChanged(
        GlobalSystemMediaTransportControlsSession sender,
        PlaybackInfoChangedEventArgs args)
    {
        lock (_sync)
        {
            if (!_disposed && ReferenceEquals(sender, _session))
            {
                var wasAvailable = _playback.Available;
                if (!RefreshPlaybackInfoLocked())
                    return;

                RefreshTimelineLocked();
                if (!wasAvailable)
                    QueueMediaPropertiesRefreshLocked(sender, resetMetadata: true);
            }
        }
    }

    private void OnMediaPropertiesChanged(
        GlobalSystemMediaTransportControlsSession sender,
        MediaPropertiesChangedEventArgs args)
    {
        lock (_sync)
        {
            if (!_disposed && ReferenceEquals(sender, _session) && _playback.Available)
            {
                RefreshTimelineLocked();
                QueueMediaPropertiesRefreshLocked(sender, resetMetadata: true);
            }
        }
    }

    private void OnTimelinePropertiesChanged(
        GlobalSystemMediaTransportControlsSession sender,
        TimelinePropertiesChangedEventArgs args)
    {
        lock (_sync)
        {
            if (!_disposed && ReferenceEquals(sender, _session) && _playback.Available)
                RefreshTimelineLocked();
        }
    }

    private void QueueMediaPropertiesRefreshLocked(
        GlobalSystemMediaTransportControlsSession session,
        bool resetMetadata)
    {
        if (resetMetadata)
        {
            _playback = _playback with
            {
                Title = MediaMetricContract.PlaybackUnknownMetadata,
                Artist = MediaMetricContract.PlaybackUnknownMetadata,
                Album = MediaMetricContract.PlaybackUnknownMetadata
            };
        }

        var generation = ++_mediaPropertiesGeneration;
        _ = RefreshMediaPropertiesAsync(session, generation);
    }

    private bool RefreshPlaybackInfoLocked()
    {
        if (_session is null)
        {
            ResetPlaybackLocked();
            return false;
        }

        try
        {
            var playbackInfo = _session.GetPlaybackInfo();
            if (playbackInfo.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Closed)
            {
                ResetPlaybackLocked();
                return false;
            }

            var status = playbackInfo.PlaybackStatus.ToString().ToLowerInvariant();
            _playback = _playback with
            {
                Status = status,
                PlaybackRate = playbackInfo.PlaybackRate,
                Available = true,
                Source = _session.SourceAppUserModelId
            };
            return true;
        }
        catch (Exception ex)
        {
            ResetPlaybackLocked();
            _log.ErrorThrottled("media.playback-info", ErrorLogThrottleInterval, "Windows media playback-info query failed", ex);
            return false;
        }
    }

    private void ResetPlaybackLocked()
    {
        _mediaPropertiesGeneration++;
        _playback = PlaybackSnapshot.Unavailable;
    }

    private void RefreshTimelineLocked()
    {
        if (_session is null)
        {
            _playback = _playback with { TimelineAvailable = false };
            return;
        }

        try
        {
            var timeline = _session.GetTimelineProperties();
            _playback = _playback with
            {
                TimelineStart = timeline.StartTime,
                TimelineEnd = timeline.EndTime,
                TimelinePosition = timeline.Position,
                TimelineLastUpdated = timeline.LastUpdatedTime,
                TimelineAvailable = timeline.EndTime > timeline.StartTime
            };
        }
        catch (Exception ex)
        {
            _playback = _playback with { TimelineAvailable = false };
            _log.ErrorThrottled("media.timeline", ErrorLogThrottleInterval, "Windows media timeline query failed", ex);
        }
    }

    private double? GetPlaybackProgressLocked(DateTimeOffset now)
    {
        if (!_playback.Available)
            return null;

        if (_playback.TimelineAvailable)
        {
            var startTicks = (double)_playback.TimelineStart.Ticks;
            var endTicks = (double)_playback.TimelineEnd.Ticks;
            var durationTicks = endTicks - startTicks;
            if (durationTicks > 0d && double.IsFinite(durationTicks))
            {
                // SMTC Position is a snapshot valid at LastUpdatedTime. Extrapolate only while
                // playing so a normal telemetry poll produces continuously advancing progress.
                var positionTicks = (double)_playback.TimelinePosition.Ticks;
                if (_playback.Status == MediaMetricContract.StatusPlaying)
                {
                    var lastUpdated = _playback.TimelineLastUpdated;
                    if (lastUpdated != default && now > lastUpdated)
                    {
                        var rate = _playback.PlaybackRate ?? 1.0;
                        if (double.IsFinite(rate))
                            positionTicks += (now - lastUpdated).Ticks * rate;
                    }
                }

                var ratio = Math.Clamp((positionTicks - startTicks) / durationTicks, 0d, 1d);
                return ratio * 100d;
            }
        }

        // Some AIMP SMTC bridges publish state/metadata but omit TimelineProperties.
        // Keep the canonical metric provider-neutral and use AIMP Remote Access only as
        // a source-specific implementation fallback for the active AIMP session.
        if (AimpRemotePlaybackFallback.IsAimpSource(_playback.Source) &&
            AimpRemotePlaybackFallback.TryGetProgress(out var aimpProgress))
        {
            return aimpProgress;
        }

        return null;
    }

    private async Task RefreshMediaPropertiesAsync(
        GlobalSystemMediaTransportControlsSession session,
        long generation)
    {
        try
        {
            var media = await session.TryGetMediaPropertiesAsync();

            lock (_sync)
            {
                if (_disposed || !ReferenceEquals(session, _session) || generation != _mediaPropertiesGeneration)
                    return;

                _playback = _playback with
                {
                    Title = MetadataOrUnknown(media.Title),
                    Artist = MetadataOrUnknown(media.Artist),
                    Album = MetadataOrUnknown(media.AlbumTitle),
                    Source = session.SourceAppUserModelId,
                    Available = true
                };
            }
        }
        catch (Exception ex)
        {
            _log.ErrorThrottled("media.properties", ErrorLogThrottleInterval, "Windows media properties query failed", ex);
        }
    }

    private static string MetadataOrUnknown(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? MediaMetricContract.PlaybackUnknownMetadata
            : value.Trim();

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed)
                return;

            _disposed = true;
            _mediaPropertiesGeneration++;

            if (_sessionManager is not null)
            {
                _sessionManager.CurrentSessionChanged -= OnCurrentSessionChanged;
                _sessionManager.SessionsChanged -= OnSessionsChanged;
            }

            if (_session is not null)
            {
                _session.PlaybackInfoChanged -= OnPlaybackInfoChanged;
                _session.MediaPropertiesChanged -= OnMediaPropertiesChanged;
                _session.TimelinePropertiesChanged -= OnTimelinePropertiesChanged;
            }

            _notificationClient.DefaultDeviceChanged -= OnDefaultDeviceChanged;
            _notificationClient.DeviceAdded -= OnDeviceAdded;
            _notificationClient.DeviceRemoved -= OnDeviceRemoved;
            _notificationClient.DeviceStateChanged -= OnDeviceStateChanged;

            _notificationClient.Dispose();
            DetachInputSessionManagerLocked();
            _defaultOutputEndpoint?.Dispose();
            _defaultInputEndpoint?.Dispose();
            _deviceEnumerator.Dispose();
        }
    }

    public sealed record MediaEndpointInfo(string EndpointId, string FriendlyName);

    private sealed record PlaybackSnapshot(
        string Status,
        string? Title,
        string? Artist,
        string? Album,
        string? Source,
        double? PlaybackRate,
        TimeSpan TimelineStart,
        TimeSpan TimelineEnd,
        TimeSpan TimelinePosition,
        DateTimeOffset TimelineLastUpdated,
        bool TimelineAvailable,
        bool Available)
    {
        public static readonly PlaybackSnapshot Unavailable =
            new(
                MediaMetricContract.StatusUnavailable,
                MediaMetricContract.PlaybackUnknownMetadata,
                MediaMetricContract.PlaybackUnknownMetadata,
                MediaMetricContract.PlaybackUnknownMetadata,
                null,
                null,
                TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero,
                default,
                false,
                false);
    }
}
