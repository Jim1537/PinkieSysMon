using System.Text.Json;

namespace PinkieSysMon;

internal sealed class AppConfig
{
    public DashboardConfig Dashboard { get; set; } = new();
    public DisplayConfig Display { get; set; } = new();
    public OutputConfig Outputs { get; set; } = new();
    public RendererConfig Renderer { get; set; } = new();
    public UsbConfig Usb { get; set; } = new();
    public MediaConfig Media { get; set; } = new();
    public Dictionary<string, bool> MetricProviders { get; set; } = MetricProviderContract.CreateDefaults();

    public static AppConfig Load(string path, FileLogger log)
    {
        if (!File.Exists(path))
        {
            log.Info("Config file not found; using defaults.");
            var defaults = new AppConfig();
            defaults.Validate();
            return defaults;
        }

        var json = File.ReadAllText(path);
        AppConfig config;
        try
        {
            config = JsonSerializer.Deserialize<AppConfig>(json, JsonOptions())
                ?? throw new InvalidDataException($"Application config '{path}' must contain a JSON object.");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"Application config '{path}' contains invalid JSON: {ex.Message}", ex);
        }

        config.Validate();
        return config;
    }

    public void Save(string path)
    {
        Validate();

        var directory = Path.GetDirectoryName(Path.GetFullPath(path))
            ?? throw new InvalidDataException("Application config path has no parent directory.");
        Directory.CreateDirectory(directory);

        var options = JsonOptions();
        options.WriteIndented = true;
        var json = JsonSerializer.Serialize(this, options) + Environment.NewLine;

        var temp = path + ".tmp";
        File.WriteAllText(temp, json);
        File.Move(temp, path, true);
    }

    private static JsonSerializerOptions JsonOptions() => new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private void Validate()
    {
        if (Dashboard is null || Display is null || Outputs is null ||
            Renderer is null || Usb is null || Media is null)
            throw new InvalidDataException("Application config requires Dashboard, Display, Outputs, Renderer, Usb and Media objects.");
        if (Outputs.Targets is null)
            throw new InvalidDataException("Outputs.Targets must be an array of configured outputs.");

        if (string.IsNullOrWhiteSpace(Dashboard.Active))
            throw new InvalidDataException("Dashboard.Active must not be empty.");

        if (Display.RefreshIntervalMs < 10)
            throw new InvalidDataException("Display.RefreshIntervalMs must be at least 10 ms.");

        OutputTargetContract.ValidateAndNormalize(
            Outputs.Targets, Dashboard.Active, Display.RefreshIntervalMs,
            Renderer.JpegQuality, Usb);

        if (Renderer.JpegQuality is < 10 or > 100)
            throw new InvalidDataException("Renderer.JpegQuality must be in the range 10..100.");

        if (Usb.RetryIntervalMs < 500)
            throw new InvalidDataException("Usb.RetryIntervalMs must be at least 500 ms.");
        if (Usb.TransferTimeoutMs < 500)
            throw new InvalidDataException("Usb.TransferTimeoutMs must be at least 500 ms.");

        MetricProviders = MetricProviderContract.Normalize(MetricProviders);

        Media.EndpointTypeOverrides ??= new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in Media.EndpointTypeOverrides)
        {
            if (string.IsNullOrWhiteSpace(pair.Key))
                throw new InvalidDataException("Media.EndpointTypeOverrides contains an empty endpoint ID.");
            if (!MediaMetricContract.EndpointTypes.Contains(pair.Value, StringComparer.OrdinalIgnoreCase))
                throw new InvalidDataException($"Invalid media endpoint override type '{pair.Value}'.");
        }
    }
}

internal sealed class DashboardConfig
{
    public string Active { get; set; } = "Default";
}

internal sealed class DisplayConfig
{
    public int RefreshIntervalMs { get; set; } = 1000;
}

internal sealed class OutputConfig
{
    public List<OutputTargetConfig> Targets { get; set; } = OutputTargetContract.CreateDefaults();
}

