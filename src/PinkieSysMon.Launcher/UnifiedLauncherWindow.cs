using System.Diagnostics;
using System.Drawing;
using PinkieSysMon.Editor;

namespace PinkieSysMon.Launcher;

// One non-modal owner for Application/Settings/About. Each tab shares one
// detached OK/Cancel draft; one-shot launch/shortcut actions never commit it.
internal sealed class UnifiedLauncherWindow : Form
{
    private readonly string _root;
    private readonly Action<string, Exception?>? _report;
    private readonly LauncherSettingsDraft? _draft;
    private readonly TabControl _tabs = new() { Dock = DockStyle.Fill };
    private readonly Button _ok = new() { Text = "OK", AutoSize = true };
    private readonly Button _cancel = new() { Text = "Cancel", AutoSize = true };
    private readonly Label _status = new() { AutoSize = true, Anchor = AnchorStyles.Left };
    private readonly ToolTip _toolTips = new();
    private bool _applying;

    internal TabControl Tabs => _tabs;
    internal LauncherSettingsDraft? Draft => _draft;

    internal UnifiedLauncherWindow(string root, string version,
        Action<string, Exception?>? report = null,
        LauncherSettingsDraft? draftForTests = null)
    {
        _root = EditorSessionWire.NormalizeRoot(root);
        _report = report;
        Text = ProjectPresentation.Title + " — Settings";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(850, 610);
        ClientSize = new Size(980, 700);
        AutoScaleMode = AutoScaleMode.Font;
        Icon = System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath) ??
            (Icon)SystemIcons.Application.Clone();
        string? loadingError = null;
        try { _draft = draftForTests ?? LauncherSettingsDraft.Open(_root); }
        catch (Exception error)
        {
            loadingError = error.Message;
            _report?.Invoke("Launcher Settings configuration could not be read.", error);
        }

        _tabs.TabPages.Add(CreateApplicationTab());
        _tabs.TabPages.Add(CreateCommonTab());
        _tabs.TabPages.Add(CreateRuntimeTab());
        _tabs.TabPages.Add(CreateEditorTab());
        _tabs.TabPages.Add(CreateProvidersTab());
        _tabs.TabPages.Add(CreateAboutTab(version));

