namespace PinkieSysMon.Editor;

internal static class EditorMainMenuContract
{
    public const string FileMenuText = "File";
    public const string EditMenuText = "Edit";
    public const string DesktopMenuText = "Desktop";
    public const string DevicesMenuText = "Devices";
    public const string ToolsMenuText = "Tools";
    public const string SettingsMenuText = "Settings";
    public const string AboutMenuText = "About";

    public static void Populate(
        MenuStrip menu,
        ToolStripMenuItem fileMenu,
        ToolStripMenuItem editMenu,
        ToolStripMenuItem desktopMenu,
        ToolStripMenuItem devicesMenu,
        ToolStripMenuItem toolsMenu,
        ToolStripMenuItem settingsMenu,
        ToolStripMenuItem aboutMenu)
    {
        ArgumentNullException.ThrowIfNull(menu);
        foreach (var item in new[] { fileMenu, editMenu, desktopMenu,
                                     devicesMenu, toolsMenu, settingsMenu, aboutMenu })
            ArgumentNullException.ThrowIfNull(item);

        menu.Items.Add(fileMenu);
        menu.Items.Add(editMenu);
        menu.Items.Add(desktopMenu);
        menu.Items.Add(devicesMenu);
        menu.Items.Add(toolsMenu);
        // WinForms aligns Right items right-to-left in insertion order.
        // Insert About first to display Settings to its left.
        menu.Items.Add(aboutMenu);
        menu.Items.Add(settingsMenu);
        settingsMenu.Alignment = ToolStripItemAlignment.Right;
        aboutMenu.Alignment = ToolStripItemAlignment.Right;
    }
}
