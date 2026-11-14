using PinkieSysMon.Launcher;

namespace PinkieSysMon.RegressionTests;

internal static class Issue128RuntimeClientTests
{
    public static void OwnershipAndLifetime()
    {
        var root = Path.Combine(Path.GetTempPath(), "pinkie-issue128-root");
        const string instance = "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee";
        string Status(string candidate, bool running) =>
            "{\"ApplicationRoot\":" + System.Text.Json.JsonSerializer.Serialize(candidate) +
            ",\"RuntimeInstanceId\":\"" + instance + "\",\"OutputLifetimeRunning\":" +
            (running ? "true" : "false") + ",\"Dashboard\":\"\"}";
        var running = LauncherRuntimeClient.VerifyStatus(root, Status(root, true));
        var stopped = LauncherRuntimeClient.VerifyStatus(root, Status(root, false));
        if (!running.Present || !running.OutputLifetimeRunning || !stopped.Present ||
            stopped.OutputLifetimeRunning || running.InstanceId != stopped.InstanceId ||
            LauncherRuntimeClient.ReloadVerb(true) != "OUTPUT_START_RELOAD_ALL" ||
            LauncherRuntimeClient.ReloadVerb(false) != "RELOAD")
            throw new InvalidOperationException("Runtime reload did not distinguish live and stopped lifetimes.");

        foreach (var invalid in new[]
        {
            Status(root + "-other", true),
            "{\"ApplicationRoot\":\"" + root.Replace("\\", "\\\\") + "\"}",
            "{\"ApplicationRoot\":\"" + root.Replace("\\", "\\\\") +
                "\",\"RuntimeInstanceId\":\"" + instance + "\"}"
        })
        {
            try
            {
                LauncherRuntimeClient.VerifyStatus(root, invalid);
                throw new InvalidOperationException("Unverified Runtime identity was accepted.");
            }
            catch (InvalidDataException) { }
            catch (InvalidOperationException error) when (
                error.Message.StartsWith("A different", StringComparison.Ordinal)) { }
        }
    }
}
