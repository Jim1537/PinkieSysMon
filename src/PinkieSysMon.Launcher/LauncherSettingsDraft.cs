using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using PinkieSysMon;
using PinkieSysMon.Editor;

namespace PinkieSysMon.Launcher;

// Detached, typed values: no Runtime assembly, telemetry engine, hardware or
// dashboard document is constructed by the standalone Settings host.
internal sealed class LauncherSettingsValues
{
    public bool StartRuntimeWithWindows { get; set; }
    public int RuntimeIdleTimeoutMinutes { get; set; }
    public int LogRetentionDays { get; set; } = 3;
    public int RasterSharpness { get; set; }
    public bool SystemProviderEnabled { get; set; } = true;
    public bool LhmProviderEnabled { get; set; }
    public bool IcueProviderEnabled { get; set; }
    public bool FilesystemProviderEnabled { get; set; }
    public List<FilesystemSourceConfig> FilesystemSources { get; set; } = [];
    public int ToolbarIconSize { get; set; } = 32;
    public string Theme { get; set; } = EditorThemeContract.SystemSetting;

    internal LauncherSettingsValues Clone() => new()
    {
        StartRuntimeWithWindows = StartRuntimeWithWindows,
        RuntimeIdleTimeoutMinutes = RuntimeIdleTimeoutMinutes,
        LogRetentionDays = LogRetentionDays,
        RasterSharpness = RasterSharpness,
        SystemProviderEnabled = SystemProviderEnabled,
        LhmProviderEnabled = LhmProviderEnabled,
        IcueProviderEnabled = IcueProviderEnabled,
        FilesystemProviderEnabled = FilesystemProviderEnabled,
        FilesystemSources = FilesystemSources.Select(source => source.Copy()).ToList(),
        ToolbarIconSize = ToolbarIconSize,
        Theme = Theme
    };
}

internal sealed class LauncherSettingsDraft
{
    internal string Root { get; }
    internal string AppPath { get; }
    internal string EditorPath { get; }
    internal byte[] OriginalAppBytes { get; }
    internal byte[]? OriginalEditorBytes { get; }
    internal string? OriginalStartupCommand { get; }
    internal LauncherSettingsValues Values { get; }
    private readonly LauncherSettingsValues _original;

    private LauncherSettingsDraft(string root, byte[] appBytes, byte[]? editorBytes,
        string? startupCommand, LauncherSettingsValues initial)
    {
        Root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        AppPath = Path.Combine(Root, "config", "app.json");
        EditorPath = Path.Combine(Root, "config", "editor.json");
        OriginalAppBytes = appBytes;
        OriginalEditorBytes = editorBytes;
        OriginalStartupCommand = startupCommand;
        _original = initial.Clone();
        Values = initial.Clone();
    }

    internal static LauncherSettingsDraft Open(string root, Func<string, bool>? readStartup = null)
    {
        var normalized = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        var appPath = Path.Combine(normalized, "config", "app.json");
        var editorPath = Path.Combine(normalized, "config", "editor.json");
        if (!File.Exists(appPath))
            throw new FileNotFoundException("Application configuration is missing; Settings cannot edit it.", appPath);

        var appBytes = File.ReadAllBytes(appPath);
        var editorBytes = File.Exists(editorPath) ? File.ReadAllBytes(editorPath) : null;
        var app = ParseApp(appBytes);
        var preferences = EditorSettings.LoadForWrite(editorPath);
        var startupCommand = readStartup is null ? StartupTaskManager.ReadCurrentCommand() : null;
        var runtimePath = Path.Combine(normalized, "bin", "PinkieSysMon.exe");
        if (startupCommand is not null &&
            !StartupTaskManager.IsOwnedCommand(startupCommand, runtimePath))
            throw new InvalidOperationException("Windows Runtime autostart belongs to another installation.");
        var values = new LauncherSettingsValues
        {
            RuntimeIdleTimeoutMinutes = ReadInt(app, "Runtime", "IdleTimeoutMinutes", 0),
            LogRetentionDays = ReadInt(app, "Runtime", "LogRetentionDays", 3),
            RasterSharpness = ReadInt(app, "Renderer", "RasterSharpness", 0),
            SystemProviderEnabled = ReadBool(app, "MetricProviders", "system", true),
            LhmProviderEnabled = ReadBool(app, "MetricProviders", "lhm", false),
            IcueProviderEnabled = ReadBool(app, "MetricProviders", "icue", false),
            FilesystemProviderEnabled = ReadBool(app, "MetricProviders", "filesystem", false),
            FilesystemSources = ReadSources(app),
            ToolbarIconSize = preferences.ToolbarIconSize,
            Theme = preferences.Theme,
            StartRuntimeWithWindows = readStartup is null
                ? !string.IsNullOrWhiteSpace(startupCommand)
                : readStartup(runtimePath)
        };
        Validate(values);
        return new LauncherSettingsDraft(normalized, appBytes, editorBytes, startupCommand, values);
    }

