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

        var pending = new InitialOutputDashboardProvisioning(configPath, oldConfig);
        if (requests.Count == 0)
            return pending;

        // A new request never silently repairs an older explicit bad reference.
        foreach (var old in updated.Outputs.Targets.Where(t => previousIds.Contains(t.Id)))
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
