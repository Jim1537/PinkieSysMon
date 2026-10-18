using System.Windows.Forms;
using PinkieSysMon.Editor;

namespace PinkieSysMon.RegressionTests;

internal static class Issue77DevicesUiTests
{
    public static void ReadOnlyTabsAndIndependentFailures()
    {
        var device = Issue77DeviceIdentityTests.Device("SYNTHETIC-UI");
        IReadOnlyList<OutputDeviceDescriptor> supported = [device];
        IReadOnlyList<UsbDisplayObservation> recognized = UsbDisplayDiscovery.BuildObservations([
            new UsbPnpNode(@"USB\VID_1CBE&PID_0123\SYNTHETIC-UI", null, "WINUSB", Guid.Empty, 0, true)]);
        var supportedFailure = false;
        var recognitionFailure = false;
        var config = new AppConfig();
        using var dialog = new PhysicalDeviceSettingsDialog(config, ["Default"],
            () => supportedFailure ? throw new InvalidOperationException("Synthetic interface scan failure.") : supported,
            discoverRecognizedDevices: () => recognitionFailure
                ? throw new InvalidOperationException("Synthetic PnP scan failure.") : recognized);
        var tabs = dialog.Controls.OfType<TabControl>().Single();
        Check(tabs.TabPages.Count == 2, "A supported candidate and unsupported TURZX must each have one Device tab.");
        var supportedPage = tabs.TabPages.OfType<DeviceSettingsPage>().Single();
        var infoPage = tabs.TabPages.OfType<RecognizedDeviceSettingsPage>().Single();
        var details = (Label)infoPage.Controls.Find("RecognizedDeviceDetails", true).Single();
        Check(details.Text.Contains("1CBE:0123") && details.Text.Contains("Unsupported / protocol not available") &&
              details.Text.Contains("Issue #71") && !details.Text.Contains("SYNTHETIC") &&
              infoPage.Controls.Find("AddDevice", true).Length == 0 &&
              infoPage.Controls.Find("DeviceEnabled", true).Length == 0 &&
              !dialog.DevicesChanged && config.Outputs.Targets.Count == 1,
            "Read-only recognition must explain support without private IDs, enable/adopt controls or config mutations.");
        tabs.SelectedTab = infoPage;
        recognitionFailure = true;
        dialog.RefreshDeviceDiscovery();
        Check(details.Text.Contains("Discovery unavailable") &&
              supportedPage.Controls.Find("DeviceFields", true).Single().Enabled && tabs.SelectedTab == infoPage,
            "Broader PnP failure must retain selection and cannot disable independently discovered supported hardware.");
        recognitionFailure = false;
        supportedFailure = true;
        dialog.RefreshDeviceDiscovery();
        Check(details.Text.Contains("Status: Recognized") &&
              !supportedPage.Controls.Find("DeviceFields", true).Single().Enabled,
            "Supported-interface failure cannot masquerade as a removed informational device or stale editable controls.");
        supportedFailure = false;
        recognized = [];
        dialog.RefreshDeviceDiscovery();
        Check(details.Text.Contains("Disconnected") && tabs.TabPages.Contains(infoPage) && !dialog.DevicesChanged,
            "Removal preserves its informational tab within the current dialog without dirtying the output draft.");
        dialog.ApplyOutputChanges(config);
        Check(config.Outputs.Targets.Count == 1, "OK on recognition alone cannot create an Output.");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
