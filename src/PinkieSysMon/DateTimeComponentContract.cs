namespace PinkieSysMon;

/// <summary>
/// Converts a DateTime reading into a finite scalar for quantitative widgets.
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

    public static bool TryProject(object? reading, string? component, out double value)
    {
        value = default;
        if (component is null)
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
        var fractionOfDay = current.TimeOfDay.TotalDays;
        var secondsOfMinute = current.Second +
            (current.TimeOfDay.Ticks % TimeSpan.TicksPerSecond) / (double)TimeSpan.TicksPerSecond;

        value = component switch
        {
            "year" => current.Year +
                (current.DayOfYear - 1 + fractionOfDay) / (DateTime.IsLeapYear(current.Year) ? 366d : 365d),
            "month" => current.Month +
                (current.Day - 1 + fractionOfDay) / DateTime.DaysInMonth(current.Year, current.Month),
            "dayOfMonth" => current.Day + fractionOfDay,
            "dayOfYear" => current.DayOfYear + fractionOfDay,
            "dayOfWeek" => ((int)current.DayOfWeek + 6) % 7 + 1 + fractionOfDay,
            "hour24" => current.TimeOfDay.TotalHours,
            "hour12" => current.TimeOfDay.TotalHours % 12d,
            "minute" => current.Minute + secondsOfMinute / 60d,
            "second" => secondsOfMinute,
            _ => double.NaN
        };
        return double.IsFinite(value);
    }
}
