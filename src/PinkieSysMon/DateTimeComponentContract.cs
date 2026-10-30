namespace PinkieSysMon;

/// <summary>
/// Projects a numeric civil calendar/clock component with optional truncated fractional precision.
/// Textual DateTime formatting remains the responsibility of MetricValueFormatter.
/// </summary>
internal static class DateTimeComponentContract
{
    public static readonly IReadOnlyList<MetricPresentationOption> Options =
    [
        new(null, "Select component"),
        new("year", "Year"),
        new("month", "Month"),
        new("dayOfMonth", "Day of month"),
        new("dayOfYear", "Day of year"),
        new("dayOfWeek", "Day of week (ISO)"),
        new("hour24", "Hour (24-hour)"),
        new("hour12", "Hour (12-hour)"),
        new("minute", "Minute"),
        new("second", "Second")
    ];

    public static string? NormalizeOrThrow(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return null;

        var trimmed = input.Trim();
        foreach (var option in Options)
        {
            if (string.Equals(option.Token, trimmed, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(option.Label, trimmed, StringComparison.OrdinalIgnoreCase))
                return option.Token;
        }

        throw new FormatException($"Unsupported DateTime component '{input}'. Select one numeric component.");
    }

    public static void Validate(string? component, string context)
    {
        if (component is null)
            return;

        if (!Options.Any(option => option.Token is not null &&
                                   string.Equals(option.Token, component, StringComparison.Ordinal)))
            throw new InvalidDataException($"{context} has an invalid DateTime Component '{component}'.");
    }

    public static string GetLabel(string? component) =>
        Options.FirstOrDefault(option => string.Equals(option.Token, component, StringComparison.Ordinal))?.Label
        ?? "Select component";

    // Reuse the existing numeric Format vocabulary, without its "raw" option.
    public static readonly IReadOnlyList<MetricPresentationOption> PrecisionFormats =
        MetricContract.NumericFormats.Where(option => option.Token is not null).ToArray();

    public static string? NormalizeFormatOrThrow(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return null;
        var token = input.Trim();
        if (PrecisionFormats.Any(option => string.Equals(option.Token, token, StringComparison.Ordinal)))
            return token;
        throw new FormatException($"Unsupported DateTime Component Format '{input}'.");
    }

    public static void ValidateFormat(string? format, string context)
    {
        if (format is not null && !PrecisionFormats.Any(option =>
                string.Equals(option.Token, format, StringComparison.Ordinal)))
            throw new InvalidDataException($"{context} has an invalid DateTime Component Format '{format}'.");
    }

    public static string GetFormatLabel(string? format) => format ?? "0";

    public static bool TryProject(object? reading, string? component, out double value) =>
        TryProject(reading, component, componentFormat: null, out value);

    public static bool TryProject(object? reading, string? component, string? componentFormat, out double value)
    {
        value = default;
        if (component is null)
            return false;

        var scale = componentFormat switch
        {
            null or "0" => 1,
            "0.0" => 10,
            "0.00" => 100,
            _ => 0
        };
        if (scale == 0)
            return false;

        var dateTime = reading switch
        {
            DateTime date => date,
            DateTimeOffset offset => offset.LocalDateTime,
            _ => (DateTime?)null
        };
        if (dateTime is null)
            return false;

        var current = dateTime.Value;
        var ticks = current.TimeOfDay.Ticks;
        var dayTicks = TimeSpan.TicksPerDay;
        int whole;
        long elapsedTicks;
        long periodTicks;
        switch (component)
        {
            case "year":
                whole = current.Year;
                elapsedTicks = (current.DayOfYear - 1L) * dayTicks + ticks;
                periodTicks = (DateTime.IsLeapYear(current.Year) ? 366L : 365L) * dayTicks;
                break;
            case "month":
                whole = current.Month;
                elapsedTicks = (current.Day - 1L) * dayTicks + ticks;
                periodTicks = DateTime.DaysInMonth(current.Year, current.Month) * dayTicks;
                break;
            case "dayOfMonth":
                whole = current.Day;
                elapsedTicks = ticks;
                periodTicks = dayTicks;
                break;
            case "dayOfYear":
                whole = current.DayOfYear;
                elapsedTicks = ticks;
                periodTicks = dayTicks;
                break;
            case "dayOfWeek":
                whole = ((int)current.DayOfWeek + 6) % 7 + 1;
                elapsedTicks = ticks;
                periodTicks = dayTicks;
                break;
            case "hour24":
                whole = current.Hour;
                elapsedTicks = ticks % TimeSpan.TicksPerHour;
                periodTicks = TimeSpan.TicksPerHour;
                break;
            case "hour12":
                whole = current.Hour % 12 is 0 ? 12 : current.Hour % 12;
                elapsedTicks = ticks % TimeSpan.TicksPerHour;
                periodTicks = TimeSpan.TicksPerHour;
                break;
            case "minute":
                whole = current.Minute;
                elapsedTicks = ticks % TimeSpan.TicksPerMinute;
                periodTicks = TimeSpan.TicksPerMinute;
                break;
            case "second":
                whole = current.Second;
                elapsedTicks = ticks % TimeSpan.TicksPerSecond;
                periodTicks = TimeSpan.TicksPerSecond;
                break;
            default:
                return false;
        }

        // Integral tick arithmetic prevents rounding into the next civil unit.
        // Decimal composition also preserves equality against parsed decimal setpoints.
        var steps = elapsedTicks * scale / periodTicks;
        value = (double)((decimal)whole + (decimal)steps / scale);
        return double.IsFinite(value);
    }
}
