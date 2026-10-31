using System.ComponentModel;
using System.Drawing.Design;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows.Forms;
using PinkieSysMon;
using PinkieSysMon.Editor;
using C = PinkieSysMon.DashboardModel;

namespace PinkieSysMon.RegressionTests;

internal static partial class Program
{
    private static void OutputConfigLegacyDefaultAndPersistence()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"PinkieSysMon-output-config-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        try
        {
            var path = Path.Combine(tempRoot, "app.json");
            File.WriteAllText(path, "{\"Dashboard\":{\"Active\":\"Default\"}}");
            var log = new FileLogger(Path.Combine(tempRoot, "regression.log"));

            var config = AppConfig.Load(path, log);
            var target = OutputTargetContract.GetPrimary(config);
            Assert(target.Id == "desktop-1" &&
                   target.Kind == OutputTargetContract.DesktopKind &&
                   target.Transport == OutputTargetContract.DesktopTransport &&
                   target.DeviceId.Length == 0,
                "Configuration without Outputs must safely default to a disabled-hardware Desktop model.");

            config.Outputs.Targets =
            [
                new OutputTargetConfig { Transport = OutputTargetContract.LyBulkTransport, Width = 1920, Height = 480, Id = "physical-1", Name = "Legacy Trofeo",
                    DeviceId = "usb-interface:" + new string('a', 64) }
            ];
            config.Save(path);
            var reloaded = AppConfig.Load(path, log);
            var persistedTarget = OutputTargetContract.GetPrimary(reloaded);
            Assert(persistedTarget.DeviceId == config.Outputs.Targets[0].DeviceId &&
                   !persistedTarget.DeviceEnabled,
                "Explicit output-device binding must persist while physical hardware remains disabled by default.");
        }
        finally
        {
            Directory.Delete(tempRoot, true);
        }
    }

    private static void OutputConfigMultiTargetSafety()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"PinkieSysMon-output-multi-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        try
        {
            var path = Path.Combine(tempRoot, "app.json");
            var log = new FileLogger(Path.Combine(tempRoot, "regression.log"));
            var config = CreateMultiOutputConfig();

            config.Save(path);
            var reloaded = AppConfig.Load(path, log);
            Assert(reloaded.Outputs.Targets.Count == 2,
                "The runtime configuration must persist more than one explicit output target.");
            Assert(reloaded.Outputs.Targets.Select(target => target.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() == 2,
                "Configured output target IDs must remain unique after persistence.");

            var unbound = CreateMultiOutputConfig();
            unbound.Outputs.Targets[1].DeviceId = string.Empty;
            AssertThrows<InvalidDataException>(() => unbound.Save(path),
                "Multiple configured output targets must require explicit physical-device bindings.");

            var duplicateDevice = CreateMultiOutputConfig();
            duplicateDevice.Outputs.Targets[1].DeviceId = duplicateDevice.Outputs.Targets[0].DeviceId;
            AssertThrows<InvalidDataException>(() => duplicateDevice.Save(path),
                "Two logical output targets must not be allowed to bind to the same physical device.");
        }
        finally
        {
            Directory.Delete(tempRoot, true);
        }
    }

    private static void FramePumpPerDeviceUsbSettings()
    {
        var root = CreateTemporaryApplicationRoot("PinkieSysMon-pump-target-usb");
        using var sendStarted = new ManualResetEventSlim();
        using var allowSend = new ManualResetEventSlim();
        var firstTransport = new BlockingTrofeoTestTransport(sendStarted, allowSend);
        var secondTransport = new BlockingTrofeoTestTransport();
        var attempts = new System.Collections.Concurrent.ConcurrentQueue<int>();
        var opens = 0;

        var config = new AppConfig();
        var target = new OutputTargetConfig
        { Transport = OutputTargetContract.LyBulkTransport, Width = 1920, Height = 480, DeviceId = "usb-interface:9db7b64ace1096dfd3006978c56ad9c802a669f230c9f3d8e240ad1deb08227a",
            Usb = new UsbConfig
            {
                RetryIntervalMs = 700,
                TransferTimeoutMs = 1100
            }
        };
        var board = new C.DashboardDefinition { BaseDirectory = Path.Combine(root, "dashboards") };
        var pump = new FramePump(config, target, board, new MetricStore(),
            new FileLogger(Path.Combine(root, "target-usb.log")),
            (selected, timeout, log, token) =>
            {
                attempts.Enqueue(timeout);
                return Interlocked.Increment(ref opens) == 1 ? firstTransport : secondTransport;
            });

        try
        {
            pump.Start();
            Assert(sendStarted.Wait(TimeSpan.FromSeconds(5)),
                "Initial physical session must reach its injected transport without USB hardware.");
            Assert(attempts.TryPeek(out var initial) &&
                   initial == 1100,
                "Transport handshake must use the physical target's USB settings, not global defaults.");

            var newConfig = new AppConfig();
            var changedTarget = new OutputTargetConfig
            { Transport = OutputTargetContract.LyBulkTransport, Width = 1920, Height = 480, DeviceId = "usb-interface:6edd1ff49a5e145027e2897c97cd4c97bb1a85b62b065462ad82d300f93becdc",
                Usb = new UsbConfig
                {
                    RetryIntervalMs = 1500,
                    TransferTimeoutMs = 1800
                }
            };
            using var plan = pump.PrepareReconfiguration(newConfig, changedTarget, board);
            pump.CommitReconfiguration(plan);
            Assert(firstTransport.DisposeCount == 0,
                "Reconfigure may not release a physical transport during its in-flight send.");

            allowSend.Set();
            Assert(SpinWait.SpinUntil(() => firstTransport.DisposeCount == 1 &&
                   Volatile.Read(ref opens) >= 2, TimeSpan.FromSeconds(5)),
                "Changing per-target USB timeout must retire and reopen only this output.");
            var values = attempts.ToArray();
            Assert(values.Length >= 2 &&
                   values[1] == 1800,
                "Reconnected physical output must use updated per-target USB parameters.");
            Assert(config.Usb.TransferTimeoutMs == 3000 &&
                   newConfig.Usb.TransferTimeoutMs == 3000,
                "Per-device reconfiguration must not mutate global USB defaults.");
        }
        finally
        {
            allowSend.Set();
            pump.Dispose();
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    private static void OutputSessionManagerLifecycle()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"PinkieSysMon-output-sessions-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        try
        {
            var log = new FileLogger(Path.Combine(tempRoot, "regression.log"));
            var metrics = new MetricStore();
            var created = new List<FakeOutputSession>();
            var config = CreateMultiOutputConfig();
            using var manager = new OutputSessionManager(
                config,
                new C.DashboardDefinition(),
                metrics,
                log,
                (app, target, dashboard, store, logger) =>
                {
                    var session = new FakeOutputSession(target.Id, target.Name);
                    created.Add(session);
                    return session;
                });

            manager.Start();
            Assert(created.Count == 2 && created.All(session => session.StartCount == 1),
                "Starting the runtime must create and start one independent session per configured output target.");
            Assert(created.All(session => session.PublishesRuntimeMetrics),
                "Every active output session must publish independently when its provider is enabled.");

            var statuses = manager.GetStatuses();
            Assert(statuses.Count == 2 && statuses.All(status => status.IsActive),
                "Every configured target must report an independent active session after startup.");

            manager.Reconfigure(OutputLifecycleTestConfiguration.WithEnabled(config, "screen-a", false),
                manager.GetDashboardAssignments());
            Assert(created[0].DisposeCount == 1 && created[1].DisposeCount == 0,
                "Disabling one output target must dispose only that target session.");
            Assert(created[1].PublishesRuntimeMetrics,
                "Disabling one output must leave the sibling's metric publisher enabled.");

            manager.Reconfigure(config, manager.GetDashboardAssignments());
            Assert(created.Count == 3 && created[2].StartCount == 1,
                "Re-enabling a target must create a fresh session without restarting unrelated targets.");
            Assert(created[2].PublishesRuntimeMetrics && created[1].PublishesRuntimeMetrics,
                "Restarting an output must restore its own publisher without suppressing the sibling.");

            manager.Suspend();
            Assert(created[1].SuspendCount == 1 && created[2].SuspendCount == 1,
                "Suspend must be propagated independently to every active output session.");
            manager.Resume();
            Assert(created[1].ResumeCount == 1 && created[2].ResumeCount == 1,
                "Resume must be propagated independently to every active output session.");

            manager.StopAll();
            statuses = manager.GetStatuses();
            Assert(statuses.All(status => !status.IsActive && status.UsbState == "STOPPED"),
                "StopAll must unload every configured output session without removing target configuration.");
        }
        finally
        {
            Directory.Delete(tempRoot, true);
        }
    }

    private static void OutputSessionManagerReconfiguration()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"PinkieSysMon-output-reconfigure-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        try
        {
            var log = new FileLogger(Path.Combine(tempRoot, "regression.log"));
            var created = new List<FakeOutputSession>();
            var initial = CreateMultiOutputConfig();
            using var manager = new OutputSessionManager(
                initial,
                new C.DashboardDefinition(),
                new MetricStore(),
                log,
                (app, target, dashboard, store, logger) =>
                {
                    var session = new FakeOutputSession(target.Id, target.Name);
                    created.Add(session);
                    return session;
                });

            manager.Start();
            var firstA = created.Single(session => session.TargetId == "screen-a");
            var firstB = created.Single(session => session.TargetId == "screen-b");

            var replacement = new AppConfig
            {
                Outputs = new OutputConfig
                {
                    Targets =
                    [
                        new OutputTargetConfig
                        { Transport = OutputTargetContract.LyBulkTransport, Width = 1920, Height = 480,
                            Id = "screen-b",
                            Name = "Right Trofeo",
                            DeviceId = "usb-interface:" + new string('b', 64),
                            DeviceEnabled = true
                        },
                        new OutputTargetConfig
                        { Transport = OutputTargetContract.LyBulkTransport, Width = 1920, Height = 480,
                            Id = "screen-c",
                            Name = "Aux Trofeo",
                            DeviceId = "usb-interface:" + new string('c', 64),
                            DeviceEnabled = true
                        }
                    ]
                }
            };

            manager.Reconfigure(replacement, new C.DashboardDefinition());
            var statuses = manager.GetStatuses();
            Assert(firstA.DisposeCount == 1,
                "Removing a configured target must unload only its existing session.");
            Assert(firstB.CommitCount == 1 && firstB.TargetName == "Right Trofeo",
                "A same-ID target must be reconfigured in place rather than destroyed and recreated.");
            Assert(created.Any(session => session.TargetId == "screen-c" && session.StartCount == 1),
                "A newly configured target must acquire and start its own session during live reconfiguration.");
            Assert(statuses.Select(status => status.TargetId).SequenceEqual(new[] { "screen-b", "screen-c" }, StringComparer.OrdinalIgnoreCase),
                "Session status order must follow configured target order after reconciliation.");

            var disabledB = OutputLifecycleTestConfiguration.WithEnabled(replacement, "screen-b", false);
            manager.Reconfigure(disabledB, manager.GetDashboardAssignments());
            manager.Reconfigure(disabledB, manager.GetDashboardAssignments());
            statuses = manager.GetStatuses();
            Assert(statuses.Single(status => status.TargetId == "screen-b") is { IsActive: false, OutputEnabled: false, UsbState: "DISABLED" },
                "A disabled target must remain inactive across configuration reloads using its saved enablement.");
            manager.Reconfigure(replacement, manager.GetDashboardAssignments());
            Assert(manager.GetStatuses().All(status => status.IsActive),
                "Re-enabling a configured target must restore its session without a separate activation command.");
        }
        finally
        {
            Directory.Delete(tempRoot, true);
        }
    }

    private static void OutputSessionManagerTransactionalPreparation()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"PinkieSysMon-output-transaction-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        try
        {
            var log = new FileLogger(Path.Combine(tempRoot, "regression.log"));
            var created = new List<FakeOutputSession>();
            var config = CreateMultiOutputConfig();
            using var manager = new OutputSessionManager(
                config,
                new C.DashboardDefinition(),
                new MetricStore(),
                log,
                (app, target, dashboard, store, logger) =>
                {
                    var session = new FakeOutputSession(target.Id, target.Name);
                    created.Add(session);
                    return session;
                });

            manager.Start();
            var first = created.Single(session => session.TargetId == "screen-a");
            var second = created.Single(session => session.TargetId == "screen-b");
            second.ThrowOnPrepare = true;

            AssertThrows<InvalidOperationException>(
                () => manager.Reconfigure(config, new C.DashboardDefinition()),
                "A failed session preparation must reject the whole output-set reconfiguration.");
            Assert(first.CommitCount == 0 && second.CommitCount == 0,
                "No active output session may be committed when any sibling session fails preparation.");
            Assert(manager.GetStatuses().All(status => status.IsActive),
                "Failed output-set reconfiguration must leave the previous active session set intact.");
        }
        finally
        {
            Directory.Delete(tempRoot, true);
        }
    }

    private static void OutputSessionCommitRollback()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"PinkieSysMon-output-commit-rollback-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        try
        {
            var log = new FileLogger(Path.Combine(tempRoot, "regression.log"));
            var created = new List<FakeOutputSession>();
            var initial = CreateMultiOutputConfig();
            using var manager = new OutputSessionManager(
                initial,
                new C.DashboardDefinition(),
                new MetricStore(),
                log,
                (app, target, dashboard, store, logger) =>
                {
                    var session = new FakeOutputSession(target.Id, target.Name);
                    created.Add(session);
                    return session;
                });

            manager.Start();
            var first = created.Single(session => session.TargetId == "screen-a");
            var second = created.Single(session => session.TargetId == "screen-b");
            second.ThrowOnNextCommit = true;

            var replacement = CreateMultiOutputConfig();
            replacement.Outputs.Targets[0].Name = "Left Trofeo New";
            replacement.Outputs.Targets[1].Name = "Right Trofeo New";

            AssertThrows<InvalidOperationException>(
                () => manager.Reconfigure(replacement, new C.DashboardDefinition()),
                "A commit failure must reject the whole output-set reconfiguration.");

            Assert(first.TargetName == "Left Trofeo" && second.TargetName == "Right Trofeo",
                "A sibling commit failure must roll every existing output session back to its previous configuration.");
            Assert(first.CommitCount == 2 && second.CommitCount == 2,
                "Commit rollback must restore both already-committed and partially-mutated sibling sessions.");
            Assert(manager.GetStatuses().Select(status => status.TargetName)
                    .SequenceEqual(new[] { "Left Trofeo", "Right Trofeo" }, StringComparer.Ordinal),
                "Failed output commit must keep the manager's externally visible session state on the previous configuration.");
        }
        finally
        {
            Directory.Delete(tempRoot, true);
        }
    }

    private static void OutputSessionStartupRollback()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"PinkieSysMon-output-start-rollback-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        try
        {
            var log = new FileLogger(Path.Combine(tempRoot, "regression.log"));
            var created = new List<FakeOutputSession>();
            var failSecondTarget = true;
            using var manager = new OutputSessionManager(
                CreateMultiOutputConfig(),
                new C.DashboardDefinition(),
                new MetricStore(),
                log,
                (app, target, dashboard, store, logger) =>
                {
                    var session = new FakeOutputSession(target.Id, target.Name)
                    {
                        ThrowOnStart = failSecondTarget && target.Id.Equals("screen-b", StringComparison.OrdinalIgnoreCase)
                    };
                    created.Add(session);
                    return session;
                });

            AssertThrows<InvalidOperationException>(
                manager.Start,
                "A startup failure in one output session must fail the initial output-set activation.");
            Assert(manager.GetStatuses().All(status => !status.IsActive),
                "Failed initial output startup must not leave any partially active sessions registered.");
            Assert(created.Count == 2 && created.All(session => session.DisposeCount == 1),
                "Every session created for a failed initial startup must be disposed exactly once.");

            failSecondTarget = false;
            manager.Start();
            Assert(manager.GetStatuses().All(status => status.IsActive),
                "A failed initial output startup must leave the manager retryable.");
            Assert(created.Skip(2).Count() == 2 && created.Skip(2).All(session => session.StartCount == 1),
                "Retry after startup rollback must create and start a fresh session set.");
        }
        finally
        {
            Directory.Delete(tempRoot, true);
        }
    }

    private static void OutputSessionGlobalRestartRollback()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"PinkieSysMon-output-start-all-rollback-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        try
        {
            var log = new FileLogger(Path.Combine(tempRoot, "regression.log"));
            var created = new List<FakeOutputSession>();
            var failRestartA = false;
            using var manager = new OutputSessionManager(
                CreateMultiOutputConfig(),
                new C.DashboardDefinition(),
                new MetricStore(),
                log,
                (app, target, dashboard, store, logger) =>
                {
                    var session = new FakeOutputSession(target.Id, target.Name)
                    {
                        ThrowOnStart = failRestartA && target.Id.Equals("screen-a", StringComparison.OrdinalIgnoreCase)
                    };
                    created.Add(session);
                    return session;
                });

            manager.Start();
            var original = created.ToArray();
            manager.StopAll();
            failRestartA = true;

            AssertThrows<InvalidOperationException>(
                manager.Start,
                "Global restart must fail cleanly when a newly required session cannot start.");
            var failedStatuses = manager.GetStatuses();
            Assert(failedStatuses.All(status => !status.IsActive && status.UsbState == "STOPPED"),
                "Failed global restart must preserve the real global lifetime stop.");
            Assert(original.All(session => session.DisposeCount == 1) &&
                   created.Skip(2).Count() == 2 && created.Skip(2).All(session => session.DisposeCount == 1),
                "Global stop and failed restart must dispose every original and candidate session exactly once.");

            failRestartA = false;
            manager.Start();
            Assert(manager.GetStatuses().All(status => status.IsActive),
                "Global restart must remain retryable after a startup rollback.");
            Assert(created.Skip(4).Count() == 2 && created.Skip(4).All(session => session.StartCount == 1),
                "Retry after global restart rollback must create and start a fresh session set.");
        }
        finally
        {
            Directory.Delete(tempRoot, true);
        }
    }

    private static void OutputSessionLiveReconfigureStartupRollback()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"PinkieSysMon-output-reconfigure-start-rollback-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        try
        {
            var log = new FileLogger(Path.Combine(tempRoot, "regression.log"));
            var created = new List<FakeOutputSession>();
            var failNewTarget = true;
            var initial = CreateMultiOutputConfig();
            using var manager = new OutputSessionManager(
                initial,
                new C.DashboardDefinition(),
                new MetricStore(),
                log,
                (app, target, dashboard, store, logger) =>
                {
                    var session = new FakeOutputSession(target.Id, target.Name)
                    {
                        ThrowOnStart = failNewTarget && target.Id.Equals("screen-c", StringComparison.OrdinalIgnoreCase)
                    };
                    created.Add(session);
                    return session;
                });

            manager.Start();
            var liveA = created.Single(session => session.TargetId == "screen-a");
            var liveB = created.Single(session => session.TargetId == "screen-b");
            var replacement = new AppConfig
            {
                Outputs = new OutputConfig
                {
                    Targets =
                    [
                        new OutputTargetConfig
                        { Transport = OutputTargetContract.LyBulkTransport, Width = 1920, Height = 480,
                            Id = "screen-b",
                            Name = "Right Trofeo",
                            DeviceId = "usb-interface:" + new string('b', 64),
                            DeviceEnabled = true
                        },
                        new OutputTargetConfig
                        { Transport = OutputTargetContract.LyBulkTransport, Width = 1920, Height = 480,
                            Id = "screen-c",
                            Name = "Aux Trofeo",
                            DeviceId = "usb-interface:" + new string('c', 64),
                            DeviceEnabled = true
                        }
                    ]
                }
            };

            AssertThrows<InvalidOperationException>(
                () => manager.Reconfigure(replacement, new C.DashboardDefinition()),
                "A newly configured output session that cannot start must reject the live output-set reconfiguration.");
            var failedStatuses = manager.GetStatuses();
            Assert(failedStatuses.Select(status => status.TargetId)
                    .SequenceEqual(new[] { "screen-a", "screen-b" }, StringComparer.OrdinalIgnoreCase) &&
                   failedStatuses.All(status => status.IsActive),
                "Failed live reconfiguration startup must preserve the previous configured and active output set.");
            Assert(liveA.DisposeCount == 0 && liveB.CommitCount == 0,
                "Failed new-target startup must not unload a removed target or commit an existing sibling.");
            var failedC = created.Last(session => session.TargetId == "screen-c");
            Assert(failedC.DisposeCount == 1,
                "The failed newly configured session must be disposed without entering the active session table.");

            failNewTarget = false;
            manager.Reconfigure(replacement, new C.DashboardDefinition());
            Assert(manager.GetStatuses().Select(status => status.TargetId)
                    .SequenceEqual(new[] { "screen-b", "screen-c" }, StringComparer.OrdinalIgnoreCase),
                "Live output reconfiguration must remain retryable after a new-session startup rollback.");
        }
        finally
        {
            Directory.Delete(tempRoot, true);
        }
    }

    private static void OutputSessionLifecycleFailureIsolation()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"PinkieSysMon-output-lifecycle-isolation-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        try
        {
            var log = new FileLogger(Path.Combine(tempRoot, "regression.log"));
            var created = new List<FakeOutputSession>();
            using var manager = new OutputSessionManager(
                CreateMultiOutputConfig(),
                new C.DashboardDefinition(),
                new MetricStore(),
                log,
                (app, target, dashboard, store, logger) =>
                {
                    var session = new FakeOutputSession(target.Id, target.Name);
                    created.Add(session);
                    return session;
                });

            manager.Start();
            var first = created.Single(session => session.TargetId == "screen-a");
            var second = created.Single(session => session.TargetId == "screen-b");
            first.ThrowOnSuspend = true;
            first.ThrowOnResume = true;
            first.ThrowOnDispose = true;

            manager.Suspend();
            Assert(first.SuspendCount == 1 && second.SuspendCount == 1,
                "A suspend failure in one output session must not prevent sibling sessions from receiving suspend.");

            manager.Resume();
            Assert(first.ResumeCount == 1 && second.ResumeCount == 1,
                "A resume failure in one output session must not prevent sibling sessions from receiving resume.");

            manager.StopAll();
            Assert(first.DisposeCount == 1 && second.DisposeCount == 1,
                "A disposal failure in one output session must not prevent sibling cleanup during Stop All.");
            Assert(manager.GetStatuses().All(status => !status.IsActive),
                "Lifecycle cleanup failures must not leave disposed output sessions registered as active.");
        }
        finally
        {
            Directory.Delete(tempRoot, true);
        }
    }

    private static void RuntimeIpcOutputCommandProtocol()
    {
        const string targetId = "screen\t\"alpha\" ";

        var start = RuntimeIpcProtocol.ParseCommand(
            RuntimeIpcProtocol.BuildTargetCommand(RuntimeIpcProtocol.StartOrReloadTargetCommand, targetId));
        Assert(start.Kind == RuntimeIpcCommandKind.StartOrReloadTarget && start.TargetId == targetId,
            "Target-scoped IPC commands must preserve opaque output target IDs without delimiter ambiguity.");

        var retiredStop = RuntimeIpcProtocol.ParseCommand(
            RuntimeIpcProtocol.BuildTargetCommand("OUTPUT_STOP", targetId));
        Assert(retiredStop.Kind == RuntimeIpcCommandKind.Invalid,
            "Retired target-stop IPC must be rejected instead of recreating manual activation state.");

        Assert(RuntimeIpcProtocol.ParseCommand(RuntimeIpcProtocol.StartOrReloadAllCommand).Kind == RuntimeIpcCommandKind.StartOrReloadAll,
            "IPC must expose a distinct Start/Reload All output command.");
        Assert(RuntimeIpcProtocol.ParseCommand(RuntimeIpcProtocol.StopAllCommand).Kind == RuntimeIpcCommandKind.StopAll,
            "IPC must expose a distinct Stop All output command.");
        Assert(RuntimeIpcProtocol.ParseCommand(RuntimeIpcProtocol.StatusCommand).Kind == RuntimeIpcCommandKind.Status,
            "IPC must expose output-session status independently from the legacy process ping.");
        Assert(RuntimeIpcProtocol.ParseCommand(RuntimeIpcProtocol.PingCommand).Kind == RuntimeIpcCommandKind.Ping,
            "Legacy PING protocol compatibility must remain even when unused client helpers are removed.");
        Assert(RuntimeIpcProtocol.ParseCommand(RuntimeIpcProtocol.ExitCommand).Kind == RuntimeIpcCommandKind.Exit,
            "Legacy EXIT protocol compatibility must remain even when unused client helpers are removed.");

        var malformed = RuntimeIpcProtocol.ParseCommand(RuntimeIpcProtocol.StartOrReloadTargetCommand + "\tnot-json");
        Assert(malformed.Kind == RuntimeIpcCommandKind.Invalid && !string.IsNullOrWhiteSpace(malformed.Error),
            "Malformed target-scoped IPC commands must be rejected deterministically.");
    }

    private static void RuntimeIpcOutputStatusPayload()
    {
        var expected = new RuntimeStatusSnapshot(
            "PinkiePie_Vertical",
            [
                new RuntimeOutputStatus("screen-a", "Left Trofeo", true, true, "CONNECTED"),
                new RuntimeOutputStatus("screen-b", "Right Trofeo", false, false, "STOPPED")
            ]);

        var payload = RuntimeIpcProtocol.SerializeStatus(expected);
        var actual = RuntimeIpcProtocol.DeserializeStatus(payload);

        Assert(actual.Dashboard == expected.Dashboard && actual.Outputs.Length == 2,
            "Runtime IPC status must preserve dashboard identity and every configured output target.");
        Assert(actual.Outputs[0] == expected.Outputs[0] && actual.Outputs[1] == expected.Outputs[1],
            "Runtime IPC status must preserve target identity, active state, connection state, and USB state.");
    }

}
