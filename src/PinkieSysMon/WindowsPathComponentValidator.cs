namespace PinkieSysMon;

internal enum WindowsPathComponentValidationError
{
    None,
    Empty,
    DotSegment,
    TrailingSpaceOrPeriod,
    InvalidCharacter,
    ReservedDeviceName
}

internal static class WindowsPathComponentValidator
{
    private static readonly HashSet<char> InvalidCharacters = new()
    {
        '<', '>', ':', '"', '/', '\\', '|', '?', '*'
    };

    private static readonly HashSet<string> ReservedDeviceNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "COM¹", "COM²", "COM³",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
        "LPT¹", "LPT²", "LPT³"
    };

    public static bool IsSafe(string? value) =>
        Validate(value) == WindowsPathComponentValidationError.None;

    public static WindowsPathComponentValidationError Validate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return WindowsPathComponentValidationError.Empty;

        if (value is "." or "..")
            return WindowsPathComponentValidationError.DotSegment;

        if (value[^1] is ' ' or '.')
            return WindowsPathComponentValidationError.TrailingSpaceOrPeriod;

        if (value.Any(c => c < 32 || InvalidCharacters.Contains(c)))
            return WindowsPathComponentValidationError.InvalidCharacter;

        var dot = value.IndexOf('.');
        var deviceStem = dot >= 0 ? value[..dot] : value;
        return ReservedDeviceNames.Contains(deviceStem)
            ? WindowsPathComponentValidationError.ReservedDeviceName
            : WindowsPathComponentValidationError.None;
    }
}
