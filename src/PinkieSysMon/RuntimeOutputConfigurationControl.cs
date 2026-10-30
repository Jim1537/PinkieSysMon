using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace PinkieSysMon;

// Called on Runtime's UI thread. Neither the client nor persistence owns live sessions.
internal sealed class RuntimeOutputConfigurationControl(
    OutputSessionManager sessions, Func<AppConfig> currentConfig, string applicationRoot,
    FileLogger log, Action<AppConfig> commitRuntime)
{
    internal RuntimeIpcResponse PreparePhysicalRemoval(RuntimeIpcCommand command)
    {
        using var operation = new LogOperation(log, "runtime.settings.remove.prepare", command.TargetId);
        try
        {
            operation.Phase("validation.selectedRecord");
            var selected = currentConfig().Outputs.Targets.SingleOrDefault(t =>
                t.Id.Equals(command.TargetId, StringComparison.OrdinalIgnoreCase));
            if (selected is null || !OutputTargetContract.IsPhysicalKind(selected.Kind) ||
                !string.Equals(RuntimeIpcProtocol.TargetRecordHash(selected), command.RecordHash, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Runtime's selected Output differs from the Remove card. Reopen Settings.");
            var path = Path.Combine(applicationRoot, "config", "app.json");
            operation.Phase("file.readAndValidate");
            var original = File.ReadAllBytes(path);
            var disk = AppConfig.Load(path, log);
            var saved = disk.Outputs.Targets.SingleOrDefault(t => t.Id.Equals(selected.Id, StringComparison.OrdinalIgnoreCase));
            if (saved is null || !string.Equals(RuntimeIpcProtocol.TargetRecordHash(saved), command.RecordHash, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("The saved Output differs from the Remove card. Reopen Settings.");
            EnsureCurrent(path, original);
            operation.Phase("native.stopAndRelease");
            sessions.PreparePhysicalRemoval(selected.Id);
            operation.Complete("selectedNativeCleanupReturned=true; savedRecordUnchanged=true");
            return new RuntimeIpcResponse(true, "Selected physical Output stopped and native cleanup completed; remove its saved record now.");
        }
        catch (Exception error) { operation.Fail(error); throw; }
    }

    public RuntimeIpcResponse Execute(RuntimeIpcCommand command)
    {
        using var operation = new LogOperation(log, "runtime.settings.output.command", command?.TargetId);
        try
        {
            operation.Phase("validation.command", "kind=" + command?.Kind);
            ArgumentNullException.ThrowIfNull(command);
            if (command.Kind == RuntimeIpcCommandKind.StartOrReloadTarget)
            {
                var reloadResult = Reload(command.TargetId!, operation.Id);
                operation.Complete("selectedReloadReturned=true");
                return reloadResult;
            }
            var previous = currentConfig();
            var selected = previous.Outputs.Targets.SingleOrDefault(t =>
                t.Id.Equals(command.TargetId, StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException($"Output '{command.TargetId}' is not configured.");
            var replacement = CloneTarget(selected);
            string property;
            JsonNode value;
            string message;
            var reloadBoard = false;
            switch (command.Kind)
            {
                case RuntimeIpcCommandKind.AssignDashboard:
                    var name = command.Dashboard?.Trim();
                    if (!WindowsPathComponentValidator.IsSafe(name))
                        throw new InvalidDataException("Invalid dashboard name.");
                    replacement.Dashboard = name;
                    property = "Dashboard";
                    value = JsonValue.Create(name)!;
                    message = $"Dashboard '{name}' assigned to Output '{selected.Id}'.";
                    break;

                case RuntimeIpcCommandKind.SetDesktopBoardEnabled:
                case RuntimeIpcCommandKind.SetDesktopLock:
                    if (selected.Kind != OutputTargetContract.DesktopKind || selected.Desktop is null || command.Value is null)
                        throw new InvalidOperationException("This setting requires a Desktop Output and a boolean value.");
                    property = command.Kind == RuntimeIpcCommandKind.SetDesktopBoardEnabled ? "BoardEnabled" : "Lock";
                    if (property == "BoardEnabled")
                    {
                        replacement.Desktop!.BoardEnabled = command.Value.Value;
                        reloadBoard = command.Value.Value;
                    }
                    else replacement.Desktop!.Lock = command.Value.Value;
                    value = JsonValue.Create(command.Value.Value)!;
                    message = $"Desktop Output '{selected.Id}' {property} = {command.Value.Value}.";
                    break;

                default:
                    throw new InvalidOperationException("Unsupported Output configuration request.");
            }

            var path = Path.Combine(applicationRoot, "config", "app.json");
            var draft = PrepareDraft(path, selected, property, value, log, operation);
            operation.Phase("dashboard.validate", "property=" + property);
            // Assignment and an explicit enable validate only this target's on-disk board.
            // Lock/hide retain its already committed document, even if its file changed.
            var board = command.Kind == RuntimeIpcCommandKind.AssignDashboard || reloadBoard
                ? RuntimeConfigurationValidator.LoadTarget(replacement, applicationRoot)
                : sessions.GetDashboardAssignments().GetValueOrDefault(selected.Id);
            var result = ApplySetting(previous, replacement, board, draft, message, parentOperationId: operation.Id);
            operation.Complete("selectedSettingReturned=true");
            return result;
        }
        catch (Exception error) { operation.Fail(error); throw; }
    }

    // No IPC route. Consent is completed before the caller creates command feedback.
    // The returned callback retains the exact input ownership across the modal dialog.
    public Func<RuntimeIpcResponse>? PreparePhysicalEnablement(string targetId, bool enabled,
        Func<OutputTargetConfig, bool> confirmEnable,
        Func<IReadOnlyList<OutputDeviceDescriptor>> discover)
    {
        using var operation = new LogOperation(log, "runtime.settings.enable.prepare", targetId);
        try
        {
            operation.Phase("validation.enablement");
            ArgumentNullException.ThrowIfNull(confirmEnable);
            ArgumentNullException.ThrowIfNull(discover);
            var previous = currentConfig();
            var target = previous.Outputs.Targets.SingleOrDefault(t =>
                t.Id.Equals(targetId, StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException($"Output '{targetId}' is not configured.");
            if (!OutputTargetContract.IsPhysicalKind(target.Kind) || target.DeviceEnabled == enabled)
                throw new InvalidOperationException("The physical Output or its enablement changed. Reopen the tray menu.");
            if (enabled && !OutputTargetContract.HasIntegratedTransport(target.Transport))
                throw new InvalidOperationException("This device transport is not integrated; enabling is unavailable.");
            var selectedJson = JsonSerializer.Serialize(target);
            var selected = CloneTarget(target);
            var replacement = CloneTarget(selected);
            replacement.DeviceEnabled = enabled;
            var path = Path.Combine(applicationRoot, "config", "app.json");
            var draft = PrepareDraft(path, selected, "DeviceEnabled", JsonValue.Create(enabled)!, log, operation);

            void ValidateInput()
            {
                var current = currentConfig();
                var actual = current.Outputs.Targets.SingleOrDefault(t =>
                    t.Id.Equals(targetId, StringComparison.OrdinalIgnoreCase));
                if (!ReferenceEquals(current, previous) || actual is null ||
                    JsonSerializer.Serialize(actual) != selectedJson)
                    throw new InvalidOperationException("Runtime configuration changed during the physical Output operation. Reopen the tray menu.");
                draft.Validate();
                if (enabled)
                {
                    var entry = PhysicalDeviceInventory.Capture(current.Outputs.Targets, discover)
                        .SingleOrDefault(e => e.ConfiguredTarget?.Id.Equals(targetId, StringComparison.OrdinalIgnoreCase) == true);
                    if (!PhysicalDeviceInventory.CanOfferEnable(entry) ||
                        !OutputTargetContract.ConnectionEquals(selected, entry!.ConfiguredTarget!))
                        throw new InvalidOperationException("The configured device is absent or its exact physical identity is ambiguous. No enablement was committed.");
                }
                draft.Validate(); // Discovery/consent does not reserve the application file.
            }

            ValidateInput();
            if (enabled && !confirmEnable(CloneTarget(selected)))
            {
                operation.Cancel("Enablement consent declined; no commit callback returned.");
                return null;
            }
            operation.Complete("validatedCallbackPrepared=true; settingCommitted=false");
            return () =>
            {
                using var commit = new LogOperation(log, "runtime.settings.enable.commit", targetId, operation.Id);
                try
                {
                    commit.Phase("conflict.afterConsent");
                    ValidateInput();
                    commit.Phase("dashboard.validate");
                    var board = enabled
                        ? RuntimeConfigurationValidator.LoadTarget(replacement, applicationRoot)
                        : sessions.GetDashboardAssignments().GetValueOrDefault(selected.Id);
                    var result = ApplySetting(previous, replacement, board, draft,
                        $"Physical Output '{selected.Id}' Enabled = {enabled}.", ValidateInput, physicalEnablement: true, parentOperationId: commit.Id);
                    commit.Complete("selectedEnablementReturned=true");
                    return result;
                }
                catch (Exception error) { commit.Fail(error); throw; }
            };
        }
        catch (Exception error) { operation.Fail(error); throw; }
    }

    private RuntimeIpcResponse ApplySetting(AppConfig previous, OutputTargetConfig replacement,
        global::PinkieSysMon.DashboardModel.DashboardDefinition? board, ConfigurationDraft draft,
        string message, Action? validateInput = null, bool physicalEnablement = false, string? parentOperationId = null)
    {
        using var operation = new LogOperation(log, "runtime.settings.output.apply", replacement.Id, parentOperationId);
        try
        {
            var runtimeCommitAttempted = false;
            var fileCommitted = false;
            string? callbackPhase = null;
            try
            {
                void Commit(AppConfig next)
                {
                    // The manager invokes this under its gate. Do not perform
                    // diagnostic writes here; publish observations after return.
                    callbackPhase = "file.commit";
                    draft.Save();
                    fileCommitted = true;
                    callbackPhase = "runtime.commit";
                    runtimeCommitAttempted = true;
                    commitRuntime(next);
                    callbackPhase = null;
                }
                operation.Phase("outputs.apply");
                if (physicalEnablement)
                    sessions.ReconfigurePhysicalEnablement(replacement, board, Commit, validateInput!);
                else
                    sessions.ReconfigureTarget(replacement, board, Commit);
                if (fileCommitted) operation.Phase("file.committed", "observedInCallback=true; emittedAfterManagerReturn=true");
                operation.Phase("runtime.commit.returned");
            }
            catch (Exception error)
            {
                operation.RecordFailure(error, callbackPhase ?? operation.CurrentPhase, $"fileCommitted={fileCommitted}; recoveryRequired=true");
                var failures = new List<Exception>();
                try { draft.Restore(operation); }
                catch (Exception rollbackError)
                {
                    failures.Add(rollbackError);
                    operation.RecordFailure(rollbackError, operation.CurrentPhase, "recovery=failed");
                }
                if (runtimeCommitAttempted)
                {
                    try { operation.Phase("rollback.runtime.commit"); commitRuntime(previous); operation.Phase("rollback.runtime.returned"); }
                    catch (Exception rollbackError) { failures.Add(rollbackError); operation.RecordFailure(rollbackError, "rollback.runtime.commit"); }
                }
                if (failures.Count > 0)
                    throw new AggregateException("Output setting failed; rollback was incomplete.",
                        [error, .. failures]);
                throw;
            }
            operation.Complete("selectedSettingCommitted=true");
            return new RuntimeIpcResponse(true, message);
        }
        catch (Exception error) { operation.Fail(error); throw; }
    }

    private static OutputTargetConfig CloneTarget(OutputTargetConfig target) =>
        JsonSerializer.Deserialize<OutputTargetConfig>(JsonSerializer.Serialize(target))!;

    private RuntimeIpcResponse Reload(string targetId, string? parentOperationId = null)
    {
        using var operation = new LogOperation(log, "runtime.settings.output.reload", targetId, parentOperationId);
        try
        {
            operation.Phase("admission");
            sessions.EnsureCanReloadTarget(targetId);
            var previous = currentConfig();
            var selected = previous.Outputs.Targets.Single(t =>
                t.Id.Equals(targetId, StringComparison.OrdinalIgnoreCase));
            var path = Path.Combine(applicationRoot, "config", "app.json");
            operation.Phase("file.readAndValidate");
            var original = File.ReadAllBytes(path);
            // Retain structural/identity validation, but do not load sibling dashboards
            // or apply the saved common settings and unrelated target edits.
            var disk = AppConfig.Load(path, log);
            EnsureCurrent(path, original);
            var replacement = disk.Outputs.Targets.SingleOrDefault(t =>
                t.Id.Equals(selected.Id, StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException($"Output '{selected.Id}' was removed on disk. Apply configuration before reloading it.");
            if (!OutputTargetContract.ConnectionEquals(selected, replacement) ||
                selected.DeviceEnabled != replacement.DeviceEnabled ||
                OutputTargetContract.IsEnabled(selected) != OutputTargetContract.IsEnabled(replacement))
                throw new InvalidOperationException($"Output '{selected.Id}' binding or enablement changed on disk. Apply configuration through the established settings/consent route first.");
            operation.Phase("dashboard.validate");
            var board = RuntimeConfigurationValidator.LoadTarget(replacement, applicationRoot);
            var runtimeCommitAttempted = false;
            string? callbackPhase = null;
            try
            {
                operation.Phase("outputs.reload");
                sessions.ReloadTarget(replacement, board, next =>
                {
                    callbackPhase = "conflict.inputs";
                    EnsureCurrent(path, original);
                    callbackPhase = "runtime.commit";
                    runtimeCommitAttempted = true;
                    commitRuntime(next);
                    callbackPhase = null;
                }, () =>
                {
                    callbackPhase = "conflict.inputs";
                    EnsureCurrent(path, original);
                    callbackPhase = null;
                });
                operation.Phase("runtime.commit.returned");
            }
            catch (Exception error)
            {
                operation.RecordFailure(error, callbackPhase ?? operation.CurrentPhase, "recoveryRequired=true");
                if (runtimeCommitAttempted)
                {
                    try { operation.Phase("rollback.runtime.commit"); commitRuntime(previous); operation.Phase("rollback.runtime.returned"); }
                    catch (Exception rollbackError)
                    {
                        operation.RecordFailure(rollbackError, "rollback.runtime.commit");
                        throw new AggregateException("Output reload failed; Runtime recovery was incomplete.",
                            error, rollbackError);
                    }
                }
                throw;
            }
            operation.Complete("selectedReloadReturned=true");
            return new RuntimeIpcResponse(true, $"Output '{replacement.Id}' reloaded saved settings and dashboard '{replacement.Dashboard}'.");
        }
        catch (Exception error) { operation.Fail(error); throw; }
    }

    private static ConfigurationDraft PrepareDraft(string path, OutputTargetConfig selected,
        string property, JsonNode value, FileLogger log, LogOperation operation)
    {
        operation.Phase("file.readAndValidate", "property=" + property);
        var original = File.ReadAllBytes(path);
        var disk = AppConfig.Load(path, log);
        operation.Phase("conflict.selectedRecord");
        EnsureCurrent(path, original);
        var diskTarget = disk.Outputs.Targets.SingleOrDefault(t => t.Id.Equals(selected.Id, StringComparison.OrdinalIgnoreCase));
        if (diskTarget is null || !OutputTargetContract.ConnectionEquals(selected, diskTarget))
            throw new InvalidOperationException("The Output binding changed on disk. Reload configuration before changing this Output.");
        var unchanged = property switch
        {
            "Dashboard" => diskTarget.Dashboard == selected.Dashboard,
            "BoardEnabled" => diskTarget.Desktop?.BoardEnabled == selected.Desktop?.BoardEnabled,
            "Lock" => diskTarget.Desktop?.Lock == selected.Desktop?.Lock,
            "DeviceEnabled" => diskTarget.DeviceEnabled == selected.DeviceEnabled,
            _ => false
        };
        if (!unchanged)
            throw new InvalidOperationException($"Output '{selected.Id}' {property} changed on disk. Reload configuration first.");

        using var stream = new MemoryStream(original);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var root = JsonNode.Parse(reader.ReadToEnd(), documentOptions: new JsonDocumentOptions
            { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true }) as JsonObject
            ?? throw new InvalidDataException("Application configuration must contain a JSON object.");
        var targets = Get(Get(root, "Outputs") as JsonObject
            ?? throw new InvalidDataException("Missing Outputs object."), "Targets") as JsonArray
            ?? throw new InvalidDataException("Missing Outputs.Targets array.");
        var matches = targets.OfType<JsonObject>().Where(t =>
            string.Equals(Get(t, "Id")?.GetValue<string>()?.Trim(), selected.Id, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (matches.Length != 1)
            throw new InvalidDataException("Expected exactly one explicitly persisted Output ID.");
        var owner = matches[0];
        if (property is "BoardEnabled" or "Lock")
        {
            var desktop = Get(owner, "Desktop") as JsonObject;
            if (desktop is null)
            {
                desktop = new JsonObject();
                Put(owner, "Desktop", desktop);
            }
            owner = desktop;
        }
        Put(owner, property, value);
        var replacement = Encoding.UTF8.GetBytes(root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine);
        return new ConfigurationDraft(path, original, replacement);
    }

    private static string? Key(JsonObject obj, string name)
    {
        var matches = obj.Select(p => p.Key).Where(k => k.Equals(name, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (matches.Length > 1)
            throw new InvalidDataException($"Ambiguous configuration property '{name}'.");
        return matches.SingleOrDefault();
    }
    private static JsonNode? Get(JsonObject obj, string name) => obj[Key(obj, name) ?? name];
    private static void Put(JsonObject obj, string name, JsonNode value) => obj[Key(obj, name) ?? name] = value;

    private static void EnsureCurrent(string path, byte[] expected)
    {
        if (!File.ReadAllBytes(path).AsSpan().SequenceEqual(expected))
            throw new InvalidOperationException("Application configuration changed during the Output operation; the newer file was retained.");
    }

    private sealed class ConfigurationDraft(string path, byte[] original, byte[] replacement)
    {
        private bool _saved;
        public void Validate() => EnsureCurrent(path, original);
        public void Save()
        {
            Replace(original, replacement);
            _saved = true;
        }
        public void Restore(LogOperation operation)
        {
            operation.Phase("rollback.file", $"fileCommitted={_saved}");
            if (_saved) Replace(replacement, original);
            operation.Phase(_saved ? "rollback.file.restored" : "rollback.file.not-written");
        }
        private void Replace(byte[] expected, byte[] next)
        {
            var temp = path + ".output-" + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllBytes(temp, next);
                EnsureCurrent(path, expected);
                File.Move(temp, path, overwrite: true);
            }
            finally
            {
                if (File.Exists(temp)) File.Delete(temp);
            }
        }
    }
}