internal sealed class OutputTargetConfig
{
    public string Id { get; set; } = OutputTargetContract.DefaultTargetId;
    public string Name { get; set; } = OutputTargetContract.DefaultTargetName;
    public string Kind { get; set; } = OutputTargetContract.TrofeoKind;
    public string Transport { get; set; } = OutputTargetContract.TrofeoTransport;
    public string DeviceId { get; set; } = string.Empty;

    // Null values on old app.json files inherit the previous global settings.
    public string? Dashboard { get; set; }
    public int? RefreshIntervalMs { get; set; }
    public bool ProviderEnabled { get; set; }
    public bool DeviceEnabled { get; set; }
    public int? JpegQuality { get; set; }
    public UsbConfig? Usb { get; set; }
    public DesktopOutputConfig? Desktop { get; set; }
}

internal sealed class DesktopOutputConfig
{
    public bool BoardEnabled { get; set; } = true;
    public bool Lock { get; set; } = true;
    public int X { get; set; }
    public int Y { get; set; }
    public int ZOrder { get; set; }
}

internal static class OutputTargetContract
{
    public const string TrofeoKind = "trofeo";
    public const string DesktopKind = "desktop";
    public const string TrofeoTransport = "trofeo";
    public const string DesktopTransport = "desktop";
    public const string DefaultTargetId = "screen-1";
    public const string DefaultTargetName = "Thermalright Trofeo Vision 9.16";

    // A new installation has a virtual output, not an implicitly enabled USB device.
    // Explicit older Trofeo entries remain readable, but DeviceEnabled is opt-in.
    public static List<OutputTargetConfig> CreateDefaults() =>
    [
        new OutputTargetConfig
        {
            Id = "desktop-1",
            Name = "Desktop 1",
            Kind = DesktopKind,
            Transport = DesktopTransport,
            Desktop = new DesktopOutputConfig()
        }
    ];

    public static OutputTargetConfig GetPrimary(AppConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        if (config.Outputs.Targets.Count == 0)
            throw new InvalidDataException("Outputs.Targets must contain at least one target.");
        return config.Outputs.Targets[0];
    }

    public static bool ConnectionEquals(OutputTargetConfig left, OutputTargetConfig right) =>
        left.Kind.Equals(right.Kind, StringComparison.OrdinalIgnoreCase) &&
        left.Transport.Equals(right.Transport, StringComparison.OrdinalIgnoreCase) &&
        left.DeviceId.Equals(right.DeviceId, StringComparison.OrdinalIgnoreCase);

    // One logical setting backed by the existing kind-specific configuration fields.
    // Provider publication and observed session state do not grant output ownership.
    public static bool IsEnabled(OutputTargetConfig target)
    {
        ArgumentNullException.ThrowIfNull(target);
        return target.Kind switch
        {
            DesktopKind => target.Desktop?.BoardEnabled == true,
            TrofeoKind => target.DeviceEnabled,
            _ => false
        };
    }

