using System.Windows.Forms;
using PinkieSysMon.Editor;
using C = PinkieSysMon.DashboardModel;

namespace PinkieSysMon.RegressionTests;

internal static class Issue71PendingAdapterTests
{
    public static void SettingsAndTrayRemainReadOnly()
    {
        var target = Target();
        var config = new AppConfig { Outputs = new OutputConfig { Targets = [target] } };
        OutputTargetContract.ValidateAndNormalize(config.Outputs.Targets);
        using var dialog = new PhysicalDeviceSettingsDialog(config, ["Default"], () => []);
        var page = dialog.Controls.OfType<TabControl>().Single().TabPages.OfType<DeviceSettingsPage>().Single();
        if (page.Controls.Find("DeviceFields", true).Single().Enabled || dialog.DevicesChanged)
            throw new InvalidOperationException("An absent pending TURZX record must remain read-only without dirtying Settings.");
        var text = string.Join(" ", Descendants(page).OfType<Label>().Select(label => label.Text));
        if (!text.Contains("TURZX 12.3-inch") || text.Contains("Trofeo Vision 9.16 adapter"))
            throw new InvalidOperationException("A pending TURZX record must not inherit Trofeo's model label.");
        using var tray = new RuntimeTrayMenu(() => { }, _ => { }, () => { }, (_, _) => { });
        var status = new OutputSessionStatus(target.Id, target.Name, false, false, "DISABLED", 0, 0, 0)
        { Kind = "turzx", Dashboard = "Default", OutputEnabled = false };
        tray.RefreshOutputs([status], ["Default"], new HashSet<string>(), new Dictionary<string, int>());
        var menu = tray.Menu.Items.OfType<ToolStripMenuItem>().Single(item => Equals(item.Tag, target.Id));
        if (menu.DropDownItems.OfType<ToolStripMenuItem>().First().Enabled ||
            RuntimeTrayOutputContract.FrameValues(status) != ("N/A", "N/A", "N/A"))
            throw new InvalidOperationException("A known disabled model must project safely without enabling or fabricating frames.");
    }

    public static void DefaultFactoryRejectsUnintegratedTransport()
    {
        var path = Path.Combine(Path.GetTempPath(), "pinkie-issue71-gate-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        try
        {
            // Deliberately bypass file validation: the production factory must
            // still fail before constructing a physical worker or opening USB.
            var target = Target();
            target.DeviceEnabled = true;
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
    { Id = "pending-turzx", Name = "TURZX", Kind = "turzx", Transport = "turzx",
        DeviceId = "turzx:" + new string('a', 64), Dashboard = "Default", Turzx = new() };
    private static IEnumerable<Control> Descendants(Control control) => control.Controls.Cast<Control>()
        .SelectMany(child => new[] { child }.Concat(Descendants(child)));
}
