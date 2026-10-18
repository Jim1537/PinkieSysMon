namespace PinkieSysMon.Editor;

// Separate from OutputTargetConfig: recognition has no dashboard, USB options,
// adoption, enablement or persistence. Remember observations only while the
// dialog is open, so temporary removal cannot silently replace a tab's identity.
internal sealed record RecognizedDeviceSettingsEntry(UsbDisplayObservation Observation, bool? IsPresent);

internal sealed class RecognizedDeviceSettingsModel
{
    private readonly Dictionary<string, RecognizedDeviceSettingsEntry> _entries = new(StringComparer.Ordinal);

    public IReadOnlyList<RecognizedDeviceSettingsEntry> Entries => _entries.Values.ToArray();

    public void Refresh(IReadOnlyList<UsbDisplayObservation> observations,
        IReadOnlyList<OutputDeviceDescriptor> supportedDevices)
    {
        var readOnly = UsbDisplayReconciliation.ReadOnlyObservations(observations, supportedDevices);
        var represented = observations.Select(o => o.ObservationId)
            .Except(readOnly.Select(o => o.ObservationId), StringComparer.Ordinal).ToHashSet(StringComparer.Ordinal);
        foreach (var id in _entries.Keys.ToArray())
        {
            if (represented.Contains(id)) _entries.Remove(id);
            else _entries[id] = _entries[id] with { IsPresent = false };
        }
        foreach (var observation in readOnly)
            _entries[observation.ObservationId] = new RecognizedDeviceSettingsEntry(observation, true);
    }

    public void DiscoveryUnavailable()
    {
        foreach (var id in _entries.Keys.ToArray())
            _entries[id] = _entries[id] with { IsPresent = null };
    }
}
