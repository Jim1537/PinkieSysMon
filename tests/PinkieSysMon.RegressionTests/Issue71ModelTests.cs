using System.Text;
using System.Text.Json;
using PinkieSysMon.Editor;

namespace PinkieSysMon.RegressionTests;

// Production configuration, discovery and draft code; synthetic identities only.
internal static class Issue71ModelTests
{
    private static readonly Guid ClassA = new("11111111-2222-3333-4444-555555555555");
    private static readonly Guid ClassB = new("AAAAAAAA-BBBB-CCCC-DDDD-EEEEEEEEEEEE");

    public static void CompatibleConfiguration()
    {
        Sandbox((path, log) =>
        {
            var config = new AppConfig();
            config.Outputs.Targets.Add(new OutputTargetConfig
            { Transport = OutputTargetContract.LyBulkTransport, Width = 1920, Height = 480, Id = "old-physical", DeviceId = "usb-interface:" + new string('a', 64), Dashboard = "OldBoard", DeviceEnabled = true });
            config.Save(path);
            var oldBytes = File.ReadAllBytes(path);
            var loaded = AppConfig.Load(path, log);
            loaded.Save(path);
            Check(File.ReadAllBytes(path).SequenceEqual(oldBytes) && !File.ReadAllText(path).Contains("Turzx"),
                "The additive model must not introduce TURZX fields or change stable old saves.");
            loaded.Outputs.Targets.Add(Target(Device("A")));
            loaded.Outputs.Targets[^1].EncryptedJpeg = new() { BrightnessPercent = 73 };
            loaded.Save(path);
            var saved = File.ReadAllBytes(path);
            var roundTrip = AppConfig.Load(path, log);
            roundTrip.Save(path);
            Check(File.ReadAllBytes(path).SequenceEqual(saved) &&
                roundTrip.Outputs.Targets[1].DeviceId == "usb-interface:" + new string('a', 64) &&
                roundTrip.Outputs.Targets[1].DeviceEnabled && roundTrip.Outputs.Targets[1].Dashboard == "OldBoard" &&
                roundTrip.Outputs.Targets[2] is { Kind: "physical", Transport: "encrypted-jpeg", DeviceEnabled: false,
                    Dashboard: "TurzxBoard", EncryptedJpeg.BrightnessPercent: 73 },
                "Mixed configurations must preserve old ownership and the explicit disabled TURZX settings.");
            var copy = JsonSerializer.Deserialize<OutputTargetConfig>(JsonSerializer.Serialize(roundTrip.Outputs.Targets[2]))!;
            copy.EncryptedJpeg!.BrightnessPercent = 40;
            Check(roundTrip.Outputs.Targets[2].EncryptedJpeg!.BrightnessPercent == 73,
                "Existing JSON-based transactional clones must own independent TURZX options.");
        });
    }

    public static void ConfigurationRejectsUnsafeOwnership()
    {
        Sandbox((path, log) =>
        {
            var config = new AppConfig();
            config.Outputs.Targets.Add(Target(Device("A")));
            config.Save(path);
            var saved = File.ReadAllBytes(path);
            foreach (var mutate in new Action<AppConfig>[]
            {
                c => { c.Outputs.Targets[1].DeviceEnabled = true; c.Outputs.Targets[1].EncryptedJpeg!.BrightnessPercent = 10; },
                c => c.Outputs.Targets[1].DeviceId = "",
                c => c.Outputs.Targets[1].DeviceId = "invalid-binding",
                c => c.Outputs.Targets[1].Transport = "unimplemented",
                c => c.Outputs.Targets[1].Desktop = new(),
                c => c.Outputs.Targets[1].EncryptedJpeg!.BrightnessPercent = -1,
                c => c.Outputs.Targets[1].EncryptedJpeg!.BrightnessPercent = 101,
                c => c.Outputs.Targets[0].EncryptedJpeg = new(),
                c => c.Outputs.Targets.Add(new OutputTargetConfig { Transport = OutputTargetContract.LyBulkTransport, Width = 1920, Height = 480, DeviceId = "usb-interface:c8deaccbc343708b612facdb9317fdb3a103e99647a7deb0c0b70be04fe302f2", Id = "wrong", EncryptedJpeg = new() }),
                c => { var t = Target(Device("A")); t.Id = "duplicate"; c.Outputs.Targets.Add(t); }
            })
            {
                var candidate = AppConfig.Load(path, log);
                mutate(candidate);
                Throws<InvalidDataException>(() => candidate.Save(path));
                Check(File.ReadAllBytes(path).SequenceEqual(saved),
                    "Invalid profiles, ownership and incomplete transports must fail before replacing the saved file.");
            }
        });
    }

