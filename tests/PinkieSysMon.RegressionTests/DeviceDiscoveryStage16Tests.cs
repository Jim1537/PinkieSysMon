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

    public static void PassiveSerialFingerprint()
    {
        const string path = @"\\?\usb#vid_0416&pid_5408#SYNTHETIC_SERIAL#{dee824ef-729b-4a0e-9c14-b7117d33a817}";
        Check(PassiveTrofeoIdentity.TryParseInstanceId(path, out var instanceId, out var token) &&
              instanceId == @"USB\VID_0416&PID_5408\SYNTHETIC_SERIAL" &&
              token == "SYNTHETIC_SERIAL",
            "The candidate must be extracted from the exact Trofeo PnP interface instance path.");
        Check(PassiveTrofeoIdentity.Fingerprint(token) ==
              PassiveTrofeoIdentity.Fingerprint("synthetic_serial") &&
              PassiveTrofeoIdentity.Fingerprint(token) !=
              PassiveTrofeoIdentity.Fingerprint("DIFFERENT_SERIAL"),
            "Fingerprints must ignore Windows path casing but distinguish individual serial tokens.");
        Check(!PassiveTrofeoIdentity.TryParseInstanceId(
                  @"\\?\usb#vid_0416&pid_5408#SYNTHETIC_SERIAL#{aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa}",
                  out _, out _) &&
              !PassiveTrofeoIdentity.TryParseInstanceId(
                  @"\\?\usb#vid_1234&pid_5408#SYNTHETIC_SERIAL#{dee824ef-729b-4a0e-9c14-b7117d33a817}",
                  out _, out _) &&
              !PassiveTrofeoIdentity.TryParseInstanceId(
                  @"\\?\usb#vid_0416&pid_5408##{dee824ef-729b-4a0e-9c14-b7117d33a817}",
                  out _, out _),
            "Wrong interface GUID, VID/PID or empty identity token must never produce a fingerprint.");
        Check(PassiveTrofeoIdentity.FingerprintIfUnique(token, 0) is null &&
              PassiveTrofeoIdentity.FingerprintIfUnique(token, 0x04) is null &&
              PassiveTrofeoIdentity.FingerprintIfUnique(token, 0x10) ==
              PassiveTrofeoIdentity.Fingerprint(token),
            "Only a PnP system-unique devnode may produce a candidate serial fingerprint.");
        Check(DeviceDiscovery.CreateTrofeoDescriptors([path])[0].PhysicalFingerprint is null,
            "The pure path parser must never assert hardware identity before native PnP verification.");
    }

    public static void DuplicateSerialFingerprint()
    {
        var first = new OutputDeviceDescriptor(
            "trofeo:" + new string('a', 64),
            OutputTargetContract.TrofeoTransport, "Synthetic A", "mock-a")
        {
            PhysicalFingerprint = PassiveTrofeoIdentity.Fingerprint("TWIN")
        };
        var second = new OutputDeviceDescriptor(
            "trofeo:" + new string('b', 64),
            OutputTargetContract.TrofeoTransport, "Synthetic B", "mock-b")
        {
            PhysicalFingerprint = PassiveTrofeoIdentity.Fingerprint("twin")
        };
        var target = Target();
        target.DeviceId = first.DeviceId;
        var collision = OutputDeviceSelection.Resolve(target, [first, second]);
        Check(collision.Status == OutputDeviceResolutionStatus.Ambiguous && collision.Device is null,
            "Two interfaces claiming the same PnP-verified physical identity must not bind through a unique legacy path ID.");
        var one = OutputDeviceSelection.Resolve(target, [first]);
        Check(one.Status == OutputDeviceResolutionStatus.Found && one.Device == first,
            "An explicit unambiguous legacy binding must remain valid.");
        var unrelated = second with
        {
            PhysicalFingerprint = PassiveTrofeoIdentity.Fingerprint("OTHER")
        };
        var distinct = OutputDeviceSelection.Resolve(target, [first, unrelated]);
        Check(distinct.Status == OutputDeviceResolutionStatus.Found && distinct.Device == first,
            "Two physically distinct fingerprints must preserve explicit single-interface selection.");
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
