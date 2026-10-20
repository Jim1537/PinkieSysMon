namespace PinkieSysMon.Editor;

// A newly added Desktop Output may receive one ordinary saved board. The
// pending file belongs to the Settings OK transaction, never to enumeration.
internal sealed class InitialDesktopDashboardProvisioning : IDisposable
{
    internal const string DashboardName = "desktop_default";
    private readonly string _configPath;
    private readonly byte[]? _originalConfig;
    private readonly string? _newFile;
    private readonly byte[]? _generatedBytes;
    private bool _committed;

    private InitialDesktopDashboardProvisioning(
        string configPath, byte[]? originalConfig, string? newFile = null, byte[]? generatedBytes = null)
    {
        _configPath = configPath;
        _originalConfig = originalConfig;
        _newFile = newFile;
        _generatedBytes = generatedBytes;
    }

    internal static InitialDesktopDashboardProvisioning Prepare(
        string applicationRoot, string configPath, byte[]? originalConfig,
        IReadOnlySet<string> originalOutputIds, AppConfig updated)
    {
        ArgumentNullException.ThrowIfNull(updated);
        var needsDefault = updated.Outputs.Targets.Any(target =>
            target.Kind == OutputTargetContract.DesktopKind &&
            !originalOutputIds.Contains(target.Id) &&
            string.Equals(target.Dashboard, DashboardName, StringComparison.OrdinalIgnoreCase));
        if (!needsDefault)
            return new InitialDesktopDashboardProvisioning(configPath, originalConfig);

        // A preexisting Output with an invalid explicit assignment must stay
        // invalid, even if a newly added Output requests the same name.
        foreach (var target in updated.Outputs.Targets.Where(t => originalOutputIds.Contains(t.Id)))
            _ = RuntimeConfigurationValidator.LoadTarget(target, applicationRoot);

        var directory = Path.Combine(DashboardCatalog.GetRoot(applicationRoot), DashboardName);
        var path = Path.Combine(directory, "dashboard.json");
        if (Directory.Exists(directory))
        {
            if (!File.Exists(path))
                throw new InvalidDataException(
                    $"Dashboard '{DashboardName}' already has a directory without dashboard.json.");
            try
            {
                _ = global::PinkieSysMon.DashboardModel.DashboardDefinition.Load(path);
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or System.Text.Json.JsonException)
            {
                throw new InvalidDataException(
                    $"Cannot reuse existing dashboard '{DashboardName}': {ex.Message}", ex);
            }
            return new InitialDesktopDashboardProvisioning(configPath, originalConfig);
        }

        try
        {
            EditorForm.WriteNewDashboardFile(directory, 640, 640, 0);
            return new InitialDesktopDashboardProvisioning(
                configPath, originalConfig, path, File.ReadAllBytes(path));
        }
        catch
        {
            RemoveEmptyDirectory(directory);
            throw;
        }
    }

    internal void Commit() => _committed = true;

    public void Dispose()
    {
        if (_committed || _newFile is null || _generatedBytes is null)
            return;
        try
        {
            // A newer config writer or dashboard editor owns its changes:
            // never delete a different file or a board in use by new config.
            var unchangedConfig = _originalConfig is null
                ? !File.Exists(_configPath)
                : File.Exists(_configPath) &&
                  File.ReadAllBytes(_configPath).AsSpan().SequenceEqual(_originalConfig);
            var directory = Path.GetDirectoryName(_newFile)!;
            if (!unchangedConfig || !File.Exists(_newFile) ||
                !File.ReadAllBytes(_newFile).AsSpan().SequenceEqual(_generatedBytes) ||
                Directory.EnumerateFileSystemEntries(directory).Count() != 1)
                return;
            File.Delete(_newFile);
            RemoveEmptyDirectory(directory);
        }
        catch (IOException) { /* Never mask the original Settings failure. */ }
        catch (UnauthorizedAccessException) { /* Preserve uncertain user data. */ }
    }

    private static void RemoveEmptyDirectory(string directory)
    {
        if (Directory.Exists(directory) && !Directory.EnumerateFileSystemEntries(directory).Any())
            Directory.Delete(directory);
    }
}
