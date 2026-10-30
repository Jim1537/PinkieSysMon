using System.Diagnostics;
using System.Text.Json;
using PinkieSysMon.Editor;

namespace PinkieSysMon.RegressionTests;

internal static class Issue105DiscoveryLoggingTests
{
    public static void CandidateInvalidationAndAdoption()
    {
        Sandbox(log =>
        {
            var device = Issue95GenericOutputTests.Device(OutputTargetContract.LyBulkTransport, 'd');
            var model = new DeviceSettingsModel(new AppConfig(), log);
            model.Refresh([device]);
            var card = model.Entries.Single().Target;
            Issue95GenericOutputTests.Confirm(model, card, device);
            var draft = model.CandidateDraft(card)!;
            var first = draft.LatestAttemptId;
            card.Name = "PRIVATE-NAME";
            model.MarkChanged(card);
            Check(draft.CanSaveConfiguration, "Name-only edits preserve wire approval.");
            card.Width++;
            model.MarkChanged(card);
            card.Width--;
            model.MarkChanged(card);
            Check(!draft.CanSaveConfiguration, "Editing away and back cannot restore approval.");
            var invalidated = Events(log.Path, "editor.device.candidate.approval.invalidated.succeeded");
            Check(invalidated.Length == 1 && invalidated[0].GetProperty("detail").GetString()!.Contains(first!.Value.ToString("N")),
                "One invalidation retains the approved attempt, without logging each field edit.");
            Check(Events(log.Path, "editor.device.candidate.adopt.succeeded").Length == 0,
                "Visual confirmation or preparation alone is not adoption.");
            Issue95GenericOutputTests.Confirm(model, card, device);
            var latest = draft.LatestAttemptId;
            var confirmed = model.PrepareCandidate(card);
            Check(Events(log.Path, "editor.device.candidate.adopt.succeeded").Length == 0,
                "A prepared record must not claim a completed card commit.");
            model.AcceptSavedCandidate(card, confirmed, "parent-save");
            model.CloseDiagnostics();
            var adopted = Events(log.Path, "editor.device.candidate.adopt.succeeded").Single();
            Check(adopted.GetProperty("parentOperationId").GetString() == "parent-save" &&
                adopted.GetProperty("detail").GetString()!.Contains(latest!.Value.ToString("N")) &&
                model.Entries.Single().IsConfigured && !card.DeviceEnabled && !card.ProviderEnabled,
                "Adoption correlates the latest Test and parent Save while keeping output disabled.");
            Check(!Text(log.Path).Contains("PRIVATE-NAME") && !Text(log.Path).Contains(device.DeviceId) &&
                !Text(log.Path).Contains(device.PnpNodeKey!), "Routine events omit private names and physical identity.");
            Check(!File.Exists(log.ErrorsPath), "Healthy invalidation/adoption is not an application error.");
        });
    }

    public static void DiscoveryFailureRecoveryAndPrivacy()
    {
        Sandbox(log =>
        {
            var device = Issue95GenericOutputTests.Device(OutputTargetContract.LyBulkTransport, 'e');
            var fail = true;
            var original = new InvalidOperationException("Injected discovery fault");
            using (var dialog = new PhysicalDeviceSettingsDialog(new AppConfig(), [],
                () => fail ? throw original : [device], log: log))
            {
                dialog.RefreshDeviceDiscovery();
                fail = false;
                dialog.RefreshDeviceDiscovery("test.recovery");
                dialog.RefreshDeviceDiscovery("test.recovery");
            }
            Check(Text(log.ErrorsPath).Contains("Injected discovery fault") &&
                Text(log.ErrorsPath).Contains("suppressed=1"), "Original failure and repeated total survive recovery.");
            var states = Events(log.Path, "editor.discovery.state");
            Check(states.Any(e => e.GetProperty("state").GetString()!.Contains("available=False")) &&
                states.Any(e => e.GetProperty("state").GetString()!.Contains("available=True")),
                "Unavailable and recovered inventory are distinct observations.");
            Check(!Text(log.Path).Contains("Injected discovery fault") &&
                !Text(log.Path).Contains(device.DeviceId) && !Text(log.Path).Contains(device.PnpNodeKey!),
                "Session keeps only an error reference and logical inventory context.");
        });
    }

    public static void CountedStatesBoundsAndClose()
    {
        Sandbox(log =>
        {
            long now = Stopwatch.GetTimestamp();
            using var observer = new DiagnosticStateObserver(log, "test.discovery", () => now);
            observer.Observe("source", "unavailable");
            for (var i = 0; i < 100; i++) observer.Observe("source", "unavailable");
            Check(Events(log.Path, "test.discovery.state").Length == 1, "Quiet observations are counted without per-poll records.");
            now += 31 * Stopwatch.Frequency;
            observer.Observe("source", "unavailable");
            observer.Observe("source", "available");
            for (var i = 0; i < 100; i++) observer.Observe("key" + i, "present");
            observer.Dispose();
            var before = Text(log.Path);
            observer.Observe("late", "ignored");
            Check(Text(log.Path) == before, "Closed observer rejects late diagnostics.");
            var events = Events(log.Path, "test.discovery.state");
            Check(events.Any(e => e.GetProperty("phase").GetString() == "summary" && e.GetProperty("observations").GetInt64() == 102) &&
                events.Any(e => e.GetProperty("phase").GetString() == "final" && e.GetProperty("state").GetString() == "available"),
                "Periodic and final records retain counts and recovery.");
            Check(Events(log.Path, "test.discovery.omitted").Single().GetProperty("observations").GetInt64() == 37,
                "At most 64 state keys are retained and omitted observations are explicit.");
        });
    }

    private static JsonElement[] Events(string path, string name) => Text(path)
        .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
        .Select(line => { using var j = JsonDocument.Parse(line[line.IndexOf('{')..]); return j.RootElement.GetProperty("message").GetString(); })
        .Where(message => message?.StartsWith('{') == true)
        .Select(message => { using var j = JsonDocument.Parse(message!); return j.RootElement.Clone(); })
        .Where(e => e.GetProperty("eventName").GetString() == name).ToArray();

    private static string Text(string path) => LogFileWriter.UnderGate(path, "write",
        () => File.Exists(path) ? File.ReadAllText(path) : "");

    private static void Sandbox(Action<FileLogger> action)
    {
        var root = Path.Combine(Path.GetTempPath(), "pinkie-discovery-evidence-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var log = new FileLogger(Path.Combine(root, "editor.log"), "Editor");
        try
        {
            action(log);
            foreach (var path in new[] { log.Path, log.ErrorsPath })
                Check(!Text(path).Contains("\"loggingFailures\":1"), "Fixture must not lose log writes.");
        }
        finally { Directory.Delete(root, true); }
    }

    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
}
