using System.Globalization;

namespace PinkieSysMon;

/// <summary>
/// Typed semantics for a numeric literal entered through Text/Value Source=Text.
/// The literal remains stored as text, but an unambiguous invariant numeric value can
/// participate in the same unit-conversion and numeric-formatting pipeline as telemetry.
/// </summary>
internal static class LiteralNumericContract
{
    private static readonly IReadOnlyList<MetricPresentationOption> RawOnly =
    [
        new(null, "raw")
    ];

    public static readonly IReadOnlyList<MetricPresentationOption> SourceUnitOptions =
    [
        new(null, "raw"),
        new("%", "%"),
        new("milliseconds", "milliseconds"),
        new("seconds", "seconds"),
        new("minutes", "minutes"),
        new("hours", "hours"),
        new("C", "C"),
        new("F", "F"),
        new("W", "W"),
        new("V", "V"),
        new("A", "A"),
        new("Hz", "Hz"),
        new("kHz", "kHz"),
        new("MHz", "MHz"),
        new("GHz", "GHz"),
        new("RPM", "RPM"),
        new("L/h", "L/h"),
        new("ns", "ns"),
        new("mWh", "mWh"),
        new("dBA", "dBA"),
        new("µS/cm", "µS/cm"),
        new("FPS", "FPS"),
        new("B", "B"),
        new("KB", "KB"),
        new("MB", "MB"),
        new("GB", "GB"),
        new("TB", "TB"),
        new("KiB", "KiB"),
        new("MiB", "MiB"),
        new("GiB", "GiB"),
        new("TiB", "TiB"),
        new("B/s", "B/s"),
        new("KB/s", "KB/s"),
        new("MB/s", "MB/s"),
        new("GB/s", "GB/s"),
        new("TB/s", "TB/s"),
        new("KiB/s", "KiB/s"),
        new("MiB/s", "MiB/s"),
        new("GiB/s", "GiB/s"),
        new("TiB/s", "TiB/s"),
        new("bit/s", "bit/s"),
        new("kbit/s", "kbit/s"),
        new("Mbit/s", "Mbit/s"),
        new("Gbit/s", "Gbit/s"),
        new("Tbit/s", "Tbit/s")
    ];

    public static bool TryParse(string? text, out double value) =>
        double.TryParse(
            text,
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out value) &&
        double.IsFinite(value);

    public static string? NormalizeSourceUnit(string? sourceUnit)
    {
        var normalized = NormalizeToken(sourceUnit);
        return SourceUnitOptions.FirstOrDefault(option => TokenEquals(option.Token, normalized))?.Token;
    }

    public static MetricDescriptor GetDescriptor(string? sourceUnit)
    {
        var normalized = NormalizeSourceUnit(sourceUnit);
        var (kind, unit) = normalized switch
        {
            null => (MetricValueKind.Number, MetricUnit.None),
            "%" => (MetricValueKind.Percent, MetricUnit.None),
            "milliseconds" => (MetricValueKind.Duration, MetricUnit.Milliseconds),
            "seconds" => (MetricValueKind.Duration, MetricUnit.Seconds),
            "minutes" => (MetricValueKind.Duration, MetricUnit.Minutes),
            "hours" => (MetricValueKind.Duration, MetricUnit.Hours),
            "C" => (MetricValueKind.Number, MetricUnit.Celsius),
            "F" => (MetricValueKind.Number, MetricUnit.Fahrenheit),
            "W" => (MetricValueKind.Number, MetricUnit.Watts),
            "V" => (MetricValueKind.Number, MetricUnit.Volts),
            "A" => (MetricValueKind.Number, MetricUnit.Amperes),
            "Hz" => (MetricValueKind.Number, MetricUnit.Hertz),
            "kHz" => (MetricValueKind.Number, MetricUnit.Kilohertz),
            "MHz" => (MetricValueKind.Number, MetricUnit.Megahertz),
            "GHz" => (MetricValueKind.Number, MetricUnit.Gigahertz),
            "RPM" => (MetricValueKind.Number, MetricUnit.RevolutionsPerMinute),
            "L/h" => (MetricValueKind.Number, MetricUnit.LitersPerHour),
            "ns" => (MetricValueKind.Number, MetricUnit.Nanoseconds),
            "mWh" => (MetricValueKind.Number, MetricUnit.MilliWattHours),
            "dBA" => (MetricValueKind.Number, MetricUnit.DecibelsA),
            "µS/cm" => (MetricValueKind.Number, MetricUnit.MicroSiemensPerCentimeter),
            "FPS" => (MetricValueKind.Number, MetricUnit.FramesPerSecond),
            "B" => (MetricValueKind.DataSize, MetricUnit.Bytes),
            "KB" => (MetricValueKind.DataSize, MetricUnit.Kilobytes),
            "MB" => (MetricValueKind.DataSize, MetricUnit.Megabytes),
            "GB" => (MetricValueKind.DataSize, MetricUnit.Gigabytes),
            "TB" => (MetricValueKind.DataSize, MetricUnit.Terabytes),
            "KiB" => (MetricValueKind.DataSize, MetricUnit.Kibibytes),
            "MiB" => (MetricValueKind.DataSize, MetricUnit.Mebibytes),
            "GiB" => (MetricValueKind.DataSize, MetricUnit.Gibibytes),
            "TiB" => (MetricValueKind.DataSize, MetricUnit.Tebibytes),
            "B/s" => (MetricValueKind.Number, MetricUnit.BytesPerSecond),
            "KB/s" => (MetricValueKind.Number, MetricUnit.KilobytesPerSecond),
            "MB/s" => (MetricValueKind.Number, MetricUnit.MegabytesPerSecond),
            "GB/s" => (MetricValueKind.Number, MetricUnit.GigabytesPerSecond),
            "TB/s" => (MetricValueKind.Number, MetricUnit.TerabytesPerSecond),
            "KiB/s" => (MetricValueKind.Number, MetricUnit.KibibytesPerSecond),
            "MiB/s" => (MetricValueKind.Number, MetricUnit.MebibytesPerSecond),
            "GiB/s" => (MetricValueKind.Number, MetricUnit.GibibytesPerSecond),
            "TiB/s" => (MetricValueKind.Number, MetricUnit.TebibytesPerSecond),
            "bit/s" => (MetricValueKind.Number, MetricUnit.BitsPerSecond),
            "kbit/s" => (MetricValueKind.Number, MetricUnit.KilobitsPerSecond),
            "Mbit/s" => (MetricValueKind.Number, MetricUnit.MegabitsPerSecond),
            "Gbit/s" => (MetricValueKind.Number, MetricUnit.GigabitsPerSecond),
            "Tbit/s" => (MetricValueKind.Number, MetricUnit.TerabitsPerSecond),
            _ => (MetricValueKind.Number, MetricUnit.None)
        };

        return new MetricDescriptor("literal.text", kind, unit);
    }

