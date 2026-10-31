using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows.Forms;
using PinkieSysMon;
using PinkieSysMon.Editor;

namespace PinkieSysMon.RegressionTests;

internal static class Issue104LoggingTests
{
    public static void RetiredOptionsIgnoredAndOmitted() => Sandbox(root =>
    {
        foreach (var protocol in new[] { "ly-bulk", "encrypted-jpeg" })
        {
            var device = Issue71AdoptionTests.Device(protocol, 'a');
            var target = Issue71AdoptionTests.Target(device);
            target.DeviceEnabled = true;
            var config = new AppConfig { Outputs = new() { Targets = [target] } };
            var path = Path.Combine(root, "app.json");
            config.Save(path);
            var json = JsonNode.Parse(File.ReadAllText(path))!;
            json["Usb"]!["DiagnosticJournalEnabled"] = true;
            json["Outputs"]!["Targets"]![0]!["Usb"]!["DiagnosticJournalEnabled"] = true;
            File.WriteAllText(path, json.ToJsonString());
            var loaded = AppConfig.Load(path, new FileLogger(Path.Combine(root, "read.log")));
            var model = new DeviceSettingsModel(loaded);
            model.Refresh([device]);
            Check(model.CanEnable(model.Entries.Single().Target),
                "Retired global and per-target values cannot block enabled exact bindings.");
            loaded.Save(path);
            Check(!File.ReadAllText(path).Contains("DiagnosticJournalEnabled", StringComparison.Ordinal),
                "Saving drops retired logging preferences at both configuration levels.");
            Check(loaded.Outputs.Targets.Single().DeviceEnabled &&
                  loaded.Outputs.Targets.Single().Usb!.TransferTimeoutMs == target.Usb!.TransferTimeoutMs,
                "Dropping retired options preserves consent and effective USB timing.");
        }
    });

    public static void RowsRemovedAndProviderRetained()
    {
        foreach (var protocol in new[] { "ly-bulk", "encrypted-jpeg" })
        {
            var device = Issue71AdoptionTests.Device(protocol, 'a');
            var target = Issue71AdoptionTests.Target(device);
            var config = new AppConfig { Outputs = new() { Targets = [target] } };
            using (var dialog = new PhysicalDeviceSettingsDialog(config, ["Default"], () => [device]))
            {
                var all = Descendants(dialog).ToArray();
                Check(!all.Any(c => c.Name is "DeviceJournal" or "DeviceNoJournal" ||
                          c.Text.Contains("USB diagnostics", StringComparison.OrdinalIgnoreCase) ||
                          c.Text.Contains("USB diagnostic journal", StringComparison.OrdinalIgnoreCase)),
                    "The complete diagnostics row and clearing controls are absent for both adapters.");
                var provider = all.OfType<CheckBox>().Single(c => c.Name == "DeviceProvider");
                Check(provider.Enabled && provider.Checked == target.ProviderEnabled && !dialog.DevicesChanged,
                    "The independent telemetry provider control remains without dirtying Settings.");
            }
            using var candidate = new PhysicalDeviceSettingsDialog(new AppConfig(), ["Default"], () => [device]);
            Check(!Descendants(candidate).Any(c => c.Name is "DeviceJournal" or "DeviceNoJournal" ||
                      c.Text.Contains("USB diagnostics", StringComparison.OrdinalIgnoreCase)),
                "An unconfigured candidate exposes no retired logging row.");
        }
    }

