namespace PinkieSysMon.RegressionTests;

// Command/status/notification fakes only: no toast, UI window, process, pipe or USB open.
internal static class RuntimeNotificationStage26Tests
{
    public static void SharedCommandResults()
    {
        var f = new Fixture(Desktop());
        var command = new RuntimeIpcCommand(RuntimeIpcCommandKind.StartOrReloadTarget, "panel-a");
        var response = f.Run(command, () => f.Put(Desktop(active: true)));
        Check(response.Success && f.Applied == 1 && f.Events.Count == 1 &&
            f.Events[0].Kind == RuntimeNotificationKind.Information &&
            Has(response.Message, "Panel A", "panel-a", "Board A", "DISPLAYING", "panel visible"),
            "A user command must run once and report its actual target, assignment and visible state.");
        Check(f.Events[0].Message == response.Message, "Tray/IPC feedback must publish the same actionable result returned to the caller.");

        response = f.Run(new(RuntimeIpcCommandKind.SetDesktopBoardEnabled, "panel-a", Value: false),
            () => f.Put(Desktop() with { BoardEnabled = false, OutputEnabled = false, UsbState = "DISABLED" }));
        Check(response.Success && f.Events.Count == 2 && Has(response.Message, "disable/hide", "session inactive"),
            "Disable must confirm the saved policy and an observed unloaded session.");
        response = f.Run(new(RuntimeIpcCommandKind.StartOrReloadTarget, "panel-a"), () => { });
        Check(!response.Success && f.Events[^1].Kind == RuntimeNotificationKind.Error &&
            response.Message.Contains("requested state is not confirmed", StringComparison.Ordinal),
            "A positive acknowledgement without the requested active session must not become success.");
    }

    public static void PendingAndTerminalOutcomes()
    {
        var f = new Fixture(Physical());
        var start = new RuntimeIpcCommand(RuntimeIpcCommandKind.StartOrReloadTarget, "device-a");
        var response = f.Run(start, () => f.Put(Physical(active: true)));
        Check(response.Success && f.Events.Count == 1 && f.Events[0].Kind == RuntimeNotificationKind.Warning &&
            Has(response.Message, "WAITING", "readiness is pending", "transport not connected"),
            "A running worker waiting for USB must be pending, never a connected/displayed success.");
        f.Observe(); f.Observe();
        Check(f.Events.Count == 1, "Repeated waiting snapshots must remain quiet.");
        f.Put(Physical(active: true, state: "ERROR")); f.Observe();
        Check(f.Events.Count == 2 && f.Events[^1].Kind == RuntimeNotificationKind.Error &&
            Has(f.Events[^1].Message, "device-a", "ERROR", "settings/sessions may remain"),
            "An asynchronous USB/open error must complete the pending user request with one failure notice.");
        f.Put(Physical(active: true, connected: true)); f.Observe(); f.Observe();
        Check(f.Events.Count == 2, "An automatic retry after terminal failure must not create a new success notification.");

        f.Run(start, () => f.Put(Physical(active: true)));
        f.Put(Physical(active: true, connected: true)); f.Observe();
        Check(f.Events.Count == 4 && f.Events[^1].Kind == RuntimeNotificationKind.Information &&
            Has(f.Events[^1].Message, "CONNECTED", "frame delivery not verified"),
            "A new explicit command can later confirm connection without claiming a delivered frame.");

        var desktop = new Fixture(Desktop());
        desktop.Run(new(RuntimeIpcCommandKind.StartOrReloadTarget, "panel-a"),
            () => desktop.Put(Desktop(active: true, state: "SUSPENDED") with { IsConnected = false, IsVisible = false }));
        Check(desktop.Events[0].Kind == RuntimeNotificationKind.Warning, "Suspension must not claim visible presentation.");
        desktop.Put(Desktop(active: true)); desktop.Observe(); desktop.Observe();
        Check(desktop.Events.Count == 2 && desktop.Events[^1].Kind == RuntimeNotificationKind.Information,
            "The existing status observer may complete a suspended user request once after real visibility appears.");

        var ambiguous = new Fixture(Physical());
        response = ambiguous.Run(start, () => ambiguous.Put(Physical(active: true, state: "AMBIGUOUS")));
        Check(!response.Success && ambiguous.Events.Single().Kind == RuntimeNotificationKind.Error,
            "An observed ambiguous physical identity must not be reported green.");
    }

