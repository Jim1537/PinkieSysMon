namespace PinkieSysMon.RegressionTests;

// Native menu objects and callback fakes; no NotifyIcon, launched process, visible window or USB.
internal static class RuntimeTrayMenuTests
{
    public static void RootOrderAndDispatch()
    {
        var commands = new List<RuntimeIpcCommand>();
        var editor = 0;
        var logs = 0;
        using var tray = new RuntimeTrayMenu(() => editor++, commands.Add, () => logs++, (_, _) => { });
        tray.RefreshOutputs([Physical("usb"), Desktop("back"), Desktop("front")], ["Board", "Other"],
            new HashSet<string>(), new Dictionary<string, int> { ["usb"] = 92 });
        Check(tray.Menu.Items.Cast<ToolStripItem>().Select(i => i is ToolStripSeparator ? "|" : i.Text)
            .SequenceEqual(new[]
            {
                "Control Center", "|", "Desktop", "back", "front", "|", "Physical Devices", "usb",
                "|", "Reload All", "|", "Starting...", "Open Logs Folder", "|", "Exit"
            }), "The root must retain exact section/separator/command order and per-kind configured order.");
        Check(tray.Menu.Items.Cast<ToolStripItem>().Where(i => i.Tag is string).Count() == 3 &&
              !Item(tray.Menu.Items, "Desktop").Enabled && !Item(tray.Menu.Items, "Desktop").HasDropDownItems &&
              !Item(tray.Menu.Items, "Physical Devices").Enabled && !Item(tray.Menu.Items, "Physical Devices").HasDropDownItems,
            "Each Output must occur once at the root under a non-clickable heading.");
        Item(tray.Menu.Items, "Control Center").PerformClick();
        Item(tray.Menu.Items, "Reload All").PerformClick();
        Item(tray.Menu.Items, "Open Logs Folder").PerformClick();
        Item(tray.Menu.Items, "Exit").PerformClick();
        Check(editor == 1 && logs == 1 && commands.Select(c => c.Kind).SequenceEqual(
                  new[] { RuntimeIpcCommandKind.StartOrReloadAll, RuntimeIpcCommandKind.Exit }) &&
              commands.All(c => c.TargetId is null) && !tray.Status.Enabled,
            "Fixed root actions must route once through their existing owners; overall status is read-only.");
        var status = tray.Status;
        tray.RefreshOutputs([], [], new HashSet<string>(), new Dictionary<string, int>());
        Check(tray.Menu.Items.Cast<ToolStripItem>().All(i => i.Tag is null) &&
              ReferenceEquals(status, tray.Status) && !status.IsDisposed,
            "An empty configured snapshot must retain fixed commands/headers/status without phantom Outputs.");
    }

    public static void DesktopActionsAndProviderOffFrames()
    {
        const string id = "panel\t\"opaque\"";
        var output = Desktop(id) with { TargetName = "A&B", Dashboard = "Board&One", Lock = true };
        var commands = new List<RuntimeIpcCommand>();
        using var tray = new RuntimeTrayMenu(() => { }, commands.Add, () => { }, (_, _) => throw new Exception("No device enable."));
        tray.RefreshOutputs([output], ["board&one", "Other"], new HashSet<string>(), new Dictionary<string, int>());
        var panel = Output(tray, id);
        Check(panel.Text == "A&&B" && panel.Enabled && panel.DropDownItems.Cast<ToolStripItem>().Select(i => i.Text)
            .SequenceEqual(new[]
            {
                "Enabled", "Dashboard", "Reload", "Lock", "Provider Enabled: False", "Refresh interval: 250 ms",
                "Frame count: 37", "Frame time: 12.5 ms"
            }), "Desktop must have only the eight approved rows, with direct frame observations even when publication is off.");
        var dashboard = Item(panel.DropDownItems, "Dashboard");
        Check(((ToolStripMenuItem)dashboard.DropDownItems[0]).Checked &&
              !((ToolStripMenuItem)dashboard.DropDownItems[1]).Checked,
            "Saved dashboard choices must indicate the assignment case-insensitively without changing the raw names.");
        ((ToolStripMenuItem)dashboard.DropDownItems[1]).PerformClick();
        Item(panel.DropDownItems, "Reload").PerformClick();
        Item(panel.DropDownItems, "Lock").PerformClick();
        Item(panel.DropDownItems, "Enabled").PerformClick();
        Check(commands.Count == 4 && commands.All(c => c.TargetId == id) &&
              commands[0] is { Kind: RuntimeIpcCommandKind.AssignDashboard, Dashboard: "Other" } &&
              commands[1].Kind == RuntimeIpcCommandKind.StartOrReloadTarget &&
              commands[2] is { Kind: RuntimeIpcCommandKind.SetDesktopLock, Value: false } &&
              commands[3] is { Kind: RuntimeIpcCommandKind.SetDesktopBoardEnabled, Value: false },
            "Native choices must retain opaque target IDs and route assignment/reload/Lock/enable exactly once.");

        commands.Clear();
        tray.RefreshOutputs([output with { OutputEnabled = false, BoardEnabled = false, ProviderEnabled = true }],
            ["Board&One", "Other"], new HashSet<string>(), new Dictionary<string, int>());
        panel = Output(tray, id);
        var enabled = Item(panel.DropDownItems, "Enabled");
        Check(panel.Enabled && enabled.Enabled && !enabled.Checked &&
              !Item(panel.DropDownItems, "Dashboard").Enabled && !Item(panel.DropDownItems, "Reload").Enabled &&
              !Item(panel.DropDownItems, "Lock").Enabled &&
              panel.DropDownItems.Cast<ToolStripItem>().Skip(4).All(i => !i.Enabled) &&
              Item(panel.DropDownItems, "Frame count: N/A").Text == "Frame count: N/A" &&
              Item(panel.DropDownItems, "Frame time: N/A ms").Text == "Frame time: N/A ms",
            "Disabled Desktop must retain reachable Enabled, gate every other action and invalidate stale live measurements.");
        Item(panel.DropDownItems, "Reload").PerformClick();
        Item(panel.DropDownItems, "Lock").PerformClick();
        enabled.PerformClick();
        Check(commands.Count == 1 && commands[0] is { Kind: RuntimeIpcCommandKind.SetDesktopBoardEnabled, Value: true },
            "Disabled actions must not dispatch; enabling needs no separate Start control.");
    }

