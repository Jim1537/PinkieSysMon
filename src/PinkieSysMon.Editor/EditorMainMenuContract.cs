namespace PinkieSysMon.Editor;

internal static class EditorMainMenuContract
{
    public const string FileMenuText = "File";
    public const string EditMenuText = "Edit";
    public const string DesktopMenuText = "Desktop";
    public const string DevicesMenuText = "Devices";
    public const string ToolsMenuText = "Tools";
    public const string HelpMenuText = "Help";
    public const string EditorSettingsText = "Editor settings";

    public static void AppendEditorSettings(ToolStripMenuItem editMenu, EventHandler click)
    {
        ArgumentNullException.ThrowIfNull(editMenu);
        ArgumentNullException.ThrowIfNull(click);

        editMenu.DropDownItems.Add(new ToolStripSeparator());
        editMenu.DropDownItems.Add(new ToolStripMenuItem(EditorSettingsText, null, click));
    }

    public static void Populate(
        MenuStrip menu,
        ToolStripMenuItem fileMenu,
        ToolStripMenuItem editMenu,
        ToolStripMenuItem desktopMenu,
        ToolStripMenuItem devicesMenu,
        ToolStripMenuItem toolsMenu,
        ToolStripMenuItem helpMenu)
    {
        ArgumentNullException.ThrowIfNull(menu);
        ArgumentNullException.ThrowIfNull(fileMenu);
        ArgumentNullException.ThrowIfNull(editMenu);
        ArgumentNullException.ThrowIfNull(desktopMenu);
        ArgumentNullException.ThrowIfNull(devicesMenu);
        ArgumentNullException.ThrowIfNull(toolsMenu);
        ArgumentNullException.ThrowIfNull(helpMenu);

        menu.Items.Add(fileMenu);
        menu.Items.Add(editMenu);
        menu.Items.Add(desktopMenu);
        menu.Items.Add(devicesMenu);
        menu.Items.Add(toolsMenu);
        menu.Items.Add(helpMenu);

        // Help occupies the former Output menu's right-aligned title-bar position.
        helpMenu.Alignment = ToolStripItemAlignment.Right;
    }
}
