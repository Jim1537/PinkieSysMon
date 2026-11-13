using System.Diagnostics;
using System.Text.Json;
using PinkieSysMon;
using PinkieSysMon.Editor;
using C = PinkieSysMon.DashboardModel;

namespace PinkieSysMon.RegressionTests;

internal static class Issue110MetricSnapshotTests
{
    private static readonly string Status = OutputMetricContract.Id("alpha", OutputMetricContract.Status);
    private static readonly string Fps = OutputMetricContract.Id("alpha", OutputMetricContract.Fps);

    public static void BoundedTypedProtocol()
    {
        var command = RuntimeMetricSnapshotProtocol.BuildRequest([Status, Fps]);
        Check(RuntimeIpcProtocol.ParseCommand(command) is
            { Kind: RuntimeIpcCommandKind.ReadMetrics, MetricIds.Length: 2 },
            "A bounded read-only request must parse without affecting legacy STATUS.");
        Check(RuntimeIpcProtocol.ParseCommand(RuntimeIpcProtocol.StatusCommand).Kind == RuntimeIpcCommandKind.Status,
            "Legacy STATUS must keep its existing meaning.");

        var instance = Guid.NewGuid();
        var response = RuntimeMetricSnapshotProtocol.SerializeSnapshot(instance, [Status, Fps],
            new Dictionary<string, object?> { [Status] = "CONNECTED", [Fps] = 24.0 });
        var snapshot = RuntimeMetricSnapshotProtocol.DeserializeSnapshot(response, [Status, Fps]);
        Check(snapshot.InstanceId == instance &&
              snapshot.Values[Status] is string text && text == "CONNECTED" &&
              snapshot.Values[Fps] is double numeric && numeric == 24.0,
            "Runtime values must survive IPC serialization with their text and numeric types.");

        var allTypes = new[]
        {
            OutputMetricContract.Id("beta", OutputMetricContract.FrameCount),
            OutputMetricContract.Id("beta", OutputMetricContract.Status),
            OutputMetricContract.Id("beta", OutputMetricContract.Fps)
        };
        var typed = RuntimeMetricSnapshotProtocol.DeserializeSnapshot(
            RuntimeMetricSnapshotProtocol.SerializeSnapshot(instance, allTypes,
                new Dictionary<string, object?>
                {
                    [allTypes[0]] = 12345L,
                    [allTypes[1]] = null,
                    [allTypes[2]] = true
                }), allTypes);
        Check(typed.Values[allTypes[0]] is long count && count == 12345 &&
              typed.Values.ContainsKey(allTypes[1]) && typed.Values[allTypes[1]] is null &&
              typed.Values[allTypes[2]] is bool boolean && boolean,
            "Integral, null and Boolean current values must preserve their distinct semantics.");

        foreach (var malformed in new[]
        {
            RuntimeMetricSnapshotProtocol.Command + "\t{}",
            RuntimeMetricSnapshotProtocol.Command + "\t{\"Ids\":[]}",
            RuntimeMetricSnapshotProtocol.Command + "\t{\"Ids\":[\"system.network.internet.connected\"]}",
            RuntimeMetricSnapshotProtocol.Command + "\t{\"Ids\":[\"" + Status + "\",\"" + Status + "\"]}",
            RuntimeMetricSnapshotProtocol.Command + "\t{\"Ids\":[\"" + Status + "\"],\"Unexpected\":true}"
        })
            Check(RuntimeIpcProtocol.ParseCommand(malformed).Kind == RuntimeIpcCommandKind.Invalid,
                "Malformed, unrequested, duplicate and extended IPC commands must fail closed.");

        var tooMany = Enumerable.Range(0, RuntimeMetricSnapshotProtocol.MaxRequestIds + 1)
            .Select(i => OutputMetricContract.Id("screen-" + i, OutputMetricContract.Fps)).ToArray();
        Check(RuntimeIpcProtocol.ParseCommand(
            RuntimeMetricSnapshotProtocol.Command + "\t" + JsonSerializer.Serialize(new { Ids = tooMany })
        ).Kind == RuntimeIpcCommandKind.Invalid, "An oversized request must be rejected.");

        Throws<InvalidDataException>(() => RuntimeMetricSnapshotProtocol.DeserializeSnapshot(
            response.Replace("\"Version\":1", "\"Version\":2", StringComparison.Ordinal), [Status, Fps]));
        Throws<InvalidDataException>(() => RuntimeMetricSnapshotProtocol.DeserializeSnapshot(
            response, [Fps]));
        Throws<InvalidDataException>(() => RuntimeMetricSnapshotProtocol.DeserializeSnapshot(
            response.Replace("\"CONNECTED\"", "[\"CONNECTED\"]", StringComparison.Ordinal), [Status, Fps]));

        var sourceId = FilesystemSourceContract.NewId();
        var sizeId = FilesystemSourceContract.MetricId(sourceId, FilesystemSourceContract.Size);
        var createdId = FilesystemSourceContract.MetricId(sourceId, FilesystemSourceContract.Created);
        var modifiedId = FilesystemSourceContract.MetricId(sourceId, FilesystemSourceContract.Modified);
        var observedLocal = new DateTime(2026, 11, 13, 13, 42, 15, DateTimeKind.Local);
        var observedOffset = new DateTimeOffset(2026, 11, 13, 13, 42, 15, TimeSpan.FromHours(-6));
        var fsRequest = RuntimeMetricSnapshotProtocol.BuildRequest([sizeId, createdId, modifiedId]);
        Check(RuntimeIpcProtocol.ParseCommand(fsRequest) is
            { Kind: RuntimeIpcCommandKind.ReadMetrics, MetricIds.Length: 3 },
            "Filesystem IDs must use the same read-only IPC command as Output IDs.");
        var fsResponse = RuntimeMetricSnapshotProtocol.SerializeSnapshot(instance,
            [sizeId, createdId, modifiedId],
            new Dictionary<string, object?>
            {
                [sizeId] = 1024L,
                [createdId] = observedLocal,
                [modifiedId] = observedOffset
            });
        var filesystem = RuntimeMetricSnapshotProtocol.DeserializeSnapshot(fsResponse,
            [sizeId, createdId, modifiedId]);
        Check(filesystem.Values[sizeId] is long bytes && bytes == 1024L &&
              filesystem.Values[createdId] is DateTime dt &&
              dt.Ticks == observedLocal.Ticks && dt.Kind == DateTimeKind.Local &&
              filesystem.Values[modifiedId] is DateTimeOffset dto && dto.EqualsExact(observedOffset),
            "Filesystem bytes and timestamps must retain their CLR types, ticks, Kind and UTC offset.");
        Check(RuntimeIpcProtocol.ParseCommand(
            RuntimeMetricSnapshotProtocol.Command + "\t{\"Ids\":[\"filesystem.invalid.size\"]}"
        ).Kind == RuntimeIpcCommandKind.Invalid,
            "Unregistered or malformed Filesystem identities must not bypass canonical request validation.");
    }