    public static void ConfigurationAndIsolation()
    {
        var disabled = Desktop() with { BoardEnabled = false, OutputEnabled = false, UsbState = "DISABLED" };
        var f = new Fixture(disabled, Physical(active: true, state: "ERROR"));
        var response = f.Run(new(RuntimeIpcCommandKind.AssignDashboard, "panel-a", Dashboard: "Edited"),
            () => f.Put(disabled with { Dashboard = "Edited" }));
        Check(response.Success && Has(response.Message, "Edited", "session inactive") &&
            !response.Message.Contains("device-a", StringComparison.Ordinal),
            "An inactive target assignment is complete without starting it or adopting a sibling failure.");

        response = f.Run(new(RuntimeIpcCommandKind.SetDesktopBoardEnabled, "panel-a", Value: true),
            () => f.Put(Desktop(active: true) with { Dashboard = "Edited" }));
        Check(response.Success && Has(response.Message, "enable/show", "panel visible"), "Board enable must report observed presentation.");
        response = f.Run(new(RuntimeIpcCommandKind.SetDesktopBoardEnabled, "panel-a", Value: false),
            () => f.Put(disabled with { Dashboard = "Edited" }));
        Check(response.Success && Has(response.Message, "disable/hide", "session inactive"), "Board disable must verify no active hidden renderer.");

        response = f.Run(new(RuntimeIpcCommandKind.SetDesktopLock, "panel-a", Value: true),
            () => f.Put(f.Current.Outputs[0] with { Lock = true }));
        Check(response.Success && !f.Current.Outputs[0].IsActive,
            "Lock configuration must complete without activating a disabled Desktop.");
        response = f.Run(new(RuntimeIpcCommandKind.SetDesktopLock, "panel-a", Value: false), () => { });
        Check(!response.Success, "An unchanged Lock value after acknowledgement must fail verification.");

        var device = new Fixture(Physical() with { DeviceEnabled = false, OutputEnabled = false });
        response = device.Run(new(RuntimeIpcCommandKind.SetPhysicalDeviceEnabled, "device-a", Value: true),
            () => device.Put(Physical(active: true)));
        Check(response.Success && device.Events.Single().Kind == RuntimeNotificationKind.Warning &&
            Has(response.Message, "Device enable", "Device A", "WAITING"),
            "Consent-owned physical enable feedback must separate permission/session apply from pending USB readiness.");
        Check(RuntimeIpcProtocol.ParseCommand("PHYSICAL_SET_DEVICE_ENABLED\t{\"TargetId\":\"device-a\",\"Value\":true}").Kind == RuntimeIpcCommandKind.Invalid,
            "Internal physical enable feedback must not expose an IPC ownership-granting command.");
    }

    public static void FailuresAndUnknownResults()
    {
        var f = new Fixture(Desktop());
        var command = new RuntimeIpcCommand(RuntimeIpcCommandKind.AssignDashboard, "panel-a", Dashboard: "Edited");
        var response = f.Run(command, () =>
        {
            f.Put(Desktop() with { Dashboard = "Edited" });
            throw new IOException("Persistence recovery failed.");
        });
        Check(!response.Success && f.CommandErrors == 1 && f.Events.Count == 1 &&
            Has(response.Message, "Persistence recovery failed", "Edited", "state changed", "rollback is not confirmed"),
            "A failed/partial apply must retain its cause and observed change without inventing complete rollback.");

        var rejected = new Fixture(Desktop()) { ApplyResult = new(false, "Saved dashboard is missing.") };
        response = rejected.Run(command, () => { });
        Check(!response.Success && rejected.Events.Single().Kind == RuntimeNotificationKind.Error &&
            response.Message.Contains("Saved dashboard is missing", StringComparison.Ordinal),
            "An explicit controller rejection must retain its actionable cause and must not be converted to success.");

        var unreadable = new Fixture(Desktop()) { FailReadAt = 1 };
        response = unreadable.Run(command, () => throw new Exception("Must not execute."));
        Check(!response.Success && unreadable.Applied == 0 && unreadable.Events.Count == 1,
            "Unavailable pre-command status must prevent a guessed action.");
        var lostStatus = new Fixture(Desktop()) { FailReadAt = 2 };
        response = lostStatus.Run(command, () => lostStatus.Put(Desktop() with { Dashboard = "Edited" }));
        Check(!response.Success && lostStatus.Applied == 1 && lostStatus.Events.Single().Kind == RuntimeNotificationKind.Error &&
            Has(response.Message, "could not be verified", "may have been applied", "before retrying"),
            "A successful apply followed by unavailable status has an unknown result, not green success or an automatic retry.");

        Check(Has(RuntimeIpc.DescribeUnavailableResponse(true, "Timed out."), "may have been applied", "Query Runtime status before retrying") &&
            Has(RuntimeIpc.DescribeUnavailableResponse(false, "Timed out."), "could not be contacted", "no command write was attempted") &&
            Has(RuntimeIpc.DescribeUnavailableResponse(true, "Timed out.", readOnly: true), "read-only", "changes no settings"),
            "Client timeout/disconnect feedback must distinguish pre-write unavailability from an unknown submitted-command outcome.");
    }