    public static void RefreshOwnershipAndUnavailableFrames()
    {
        var commands = new List<RuntimeIpcCommand>();
        using var tray = new RuntimeTrayMenu(() => { }, commands.Add, () => { }, (_, _) => { });
        var empty = new Dictionary<string, int>();
        var enabledIds = new HashSet<string>();
        var output = Desktop("first");
        tray.RefreshOutputs([output], ["Board"], enabledIds, empty);
        var old = Output(tray, "first");
        var oldDropDown = old.DropDown;
        var oldChildren = old.DropDownItems.Cast<ToolStripItem>().ToArray();
        var oldDashboard = Item(old.DropDownItems, "Dashboard");
        var oldDashboardDropDown = oldDashboard.DropDown;
        var oldChoices = oldDashboard.DropDownItems.Cast<ToolStripItem>().ToArray();
        var failed = false;
        try { tray.RefreshOutputs([Desktop("prepared"), output with { Kind = "unsupported" }], ["Board"], enabledIds, empty); }
        catch (InvalidOperationException) { failed = true; }
        Check(failed && ReferenceEquals(Output(tray, "first"), old) && !old.IsDisposed &&
              tray.Menu.Items.Cast<ToolStripItem>().Count(i => i.Tag is string) == 1,
            "Failed preparation must retain the previous complete native tree without partially adopting new entries.");
        tray.RefreshOutputs([Desktop("second")], ["Board"], enabledIds, empty);
        Check(old.IsDisposed && old.Owner is null && !tray.Menu.Items.Contains(old),
            "Replacement must dispose the retired Output while owned and let WinForms remove it.");
        Check(oldDropDown.IsDisposed && oldChildren.All(i => i.IsDisposed),
            "Replacement must dispose the retired submenu and every direct child.");
        Check(oldDashboardDropDown.IsDisposed && oldChoices.All(i => i.IsDisposed),
            "Replacement must also dispose nested Dashboard choices and their submenu.");
        Check(tray.Menu.Items.Cast<ToolStripItem>().Count(i => i.Tag is string) == 1,
            "Replacement must leave exactly one current Output occurrence.");
        oldChildren[0].PerformClick();
        oldChoices[0].PerformClick();
        Item(Output(tray, "second").DropDownItems, "Enabled").PerformClick();
        Check(commands.Count == 1 && commands[0].TargetId == "second",
            "Retired callbacks must not mutate the old target; current native callbacks retain their own identity.");
        foreach (var state in new[] { "WAITING", "SUSPENDED", "ERROR", "AMBIGUOUS", "STOPPED", "DISABLED" })
            Check(RuntimeTrayOutputContract.FrameValues(output with { UsbState = state }) == ("N/A", "N/A", "N/A"),
                "Inactive/non-ready lifecycle observations must not present retained counters as valid live measurements.");
        foreach (var unavailable in new[]
        {
            output with { IsActive = false }, output with { IsConnected = false },
            output with { IsVisible = false }, output with { SentFrames = 0 }
        })
            Check(RuntimeTrayOutputContract.FrameValues(unavailable) == ("N/A", "N/A", "N/A"),
                "No active visible completed frame must remain unavailable, not fabricate zero-duration rendering.");
        Check(RuntimeTrayOutputContract.FrameValues(output with { LastFrameMs = double.NaN }) == ("37", "N/A", "N/A") &&
              RuntimeTrayOutputContract.FrameValues(output with { LastFrameMs = 0 }) == ("37", "0.0", "N/A") &&
              !RuntimeTrayOutputContract.CanUseActions(null),
            "Invalid measured duration and unknown enablement must fail closed; a real completed zero duration remains valid.");
        var current = Output(tray, "second");
        var currentDropDown = current.DropDown;
        var currentChildren = current.DropDownItems.Cast<ToolStripItem>().ToArray();
        var currentDashboard = Item(current.DropDownItems, "Dashboard");
        var currentDashboardDropDown = currentDashboard.DropDown;
        var currentChoices = currentDashboard.DropDownItems.Cast<ToolStripItem>().ToArray();
        tray.Dispose();
        Check(tray.Menu.IsDisposed && current.IsDisposed && tray.Status.IsDisposed &&
              currentDropDown.IsDisposed && currentChildren.All(i => i.IsDisposed) &&
              currentDashboardDropDown.IsDisposed && currentChoices.All(i => i.IsDisposed),
            "Menu owner shutdown must release fixed items and the complete dynamic submenu tree.");
        currentChildren[0].PerformClick();
        currentChoices[0].PerformClick();
        Check(commands.Count == 1, "Disposed shutdown callbacks must not dispatch additional commands.");
    }

