using System.Text;
using PinkieSysMon.Editor;

namespace PinkieSysMon.RegressionTests;

internal static class Issue77DeviceIdentityTests
{
    public static void ExactDevnodeMetadata()
    {
        var id = Id("0416:5408", "SYNTHETIC-A");
        var bytes = Encoding.Unicode.GetBytes(id + '\0');
        var key = PassiveUsbInterfaceIdentity.DecodeTrofeoNodeKey(0x12, bytes, (uint)bytes.Length);
        Check(key == UsbDisplayDiscovery.CreateObservationId(id.ToLowerInvariant()) &&
              key != PassiveTrofeoIdentity.Fingerprint("SYNTHETIC-A") &&
              key != Device("SYNTHETIC-A").DeviceId && !key!.Contains("SYNTHETIC"),
            "Devnode correlation, serial claims and persisted interface bindings must remain separate namespaces.");
        foreach (var invalid in new[] { Id("1CBE:0123", "SYNTHETIC-A"), id + "\0EXTRA",
            @"HID\VID_0416&PID_5408\SYNTHETIC-A", "USB\\VID_0416&PID_5408\\BAD\nTOKEN" })
        {
            var bad = Encoding.Unicode.GetBytes(invalid + '\0');
            Check(PassiveUsbInterfaceIdentity.DecodeTrofeoNodeKey(0x12, bad, (uint)bad.Length) is null,
                "Wrong family, enumerator and malformed metadata must not produce a Trofeo node key.");
        }
        Check(PassiveUsbInterfaceIdentity.DecodeTrofeoNodeKey(1, bytes, (uint)bytes.Length) is null &&
              PassiveUsbInterfaceIdentity.DecodeTrofeoNodeKey(0x12, bytes, (uint)bytes.Length - 1) is null &&
              PassiveUsbInterfaceIdentity.DecodeTrofeoNodeKey(0x12, bytes, (uint)bytes.Length + 2) is null &&
              PassiveUsbInterfaceIdentity.DecodeTrofeoNodeKey(0x12, bytes, 402) is null,
            "Property type, Unicode termination and bounded native lengths must be validated.");
        var unterminated = Encoding.Unicode.GetBytes(id);
        Check(PassiveUsbInterfaceIdentity.DecodeTrofeoNodeKey(0x12, unterminated, (uint)unterminated.Length) is null,
            "An unterminated metadata string must never become an identity.");
    }

    public static void ReconcileDistinctAndCompositeDevices()
    {
        var a = Id("0416:5408", "SYNTHETIC-A");
        var b = Id("0416:5408", "SYNTHETIC-B");
        var turzx = Id("1CBE:0123", "SYNTHETIC-A");
        var child = @"USB\VID_0416&PID_5408&MI_00\SYNTHETIC-FUNCTION";
        var observations = UsbDisplayDiscovery.BuildObservations([Node(a), Node(b), Node(turzx), Node(child, a)]);
        var first = Device("SYNTHETIC-A") with { PnpNodeKey = UsbDisplayDiscovery.CreateObservationId(child) };
        var second = Device("SYNTHETIC-B") with { PnpNodeKey = UsbDisplayDiscovery.CreateObservationId(b) };
        Check(observations.Count == 3 && observations.Single(o => o.NodeCount == 2).NodeKeys.Count == 2,
            "Composite grouping retains hashed member IDs while identical models remain separate units.");
        var remaining = UsbDisplayReconciliation.ReadOnlyObservations(observations, [first, second]);
        Check(remaining.Count == 1 && remaining[0].Family.Product == new UsbProductId(0x1CBE, 0x0123),
            "Exact parent/function membership suppresses only the independently discovered LY interfaces.");
        Check(UsbDisplayReconciliation.ReadOnlyObservations(observations, [first with { PnpNodeKey = null }]).Count == 3,
            "Same model, serial text or similar path is insufficient to guess a correlation.");
        Check(UsbDisplayReconciliation.ReadOnlyObservations(observations,
                [first with { PnpNodeKey = UsbDisplayDiscovery.CreateObservationId(turzx) }]).Count == 3,
            "A mismatched product/handler pair cannot authorize a cross-family match.");
        var duplicate = second with { PnpNodeKey = first.PnpNodeKey };
        Check(UsbDisplayReconciliation.ReadOnlyObservations(observations, [first, duplicate])
                .Single(o => o.NodeCount == 2).Status == UsbDisplayObservationStatus.Ambiguous,
            "Two interfaces claiming the same physical observation remain visible and ambiguous.");
        var unavailable = UsbDisplayDiscovery.BuildObservations([new UsbPnpNode(a, null, null, null, null, false)]);
        Check(UsbDisplayReconciliation.ReadOnlyObservations(unavailable,
                [first with { PnpNodeKey = UsbDisplayDiscovery.CreateObservationId(a) }]).Count == 1,
            "Missing PnP metadata must stay visible instead of being silently discarded.");
    }