    public static void ValidateAndNormalize(
        List<OutputTargetConfig> targets,
        string legacyDashboard = "Default",
        int legacyRefreshIntervalMs = 1000,
        int legacyJpegQuality = 92,
        UsbConfig? legacyUsb = null)
    {
        ArgumentNullException.ThrowIfNull(targets);
        if (targets.Count == 0)
            throw new InvalidDataException("Outputs.Targets must contain at least one target.");

        var physicalCount = targets.Count(t =>
            t is not null && string.Equals(t.Kind, TrofeoKind, StringComparison.OrdinalIgnoreCase));
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var explicitBindings = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var target in targets)
        {
            if (target is null)
                throw new InvalidDataException("Outputs.Targets contains a null entry.");

            target.Id = target.Id?.Trim() ?? string.Empty;
            target.Name = target.Name?.Trim() ?? string.Empty;
            target.Kind = target.Kind?.Trim().ToLowerInvariant() ?? string.Empty;
            target.Transport = target.Transport?.Trim().ToLowerInvariant() ?? string.Empty;
            target.DeviceId = target.DeviceId?.Trim() ?? string.Empty;

            if (target.Id.Length == 0)
                throw new InvalidDataException("Output target Id must not be empty.");
            if (!ids.Add(target.Id))
                throw new InvalidDataException($"Duplicate output target Id '{target.Id}'.");
            if (target.Name.Length == 0)
                throw new InvalidDataException($"Output target '{target.Id}' Name must not be empty.");
            if (target.Kind is not (TrofeoKind or DesktopKind))
                throw new InvalidDataException($"Unsupported output Kind '{target.Kind}'.");

            target.Dashboard ??= legacyDashboard;
            target.Dashboard = target.Dashboard.Trim();
            if (!WindowsPathComponentValidator.IsSafe(target.Dashboard))
                throw new InvalidDataException($"Output target '{target.Id}' has invalid Dashboard reference '{target.Dashboard}'.");

            target.RefreshIntervalMs ??= legacyRefreshIntervalMs;
            if (target.RefreshIntervalMs < 10)
                throw new InvalidDataException($"Output target '{target.Id}' RefreshIntervalMs must be at least 10 ms.");

            if (target.Kind == DesktopKind)
            {
                // The legacy transport initializer is 'trofeo' even when Kind is Desktop.
                if (target.Transport is not (TrofeoTransport or DesktopTransport))
                    throw new InvalidDataException($"Desktop output '{target.Id}' has unsupported transport '{target.Transport}'.");
                if (target.DeviceId.Length != 0 || target.DeviceEnabled ||
                    target.JpegQuality is not null || target.Usb is not null)
                    throw new InvalidDataException($"Desktop output '{target.Id}' must not own device/USB/JPEG configuration.");

                target.Transport = DesktopTransport;
                target.Desktop ??= new DesktopOutputConfig();
                continue;
            }

            if (target.Transport != TrofeoTransport)
                throw new InvalidDataException($"Output target '{target.Id}' uses unsupported transport '{target.Transport}'.");
            if (target.Desktop is not null)
                throw new InvalidDataException($"Trofeo output '{target.Id}' must not contain Desktop settings.");
            if (target.DeviceId.Length > 0 &&
                (target.DeviceId.Length != 71 ||
                 !target.DeviceId.StartsWith("trofeo:", StringComparison.OrdinalIgnoreCase) ||
                 !target.DeviceId.AsSpan(7).ToString().All(Uri.IsHexDigit)))
                throw new InvalidDataException(
                    $"Output target '{target.Id}' has an invalid Trofeo DeviceId (expected trofeo: followed by 64 hex digits).");

            if (physicalCount > 1 && target.DeviceId.Length == 0)
                throw new InvalidDataException(
                    $"Output target '{target.Id}' must have an explicit DeviceId when multiple physical targets are configured.");

            if (target.DeviceId.Length > 0 &&
                !explicitBindings.Add($"{target.Transport}\n{target.DeviceId}"))
                throw new InvalidDataException(
                    $"Multiple output targets are bound to the same physical device '{target.DeviceId}'.");

            target.JpegQuality ??= legacyJpegQuality;
            if (target.JpegQuality is < 10 or > 100)
                throw new InvalidDataException($"Output target '{target.Id}' JpegQuality must be in the range 10..100.");

            var defaults = legacyUsb ?? new UsbConfig();
            target.Usb ??= new UsbConfig
            {
                RetryIntervalMs = defaults.RetryIntervalMs,
                TransferTimeoutMs = defaults.TransferTimeoutMs,
                DiagnosticJournalEnabled = defaults.DiagnosticJournalEnabled
            };
            if (target.Usb.RetryIntervalMs < 500 || target.Usb.TransferTimeoutMs < 500)
                throw new InvalidDataException($"Output target '{target.Id}' USB intervals must be at least 500 ms.");
        }
    }
}

internal sealed class RendererConfig
{
    public int JpegQuality { get; set; } = 92;
}

internal sealed class UsbConfig
{
    public int RetryIntervalMs { get; set; } = 3000;
    public int TransferTimeoutMs { get; set; } = 3000;
    public bool DiagnosticJournalEnabled { get; set; }
}

internal sealed class MediaConfig
{
    public Dictionary<string, string> EndpointTypeOverrides { get; set; } = new(StringComparer.Ordinal);
}
