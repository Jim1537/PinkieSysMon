namespace PinkieSysMon;

// Runtime UI-thread owner. Observing a snapshot performs no I/O and creates no requests.
internal sealed class RuntimeCommandFeedback(
    Action<string, RuntimeNotificationKind> notify, Action<Exception> notificationFailure,
    Action<Exception> commandFailure)
{
    private sealed record Result(RuntimeIpcResponse Response, RuntimeNotificationKind Kind, bool Pending = false);
    private sealed record PendingCommand(RuntimeIpcCommand Command, RuntimeStatusSnapshot Before,
        RuntimeOutputStatus[] Expected);
    private readonly List<PendingCommand> _pending = [];

    public RuntimeIpcResponse Execute(RuntimeIpcCommand command, Func<RuntimeIpcResponse> apply,
        Func<RuntimeStatusSnapshot> getStatus)
    {
        if (!IsUserCommand(command.Kind))
            return apply(); // PING/STATUS and invalid input have no notification side effects.

        ForgetSuperseded(command);
        Result result;
        if (command.Kind == RuntimeIpcCommandKind.Exit)
        {
            try
            {
                var response = apply();
                result = response.Success
                    ? new(new(true, "Runtime shutdown accepted; unloading is not yet finished."), RuntimeNotificationKind.Information)
                    : new(response, RuntimeNotificationKind.Error);
            }
            catch (Exception ex)
            {
                LogFailure(commandFailure, ex);
                result = new(new(false, $"Runtime shutdown failed: {ex.Message}"), RuntimeNotificationKind.Error);
            }
        }
        else
        {
            RuntimeStatusSnapshot before;
            try { before = getStatus(); }
            catch (Exception ex)
            {
                LogFailure(commandFailure, ex);
                result = new(new(false, $"{ActionName(command)} for {Identity(command, [])} was not applied: Runtime status could not be read. {ex.Message}"), RuntimeNotificationKind.Error);
                Publish(result);
                return result.Response;
            }

            RuntimeIpcResponse response;
            try { response = apply(); }
            catch (Exception ex)
            {
                LogFailure(commandFailure, ex);
                response = new(false, ex.Message);
            }

            try
            {
                var after = getStatus();
                result = Assess(command, response, before, after);
                if (result.Pending)
                    _pending.Add(new(command, new(before.Dashboard, Select(command, before)), Select(command, after)));
            }
            catch (Exception ex)
            {
                LogFailure(commandFailure, ex);
                result = new(new(false,
                    $"{ActionName(command)} for {Identity(command, Select(command, before))}: {response.Message} Resulting state could not be verified; changes may have been applied. Query Runtime status before retrying. {ex.Message}"),
                    RuntimeNotificationKind.Error);
            }
        }

        Publish(result);
        return result.Response;
    }

    public void Observe(RuntimeStatusSnapshot status)
    {
        foreach (var pending in _pending.ToArray())
        {
            // A later configuration/session owner supersedes this command's completion.
            var current = Select(pending.Command, status);
            if (current.Length != pending.Expected.Length || pending.Expected.Any(expected =>
                !current.Any(output => SameOwnership(expected, output))))
            {
                _pending.Remove(pending);
                continue;
            }

            // Waiting snapshots need no message construction or shell call.
            if (!current.Any(o => o.IsActive && IsError(o.UsbState)) && current.Any(IsPending))
                continue;
            var result = Assess(pending.Command, new(true, string.Empty), pending.Before, status);
            if (result.Pending)
                continue;
            _pending.Remove(pending);
            Publish(result); // Only one terminal result; later automatic retries are quiet.
        }
    }

    public void Clear() => _pending.Clear();

    private void ForgetSuperseded(RuntimeIpcCommand command)
    {
        if (command.TargetId is null)
            Clear();
        else
            _pending.RemoveAll(p => p.Expected.Any(output =>
                output.TargetId.Equals(command.TargetId, StringComparison.OrdinalIgnoreCase)));
    }

    private static bool SameOwnership(RuntimeOutputStatus a, RuntimeOutputStatus b) =>
        a.TargetId.Equals(b.TargetId, StringComparison.OrdinalIgnoreCase) &&
        a.Kind == b.Kind && a.Dashboard == b.Dashboard && a.OutputEnabled == b.OutputEnabled &&
        a.BoardEnabled == b.BoardEnabled && a.DeviceEnabled == b.DeviceEnabled && a.Lock == b.Lock &&
        a.IsActive == b.IsActive;

    private static RuntimeOutputStatus[] Select(RuntimeIpcCommand command, RuntimeStatusSnapshot status) =>
        command.TargetId is null ? status.Outputs : status.Outputs.Where(output =>
            output.TargetId.Equals(command.TargetId, StringComparison.OrdinalIgnoreCase)).ToArray();

    private static string Identity(RuntimeIpcCommand command, RuntimeOutputStatus[] outputs) =>
        command.TargetId is null ? "all Outputs" : outputs.FirstOrDefault() is { } selected
            ? $"Output '{selected.TargetName}' ({selected.TargetId})"
            : $"Output '{command.TargetId}'";

    private static Result Assess(RuntimeIpcCommand command, RuntimeIpcResponse response,
        RuntimeStatusSnapshot before, RuntimeStatusSnapshot after)
    {
        var outputs = Select(command, after);
        var previous = Select(command, before);
        var identity = Identity(command, outputs);
        var observed = outputs.Length == 0 ? "No matching Output is reported." :
            string.Join("; ", outputs.Select(Describe));
        if (!response.Success)
        {
            var changed = previous.Length != outputs.Length || previous.Any(p =>
                !outputs.Any(o => SameOwnership(p, o)));
            return new(new(false,
                $"{ActionName(command)} failed for {identity}: {response.Message} Observed: {observed}" +
                (changed ? " Configuration/session state changed; complete rollback is not confirmed." : string.Empty)),
                RuntimeNotificationKind.Error);
        }

        if (command.TargetId is not null && outputs.Length != 1)
            return new(new(false, $"{ActionName(command)} returned success, but {identity} cannot be uniquely verified. {observed}"), RuntimeNotificationKind.Error);

        var mismatch = outputs.FirstOrDefault(output => !MatchesRequest(command, output, previous));
        if (mismatch is not null)
            return new(new(false,
                $"{ActionName(command)} returned success, but requested state is not confirmed for {identity}. Observed: {observed} Changes may be partially applied; check Runtime status before retrying."),
                RuntimeNotificationKind.Error);

        if (outputs.Any(o => o.IsActive && IsError(o.UsbState)))
            return new(new(false,
                $"{ActionName(command)} control applied for {identity}, but an active Output reports failure. Observed: {observed} Applied settings/sessions may remain; check the Output and Runtime logs."),
                RuntimeNotificationKind.Error);

        var pending = outputs.Any(IsPending);
        var noEligible = command.Kind == RuntimeIpcCommandKind.StartOrReloadAll &&
            !outputs.Any(o => o.OutputEnabled == true);
        return new(new(true,
            $"{ActionName(command)} control applied for {identity}. Observed: {observed}" +
            (pending ? " Display readiness is pending; connection/presentation is not confirmed for every active Output." : string.Empty) +
            (noEligible ? " No enabled Output was started." : string.Empty)),
            pending || noEligible ? RuntimeNotificationKind.Warning : RuntimeNotificationKind.Information, pending);
    }

    private static bool MatchesRequest(RuntimeIpcCommand command, RuntimeOutputStatus output,
        RuntimeOutputStatus[] previous) => command.Kind switch
    {
        RuntimeIpcCommandKind.StartOrReloadTarget => output.OutputEnabled == true && output.IsActive,
        RuntimeIpcCommandKind.StartOrReloadAll => output.OutputEnabled == false && !output.IsActive ||
            output.OutputEnabled == true && output.IsActive,
        RuntimeIpcCommandKind.StopTarget or RuntimeIpcCommandKind.StopAll => !output.IsActive,
        RuntimeIpcCommandKind.AssignDashboard => !string.IsNullOrWhiteSpace(command.Dashboard) &&
            string.Equals(output.Dashboard, command.Dashboard.Trim(), StringComparison.OrdinalIgnoreCase),
        RuntimeIpcCommandKind.SetPhysicalDeviceEnabled => output.Kind == OutputTargetContract.TrofeoKind &&
            command.Value is not null && output.DeviceEnabled == command.Value && output.OutputEnabled == command.Value &&
            (command.Value == true || !output.IsActive),
        RuntimeIpcCommandKind.SetDesktopBoardEnabled => output.Kind == OutputTargetContract.DesktopKind &&
            command.Value is not null && output.BoardEnabled == command.Value &&
            output.OutputEnabled == command.Value && output.IsActive == command.Value,
        RuntimeIpcCommandKind.SetDesktopLock => output.Kind == OutputTargetContract.DesktopKind &&
            command.Value is not null && output.Lock == command.Value &&
            previous.Any(p => p.TargetId.Equals(output.TargetId, StringComparison.OrdinalIgnoreCase) && p.IsActive == output.IsActive),
        _ => true
    };

    private static bool IsError(string state) => state.Equals("ERROR", StringComparison.OrdinalIgnoreCase) ||
        state.Equals("AMBIGUOUS", StringComparison.OrdinalIgnoreCase);

    private static bool IsPending(RuntimeOutputStatus output) => output.IsActive &&
        (!output.IsConnected || (output.Kind switch
        {
            OutputTargetContract.DesktopKind => output.IsVisible != true || !output.UsbState.Equals("DISPLAYING", StringComparison.OrdinalIgnoreCase),
            OutputTargetContract.TrofeoKind => !output.UsbState.Equals("CONNECTED", StringComparison.OrdinalIgnoreCase),
            _ => true
        }));

    private static string Describe(RuntimeOutputStatus output) =>
        $"'{output.TargetName}' ({output.TargetId}), dashboard '{output.Dashboard ?? "unknown"}', " +
        (output.Kind switch
        {
            OutputTargetContract.DesktopKind => $"Board Enabled={output.BoardEnabled?.ToString() ?? "unknown"}, Lock={output.Lock?.ToString() ?? "unknown"}, ",
            OutputTargetContract.TrofeoKind => $"Device Enabled={output.DeviceEnabled?.ToString() ?? "unknown"}, ",
            _ => "Output kind unknown, "
        }) +
        $"state {output.UsbState}, " + (output.IsActive
            ? output.Kind switch
            {
                OutputTargetContract.DesktopKind => output.IsVisible == true ? "panel visible" : "panel visibility not confirmed",
                OutputTargetContract.TrofeoKind => output.IsConnected ? "transport connected; frame delivery not verified" : "transport not connected",
                _ => "presentation type not confirmed"
            }
            : "session inactive");

    private void Publish(Result result)
    {
        try { notify(result.Response.Message, result.Kind); }
        catch (Exception ex)
        {
            // Notification failure must not change or repeat an already applied command.
            LogFailure(notificationFailure, ex);
        }
    }

    private static void LogFailure(Action<Exception> log, Exception exception)
    {
        try { log(exception); } catch { }
    }

    private static bool IsUserCommand(RuntimeIpcCommandKind kind) => kind is
        RuntimeIpcCommandKind.ReloadConfiguration or RuntimeIpcCommandKind.Exit or
        RuntimeIpcCommandKind.StartOrReloadTarget or RuntimeIpcCommandKind.StopTarget or
        RuntimeIpcCommandKind.StartOrReloadAll or RuntimeIpcCommandKind.StopAll or
        RuntimeIpcCommandKind.AssignDashboard or RuntimeIpcCommandKind.SetDesktopBoardEnabled or
        RuntimeIpcCommandKind.SetDesktopLock or RuntimeIpcCommandKind.SetPhysicalDeviceEnabled;

    private static string ActionName(RuntimeIpcCommand command) => command.Kind switch
    {
        RuntimeIpcCommandKind.ReloadConfiguration => "Configuration reload",
        RuntimeIpcCommandKind.StartOrReloadTarget or RuntimeIpcCommandKind.StartOrReloadAll => "Start/reload",
        RuntimeIpcCommandKind.StopTarget or RuntimeIpcCommandKind.StopAll => "Stop/unload",
        RuntimeIpcCommandKind.AssignDashboard => "Dashboard assignment",
        RuntimeIpcCommandKind.SetDesktopBoardEnabled => command.Value == true ? "Board enable/show" : "Board disable/hide",
        RuntimeIpcCommandKind.SetDesktopLock => "Desktop Lock change",
        RuntimeIpcCommandKind.SetPhysicalDeviceEnabled => command.Value == true ? "Device enable" : "Device disable",
        _ => command.Kind.ToString()
    };
}
