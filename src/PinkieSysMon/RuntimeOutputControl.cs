namespace PinkieSysMon;

internal sealed class RuntimeOutputControl
{
    private readonly OutputSessionManager _sessions;
    private readonly Func<string> _dashboardName;

    public RuntimeOutputControl(OutputSessionManager sessions, Func<string> dashboardName)
    {
        _sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
        _dashboardName = dashboardName ?? throw new ArgumentNullException(nameof(dashboardName));
    }

    public RuntimeStatusSnapshot GetStatus()
    {
        var outputs = _sessions.GetStatuses()
            .Select(status => new RuntimeOutputStatus(
                status.TargetId,
                status.TargetName,
                status.IsActive,
                status.IsConnected,
                status.UsbState))
            .ToArray();

        return new RuntimeStatusSnapshot(_dashboardName(), outputs);
    }

    public RuntimeIpcResponse Execute(RuntimeIpcCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        switch (command.Kind)
        {
            case RuntimeIpcCommandKind.StartOrReloadTarget:
                _sessions.StartOrReloadTarget(command.TargetId!);
                return new RuntimeIpcResponse(true, $"Output target '{command.TargetId}' started/reloaded.");

            case RuntimeIpcCommandKind.StopTarget:
            {
                var stopped = _sessions.StopTarget(command.TargetId!);
                return new RuntimeIpcResponse(
                    true,
                    stopped
                        ? $"Output target '{command.TargetId}' stopped and unloaded."
                        : $"Output target '{command.TargetId}' is already stopped.");
            }

            case RuntimeIpcCommandKind.StartOrReloadAll:
                _sessions.StartOrReloadAll();
                return new RuntimeIpcResponse(true, "All configured output targets started/reloaded.");

            case RuntimeIpcCommandKind.StopAll:
                _sessions.StopAll();
                return new RuntimeIpcResponse(true, "All output targets stopped and unloaded.");

            default:
                return new RuntimeIpcResponse(false, $"IPC command '{command.Kind}' is not an output-session command.");
        }
    }

    public bool ShouldExitRuntimeAfter(RuntimeIpcCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        return command.Kind switch
        {
            RuntimeIpcCommandKind.StopAll => true,
            RuntimeIpcCommandKind.StopTarget => !_sessions.GetStatuses().Any(status => status.IsActive),
            _ => false
        };
    }
}
