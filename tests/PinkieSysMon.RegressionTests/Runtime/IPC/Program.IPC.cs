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
    private static void RuntimeIpcOutputControlIsolation()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"PinkieSysMon-output-ipc-{Guid.NewGuid():N}");
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
            var control = new RuntimeOutputControl(manager, () => "PinkiePie_Vertical");

            var retiredStop = RuntimeIpcProtocol.ParseCommand(
                RuntimeIpcProtocol.BuildTargetCommand("OUTPUT_STOP", "screen-a"));
            Assert(!control.Execute(retiredStop).Success && !control.ShouldExitRuntimeAfter(retiredStop) &&
                   created.Count == 2 && created.All(session => session.DisposeCount == 0),
                "A retired target-stop command must fail without changing sessions or requesting shutdown.");
            var disabledA = OutputLifecycleTestConfiguration.WithEnabled(config, "screen-a", false);
            manager.Reconfigure(disabledA, manager.GetDashboardAssignments());
            var disableACommand = new RuntimeIpcCommand(RuntimeIpcCommandKind.SetPhysicalDeviceEnabled, "screen-a", Value: false);
            Assert(
                   !control.GetStatus().Outputs.Single(output => output.TargetId == "screen-a").IsActive &&
                   !control.ShouldExitRuntimeAfter(disableACommand),
                "Disabling an Output must unload its session and keep Runtime available.");
            Assert(control.GetStatus().Outputs.Single(output => output.TargetId == "screen-b").IsActive,
                "Disabling one target must leave sibling output sessions active.");

            manager.Reconfigure(OutputLifecycleTestConfiguration.WithEnabled(disabledA, "screen-b", false),
                manager.GetDashboardAssignments());
            var disableBCommand = new RuntimeIpcCommand(RuntimeIpcCommandKind.SetPhysicalDeviceEnabled, "screen-b", Value: false);
            Assert(
                   control.GetStatus().Outputs.All(output => !output.IsActive) &&
                   !control.ShouldExitRuntimeAfter(disableBCommand),
                "Disabling the final Output must leave Runtime available for configuration.");
            AssertThrows<InvalidOperationException>(() => manager.ReloadTarget(config.Outputs.Targets[0],
                manager.GetDashboardAssignments()["screen-a"], _ => { }),
                "Target reload must not bypass disabled configuration.");

            manager.Reconfigure(config, manager.GetDashboardAssignments());
            Assert(control.GetStatus().Outputs.All(output => output.IsActive),
                "Re-enabling Outputs must restore every eligible session without manual activation state.");

            var stopAllCommand = new RuntimeIpcCommand(RuntimeIpcCommandKind.StopAll);
            var stopAll = control.Execute(stopAllCommand);
            Assert(stopAll.Success &&
                   control.GetStatus().Outputs.All(output => !output.IsActive) &&
                   control.ShouldExitRuntimeAfter(stopAllCommand),
                "Stop All must unload every configured output session and request runtime-process shutdown.");
            AssertThrows<InvalidOperationException>(() => manager.ReloadTarget(config.Outputs.Targets[0],
                manager.GetDashboardAssignments()["screen-a"], _ => { }),
                "A target command must not override the global lifetime stop during shutdown.");
            Assert(!control.Execute(new RuntimeIpcCommand(RuntimeIpcCommandKind.StartOrReloadAll)).Success &&
                   control.GetStatus().Outputs.All(output => !output.IsActive),
                "All without the common-configuration controller must fail without bypassing the global lifetime stop.");
            manager.Start();
            Assert(control.GetStatus().Outputs.All(output => output.IsActive),
                "The retained global lifecycle primitive must remain retryable after Stop All.");

            Assert(RuntimeIpcProtocol.ResponseIndicatesRuntimeStopping(
                       new RuntimeIpcResponse(true, $"Outputs stopped. {RuntimeIpcProtocol.RuntimeStoppingMessage}")) &&
                   !RuntimeIpcProtocol.ResponseIndicatesRuntimeStopping(
                       new RuntimeIpcResponse(true, "Output disabled; Runtime remains available.")),
                "The Editor/runtime shutdown handshake must distinguish a response that requires waiting for process exit.");
        }
        finally
        {
            Directory.Delete(tempRoot, true);
        }
    }

    private static void RuntimeIpcShutdownCancellationContract()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"PinkieSysMon-ipc-shutdown-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        try
        {
            var log = new FileLogger(Path.Combine(tempRoot, "regression.log"));
            var server = new RuntimeIpcServer(
                static (_, cancellationToken) =>
                    cancellationToken.IsCancellationRequested
                        ? Task.FromCanceled<RuntimeIpcResponse>(cancellationToken)
                        : Task.FromResult(new RuntimeIpcResponse(true, "OK")),
                log);

            server.Dispose();
            server.Dispose();
        }
        finally
        {
            try { Directory.Delete(tempRoot, recursive: true); } catch { }
        }
    }

    private static void RuntimeOutputStartupPolicy()
    {
        RuntimeStartupOptions.Validate([]);
        foreach (var argument in new[] { "--outputs-stopped", "--OUTPUTS-STOPPED", "--unrelated" })
            AssertThrows<ArgumentException>(() => RuntimeStartupOptions.Validate([argument]),
                "Unsupported startup arguments must fail before Runtime can create sessions or open hardware.");
    }

    private static void ScreenMenuDeviceCommandModel()
    {
        var targets = CreateMultiOutputConfig().Outputs.Targets;

        var runtimeAbsent = ScreenMenuContract.Build(targets, false, null, commandInProgress: false);
        Assert(runtimeAbsent.Targets.Length == 2 && runtimeAbsent.Targets[0].TargetName == "Left Trofeo" && runtimeAbsent.Targets[1].TargetName == "Right Trofeo",
            "Output first-level items must be based on configured user-facing output names.");
        Assert(runtimeAbsent.Targets.All(target => target.CanStartOrReload) &&
               runtimeAbsent.CanStartOrReloadAll && !runtimeAbsent.CanStopAll,
            "When Runtime is absent, reload workflow must remain available while global shutdown remains disabled.");
        Assert(runtimeAbsent.Targets.All(target => target.StatusText == "Status: Runtime not running"),
            "Output target status must explicitly represent an absent Runtime.");

        var snapshot = new RuntimeStatusSnapshot(
            "PinkiePie_Vertical",
            [
                new RuntimeOutputStatus("screen-a", "Left Trofeo", true, false, "WAITING"),
                new RuntimeOutputStatus("screen-b", "Right Trofeo", false, false, "DISABLED") { OutputEnabled = false }
            ]);
        var running = ScreenMenuContract.Build(targets, true, snapshot, commandInProgress: false);
        Assert(running.Targets[0].CanStartOrReload &&
               running.Targets[0].StatusText == "Status: Waiting for device",
            "An active target with no physical connection must remain addressable and visibly report the missing-device state.");
        Assert(!running.Targets[1].CanStartOrReload &&
               running.Targets[1].StatusText == "Status: Disabled",
            "A disabled target must deny reload and report its configured state.");
        Assert(running.CanStartOrReloadAll && running.CanStopAll,
            "Global Output commands must reflect the aggregate configured-session state.");
        var allActiveSnapshot = new RuntimeStatusSnapshot(
            "PinkiePie_Vertical",
            [
                new RuntimeOutputStatus("screen-a", "Left Trofeo", true, true, "CONNECTED"),
                new RuntimeOutputStatus("screen-b", "Right Trofeo", true, true, "CONNECTED")
            ]);
        Assert(ScreenMenuContract.Build(targets, true, allActiveSnapshot, false).CanStopAll,
            "Global shutdown must remain available when actual Output sessions are active.");

        var failureSnapshot = new RuntimeStatusSnapshot(
            "PinkiePie_Vertical",
            [
                new RuntimeOutputStatus("screen-a", "Left Trofeo", true, false, "AMBIGUOUS"),
                new RuntimeOutputStatus("screen-b", "Right Trofeo", true, false, "ERROR")
            ]);
        var failureState = ScreenMenuContract.Build(targets, true, failureSnapshot, commandInProgress: false);
        Assert(failureState.Targets[0].StatusText == "Status: Device selection required" &&
               failureState.Targets[1].StatusText == "Status: Output error",
            "Output target status must distinguish device-selection ambiguity from a generic output connection error.");

        var busy = ScreenMenuContract.Build(targets, true, snapshot, commandInProgress: true);
        Assert(busy.Targets.All(target => !target.CanStartOrReload) &&
               !busy.CanStartOrReloadAll && !busy.CanStopAll,
            "A pending Output command must disable every competing per-target and global Output action.");

        Assert(ScreenMenuContract.StartReloadText == "Reload" &&
               ScreenMenuContract.StartReloadAllText == "Reload All" &&
               ScreenMenuContract.StopAllText == "Stop / Unload All Outputs",
            "Editor labels must expose Reload and Reload All while retaining the existing global shutdown scope.");
    }


}