    public static void PhysicalPlacementAndConsentBridge()
    {
        var commands = new List<RuntimeIpcCommand>();
        var permissions = new List<(string Id, bool Enabled)>();
        using var tray = new RuntimeTrayMenu(() => { }, commands.Add, () => { },
            (id, value) => permissions.Add((id, value)));
        var physical = Physical("device");
        tray.RefreshOutputs([physical], ["Board"], new HashSet<string>(), new Dictionary<string, int> { ["device"] = 92 });
        var device = Output(tray, "device");
        Check(device.DropDownItems.Cast<ToolStripItem>().Select(i => i.Text).SequenceEqual(new[]
            {
                "Enabled", "Dashboard", "Reload", "Connected: True", "Provider Enabled: False", "Refresh interval: 250 ms",
                "Frame count: 37", "Frame time: 12.5 ms", "JPEG Size: 1.5 KiB", "JPEG Quality: 92%"
            }), "Common physical placement must use transport observations and correctly scaled JPEG/configuration fields.");
        Item(device.DropDownItems, "Enabled").PerformClick();
        Check(permissions.SequenceEqual(new[] { ("device", false) }) && commands.Count == 0,
            "Physical enable/disable must route to the existing consent owner, never an unrestricted IPC setter.");
        permissions.Clear();
        var absent = physical with { OutputEnabled = false, DeviceEnabled = false, IsActive = false, IsConnected = false };
        tray.RefreshOutputs([absent], ["Board"], new HashSet<string>(), new Dictionary<string, int>());
        device = Output(tray, "device");
        Check(device.Enabled && !Item(device.DropDownItems, "Enabled").Enabled &&
              !Item(device.DropDownItems, "Dashboard").Enabled && !Item(device.DropDownItems, "Reload").Enabled &&
              Item(device.DropDownItems, "Connected: False").Text == "Connected: False" &&
              Item(device.DropDownItems, "Frame count: N/A").Text == "Frame count: N/A" &&
              Item(device.DropDownItems, "JPEG Quality: N/A%").Text == "JPEG Quality: N/A%",
            "Configured absent/unsafe disabled devices remain once with readable unavailable information and no permission grant.");
        Item(device.DropDownItems, "Enabled").PerformClick();
        Check(permissions.Count == 0, "Unsafe physical enable must not reach the consent callback.");
        tray.RefreshOutputs([absent], ["Board"], new HashSet<string> { "device" }, new Dictionary<string, int>());
        Item(Output(tray, "device").DropDownItems, "Enabled").PerformClick();
        Check(permissions.SequenceEqual(new[] { ("device", true) }),
            "Only fresh passive eligibility may offer a disabled configured device to the consent callback.");
        tray.RefreshOutputs([physical with { IsConnected = false }], [], new HashSet<string>(), new Dictionary<string, int>());
        Check(Item(Output(tray, "device").DropDownItems, "Enabled").Enabled &&
              !Item(Output(tray, "device").DropDownItems, "Dashboard").Enabled,
            "An enabled disconnected device stays disableable, and an unavailable dashboard catalog offers no assignment.");
    }

    private static OutputSessionStatus Desktop(string id) => new(id, id, true, true, "DISPLAYING", 37, 12.5, 0)
    {
        Kind = OutputTargetContract.DesktopKind, Dashboard = "Board", OutputEnabled = true,
        BoardEnabled = true, IsVisible = true, Lock = true, RefreshIntervalMs = 250
    };

    private static OutputSessionStatus Physical(string id) => new(id, id, true, true, "CONNECTED", 37, 12.5, 1536)
    {
        Kind = OutputTargetContract.TrofeoKind, Dashboard = "Board", OutputEnabled = true,
        DeviceEnabled = true, RefreshIntervalMs = 250
    };

    private static ToolStripMenuItem Output(RuntimeTrayMenu tray, string id) =>
        tray.Menu.Items.OfType<ToolStripMenuItem>().Single(i => i.Tag is string value && value == id);
    private static ToolStripMenuItem Item(ToolStripItemCollection items, string text) =>
        items.OfType<ToolStripMenuItem>().Single(i => i.Text == text);
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
