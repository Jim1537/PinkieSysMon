using System.Windows.Forms;
using PinkieSysMon.Editor;

namespace PinkieSysMon.RegressionTests;

internal static class DeviceSettingsStage23Tests
{
    public static void DiscoveryAndAdoption()
    {
        var config = new AppConfig();
        var desktop = config.Outputs.Targets.Single();
        var model = new DeviceSettingsModel(config);
        model.Refresh([]);
        Check(model.Entries.Count == 0 && !model.IsDirty, "No never-detected physical tab or config mutation is allowed.");
        var device = Device("first");
        model.Refresh([device]);
        var entry = model.Entries.Single();
        var target = entry.Target;
        Check(entry.CanEdit && !entry.IsConfigured && !target.DeviceEnabled && !target.ProviderEnabled &&
              !model.IsDirty && config.Outputs.Targets.Count == 1,
            "Discovery must offer an editable, disabled unsaved candidate without taking hardware ownership.");
        model.Refresh([]);
        Check(model.Entries.Single().Status == PhysicalDeviceInventoryStatus.Disconnected &&
              ReferenceEquals(model.Entries.Single().Target, target),
            "A detected draft must retain its identity/settings through absence within the open dialog.");
        model.Refresh([device]);
        Issue95GenericOutputTests.Confirm(model, target, device);
        model.Add(target);
        model.ValidateForApply([device]);
        model.ApplyTo(config);
        Check(config.Outputs.Targets.Count == 2 && ReferenceEquals(config.Outputs.Targets[0], desktop) &&
              config.Outputs.Targets[1].Id == target.Id && !config.Outputs.Targets[1].DeviceEnabled,
            "Explicit adoption adds only a disabled physical Output, retaining the Desktop and saved dashboard assignment.");
        model.Remove(target, 1);
        model.Refresh([device]);
        Check(model.Entries.Count == 0, "A removed attached device must not recreate its tab/record on the next notification.");
        model.ApplyTo(config);
        var reopened = new DeviceSettingsModel(config);
        reopened.Refresh([device]);
        reopened.ApplyTo(config);
        Check(!reopened.Entries.Single().IsConfigured && !reopened.IsDirty && config.Outputs.Targets.Count == 1,
            "Reopening may show the removed hardware as an unsaved candidate; discovery alone must never restore its record.");

        var solePhysical = new AppConfig();
        solePhysical.Outputs.Targets = [Target(device, "only")];
        var soleModel = new DeviceSettingsModel(solePhysical);
        soleModel.Refresh([]);
        Check(!soleModel.CanRemove(soleModel.Entries.Single().Target, 0), "The last Output cannot be removed.");
        Throws(() => soleModel.Remove(soleModel.Entries.Single().Target, 0));
        var desktopModel = new DesktopSettingsModel(config);
        Check(!desktopModel.CanRemove(desktopModel.Desktops.Single()),
            "A lone Desktop must remain protected before device adoption.");
        Issue95GenericOutputTests.Confirm(reopened, reopened.Entries.Single().Target, device);
        reopened.Add(reopened.Entries.Single().Target);
        reopened.ApplyTo(config);
        Check(config.Outputs.Targets.Count == 2 && !config.Outputs.Targets[1].DeviceEnabled,
            "Physical Devices OK must adopt only its device without mutating the existing Desktop.");
        var nextDesktopDialog = new DesktopSettingsModel(config);
        Check(nextDesktopDialog.CanRemove(nextDesktopDialog.Desktops.Single()),
            "After a separately saved device adoption, the Desktop dialog can remove its final panel.");
        nextDesktopDialog.Remove(nextDesktopDialog.Desktops.Single());
        nextDesktopDialog.ApplyTo(config);
        Check(config.Outputs.Targets.Count == 1 && config.Outputs.Targets[0].Kind == OutputTargetContract.PhysicalKind,
            "Separate device and Desktop apply operations must retain one configured Output.");
    }

