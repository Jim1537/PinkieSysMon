using System.Windows.Forms;
using System.Collections.Concurrent;
using System.Text.Json;
using SkiaSharp;
using PinkieSysMon.Editor;
using C = PinkieSysMon.DashboardModel;

namespace PinkieSysMon.RegressionTests;

internal static class Issue71SettingsTests
{
    public static void ConnectedSettingsControls()
    {
        var device = Issue71AdoptionTests.Device("encrypted-jpeg", 'b');
        var config = new AppConfig();
        using (var dialog = new PhysicalDeviceSettingsDialog(config, ["Default"], () => [device]))
        {
            var page = dialog.Controls.OfType<TabControl>().Single().TabPages.OfType<DeviceSettingsPage>().Single();
            if (!page.Controls.Find("DeviceFields", true).Single().Enabled || dialog.DevicesChanged ||
                page.Controls.Find("DeviceJournal", true).Length != 0 ||
                page.Controls.Find("DeviceWidth", true).Length != 1 ||
                page.Controls.Find("DeviceTest", true).Single().Enabled)
                throw new InvalidOperationException("A connected TURZX candidate exposes supported fields without dirtying Settings or offering unsupported commands.");
            var choices = page.Controls.Find("DeviceProtocol", true).Single().Controls.OfType<RadioButton>().ToArray();
            if (choices.Length != 2 || choices.Any(choice => !choice.Enabled) || choices.Any(choice => choice.Checked))
                throw new InvalidOperationException("The candidate must expose both implemented protocols without inventing a hint for this unhinted fixture.");
            if (page.Controls.Find("AddDevice", true).Single().Enabled)
                throw new InvalidOperationException("A candidate cannot bypass Runtime Test through the old Add Device action.");
        }
        if (config.Outputs.Targets.Count != 1 || config.Outputs.Targets[0].Kind != "desktop")
            throw new InvalidOperationException("Closing an uncommitted device dialog cannot adopt hardware into configuration.");
    }