    public static void BindingAndAmbiguity()
    {
        var first = Device("SYNTHETIC-A") with { PnpNodeKey = "usb-pnp:synthetic-node-a" };
        var second = Device("SYNTHETIC-B") with { PnpNodeKey = "usb-pnp:synthetic-node-b" };
        var target = Target(first);
        Check(OutputDeviceSelection.Resolve(target, [second, first]).Device == first,
            "Exact binding must select its unit independently of enumeration order or shared model.");
        var collision = second with { PnpNodeKey = first.PnpNodeKey };
        Check(OutputDeviceSelection.Resolve(target, [first, collision]).Status == OutputDeviceResolutionStatus.Ambiguous &&
              PhysicalDeviceInventory.Build([target], [first, collision]).All(e => e.Status == PhysicalDeviceInventoryStatus.Ambiguous),
            "Repeated devnode identity must fail closed in Runtime selection and every inventory row.");
        var fingerprint = PassiveTrofeoIdentity.Fingerprint("SYNTHETIC-SERIAL");
        Check(OutputDeviceSelection.Resolve(target, [first with { PhysicalFingerprint = fingerprint },
                second with { PhysicalFingerprint = fingerprint }]).Status == OutputDeviceResolutionStatus.Ambiguous,
            "Duplicate optional serial claims remain ambiguous even when per-path bindings differ.");
        var moved = Device("SYNTHETIC-NEW-PORT") with { PhysicalFingerprint = fingerprint, PnpNodeKey = first.PnpNodeKey };
        Check(OutputDeviceSelection.Resolve(target, [moved]).Status == OutputDeviceResolutionStatus.Missing &&
              target.DeviceId == first.DeviceId && !target.DeviceEnabled && target.Dashboard == "RetainedBoard",
            "New path, matching node key or matching serial must not rebind saved identity, settings or enablement.");
        Check(PhysicalDeviceInventory.Build([target, Target(first)], [first])
                .All(e => e.Status == PhysicalDeviceInventoryStatus.Ambiguous),
            "Duplicate saved bindings must not select the first logical Output.");
        target.DeviceId = "";
        Check(OutputDeviceSelection.Resolve(target, [first]).Status == OutputDeviceResolutionStatus.Missing &&
              OutputDeviceSelection.Resolve(target, [first, second]).Status == OutputDeviceResolutionStatus.Missing &&
              OutputDeviceSelection.Resolve(target, []).Status == OutputDeviceResolutionStatus.Missing &&
              target.DeviceId == "",
            "An unbound physical Output must stay unresolved with zero, one or multiple devices and never gain an implicit binding.");
    }

    public static void RecognitionAndDispatchContract()
    {
        Check(UsbDisplayCatalog.Families.Count(f => f.CandidateTransport is not null) == 2 &&
              UsbDisplayCatalog.Find(new UsbProductId(0x0416, 0x5408))!.CandidateTransport == OutputTargetContract.LyBulkTransport &&
              UsbDisplayCatalog.Find(new UsbProductId(0x1CBE, 0x0123)) is { CandidateTransport: OutputTargetContract.EncryptedJpegTransport, IntegrationIssue: 71 },
            "Only the reviewed LY and TURZX adapters are eligible; TURZX integration remains owned by Issue 71.");
        var path = Device("SYNTHETIC-A").DevicePath;
        Check(DeviceDiscovery.CreateTrofeoDescriptors([path, path.ToUpperInvariant(),
                path.Replace("5408", "54080"), path.Replace("5408", "5409"),
                path.Replace("vid_0416&pid_5408", "vid_1cbe&pid_0123"),
                path.Replace("DEE824EF-729B-4A0E-9C14-B7117D33A817", "AAAAAAAA-AAAA-AAAA-AAAA-AAAAAAAAAAAA")]).Count == 1,
            "Catalog families, product substrings and wrong interface classes cannot enter Trofeo dispatch.");
        var target = Target(Device("SYNTHETIC-A"));
        target.Transport = "unimplemented";
        Check(OutputDeviceSelection.Resolve(target, [Device("SYNTHETIC-A")]).Status == OutputDeviceResolutionStatus.UnsupportedTransport,
            "An unsupported configured handler must fail explicitly without discovery fallback.");
    }

    public static void InformationalDraftLifecycle()
    {
        var config = new AppConfig();
        var deviceDraft = new DeviceSettingsModel(config);
        var model = new RecognizedDeviceSettingsModel();
        var a = Id("1CBE:0123", "SYNTHETIC-A");
        var b = Id("1CBE:0123", "SYNTHETIC-B");
        var observations = UsbDisplayDiscovery.BuildObservations([Node(a), Node(b)]);
        model.Refresh(observations, []);
        var order = model.Entries.Select(e => e.Observation.ObservationId).ToArray();
        Check(model.Entries.Count == 2 && model.Entries.All(e => e.IsPresent == true) &&
              !deviceDraft.IsDirty && config.Outputs.Targets.Count == 1 && deviceDraft.Entries.Count == 0,
            "Recognition must not fabricate an Output, dirty the draft, or save/enable a device.");
        model.Refresh([], []);
        Check(model.Entries.All(e => e.IsPresent == false), "Disconnected informational identities stay visible in this dialog.");
        model.DiscoveryUnavailable();
        Check(model.Entries.All(e => e.IsPresent is null), "Discovery failure is distinct from successful removal.");
        model.Refresh(observations.Reverse().ToArray(), []);
        Check(model.Entries.Select(e => e.Observation.ObservationId).SequenceEqual(order) &&
              model.Entries.All(e => e.IsPresent == true), "Refresh order must not shuffle retained informational tabs.");
        var lyId = Id("0416:5408", "SYNTHETIC-LY");
        var ly = UsbDisplayDiscovery.BuildObservations([Node(lyId)]);
        model.Refresh(ly, []);
        Check(model.Entries.Count == 3, "An LY product without its supported interface remains informational.");
        model.Refresh(ly, [Device("SYNTHETIC-LY") with { PnpNodeKey = UsbDisplayDiscovery.CreateObservationId(lyId) }]);
        Check(model.Entries.Count == 2 && model.Entries.All(e => e.IsPresent == false),
            "A proven supported match removes only its duplicate informational entry, preserving other identities.");
        Check(new RecognizedDeviceSettingsModel().Entries.Count == 0,
            "Unconfigured recognition history is not persisted or restored on a new dialog.");
    }

