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
            return new AppConfig();
        }

        var json = File.ReadAllText(path);
        var config = JsonSerializer.Deserialize<AppConfig>(json, JsonOptions()) ?? new AppConfig();
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
        if (string.IsNullOrWhiteSpace(Dashboard.Active))
            throw new InvalidDataException("Dashboard.Active must not be empty.");

        if (Display.RefreshIntervalMs < 10)
            throw new InvalidDataException("Display.RefreshIntervalMs must be at least 10 ms.");

        Outputs ??= new OutputConfig();
        Outputs.Targets ??= OutputTargetContract.CreateDefaults();
        OutputTargetContract.ValidateAndNormalize(Outputs.Targets);

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
    public string Transport { get; set; } = OutputTargetContract.TrofeoTransport;
    public string DeviceId { get; set; } = string.Empty;
}

internal static class OutputTargetContract
{
    public const string TrofeoTransport = "trofeo";
    public const string DefaultTargetId = "screen-1";
    public const string DefaultTargetName = "Thermalright Trofeo Vision 9.16";

    public static List<OutputTargetConfig> CreateDefaults() =>
    [
        new OutputTargetConfig()
    ];

    public static OutputTargetConfig GetPrimary(AppConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        if (config.Outputs.Targets.Count == 0)
            throw new InvalidDataException("Outputs.Targets must contain at least one target.");
        return config.Outputs.Targets[0];
    }

    public static bool ConnectionEquals(OutputTargetConfig left, OutputTargetConfig right) =>
        left.Transport.Equals(right.Transport, StringComparison.OrdinalIgnoreCase) &&
        left.DeviceId.Equals(right.DeviceId, StringComparison.OrdinalIgnoreCase);

    public static void ValidateAndNormalize(List<OutputTargetConfig> targets)
    {
        if (targets.Count == 0)
            throw new InvalidDataException("Outputs.Targets must contain at least one target.");

        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var explicitBindings = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var target in targets)
        {
            target.Id = target.Id?.Trim() ?? string.Empty;
            target.Name = target.Name?.Trim() ?? string.Empty;
            target.Transport = target.Transport?.Trim().ToLowerInvariant() ?? string.Empty;
            target.DeviceId = target.DeviceId?.Trim() ?? string.Empty;

            if (target.Id.Length == 0)
                throw new InvalidDataException("Output target Id must not be empty.");
            if (!ids.Add(target.Id))
                throw new InvalidDataException($"Duplicate output target Id '{target.Id}'.");
            if (target.Name.Length == 0)
                throw new InvalidDataException($"Output target '{target.Id}' Name must not be empty.");
            if (!target.Transport.Equals(TrofeoTransport, StringComparison.Ordinal))
                throw new InvalidDataException($"Output target '{target.Id}' uses unsupported transport '{target.Transport}'.");
            if (target.DeviceId.Length > 0 && !target.DeviceId.StartsWith("trofeo:", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Output target '{target.Id}' has an invalid Trofeo DeviceId.");

            if (targets.Count > 1 && target.DeviceId.Length == 0)
            {
                throw new InvalidDataException(
                    $"Output target '{target.Id}' must have an explicit DeviceId when multiple targets are configured.");
            }

            if (target.DeviceId.Length > 0 &&
                !explicitBindings.Add($"{target.Transport}\n{target.DeviceId}"))
            {
                throw new InvalidDataException(
                    $"Multiple output targets are bound to the same physical device '{target.DeviceId}'.");
            }
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
}

internal sealed class MediaConfig
{
    public Dictionary<string, string> EndpointTypeOverrides { get; set; } = new(StringComparer.Ordinal);
}
