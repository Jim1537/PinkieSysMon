using System.Text.Json;

namespace PinkieSysMon;

internal sealed class AppConfig
{
    public DisplayConfig Display { get; set; } = new();
    public RuntimeConfig Runtime { get; set; } = new();
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
        if (Display is null || Runtime is null || Outputs is null ||
            Renderer is null || Usb is null || Media is null)
            throw new InvalidDataException("Application config requires Display, Runtime, Outputs, Renderer, Usb and Media objects.");
        if (Outputs.Targets is null)
            throw new InvalidDataException("Outputs.Targets must be an array of configured outputs.");

        if (Runtime.IdleTimeoutMinutes is < 0 or > RuntimeIdlePolicy.MaximumTimeoutMinutes)
            throw new InvalidDataException("Runtime.IdleTimeoutMinutes must be 0 (disabled) or 1..1440 minutes.");

        if (Display.RefreshIntervalMs < 10)
            throw new InvalidDataException("Display.RefreshIntervalMs must be at least 10 ms.");

        OutputTargetContract.ValidateAndNormalize(
            Outputs.Targets, Display.RefreshIntervalMs,
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

internal sealed class DisplayConfig
{
    public int RefreshIntervalMs { get; set; } = 1000;
}

internal sealed class RuntimeConfig
{
    // Opt-in. Existing installations continue rendering until explicitly configured.
    public int IdleTimeoutMinutes { get; set; }
}

internal sealed class OutputConfig
{
    public List<OutputTargetConfig> Targets { get; set; } = OutputTargetContract.CreateDefaults();
}

internal sealed class OutputTargetConfig
{
    public string Id { get; set; } = OutputTargetContract.DefaultTargetId;
    public string Name { get; set; } = OutputTargetContract.DefaultTargetName;
    public string Kind { get; set; } = OutputTargetContract.PhysicalKind;
    public string Transport { get; set; } = string.Empty;
    public string DeviceId { get; set; } = string.Empty;

    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public int Width { get; set; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public int Height { get; set; }

    // Missing per-Output cadence/encoding options use the application defaults.
    public string? Dashboard { get; set; }
    public int? RefreshIntervalMs { get; set; }
    public bool ProviderEnabled { get; set; }
    public bool DeviceEnabled { get; set; }
    public int? JpegQuality { get; set; }
    public UsbConfig? Usb { get; set; }
    public DesktopOutputConfig? Desktop { get; set; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public EncryptedJpegOutputConfig? EncryptedJpeg { get; set; }
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
    public const string PhysicalKind = "physical";
    public const string DesktopKind = "desktop";
    public const string LyBulkTransport = "ly-bulk";
    public const string EncryptedJpegTransport = "encrypted-jpeg";
    public const string DesktopTransport = "desktop";

    public static bool IsPhysicalKind(string? kind) =>
        string.Equals(kind, PhysicalKind, StringComparison.OrdinalIgnoreCase);

    public static bool HasIntegratedTransport(string? transport) =>
        string.Equals(transport, LyBulkTransport, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(transport, EncryptedJpegTransport, StringComparison.OrdinalIgnoreCase);
    public const string DefaultTargetId = "screen-1";
    public const string DefaultTargetName = "Physical display";

    // New installations contain no physical bindings or model presets.
    public static List<OutputTargetConfig> CreateDefaults() =>
    [
        new OutputTargetConfig
        {
            Id = "desktop-1",
            Name = "Desktop 1",
            Kind = DesktopKind,
            Transport = DesktopTransport,
            Dashboard = OutputDefaultDashboardPolicy.DesktopName,
            // No prepackaged file exists; first install opens Control Center
            // without trying to render a missing dashboard.
            Desktop = new DesktopOutputConfig { BoardEnabled = false }
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
        left.DeviceId.Equals(right.DeviceId, StringComparison.OrdinalIgnoreCase) &&
        left.Width == right.Width && left.Height == right.Height;

    // One logical setting backed by the existing kind-specific configuration fields.
    // Provider publication and observed session state do not grant output ownership.
    public static bool IsEnabled(OutputTargetConfig target)
    {
        ArgumentNullException.ThrowIfNull(target);
        return target.Kind switch
        {
            DesktopKind => target.Desktop?.BoardEnabled == true,
            PhysicalKind => target.DeviceEnabled,
            _ => false
        };
    }

    public static void ValidateAndNormalize(
        List<OutputTargetConfig> targets,
        int defaultRefreshIntervalMs = 1000,
        int defaultJpegQuality = 92,
        UsbConfig? defaultUsb = null)
    {
        ArgumentNullException.ThrowIfNull(targets);
        if (targets.Count == 0)
            throw new InvalidDataException("Outputs.Targets must contain at least one target.");

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
            if (target.Name.Length == 0 || target.Name.Any(char.IsControl))
                throw new InvalidDataException($"Output target '{target.Id}' Name must not be empty.");
            if (target.Kind is not (PhysicalKind or DesktopKind))
                throw new InvalidDataException($"Unsupported output Kind '{target.Kind}'.");

            if (target.Kind == PhysicalKind)
                ValidatePhysicalConnection(target);

            target.Dashboard ??= OutputDefaultDashboardPolicy.NameFor(target);
            target.Dashboard = target.Dashboard.Trim();
            if (!WindowsPathComponentValidator.IsSafe(target.Dashboard))
                throw new InvalidDataException($"Output target '{target.Id}' has invalid Dashboard reference '{target.Dashboard}'.");

            target.RefreshIntervalMs ??= defaultRefreshIntervalMs;
            if (target.RefreshIntervalMs < 10)
                throw new InvalidDataException($"Output target '{target.Id}' RefreshIntervalMs must be at least 10 ms.");

            if (target.Kind == DesktopKind)
            {
                // Desktop has no physical protocol selector.
                if (target.Transport is not ("" or DesktopTransport))
                    throw new InvalidDataException($"Desktop output '{target.Id}' has unsupported transport '{target.Transport}'.");
                if (target.DeviceId.Length != 0 || target.DeviceEnabled ||
                    target.JpegQuality is not null || target.Usb is not null || target.EncryptedJpeg is not null ||
                    target.Width != 0 || target.Height != 0)
                    throw new InvalidDataException($"Desktop output '{target.Id}' must not own device/USB/JPEG configuration.");

                target.Transport = DesktopTransport;
                target.Desktop ??= new DesktopOutputConfig();
                continue;
            }

            var encryptedJpeg = target.Transport == EncryptedJpegTransport;
            if (target.Desktop is not null || (!encryptedJpeg && target.EncryptedJpeg is not null))
                throw new InvalidDataException($"Output target '{target.Id}' contains options for another protocol.");
            if (encryptedJpeg)
            {
                target.EncryptedJpeg ??= new EncryptedJpegOutputConfig();
                if (target.EncryptedJpeg.BrightnessPercent is < 0 or > 100)
                    throw new InvalidDataException($"Output target '{target.Id}' BrightnessPercent must be null or 0..100.");
            }
            // Protocol selection never partitions ownership of one interface.
            if (!explicitBindings.Add(target.DeviceId))
                throw new InvalidDataException($"Multiple Outputs are bound to the same physical interface.");

            target.JpegQuality ??= defaultJpegQuality;
            if (target.JpegQuality is < 10 or > 100)
                throw new InvalidDataException($"Output target '{target.Id}' JpegQuality must be in the range 10..100.");

            var defaults = defaultUsb ?? new UsbConfig();
            target.Usb ??= new UsbConfig
            {
                RetryIntervalMs = defaults.RetryIntervalMs,
                TransferTimeoutMs = defaults.TransferTimeoutMs,
                DiagnosticJournalEnabled = defaults.DiagnosticJournalEnabled
            };
            if (encryptedJpeg && target.DeviceEnabled)
            {
                try { EncryptedJpegOutputConfig.ValidateFrameOptions(target.EncryptedJpeg!.BrightnessPercent, target.Usb.DiagnosticJournalEnabled); }
                catch (NotSupportedException error) { throw new InvalidDataException($"Output '{target.Id}': {error.Message}", error); }
            }
            if (target.Usb.RetryIntervalMs < 500 || target.Usb.TransferTimeoutMs < 500)
                throw new InvalidDataException($"Output target '{target.Id}' USB intervals must be at least 500 ms.");
        }
    }
    internal static void ValidatePhysicalConnection(OutputTargetConfig target)
    {
        if (!IsPhysicalKind(target.Kind) || !HasIntegratedTransport(target.Transport))
            throw new InvalidDataException("Physical Output requires an implemented protocol selection.");
        if (!IsInterfaceId(target.DeviceId))
            throw new InvalidDataException("Physical Output requires an exact usb-interface binding.");
        try { AdaptiveDashboardFactory.ValidateGeometry(target.Width, target.Height, 0); }
        catch (ArgumentOutOfRangeException error)
        { throw new InvalidDataException($"Output '{target.Id}' geometry is invalid: {error.Message}", error); }
    }

    internal static bool IsInterfaceId(string? value) => value is not null &&
        value.Length == "usb-interface:".Length + 64 &&
        value.StartsWith("usb-interface:", StringComparison.OrdinalIgnoreCase) &&
        value.AsSpan("usb-interface:".Length).ToString().All(Uri.IsHexDigit);

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
