namespace PinkieSysMon.Editor;

// Save one Settings draft, then apply it through Runtime's existing control path.
// A failed apply restores the exact previous local file, unless another writer intervened.
internal static class EditorSettingsPersistence
{
    public static async Task<bool> ApplyAsync(string path, AppConfig config, byte[]? expectedFile,
        Func<Task<RuntimeIpcResponse?>> reloadIfRunning)
    {
        if (!Matches(path, expectedFile))
            throw new InvalidOperationException(
                "Application configuration changed while Settings was open. Reopen Settings and retry.");
        config.Save(path);
        var written = File.ReadAllBytes(path);
        try
        {
            var response = await reloadIfRunning();
            if (response is null)
                return false; // Saved for the next start; never start an absent Runtime.
            if (!response.Success)
                throw new InvalidOperationException(response.Message);
            return true;
        }
        catch (Exception failure)
        {
            if (!Matches(path, written))
                throw new InvalidOperationException(
                    "Runtime apply failed and configuration changed again. The newer file was preserved; inspect Runtime state before retrying.", failure);
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
            RuntimeIpcResponse? recovered;
            try { recovered = await reloadIfRunning(); }
            catch (Exception recovery)
            {
                throw new InvalidOperationException(
                    $"Settings apply failed. Previous configuration restored, but Runtime recovery failed: {recovery.Message}", failure);
            }
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
