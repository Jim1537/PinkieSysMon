using PinkieSysMon;

namespace PinkieSysMon.RegressionTests;

// Pure discovery/selection tests: no Windows enumeration, CreateFile or WinUSB.
// Production device-open lifecycle remains in FramePump/TrofeoTransport.
internal static class DeviceDiscoveryStage16Tests
{
    public static void PassiveReconnect()
    {
        const string path = @"\\?\usb#vid_0416&pid_5408#STAGE16-PORT-A#{DEE824EF-729B-4A0E-9C14-B7117D33A817}";
        const string unrelated = @"\\?\usb#vid_1234&pid_5678#UNRELATED";
        var target = Target();

        var devices = DeviceDiscovery.CreateTrofeoDescriptors([path, path.ToUpperInvariant(), unrelated]);
        Check(devices.Count == 1 && devices[0].Transport == OutputTargetContract.TrofeoTransport,
            "Discovery must filter unsupported interfaces and deduplicate repeated case-insensitive paths.");
        Check(!target.DeviceEnabled && !string.IsNullOrWhiteSpace(devices[0].DeviceId),
            "Passive discovery must never enable a disabled physical target.");

        target.DeviceId = devices[0].DeviceId;
        var connected = OutputDeviceSelection.Resolve(target, devices);
        var missing = OutputDeviceSelection.Resolve(target, []);
        var reconnected = DeviceDiscovery.CreateTrofeoDescriptors([path.ToUpperInvariant()]);
        var restored = OutputDeviceSelection.Resolve(target, reconnected);

        Check(connected.Status == OutputDeviceResolutionStatus.Found &&
              connected.Device?.DeviceId == target.DeviceId &&
              missing.Status == OutputDeviceResolutionStatus.Missing &&
              missing.Device is null &&
              restored.Status == OutputDeviceResolutionStatus.Found &&
              restored.Device?.DeviceId == target.DeviceId,
            "Exact interface identity must survive a passive missing/reconnect cycle without fallback.");
        Check(!target.DeviceEnabled && target.DeviceId == reconnected[0].DeviceId,
            "Discovery must not mutate the persisted Device Enabled or binding settings.");
    }

    public static void DuplicateIdentity()
    {
        var target = Target();
        target.DeviceId = "trofeo:" + new string('a', 64);
        var twins = new[]
        {
            new OutputDeviceDescriptor(target.DeviceId, OutputTargetContract.TrofeoTransport,
                "Synthetic Trofeo A", "fake-path-A"),
            new OutputDeviceDescriptor(target.DeviceId.ToUpperInvariant(), OutputTargetContract.TrofeoTransport,
                "Synthetic Trofeo B", "fake-path-B")
        };

        var bound = OutputDeviceSelection.Resolve(target, twins);
        Check(bound.Status == OutputDeviceResolutionStatus.Ambiguous && bound.Device is null,
            "Duplicate physical identities must never silently bind to the first match.");

        target.DeviceId = string.Empty;
        var unbound = OutputDeviceSelection.Resolve(target, twins);
        Check(unbound.Status == OutputDeviceResolutionStatus.Ambiguous && unbound.Device is null,
            "An unbound target must also refuse two compatible physical interfaces.");

        target.DeviceId = "trofeo:" + new string('b', 64);
        var absent = OutputDeviceSelection.Resolve(target, twins);
        Check(absent.Status == OutputDeviceResolutionStatus.Missing && absent.Device is null,
            "A missing exact binding must never fall back to a similar attached interface.");
        Check(!target.DeviceEnabled,
            "Ambiguous or absent discovery must not authorize a physical transport start.");
    }

    private static OutputTargetConfig Target() => new()
    {
        Id = "stage16-physical",
        Name = "Stage16 synthetic physical output",
        Kind = OutputTargetContract.TrofeoKind,
        Transport = OutputTargetContract.TrofeoTransport,
        DeviceEnabled = false
    };

    private static void Check(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