    public static void BrightnessPolicy()
    {
        Check(new EncryptedJpegOutputConfig().BrightnessPercent is null,
            "The default means no brightness command, not a fabricated factory percentage.");
        var previous = -1;
        for (var percent = 0; percent <= 100; percent++)
        {
            var controller = EncryptedJpegOutputConfig.ToControllerBrightness(percent);
            Check(controller >= previous && controller <= 102 && controller == percent * 102 / 100,
                "Brightness conversion must be deterministic, bounded and monotonic across the entire slider.");
            previous = controller;
        }
        Check(EncryptedJpegOutputConfig.ToControllerBrightness(0) == 0 &&
            EncryptedJpegOutputConfig.ToControllerBrightness(100) == 102, "Both slider endpoints must map exactly.");
        Throws<ArgumentOutOfRangeException>(() => EncryptedJpegOutputConfig.ToControllerBrightness(-1));
        Throws<ArgumentOutOfRangeException>(() => EncryptedJpegOutputConfig.ToControllerBrightness(101));
    }

    public static void ExactPassiveInterfaces()
    {
        var api = new FakeApi(Node("A"), Node("B"), Node("UNRELATED", "0416", "5408"));
        var found = TurzxDeviceDiscovery.Capture(api);
        Check(found.Count == 2 && found.Select(d => d.DeviceId).Distinct().Count() == 2 &&
            found.All(d => d.Transport == "encrypted-jpeg" && d.DeviceId.StartsWith("usb-interface:") &&
                d.DeviceId.Length == 78 && d.PhysicalFingerprint is null && d.PnpNodeKey is not null) &&
            api.ReadIds.All(id => id.Contains("VID_1CBE&PID_0123")),
            "Only exact reviewed products incur metadata reads; distinct units retain distinct local bindings.");
        Check(TurzxDeviceDiscovery.CreateStableDeviceId("  " + PathFor("A").ToUpperInvariant() + "  ") ==
            TurzxDeviceDiscovery.CreateStableDeviceId(PathFor("A")) &&
            found.Any(d => d.DeviceId == TurzxDeviceDiscovery.CreateStableDeviceId(PathFor("A"))),
            "Interface case and trimming must not change a TURZX binding.");
        Check(!found.Any(d => d.DeviceId.Contains("SYNTHETIC")) &&
            UsbDisplayCatalog.Find(TurzxDeviceDiscovery.Product) is { CandidateTransport: OutputTargetContract.EncryptedJpegTransport, IntegrationIssue: 71 },
            "Catalog eligibility must retain the reviewed TURZX adapter without publishing instance tokens.");
        api.Paths = id => [PathFor(Token(id)), PathFor(Token(id)).ToUpperInvariant(), PathFor(Token(id), ClassB),
            PathFor(Token(id)).Replace("pid_0123", "pid_01230"), PathFor(Token(id)).Replace("pid_0123", "pid_0088")];
        Check(TurzxDeviceDiscovery.Capture(api).Count == 2,
            "Case aliases deduplicate; wrong registered class and product substrings cannot enter the adapter.");
        api.Correlation = _ => Node("OTHER").InstanceId;
        Check(TurzxDeviceDiscovery.Capture(api).Count == 0,
            "A valid-looking path without exact devnode correlation is not a usable descriptor.");
    }