    public static void DefaultPhysicalFactoryAndSiblingIsolation()
    {
        var turzx = Issue71AdoptionTests.Target(Issue71AdoptionTests.Device("encrypted-jpeg", 'b'));
        var trofeo = Issue71AdoptionTests.Target(Issue71AdoptionTests.Device("ly-bulk", 'a'));
        turzx.DeviceEnabled = trofeo.DeviceEnabled = true;
        var config = new AppConfig { Outputs = new() { Targets = [turzx, trofeo] } };
        OutputTargetContract.ValidateAndNormalize(config.Outputs.Targets);
        var boards = new Dictionary<string, C.DashboardDefinition>
        {
            [turzx.Id] = Issue71AdoptionTests.Board("#FF0000"), [trofeo.Id] = Issue71AdoptionTests.Board("#0000FF")
        };
        var frames = new ConcurrentDictionary<string, RecordedFrames>();
        var root = Path.Combine(Path.GetTempPath(), "pinkie-manager-turzx-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            using var manager = new OutputSessionManager(config, boards, new MetricStore(), new FileLogger(Path.Combine(root, "worker.log")),
                physicalTransportFactory: (target, _, _, _, _) => frames.GetOrAdd(target.Id,
                    _ => new RecordedFrames(target.Transport == "encrypted-jpeg" ? PhysicalFrameProfile.EncryptedJpeg : new PhysicalFrameProfile(180))));
            manager.Start();
            if (!SpinWait.SpinUntil(() => manager.GetStatuses().All(s => s.SentFrames >= 2), 5000))
                throw new InvalidOperationException("The production manager factory must start both independent physical workers.");
            var own = frames[turzx.Id]; var sibling = frames[trofeo.Id];
            using (var image = SKBitmap.Decode(own.Images.First()))
                if (image.Width != 480 || image.Height != 1920 || image.GetPixel(360, 960).Red < 240)
                    throw new InvalidOperationException("Default TURZX dispatch must use its own profile and assigned red board.");
            using (var image = SKBitmap.Decode(sibling.Images.First()))
                if (image.Width != 1920 || image.Height != 480 || image.GetPixel(960, 240).Blue < 240)
                    throw new InvalidOperationException("TURZX integration must not change the Trofeo profile or assigned blue board.");
            var replacement = JsonSerializer.Deserialize<OutputTargetConfig>(JsonSerializer.Serialize(turzx))!;
            replacement.DeviceEnabled = false;
            manager.ReconfigurePhysicalEnablement(replacement, boards[turzx.Id], _ => { }, () => { });
            var siblingCount = sibling.Images.Count;
            if (!SpinWait.SpinUntil(() => sibling.Images.Count > siblingCount, 3000) || own.Disposals != 1 ||
                manager.GetStatuses().Single(s => s.TargetId == turzx.Id).IsActive)
                throw new InvalidOperationException("Disabling TURZX must drain only its session while Trofeo continues rendering.");
            manager.StopAll();
            if (sibling.Disposals != 1) throw new InvalidOperationException("Global stop must still dispose the surviving physical session once.");
        }
        finally { Directory.Delete(root, true); }
    }

    private sealed class RecordedFrames(PhysicalFrameProfile profile) : IPhysicalFrameTransport
    {
        public ConcurrentQueue<byte[]> Images { get; } = new();
        public int Disposals;
        public int WireRotationDegrees => profile.RotationDegrees;
        public PhysicalFrameProfile Profile => profile;
        public void SendJpegWithDiagnostics(ReadOnlySpan<byte> jpeg, FrameRenderDiagnostics diagnostics)
        {
            if (Volatile.Read(ref Disposals) != 0) throw new InvalidOperationException("Write after transport disposal.");
            Images.Enqueue(jpeg.ToArray());
        }
        public void RecordLifecycle(string state, string reason) { }
        public void Dispose() => Interlocked.Increment(ref Disposals);
    }

    public static void AbsentSettingsAndTrayRemainReadOnly()
    {
        var target = Target();
        var config = new AppConfig { Outputs = new OutputConfig { Targets = [target] } };
        OutputTargetContract.ValidateAndNormalize(config.Outputs.Targets);
        using var dialog = new PhysicalDeviceSettingsDialog(config, ["Default"], () => []);
        var page = dialog.Controls.OfType<TabControl>().Single().TabPages.OfType<DeviceSettingsPage>().Single();
        if (page.Controls.Find("DeviceFields", true).Single().Enabled || dialog.DevicesChanged)
            throw new InvalidOperationException("An absent TURZX record must remain read-only without dirtying Settings.");
        var text = string.Join(" ", Descendants(page).OfType<Label>().Select(label => label.Text));
        if (!text.Contains("Physical USB display") || !text.Contains(OutputTargetContract.EncryptedJpegTransport))
            throw new InvalidOperationException("An absent physical record must retain its generic type and independently selected protocol.");
        using var tray = new RuntimeTrayMenu(() => { }, _ => { }, () => { }, (_, _) => { });
        var status = new OutputSessionStatus(target.Id, target.Name, false, false, "DISABLED", 0, 0, 0)
        { Kind = "physical", Dashboard = "Default", OutputEnabled = false };
        tray.RefreshOutputs([status], ["Default"], new HashSet<string>(), new Dictionary<string, int>());
        var menu = tray.Menu.Items.OfType<ToolStripMenuItem>().Single(item => Equals(item.Tag, target.Id));
        if (menu.DropDownItems.OfType<ToolStripMenuItem>().First().Enabled ||
            RuntimeTrayOutputContract.FrameValues(status) != ("N/A", "N/A", "N/A"))
            throw new InvalidOperationException("A known disabled model must project safely without enabling or fabricating frames.");
    }

    public static void DefaultFactoryRejectsMismatchedTransport()
    {
        var path = Path.Combine(Path.GetTempPath(), "pinkie-issue71-gate-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        try
        {
            // Deliberately bypass file validation: the production factory must
            // still fail before constructing a physical worker or opening USB.
            var target = Target();
            target.DeviceEnabled = true;
            target.Transport = "unimplemented";
            var config = new AppConfig { Outputs = new OutputConfig { Targets = [target] } };
            using var manager = new OutputSessionManager(config, new C.DashboardDefinition(),
                new MetricStore(), new FileLogger(Path.Combine(path, "test.log")));
            try { manager.Start(); }
            catch (InvalidOperationException error) when (error.Message.Contains("adapter is not integrated"))
            {
                if (manager.GetStatuses().Single().IsActive)
                    throw new InvalidOperationException("A rejected adapter must not publish an active session.");
                return;
            }
            throw new InvalidOperationException("The default factory must not route TURZX to Trofeo.");
        }
        finally { Directory.Delete(path, true); }
    }

    private static OutputTargetConfig Target() => new()
    { Width = 1920, Height = 720, Id = "turzx-settings-test", Name = "TURZX", Kind = "physical", Transport = "encrypted-jpeg",
        DeviceId = "usb-interface:" + new string('a', 64), Dashboard = "Default", EncryptedJpeg = new() };
    private static IEnumerable<Control> Descendants(Control control) => control.Controls.Cast<Control>()
        .SelectMany(child => new[] { child }.Concat(Descendants(child)));
}
