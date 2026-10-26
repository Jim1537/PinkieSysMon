namespace PinkieSysMon.Editor;

// Save one Settings draft, then apply it through Runtime's existing control path.
// A failed apply restores the exact previous local file, unless another writer intervened.
internal static class EditorSettingsPersistence
{
    internal static void ApplyRemovalWithoutRuntime(string path, AppConfig config, byte[]? expectedFile, LogOperation? operation = null)
    {
        operation?.Phase("file.conflictCheck");
        if (!Matches(path, expectedFile))
            throw new InvalidOperationException("Configuration changed while removing the selected Output. Reopen Settings.");
        operation?.Phase("file.write");
        config.Save(path);
        operation?.Phase("file.committed");
    }

    internal static async Task<(bool RuntimeReloaded, string? Warning)> ApplyRemovalAsync(
        string path, AppConfig config, byte[]? expectedFile, Func<Task<RuntimeIpcResponse>>? reload, LogOperation? operation = null)
    {
        operation?.Phase("file.conflictCheck");
        if (!Matches(path, expectedFile))
            throw new InvalidOperationException("Configuration changed after Runtime stop. The selected session remains stopped; reopen Settings.");
        operation?.Phase("file.write");
        config.Save(path);
        operation?.Phase("file.committed");
        if (reload is null) { operation?.Phase("runtime.absent"); return (false, null); }
        try
        {
            operation?.Phase("runtime.reload");
            var response = await reload();
            operation?.Phase("runtime.result", $"success={response.Success}; responseReceived={response.ResponseReceived}");
            if (response.Success && response.ResponseReceived) return (true, null);
            operation?.RecordFailure(null, "runtime.reload", "recordRemoved=true; rollback=not-permitted; result=unconfirmed; message=" + response.Message);
            return (false, "Selected record was removed, but Runtime reload was not confirmed. Other Outputs may retain their previous settings; inspect Runtime before retrying. " + response.Message);
        }
        catch (Exception error)
        {
            operation?.RecordFailure(error, "runtime.reload", "recordRemoved=true; rollback=not-permitted");
            return (false, "Selected record was removed, but Runtime reload was not confirmed. Inspect Runtime before retrying. " + error.Message);
        }
    }

    public static async Task<bool> ApplyAsync(string path, AppConfig config, byte[]? expectedFile,
        Func<Task<RuntimeIpcResponse?>> reloadIfRunning, LogOperation? operation = null)
    {
        operation?.Phase("file.conflictCheck");
        if (!Matches(path, expectedFile))
            throw new InvalidOperationException(
                "Application configuration changed while Settings was open. Reopen Settings and retry.");
        operation?.Phase("file.write");
        config.Save(path);
        operation?.Phase("file.committed");
        var written = File.ReadAllBytes(path);
        try
        {
            operation?.Phase("runtime.apply");
            var response = await reloadIfRunning();
            operation?.Phase(response is null ? "runtime.absent" : "runtime.result",
                response is null ? "savedForNextStart=true" : $"success={response.Success}; responseReceived={response.ResponseReceived}");
            if (response is null)
                return false; // Saved for the next start; never start an absent Runtime.
            if (!response.Success)
                throw new InvalidOperationException(response.Message);
            return true;
        }
        catch (Exception failure)
        {
            operation?.RecordFailure(failure, "runtime.apply", "fileCommitted=true; recoveryRequired=true");
            operation?.Phase("rollback.file.conflictCheck");
            if (!Matches(path, written))
            {
                operation?.Phase("rollback.file.conflict", "newerFilePreserved=true; runtimeState=unconfirmed");
                throw new InvalidOperationException(
                    "Runtime apply failed and configuration changed again. The newer file was preserved; inspect Runtime state before retrying.", failure);
            }
            operation?.Phase("rollback.file.restore");
            if (expectedFile is null)
                File.Delete(path);
            else
            {
                var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
                try
                {
                    File.WriteAllBytes(temporary, expectedFile);
                    File.Move(temporary, path, true);
                }
                finally { if (File.Exists(temporary)) File.Delete(temporary); }
            }
            operation?.Phase("rollback.file.restored");
            RuntimeIpcResponse? recovered;
            operation?.Phase("rollback.runtime.apply");
            try { recovered = await reloadIfRunning(); }
            catch (Exception recovery)
            {
                operation?.RecordFailure(recovery, "rollback.runtime.apply", "previousFileRestored=true; runtimeRecovery=failed");
                throw new InvalidOperationException(
                    $"Settings apply failed. Previous configuration restored, but Runtime recovery failed: {recovery.Message}", failure);
            }
            operation?.Phase("rollback.runtime.result", recovered is null ? "response=absent; recovery=unconfirmed"
                : $"success={recovered.Success}; responseReceived={recovered.ResponseReceived}");
            var recoveryStatus = recovered is { Success: true }
                ? "Previous Runtime configuration reapplied."
                : "Runtime recovery was not confirmed; check Runtime before retrying.";
            throw new InvalidOperationException(
                $"Settings apply failed: {failure.Message}\nPrevious configuration restored. {recoveryStatus}", failure);
        }
    }

    private static bool Matches(string path, byte[]? expected) => expected is null
        ? !File.Exists(path)
        : File.Exists(path) && File.ReadAllBytes(path).AsSpan().SequenceEqual(expected);
}