    public static void MissingAndRemovedMetadata()
    {
        foreach (var node in new[]
        {
            new UsbPnpNode(Node("A").InstanceId, null, "HIDUSB", ClassA, 0, true),
            new UsbPnpNode(Node("A").InstanceId, null, "WINUSB", ClassA, 10, true),
            new UsbPnpNode(Node("A").InstanceId, null, "WINUSB", ClassA, null, false),
            new UsbPnpNode(@"USB\VID_1CBE&PID_0123&MI_00\SYNTHETIC-A", null, "WINUSB", ClassA, 0, true)
        })
        {
            var api = new FakeApi(node);
            Check(TurzxDeviceDiscovery.Capture(api).Count == 0 && api.ClassReads == 0,
                "Missing/invalid driver, problem state and unevidenced composite functions stay ineligible.");
        }
        var missing = new FakeApi(Node("A")) { Classes = [] };
        Check(TurzxDeviceDiscovery.Capture(missing).Count == 0, "A missing registration must not borrow Trofeo's GUID.");
        var removed = new FakeApi(Node("A")) { RemoveAfterFirstRead = true };
        Check(TurzxDeviceDiscovery.Capture(removed).Count == 0, "A removed node must not survive final presence revalidation.");
        var invalid = new FakeApi(Node("A")) { Classes = [Guid.Empty] };
        Throws<InvalidOperationException>(() => TurzxDeviceDiscovery.Capture(invalid));
        invalid.Classes = Enumerable.Repeat(ClassA, 17).ToArray();
        Throws<InvalidOperationException>(() => TurzxDeviceDiscovery.Capture(invalid));
        var failed = new FakeApi(Node("A")) { FailList = true };
        Throws<InvalidOperationException>(() => TurzxDeviceDiscovery.Capture(failed));
    }

    public static void RecoveryMetadataCannotInventRemoval()
    {
        Check(TurzxDeviceDiscovery.Capture(new FakeApi(), requireCompleteMetadata: true).Count == 0,
            "A successful empty present-node list may establish absence.");
        Check(TurzxDeviceDiscovery.Capture(new FakeApi(Node("A")), requireCompleteMetadata: true).Count == 1,
            "Complete correlated metadata must remain eligible during recovery.");
        foreach (var api in new[]
        {
            new FakeApi(new UsbPnpNode(Node("A").InstanceId, null, "WINUSB", ClassA, null, false)),
            new FakeApi(new UsbPnpNode(Node("A").InstanceId, null, "WINUSB", ClassA, 10, true)),
            new FakeApi(Node("A")) { Classes = [] },
            new FakeApi(Node("A")) { Paths = _ => [] },
            new FakeApi(Node("A")) { Correlation = _ => null },
            new FakeApi(Node("A")) { FailList = true },
            new FakeApi(Node("A")) { RemoveAfterFirstRead = true }
        })
            Throws<InvalidOperationException>(() => TurzxDeviceDiscovery.Capture(api, requireCompleteMetadata: true));
    }

    public static void BindingAndInventory()
    {
        var devices = TurzxDeviceDiscovery.Capture(new FakeApi(Node("A"), Node("B")));
        var selected = devices[0];
        var target = Target(selected);
        Check(OutputDeviceSelection.Resolve(target, devices.Reverse().ToArray()).Device == selected,
            "Selection must use the exact TURZX binding independently of enumeration order.");
        var upper = Target(selected);
        upper.Kind = "PHYSICAL";
        upper.Transport = "ENCRYPTED-JPEG";
        var upperDevices = devices.Select(d => d with { Transport = "ENCRYPTED-JPEG" }).ToArray();
        Check(OutputDeviceSelection.Resolve(upper, upperDevices).Status == OutputDeviceResolutionStatus.Found &&
            PhysicalDeviceInventory.Build([upper], upperDevices).Single(e => e.IsPersisted).Status == PhysicalDeviceInventoryStatus.Connected,
            "Shared selection and inventory retain their case-insensitive transport contract.");
        target.DeviceId = "";
        Check(OutputDeviceSelection.Resolve(target, [selected]).Status == OutputDeviceResolutionStatus.Missing,
            "A new TURZX target must not use the legacy unbound single-device fallback.");
        target.DeviceId = selected.DeviceId;
        var moved = selected with { DeviceId = "usb-interface:" + new string('c', 64), DevicePath = PathFor("MOVED") };
        Check(OutputDeviceSelection.Resolve(target, [moved]).Status == OutputDeviceResolutionStatus.Missing,
            "A matching PnP node must not migrate the saved interface binding.");
        var duplicate = devices[1] with { PnpNodeKey = selected.PnpNodeKey };
        Check(OutputDeviceSelection.Resolve(target, [selected, duplicate]).Status == OutputDeviceResolutionStatus.Ambiguous,
            "Duplicate physical-node claims must fail closed even with one exact interface ID.");
        var entries = PhysicalDeviceInventory.Build([target], devices);
        Check(entries.Count == 2 && entries.Single(e => e.IsPersisted).Status == PhysicalDeviceInventoryStatus.Connected &&
            entries.Single(e => e.IsPersisted).CanEditSettings &&
            PhysicalDeviceInventory.CanOfferEnable(entries.Single(e => e.IsPersisted)) && !target.DeviceEnabled,
            "An exact passive TURZX binding offers settings/consent without enabling hardware.");
        var retained = PhysicalDeviceInventory.Build([target], []).Single();
        Check(retained.Status == PhysicalDeviceInventoryStatus.Disconnected && target.Dashboard == "TurzxBoard" && !target.DeviceEnabled,
            "Missing hardware retains its assignment and saved disabled state.");
        target.Kind = "desktop";
        Check(OutputDeviceSelection.Resolve(target, devices).Status == OutputDeviceResolutionStatus.UnsupportedTransport,
            "A TURZX transport cannot masquerade as a Trofeo model.");
        var sameNode = new FakeApi(Node("A")) { Classes = [ClassA, ClassB] };
        sameNode.PathsByClass = (id, cls) => [PathFor(Token(id), cls)];
        var multipleInterfaces = TurzxDeviceDiscovery.Capture(sameNode);
        Check(multipleInterfaces.Count == 2 && OutputDeviceSelection.Resolve(Target(multipleInterfaces[0]), multipleInterfaces)
            .Status == OutputDeviceResolutionStatus.Ambiguous,
            "Two registered interfaces on one unit must remain ambiguous rather than selecting the first.");
    }