    public static void AggregateAllOutcomes()
    {
        var disabled = Physical("disabled-device") with { DeviceEnabled = false, OutputEnabled = false, UsbState = "DISABLED", ProviderEnabled = true };
        var f = new Fixture(Desktop(), Physical(), disabled);
        var response = f.Run(new(RuntimeIpcCommandKind.StartOrReloadAll), () =>
        {
            f.Put(Desktop(active: true)); f.Put(Physical(active: true));
        });
        Check(response.Success && f.Events.Count == 1 && f.Events[0].Kind == RuntimeNotificationKind.Warning &&
            Has(response.Message, "Panel A", "Device A", "disabled-device", "pending") && !f.Current.Outputs[2].IsActive,
            "All must return one aggregate result, preserve a provider-enabled disabled device and reveal partial readiness.");
        f.Put(Physical(active: true, state: "ERROR")); f.Observe(); f.Observe();
        Check(f.Events.Count == 2 && f.Events[^1].Kind == RuntimeNotificationKind.Error,
            "One failing member must terminate aggregate readiness with a single failure result.");

        var none = new Fixture(disabled);
        response = none.Run(new(RuntimeIpcCommandKind.StartOrReloadAll), () => { });
        Check(response.Success && none.Events.Single().Kind == RuntimeNotificationKind.Warning &&
            response.Message.Contains("No enabled Output was started", StringComparison.Ordinal),
            "An All request with no eligible Output must disclose its no-op outcome.");

        response = f.Run(new(RuntimeIpcCommandKind.StopAll), () =>
        {
            f.Put(Desktop()); f.Put(Physical());
        });
        Check(response.Success && f.Events[^1].Kind == RuntimeNotificationKind.Information && f.Current.Outputs.All(o => !o.IsActive),
            "Stop All must verify every session is inactive.");
    }

    public static void QuietAndSupersededRequests()
    {
        var quiet = new Fixture(Desktop(active: true));
        foreach (var kind in new[] { RuntimeIpcCommandKind.Ping, RuntimeIpcCommandKind.Status, RuntimeIpcCommandKind.Invalid })
            quiet.Run(new(kind), () => { });
        quiet.Put(Desktop(active: true, state: "ERROR")); quiet.Observe();
        Check(quiet.Events.Count == 0 && quiet.Reads == 0, "Read-only/passive snapshots must not create notification ownership.");

        var f = new Fixture(Physical(), Physical("device-b"));
        f.Run(new(RuntimeIpcCommandKind.StartOrReloadTarget, "device-a"), () => f.Put(Physical(active: true)));
        f.Run(new(RuntimeIpcCommandKind.StartOrReloadTarget, "device-b"), () => f.Put(Physical("device-b", active: true)));
        f.Run(new(RuntimeIpcCommandKind.SetPhysicalDeviceEnabled, "device-a", Value: false),
            () => f.Put(Physical() with { DeviceEnabled = false, OutputEnabled = false, UsbState = "DISABLED" }));
        f.Put(Physical(active: true, state: "ERROR"));
        f.Put(Physical("device-b", active: true, connected: true)); f.Observe();
        Check(f.Events.Count == 4 && f.Events[^1].Kind == RuntimeNotificationKind.Information &&
            !f.Events[^1].Message.Contains("device-a", StringComparison.Ordinal),
            "A superseding disable must remove its pending notification without canceling an unrelated pending command.");

        var changed = new Fixture(Physical());
        changed.Run(new(RuntimeIpcCommandKind.StartOrReloadTarget, "device-a"), () => changed.Put(Physical(active: true)));
        changed.Put(Physical(active: true, connected: true) with { Dashboard = "Different" }); changed.Observe();
        Check(changed.Events.Count == 1, "A changed assignment must not satisfy an older command's deferred completion.");
        changed.Run(new(RuntimeIpcCommandKind.StartOrReloadTarget, "device-a"), () => changed.Put(Physical(active: true)));
        changed.Clear(); changed.Put(Physical(active: true, state: "ERROR")); changed.Observe();
        Check(changed.Events.Count == 2, "Shutdown/cleanup must forget pending completion state.");
    }