    internal bool AppChanged =>
        Values.RuntimeIdleTimeoutMinutes != _original.RuntimeIdleTimeoutMinutes ||
        Values.LogRetentionDays != _original.LogRetentionDays ||
        Values.RasterSharpness != _original.RasterSharpness ||
        Values.SystemProviderEnabled != _original.SystemProviderEnabled ||
        Values.LhmProviderEnabled != _original.LhmProviderEnabled ||
        Values.IcueProviderEnabled != _original.IcueProviderEnabled ||
        Values.FilesystemProviderEnabled != _original.FilesystemProviderEnabled ||
        !FilesystemSourceContract.SameSources(Values.FilesystemSources, _original.FilesystemSources);

    internal bool EditorChanged => Values.ToolbarIconSize != _original.ToolbarIconSize ||
        Values.Theme != _original.Theme;
    internal bool StartupChanged => Values.StartRuntimeWithWindows != _original.StartRuntimeWithWindows;
    internal bool HasChanges => AppChanged || EditorChanged || StartupChanged;

    internal void Validate() => Validate(Values);

    internal static void Validate(LauncherSettingsValues values)
    {
        if (values.RuntimeIdleTimeoutMinutes is < 0 or > 1440 ||
            values.LogRetentionDays is < 1 or > 365 ||
            values.RasterSharpness is < 0 or > 100 ||
            values.ToolbarIconSize is not (16 or 32 or 48) ||
            values.Theme is not (EditorThemeContract.SystemSetting or EditorThemeContract.LightSetting or EditorThemeContract.DarkSetting))
            throw new InvalidDataException("Settings contains an out-of-range value.");
        FilesystemSourceContract.Validate(new FilesystemConfig
        {
            Sources = values.FilesystemSources.Select(source => source.Copy()).ToList()
        });
    }

    // Preserve every unrelated app.json node, including Output binding identities,
    // media and USB settings. Apply only fields changed by the current draft.
    internal byte[] PrepareAppBytes()
    {
        Validate();
        if (!AppChanged) return OriginalAppBytes.ToArray();

        var app = ParseApp(OriginalAppBytes);
        if (Values.RuntimeIdleTimeoutMinutes != _original.RuntimeIdleTimeoutMinutes)
            Set(EnsureObject(app, "Runtime"), "IdleTimeoutMinutes", Values.RuntimeIdleTimeoutMinutes);
        if (Values.LogRetentionDays != _original.LogRetentionDays)
            Set(EnsureObject(app, "Runtime"), "LogRetentionDays", Values.LogRetentionDays);
        if (Values.RasterSharpness != _original.RasterSharpness)
            Set(EnsureObject(app, "Renderer"), "RasterSharpness", Values.RasterSharpness);
        var providers = EnsureObject(app, "MetricProviders");
        if (Values.SystemProviderEnabled != _original.SystemProviderEnabled)
            Set(providers, "system", Values.SystemProviderEnabled);
        if (Values.LhmProviderEnabled != _original.LhmProviderEnabled)
            Set(providers, "lhm", Values.LhmProviderEnabled);
        if (Values.IcueProviderEnabled != _original.IcueProviderEnabled)
            Set(providers, "icue", Values.IcueProviderEnabled);
        if (Values.FilesystemProviderEnabled != _original.FilesystemProviderEnabled)
            Set(providers, "filesystem", Values.FilesystemProviderEnabled);
        if (!FilesystemSourceContract.SameSources(Values.FilesystemSources, _original.FilesystemSources))
            Set(EnsureObject(app, "Filesystem"), "Sources",
                JsonSerializer.SerializeToNode(Values.FilesystemSources));
        return Encoding.UTF8.GetBytes(app.ToJsonString(new JsonSerializerOptions
        { WriteIndented = true }) + Environment.NewLine);
    }

    internal void EnsureInputUnchanged()
    {
        if (!Matches(AppPath, OriginalAppBytes) || !Matches(EditorPath, OriginalEditorBytes))
            throw new InvalidOperationException(
                "Settings input changed after the draft was opened. No newer configuration was overwritten.");
    }

    // Closing a verified Editor legitimately saves fresh per-dashboard view
    // state. Accept only those changes while rejecting external app-wide edits.
    internal void EnsureSharedPreferencesUnchanged()
    {
        if (!Matches(AppPath, OriginalAppBytes))
            throw new InvalidOperationException("Application configuration changed during Editor close.");
        var latest = EditorSettings.LoadForWrite(EditorPath);
        if (latest.ToolbarIconSize != _original.ToolbarIconSize ||
            !string.Equals(latest.Theme, _original.Theme, StringComparison.Ordinal))
            throw new InvalidOperationException(
                "Shared Editor interface settings changed while Settings was open.");
        if (OriginalStartupCommand != StartupTaskManager.ReadCurrentCommand())
            throw new InvalidOperationException(
                "Windows Runtime startup entry changed while Settings was open.");
    }

