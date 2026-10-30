namespace PinkieSysMon.Editor;

// No OutputTargetConfig or mutation callbacks: this page cannot adopt or enable
// hardware. Product identifiers and family names contain no private unit IDs.
internal sealed class RecognizedDeviceSettingsPage : TabPage
{
    private readonly Label _details = new()
    {
        Name = "RecognizedDeviceDetails", Dock = DockStyle.Top, AutoSize = true,
        MaximumSize = new Size(790, 0), Padding = new Padding(12), AccessibleName = "USB display status"
    };

    public RecognizedDeviceSettingsPage()
    {
        AutoScroll = true;
        Controls.Add(_details);
    }

    public void UpdateState(RecognizedDeviceSettingsEntry entry)
    {
        var observation = entry.Observation;
        var status = entry.IsPresent switch
        {
            false => "Disconnected",
            null => "Discovery unavailable",
            _ => observation.Status switch
            {
                UsbDisplayObservationStatus.Ambiguous => "Ambiguous identity",
                UsbDisplayObservationStatus.MetadataUnavailable => "Windows device metadata unavailable",
                _ => "Recognized"
            }
        };
        var support = "No matching eligible WinUSB interface was confirmed. Control is unavailable.";
        var prerequisite = observation.Status == UsbDisplayObservationStatus.Ambiguous
            ? "Resolve the ambiguous device identity before selecting an interface."
            : observation.Status == UsbDisplayObservationStatus.MetadataUnavailable
                ? "Windows did not provide complete device metadata. Reconnect and retry discovery."
                : observation.ProblemCodes.Any(code => code != 0)
                    ? "Windows reports a device problem. Check the device status in Device Manager."
                    : !observation.DriverServices.Contains("WINUSB", StringComparer.OrdinalIgnoreCase)
                        ? "No usable WinUSB driver was observed. HID and storage interfaces cannot use the current transports."
                        : "The registered interface and its exact device association must be available before Test.";
        var missing = UsbDisplayCatalog.ProtocolChoices(observation.Family).Where(choice => !choice.IsImplemented)
            .Select(choice => choice.Label).ToArray();
        var next = "A catalog protocol hint is only a suggestion; an eligible interface permits explicit protocol selection. " +
            "For support requests, include the product/model and USB product ID shown here.";
        _details.Text = $"{observation.Family.Name}\n\n" +
            $"USB VID:PID: {observation.Family.Product}\nStatus: {status}\n\n" +
            support + "\n" + prerequisite + (missing.Length == 0 ? "" : "\n" + string.Join("\n", missing)) + "\nNot configured. This read-only entry cannot be added or enabled.\n\n" +
            next + "\nDo not include device serials or Windows instance paths in public reports.";
    }
}
