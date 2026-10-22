using PinkieSysMon.Editor;

namespace PinkieSysMon.RegressionTests;

internal static class Issue95CandidateSaveTests
{
    public static void PerCardMergeAndWorkingBoard()
    {
        InSandbox((root, path, log) =>
        {
            EditorForm.WriteNewDashboardFile(Path.Combine(root, "dashboards", "desktop_default"), 640, 640, 0);
            var original = new AppConfig(); original.Save(path);
            var device = Issue95GenericOutputTests.Device(OutputTargetContract.LyBulkTransport, 's');
            var model = new DeviceSettingsModel(original); model.Refresh([device]);
            var card = model.Entries.Single().Target;
            Issue95GenericOutputTests.Confirm(model, card, device);
            var confirmed = model.PrepareCandidate(card);
            var board = DashboardCatalog.GetDefinitionPath(root, confirmed.Dashboard!);
            Check(!File.Exists(board) && original.Outputs.Targets.Count == 1,
                "Visual Yes alone must not create a working board or Output.");

            // Another Settings writer changes an unrelated saved field after
            // this candidate was confirmed. Save must merge against disk.
            var newer = AppConfig.Load(path, log);
            newer.Outputs.Targets[0].Name = "Newer Desktop label";
            newer.Display.RefreshIntervalMs = 777;
            newer.Save(path);
            var calls = 0;
            var (saved, reloaded) = PhysicalDeviceSettingsTransaction.SaveCandidateAsync(root, path, log,
                confirmed, device.PnpNodeKey!, () => [device], (updated, bytes) =>
                {
                    calls++;
                    return EditorSettingsPersistence.ApplyAsync(path, updated, bytes,
                        () => Task.FromResult<RuntimeIpcResponse?>(null));
                }).GetAwaiter().GetResult();
            model.AcceptSavedCandidate(card, confirmed);
            Check(calls == 1 && !reloaded && !model.IsDirty &&
                  model.Entries.Single().IsConfigured && File.Exists(board) &&
                  saved.Outputs.Targets.Count == 2 &&
                  saved.Outputs.Targets[0].Name == "Newer Desktop label" &&
                  saved.Display.RefreshIntervalMs == 777 &&
                  saved.Outputs.Targets[1].Dashboard == "device_800x600_default" &&
                  !saved.Outputs.Targets[1].DeviceEnabled && !saved.Outputs.Targets[1].ProviderEnabled,
                "Only the selected confirmed card commits; unrelated newer settings survive and Runtime stays absent.");
            var reopened = AppConfig.Load(path, log);
            Check(reopened.Outputs.Targets[1].DeviceId == device.DeviceId && File.Exists(board),
                "The disabled exact binding and ordinary editable board survive restart without another Test.");
        });
    }