    public static IReadOnlyList<MetricPresentationOption> GetUnitOptions(string? sourceUnit)
    {
        var units = MetricContract.GetUnits(GetDescriptor(sourceUnit));
        return units.Count == 0 ? RawOnly : units;
    }

    public static IReadOnlyList<MetricPresentationOption> GetFormatOptions(string? sourceUnit) =>
        MetricContract.GetFormats(GetDescriptor(sourceUnit));

    public static string? NormalizeUnit(string? sourceUnit, string? unit)
    {
        var normalized = NormalizeToken(unit);
        return GetUnitOptions(sourceUnit)
            .FirstOrDefault(option => TokenEquals(option.Token, normalized))?.Token;
    }

    public static string? NormalizeFormat(string? sourceUnit, string? unit, string? format)
    {
        if (!SupportsFormat(sourceUnit, unit))
            return null;

        var normalized = NormalizeToken(format);
        return GetFormatOptions(sourceUnit)
            .FirstOrDefault(option => TokenEquals(option.Token, normalized))?.Token;
    }

    public static bool NormalizeStoredState(WidgetDefinition widget)
    {
        if (!WidgetTypeContract.Is(widget.Type, WidgetTypeContract.Value) ||
            !TextValueContract.IsTextSource(widget.ValueSource) ||
            !TryParse(widget.Text, out _))
        {
            return false;
        }

        var changed = false;

        var sourceUnit = NormalizeSourceUnit(widget.SourceUnit);
        if (!string.Equals(widget.SourceUnit, sourceUnit, StringComparison.Ordinal))
        {
            widget.SourceUnit = sourceUnit;
            changed = true;
        }

        var unit = NormalizeUnit(widget.SourceUnit, widget.Unit);
        if (!string.Equals(widget.Unit, unit, StringComparison.Ordinal))
        {
            widget.Unit = unit;
            changed = true;
        }

        var format = NormalizeFormat(widget.SourceUnit, widget.Unit, widget.Format);
        if (!string.Equals(widget.Format, format, StringComparison.Ordinal))
        {
            widget.Format = format;
            changed = true;
        }

        return changed;
    }

    public static bool SupportsFormat(string? sourceUnit, string? unit)
    {
        var descriptor = GetDescriptor(sourceUnit);
        return !(descriptor.ValueKind == MetricValueKind.Duration &&
                 string.Equals(unit, "auto", StringComparison.OrdinalIgnoreCase));
    }

    private static bool TokenEquals(string? left, string? right) =>
        string.Equals(NormalizeToken(left), NormalizeToken(right), StringComparison.OrdinalIgnoreCase);

    private static string? NormalizeToken(string? token) =>
        string.IsNullOrWhiteSpace(token) || token.Equals("raw", StringComparison.OrdinalIgnoreCase)
            ? null
            : token.Trim();
}