    public static void DetachedDraftAndAdapterOptions()
    {
        var device = Device("A");
        var target = Target(device);
        target.EncryptedJpeg = new() { BrightnessPercent = 61 };
        var old = Issue77DeviceIdentityTests.Device("SYNTHETIC-OLD");
        var oldTarget = new OutputTargetConfig { Transport = OutputTargetContract.LyBulkTransport, Width = 1920, Height = 480, Id = "old", DeviceId = old.DeviceId };
        var config = new AppConfig();
        config.Outputs.Targets.AddRange([oldTarget, target]);
        var model = new DeviceSettingsModel(config);
        model.Refresh([old, device]);
        var draft = model.Entries.Single(e => e.Target.Id == target.Id);
        Check(draft.CanEdit && !model.CanEnable(draft.Target) && !model.IsDirty &&
            !ReferenceEquals(draft.Target.EncryptedJpeg, target.EncryptedJpeg) && draft.Target.EncryptedJpeg!.BrightnessPercent == 61,
            "A saved unsupported option remains independent and cannot be silently enabled.");
        Check(!model.SetEnabled(draft.Target, true, true), "Unsupported brightness cannot be enabled.");
        var existing = model.Entries.Single(e => e.Target.Id == "old");
        existing.Target.Name = "Renamed old device";
        model.MarkChanged(existing.Target);
        model.ApplyTo(config);
        Check(config.Outputs.Targets.Single(t => t.Id == target.Id).EncryptedJpeg!.BrightnessPercent == 61 &&
            config.Outputs.Targets.Single(t => t.Id == "old").Name == "Renamed old device" && !target.DeviceEnabled,
            "Saving a sibling's editable settings must preserve the pending model's complete options.");
        var candidate = new DeviceSettingsModel(new AppConfig());
        candidate.Refresh([device]);
        Check(!candidate.IsDirty && candidate.ConfiguredCount == 0 && candidate.Entries.Single().CanEdit,
            "Passive discovery offers an editable candidate without automatically adopting it.");
        Throws<InvalidOperationException>(() => candidate.Add(candidate.Entries.Single().Target));
        Check(!candidate.IsDirty && candidate.ConfiguredCount == 0,
            "An old descriptor with no confirmed Runtime Test cannot bypass new onboarding.");
    }