    public static void CompatiblePersistenceAndConsent()
    {
        var directory = Path.Combine(Path.GetTempPath(), "Pinkie-Issue77-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "app.json");
            var log = new FileLogger(Path.Combine(directory, "test.log"));
            var first = Device("SYNTHETIC-A") with { PnpNodeKey = "usb-pnp:synthetic-node-a" };
            var second = Device("SYNTHETIC-B") with { PnpNodeKey = "usb-pnp:synthetic-node-b" };
            var config = new AppConfig();
            var target = Target(first);
            config.Outputs.Targets.Add(target);
            config.Save(path);
            var baseline = File.ReadAllBytes(path);
            var model = new DeviceSettingsModel(config);
            model.Refresh([first, second]);
            model.ApplyTo(config);
            Check(!model.IsDirty && File.ReadAllBytes(path).SequenceEqual(baseline) && config.Outputs.Targets.Count == 2,
                "Discovery and an unchanged draft must not adopt a second unit or write app.json.");
            var draft = model.Entries.Single(e => e.IsConfigured).Target;
            Check(!model.SetEnabled(draft, true, false) && !model.IsDirty && !draft.DeviceEnabled,
                "Default-No consent must preserve disabled saved bindings.");
            Check(model.SetEnabled(draft, true, true) && !target.DeviceEnabled,
                "Confirmed consent changes only the detached draft.");
            var duplicate = second with { PnpNodeKey = first.PnpNodeKey };
            Throws<InvalidOperationException>(() => model.ValidateForApply([first, duplicate]));
            Check(!target.DeviceEnabled && File.ReadAllBytes(path).SequenceEqual(baseline),
                "An identity collision after consent must reject apply without saving or mutating live config.");
            model.ValidateForApply([first, second]);
            model.ApplyTo(config);
            config.Save(path);
            var enabledBytes = File.ReadAllBytes(path);
            var loaded = AppConfig.Load(path, log);
            loaded.Save(path);
            var persisted = loaded.Outputs.Targets.Single(t => t.Id == target.Id);
            Check(persisted.DeviceId == first.DeviceId && persisted.DeviceEnabled &&
                  persisted.Dashboard == "RetainedBoard" && File.ReadAllBytes(path).SequenceEqual(enabledBytes) &&
                  !File.ReadAllText(path).Contains("PnpNodeKey") && !File.ReadAllText(path).Contains("SYNTHETIC-A"),
                "Old trofeo binding semantics and save/reload idempotence remain unchanged; snapshot metadata stays private.");
            persisted.Transport = "unimplemented";
            Throws<InvalidDataException>(() => loaded.Save(path));
            Check(File.ReadAllBytes(path).SequenceEqual(enabledBytes), "Unsupported adapter configuration must fail before persistence.");
            persisted.Transport = OutputTargetContract.LyBulkTransport;
            var duplicateTarget = Target(first);
            duplicateTarget.Id = "synthetic-duplicate";
            loaded.Outputs.Targets.Add(duplicateTarget);
            Throws<InvalidDataException>(() => loaded.Save(path));
        }
        finally { Directory.Delete(directory, true); }
    }

    internal static OutputDeviceDescriptor Device(string token) => DeviceDiscovery.CreateTrofeoDescriptors([
        $@"\\?\usb#vid_0416&pid_5408#{token}#{{DEE824EF-729B-4A0E-9C14-B7117D33A817}}"])[0];

    private static OutputTargetConfig Target(OutputDeviceDescriptor device) => new()
    { Transport = OutputTargetContract.LyBulkTransport, Width = 1920, Height = 480, Id = "synthetic-output", DeviceId = device.DeviceId, Dashboard = "RetainedBoard" };
    private static string Id(string product, string token) => $@"USB\VID_{product[..4]}&PID_{product[5..]}\{token}";
    private static UsbPnpNode Node(string id, string? parent = null) => new(id, parent, "WINUSB", Guid.Empty, 0, true);
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
    private static void Throws<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T) { return; }
        throw new InvalidOperationException($"Expected {typeof(T).Name}.");
    }
}
