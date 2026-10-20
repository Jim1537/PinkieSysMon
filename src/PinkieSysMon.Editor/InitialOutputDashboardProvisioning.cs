namespace PinkieSysMon.Editor;

// A saved initial dashboard is materialized only as part of Settings OK.
// Incomplete commits may release only files created by this transaction.
internal sealed class InitialOutputDashboardProvisioning : IDisposable
{
    private readonly string _configPath;
    private readonly byte[]? _oldConfig;
    private readonly List<(string Path, byte[] Bytes)> _created = [];
    private bool _committed;

    private InitialOutputDashboardProvisioning(string configPath, byte[]? oldConfig)
    {
        _configPath = configPath;
        _oldConfig = oldConfig;
    }

    internal static InitialOutputDashboardProvisioning Prepare(
        string applicationRoot, string configPath, byte[]? oldConfig,
        IReadOnlySet<string> previousIds, AppConfig updated)
    {
        ArgumentNullException.ThrowIfNull(updated);
        var requests = new Dictionary<string, (int Width, int Height)>(StringComparer.OrdinalIgnoreCase);
        foreach (var target in updated.Outputs.Targets.Where(t => !previousIds.Contains(t.Id)))
        {
            if (target.Kind == OutputTargetContract.DesktopKind)
            {
                if (string.Equals(target.Dashboard, OutputDefaultDashboardPolicy.DesktopName, StringComparison.OrdinalIgnoreCase))
                    requests.TryAdd(OutputDefaultDashboardPolicy.DesktopName, (640, 640));
            }
            else if (OutputTargetContract.IsPhysicalKind(target.Kind))
            {
                var profile = OutputDefaultDashboardPolicy.PhysicalProfile(target);
                if (string.Equals(target.Dashboard, profile.Name, StringComparison.OrdinalIgnoreCase))
                    requests.TryAdd(profile.Name, (profile.Width, profile.Height));
            }
        }

        // An untouched public first-install Desktop is intentionally pending
        // its first board. This is not a user-created missing assignment:
        // successful Settings commit materializes it exactly once.
        var bootstrap = updated.Outputs.Targets.FirstOrDefault(t =>
            IsUnprovisionedPublicDesktop(oldConfig, previousIds, t));
        if (bootstrap is not null)
            requests.TryAdd(OutputDefaultDashboardPolicy.DesktopName, (640, 640));

        var pending = new InitialOutputDashboardProvisioning(configPath, oldConfig);
        if (requests.Count == 0)
            return pending;

        // A new request never silently repairs an older explicit bad reference.
        foreach (var old in updated.Outputs.Targets.Where(t =>
                     previousIds.Contains(t.Id) && !ReferenceEquals(t, bootstrap)))
            _ = RuntimeConfigurationValidator.LoadTarget(old, applicationRoot);

        try
        {
            foreach (var (name, size) in requests)
                pending.EnsureBoard(applicationRoot, name, size.Width, size.Height);
            return pending;
        }
        catch
        {
            pending.Dispose();
            throw;
        }
    }

    private static bool IsUnprovisionedPublicDesktop(
        byte[]? oldConfig, IReadOnlySet<string> originalIds, OutputTargetConfig target)
    {
        if (originalIds.Count != 1 || !originalIds.Contains("desktop-1") ||
            !string.Equals(target.Id, "desktop-1", StringComparison.OrdinalIgnoreCase) ||
            target.Kind != OutputTargetContract.DesktopKind ||
            !string.Equals(target.Dashboard, OutputDefaultDashboardPolicy.DesktopName,
                StringComparison.OrdinalIgnoreCase))
            return false;
        if (oldConfig is null)
            return true; // Missing config: AppConfig.Load constructed the same safe defaults.

        try
        {
            ReadOnlySpan<byte> json = oldConfig;
            if (json.StartsWith(new byte[] { 0xEF, 0xBB, 0xBF }))
                json = json[3..];
            using var document = System.Text.Json.JsonDocument.Parse(json.ToArray());
            var root = document.RootElement;
            if (root.ValueKind != System.Text.Json.JsonValueKind.Object ||
                root.TryGetProperty("Dashboard", out _) ||
                root.TryGetProperty("Runtime", out _))
                return false; // Normalized saved/user config is never treated as untouched stock.
            if (!root.TryGetProperty("Outputs", out var outputs))
                return !root.EnumerateObject().Any(); // A literal empty initial config.
            if (!outputs.TryGetProperty("Targets", out var targets) ||
                targets.ValueKind != System.Text.Json.JsonValueKind.Array ||
                targets.GetArrayLength() != 1)
                return false;
            var first = targets[0];
            return first.GetProperty("Id").GetString() == "desktop-1" &&
                   first.GetProperty("Name").GetString() == "Desktop 1" &&
                   first.GetProperty("Kind").GetString() == OutputTargetContract.DesktopKind &&
                   first.GetProperty("Dashboard").GetString() == OutputDefaultDashboardPolicy.DesktopName &&
                   first.GetProperty("Desktop").GetProperty("BoardEnabled").GetBoolean() == false;
        }
        catch (System.Text.Json.JsonException) { return false; }
        catch (InvalidOperationException) { return false; }
        catch (KeyNotFoundException) { return false; }
    }

    private void EnsureBoard(string root, string name, int width, int height)
    {
        var folder = Path.Combine(DashboardCatalog.GetRoot(root), name);
        var path = Path.Combine(folder, "dashboard.json");
        if (Directory.Exists(folder))
        {
            if (!File.Exists(path))
                throw new InvalidDataException($"Dashboard '{name}' already has a folder without dashboard.json.");
            try { _ = global::PinkieSysMon.DashboardModel.DashboardDefinition.Load(path); }
            catch (Exception e) when (e is IOException or InvalidDataException or System.Text.Json.JsonException
                                      or UnauthorizedAccessException)
            {
                throw new InvalidDataException($"Cannot reuse dashboard '{name}': {e.Message}", e);
            }
            return;
        }
        try
        {
            EditorForm.WriteNewDashboardFile(folder, width, height, 0);
            _created.Add((path, File.ReadAllBytes(path)));
        }
        catch
        {
            if (Directory.Exists(folder) && !Directory.EnumerateFileSystemEntries(folder).Any())
                Directory.Delete(folder);
            throw;
        }
    }

    internal void Commit() => _committed = true;

    public void Dispose()
    {
        if (_committed || _created.Count == 0)
            return;
        try
        {
            var configUnchanged = _oldConfig is null
                ? !File.Exists(_configPath)
                : File.Exists(_configPath) &&
                  File.ReadAllBytes(_configPath).AsSpan().SequenceEqual(_oldConfig);
            if (!configUnchanged) return;
            foreach (var (path, bytes) in _created)
            {
                try
                {
                    var folder = Path.GetDirectoryName(path)!;
                    if (!File.Exists(path) ||
                        !File.ReadAllBytes(path).AsSpan().SequenceEqual(bytes) ||
                        Directory.EnumerateFileSystemEntries(folder).Count() != 1)
                        continue;
                    File.Delete(path);
                    Directory.Delete(folder);
                }
                catch (IOException) { /* Retain uncertain user data. */ }
                catch (UnauthorizedAccessException) { /* Do not mask original error. */ }
            }
        }
        catch (IOException) { /* Preserve uncertain data and the original failure. */ }
        catch (UnauthorizedAccessException) { /* Preserve uncertain data. */ }
    }
}