    public static void StaleBindingAndApplyFailure()
    {
        InSandbox((root, path, log) =>
        {
            EditorForm.WriteNewDashboardFile(Path.Combine(root, "dashboards", "desktop_default"), 640, 640, 0);
            var config = new AppConfig(); config.Save(path);
            var device = Issue95GenericOutputTests.Device(OutputTargetContract.EncryptedJpegTransport, 't');
            var model = new DeviceSettingsModel(config); model.Refresh([device]);
            var card = model.Entries.Single().Target;
            Issue95GenericOutputTests.Confirm(model, card, device);
            var confirmed = model.PrepareCandidate(card);
            var board = DashboardCatalog.GetDefinitionPath(root, confirmed.Dashboard!);
            Task<bool> Persist(AppConfig updated, byte[]? bytes) =>
                EditorSettingsPersistence.ApplyAsync(path, updated, bytes,
                    () => Task.FromResult<RuntimeIpcResponse?>(new(false, "Injected apply failure")));
            var before = File.ReadAllBytes(path);
            Reject(() => PhysicalDeviceSettingsTransaction.SaveCandidateAsync(root, path, log,
                confirmed, device.PnpNodeKey!, () => [], Persist).GetAwaiter().GetResult());
            Check(!File.Exists(board) && File.ReadAllBytes(path).SequenceEqual(before),
                "Lost exact binding must reject before writing config or board.");
            Reject(() => PhysicalDeviceSettingsTransaction.SaveCandidateAsync(root, path, log,
                confirmed, device.PnpNodeKey!, () => [device with { PnpNodeKey = "changed" }], Persist)
                .GetAwaiter().GetResult());
            Check(!File.Exists(board), "Changed physical node cannot inherit the Test confirmation.");
            Reject(() => PhysicalDeviceSettingsTransaction.SaveCandidateAsync(root, path, log,
                confirmed, device.PnpNodeKey!, () => [device], Persist).GetAwaiter().GetResult());
            Check(File.ReadAllBytes(path).SequenceEqual(before) && !File.Exists(board) &&
                  !model.Entries.Single().IsConfigured && model.CandidateDraft(card)!.CanSaveConfiguration,
                "Failed Runtime apply restores file/created board and leaves the confirmed draft recoverable.");

            var external = AppConfig.Load(path, log);
            external.Outputs.Targets.Add(confirmed);
            external.Save(path);
            var concurrent = File.ReadAllBytes(path);
            Reject(() => PhysicalDeviceSettingsTransaction.SaveCandidateAsync(root, path, log,
                confirmed, device.PnpNodeKey!, () => [device], Persist).GetAwaiter().GetResult());
            Check(File.ReadAllBytes(path).SequenceEqual(concurrent) && !File.Exists(board),
                "An already committed binding must reject the stale card without collateral writes.");
        });
    }

