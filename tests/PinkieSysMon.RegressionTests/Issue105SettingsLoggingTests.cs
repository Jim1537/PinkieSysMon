using System.Text.Json;
using PinkieSysMon.Editor;
using C = PinkieSysMon.DashboardModel;

namespace PinkieSysMon.RegressionTests;

internal static class Issue105SettingsLoggingTests
{
    public static void PersistenceMilestonesAndConflict() => Sandbox((root, path, log) =>
    {
        var config = new AppConfig(); config.Save(path);
        var original = File.ReadAllBytes(path);
        config.Outputs.Targets[0].Name = "private-fixture-label";
        var reloaded = true;
        Observe(log, "fixture.settings.saved", operation =>
            reloaded = EditorSettingsPersistence.ApplyAsync(path, config, original,
                () => Task.FromResult<RuntimeIpcResponse?>(null), operation).GetAwaiter().GetResult());
        var records = Operations(log).Where(e => Name(e) == "fixture.settings.saved").ToArray();
        Check(!reloaded && records.Last().GetProperty("outcome").GetString() == "succeeded" &&
              records.Where(e => e.GetProperty("outcome").GetString() == "progress")
                  .Select(e => e.GetProperty("phase").GetString()).SequenceEqual(new[]
                  { "file.conflictCheck", "file.write", "file.committed", "runtime.apply", "runtime.absent" }) &&
              !File.Exists(log.ErrorsPath) && !File.ReadAllText(log.Path).Contains("private-fixture-label"),
            "A real save for the next start must expose ordered milestones without settings values or fabricated errors.");
        var saved = File.ReadAllBytes(path);
        var calls = 0;
        var failure = Capture(() => Observe(log, "fixture.settings.stale", operation =>
            EditorSettingsPersistence.ApplyAsync(path, config, original, () =>
            { calls++; return Task.FromResult<RuntimeIpcResponse?>(null); }, operation).GetAwaiter().GetResult()));
        var error = Operations(log, errors: true).Single(e => Name(e) == "fixture.settings.stale");
        Check(failure is InvalidOperationException && calls == 0 && File.ReadAllBytes(path).SequenceEqual(saved) &&
              error.GetProperty("phase").GetString() == "file.conflictCheck",
            "Stale drafts must fail before save/apply and identify the failed conflict check.");
    });

    public static void RecoveryAndConcurrentWriterEvidence() => Sandbox((root, path, log) =>
    {
        var config = new AppConfig(); config.Save(path);
        var original = File.ReadAllBytes(path);
        config.Outputs.Targets[0].Name = "private-recovery-draft";
        var first = new IOException("fixture-primary-apply-failure");
        var recovery = new IOException("fixture-secondary-recovery-failure");
        var calls = 0;
        var failure = Capture(() => Observe(log, "fixture.settings.recovery", operation =>
            EditorSettingsPersistence.ApplyAsync(path, config, original, () =>
            { calls++; throw calls == 1 ? first : recovery; }, operation).GetAwaiter().GetResult()));
        var errors = Operations(log, errors: true);
        Check(failure is InvalidOperationException && ReferenceEquals(failure.InnerException, first) && calls == 2 &&
              File.ReadAllBytes(path).SequenceEqual(original) &&
              errors.Any(e => e.GetProperty("phase").GetString() == "runtime.apply") &&
              errors.Any(e => e.GetProperty("phase").GetString() == "rollback.runtime.apply") &&
              Operations(log).Any(e => e.GetProperty("phase").GetString() == "rollback.file.restored") &&
              !File.ReadAllText(log.Path).Contains(first.Message) && !File.ReadAllText(log.Path).Contains(recovery.Message),
            "Both original failures must be retained in Errors while exact bytes and the previous recovery policy survive.");
        var rows = File.ReadAllLines(log.ErrorsPath).Select(line => JsonSerializer.Deserialize<JsonElement>(line[line.IndexOf('{')..])).ToArray();
        Check(rows.Count(e => e.GetProperty("exception").GetString()?.Contains("System.IO.IOException: " + recovery.Message) == true) == 1,
            "The previously unretained secondary recovery exception needs one full error record.");
        calls = 0;
        Capture(() => Observe(log, "fixture.settings.concurrent", operation =>
            EditorSettingsPersistence.ApplyAsync(path, config, original, () =>
            { calls++; File.AppendAllText(path, " "); throw first; }, operation).GetAwaiter().GetResult()));
        Check(calls == 1 && File.ReadAllBytes(path).Length > original.Length &&
              Operations(log).Any(e => Name(e) == "fixture.settings.concurrent" &&
                  e.GetProperty("phase").GetString() == "rollback.file.conflict"),
            "A newer file must survive without a second Runtime request and with an explicit unconfirmed recovery state.");
    });

