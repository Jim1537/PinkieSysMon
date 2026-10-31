using PinkieSysMon.Editor;

namespace PinkieSysMon.RegressionTests;

internal static class Issue95ConfiguredRuntimePromptTests
{
    public static void SaveStartDecisions()
    {
        var promptCount = 0;
        var launchCount = 0;
        var present = false;
        var running = false;
        var consent = false;
        var launchSucceeds = true;
        var status = EditorOutputStartWorkflow.FromConfiguration(new AppConfig());
        Task<RuntimeIpcStatusResponse> Probe() => Task.FromResult(running
            ? new RuntimeIpcStatusResponse(true, "Running", status)
            : new RuntimeIpcStatusResponse(false, "Not running", null));
        Task<RuntimeIpcStatusResponse> Launch()
        {
            launchCount++;
            return Task.FromResult(launchSucceeds
                ? new RuntimeIpcStatusResponse(true, "Started", status)
                : new RuntimeIpcStatusResponse(false, "Launch failed", null));
        }
        bool Ask() { promptCount++; return consent; }
        ConfiguredDeviceRuntimeResult Save(bool applied) => ConfiguredDeviceRuntimeStart.AfterSaveAsync(
            applied, Probe, () => present, Ask, Launch).GetAwaiter().GetResult();

        Check(Save(true).State == ConfiguredDeviceRuntimeState.Applied && promptCount == 0,
            "An applied configured Save must not offer another Runtime launch.");
        running = true;
        Check(Save(false).State == ConfiguredDeviceRuntimeState.AlreadyRunning && promptCount == 0,
            "A Runtime that started during Save must not trigger a duplicate launch.");
        running = false; present = true;
        Check(Save(false).State == ConfiguredDeviceRuntimeState.Unresponsive && promptCount == 0,
            "An unresponsive process must be reported without offering a duplicate launch.");
        present = false;
        Check(Save(false).State == ConfiguredDeviceRuntimeState.Declined && promptCount == 1 && launchCount == 0,
            "Declining the prompt leaves the saved card unchanged and does not start Runtime.");
        consent = true;
        Check(Save(false).State == ConfiguredDeviceRuntimeState.Started && promptCount == 2 && launchCount == 1,
            "Consent launches Runtime once and confirms its status.");
        launchSucceeds = false;
        var failure = Save(false);
        Check(failure.State == ConfiguredDeviceRuntimeState.StartFailed && failure.Message == "Launch failed" &&
              launchCount == 2, "A launch failure must remain explicit after successful persistence.");
    }

    private static void Check(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }
}