    public static void ConfiguredCardIsolationAndStaleWrite()
    {
        InSandbox((root, path, log) =>
        {
            EditorForm.WriteNewDashboardFile(Path.Combine(root, "dashboards", "desktop_default"), 640, 640, 0);
            EditorForm.WriteNewDashboardFile(Path.Combine(root, "dashboards", "Existing"), 800, 600, 0);
            var device = Issue95GenericOutputTests.Device(OutputTargetContract.LyBulkTransport, 'u');
            var siblingDevice = Issue95GenericOutputTests.Device(OutputTargetContract.LyBulkTransport, 'v');
            var config = new AppConfig();
            config.Outputs.Targets.Add(new OutputTargetConfig
            {
                Id = "configured-u", Name = "Original", Kind = OutputTargetContract.PhysicalKind,
                Transport = device.Transport, DeviceId = device.DeviceId, Width = 800, Height = 600,
                Dashboard = "Existing", Usb = new UsbConfig()
            });
            config.Outputs.Targets.Add(new OutputTargetConfig
            {
                Id = "configured-v", Name = "Sibling", Kind = OutputTargetContract.PhysicalKind,
                Transport = siblingDevice.Transport, DeviceId = siblingDevice.DeviceId,
                Width = 800, Height = 600, Dashboard = "Existing", Usb = new UsbConfig()
            });
            config.Save(path);
            var model = new DeviceSettingsModel(AppConfig.Load(path, log)); model.Refresh([device, siblingDevice]);
            var card = model.Entries.Single(e => e.Target.Id == "configured-u").Target;
            var siblingDraft = model.Entries.Single(e => e.Target.Id == "configured-v").Target;
            siblingDraft.Name = "Unsaved sibling draft"; model.MarkChanged(siblingDraft);
            card.Name = "Selected edit"; card.ProviderEnabled = true; model.MarkChanged(card);
            var (original, updated) = model.PrepareConfigured(card);
            var newer = AppConfig.Load(path, log);
            newer.Outputs.Targets[0].Name = "Concurrent Desktop edit";
            newer.Outputs.Targets[2].Name = "Concurrent sibling edit";
            newer.Display.RefreshIntervalMs = 655;
            newer.Save(path);
            var beforeSave = File.ReadAllBytes(path);
            Reject(() => PhysicalDeviceSettingsTransaction.SaveConfiguredAsync(
                root, path, log, original, updated, () => [],
                (next, expected) => EditorSettingsPersistence.ApplyAsync(path, next, expected,
                    () => Task.FromResult<RuntimeIpcResponse?>(null))).GetAwaiter().GetResult());
            Check(File.ReadAllBytes(path).SequenceEqual(beforeSave),
                "Configured Save cannot commit against a lost exact interface.");
            var (saved, reloaded) = PhysicalDeviceSettingsTransaction.SaveConfiguredAsync(
                root, path, log, original, updated, () => [device],
                (next, expected) => EditorSettingsPersistence.ApplyAsync(path, next, expected,
                    () => Task.FromResult<RuntimeIpcResponse?>(null))).GetAwaiter().GetResult();
            model.AcceptSavedConfigured(card, updated);
            Check(!reloaded && model.IsDirty && siblingDraft.Name == "Unsaved sibling draft" &&
                  saved.Outputs.Targets[0].Name == "Concurrent Desktop edit" &&
                  saved.Display.RefreshIntervalMs == 655 &&
                  saved.Outputs.Targets[1].Name == "Selected edit" && saved.Outputs.Targets[1].ProviderEnabled &&
                  !saved.Outputs.Targets[1].DeviceEnabled && saved.Outputs.Targets[1].Dashboard == "Existing" &&
                  saved.Outputs.Targets[2].Name == "Concurrent sibling edit",
                "Configured Save commits only its card, retaining an unsaved sibling draft and newer disk settings.");

            var another = new DeviceSettingsModel(AppConfig.Load(path, log)); another.Refresh([device, siblingDevice]);
            var staleCard = another.Entries.Single(e => e.Target.Id == "configured-u").Target;
            staleCard.Name = "Stale edit"; another.MarkChanged(staleCard);
            var (staleOriginal, staleUpdated) = another.PrepareConfigured(staleCard);
            var concurrent = AppConfig.Load(path, log);
            concurrent.Outputs.Targets[1].Name = "External selected edit";
            concurrent.Save(path);
            var externalBytes = File.ReadAllBytes(path);
            Reject(() => PhysicalDeviceSettingsTransaction.SaveConfiguredAsync(
                root, path, log, staleOriginal, staleUpdated, () => [device],
                (next, expected) => EditorSettingsPersistence.ApplyAsync(path, next, expected,
                    () => Task.FromResult<RuntimeIpcResponse?>(null))).GetAwaiter().GetResult());
            Check(File.ReadAllBytes(path).SequenceEqual(externalBytes),
                "A newer edit of this same Output must not be overwritten by an older card.");

            var current = new DeviceSettingsModel(AppConfig.Load(path, log)); current.Refresh([device, siblingDevice]);
            var missing = current.Entries.Single(e => e.Target.Id == "configured-u").Target;
            missing.Dashboard = "Missing-assignment"; current.MarkChanged(missing);
            var (missingOriginal, missingUpdated) = current.PrepareConfigured(missing);
            Reject(() => PhysicalDeviceSettingsTransaction.SaveConfiguredAsync(
                root, path, log, missingOriginal, missingUpdated, () => [device],
                (next, expected) => EditorSettingsPersistence.ApplyAsync(path, next, expected,
                    () => Task.FromResult<RuntimeIpcResponse?>(null))).GetAwaiter().GetResult());
            Check(File.ReadAllBytes(path).SequenceEqual(externalBytes) &&
                  !Directory.Exists(Path.Combine(root, "dashboards", "Missing-assignment")),
                "Missing selected dashboard is an Output-specific failure, not generated or saved silently.");
        });
    }

