using Microsoft.Win32;

namespace PinkieSysMon.Editor;

internal static class StartupTaskManager
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "PinkieSysMon";

    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
        return key?.GetValue(ValueName) is string value && !string.IsNullOrWhiteSpace(value);
    }

    // The HKCU Run value is account-wide, not scoped to an installation.
    // A foreign value must never be silently claimed or removed.
    public static bool IsEnabled(string runtimeExePath)
    {
        var existing = ReadExisting();
        if (string.IsNullOrWhiteSpace(existing))
            return false;
        RequireOwned(existing, runtimeExePath);
        return true;
    }

    internal static bool IsOwnedCommand(string command, string runtimeExePath)
    {
        if (string.IsNullOrWhiteSpace(command) || string.IsNullOrWhiteSpace(runtimeExePath))
            return false;
        var raw = command.Trim();
        if (raw.StartsWith('"'))
        {
            var end = raw.IndexOf('"', 1);
            if (end <= 1 || raw[(end + 1)..].Trim().Length != 0)
                return false;
            raw = raw[1..end];
        }
        try
        {
            return string.Equals(Path.GetFullPath(raw), Path.GetFullPath(runtimeExePath),
                StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception error) when (error is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    internal static string? ReadCurrentCommand() => ReadExisting();

    private static string? ReadExisting()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
        var raw = key?.GetValue(ValueName);
        return raw switch
        {
            null => null,
            string text => text,
            _ => throw new InvalidDataException(
                "Windows Runtime startup entry uses an unexpected registry value type; it was not changed.")
        };
    }

    private static void RequireOwned(string command, string runtimeExePath)
    {
        if (!IsOwnedCommand(command, runtimeExePath))
            throw new InvalidOperationException(
                "Windows Runtime startup is already configured for another installation or an unrecognized command. " +
                "The existing HKCU Run entry was not changed.");
    }

    public static void SetEnabled(bool enabled, string runtimeExePath)
    {
        var existing = ReadExisting();
        if (!string.IsNullOrWhiteSpace(existing))
            RequireOwned(existing, runtimeExePath);

        if (enabled)
        {
            if (!File.Exists(runtimeExePath))
                throw new FileNotFoundException("PinkieSysMon runtime executable was not found. Build the project first.", runtimeExePath);

            using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true)
                ?? throw new InvalidOperationException("Could not open the current user's Windows Run registry key.");

            if (string.IsNullOrWhiteSpace(existing))
                key.SetValue(ValueName, QuoteCommand(runtimeExePath), RegistryValueKind.String);
            return;
        }

        using var runKey = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
        runKey?.DeleteValue(ValueName, throwOnMissingValue: false);
    }

    private static string QuoteCommand(string executablePath) => $"\"{Path.GetFullPath(executablePath)}\"";
}
