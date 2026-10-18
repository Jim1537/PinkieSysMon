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
            new UsbPnpNode(@"USB\VID_1CBE&PID_0123\SYNTHETIC-UI", null, "WINUSB", Guid.Empty, 0, true),
            new UsbPnpNode(@"USB\VID_1CBE&PID_0080\SYNTHETIC-OTHER", null, "WINUSB", Guid.Empty, 0, true)]);
        var supportedFailure = false;
        var recognitionFailure = false;
        var config = new AppConfig();
        using var dialog = new PhysicalDeviceSettingsDialog(config, ["Default"],
            () => supportedFailure ? throw new InvalidOperationException("Synthetic interface scan failure.") : supported,
            discoverRecognizedDevices: () => recognitionFailure
                ? throw new InvalidOperationException("Synthetic PnP scan failure.") : recognized);
        var tabs = dialog.Controls.OfType<TabControl>().Single();
        Check(tabs.TabPages.Count == 3, "An exact supported candidate, an unconfirmed TURZX interface and an unsupported family each need one tab.");
        var supportedPage = tabs.TabPages.OfType<DeviceSettingsPage>().Single();
        static Label Details(RecognizedDeviceSettingsPage page) =>
            (Label)page.Controls.Find("RecognizedDeviceDetails", true).Single();
        var infoPages = tabs.TabPages.OfType<RecognizedDeviceSettingsPage>().ToArray();
        var infoPage = infoPages.Single(page => Details(page).Text.Contains("1CBE:0123"));
        var unsupportedPage = infoPages.Single(page => Details(page).Text.Contains("1CBE:0080"));
        var details = Details(infoPage);
        Check(details.Text.Contains("No matching usable TURZX interface was confirmed") &&
              !details.Text.Contains("Unsupported / protocol not available") &&
              details.Text.Contains("Issue #71") && !details.Text.Contains("SYNTHETIC") &&
              infoPages.All(page => page.Controls.Find("AddDevice", true).Length == 0 &&
                  page.Controls.Find("DeviceEnabled", true).Length == 0 && !Details(page).Text.Contains("SYNTHETIC")) &&
              !dialog.DevicesChanged && config.Outputs.Targets.Count == 1,
            "An integrated family without a proven exact interface stays read-only without private IDs, adoption or config mutation.");
        Check(Details(unsupportedPage).Text.Contains("Unsupported / protocol not available") &&
              !Details(unsupportedPage).Text.Contains("No matching usable TURZX interface"),
            "An unsupported Turing size must retain its unsupported-family explanation rather than inherit the TURZX adapter.");
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
        Check(details.Text.Contains("Disconnected") && Details(unsupportedPage).Text.Contains("Disconnected") &&
              tabs.TabPages.Contains(infoPage) && tabs.TabPages.Contains(unsupportedPage) && !dialog.DevicesChanged,
            "Removal preserves its informational tab within the current dialog without dirtying the output draft.");
        dialog.ApplyOutputChanges(config);
        Check(config.Outputs.Targets.Count == 1, "OK on recognition alone cannot create an Output.");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