        var footer = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 3, RowCount = 1,
            Padding = new Padding(10, 5, 10, 5)
        };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _status.Text = loadingError is null ? "Settings changes apply only after OK." :
            "Settings unavailable: " + loadingError;
        _status.AutoEllipsis = true;
        _status.AutoSize = false;
        _status.Dock = DockStyle.Fill;
        _status.TextAlign = ContentAlignment.MiddleLeft;
        footer.Controls.Add(_status, 0, 0);
        footer.Controls.Add(_ok, 1, 0);
        footer.Controls.Add(_cancel, 2, 0);
        _ok.Enabled = _draft is not null;
        _ok.Click += async (_, _) => await ApplyAsync();
        _cancel.Click += (_, _) => Close();
        AcceptButton = _ok;
        CancelButton = _cancel;

        var shell = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
        shell.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        shell.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        shell.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        shell.Controls.Add(_tabs, 0, 0);
        shell.Controls.Add(footer, 0, 1);
        Controls.Add(shell);

        if (loadingError is not null)
            foreach (TabPage tab in _tabs.TabPages)
                if (tab.Text is not ("Application" or "About"))
                    tab.Enabled = false;
        FormClosing += (_, e) =>
        {
            if (_applying) e.Cancel = true;
        };
    }

    internal void SelectTab(LauncherTab tab, bool activateWindow = true)
    {
        if (IsDisposed || Disposing)
            throw new ObjectDisposedException(nameof(UnifiedLauncherWindow));
        var index = (int)tab;
        if (index < 0 || index >= _tabs.TabPages.Count)
            throw new ArgumentOutOfRangeException(nameof(tab));
        _tabs.SelectedIndex = index;
        if (activateWindow)
        {
            if (WindowState == FormWindowState.Minimized)
                WindowState = FormWindowState.Normal;
            Show();
            Activate();
        }
    }

    private TabPage Page(string caption, out TableLayoutPanel body)
    {
        var page = new TabPage(caption);
        body = new TableLayoutPanel
        {
            Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1,
            Padding = new Padding(18, 12, 18, 12)
        };
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        var scroller = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
        scroller.Controls.Add(body);
        page.Controls.Add(scroller);
        return page;
    }

    private static GroupBox Group(string title, params Control[] controls)
    {
        var group = new GroupBox
        {
            Text = title, Dock = DockStyle.Top, AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(12)
        };
        var layout = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        foreach (var control in controls)
        {
            control.Dock = DockStyle.Top;
            control.Margin = new Padding(4, 6, 4, 6);
            layout.Controls.Add(control);
        }
        group.Controls.Add(layout);
        return group;
    }

    private static void AddGroup(TableLayoutPanel body, Control group) =>
        body.Controls.Add(group, 0, body.RowCount++);

    private static Label Hint(string text) =>
        new() { Text = text, AutoSize = true, MaximumSize = new Size(800, 0) };

    private NumericUpDown Number(int min, int max, int value, Action<int> update, string description)
    {
        var numeric = new NumericUpDown
        {
            Minimum = min, Maximum = max, Value = Math.Clamp(value, min, max),
            Width = 110, AutoSize = true
        };
        _toolTips.SetToolTip(numeric, description);
        numeric.ValueChanged += (_, _) => update((int)numeric.Value);
        return numeric;
    }

    private static TableLayoutPanel Field(string caption, Control control)
    {
        var row = new TableLayoutPanel { AutoSize = true, ColumnCount = 2, RowCount = 1, Dock = DockStyle.Top };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 280));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        row.Controls.Add(new Label { Text = caption, AutoSize = true, Anchor = AnchorStyles.Left }, 0, 0);
        control.Anchor = AnchorStyles.Left;
        row.Controls.Add(control, 1, 0);
        return row;
    }

    private Button ActionButton(string title, Action action)
    {
        var button = new Button { Text = title, AutoSize = true, MinimumSize = new Size(200, 40) };
        button.Click += (_, _) =>
        {
            try { action(); _status.Text = title + ": requested."; }
            catch (Exception error)
            {
                _report?.Invoke(title + " failed.", error);
                MessageBox.Show(this, error.Message, title, MessageBoxButtons.OK, MessageBoxIcon.Error);
                _status.Text = title + ": failed.";
            }
        };
        return button;
    }

    private TabPage CreateApplicationTab()
    {
        var tab = Page("Application", out var body);
        AddGroup(body, Group("Application launch",
            Hint("These actions take effect immediately and do not discard the Settings draft."),
            ActionButton("Launch Control Center", () => ProjectLauncherActions.Run(_root, "--editor")),
            ActionButton("Launch Runtime", () => ProjectLauncherActions.Run(_root, "--runtime")),
            ActionButton("Launch Both", () => ProjectLauncherActions.Run(_root, "--both"))));
        AddGroup(body, Group("Shortcuts",
            Hint("Create or update the existing Launcher, Control Center and Runtime shortcuts."),
            ActionButton("Create Desktop shortcuts",
                () => ProjectShortcutInstaller.Create(_root, startMenu: false, this)),
            ActionButton("Create Start Menu group",
                () => ProjectShortcutInstaller.Create(_root, startMenu: true, this))));
        return tab;
    }

    private TabPage CreateCommonTab()
    {
        var tab = Page("Common", out var body);
        var values = _draft?.Values ?? new LauncherSettingsValues();
        AddGroup(body, Group("Logging",
            Field("Keep logs (days)", Number(1, 365, values.LogRetentionDays,
                v => values.LogRetentionDays = v, "Shared Runtime, Editor and Errors log retention."))));
        AddGroup(body, Group("Rendering",
            Field("Raster sharpness (0–100)", Number(0, 100, values.RasterSharpness,
                v => values.RasterSharpness = v, "Edge sharpening after raster image downscaling."))));
        return tab;
    }

    private TabPage CreateRuntimeTab()
    {
        var tab = Page("Runtime", out var body);
        var values = _draft?.Values ?? new LauncherSettingsValues();
        var startup = new CheckBox
        {
            Text = "Start Runtime when signing in to Windows",
            Checked = values.StartRuntimeWithWindows, AutoSize = true
        };
        startup.CheckedChanged += (_, _) => values.StartRuntimeWithWindows = startup.Checked;
        AddGroup(body, Group("Startup", startup));
        AddGroup(body, Group("Runtime activity",
            Field("Idle timeout (minutes; 0 = off)", Number(0, 1440, values.RuntimeIdleTimeoutMinutes,
                v => values.RuntimeIdleTimeoutMinutes = v, "Pause Runtime activity after input inactivity."))));
        return tab;
    }

    private TabPage CreateEditorTab()
    {
        var tab = Page("Editor", out var body);
        var values = _draft?.Values ?? new LauncherSettingsValues();
        var theme = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 180 };
        theme.Items.AddRange(["System", "Light", "Dark"]);
        theme.SelectedItem = values.Theme;
        theme.SelectedIndexChanged += (_, _) => values.Theme = theme.SelectedItem as string ?? "System";
        var icons = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 100 };
        icons.Items.AddRange(["16", "32", "48"]);
        icons.SelectedItem = values.ToolbarIconSize.ToString();
        icons.SelectedIndexChanged += (_, _) =>
        {
            if (int.TryParse(icons.SelectedItem as string, out var size))
                values.ToolbarIconSize = size;
        };
        AddGroup(body, Group("Interface",
            Field("Theme", theme),
            Field("Toolbar icon size (px)", icons)));
        return tab;
    }

    private TabPage CreateProvidersTab()
    {
        var tab = Page("Providers", out var body);
        var values = _draft?.Values ?? new LauncherSettingsValues();
        CheckBox Provider(string name, bool enabled, Action<bool> setValue)
        {
            var item = new CheckBox { Text = name, AutoSize = true, Checked = enabled };
            item.CheckedChanged += (_, _) => setValue(item.Checked);
            return item;
        }
        var system = Provider("System", values.SystemProviderEnabled, v => values.SystemProviderEnabled = v);
        var lhm = Provider("LibreHardwareMonitor", values.LhmProviderEnabled, v => values.LhmProviderEnabled = v);
        var icue = Provider("iCUE", values.IcueProviderEnabled, v => values.IcueProviderEnabled = v);
        var filesystem = Provider("Filesystem", values.FilesystemProviderEnabled, v =>
        {
            if (v && !values.FilesystemProviderEnabled)
            {
                using var warning = new FilesystemEnableWarningDialog();
                if (warning.ShowDialog(this) != DialogResult.OK)
                {
                    // Restore the previous model value, not a new side effect.
                    var box = _filesystemCheckbox;
                    if (box is not null) box.Checked = false;
                    return;
                }
            }
            values.FilesystemProviderEnabled = v;
        });
        _filesystemCheckbox = filesystem;
        var sources = new Button { Text = "Sources...", AutoSize = true };
        sources.Click += (_, _) =>
        {
            using var dialog = new FilesystemSourcesDialog(values.FilesystemSources);
            if (dialog.ShowDialog(this) == DialogResult.OK)
                values.FilesystemSources = dialog.Sources.Select(s => s.Copy()).ToList();
        };
        var sourceRow = new TableLayoutPanel
        {
            AutoSize = true, ColumnCount = 2, RowCount = 1, Dock = DockStyle.Top
        };
        sourceRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 280));
        sourceRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        sourceRow.Controls.Add(filesystem, 0, 0);
        sourceRow.Controls.Add(sources, 1, 0);
        AddGroup(body, Group("Metric providers", system, lhm, icue, sourceRow));
        return tab;
    }

    private CheckBox? _filesystemCheckbox;

    private TabPage CreateAboutTab(string version)
    {
        var tab = Page("About", out var body);
        var website = new LinkLabel { Text = ProjectPresentation.Website, AutoSize = true };
        website.LinkClicked += (_, _) => OpenWebsite(ProjectPresentation.Website);
        var supported = new LinkLabel { Text = ProjectPresentation.SupportedDevicesUrl, AutoSize = true };
        supported.LinkClicked += (_, _) => OpenWebsite(ProjectPresentation.SupportedDevicesUrl);
        AddGroup(body, Group(ProjectPresentation.Title,
            Hint("Version " + version),
            Hint(ProjectPresentation.Description),
            website,
            Hint("Supported USB devices"),
            supported));
        return tab;
    }

    private void OpenWebsite(string address)
    {
        try { Process.Start(new ProcessStartInfo(address) { UseShellExecute = true }); }
        catch (Exception error)
        {
            _report?.Invoke("Could not open project website.", error);
            MessageBox.Show(this, error.Message, "Open Website", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async Task ApplyAsync()
    {
        if (_applying || _draft is null) return;
        _applying = true;
        _ok.Enabled = _cancel.Enabled = false;
        _status.Text = "Validating and coordinating Settings...";
        try
        {
            // One dedicated worker thread owns the transaction mutex through
            // all synchronous IPC waits. The UI remains responsive.
            var outcome = await Task.Run(() => LauncherSettingsCoordinator.Apply(_draft));
            if (outcome.Success)
            {
                _status.Text = outcome.Message;
                DialogResult = DialogResult.OK;
                _applying = false;
                Close();
                return;
            }
            _status.Text = outcome.Partial ? "Partial Settings outcome — inspect the details." :
                "Settings were not applied.";
            _report?.Invoke("Settings apply failed: " + outcome.Message, null);
            MessageBox.Show(this, outcome.Message, outcome.Partial ?
                "Settings partially applied" : "Settings not applied",
                MessageBoxButtons.OK, outcome.Partial ? MessageBoxIcon.Warning : MessageBoxIcon.Error);
            // After a partial/failed transaction, stale snapshots must never
            // be reused for a second blind OK. Reopen Settings for a fresh draft.
            _ok.Enabled = false;
        }
        catch (Exception error)
        {
            _status.Text = "Settings validation failed; configuration was not committed.";
            _report?.Invoke("Settings transaction did not complete.", error);
            MessageBox.Show(this, error.Message, "Settings", MessageBoxButtons.OK, MessageBoxIcon.Error);
            _ok.Enabled = false;
        }
        finally
        {
            _applying = false;
            _cancel.Enabled = true;
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _toolTips.Dispose();
        base.Dispose(disposing);
    }
}
