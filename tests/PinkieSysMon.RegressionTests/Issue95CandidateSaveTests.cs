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
            var (saved, reloaded) = PhysicalCandidateSaveTransaction.SaveAsync(root, path, log,
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
            Reject(() => PhysicalCandidateSaveTransaction.SaveAsync(root, path, log,
                confirmed, device.PnpNodeKey!, () => [], Persist).GetAwaiter().GetResult());
            Check(!File.Exists(board) && File.ReadAllBytes(path).SequenceEqual(before),
                "Lost exact binding must reject before writing config or board.");
            Reject(() => PhysicalCandidateSaveTransaction.SaveAsync(root, path, log,
                confirmed, device.PnpNodeKey!, () => [device with { PnpNodeKey = "changed" }], Persist)
                .GetAwaiter().GetResult());
            Check(!File.Exists(board), "Changed physical node cannot inherit the Test confirmation.");
            Reject(() => PhysicalCandidateSaveTransaction.SaveAsync(root, path, log,
                confirmed, device.PnpNodeKey!, () => [device], Persist).GetAwaiter().GetResult());
            Check(File.ReadAllBytes(path).SequenceEqual(before) && !File.Exists(board) &&
                  !model.Entries.Single().IsConfigured && model.CandidateDraft(card)!.CanSaveConfiguration,
                "Failed Runtime apply restores file/created board and leaves the confirmed draft recoverable.");

            var external = AppConfig.Load(path, log);
            external.Outputs.Targets.Add(confirmed);
            external.Save(path);
            var concurrent = File.ReadAllBytes(path);
            Reject(() => PhysicalCandidateSaveTransaction.SaveAsync(root, path, log,
                confirmed, device.PnpNodeKey!, () => [device], Persist).GetAwaiter().GetResult());
            Check(File.ReadAllBytes(path).SequenceEqual(concurrent) && !File.Exists(board),
                "An already committed binding must reject the stale card without collateral writes.");
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
