using System.ComponentModel;
using System.Drawing.Design;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows.Forms;
using PinkieSysMon;
using PinkieSysMon.Editor;
using C = PinkieSysMon.DashboardModel;

namespace PinkieSysMon.RegressionTests;

internal static partial class Program
{
    private static void EditorMainMenuGroupCleanup()
    {
        using var menu = new MenuStrip();
        var fileMenu = new ToolStripMenuItem(EditorMainMenuContract.FileMenuText);
        var editMenu = new ToolStripMenuItem(EditorMainMenuContract.EditMenuText);
        var desktopMenu = new ToolStripMenuItem(EditorMainMenuContract.DesktopMenuText);
        var devicesMenu = new ToolStripMenuItem(EditorMainMenuContract.DevicesMenuText);
        var toolsMenu = new ToolStripMenuItem(EditorMainMenuContract.ToolsMenuText);
        var helpMenu = new ToolStripMenuItem(EditorMainMenuContract.HelpMenuText);

        EditorMainMenuContract.Populate(menu, fileMenu, editMenu, desktopMenu, devicesMenu, toolsMenu, helpMenu);
        var labels = menu.Items.Cast<ToolStripItem>().Select(item => item.Text).ToArray();
        Assert(labels.SequenceEqual(new[] { "File", "Edit", "Desktop", "Devices", "Tools", "Help" }),
            "The main menu must expose direct Desktop/Devices commands before Tools, without Settings or Outputs.");
        Assert(desktopMenu.Alignment == ToolStripItemAlignment.Left &&
               devicesMenu.Alignment == ToolStripItemAlignment.Left &&
               toolsMenu.Alignment == ToolStripItemAlignment.Left &&
               helpMenu.Alignment == ToolStripItemAlignment.Right,
            "Help must occupy the right-aligned title-bar position; all other menus remain left.");
        editMenu.DropDownItems.Add(new ToolStripMenuItem("Send to Back"));
        var editorInvoked = false;
        EditorMainMenuContract.AppendEditorSettings(editMenu, (_, _) => editorInvoked = true);
        Assert(editMenu.DropDownItems[^2] is ToolStripSeparator &&
               editMenu.DropDownItems[^1] is ToolStripMenuItem { Text: EditorMainMenuContract.EditorSettingsText },
            "Editor settings must be the last Edit command, immediately after a separator.");
        ((ToolStripMenuItem)editMenu.DropDownItems[^1]).PerformClick();
        Assert(editorInvoked, "The relocated Editor settings command must invoke its handler.");
        Assert(!labels.Contains("Settings", StringComparer.OrdinalIgnoreCase) &&
               !labels.Contains("Output", StringComparer.OrdinalIgnoreCase) &&
               !labels.Contains("Group", StringComparer.OrdinalIgnoreCase),
            "Editor top-level Group menu must remain removed.");

        var fields = typeof(EditorForm)
            .GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            .Select(field => field.Name)
            .ToHashSet(StringComparer.Ordinal);
        Assert(!fields.Contains("_groupMenu") &&
               !fields.Contains("_groupGroupsMenuItem") &&
               !fields.Contains("_ungroupGroupsMenuItem") &&
               !fields.Contains("_groupMoveUpMenuItem") &&
               !fields.Contains("_groupMoveDownMenuItem") &&
               !fields.Contains("_groupBringToFrontMenuItem") &&
               !fields.Contains("_groupSendToBackMenuItem"),
            "Obsolete top-level Group-menu wiring must be removed rather than merely hidden or disabled.");
        var outputButtonField = typeof(EditorForm).GetField("_screenMenu",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        Assert(outputButtonField?.FieldType == typeof(ToolStripDropDownButton),
            "Outputs must be a toolbar dropdown button, not a title-bar menu item.");
        Assert(fields.Contains("_groupSelectedMenuItem") &&
               fields.Contains("_ungroupSelectedMenuItem") &&
               fields.Contains("_treeGroupSelectedMenuItem") &&
               fields.Contains("_treeUngroupSelectedMenuItem") &&
               fields.Contains("_toolbarGroupButton") &&
               fields.Contains("_toolbarUngroupButton"),
            "Removing the duplicate Group menu must preserve grouping commands in Edit, Layers context menu, and toolbar surfaces.");
    }


    private static void EditorOutputsToolbarRightInset()
    {
        using var form = new Form { ClientSize = new Size(600, 100) };
        using var toolbar = new ToolStrip { Dock = DockStyle.Top, GripStyle = ToolStripGripStyle.Hidden };
        var outputs = new ToolStripDropDownButton("Outputs")
        {
            Alignment = ToolStripItemAlignment.Right
        };
        var originalMargin = outputs.Margin;
        EditorForm.ApplyOutputsToolbarRightInset(outputs);
        toolbar.Items.Add(outputs);
        form.Controls.Add(toolbar);
        form.PerformLayout();
        toolbar.PerformLayout();

        Assert(outputs.Margin.Right == 12 &&
               outputs.Margin.Left == originalMargin.Left &&
               outputs.Margin.Top == originalMargin.Top &&
               outputs.Margin.Bottom == originalMargin.Bottom,
            "Outputs toolbar inset must add 12 px on the right without changing vertical or left spacing.");
        Assert(outputs.Alignment == ToolStripItemAlignment.Right &&
               toolbar.ClientRectangle.Right - outputs.Bounds.Right >= 12,
            "Outputs must remain right aligned while leaving a visible gap from the toolbar edge.");
    }

    private static void EditorWindowChromeHitTestContract()
    {
        using var host = new EditorTitleBarHost { Size = new Size(1200, 32) };
        host.UpdateNativeMetrics(height: 32, leftInset: 44, rightInset: 138);

        Assert(host.MenuBounds == Rectangle.FromLTRB(44, 0, 1062, 32),
            "Native title-bar layout must use Windows App SDK LeftInset/RightInset instead of hand-calculated caption-button geometry.");

        using var icon = (Icon)SystemIcons.Application.Clone();
        host.SetWindowIcon(icon);
        host.ShowWindowIcon = true;
        Assert(!host.WindowIconBounds.IsEmpty && host.MenuBounds.Left > 44,
            "A fully extended title bar must reserve client-title-bar space for the app icon because Windows retains only the caption controls.");

        var centered = EditorWindowChromeContract.CenterVertically(host.MenuBounds, 24);
        Assert(centered.Left == host.MenuBounds.Left && centered.Top == 4 && centered.Height == 24,
            "Title-bar menu content must be centered inside the AppWindowTitleBar-reported height while preserving the icon reservation.");
    }


    private static void EditorTitleBarCaptionVisualExclusion()
    {
        var fields = typeof(EditorForm)
            .GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            .ToDictionary(field => field.Name, StringComparer.Ordinal);

        Assert(fields.TryGetValue("_appWindow", out var appWindowField) &&
               appWindowField.FieldType.FullName == "Microsoft.UI.Windowing.AppWindow",
            "Editor chrome must be owned by Windows App SDK AppWindow rather than manual Win32 frame emulation.");
        Assert(fields.TryGetValue("_appWindowTitleBar", out var titleBarField) &&
               titleBarField.FieldType.FullName == "Microsoft.UI.Windowing.AppWindowTitleBar",
            "Editor title-bar layout must use AppWindowTitleBar system insets and caption-button ownership.");
        Assert(fields.TryGetValue("_nonClientPointerSource", out var pointerSourceField) &&
               pointerSourceField.FieldType.FullName == "Microsoft.UI.Input.InputNonClientPointerSource",
            "Interactive title-bar menu regions must use the Windows App SDK non-client pointer source.");

        var assembly = typeof(EditorForm).Assembly;
        Assert(assembly.GetType("PinkieSysMon.Editor.EditorWindowChromeNative") is null,
            "The rejected hand-written DWM/WM_NCHITTEST frame implementation must not return.");
    }


    private static void EditorTitleBarMenuIntegration()
    {
        using var form = new Form { ClientSize = new Size(1200, 800) };
        using var host = new EditorTitleBarHost { Size = new Size(1200, 32) };
        using var menu = new EditorTitleBarMenuStrip();
        var fileMenu = new ToolStripMenuItem(EditorMainMenuContract.FileMenuText);
        var editMenu = new ToolStripMenuItem(EditorMainMenuContract.EditMenuText);
        var desktopMenu = new ToolStripMenuItem(EditorMainMenuContract.DesktopMenuText);
        var devicesMenu = new ToolStripMenuItem(EditorMainMenuContract.DevicesMenuText);
        var toolsMenu = new ToolStripMenuItem(EditorMainMenuContract.ToolsMenuText);
        var helpMenu = new ToolStripMenuItem(EditorMainMenuContract.HelpMenuText);

        EditorMainMenuContract.Populate(menu, fileMenu, editMenu, desktopMenu, devicesMenu, toolsMenu, helpMenu);
        host.Controls.Add(menu);
        form.Controls.Add(host);
        form.MainMenuStrip = menu;

        host.UpdateNativeMetrics(height: 32, leftInset: 44, rightInset: 138);
        menu.Bounds = EditorWindowChromeContract.CenterVertically(
            host.MenuBounds,
            menu.GetTitleBarPreferredHeight(verticalPadding: 3));
        menu.PerformLayout();

        Assert(ReferenceEquals(menu.Parent, host) && ReferenceEquals(form.MainMenuStrip, menu),
            "The title-bar MenuStrip must live inside the title-bar host while remaining Form.MainMenuStrip for native keyboard routing.");
        Assert(menu.Left >= 44 && menu.Right <= 1062,
            "Title-bar menu content must remain outside Windows App SDK system-reserved left/right inset regions.");

        var passthrough = EditorWindowChromeContract.GetMenuPassthroughRegions(
            menu.Bounds,
            menu.GetInteractiveItemBounds());
        Assert(helpMenu.Alignment == ToolStripItemAlignment.Right,
            "The Help menu must occupy the former right-aligned Output title-bar position.");
        Assert(passthrough.Length == 6,
            "Every visible top-level menu item must have an explicit non-client passthrough region.");

        var leftGroupRight = new[] { fileMenu, editMenu, desktopMenu, devicesMenu, toolsMenu }
            .Max(item => menu.Left + item.Bounds.Right);
        var rightGroupLeft = menu.Left + helpMenu.Bounds.Left;
        if (rightGroupLeft - leftGroupRight > 8)
        {
            var dragPoint = new Point(
                leftGroupRight + ((rightGroupLeft - leftGroupRight) / 2),
                menu.Top + Math.Max(0, menu.Height / 2));
            Assert(passthrough.All(region => !region.Contains(dragPoint)),
                "Blank/title space between left and right menu groups must remain outside passthrough regions so Windows owns dragging.");
        }
    }


    private static void EditorTitleBarVerticalAlignment()
    {
        var available = new Rectangle(24, 8, 800, 24);
        var centered = EditorWindowChromeContract.CenterVertically(available, 18);
        Assert(centered.Left == available.Left && centered.Width == available.Width && centered.Height == 18,
            "Title-bar menu centering must preserve its horizontal allocation and preferred height.");
        Assert(Math.Abs((centered.Top - available.Top) - (available.Bottom - centered.Bottom)) <= 1,
            "Title-bar menu content must be vertically centered inside the available caption band.");

        var clamped = EditorWindowChromeContract.CenterVertically(available, 100);
        Assert(clamped == available,
            "A title-bar menu taller than the available caption band must clamp to that band instead of shifting the shell layout.");
    }

    private static void EditorTitleBarMenuThemeContract()
    {
        using var menu = new EditorTitleBarMenuStrip();
        var fileMenu = new ToolStripMenuItem(EditorMainMenuContract.FileMenuText);
        menu.Items.Add(fileMenu);
        var theme = EditorShellTheme.CaptureCurrent();
        menu.ApplyTheme(theme);

        Assert(menu.Renderer is EditorTitleBarMenuRenderer,
            "The title-bar MenuStrip may custom-draw only its client content; the window frame itself remains Windows-owned.");
        Assert(menu.BackColor == Color.Transparent &&
               menu.ForeColor.ToArgb() == theme.Foreground.ToArgb() &&
               fileMenu.ForeColor.ToArgb() == theme.Foreground.ToArgb(),
            "Title-bar menu background must remain transparent while labels use the shared shell foreground palette.");
        Assert(theme.IsDark ? theme.Foreground.GetBrightness() > theme.Background.GetBrightness() : true,
            "Dark shell custom content must use a foreground brighter than its background.");
    }


    private static void EditorGeneralSettingsContract()
    {
        var config = new AppConfig();
        config.Runtime.IdleTimeoutMinutes = 17;
        config.Runtime.LogRetentionDays = 7;
        using var dialog = new EditorGeneralSettingsDialog(
            startRuntimeWithWindows: true,
            systemProviderEnabled: true,
            libreHardwareMonitorProviderEnabled: false,
            icueProviderEnabled: true,
            toolbarIconSize: 48,
            themeMode: EditorThemeMode.Dark,
            config: config);
        Assert(!dialog.Controls.OfType<TabControl>().Any(),
            "Editor Settings must be a standalone non-tabbed dialog.");
        Assert(dialog.Padding == new Padding(12),
            "Editor Settings must keep its controls inset from the window edges.");
        Assert(dialog.StartRuntimeWithWindows && dialog.SystemProviderEnabled &&
               !dialog.LibreHardwareMonitorProviderEnabled && dialog.IcueProviderEnabled &&
               dialog.ToolbarIconSize == 48 && dialog.ThemeMode == EditorThemeMode.Dark,
            "General Settings must preserve startup/provider/theme/icon values passed by their existing owners.");
        var groups = dialog.Controls.OfType<Panel>().Single(panel => panel.Controls.OfType<GroupBox>().Any()).Controls.OfType<GroupBox>().ToArray();
        Assert(groups.Select(group => group.Text).ToHashSet()
                .SetEquals(["Startup", "Runtime activity", "Logging", "Metric providers", "Interface"]),
            "General tab must expose the Runtime idle setting but no legacy device binding or per-output settings.");
        var idleLayout = groups.Single(group => group.Text == "Runtime activity")
            .Controls.OfType<TableLayoutPanel>().Single();
        var idleTimeout = idleLayout.Controls.OfType<NumericUpDown>().Single();
        Assert(dialog.RuntimeIdleTimeoutMinutes == 17 && idleTimeout.Value == 17 &&
               idleTimeout.Minimum == 0 && idleTimeout.Maximum == RuntimeIdlePolicy.MaximumTimeoutMinutes,
            "Runtime idle setting must preserve its configured value and supported range.");
        var loggingLayout = groups.Single(group => group.Text == "Logging")
            .Controls.OfType<TableLayoutPanel>().Single();
        var retention = loggingLayout.Controls.OfType<NumericUpDown>().Single();
        Assert(dialog.LogRetentionDays == 7 && retention.Value == 7 &&
               retention.Minimum == LogRetentionPolicy.MinimumDays &&
               retention.Maximum == LogRetentionPolicy.MaximumDays &&
               loggingLayout.Controls.OfType<Label>().Any(label => label.Text == "Keep logs (days)"),
            "General Settings must expose persisted global log retention with explicit limits.");
        var generalNumericControls = groups.SelectMany(group => group.Controls.OfType<TableLayoutPanel>())
            .SelectMany(layout => layout.Controls.OfType<NumericUpDown>()).ToArray();
        Assert(generalNumericControls.Length == 2 &&
               generalNumericControls.Contains(idleTimeout) && generalNumericControls.Contains(retention) &&
               !dialog.Controls.OfType<NumericUpDown>().Any(),
            "General must contain only app-wide idle and logging numeric controls, not Output device options.");
    }

    private static void EditorDesktopSettingsContract()
    {
        var config = new AppConfig();
        var original = config.Outputs.Targets.Single();
        original.Name = "Left panel";
        original.Desktop!.BoardEnabled = false;
        original.Desktop.ZOrder = 17; // Earlier user order may contain gaps.
        original.ProviderEnabled = true;
        original.Desktop.Lock = true;
        original.RefreshIntervalMs = 500;
        var physical = new OutputTargetConfig
        { Width = 1920, Height = 480, DeviceId = "usb-interface:857aa06d11e4955e39fabdc4ac52f49f61d034e95b9a5b34ebdc113c9f678f7d",
            Id = "stage22-physical",
            Name = "Disabled hardware",
            Kind = OutputTargetContract.PhysicalKind,
            Transport = OutputTargetContract.LyBulkTransport,
            DeviceEnabled = false,
            Dashboard = "Default"
        };
        config.Outputs.Targets.Add(physical);
        var model = new DesktopSettingsModel(config);
        Assert(model.Desktops.Count == 1 && !model.IsDirty &&
               !model.Desktops[0].Desktop!.BoardEnabled && model.Desktops[0].ProviderEnabled &&
               model.Desktops[0].Desktop!.Lock && model.Desktops[0].RefreshIntervalMs == 500,
            "Desktop Settings must select a detached working copy preserving independent flags and refresh.");
        model.Desktops[0].Name = "Changed panel";
        model.MarkChanged();
        Assert(original.Name == "Left panel",
            "Editing a Desktop in Settings must not change the committed config before OK.");
        var added = model.Create("Default");
        Assert(model.Desktops[0].Desktop!.ZOrder == 0 && added.Desktop!.ZOrder == 1,
            "A newly created Desktop must appear at the front of Z-order after old gaps are normalized.");
        added.ProviderEnabled = false;
        added.Desktop!.Lock = false;
        added.Desktop.BoardEnabled = true;
        added.RefreshIntervalMs = 333;
        model.MarkChanged();
        Assert(DesktopInsertionPolicy.SlotForItem(1, false) == 1 &&
               DesktopInsertionPolicy.SlotForItem(1, true) == 2 &&
               DesktopInsertionPolicy.DestinationIndex(3, 0, 3) == 2 &&
               DesktopInsertionPolicy.DestinationIndex(3, 2, 0) == 0 &&
               DesktopInsertionPolicy.DestinationIndex(3, 1, 1) == 1 &&
               DesktopInsertionPolicy.DestinationIndex(3, 1, 2) == 1 &&
               DesktopInsertionPolicy.DestinationIndex(3, 2, 2) == 2 &&
               DesktopInsertionPolicy.DestinationIndex(3, -1, 0) == -1 &&
               DesktopInsertionPolicy.DestinationIndex(3, 1, 4) == -1,
            "Desktop insertion preview must map before/after boundaries to the correct final list position.");
        model.Move(1, 0);
        Assert(model.Desktops[0] == added && added.Desktop.ZOrder == 0 &&
               model.Desktops[1].Desktop!.ZOrder == 1,
            "Dragging Desktop entries must reorder the separate output Z-order deterministically.");
        model.ApplyTo(config);
        Assert(config.Outputs.Targets.Count == 3 &&
               ReferenceEquals(config.Outputs.Targets.Single(t => t.Kind == OutputTargetContract.PhysicalKind), physical) &&
               config.Outputs.Targets.Where(t => t.Kind == OutputTargetContract.DesktopKind).Select(t => t.Id)
                   .SequenceEqual(new[] { added.Id, original.Id }) &&
               config.Outputs.Targets[0].RefreshIntervalMs == 333 &&
               !config.Outputs.Targets[0].ProviderEnabled,
            "Applying a Desktop dialog must preserve the physical Output object and each sibling's independent settings.");
        var secondModel = new DesktopSettingsModel(config);
        var extra = secondModel.Desktops.Single(t => t.Id == added.Id);
        secondModel.Remove(extra);
        secondModel.ApplyTo(config);
        Assert(config.Outputs.Targets.Count == 2 &&
               ReferenceEquals(config.Outputs.Targets[1], physical) &&
               config.Outputs.Targets[0].Name == "Changed panel" &&
               config.Outputs.Targets[0].ProviderEnabled,
            "Desktop Remove must delete only the selected virtual Output; preserve remaining providers and physical targets.");

        var single = new DesktopSettingsModel(new AppConfig());
        Assert(!single.CanRemove(single.Desktops.Single()),
            "Removing the final Output must remain prohibited.");
        var rejected = false;
        try { single.Remove(single.Desktops.Single()); }
        catch (InvalidOperationException) { rejected = true; }
        Assert(rejected, "Direct Remove calls must reject deletion of the last Output.");

        using var dialog = new DesktopSettingsDialog(new AppConfig(), ["Default"]);
        Assert(!dialog.Controls.OfType<TabControl>().Any(),
            "Desktop Settings must be a standalone non-tabbed dialog.");
        Assert(dialog.Padding == new Padding(12),
            "Desktop Settings must keep its controls inset from the window edges.");
        var desktopPage = dialog.Controls.OfType<Panel>().Single(panel => panel.Controls.OfType<TableLayoutPanel>().Any());
        Assert(desktopPage.Controls.OfType<TableLayoutPanel>().Single().ColumnCount == 2,
            "Desktop Settings must use a single two-column master/detail layout.");
        Assert(!dialog.DesktopChanged,
            "Opening Desktop Settings must not modify persisted configuration.");
        Assert(dialog.AcceptButton is Button { DialogResult: DialogResult.OK } &&
               dialog.CancelButton is Button { DialogResult: DialogResult.Cancel },
            "Desktop Settings must own OK/Cancel instead of relying on the former shared Settings dialog.");
        Assert(dialog.Controls.OfType<Label>().Any(label =>
                   label.Text == DesktopSettingsDialog.ApplyTimingNotice &&
                   label.Text.Contains("after OK", StringComparison.Ordinal) &&
                   label.Text.Contains("next start", StringComparison.Ordinal)),
            "Desktop Settings must explain when saved Outputs take effect.");
        var desktopList = desktopPage.Controls.OfType<TableLayoutPanel>().Single()
            .Controls.OfType<TableLayoutPanel>().Single()
            .Controls.OfType<ListBox>().Single();
        Assert(desktopList.DrawMode == DrawMode.OwnerDrawFixed,
            "Desktop list must support painting the precise before/after insertion marker.");
    }

    private static void EditorThemeSettingsContract()
    {
        Assert(EditorThemeContract.Parse(null) == EditorThemeMode.System &&
               EditorThemeContract.Parse("") == EditorThemeMode.System &&
               EditorThemeContract.Parse("System") == EditorThemeMode.System,
            "Missing/invalid legacy editor theme settings must default to System mode.");
        Assert(EditorThemeContract.Parse("Light") == EditorThemeMode.Light &&
               EditorThemeContract.Parse("dark") == EditorThemeMode.Dark,
            "Editor theme settings must accept the persisted System/Light/Dark contract case-insensitively.");
        Assert(EditorThemeContract.ToSystemColorMode(EditorThemeMode.System) == SystemColorMode.System &&
               EditorThemeContract.ToSystemColorMode(EditorThemeMode.Light) == SystemColorMode.Classic &&
               EditorThemeContract.ToSystemColorMode(EditorThemeMode.Dark) == SystemColorMode.Dark,
            "Editor theme modes must map directly to the supported WinForms .NET 10 color-mode API.");

        var propertyTabsField = typeof(EditorForm)
            .GetField("_propertyTabs", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        Assert(propertyTabsField?.FieldType == typeof(EditorThemedTabControl),
            "Property tabs must opt into WinForms implicit theming through CreateParams rather than private control internals or ad-hoc recoloring.");

        using var propertyGrid = new PropertyGrid();
        var currentTheme = EditorShellTheme.CaptureCurrent();
        EditorPropertyGridTheme.Apply(propertyGrid, currentTheme with { IsDark = true });
        Assert(!propertyGrid.CanShowVisualStyleGlyphs,
            "Dark PropertyGrid must use the supported classic expansion-glyph path so +/- controls remain visible.");
        EditorPropertyGridTheme.Apply(propertyGrid, currentTheme with { IsDark = false });
        Assert(propertyGrid.CanShowVisualStyleGlyphs,
            "Light PropertyGrid must retain the normal OS visual-style expansion glyphs.");

        var tempRoot = Path.Combine(Path.GetTempPath(), $"PinkieSysMon-editor-theme-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        try
        {
            var path = Path.Combine(tempRoot, "editor.json");
            var settings = new EditorSettings { Theme = EditorThemeContract.DarkSetting };
            settings.Save(path);
            var loaded = EditorSettings.Load(path);
            Assert(loaded.Theme == EditorThemeContract.DarkSetting,
                "Editor theme choice must persist in editor.json independently of dashboard.json semantics.");
        }
        finally
        {
            try { Directory.Delete(tempRoot, recursive: true); }
            catch { }
        }
    }

}