    public static void NotificationFailureAndShutdown()
    {
        var f = new Fixture(Desktop()) { RejectNotification = true };
        var response = f.Run(new(RuntimeIpcCommandKind.StartOrReloadTarget, "panel-a"), () => f.Put(Desktop(active: true)));
        Check(response.Success && f.Applied == 1 && f.NotificationErrors == 1,
            "A delivery failure must be logged without changing or repeating a successfully verified command.");

        var exit = new Fixture(Desktop(active: true));
        response = exit.Run(new(RuntimeIpcCommandKind.Exit), () => { });
        Check(response.Success && exit.Applied == 1 && exit.Reads == 0 &&
            Has(response.Message, "shutdown accepted", "unloading is not yet finished"),
            "Shutdown acknowledgement must describe acceptance, without asserting already-completed disposal.");
        // The application coordinator appends the existing marker after feedback assessment.
        var stopping = response with { Message = response.Message + " " + RuntimeIpcProtocol.RuntimeStoppingMessage };
        Check(RuntimeIpcProtocol.ResponseIndicatesRuntimeStopping(stopping), "Accepted shutdown retains the v1 Editor wait-for-exit handshake.");
    }

    private static RuntimeOutputStatus Desktop(bool active = false, string? state = null) =>
        new("panel-a", "Panel A", active, active, state ?? (active ? "DISPLAYING" : "STOPPED"))
        {
            Kind = OutputTargetContract.DesktopKind, Dashboard = "Board A", OutputEnabled = true,
            BoardEnabled = true, ProviderEnabled = false, Lock = false, IsVisible = active
        };

    private static RuntimeOutputStatus Physical(string id = "device-a", bool active = false,
        bool connected = false, string? state = null) =>
        new(id, "Device A", active, connected, state ?? (connected ? "CONNECTED" : active ? "WAITING" : "STOPPED"))
        {
            Kind = OutputTargetContract.TrofeoKind, Dashboard = "Board B", OutputEnabled = true,
            DeviceEnabled = true, ProviderEnabled = false
        };

    private sealed class Fixture
    {
        private readonly RuntimeCommandFeedback _feedback;
        public RuntimeStatusSnapshot Current;
        public readonly List<(string Message, RuntimeNotificationKind Kind)> Events = [];
        public int Applied, Reads, CommandErrors, NotificationErrors;
        public int FailReadAt;
        public bool RejectNotification;
        public RuntimeIpcResponse? ApplyResult;

        public Fixture(params RuntimeOutputStatus[] outputs)
        {
            Current = new("(per-output dashboards)", outputs);
            _feedback = new((message, kind) =>
            {
                if (RejectNotification) throw new InvalidOperationException("Toast unavailable.");
                Events.Add((message, kind));
            }, _ => NotificationErrors++, _ => CommandErrors++);
        }

        public RuntimeIpcResponse Run(RuntimeIpcCommand command, Action apply) =>
            _feedback.Execute(command, () => { Applied++; apply(); return ApplyResult ?? new(true, "Mock control applied."); }, () =>
            {
                Reads++;
                if (FailReadAt == Reads) throw new IOException("Status unavailable.");
                return Current;
            });

        public void Put(RuntimeOutputStatus replacement) => Current = Current with
        {
            Outputs = Current.Outputs.Select(o => o.TargetId == replacement.TargetId ? replacement : o).ToArray()
        };
        public void Observe() => _feedback.Observe(Current);
        public void Clear() => _feedback.Clear();
    }

    private static bool Has(string text, params string[] parts) => parts.All(p => text.Contains(p, StringComparison.OrdinalIgnoreCase));
    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
}