    public static void ConsentAndPersistence()
    {
        var first = Device("first");
        var second = Device("second");
        var config = new AppConfig();
        var original = Target(first, "first");
        original.ProviderEnabled = true;
        var sibling = Target(second, "second");
        config.Outputs.Targets.AddRange([original, sibling]);
        var model = new DeviceSettingsModel(config);
        model.Refresh([first, second]);
        var draft = model.Entries.Single(e => e.Target.Id == original.Id).Target;
        Check(!model.SetEnabled(draft, true, false) && !draft.DeviceEnabled && draft.ProviderEnabled && !model.IsDirty,
            "No consent must leave device enablement and its independent provider unchanged.");
        Check(model.SetEnabled(draft, true, true) && !original.DeviceEnabled,
            "Consent enables only the detached draft, never live config before OK.");
        draft.RefreshIntervalMs = 333;
        draft.JpegQuality = 80;
        draft.Usb!.TransferTimeoutMs = 5000;
        model.MarkChanged(draft);
        Throws(() => model.ValidateForApply([]));
        Check(!original.DeviceEnabled && original.RefreshIntervalMs == 1000,
            "Loss of identity before save must reject new enablement without touching the original config.");
        model.ValidateForApply([first, second]);
        model.ApplyTo(config);
        var applied = config.Outputs.Targets.Single(t => t.Id == original.Id);
        var other = config.Outputs.Targets.Single(t => t.Id == sibling.Id);
        Check(applied.DeviceEnabled && applied.ProviderEnabled && applied.RefreshIntervalMs == 333 &&
              applied.JpegQuality == 80 && applied.Usb!.TransferTimeoutMs == 5000 &&
              !other.DeviceEnabled && other.RefreshIntervalMs == 1000 && other.JpegQuality == 92 &&
              config.Usb.TransferTimeoutMs == 3000,
            "Apply must preserve independent provider/device switches, sibling settings and global defaults.");
        InSandbox(root =>
        {
            var path = Path.Combine(root, "app.json");
            config.Save(path);
            var loaded = AppConfig.Load(path, new FileLogger(Path.Combine(root, "test.log")));
            Check(loaded.Outputs.Targets.Single(t => t.Id == applied.Id).DeviceEnabled &&
                  loaded.Outputs.Targets.Single(t => t.Id == applied.Id).Usb!.TransferTimeoutMs == 5000 &&
                  loaded.Outputs.Targets.Single(t => t.Id == sibling.Id).DeviceId == second.DeviceId,
                "Save/reload must retain exact output identities, consented state and per-device settings.");
        });
        model.Refresh([first, first with { DevicePath = "duplicate-interface" }, second]);
        Check(!model.CanEnable(draft), "Duplicated explicit identities cannot be enabled.");
        Throws(() => model.ValidateForApply([first, first with { DevicePath = "duplicate-interface" }, second]));
    }

    public static void LiveStatusControls()
    {
        var device = Device("ui");
        IReadOnlyList<OutputDeviceDescriptor> discovery = [device];
        var fail = false;
        var calls = 0;
        var config = new AppConfig();
        var physical = Target(device, "ui");
        config.Outputs.Targets.Add(physical);
        using var dialog = new PhysicalDeviceSettingsDialog(
            config, ["Default"], () =>
            {
                calls++;
                if (fail) throw new InvalidOperationException("Synthetic passive-discovery failure.");
                return discovery;
            });
        var tabs = dialog.Controls.OfType<TabControl>().Single();
        Check(dialog.Padding == new Padding(12),
            "Physical Devices Settings must inset the dynamic device tabs from the window edges.");
        Check(dialog.AcceptButton is Button { DialogResult: DialogResult.OK } &&
              dialog.CancelButton is Button { DialogResult: DialogResult.Cancel },
            "Physical Devices Settings must own OK/Cancel independently.");
        var page = tabs.TabPages.Cast<TabPage>().Single(p => p.Text == "Device #1");
        var fields = page.Controls.Find("DeviceFields", true).Single();
        var remove = page.Controls.Find("RemoveDevice", true).Single();
        Check(fields.Enabled && remove.Enabled && !dialog.DevicesChanged && calls == 1,
            "A configured connected device must have one editable tab after one passive capture.");
        var provider = (CheckBox)page.Controls.Find("DeviceProvider", true).Single();
        provider.Checked = true;
        Check(dialog.DevicesChanged && !physical.ProviderEnabled && !physical.DeviceEnabled,
            "Provider UI edits must be staged independently without altering the original hardware state.");
        discovery = [];
        dialog.RefreshDeviceDiscovery();
        Check(!fields.Enabled && remove.Enabled &&
              fields.Controls.Cast<Control>().All(c => !c.Enabled) && provider.Checked &&
              tabs.TabPages.Contains(page),
            "Disconnect must retain the same tab/draft and disable every settings field while leaving Remove usable.");
        discovery = [device];
        dialog.RefreshDeviceDiscovery();
        Check(fields.Enabled && provider.Checked, "Reconnect must restore controls without resetting the staged provider setting.");
        discovery = [device, device with { DevicePath = "duplicate-ui" }];
        dialog.RefreshDeviceDiscovery();
        Check(!fields.Enabled && remove.Enabled && tabs.TabPages.Count == 1,
            "Ambiguous hardware must not create a duplicate configured tab or permit editing.");
        fail = true;
        dialog.RefreshDeviceDiscovery();
        Check(!fields.Enabled && remove.Enabled, "Discovery failure must not retain editable stale-connected controls.");
        fail = false;
        discovery = [device];
        dialog.RefreshDeviceDiscovery();
        dialog.ApplyOutputChanges(config);
        Check(config.Outputs.Targets.Single(t => t.Id == physical.Id).ProviderEnabled &&
              !config.Outputs.Targets.Single(t => t.Id == physical.Id).DeviceEnabled,
            "Provider-only apply must never implicitly enable USB.");
    }

