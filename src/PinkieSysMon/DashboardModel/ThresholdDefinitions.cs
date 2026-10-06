using System.Text.Json.Serialization;

namespace PinkieSysMon.DashboardModel;

internal sealed class ThresholdDefinition
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; }

    [JsonPropertyName("value")]
    public double Value { get; set; }

    [JsonPropertyName("color")]
    public string Color { get; set; } = "#FFFFFFFF";

    internal void ValidateStructure(string context)
    {
        if (!double.IsFinite(Value))
            throw new InvalidDataException($"{context} Value must be finite.");
        _ = global::PinkieSysMon.ColorParser.Parse(Color);
    }
}

internal sealed class ThresholdSetDefinition
{
    [JsonPropertyName("mode")]
    public string Mode { get; set; } = ThresholdModeContract.SegmentSolid;

    [JsonPropertyName("items")]
    public List<ThresholdDefinition> Items { get; set; } =
    [
        new(),
        new(),
        new()
    ];

    internal void Validate(double min, double max, string context)
    {
        if (!ThresholdModeContract.SupportedModes.Contains(Mode, StringComparer.OrdinalIgnoreCase))
            throw new InvalidDataException($"{context} has invalid Mode '{Mode}'.");
        if (Items is null || Items.Count != 3 || Items.Any(item => item is null))
            throw new InvalidDataException($"{context} must contain exactly three threshold slots.");

        for (var i = 0; i < Items.Count; i++)
            Items[i].ValidateStructure($"{context} item {i + 1}");

        var enabledValues = Items.Where(item => item.Enabled).Select(item => item.Value).ToArray();
        if (enabledValues.Any(value => value < min || value > max))
            throw new InvalidDataException($"{context} enabled values must be within Min..Max.");
        if (!enabledValues.SequenceEqual(enabledValues.OrderBy(value => value)))
            throw new InvalidDataException($"{context} enabled values must be ordered by value.");
    }
}