    public static void SharedPhysicalEventsAndFailures() => Sandbox(root =>
    {
        var runtime = new FileLogger(Path.Combine(root, "PinkieSysMon.Runtime.log"), "Runtime");
        var editor = new FileLogger(Path.Combine(root, "PinkieSysMon.Editor.log"), "Editor");
        var historical = Path.Combine(root, "trofeo-usb-history.journal.log");
        File.WriteAllText(historical, "retained historical evidence");
        using var runtimeSession = ApplicationLogSession.Start(runtime, "regression");
        using var editorSession = ApplicationLogSession.Start(editor, "regression");
        foreach (var protocol in new[] { "ly-bulk", "encrypted-jpeg" })
        {
            var target = Issue71AdoptionTests.Target(Issue71AdoptionTests.Device(protocol, 'a'));
            target.DeviceEnabled = true;
            var config = new AppConfig { Outputs = new() { Targets = [target] } };
            var healthy = new Frames(protocol, fail: false);
            using (var pump = new FramePump(config, target, Issue71AdoptionTests.Board(), new MetricStore(),
                       runtime, (_, _, _, _) => healthy, maximumFrames: 1))
            {
                pump.Start();
                Check(SpinWait.SpinUntil(() => pump.SentFrames == 1, 5000),
                    "A healthy injected adapter must complete its first frame.");
            }
            Check(healthy.Disposals == 1, "The shared worker retains native disposal ownership.");
            var failed = new Frames(protocol, fail: true);
            using (var pump = new FramePump(config, target, Issue71AdoptionTests.Board(), new MetricStore(),
                       runtime, (_, _, _, _) => failed, maximumFrames: 1, stopOnFailure: true))
            {
                pump.Start();
                Check(SpinWait.SpinUntil(() => pump.TestFailure is not null, 5000),
                    "An injected transfer error must reach the existing failure boundary.");
            }
        }

        var session = Read(runtime.Path);
        var errors = Read(runtime.ErrorsPath);
        foreach (var protocol in new[] { "ly-bulk", "encrypted-jpeg" })
        {
            var messages = session.Select(r => r.GetProperty("message").GetString()!).ToArray();
            Check(messages.Any(m => m.Contains("protocol=" + protocol) && m.Contains("first frame completed")) &&
                  messages.Any(m => m.Contains("protocol=" + protocol) && m.Contains("transport retired")),
                "Both adapters retain attributed first-frame and confirmed-retirement events.");
            var failure = errors.Single(r => r.GetProperty("message").GetString()!.Contains("protocol=" + protocol) &&
                                            r.GetProperty("exception").ValueKind == JsonValueKind.String);
            var id = failure.GetProperty("eventId").GetString();
            Check(session.Any(r => r.GetProperty("eventId").GetString() == id) &&
                  !File.ReadAllText(runtime.Path).Contains("fixture-native-failure"),
                "Full failure payloads remain only in Errors, with a correlated Runtime reference.");
        }
        Check(File.ReadAllText(historical) == "retained historical evidence" &&
              Directory.GetFiles(root).Select(Path.GetFileName).ToHashSet(StringComparer.Ordinal).SetEquals(
                  ["PinkieSysMon.Runtime.log", "PinkieSysMon.Editor.log", "PinkieSysMon.Errors.log",
                   "trofeo-usb-history.journal.log"]),
            "Only three core files are created; historical journals are preserved.");
        runtimeSession.MarkCompleted();
        editorSession.MarkCompleted();
    });

    public static void RetiredFlagDoesNotReconnect() => Sandbox(root =>
    {
        var target = Issue71AdoptionTests.Target(Issue71AdoptionTests.Device("encrypted-jpeg", 'a'));
        target.DeviceEnabled = true;
        target.RefreshIntervalMs = 10;
        var config = new AppConfig { Outputs = new() { Targets = [target] } };
        var path = Path.Combine(root, "app.json");
        config.Save(path);
        var log = new FileLogger(Path.Combine(root, "runtime.log"));
        var original = AppConfig.Load(path, log);
        var transport = new Frames("encrypted-jpeg", fail: false);
        var opens = 0;
        using var pump = new FramePump(original, original.Outputs.Targets.Single(), Issue71AdoptionTests.Board(),
            new MetricStore(), log, (_, _, _, _) => { Interlocked.Increment(ref opens); return transport; });
        pump.Start();
        Check(SpinWait.SpinUntil(() => pump.SentFrames >= 1, 5000), "Initial connection must send a frame.");
        var json = JsonNode.Parse(File.ReadAllText(path))!;
        json["Usb"]!["DiagnosticJournalEnabled"] = true;
        json["Outputs"]!["Targets"]![0]!["Usb"]!["DiagnosticJournalEnabled"] = true;
        File.WriteAllText(path, json.ToJsonString());
        var replacement = AppConfig.Load(path, log);
        var before = pump.SentFrames;
        using var plan = pump.PrepareReconfiguration(replacement, replacement.Outputs.Targets.Single(), Issue71AdoptionTests.Board());
        pump.CommitReconfiguration(plan);
        Check(SpinWait.SpinUntil(() => pump.SentFrames > before, 5000) &&
              Volatile.Read(ref opens) == 1 && transport.Disposals == 0,
            "A stale logging-only edit cannot retire or reopen the live transport.");
    });

    private sealed class Frames(string protocol, bool fail) : IPhysicalFrameTransport
    {
        public int Disposals;
        public int WireRotationDegrees => Profile.RotationDegrees;
        public PhysicalFrameProfile Profile => protocol == "encrypted-jpeg"
            ? PhysicalFrameProfile.EncryptedJpeg : new PhysicalFrameProfile(0);
        public void SendJpegWithDiagnostics(ReadOnlySpan<byte> jpeg, FrameRenderDiagnostics diagnostics)
        {
            if (fail) throw new IOException("fixture-native-failure");
        }
        public void RecordLifecycle(string state, string reason) { }
        public void Dispose() => Interlocked.Increment(ref Disposals);
    }

    private static IEnumerable<Control> Descendants(Control parent) => parent.Controls.Cast<Control>()
        .SelectMany(c => new[] { c }.Concat(Descendants(c)));

    private static JsonElement[] Read(string path) => File.ReadAllLines(path).Select(line =>
    {
        using var json = JsonDocument.Parse(line[line.IndexOf('{')..]);
        return json.RootElement.Clone();
    }).ToArray();

    private static void Sandbox(Action<string> work)
    {
        var root = Path.Combine(Path.GetTempPath(), "pinkie-issue104-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try { work(root); }
        finally { Directory.Delete(root, true); }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
