namespace PinkieSysMon.Editor;

internal static class EditorMainMenuContract
{
    public const string FileMenuText = "File";
    public const string EditMenuText = "Edit";
    public const string ToolsMenuText = "Tools";
    public const string OutputMenuText = "Output";
    public const string HelpMenuText = "Help";

    public static void Populate(
        MenuStrip menu,
        ToolStripMenuItem fileMenu,
        ToolStripMenuItem editMenu,
        ToolStripMenuItem toolsMenu,
        ToolStripMenuItem helpMenu,
        ToolStripMenuItem outputMenu)
    {
        ArgumentNullException.ThrowIfNull(menu);
        ArgumentNullException.ThrowIfNull(fileMenu);
        ArgumentNullException.ThrowIfNull(editMenu);
        ArgumentNullException.ThrowIfNull(toolsMenu);
        ArgumentNullException.ThrowIfNull(helpMenu);
        ArgumentNullException.ThrowIfNull(outputMenu);

        menu.Items.Add(fileMenu);
        menu.Items.Add(editMenu);
        menu.Items.Add(toolsMenu);
        menu.Items.Add(helpMenu);

        // Output remains right-aligned so the title-bar center keeps useful drag space.
        menu.Items.Add(outputMenu);
    }
}