    public static void PhysicalCardIdentityAndPrivacy() => Sandbox((root, path, log) =>
    {
        var device = Issue95GenericOutputTests.Device(OutputTargetContract.LyBulkTransport, 'h');
        var other = Issue95GenericOutputTests.Device(OutputTargetContract.LyBulkTransport, 'i');
        var config = new AppConfig();
        var selected = Target(device); config.Outputs.Targets.Add(selected); config.Save(path);
        selected = AppConfig.Load(path, log).Outputs.Targets.Single(t => t.Id == selected.Id);
        Board(root);
        var updated = AppConfig.Load(path, log).Outputs.Targets.Single(t => t.Id == selected.Id);
        updated.Name = "private-card-name"; updated.ProviderEnabled = true;
        Task<bool> Persist(AppConfig next, byte[]? expected) => EditorSettingsPersistence.ApplyAsync(
            path, next, expected, () => Task.FromResult<RuntimeIpcResponse?>(null));
        PhysicalDeviceSettingsTransaction.SaveConfiguredAsync(root, path, log, selected, updated,
            () => [device], Persist, "fixture-card-parent").GetAwaiter().GetResult();
        var save = Operations(log).Where(e => Name(e) == "editor.settings.configured.save").ToArray();
        var detail = save.Single(e => e.GetProperty("phase").GetString() == "dashboard.validate").GetProperty("detail").GetString()!;
        Check(save.All(e => e.GetProperty("parentOperationId").GetString() == "fixture-card-parent" &&
                    e.GetProperty("subject").GetString() == selected.Id) &&
              detail.Contains("Name") && detail.Contains("ProviderEnabled") &&
              save.Last().GetProperty("outcome").GetString() == "succeeded",
            "Configured Save must identify its logical card, parent and changed property names.");
        PhysicalDeviceSettingsTransaction.ForgetCandidateAsync(path, log, other.DeviceId, other.PnpNodeKey!,
            () => [other], Persist, "fixture-forget-parent").GetAwaiter().GetResult();
        var forget = Operations(log).Last(e => Name(e) == "editor.settings.candidate.forget");
        var text = File.ReadAllText(log.Path);
        Check(forget.GetProperty("outcome").GetString() == "succeeded" &&
              forget.GetProperty("subject").ValueKind == JsonValueKind.Null &&
              !text.Contains("private-card-name") && !text.Contains(device.DeviceId) && !text.Contains(other.PnpNodeKey!),
            "Successful Save/Forget must not expose display names, interface identities or node hashes.");
        var expected = File.ReadAllBytes(path);
        var calls = 0;
        Capture(() => PhysicalDeviceSettingsTransaction.SaveConfiguredAsync(root, path, log, selected, updated,
            () => [device], (next, bytes) => { calls++; return Persist(next, bytes); }).GetAwaiter().GetResult());
        Check(calls == 0 && File.ReadAllBytes(path).SequenceEqual(expected) &&
              Operations(log, errors: true).Last().GetProperty("phase").GetString() == "conflict.selectedRecord",
            "A stale configured card must expose its selected-record rejection before persistence.");
    });

