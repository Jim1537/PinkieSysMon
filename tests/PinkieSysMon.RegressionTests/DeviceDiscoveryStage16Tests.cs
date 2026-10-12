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

    public static void InventoryPreservesKnownAndNew()
    {
        var known = Target();
        var other = Target();
        other.Kind = OutputTargetContract.DesktopKind;
        other.Transport = OutputTargetContract.DesktopTransport;
        var device = new OutputDeviceDescriptor(
            DeviceDiscovery.CreateStableDeviceId("known-stage16"),
            OutputTargetContract.TrofeoTransport, "Known", "known-stage16");
        var newDevice = new OutputDeviceDescriptor(
            DeviceDiscovery.CreateStableDeviceId("new-stage16"),
            OutputTargetContract.TrofeoTransport, "New", "new-stage16");
        known.DeviceId = device.DeviceId;

        var before = PhysicalDeviceInventory.Build([other, known], []);
        Check(before.Count == 1 &&
              before[0].Status == PhysicalDeviceInventoryStatus.Disconnected &&
              before[0].ConfiguredTarget == known &&
              before[0].CanRemove && !before[0].CanEditSettings,
            "Disconnected known physical devices must retain a removable record without changing settings.");

        var observed = PhysicalDeviceInventory.Build([other, known], [device, newDevice]);
        Check(observed.Count == 2 &&
              observed[0].Status == PhysicalDeviceInventoryStatus.Connected &&
              observed[0].ConfiguredTarget == known &&
              observed[0].DiscoveredDevice == device &&
              observed[0].CanEditSettings && observed[0].CanRemove &&
              observed[1].Status == PhysicalDeviceInventoryStatus.Unconfigured &&
              observed[1].ConfiguredTarget is null &&
              observed[1].DiscoveredDevice == newDevice &&
              !observed[1].CanEditSettings && !observed[1].CanRemove,
            "Inventory must bind only the exact known ID and expose new hardware as an unconfigured candidate.");
        Check(!known.DeviceEnabled && known.DeviceId == device.DeviceId,
            "Passive inventory must not enable hardware or mutate a configured physical binding.");

        var legacy = Target();
        var legacyObserved = PhysicalDeviceInventory.Build([legacy], [device]);
        Check(legacyObserved.Count == 1 &&
              legacyObserved[0].Status == PhysicalDeviceInventoryStatus.Connected &&
              legacy.DeviceId.Length == 0,
            "An unbound legacy target may use the sole physical device without persisting an implicit binding.");
        var legacyAmbiguous = PhysicalDeviceInventory.Build([legacy], [device, newDevice]);
        Check(legacyAmbiguous[0].Status == PhysicalDeviceInventoryStatus.Ambiguous &&
              legacyAmbiguous[0].DiscoveredDevice is null,
            "An unbound legacy target must be ambiguous with multiple devices.");

        var reconnect = PhysicalDeviceInventory.Build([known], [device]);
        Check(reconnect.Count == 1 &&
              reconnect[0].Status == PhysicalDeviceInventoryStatus.Connected &&
              reconnect[0].ConfiguredTarget == known,
            "Reconnecting the same identity must restore its previously configured record.");
    }

    public static void DeviceEnabledConsentPolicy()
    {
        var known = Target();
        var device = new OutputDeviceDescriptor(
            DeviceDiscovery.CreateStableDeviceId("stage17-physical-device"),
            OutputTargetContract.TrofeoTransport, "Known", "stage17-physical-device");
        known.DeviceId = device.DeviceId;

        var found = PhysicalDeviceInventory.Build([known], [device]);
        Check(PhysicalDeviceInventory.CanOfferEnable(found.Single()) && !known.DeviceEnabled,
            "Only a present and exactly bound disabled physical target may request USB ownership consent.");

        known.DeviceEnabled = true;
        Check(!PhysicalDeviceInventory.CanOfferEnable(PhysicalDeviceInventory.Build([known], [device]).Single()),
            "An already enabled target must not prompt again for enablement.");
        known.DeviceEnabled = false;

        Check(!PhysicalDeviceInventory.CanOfferEnable(PhysicalDeviceInventory.Build([known], []).Single()),
            "Disconnected configured hardware must never be enabled by the menu.");

        known.DeviceId = string.Empty;
        Check(!PhysicalDeviceInventory.CanOfferEnable(PhysicalDeviceInventory.Build([known], [device]).Single()),
            "Implicit single-device legacy matching is insufficient to enable USB ownership without explicit binding.");
        known.DeviceId = device.DeviceId;

        var twin = device with { DevicePath = "second-path" };
        Check(!PhysicalDeviceInventory.CanOfferEnable(PhysicalDeviceInventory.Build([known], [device, twin])[0]),
            "Duplicate physical device identity must fail closed when offering enable consent.");

        var unconfigured = PhysicalDeviceInventory.Build([], [device]);
        Check(!PhysicalDeviceInventory.CanOfferEnable(unconfigured.Single()),
            "Unconfigured newly detected hardware cannot be enabled before explicit adoption.");
    }

    public static void DeviceRemovalPolicy()
    {
        var physical = Target();
        physical.DeviceId = DeviceDiscovery.CreateStableDeviceId("stage18-physical");
        physical.DeviceEnabled = true;
        physical.ProviderEnabled = true;
        physical.Dashboard = "PhysicalBoard";
        var desktop = new OutputTargetConfig
        {
            Id = "stage18-desktop", Name = "Unaffected desktop",
            Kind = OutputTargetContract.DesktopKind, Transport = OutputTargetContract.DesktopTransport,
            Desktop = new DesktopOutputConfig(), Dashboard = "Default"
        };
        var config = new AppConfig { Outputs = new OutputConfig { Targets = [physical, desktop] } };
        var device = new OutputDeviceDescriptor(
            physical.DeviceId, OutputTargetContract.TrofeoTransport, "Known device", "synthetic-path");
        var before = PhysicalDeviceInventory.Build(config.Outputs.Targets, [device]);
        var disconnected = PhysicalDeviceInventory.Build(config.Outputs.Targets, []);
        Check(before.Single(entry => entry.IsPersisted).Status == PhysicalDeviceInventoryStatus.Connected &&
              disconnected.Single().Status == PhysicalDeviceInventoryStatus.Disconnected &&
              disconnected.Single().CanRemove &&
              !disconnected.Single().CanEditSettings &&
              PhysicalDeviceRemovalPolicy.CanRemove(config, physical.Id),
            "A configured physical Output must remain removable even when disconnected.");
        var reconnected = PhysicalDeviceInventory.Build(config.Outputs.Targets, [device]);
        Check(reconnected.Single(entry => entry.IsPersisted).Status == PhysicalDeviceInventoryStatus.Connected &&
              physical.DeviceEnabled && physical.ProviderEnabled &&
              physical.Dashboard == "PhysicalBoard",
            "Passive reconnect must retain the original identity and both independent switches.");

        PhysicalDeviceRemovalPolicy.Remove(config, physical.Id);
        Check(config.Outputs.Targets.Count == 1 &&
              ReferenceEquals(config.Outputs.Targets[0], desktop) &&
              !config.Outputs.Targets.Any(t => t.Id == physical.Id),
            "Removal must delete only the selected physical Output and leave its Desktop sibling unchanged.");
        var after = PhysicalDeviceInventory.Build(config.Outputs.Targets, [device]);
        Check(after.Count == 1 && after[0].Status == PhysicalDeviceInventoryStatus.Unconfigured &&
              after[0].ConfiguredTarget is null && after[0].DiscoveredDevice == device,
            "An attached removed device may be observed passively but must never be re-adopted.");
        Check(!PhysicalDeviceRemovalPolicy.CanRemove(config, desktop.Id),
            "Desktop Outputs must never be removable through the physical-device command.");
        var rejected = false;
        try { PhysicalDeviceRemovalPolicy.Remove(config, desktop.Id); }
        catch (InvalidOperationException) { rejected = true; }
        Check(rejected && config.Outputs.Targets.Single() == desktop,
            "Invalid physical removal must not change surviving output configuration.");

        var solePhysical = new AppConfig
        {
            Outputs = new OutputConfig { Targets = [Target()] }
        };
        Check(!PhysicalDeviceRemovalPolicy.CanRemove(solePhysical, solePhysical.Outputs.Targets[0].Id),
            "The final physical Output must never offer Remove.");
        rejected = false;
        try { PhysicalDeviceRemovalPolicy.Remove(solePhysical, solePhysical.Outputs.Targets[0].Id); }
        catch (InvalidOperationException) { rejected = true; }
        Check(rejected && solePhysical.Outputs.Targets.Count == 1,
            "The last remaining Output must not be removed even through a direct call.");
    }

    public static void InventoryCaptureIsolation()
    {
        var known = Target();
        var device = new OutputDeviceDescriptor(
            DeviceDiscovery.CreateStableDeviceId("stage16-captured-interface"),
            OutputTargetContract.TrofeoTransport, "Captured", "stage16-captured-interface")
        {
            PhysicalFingerprint = PassiveTrofeoIdentity.Fingerprint("synthetic-captured-serial")
        };
        known.DeviceId = device.DeviceId;
        var enumerations = 0;
        var snapshot = PhysicalDeviceInventory.Capture([known], () =>
        {
            enumerations++;
            return [device];
        });

        Check(enumerations == 1 &&
              snapshot.Count == 1 &&
              snapshot[0].Status == PhysicalDeviceInventoryStatus.Connected &&
              snapshot[0].DiscoveredDevice?.PhysicalFingerprint == device.PhysicalFingerprint &&
              known.DeviceId == device.DeviceId && !known.DeviceEnabled,
            "Capturing a discovery snapshot must query only once, retain the selected fingerprint and never enable hardware.");
        var unavailable = PhysicalDeviceInventory.Capture([known], () => []);
        Check(unavailable.Count == 1 &&
              unavailable[0].Status == PhysicalDeviceInventoryStatus.Disconnected &&
              unavailable[0].CanRemove && !unavailable[0].CanEditSettings,
            "A subsequent passive capture with no device must retain the configured output for the future Settings view.");
    }

    public static void InventoryNeverRebindsAmbiguousHardware()
    {
        var known = Target();
        var previous = new OutputDeviceDescriptor(
            DeviceDiscovery.CreateStableDeviceId("old-stage16"),
            OutputTargetContract.TrofeoTransport, "Known", "old-stage16")
        {
            PhysicalFingerprint = PassiveTrofeoIdentity.Fingerprint("same-serial")
        };
        var newPath = new OutputDeviceDescriptor(
            DeviceDiscovery.CreateStableDeviceId("other-port-stage16"),
            OutputTargetContract.TrofeoTransport, "Other port", "other-port-stage16")
        {
            PhysicalFingerprint = previous.PhysicalFingerprint
        };
        known.DeviceId = previous.DeviceId;

        var changed = PhysicalDeviceInventory.Build([known], [newPath]);
        Check(changed.Count == 2 &&
              changed[0].Status == PhysicalDeviceInventoryStatus.Disconnected &&
              changed[0].ConfiguredTarget == known &&
              changed[1].Status == PhysicalDeviceInventoryStatus.Unconfigured &&
              changed[1].ConfiguredTarget is null,
            "Even a matching serial fingerprint must not silently rewrite an existing legacy path binding.");

        var twins = PhysicalDeviceInventory.Build([known], [previous, newPath]);
        Check(twins.Count == 3 &&
              twins[0].Status == PhysicalDeviceInventoryStatus.Ambiguous &&
              twins[0].DiscoveredDevice is null &&
              twins.Skip(1).All(entry =>
                  entry.Status == PhysicalDeviceInventoryStatus.Ambiguous &&
                  entry.ConfiguredTarget is null),
            "Duplicate physical fingerprints must fail closed for known and unconfigured devices.");

        var duplicateConfig = Target();
        duplicateConfig.DeviceId = known.DeviceId;
        var bad = PhysicalDeviceInventory.Build([known, duplicateConfig], [previous]);
        Check(bad.Count == 3 &&
              bad.Take(2).All(entry => entry.Status == PhysicalDeviceInventoryStatus.Ambiguous),
            "Even unvalidated duplicate configured bindings must not claim the same device.");
        Check(!known.DeviceEnabled && !duplicateConfig.DeviceEnabled,
            "Inventory enumeration may not alter Device Enabled.");
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
