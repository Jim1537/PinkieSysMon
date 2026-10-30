using System.Globalization;

namespace PinkieSysMon;

/// <summary>
/// Presentation formatting for semantic metric values. Numeric conversion is delegated
/// to MetricValueConverter so text and quantitative widgets share identical unit semantics.
/// </summary>
internal static class MetricValueFormatter
{
    public const string DefaultDateTimeFormat = "yyyy-MM-dd HH:mm:ss";

    public static string Format(
        object value,
        MetricDescriptor descriptor,
        string? unit,
        string? format,
        CultureInfo culture)
    {
        return descriptor.ValueKind switch
        {
            MetricValueKind.DateTime => FormatDateTime(value, format, culture),
            MetricValueKind.Text => Convert.ToString(value, culture) ?? string.Empty,
            MetricValueKind.Boolean => Convert.ToBoolean(value, CultureInfo.InvariantCulture) ? "true" : "false",
            MetricValueKind.Duration => FormatDuration(value, descriptor, unit, format, culture),
            MetricValueKind.DataSize or MetricValueKind.Number or MetricValueKind.Percent =>
                FormatNumeric(value, descriptor, unit, format, culture),
            _ => Convert.ToString(value, culture) ?? string.Empty
        };
    }

    private static string FormatDateTime(object value, string? format, CultureInfo culture)
    {
        var dateTime = value switch
        {
            DateTime dt => dt,
            DateTimeOffset dto => dto.LocalDateTime,
            _ => throw new FormatException("Metric value is not a date/time value.")
        };
        return dateTime.ToString(string.IsNullOrWhiteSpace(format) ? DefaultDateTimeFormat : format, culture);
    }

    private static string FormatDuration(
        object value,
        MetricDescriptor descriptor,
        string? unit,
        string? format,
        CultureInfo culture)
    {
        var nativeValue = Convert.ToDouble(value, CultureInfo.InvariantCulture);
        var milliseconds = MetricValueConverter.ToMilliseconds(nativeValue, descriptor.BaseUnit);

        if (Normalize(unit)?.Equals("auto", StringComparison.OrdinalIgnoreCase) == true)
            return FormatDurationAuto(TimeSpan.FromMilliseconds(milliseconds));

        if (format is "h:mm" or "h:mm:ss")
        {
            var duration = TimeSpan.FromMilliseconds(milliseconds);
            var totalHours = (long)Math.Floor(duration.TotalHours);
            return format == "h:mm"
                ? $"{totalHours}:{duration.Minutes:00}"
                : $"{totalHours}:{duration.Minutes:00}:{duration.Seconds:00}";
        }

        return FormatNumber(MetricValueConverter.ConvertNumeric(value, descriptor, unit), format, culture);
    }

    private static string FormatNumeric(
        object value,
        MetricDescriptor descriptor,
        string? unit,
        string? format,
        CultureInfo culture)
    {
        if (Normalize(unit) is null)
            return FormatNumber(value, format, culture);

        return FormatNumber(MetricValueConverter.ConvertNumeric(value, descriptor, unit), format, culture);
    }

    private static string FormatDurationAuto(TimeSpan duration)
    {
        var negative = duration < TimeSpan.Zero;
        if (negative)
            duration = duration.Negate();

        var prefix = negative ? "-" : string.Empty;
        if (duration.TotalMinutes < 1)
            return $"{prefix}{(long)Math.Floor(duration.TotalSeconds)}s";
        if (duration.TotalHours < 1)
            return $"{prefix}{(long)Math.Floor(duration.TotalMinutes)}m:{duration.Seconds:00}s";
        if (duration.TotalDays < 1)
            return $"{prefix}{(long)Math.Floor(duration.TotalHours)}h:{duration.Minutes:00}m";

        return $"{prefix}{(long)Math.Floor(duration.TotalDays)}d:{duration.Hours:00}h:{duration.Minutes:00}m";
    }

    private static string FormatNumber(object value, string? format, CultureInfo culture)
    {
        if (string.IsNullOrWhiteSpace(format) || format.Equals("raw", StringComparison.OrdinalIgnoreCase))
            return Convert.ToString(value, culture) ?? string.Empty;

        return value is IFormattable formattable
            ? formattable.ToString(format, culture) ?? string.Empty
            : Convert.ToString(value, culture) ?? string.Empty;
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) || value.Equals("raw", StringComparison.OrdinalIgnoreCase)
            ? null
            : value.Trim();
}