    public static void ForgetExactNodeAndRollback()
    {
        InSandbox((root, path, log) =>
        {
            var first = Issue95GenericOutputTests.Device(OutputTargetContract.LyBulkTransport, 'w');
            var neighbor = Issue95GenericOutputTests.Device(OutputTargetContract.LyBulkTransport, 'x');
            var original = new AppConfig(); original.Save(path);
            var model = new DeviceSettingsModel(original); model.Refresh([first, neighbor]);
            var card = model.Entries.Single(e => e.Target.DeviceId == first.DeviceId).Target;
            var node = model.PrepareForget(card);
            var before = File.ReadAllBytes(path);
            Task<bool> Apply(AppConfig next, byte[]? expected) =>
                EditorSettingsPersistence.ApplyAsync(path, next, expected,
                    () => Task.FromResult<RuntimeIpcResponse?>(null));
            Reject(() => PhysicalDeviceSettingsTransaction.ForgetCandidateAsync(path, log,
                first.DeviceId, node, () => [first, first], Apply).GetAwaiter().GetResult());
            Check(File.ReadAllBytes(path).SequenceEqual(before), "Ambiguous node cannot be forgotten.");
            Reject(() => PhysicalDeviceSettingsTransaction.ForgetCandidateAsync(path, log,
                first.DeviceId, node, () => [first], (next, expected) =>
                    EditorSettingsPersistence.ApplyAsync(path, next, expected,
                        () => Task.FromResult<RuntimeIpcResponse?>(new(false, "Injected apply failure"))))
                .GetAwaiter().GetResult());
            Check(File.ReadAllBytes(path).SequenceEqual(before) && model.Entries.Count == 2,
                "Failed apply restores the config and both candidate cards.");
            var newer = AppConfig.Load(path, log);
            newer.Outputs.Targets[0].Name = "Newer Desktop"; newer.Save(path);
            var (saved, reloaded) = PhysicalDeviceSettingsTransaction.ForgetCandidateAsync(path, log,
                first.DeviceId, node, () => [first, neighbor], Apply).GetAwaiter().GetResult();
            model.AcceptForgot(card, node); model.Refresh([first, neighbor]);
            Check(!reloaded && saved.Outputs.Targets[0].Name == "Newer Desktop" &&
                  saved.Outputs.IgnoredDeviceNodeKeys.SequenceEqual([node]) &&
                  model.Entries.Count == 1 && model.Entries[0].Target.DeviceId == neighbor.DeviceId,
                "Forget merges one hashed node and retains a same-product neighbor and newer settings.");
            var reopened = new DeviceSettingsModel(AppConfig.Load(path, log)); reopened.Refresh([first, neighbor]);
            Check(reopened.Entries.Count == 1 && reopened.Entries[0].Target.DeviceId == neighbor.DeviceId,
                "The exclusion survives reload and replug.");
            var intent = new CatalogUsbTrialIntent(Guid.NewGuid(), first.DeviceId, node,
                OutputTargetContract.LyBulkTransport, 800, 600, 1);
            Check(RuntimeUsbTestPreflight.Check(intent, new([first.Candidate!, neighbor.Candidate!], []),
                saved.Outputs.IgnoredDeviceNodeKeys.ToHashSet(StringComparer.OrdinalIgnoreCase)) ==
                  RuntimeUsbTestPreflightStatus.Missing,
                "Runtime Test must deny a forgotten exact node before opening USB.");
            saved.Outputs.IgnoredDeviceNodeKeys.Clear(); saved.Save(path);
            var restored = new DeviceSettingsModel(AppConfig.Load(path, log)); restored.Refresh([first, neighbor]);
            Check(restored.Entries.Count == 2, "Manual exclusion removal restores the candidate offer.");
        });
    }

    private static void InSandbox(Action<string, string, FileLogger> body)
    {
        var root = Path.Combine(Path.GetTempPath(), "PinkieSysMon-CandidateSave-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "config"));
        try { body(root, Path.Combine(root, "config", "app.json"),
            new FileLogger(Path.Combine(root, "test.log"))); }
        finally { Directory.Delete(root, true); }
    }

    private static void Reject(Action action)
    {
        try { action(); }
        catch (Exception ex) when (ex is InvalidOperationException or InvalidDataException) { return; }
        throw new InvalidOperationException("Expected candidate Save to reject this transaction.");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
