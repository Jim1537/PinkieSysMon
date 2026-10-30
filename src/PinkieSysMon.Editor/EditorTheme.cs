namespace PinkieSysMon.Editor;

internal enum EditorThemeMode
{
    System,
    Light,
    Dark
}

internal readonly record struct EditorShellTheme(
    Color Background,
    Color Foreground,
    Color InactiveForeground,
    Color Border,
    Color SelectionBackground,
    Color SelectionForeground,
    Color SecondarySelectionBackground,
    Color WorkspaceBackground,
    bool IsDark,
    bool HighContrast)
{
    public static EditorShellTheme CaptureCurrent()
    {
        var highContrast = SystemInformation.HighContrast;
        var isDark = !highContrast && EditorThemeContract.IsEffectiveDarkMode();

        if (!isDark)
        {
            var background = SystemColors.Control;
            var foreground = SystemColors.ControlText;
            return new EditorShellTheme(
                Background: background,
                Foreground: foreground,
                InactiveForeground: SystemColors.GrayText,
                Border: SystemColors.ControlDark,
                SelectionBackground: SystemColors.Highlight,
                SelectionForeground: SystemColors.HighlightText,
                SecondarySelectionBackground: Blend(SystemColors.Highlight, background, 0.24f),
                WorkspaceBackground: Blend(background, Color.White, 0.94f),
                IsDark: false,
                HighContrast: highContrast);
        }

        // WinForms dark mode themes stock controls, but classic SystemColors remain light-mode
        // values. These semantic colors are used only by PinkieSysMon's own painted shell
        // surfaces and icons; stock WinForms controls remain owned by Application.SetColorMode.
        var darkBackground = Color.FromArgb(32, 32, 32);
        var darkForeground = Color.FromArgb(245, 245, 245);
        var darkInactive = Color.FromArgb(180, 180, 180);
        var darkBorder = Color.FromArgb(68, 68, 68);
        var darkSelection = Color.FromArgb(58, 102, 173);

        return new EditorShellTheme(
            Background: darkBackground,
            Foreground: darkForeground,
            InactiveForeground: darkInactive,
            Border: darkBorder,
            SelectionBackground: darkSelection,
            SelectionForeground: Color.White,
            SecondarySelectionBackground: Blend(darkSelection, darkBackground, 0.34f),
            WorkspaceBackground: Color.FromArgb(24, 24, 24),
            IsDark: true,
            HighContrast: false);
    }

    internal static Color Blend(Color foreground, Color background, float foregroundWeight)
    {
        var weight = Math.Clamp(foregroundWeight, 0f, 1f);
        var inverse = 1f - weight;
        return Color.FromArgb(
            255,
            (int)Math.Round(foreground.R * weight + background.R * inverse),
            (int)Math.Round(foreground.G * weight + background.G * inverse),
            (int)Math.Round(foreground.B * weight + background.B * inverse));
    }
}

internal static class EditorThemeContract
{
    public const string SystemSetting = "System";
    public const string LightSetting = "Light";
    public const string DarkSetting = "Dark";

    public static EditorThemeMode Parse(string? value) => value?.Trim() switch
    {
        var text when text is not null && text.Equals(LightSetting, StringComparison.OrdinalIgnoreCase) => EditorThemeMode.Light,
        var text when text is not null && text.Equals(DarkSetting, StringComparison.OrdinalIgnoreCase) => EditorThemeMode.Dark,
        _ => EditorThemeMode.System
    };

    public static string Serialize(EditorThemeMode mode) => mode switch
    {
        EditorThemeMode.Light => LightSetting,
        EditorThemeMode.Dark => DarkSetting,
        _ => SystemSetting
    };

    public static SystemColorMode ToSystemColorMode(EditorThemeMode mode) => mode switch
    {
        EditorThemeMode.Light => SystemColorMode.Classic,
        EditorThemeMode.Dark => SystemColorMode.Dark,
        _ => SystemColorMode.System
    };

    public static bool IsEffectiveDarkMode()
    {
        var configured = Application.ColorMode;
        return configured == SystemColorMode.Dark ||
               (configured == SystemColorMode.System && Application.SystemColorMode == SystemColorMode.Dark);
    }

    public static void ApplyApplicationTheme(EditorThemeMode mode) =>
        Application.SetColorMode(ToSystemColorMode(mode));
}

internal static class EditorPropertyGridTheme
{
    public static void Apply(PropertyGrid grid, EditorShellTheme theme)
    {
        ArgumentNullException.ThrowIfNull(grid);

        // .NET 10 dark mode currently leaves the OS visual-style expansion glyphs
        // effectively invisible on the PropertyGrid dark surface. PropertyGrid
        // exposes a supported switch for exactly this rendering choice; use its
        // classic glyph path in dark mode instead of touching PropertyGrid internals.
        grid.CanShowVisualStyleGlyphs = !theme.IsDark;
        grid.Invalidate();
    }
}
