using System.Text.Json;
using PinkieSysMon.Editor;

namespace PinkieSysMon.RegressionTests;

internal static class Issue95GenericOutputTests
{
    internal static OutputDeviceDescriptor Device(string protocol, char token)
    {
        var digest = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(token.ToString()))).ToLowerInvariant();
        var id = "usb-interface:" + digest;
        var node = "usb-pnp:" + digest;
        // Listed but unhinted product: neither its name nor its catalog hint
        // chooses the user's implemented protocol.
        var candidate = new CatalogUsbInterfaceCandidate(id,
            UsbDisplayCatalog.Find(new UsbProductId(0x0416, 0x5409))!,
            "synthetic-private-interface", node, "Unknown commercial name");
        return new(id, protocol, candidate.DisplayName, candidate.InterfacePath)
        { PnpNodeKey = node, Candidate = candidate };
    }

    // Synthetic completion exclusively in tests. Production cannot manufacture
    // a Runtime technical result merely from the user's visual answer.
    internal static void Confirm(DeviceSettingsModel model, OutputTargetConfig target, OutputDeviceDescriptor device)
    {
        var draft = model.CandidateDraft(target) ?? throw new InvalidOperationException("Missing candidate draft.");
        target.Width = 800; target.Height = 600;
        model.MarkChanged(target);
        draft.Protocol = target.Transport;
        var attempt = draft.BeginTest(new([device.Candidate!], []), new HashSet<string>());
        Check(draft.MarkDispatchAttempted(attempt), "Synthetic request must be marked dispatched.");
        Check(draft.RecordTechnicalOutcome(attempt, CatalogUsbTestTechnicalOutcome.SucceededAndReleased), "Synthetic release must correlate.");
        Check(draft.ConfirmVisibleChangingSeconds(attempt, true), "Visual Yes must correlate to the clean attempt.");
    }

    public static void NewFormatRoundTripAndOwnership()
    {
        var root = Path.Combine(Path.GetTempPath(), "pinkie-generic-output-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var config = new AppConfig();
            var desktop = JsonSerializer.Serialize(config.Outputs.Targets[0]);
            var devices = new[] { Device(OutputTargetContract.LyBulkTransport, 'a'), Device(OutputTargetContract.EncryptedJpegTransport, 'b') };
            var model = new DeviceSettingsModel(config); model.Refresh(devices);
            foreach (var entry in model.Entries.ToArray())
            {
                var device = devices.Single(d => d.DeviceId == entry.Target.DeviceId);
                Confirm(model, entry.Target, device); model.Add(entry.Target);
            }
            model.ApplyTo(config);
            var file = Path.Combine(root, "app.json");
            config.Save(file);
            var bytes = File.ReadAllBytes(file);
            var loaded = AppConfig.Load(file, new FileLogger(Path.Combine(root, "test.log")));
            loaded.Save(file);
            Check(bytes.SequenceEqual(File.ReadAllBytes(file)), "New-format parse/save must be idempotent.");
            Check(loaded.Outputs.Targets.Skip(1).All(t => t.Kind == "physical" && t.Width == 800 && t.Height == 600 &&
                t.Dashboard == "device_800x600_default" && !t.DeviceEnabled && !t.ProviderEnabled),
                "Explicit Save preserves arbitrary user geometry and starts both protocols disabled.");
            Check(config.Outputs.Targets[0].Kind == "desktop" &&
                JsonSerializer.Serialize(new AppConfig().Outputs.Targets[0]) == desktop,
                "Candidate operations do not alter the Desktop defaults.");
            Check(!File.ReadAllText(file).Contains("synthetic-private-interface") &&
                  !File.ReadAllText(file).Contains("PnpNodeKey"), "Native metadata stays out of persisted Outputs.");
            var copy = JsonSerializer.Deserialize<OutputTargetConfig>(JsonSerializer.Serialize(loaded.Outputs.Targets[1]))!;
            copy.Transport = OutputTargetContract.EncryptedJpegTransport;
            copy.Id = "duplicate-independent-of-protocol";
            loaded.Outputs.Targets.Add(copy);
            Invalid(() => loaded.Save(file));
            Check(bytes.SequenceEqual(File.ReadAllBytes(file)), "Duplicate binding fails before replacing configuration.");
        }
        finally { Directory.Delete(root, true); }
    }

    public static void RequiredGeometryProtocolAndBinding()
    {
        var device = Device(OutputTargetContract.LyBulkTransport, 'a');
        OutputTargetConfig Valid() => new()
        {
            Id = "custom", Name = "Uncatalogued commercial name", Kind = "physical",
            Transport = OutputTargetContract.LyBulkTransport, DeviceId = device.DeviceId, Width = 800, Height = 600
        };
        foreach (var mutate in new Action<OutputTargetConfig>[]
        {
            t => t.Transport = "", t => t.Transport = "unimplemented", t => t.DeviceId = "",
            t => t.DeviceId = "not-a-binding", t => t.Width = 0, t => t.Height = -1,
            t => t.Width = int.MaxValue, t => t.Name = "\n"
        })
        {
            var target = Valid(); mutate(target);
            Invalid(() => OutputTargetContract.ValidateAndNormalize([target]));
        }
        var one = Valid(); var two = Valid(); two.Height = 601;
        Check(!OutputTargetContract.ConnectionEquals(one, two), "Geometry changes invalidate connection equality and stale reloads.");
        one.Transport = OutputTargetContract.EncryptedJpegTransport;
        Check(OutputDeviceSelection.Resolve(one, [device]).Status == OutputDeviceResolutionStatus.Found,
            "An exact binding can use a selected implemented protocol independent of its passive hint.");
        Check(OutputDeviceSelection.Resolve(one, [device, device with { Transport = "other-hint" }]).Status == OutputDeviceResolutionStatus.Ambiguous,
            "Different protocol hints cannot hide duplicate ownership of the same interface.");
        one.DeviceId = "";
        Check(OutputDeviceSelection.Resolve(one, [device]).Status == OutputDeviceResolutionStatus.Missing,
            "A singleton cannot fill a missing binding.");
    }

    public static void CandidateCannotBypassConfirmation()
    {
        var device = Device("", 'c');
        var config = new AppConfig(); var model = new DeviceSettingsModel(config); model.Refresh([device]);
        var entry = model.Entries.Single(); var target = entry.Target;
        Check(entry.CanEdit && target.Transport == "" && target.Width == 0 && target.Height == 0,
            "An unhinted catalog candidate remains editable with no inferred protocol or dimensions.");
        Throws(() => model.Add(target));
        Check(!model.SetEnabled(target, true, true) && !model.IsDirty,
            "Even explicit Enabled consent cannot persist an untested candidate.");
        target.Transport = OutputTargetContract.EncryptedJpegTransport;
        Confirm(model, target, device);
        var draft = model.CandidateDraft(target)!;
        draft.Width++; draft.Width--;
        Throws(() => model.Add(target));
        Check(config.Outputs.Targets.Count == 1, "Changed-away-and-back geometry cannot restore old approval.");
        Confirm(model, target, device);
        model.Refresh([]);
        Throws(() => model.Add(target));
        model.Refresh([device]); model.Add(target); model.ApplyTo(config);
        Check(config.Outputs.Targets.Count == 2 && !target.DeviceEnabled,
            "A confirmed current exact binding creates only a disabled Output.");
        var renamed = new DeviceSettingsModel(config); renamed.Refresh([device]);
        var saved = renamed.Entries.Single().Target;
        Check(saved.Width == 800 && saved.Height == 600, "Editor detached copies preserve both dimensions.");
        saved.Name = "User label"; renamed.MarkChanged(saved); renamed.ApplyTo(config);
        Check(config.Outputs.Targets[1].Name == "User label" && config.Outputs.Targets[1].Width == 800,
            "Name-only editing preserves geometry without another Test.");
        saved.Width++;
        renamed.MarkChanged(saved);
        Throws(() => renamed.ApplyTo(config));
        Check(config.Outputs.Targets[1].Width == 800, "Configured geometry cannot bypass Remove/Test/Save.");
    }

    public static void LyEndpointAdmission()
    {
        TurzxNativePipe Out() => new() { Id = 0x09, Type = 2, MaximumPacketSize = 512 };
        TurzxNativePipe In() => new() { Id = 0x81, Type = 2, MaximumPacketSize = 512 };
        TrofeoTransport.ValidateBulkPipes([Out(), In()]);
        foreach (var pipes in new TurzxNativePipe[][]
        {
            [Out()], [Out(), In(), In()],
            [Out(), new() { Id = 0x81, Type = 3, MaximumPacketSize = 512 }],
            [new() { Id = 0x09, Type = 2, MaximumPacketSize = 0 }, In()]
        })
        {
            try { TrofeoTransport.ValidateBulkPipes(pipes); }
            catch (IOException) { continue; }
            throw new InvalidOperationException("Missing, duplicate, non-bulk or zero-packet endpoints must reject admission.");
        }
    }

    private static void Invalid(Action action) { try { action(); } catch (InvalidDataException) { return; } throw new InvalidOperationException("Expected invalid generic configuration."); }
    private static void Throws(Action action) { try { action(); } catch (InvalidOperationException) { return; } throw new InvalidOperationException("Expected rejected candidate operation."); }
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