    // App files and Editor preferences are persisted separately by their
    // respective established writers; Runtime owns live apply/recovery.
    internal void WriteAppIfUnchanged(byte[] prepared)
    {
        if (!AppChanged) return;
        WriteCompared(AppPath, OriginalAppBytes, prepared);
    }

    internal static void WriteCompared(string path, byte[]? expected, byte[] updated)
    {
        if (!Matches(path, expected))
            throw new InvalidOperationException("Settings file changed before commit. Newer contents were preserved.");
        var temp = path + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            File.WriteAllBytes(temp, updated);
            if (!Matches(path, expected))
                throw new InvalidOperationException("Settings file changed during commit. Newer contents were preserved.");
            if (expected is null) File.Move(temp, path);
            else File.Move(temp, path, overwrite: true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }

    internal static bool RestoreIfUnchanged(string path, byte[] written, byte[]? previous)
    {
        if (!Matches(path, written)) return false;
        if (previous is null) File.Delete(path);
        else WriteCompared(path, written, previous);
        return true;
    }

    internal static bool Matches(string path, byte[]? expected) =>
        expected is null ? !File.Exists(path) :
        File.Exists(path) && File.ReadAllBytes(path).AsSpan().SequenceEqual(expected);

    private static JsonObject ParseApp(byte[] bytes)
    {
        try
        {
            var text = Encoding.UTF8.GetString(bytes).TrimStart('\uFEFF');
            return JsonNode.Parse(text, documentOptions: new JsonDocumentOptions
            {
                AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip
            }) as JsonObject ?? throw new InvalidDataException("Application configuration requires a JSON object.");
        }
        catch (JsonException error)
        {
            throw new InvalidDataException("Application configuration contains malformed JSON. No changes were made.", error);
        }
    }

    private static string? Key(JsonObject obj, string name)
    {
        var keys = obj.Select(pair => pair.Key)
            .Where(key => key.Equals(name, StringComparison.OrdinalIgnoreCase)).Take(2).ToArray();
        if (keys.Length > 1)
            throw new InvalidDataException("Configuration contains ambiguous property names: " + name);
        return keys.Length == 0 ? null : keys[0];
    }

    private static JsonObject? GetObject(JsonObject root, string name)
    {
        var key = Key(root, name);
        if (key is null) return null;
        return root[key] as JsonObject ??
            throw new InvalidDataException("Configuration section is not an object: " + name);
    }

    private static JsonObject EnsureObject(JsonObject root, string name)
    {
        var existing = GetObject(root, name);
        if (existing is not null) return existing;
        var result = new JsonObject();
        root.Add(name, result);
        return result;
    }

    private static JsonNode? Get(JsonObject obj, string name)
    {
        var key = Key(obj, name);
        return key is null ? null : obj[key];
    }

    private static int ReadInt(JsonObject root, string section, string name, int fallback)
    {
        var obj = GetObject(root, section);
        var node = obj is null ? null : Get(obj, name);
        if (node is null) return fallback;
        try { return node.GetValue<int>(); }
        catch (Exception error) when (error is FormatException or InvalidOperationException)
        { throw new InvalidDataException("Invalid numeric Settings value: " + section + "." + name, error); }
    }

    private static bool ReadBool(JsonObject root, string section, string name, bool fallback)
    {
        var obj = GetObject(root, section);
        var node = obj is null ? null : Get(obj, name);
        if (node is null) return fallback;
        try { return node.GetValue<bool>(); }
        catch (Exception error) when (error is FormatException or InvalidOperationException)
        { throw new InvalidDataException("Invalid boolean Settings value: " + section + "." + name, error); }
    }

    private static List<FilesystemSourceConfig> ReadSources(JsonObject root)
    {
        var fileSystem = GetObject(root, "Filesystem");
        var node = fileSystem is null ? null : Get(fileSystem, "Sources");
        if (node is null) return [];
        try
        {
            var sources = JsonSerializer.Deserialize<List<FilesystemSourceConfig>>(node.ToJsonString(),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                ?? throw new InvalidDataException("Filesystem.Sources cannot be null.");
            FilesystemSourceContract.Validate(new FilesystemConfig { Sources = sources });
            return sources;
        }
        catch (JsonException error)
        {
            throw new InvalidDataException("Filesystem.Sources cannot be decoded.", error);
        }
    }

    private static void Set(JsonObject obj, string name, JsonNode? value)
    {
        var key = Key(obj, name) ?? name;
        obj[key] = value;
    }

    private static void Set(JsonObject obj, string name, int value) => Set(obj, name, JsonValue.Create(value));
    private static void Set(JsonObject obj, string name, bool value) => Set(obj, name, JsonValue.Create(value));
}