    public static void RuntimeOwnershipAndProviderFiltering()
    {
        var config = Config();
        var betaStatus = OutputMetricContract.Id("beta", OutputMetricContract.Status);
        var desktopUsb = OutputMetricContract.Id("alpha", OutputMetricContract.UsbState);
        var metrics = new MetricStore();
        metrics.Publish(new Dictionary<string, object?>
        {
            [Status] = "CONNECTED",
            [Fps] = 25d,
            [betaStatus] = "CONNECTED",
            [desktopUsb] = "CONNECTED"
        });
        var root = Path.Combine(Path.GetTempPath(), "PinkieSysMon-110-metrics-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            using var manager = new OutputSessionManager(config, new C.DashboardDefinition(), metrics,
                new FileLogger(Path.Combine(root, "regression.log")));
            var readings = manager.ReadPublishedMetricSnapshot([Status, Fps, betaStatus, desktopUsb]);
            Check(readings.Count == 2 && (string?)readings[Status] == "CONNECTED" &&
                  (double)readings[Fps]! == 25d && !readings.ContainsKey(betaStatus) &&
                  !readings.ContainsKey(desktopUsb),
                "Only configured, provider-enabled, kind-supported published Output IDs may be exported.");

            var replacement = Config();
            replacement.Outputs.Targets.RemoveAt(0);
            manager.Reconfigure(replacement, new C.DashboardDefinition());
            Check(manager.ReadPublishedMetricSnapshot([Status, Fps]).Count == 0,
                "Removed Output identities must cease to export previously published values.");
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { }
        }
    }

    public static void FilesystemOwnerAuthorizationAndPreview()
    {
        var root = Path.Combine(Path.GetTempPath(), "PinkieSysMon-110-filesystem-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var source = new FilesystemSourceConfig
            {
                Id = FilesystemSourceContract.NewId(), Alias = "Synthetic",
                Path = Path.Combine(root, "fixture.bin"), Kind = FilesystemSourceContract.FileKind
            };
            var size = FilesystemSourceContract.MetricId(source.Id, FilesystemSourceContract.Size);
            var created = FilesystemSourceContract.MetricId(source.Id, FilesystemSourceContract.Created);
            var invalidCount = FilesystemSourceContract.MetricId(source.Id, FilesystemSourceContract.Files);
            var missing = FilesystemSourceContract.MetricId(FilesystemSourceContract.NewId(), FilesystemSourceContract.Size);
            var timestamp = new DateTime(2026, 11, 13, 14, 8, 0, DateTimeKind.Local);
            var config = Config();
            config.Filesystem.Sources.Add(source);
            config.MetricProviders[MetricProviderContract.Filesystem] = true;
            var store = new MetricStore();
            store.Publish(new Dictionary<string, object?>
            {
                [size] = 4096L, [created] = timestamp,
                [invalidCount] = 999L, [missing] = 123L
            });
            using var manager = new OutputSessionManager(config, new C.DashboardDefinition(), store,
                new FileLogger(Path.Combine(root, "regression.log")));
            var requested = new[] { size, created, invalidCount, missing };
            var values = manager.ReadPublishedMetricSnapshot(requested);
            Check(values.Count == 2 && values[size] is long bytes && bytes == 4096L &&
                  values[created] is DateTime date && date.Ticks == timestamp.Ticks &&
                  !values.ContainsKey(invalidCount) && !values.ContainsKey(missing),
                "Only configured and enabled Filesystem source metrics may be read; File sources cannot expose Directory counters.");
            var formatted = MetricValueFormatter.Format(values[size]!,
                FilesystemSourceContract.DescriptorsFor([source]).Single(x => x.Id == size),
                null, null, System.Globalization.CultureInfo.InvariantCulture);
            Check(!string.IsNullOrWhiteSpace(formatted) && formatted != "--",
                "The original Text/Value formatting pipeline must accept genuine Filesystem values.");

            var preview = EditorRuntimeMetricCache.Compose(
                new Dictionary<string, object?> { [size] = 100L, ["system.test.local"] = 7L },
                values, new HashSet<string>([size, created, "system.test.local"], StringComparer.OrdinalIgnoreCase));
            Check(preview[size] is long visible && visible == 4096 &&
                  preview[created] is DateTime && preview["system.test.local"] is long local && local == 7,
                "Editor preview must prefer published Filesystem readings while retaining its independently captured metrics.");

            var directory = Config();
            directory.Filesystem.Sources.Add(new FilesystemSourceConfig
            {
                Id = source.Id, Alias = "Directory", Path = Path.Combine(root, "directory"),
                Kind = FilesystemSourceContract.DirectoryKind
            });
            directory.MetricProviders[MetricProviderContract.Filesystem] = true;
            manager.Reconfigure(directory, new C.DashboardDefinition());
            store.Publish(invalidCount, 0L);
            var directoryCounts = manager.ReadPublishedMetricSnapshot([invalidCount]);
            Check(directoryCounts.TryGetValue(invalidCount, out var zero) && zero is long number && number == 0,
                "Configured Directory counters must preserve a genuine zero instead of rendering Fallback.");

            var off = Config();
            off.Filesystem.Sources.Add(source.Copy());
            off.MetricProviders[MetricProviderContract.Filesystem] = false;
            manager.Reconfigure(off, new C.DashboardDefinition());
            Check(manager.ReadPublishedMetricSnapshot(requested).Count == 0,
                "Turning Filesystem off must hide previous published values even when MetricStore still holds them.");

            var removed = Config();
            removed.MetricProviders[MetricProviderContract.Filesystem] = true;
            manager.Reconfigure(removed, new C.DashboardDefinition());
            Check(manager.ReadPublishedMetricSnapshot(requested).Count == 0,
                "Removing a Filesystem source must not expose stale metrics or its private path.");

            var onlineButUnpublished = Config();
            onlineButUnpublished.Filesystem.Sources.Add(new FilesystemSourceConfig
            {
                Id = FilesystemSourceContract.NewId(), Alias = "Unscanned",
                Path = Path.Combine(root, "unscanned.bin"), Kind = FilesystemSourceContract.FileKind
            });
            onlineButUnpublished.MetricProviders[MetricProviderContract.Filesystem] = true;
            manager.Reconfigure(onlineButUnpublished, new C.DashboardDefinition());
            var absentSize = FilesystemSourceContract.MetricId(
                onlineButUnpublished.Filesystem.Sources[0].Id, FilesystemSourceContract.Size);
            var absent = manager.ReadPublishedMetricSnapshot([absentSize]);
            Check(absent.TryGetValue(absentSize, out var unknown) && unknown is null,
                "Configured but not yet published Filesystem readings must be unavailable, never zero.");
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { }
        }
    }

    public static void ReadOnlyPipeAndGenerationIsolation()
    {
        var root = Path.Combine(Path.GetTempPath(), "PinkieSysMon-110-pipe-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var pipe = "PinkieSysMon-110-" + Guid.NewGuid().ToString("N");
            var firstInstance = Guid.NewGuid();
            var secondInstance = Guid.NewGuid();
            var ids = Enumerable.Range(0, RuntimeMetricSnapshotProtocol.MaxRequestIds + 1)
                .Select(i => OutputMetricContract.Id("screen-" + i, OutputMetricContract.Fps)).ToArray();
            var requests = 0;
            var changeInstance = false;
            var legacyRuntime = false;
            using var server = new RuntimeIpcServer((command, _) =>
            {
                if (command == RuntimeIpcProtocol.StatusCommand)
                    return Task.FromResult(new RuntimeIpcResponse(true,
                        RuntimeIpcProtocol.SerializeStatus(new RuntimeStatusSnapshot("board", []))));

                var parsed = RuntimeIpcProtocol.ParseCommand(command);
                if (legacyRuntime || parsed.Kind != RuntimeIpcCommandKind.ReadMetrics || parsed.MetricIds is null)
                    return Task.FromResult(new RuntimeIpcResponse(false, "Unsupported read-only request."));

                requests++;
                var instance = changeInstance && requests % 2 == 0 ? secondInstance : firstInstance;
                var published = parsed.MetricIds.ToDictionary(id => id, _ => (object?)60L,
                    StringComparer.OrdinalIgnoreCase);
                return Task.FromResult(new RuntimeIpcResponse(true,
                    RuntimeMetricSnapshotProtocol.SerializeSnapshot(instance, parsed.MetricIds, published)));
            }, new FileLogger(Path.Combine(root, "runtime.log")), pipe);
            server.Start();

            using var client = new RuntimeIpcClient(null, pipe);
            var status = client.GetStatusAsync(5000).GetAwaiter().GetResult();
            var snapshot = client.GetCurrentMetricsAsync(ids, 5000).GetAwaiter().GetResult();
            Check(status.Success && status.Status is not null &&
                  snapshot is not null && snapshot.Values.Count == ids.Length && requests == 2 &&
                  snapshot.Values.Values.All(value => value is long number && number == 60),
                "The unchanged STATUS endpoint and bounded multi-batch read-only IPC must coexist.");

            var filesystemId = FilesystemSourceContract.MetricId(
                FilesystemSourceContract.NewId(), FilesystemSourceContract.Size);
            var filesystemSnapshot = client.GetCurrentMetricsAsync([filesystemId], 5000)
                .GetAwaiter().GetResult();
            Check(filesystemSnapshot?.Values[filesystemId] is long fileSize && fileSize == 60,
                "Read-only named pipe must accept Filesystem IDs through the shared request path.");

            requests = 0;
            changeInstance = true;
            Check(client.GetCurrentMetricsAsync(ids, 5000).GetAwaiter().GetResult() is null &&
                  requests == 2,
                "A Runtime process identity change during a multi-batch cycle must discard all values.");

            legacyRuntime = true;
            Check(client.GetCurrentMetricsAsync([Status], 5000).GetAwaiter().GetResult() is null,
                "An older Runtime rejecting METRICS_V1 must leave preview values unavailable.");
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { }
        }
    }

    public static void EditorMergeAndStaleInvalidation()
    {
        var local = new Dictionary<string, object?>
        {
            ["system.test.local"] = 42L,
            [Status] = "EDITOR",
            [Fps] = 24d
        };
        var required = new HashSet<string>([Status, Fps, "system.test.local"], StringComparer.OrdinalIgnoreCase);
        var noRuntime = EditorRuntimeMetricCache.Compose(local,
            new Dictionary<string, object?>(), required);
        Check(!noRuntime.ContainsKey(Status) && !noRuntime.ContainsKey(Fps) &&
              (long)noRuntime["system.test.local"]! == 42L,
            "Offline Editor preview must neither fabricate Output values nor lose locally owned metrics.");

        long now = Stopwatch.GetTimestamp();
        var cache = new EditorRuntimeMetricCache(() => now);
        cache.Accept(new RuntimeMetricSnapshot(Guid.NewGuid(),
            new Dictionary<string, object?> { [Status] = "CONNECTED", [Fps] = 31.25 }));
        var merged = EditorRuntimeMetricCache.Compose(local, cache.Current(), required);
        Check(BinarySignalContract.TryResolve(merged[Status], "Setpoint", "CONNECTED", "=", out var state) && state &&
              BinarySignalContract.TryResolve(merged[Status], "Setpoint", "Connected", "=", out state) && !state &&
              merged[Fps] is double fps && fps == 31.25,
            "All current-value widgets must use genuine Runtime values; text Setpoint remains case-sensitive.");

        cache.Accept(new RuntimeMetricSnapshot(Guid.NewGuid(),
            new Dictionary<string, object?> { [Status] = "WAITING", [Fps] = null }));
        merged = EditorRuntimeMetricCache.Compose(local, cache.Current(), required);
        Check(BinarySignalContract.TryResolve(merged[Status], "Setpoint", "CONNECTED", "=", out state) && !state &&
              merged.ContainsKey(Fps) && merged[Fps] is null,
            "A genuinely available mismatch is False; a missing numeric value remains unavailable.");

        now += Stopwatch.Frequency * 4;
        merged = EditorRuntimeMetricCache.Compose(local, cache.Current(), required);
        Check(!merged.ContainsKey(Status) &&
              !BinarySignalContract.TryResolve(
                  merged.GetValueOrDefault(Status), "Setpoint", "CONNECTED", "=", out state),
            "An expired snapshot must not retain a stale True or turn missing telemetry into False.");

        cache.Clear();
        Check(cache.Current().Count == 0, "Runtime loss or document switch must invalidate cached readings.");
    }

    private static AppConfig Config() => new()
    {
        Outputs = new OutputConfig
        {
            Targets =
            [
                new OutputTargetConfig
                {
                    Id = "alpha", Name = "Desktop Alpha", Kind = OutputTargetContract.DesktopKind,
                    Transport = OutputTargetContract.DesktopTransport, ProviderEnabled = true,
                    Desktop = new DesktopOutputConfig { BoardEnabled = false }
                },
                new OutputTargetConfig
                {
                    Id = "beta", Name = "Desktop Beta", Kind = OutputTargetContract.DesktopKind,
                    Transport = OutputTargetContract.DesktopTransport, ProviderEnabled = false,
                    Desktop = new DesktopOutputConfig { BoardEnabled = false }
                }
            ]
        }
    };

    private static void Throws<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T) { return; }
        throw new InvalidOperationException("Expected failure was not reported: " + typeof(T).Name);
    }

    private static void Check(bool passed, string reason)
    {
        if (!passed) throw new InvalidOperationException(reason);
    }
}