    public static void RemovalPartialAndAbsentEvidence() => Sandbox((root, path, log) =>
    {
        var device = Issue95GenericOutputTests.Device(OutputTargetContract.LyBulkTransport, 'j');
        var config = new AppConfig(); var selected = Target(device); config.Outputs.Targets.Add(selected); config.Save(path);
        selected = AppConfig.Load(path, log).Outputs.Targets.Single(t => t.Id == selected.Id);
        var stops = 0;
        var result = PhysicalDeviceSettingsTransaction.RemoveConfiguredAsync(path, log, selected,
            () => Task.FromResult(new RuntimeIpcStatusResponse(true, "", new("", [new(selected.Id, "fixture", true, true, "ACTIVE")]))),
            () => true, () => throw new InvalidOperationException("Running Runtime must keep its lease."),
            (_, _) => { stops++; return Task.FromResult(new RuntimeIpcResponse(true, "fixture released") { ResponseReceived = true }); },
            () => Task.FromResult(new RuntimeIpcResponse(false, "fixture-reload-unconfirmed") { ResponseReceived = false }))
            .GetAwaiter().GetResult();
        var events = Operations(log).Where(e => Name(e) == "editor.settings.configured.remove").ToArray();
        var phases = events.Select(e => e.GetProperty("phase").GetString()).ToList();
        Check(stops == 1 && !result.RuntimeReloaded && result.Warning is not null &&
              AppConfig.Load(path, log).Outputs.Targets.All(t => t.Id != selected.Id) &&
              events.Last().GetProperty("outcome").GetString() == "partial" &&
              phases.IndexOf("runtime.releaseConfirmed") < phases.IndexOf("file.committed") &&
              Operations(log, errors: true).Any(e => e.GetProperty("outcome").GetString() == "failure") &&
              !File.ReadAllText(log.Path).Contains("fixture-reload-unconfirmed"),
            "Remove must retain deletion after release, keep the full no-exception failure only in Errors and report partial outcome.");
        config.Save(path);
        var disposed = false;
        var lease = new CallbackLease(() => disposed = true);
        result = PhysicalDeviceSettingsTransaction.RemoveConfiguredAsync(path, log, selected,
            () => Task.FromResult(new RuntimeIpcStatusResponse(false, "absent", null)), () => false,
            () => lease, (_, _) => throw new InvalidOperationException("Absent Runtime must not receive stop."),
            () => throw new InvalidOperationException("Absent Runtime must not receive reload."))
            .GetAwaiter().GetResult();
        Check(disposed && !result.RuntimeReloaded && result.Warning is null &&
              Operations(log).Last().GetProperty("outcome").GetString() == "succeeded",
            "Absent-Runtime removal must retain lease disposal and distinguish saved success from a reload.");
    });

    internal static JsonElement[] Operations(FileLogger log, bool errors = false)
    {
        var path = errors ? log.ErrorsPath : log.Path;
        if (!File.Exists(path)) return [];
        return File.ReadAllLines(path)
            .Select(line => JsonSerializer.Deserialize<JsonElement>(line[line.IndexOf('{')..]).GetProperty("message").GetString())
            .Where(message => message is not null && message.StartsWith('{'))
            .Select(message => JsonSerializer.Deserialize<JsonElement>(message!))
            .Where(record => record.TryGetProperty("operation", out _)).ToArray();
    }

    internal static string? Name(JsonElement record) => record.GetProperty("operation").GetString();
    private static void Observe(FileLogger log, string name, Action<LogOperation> work)
    {
        using var operation = new LogOperation(log, name);
        try { work(operation); operation.Complete(); }
        catch (Exception error) { operation.Fail(error); throw; }
    }
    private static Exception Capture(Action action)
    {
        try { action(); } catch (Exception error) { return error; }
        throw new InvalidOperationException("Expected the settings operation to fail.");
    }
    private static OutputTargetConfig Target(OutputDeviceDescriptor device) => new()
    {
        Id = "fixture-physical", Name = "fixture", Kind = OutputTargetContract.PhysicalKind,
        Transport = device.Transport, DeviceId = device.DeviceId, Width = 800, Height = 600,
        Dashboard = "Existing", Usb = new UsbConfig()
    };
    private static void Board(string root)
    {
        var directory = Path.Combine(root, "dashboards", "Existing"); Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "dashboard.json"), DashboardJson.Serialize(new C.DashboardDefinition
        { Canvas = new() { Width = 800, Height = 600 }, Widgets = [] }));
    }
    private sealed class CallbackLease(Action dispose) : IDisposable
    { public void Dispose() => dispose(); }
    private static void Sandbox(Action<string, string, FileLogger> action)
    {
        var root = Path.Combine(Path.GetTempPath(), "pinkie-settings-logging-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "config"));
        try { action(root, Path.Combine(root, "config", "app.json"), new FileLogger(Path.Combine(root, "session.log"), "Editor")); }
        finally { Directory.Delete(root, recursive: true); }
    }
    private static void Check(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }
}
