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

    public static void SetEnabled(bool enabled, string runtimeExePath)
    {
        if (enabled)
        {
            if (!File.Exists(runtimeExePath))
                throw new FileNotFoundException("PinkieSysMon runtime executable was not found. Build the project first.", runtimeExePath);

            using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true)
                ?? throw new InvalidOperationException("Could not open the current user's Windows Run registry key.");

            key.SetValue(ValueName, QuoteCommand(runtimeExePath), RegistryValueKind.String);
            return;
        }

        using var existing = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
        existing?.DeleteValue(ValueName, throwOnMissingValue: false);
    }

    private static string QuoteCommand(string executablePath) => $"\"{Path.GetFullPath(executablePath)}\"";
}
