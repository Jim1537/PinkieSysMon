using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace PinkieSysMon;

// Called on Runtime's UI thread. Neither the client nor persistence owns live sessions.
internal sealed class RuntimeOutputConfigurationControl(
    OutputSessionManager sessions, Func<AppConfig> currentConfig, string applicationRoot,
    FileLogger log, Action<AppConfig> commitRuntime)
{
    public RuntimeIpcResponse Execute(RuntimeIpcCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (command.Kind == RuntimeIpcCommandKind.StartOrReloadTarget)
            return Reload(command.TargetId!);
        var previous = currentConfig();
        var selected = previous.Outputs.Targets.SingleOrDefault(t =>
            t.Id.Equals(command.TargetId, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Output '{command.TargetId}' is not configured.");
        var replacement = JsonSerializer.Deserialize<OutputTargetConfig>(JsonSerializer.Serialize(selected))!;
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
        var draft = PrepareDraft(path, selected, property, value, log);
        // Assignment and an explicit enable validate only this target's on-disk board.
        // Lock/hide retain its already committed document, even if its file changed.
        var board = command.Kind == RuntimeIpcCommandKind.AssignDashboard || reloadBoard
            ? RuntimeConfigurationValidator.LoadTarget(replacement, applicationRoot)
            : sessions.GetDashboardAssignments().GetValueOrDefault(selected.Id);
        var runtimeCommitAttempted = false;
        try
        {
            sessions.ReconfigureTarget(replacement, board, next =>
            {
                draft.Save();
                runtimeCommitAttempted = true;
                commitRuntime(next);
            });
        }
        catch (Exception error)
        {
            var failures = new List<Exception>();
            try { draft.Restore(); }
            catch (Exception rollbackError) { failures.Add(rollbackError); }
            if (runtimeCommitAttempted)
            {
                try { commitRuntime(previous); }
                catch (Exception rollbackError) { failures.Add(rollbackError); }
            }
            if (failures.Count > 0)
                throw new AggregateException("Output setting failed; rollback was incomplete.",
                    [error, .. failures]);
            throw;
        }
        return new RuntimeIpcResponse(true, message);
    }

    private RuntimeIpcResponse Reload(string targetId)
    {
        sessions.EnsureCanReloadTarget(targetId);
        var previous = currentConfig();
        var selected = previous.Outputs.Targets.Single(t =>
            t.Id.Equals(targetId, StringComparison.OrdinalIgnoreCase));
        var path = Path.Combine(applicationRoot, "config", "app.json");
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
        var board = RuntimeConfigurationValidator.LoadTarget(replacement, applicationRoot);
        var runtimeCommitAttempted = false;
        try
        {
            sessions.ReloadTarget(replacement, board, next =>
            {
                EnsureCurrent(path, original);
                runtimeCommitAttempted = true;
                commitRuntime(next);
            }, () => EnsureCurrent(path, original));
        }
        catch (Exception error)
        {
            if (runtimeCommitAttempted)
            {
                try { commitRuntime(previous); }
                catch (Exception rollbackError)
                {
                    throw new AggregateException("Output reload failed; Runtime recovery was incomplete.",
                        error, rollbackError);
                }
            }
            throw;
        }
        return new RuntimeIpcResponse(true, $"Output '{replacement.Id}' reloaded saved settings and dashboard '{replacement.Dashboard}'.");
    }

    private static ConfigurationDraft PrepareDraft(string path, OutputTargetConfig selected,
        string property, JsonNode value, FileLogger log)
    {
        var original = File.ReadAllBytes(path);
        var disk = AppConfig.Load(path, log);
        EnsureCurrent(path, original);
        var diskTarget = disk.Outputs.Targets.SingleOrDefault(t => t.Id.Equals(selected.Id, StringComparison.OrdinalIgnoreCase));
        if (diskTarget is null || !OutputTargetContract.ConnectionEquals(selected, diskTarget))
            throw new InvalidOperationException("The Output binding changed on disk. Reload configuration before changing this Output.");
        var unchanged = property switch
        {
            "Dashboard" => diskTarget.Dashboard == selected.Dashboard,
            "BoardEnabled" => diskTarget.Desktop?.BoardEnabled == selected.Desktop?.BoardEnabled,
            "Lock" => diskTarget.Desktop?.Lock == selected.Desktop?.Lock,
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
        if (property != "Dashboard")
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
        public void Save()
        {
            Replace(original, replacement);
            _saved = true;
        }
        public void Restore()
        {
            if (_saved) Replace(replacement, original);
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