    public static void SettingsApplyRecovery()
    {
        InSandbox(root =>
        {
            var path = Path.Combine(root, "app.json");
            var config = new AppConfig();
            config.Save(path);
            File.AppendAllText(path, " \r\n");
            var original = File.ReadAllBytes(path);
            config.Outputs.Targets[0].Name = "Edited draft";
            var calls = 0;
            Throws(() => EditorSettingsPersistence.ApplyAsync(path, config, original, () =>
            {
                calls++;
                return Task.FromResult<RuntimeIpcResponse?>(new(calls > 1, "Synthetic apply failure"));
            }).GetAwaiter().GetResult());
            Check(calls == 2 && File.ReadAllBytes(path).SequenceEqual(original),
                "Failed Runtime apply must restore the exact original file and attempt old-state recovery once.");
            File.AppendAllText(path, " ");
            var external = File.ReadAllBytes(path);
            calls = 0;
            Throws(() => EditorSettingsPersistence.ApplyAsync(path, config, original, () =>
            {
                calls++;
                return Task.FromResult<RuntimeIpcResponse?>(null);
            }).GetAwaiter().GetResult());
            Check(calls == 0 && File.ReadAllBytes(path).SequenceEqual(external),
                "Stale dialog state must fail before save/reload and preserve an external configuration change.");
            var reloaded = EditorSettingsPersistence.ApplyAsync(path, config, external,
                () => Task.FromResult<RuntimeIpcResponse?>(null)).GetAwaiter().GetResult();
            Check(!reloaded && File.ReadAllText(path).Contains("Edited draft", StringComparison.Ordinal),
                "An absent Runtime must receive no start request; settings remain saved for the next start.");
            var saved = File.ReadAllBytes(path);
            calls = 0;
            Throws(() => EditorSettingsPersistence.ApplyAsync(path, config, saved, () =>
            {
                calls++;
                File.AppendAllText(path, " ");
                return Task.FromResult<RuntimeIpcResponse?>(new(false, "Synthetic concurrent-writer failure"));
            }).GetAwaiter().GetResult());
            Check(calls == 1 && File.ReadAllBytes(path).Length > saved.Length,
                "Rollback must not overwrite a newer external writer's file or claim Runtime recovery.");
        });
    }

    private static OutputDeviceDescriptor Device(string suffix) =>
        Issue95GenericOutputTests.Device(OutputTargetContract.LyBulkTransport, suffix[0]);

    private static OutputTargetConfig Target(OutputDeviceDescriptor device, string suffix) => new()
    { Width = 1920, Height = 480,
        Id = "stage23-" + suffix, Name = "Synthetic " + suffix,
        Kind = OutputTargetContract.PhysicalKind, Transport = device.Transport, DeviceId = device.DeviceId,
        Dashboard = "Default", RefreshIntervalMs = 1000, JpegQuality = 92, Usb = new UsbConfig()
    };

    private static void InSandbox(Action<string> action)
    {
        var root = Path.Combine(Path.GetTempPath(), "PinkieSysMon-Stage23-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try { action(root); }
        finally { Directory.Delete(root, true); }
    }

    private static void Throws(Action action)
    {
        try { action(); }
        catch (InvalidOperationException) { return; }
        throw new InvalidOperationException("Expected the unsafe Settings action to be rejected.");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
