using PinkieSysMon.Editor;

namespace PinkieSysMon.RegressionTests;

internal static class Issue128StartupOwnershipTests
{
    public static void AccountWideRunEntryOwnership()
    {
        var root = Path.Combine(Path.GetTempPath(), "PinkieSysMon issue128 install");
        var runtime = Path.Combine(root, "bin", "PinkieSysMon.exe");
        var other = Path.Combine(Path.GetTempPath(), "other-install", "bin", "PinkieSysMon.exe");
        if (!StartupTaskManager.IsOwnedCommand($"\"{runtime}\"", runtime) ||
            !StartupTaskManager.IsOwnedCommand(runtime, runtime) ||
            StartupTaskManager.IsOwnedCommand($"\"{other}\"", runtime) ||
            StartupTaskManager.IsOwnedCommand($"\"{runtime}\" --extra", runtime) ||
            StartupTaskManager.IsOwnedCommand("\"broken", runtime) ||
            StartupTaskManager.IsOwnedCommand("", runtime))
            throw new InvalidOperationException("HKCU Run entry ownership must be exact and reject foreign commands.");
    }
}
