using System.Diagnostics;

namespace PinkieSysMon;

// Same Runtime executable and shared worker; no saved Output startup or reload.
internal sealed class UsbTestApplicationContext : ApplicationContext
{
    private readonly Control _invoker = new();
    private readonly RuntimeUsbTestService _tests;
    private readonly RuntimeIpcServer _ipc;
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 500 };
    private readonly WindowsAppNotificationService _notifications;
    private readonly FileLogger _log;
    private readonly long _started = Stopwatch.GetTimestamp();
    private bool _terminalRead;
    private bool _closing;
    private bool _exiting;

    internal UsbTestApplicationContext(string root, FileLogger log)
    {
        _log = log;
        _notifications = new WindowsAppNotificationService(log);
        _tests = new RuntimeUsbTestService(CatalogUsbInterfaceDiscovery.CaptureSnapshot,
            (_, _) => false, intent => RuntimeUsbTestService.CreatePump(root, log, intent), log,
            () => AppConfig.Load(Path.Combine(root, "config", "app.json"), log)
                .Outputs.IgnoredDeviceNodeKeys.ToHashSet(StringComparer.OrdinalIgnoreCase));
        _tests.TestFailed += () => _ = _invoker.InvokeAsync(() => _notifications.Show(
            "USB Test not passed. Configuration was not saved. Check the Runtime log and retry after cleanup.", RuntimeNotificationKind.Error));
        _invoker.CreateControl();
        _ipc = new RuntimeIpcServer((line, token) => _invoker.InvokeAsync(() => Handle(line), token), log);
        _timer.Tick += (_, _) =>
        {
            _tests.Tick();
            if (!_tests.HasActive && (_closing || Volatile.Read(ref _terminalRead) ||
                (_tests.CompletedAt != 0 ? Stopwatch.GetElapsedTime(_tests.CompletedAt).TotalSeconds >= 10
                    : Stopwatch.GetElapsedTime(_started).TotalSeconds >= 30)))
            {
                _log.Info($"USB Test-only Runtime exit requested: closing={_closing}; terminalRead={_terminalRead}; hasCompletedAttempt={_tests.CompletedAt != 0}.");
                ExitThread();
            }
        };
        Microsoft.Win32.SystemEvents.PowerModeChanged += OnPowerModeChanged;
        _ipc.Start();
        _timer.Start();
    }

    private RuntimeIpcResponse Handle(string line)
    {
        var command = RuntimeIpcProtocol.ParseCommand(line);
        if (command.Kind == RuntimeIpcCommandKind.Ping) return new(true, "USB Test-only Runtime");
        if (command.Kind == RuntimeIpcCommandKind.Status) return new(true,
            RuntimeIpcProtocol.SerializeStatus(new RuntimeStatusSnapshot("USB Test only", [])));
        if (command.Kind is RuntimeIpcCommandKind.UsbTestBegin or RuntimeIpcCommandKind.UsbTestStatus or RuntimeIpcCommandKind.UsbTestCancel)
        {
            if (_closing || Volatile.Read(ref _terminalRead) || command.Kind == RuntimeIpcCommandKind.UsbTestBegin && _tests.CompletedAt != 0)
                return new(false, "Test-only Runtime is finishing. Retry after it exits.");
            var response = _tests.Execute(command);
            // Terminal response is written before the next timer tick normally;
            // a lost response remains unknown to the Editor, never a success.
            if (command.Kind == RuntimeIpcCommandKind.UsbTestStatus && response.Success &&
                System.Text.Json.JsonSerializer.Deserialize<CatalogUsbTestReply>(response.Message)?.Status.TechnicalOutcome is not null)
                Volatile.Write(ref _terminalRead, true);
            return response;
        }
        return new(false, "Test-only Runtime cannot start, reload or change saved Outputs.");
    }

    private void OnPowerModeChanged(object sender, Microsoft.Win32.PowerModeChangedEventArgs e)
    {
        if (e.Mode == Microsoft.Win32.PowerModes.Suspend) _tests.CancelActive();
    }

    protected override void ExitThreadCore()
    {
        if (_exiting) return;
        if (_tests.HasActive)
        {
            if (!_closing) _log.Info("USB Test-only Runtime shutdown awaits native Test release; cancellation requested, release not confirmed.");
            _closing = true; _tests.CancelActive(); return;
        }
        _exiting = true;
        using var cleanup = new CleanupOperation(_log, "runtime.usb-test.shutdown");
        cleanup.Step("admission.stop", () =>
        {
            Microsoft.Win32.SystemEvents.PowerModeChanged -= OnPowerModeChanged;
            _timer.Stop();
        });
        cleanup.Step("ipc.dispose", _ipc.Dispose);
        cleanup.Step("notifications.dispose", _notifications.Dispose);
        cleanup.Step("timer.dispose", _timer.Dispose);
        cleanup.Step("ui-invoker.dispose", _invoker.Dispose);
        cleanup.Step("message-loop.exit", () => base.ExitThreadCore());
        cleanup.Complete();
    }
}
