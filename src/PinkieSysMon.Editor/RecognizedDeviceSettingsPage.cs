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
        var support = observation.Family.CandidateTransport is not null
            ? $"No matching usable {(observation.Family.CandidateTransport == OutputTargetContract.TrofeoTransport ? "Trofeo" : "TURZX")} interface was confirmed. Control is unavailable."
            : "Unsupported / protocol not available in PinkieSysMon.";
        var next = observation.Family.IntegrationIssue is { } issue
            ? $"Transport integration is tracked in Issue #{issue}."
            : "For support requests, include the product/model and USB product ID shown here.";
        _details.Text = $"{observation.Family.Name}\n\n" +
            $"USB VID:PID: {observation.Family.Product}\nStatus: {status}\n\n" +
            support + "\nNot configured. This read-only entry cannot be added or enabled.\n\n" +
            next + "\nDo not include device serials or Windows instance paths in public reports.";
    }
}
