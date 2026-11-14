namespace PinkieSysMon.Editor;

internal sealed partial class EditorForm
{
    private TaskCompletionSource<EditorSessionCloseResult>? _coordinatedCloseCompletion;
    private bool _coordinatedSettingsSaved;

    // Called only on this form's UI thread. Returns after the actual FormClosed
    // event, never merely after an OS close message or asynchronous Close() entry.
    internal Task<EditorSessionCloseResult> RequestCoordinatedClose(string? expectedDashboard)
    {
        var actual = string.IsNullOrWhiteSpace(_dashboardName) ? null : _dashboardName;
        if (!string.Equals(actual, expectedDashboard, StringComparison.OrdinalIgnoreCase))
            return Task.FromResult(new EditorSessionCloseResult(false,
                "The active dashboard changed after Settings enumerated this Editor."));
        if (_previewShutdownStarted || _coordinatedCloseCompletion is not null)
            return Task.FromResult(new EditorSessionCloseResult(false,
                "Editor shutdown is already in progress."));
        if (IsDisposed || Disposing)
            return Task.FromResult(new EditorSessionCloseResult(false,
                "The Editor has already been disposed; close outcome is unverified."));

        _coordinatedSettingsSaved = false;
        _restartAfterClose = false; // The Settings host alone owns any reopen.
        var pending = new TaskCompletionSource<EditorSessionCloseResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        _coordinatedCloseCompletion = pending;
        Close();
        return pending.Task;
    }

    private void RejectCoordinatedClose(string message)
    {
        var pending = _coordinatedCloseCompletion;
        _coordinatedCloseCompletion = null;
        _coordinatedSettingsSaved = false;
        pending?.TrySetResult(new EditorSessionCloseResult(false, message));
    }

    internal EditorSessionSnapshot GetSessionSnapshot(long startedUtcTicks, string root)
    {
        return new EditorSessionSnapshot(EditorSessionWire.NormalizeRoot(root), Environment.ProcessId,
            startedUtcTicks, string.IsNullOrWhiteSpace(_dashboardName) ? null : _dashboardName);
    }
}