    public static void RegisteredClassDecoding()
    {
        static IReadOnlyList<Guid> Decode(uint type, string value)
        {
            var bytes = Encoding.Unicode.GetBytes(value);
            return WindowsTurzxInterfaceDiscoveryApi.DecodeRegisteredClasses(type, bytes, (uint)bytes.Length);
        }
        Check(Decode(1, ClassA.ToString("B") + '\0').SequenceEqual([ClassA]) &&
            Decode(7, ClassA.ToString("B") + '\0' + ClassB.ToString("B") + "\0\0").SequenceEqual([ClassA, ClassB]) &&
            Decode(7, "\0\0").Count == 0,
            "Both documented registry forms must decode, with no fabricated fallback class.");
        foreach (var value in new[] { "bad\0", ClassA + "\0HIDDEN\0", "\0HIDDEN\0", Guid.Empty + "\0" })
            Throws<InvalidOperationException>(() => Decode(1, value));
        Throws<InvalidOperationException>(() => Decode(7, ClassA + "\0\0HIDDEN\0\0"));
        var valid = Encoding.Unicode.GetBytes(ClassA + "\0");
        foreach (var length in new uint[] { 1, (uint)valid.Length - 1, (uint)valid.Length + 2, 4098 })
            Throws<InvalidOperationException>(() => WindowsTurzxInterfaceDiscoveryApi.DecodeRegisteredClasses(1, valid, length));
        Throws<InvalidOperationException>(() => WindowsTurzxInterfaceDiscoveryApi.DecodeRegisteredClasses(4, valid, (uint)valid.Length));
    }

    private static OutputDeviceDescriptor Device(string token) => TurzxDeviceDiscovery.Capture(new FakeApi(Node(token))).Single();
    private static OutputTargetConfig Target(OutputDeviceDescriptor device) => new()
    { Width = 1920, Height = 720, Id = "turzx-output", Name = "TURZX", Kind = "physical", Transport = "encrypted-jpeg", DeviceId = device.DeviceId, Dashboard = "TurzxBoard" };
    private static UsbPnpNode Node(string token, string vid = "1CBE", string pid = "0123") =>
        new($@"USB\VID_{vid}&PID_{pid}\SYNTHETIC-{token}", null, "WINUSB", ClassA, 0, true);
    private static string Token(string instanceId) => instanceId[(instanceId.LastIndexOf('-') + 1)..];
    private static string PathFor(string token, Guid? cls = null) => $@"\\?\usb#vid_1cbe&pid_0123#SYNTHETIC-{token}#{{{cls ?? ClassA}}}";
    private static void Check(bool condition, string reason) { if (!condition) throw new InvalidOperationException(reason); }
    private static void Throws<T>(Action action) where T : Exception
    { try { action(); } catch (T) { return; } throw new InvalidOperationException($"Expected {typeof(T).Name}."); }
    private static void Sandbox(Action<string, FileLogger> action)
    {
        var root = Path.Combine(Path.GetTempPath(), "pinkie-issue71-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try { action(Path.Combine(root, "app.json"), new FileLogger(Path.Combine(root, "test.log"))); }
        finally { Directory.Delete(root, true); }
    }

    private sealed class FakeApi(params UsbPnpNode[] nodes) : ITurzxInterfaceDiscoveryApi
    {
        public List<string> ReadIds { get; } = [];
        public int ClassReads { get; private set; }
        public bool RemoveAfterFirstRead { get; init; }
        public bool FailList { get; init; }
        public IReadOnlyList<Guid> Classes { get; set; } = [ClassA];
        public Func<string, IReadOnlyList<string>> Paths { get; set; } = id => [PathFor(Token(id))];
        public Func<string, Guid, IReadOnlyList<string>>? PathsByClass { get; set; }
        public Func<string, string?>? Correlation { get; set; }
        private string List => string.Join('\0', nodes.Select(n => n.InstanceId)) + "\0\0";
        public uint GetListSize(out uint characters) { characters = (uint)List.Length; return 0; }
        public uint GetList(char[] buffer) { if (FailList) return 1; List.CopyTo(0, buffer, 0, List.Length); return 0; }
        public UsbPnpNode? ReadNode(string instanceId)
        {
            ReadIds.Add(instanceId);
            return RemoveAfterFirstRead && ReadIds.Count > 1 ? null : nodes.Single(n => n.InstanceId == instanceId);
        }
        public IReadOnlyList<Guid> ReadInterfaceClasses(string instanceId) { ClassReads++; return Classes; }
        public IReadOnlyList<string> EnumerateInterfaces(Guid cls, string instanceId) =>
            PathsByClass?.Invoke(instanceId, cls) ?? Paths(instanceId);
        public string? ReadInterfaceInstanceId(string path) => Correlation is not null ? Correlation(path) :
            nodes.Single(n => Token(n.InstanceId).Equals(path.Split('#')[2]["SYNTHETIC-".Length..], StringComparison.OrdinalIgnoreCase)).InstanceId;
    }
}
