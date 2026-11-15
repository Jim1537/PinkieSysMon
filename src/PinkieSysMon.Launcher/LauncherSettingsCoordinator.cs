using System.Diagnostics;
using PinkieSysMon.Editor;

namespace PinkieSysMon.Launcher;

internal sealed record LauncherSettingsApplyResult(bool Success, bool NoChanges,
    bool Partial, string Message, int EditorsRestored);

// The entire commit path is synchronous on one worker thread. This is deliberate:
// the Windows mutex is thread-owned; awaits may move to a different thread.
// Callers can Task.Run the operation while their Settings window stays responsive.
internal static class LauncherSettingsCoordinator
{
    internal static LauncherSettingsApplyResult Apply(LauncherSettingsDraft draft)
    {
        draft.Validate();
        if (!draft.HasChanges)
            return new(true, true, false, "No Settings changes. No Editors were closed and no Runtime command was sent.", 0);

        var closed = new List<EditorSessionSnapshot>();
        var started = new List<(EditorSessionSnapshot Original, Process Process)>();
        var errors = new List<string>();
        var appWritten = false;
        var editorWritten = false;
        var startupWritten = false;
        var runtimeCommandStarted = false;
        byte[]? freshEditorBytes = null;
        byte[]? savedEditorBytes = null;
        byte[]? preparedAppBytes = null;
        LauncherRuntimeState runtime = LauncherRuntimeState.Absent;
        IDisposable? gate = null;
        var success = false;
        var recoveryIncomplete = false;
        var runtimeReloaded = false;

        try
        {
            // No process mutation before the root's startup/Settings gate.
            draft.EnsureInputUnchanged();
            gate = EditorSessionWire.AcquireStartupGate(draft.Root);
            var knownEditors = EditorSessionWire.DiscoverAsync(draft.Root).GetAwaiter().GetResult();
            runtime = LauncherRuntimeClient.GetStatusAsync(draft.Root).GetAwaiter().GetResult();

            var editorExe = Path.Combine(draft.Root, "bin", "PinkieSysMon.Editor.exe");
            if (knownEditors.Count > 0 && !File.Exists(editorExe))
                throw new FileNotFoundException(
                    "Editor executable is missing; existing Editors cannot safely be closed.", editorExe);

            // Each participant proves root/PID/start-time and current document.
            // A user's Cancel, failed save or unconfirmed shutdown aborts all
            // remaining closures and the entire Settings commit.
            foreach (var session in knownEditors.OrderBy(editor => editor.ProcessId))
            {
                EditorSessionReply reply;
                try { reply = EditorSessionWire.RequestCloseAsync(session).GetAwaiter().GetResult(); }
                catch (Exception closeError)
                {
                    // A lost acknowledgement may follow a successful FormClosed.
                    // Reconcile the exact old process before deciding restoration.
                    if (OriginalProcessExited(session, 5000)) closed.Add(session);
                    throw new InvalidOperationException(
                        "Editor close outcome was not confirmed. No Settings were committed.", closeError);
                }
                if (!reply.Accepted)
                    throw new InvalidOperationException("Editor refused coordinated close: " + reply.Message);
                closed.Add(session); // The FormClosed ACK is definitive even if the process has already exited.
                if (!OriginalProcessExited(session, 15000))
                    throw new InvalidOperationException(
                        "Editor window closed, but exact process termination could not be confirmed.");
            }

            // Editor may have just saved view/scroll/expansion preferences.
            // Compare semantic app-wide fields, not the entire editor.json blob.
            draft.EnsureSharedPreferencesUnchanged();
            var newlyAdmitted = EditorSessionWire.DiscoverAsync(draft.Root).GetAwaiter().GetResult();
            if (newlyAdmitted.Count != 0)
                throw new InvalidOperationException(
                    "A new Editor became active during the Settings transaction. Configuration was not committed.");
            var freshRuntime = LauncherRuntimeClient.GetStatusAsync(draft.Root).GetAwaiter().GetResult();
            if (freshRuntime.Present != runtime.Present ||
                freshRuntime.InstanceId != runtime.InstanceId)
                throw new InvalidOperationException("Runtime ownership changed while closing Editors.");

            preparedAppBytes = draft.PrepareAppBytes();
            if (draft.AppChanged)
            {
                draft.WriteAppIfUnchanged(preparedAppBytes);
                appWritten = true;
            }
            if (draft.EditorChanged)
            {
                freshEditorBytes = File.Exists(draft.EditorPath)
                    ? File.ReadAllBytes(draft.EditorPath) : null;
                EditorSettings.SaveSharedPreferences(draft.EditorPath, freshEditorBytes,
                    draft.Values.ToolbarIconSize, draft.Values.Theme);
                savedEditorBytes = File.ReadAllBytes(draft.EditorPath);
                editorWritten = true;
            }
            if (draft.StartupChanged)
            {
                StartupTaskManager.SetEnabled(draft.Values.StartRuntimeWithWindows,
                    Path.Combine(draft.Root, "bin", "PinkieSysMon.exe"));
                startupWritten = true;
            }

            if (runtime.Present && draft.AppChanged)
            {
                runtimeCommandStarted = true;
                LauncherRuntimeClient.ReloadAsync(draft.Root, runtime).GetAwaiter().GetResult();
                runtimeReloaded = true;
            }
            success = true;
        }
        catch (Exception error)
        {
            errors.Add(error.Message);
            // An ERR reply is a known rejection. Recover the files and ask
            // Runtime once to reapply its previous configuration; never retry
            // an UNKNOWN-after-send request (or a changed process identity).
            var knownRejection = error is LauncherRuntimeRejectedException;
            var knownNotSent = error is LauncherRuntimePreDispatchException;
            if (!runtimeCommandStarted || knownRejection || knownNotSent)
            {
                if (editorWritten && savedEditorBytes is not null)
                {
                    try
                    {
                        if (!LauncherSettingsDraft.RestoreIfUnchanged(
                            draft.EditorPath, savedEditorBytes, freshEditorBytes))
                        {
                            recoveryIncomplete = true;
                            errors.Add("Editor preferences rollback blocked by newer bytes.");
                        }
                    }
                    catch (Exception rollback) { recoveryIncomplete = true; errors.Add("Editor preferences rollback failed: " + rollback.Message); }
                }
                if (appWritten && preparedAppBytes is not null)
                {
                    try
                    {
                        if (!LauncherSettingsDraft.RestoreIfUnchanged(
                            draft.AppPath, preparedAppBytes, draft.OriginalAppBytes))
                        {
                            recoveryIncomplete = true;
                            errors.Add("Application configuration rollback blocked by newer bytes.");
                        }
                    }
                    catch (Exception rollback) { recoveryIncomplete = true; errors.Add("Application configuration rollback failed: " + rollback.Message); }
                }
                if (startupWritten)
                {
                    try
                    {
                        StartupTaskManager.SetEnabled(!draft.Values.StartRuntimeWithWindows,
                            Path.Combine(draft.Root, "bin", "PinkieSysMon.exe"));
                    }
                    catch (Exception rollback) { recoveryIncomplete = true; errors.Add("Autostart rollback failed: " + rollback.Message); }
                }
                if (knownRejection && runtime.Present)
                {
                    if (!recoveryIncomplete)
                    {
                        try
                        {
                            LauncherRuntimeClient.ReloadAsync(draft.Root, runtime).GetAwaiter().GetResult();
                        }
                        catch (Exception rollback)
                        {
                            recoveryIncomplete = true;
                            errors.Add("Runtime recovery unconfirmed: " + rollback.Message);
                        }
                    }
                    else errors.Add("Runtime recovery skipped because previous file state was not restored.");
                }
            }
            else
            {
                recoveryIncomplete = true;
                errors.Add("Runtime apply outcome is uncertain after a command attempt. " +
                    "Persisted changes were retained; no blind rollback or retry occurred.");
            }
        }
        finally
        {
            // Start exactly the Editors verified closed, preserving each live
            // document selection. They will wait for the startup gate, then
            // register their new session endpoints before becoming editable.
            foreach (var session in closed)
            {
                try
                {
                    if (!OriginalProcessExited(session, 0))
                        throw new InvalidOperationException(
                            "Original Editor process has not exited; no duplicate was launched.");
                    var command = new ProcessStartInfo
                    {
                        FileName = Path.Combine(draft.Root, "bin", "PinkieSysMon.Editor.exe"),
                        WorkingDirectory = Path.Combine(draft.Root, "bin"),
                        UseShellExecute = false
                    };
                    foreach (var arg in EditorStartupContract.GetRestartArguments(
                        draft.Root, session.ActiveDashboard))
                        command.ArgumentList.Add(arg);
                    var process = Process.Start(command)
                        ?? throw new InvalidOperationException("Windows did not return an Editor process.");
                    started.Add((session, process));
                }
                catch (Exception restart) { recoveryIncomplete = true; errors.Add("Editor restart failed: " + restart.Message); }
            }
            gate?.Dispose(); // Same worker thread that acquired the mutex.
        }

        var restored = 0;
        foreach (var (original, process) in started)
        {
            using (process)
            {
                try
                {
                    var until = DateTime.UtcNow.AddSeconds(20);
                    EditorSessionSnapshot? verified = null;
                    while (DateTime.UtcNow < until && !process.HasExited)
                    {
                        try
                        {
                            verified = EditorSessionWire.GetStatusAsync(process).GetAwaiter().GetResult();
                            break;
                        }
                        catch (Exception error) when (error is IOException or TimeoutException or
                                                     InvalidDataException or UnauthorizedAccessException)
                        { Thread.Sleep(250); }
                    }
                    if (verified is null ||
                        !string.Equals(EditorSessionWire.NormalizeRoot(verified.Root), draft.Root,
                            StringComparison.OrdinalIgnoreCase) ||
                        !string.Equals(verified.ActiveDashboard, original.ActiveDashboard,
                            StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException("Reopened Editor session/document readiness is unverified.");
                    restored++;
                }
                catch (Exception verification)
                { recoveryIncomplete = true; errors.Add("Editor restore unconfirmed: " + verification.Message); }
            }
        }
        if (closed.Count != restored)
        {
            recoveryIncomplete = true;
            errors.Add("Only " + restored + "/" + closed.Count + " closed Editors were confirmed restored.");
        }

        if (errors.Count > 0)
            return new(false, false, recoveryIncomplete,
                string.Join(Environment.NewLine, errors.Distinct()), restored);

        return new(success, false, false,
            runtimeReloaded ? "Settings saved and Runtime reload confirmed." :
                runtime.Present ? "Settings saved; Runtime reload was not necessary." :
                "Settings saved; absent Runtime will load them on its next start.",
            restored);
    }

    private static bool OriginalProcessExited(EditorSessionSnapshot session, int waitMs)
    {
        try
        {
            using var process = Process.GetProcessById(session.ProcessId);
            if (process.StartTime.ToUniversalTime().Ticks != session.StartedUtcTicks)
                return true; // PID was reused, so the original process has exited.
            return process.WaitForExit(waitMs);
        }
        catch (ArgumentException) { return true; } // The original PID no longer exists.
        catch (System.ComponentModel.Win32Exception) { return false; }
        catch (InvalidOperationException) { return false; }
    }
}
