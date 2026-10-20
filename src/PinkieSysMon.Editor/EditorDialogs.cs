namespace PinkieSysMon.Editor;

internal enum SaveChangesChoice
{
    Save,
    DontSave,
    Cancel
}

internal sealed class NewDashboardDialog : PinkieEditorForm
{
    // Convenience values for manual Editor creation only; not a factory whitelist.
    internal static readonly (int Width, int Height)[] ResolutionPresets =
    [
        (320, 240), (320, 320), (320, 480), (480, 320), (480, 480),
        (640, 480), (640, 640), (800, 480), (960, 320), (1024, 600),
        (1280, 480), (1280, 720), (1280, 800), (1600, 720),
        (1920, 462), (1920, 480), (1920, 720)
    ];

    private readonly TextBox _name = new() { Dock = DockStyle.Fill };
    private readonly ComboBox _resolution = new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
    private readonly ComboBox _orientation = new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };

    public NewDashboardDialog()
    {
        Text = "New Dashboard";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(440, 185);

        foreach (var (width, height) in ResolutionPresets)
            _resolution.Items.Add(width == 640 && height == 640
                ? "640 × 640 (Desktop)" : $"{width} × {height}");
        _resolution.SelectedIndex = 6;
        _orientation.Items.AddRange(["0°", "90°", "180°", "270°"]);
        _orientation.SelectedIndex = 0;

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(12),
            ColumnCount = 2,
            RowCount = 4
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        layout.Controls.Add(new Label { Text = "Name:", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 6, 10, 8) }, 0, 0);
        layout.Controls.Add(_name, 1, 0);
        layout.Controls.Add(new Label { Text = "Resolution:", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 6, 10, 8) }, 0, 1);
        layout.Controls.Add(_resolution, 1, 1);
        layout.Controls.Add(new Label { Text = "Orientation:", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 6, 10, 8) }, 0, 2);
        layout.Controls.Add(_orientation, 1, 2);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            AutoSize = true,
            WrapContents = false,
            Padding = new Padding(0, 10, 0, 0)
        };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };
        var create = new Button { Text = "Create", DialogResult = DialogResult.OK, AutoSize = true };
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(create);
        layout.Controls.Add(buttons, 0, 3);
        layout.SetColumnSpan(buttons, 2);

        Controls.Add(layout);
        AcceptButton = create;
        CancelButton = cancel;
        Shown += (_, _) => _name.Focus();
    }

    public string DashboardName => _name.Text;
    public (int Width, int Height) Resolution => ResolutionPresets[_resolution.SelectedIndex];
    public int Orientation => _orientation.SelectedIndex switch { 1 => 90, 2 => 180, 3 => 270, _ => 0 };
}

internal sealed class CloneDashboardDialog : PinkieEditorForm
{
    private readonly TextBox _name = new() { Dock = DockStyle.Fill };

    public CloneDashboardDialog(string suggestedName)
    {
        Text = "Clone Dashboard";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(440, 108);

        _name.Text = suggestedName;

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(12),
            ColumnCount = 2,
            RowCount = 2
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        layout.Controls.Add(new Label { Text = "Name:", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 6, 10, 8) }, 0, 0);
        layout.Controls.Add(_name, 1, 0);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            AutoSize = true,
            WrapContents = false,
            Padding = new Padding(0, 10, 0, 0)
        };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };
        var clone = new Button { Text = "Clone", DialogResult = DialogResult.OK, AutoSize = true };
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(clone);
        layout.Controls.Add(buttons, 0, 1);
        layout.SetColumnSpan(buttons, 2);

        Controls.Add(layout);
        AcceptButton = clone;
        CancelButton = cancel;
        Shown += (_, _) =>
        {
            _name.Focus();
            _name.SelectAll();
        };
    }

    public string DashboardName => _name.Text;
}

internal sealed class OpenDashboardDialog : PinkieEditorForm
{
    private readonly ListBox _list = new() { Dock = DockStyle.Fill, IntegralHeight = false };

    public OpenDashboardDialog(IEnumerable<string> dashboards, string currentDashboard)
    {
        Text = "Open Dashboard";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.Sizable;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(420, 420);
        MinimumSize = new Size(320, 260);

        foreach (var dashboard in dashboards)
            _list.Items.Add(dashboard);

        if (_list.Items.Count > 0)
        {
            var index = _list.Items.IndexOf(currentDashboard);
            _list.SelectedIndex = index >= 0 ? index : 0;
        }

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 46,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(8),
            WrapContents = false
        };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };
        var open = new Button { Text = "Open", DialogResult = DialogResult.OK, AutoSize = true, Enabled = _list.SelectedItem is not null };
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(open);

        _list.SelectedIndexChanged += (_, _) => open.Enabled = _list.SelectedItem is not null;
        _list.DoubleClick += (_, _) =>
        {
            if (_list.SelectedItem is not null)
            {
                DialogResult = DialogResult.OK;
                Close();
            }
        };

        Controls.Add(_list);
        Controls.Add(buttons);
        AcceptButton = open;
        CancelButton = cancel;
    }

    public string? SelectedDashboard => _list.SelectedItem as string;
}

internal static class EditorDialogs
{
    public static OutputDashboardChoice ChooseOutputDashboard(
        IWin32Window owner, RuntimeOutputStatus output, string editorDashboard)
    {
        using var dialog = new Form
        {
            Text = "Reload Output",
            StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MinimizeBox = false, MaximizeBox = false, ShowInTaskbar = false,
            ClientSize = new Size(620, 230)
        };
        var label = new Label
        {
            Text = $"Output: {output.TargetName} ({output.TargetId})\n" +
                $"Assigned dashboard: {output.Dashboard}\nEditor dashboard: {editorDashboard}\n\n" +
                "Use the editor dashboard on this Output, or reload its assigned dashboard. Runtime uses the last saved version.",
            Dock = DockStyle.Fill, AutoSize = false, TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(16)
        };
        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom, Height = 58, FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(10), WrapContents = false
        };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };
        var keep = new Button { Text = "Keep assigned dashboard", DialogResult = DialogResult.No, AutoSize = true };
        var use = new Button { Text = "Use editor dashboard", DialogResult = DialogResult.Yes, AutoSize = true };
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(keep);
        buttons.Controls.Add(use);
        dialog.Controls.Add(label);
        dialog.Controls.Add(buttons);
        dialog.AcceptButton = cancel;
        dialog.CancelButton = cancel;
        return dialog.ShowDialog(owner) switch
        {
            DialogResult.Yes => OutputDashboardChoice.UseEditorDashboard,
            DialogResult.No => OutputDashboardChoice.KeepAssignedDashboard,
            _ => OutputDashboardChoice.Cancel
        };
    }

    public static SaveChangesChoice ConfirmSaveChanges(IWin32Window owner, string dashboardName)
    {
        using var dialog = new Form
        {
            Text = "PinkieSysMon Editor",
            StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MinimizeBox = false,
            MaximizeBox = false,
            ShowInTaskbar = false,
            ClientSize = new Size(500, 145)
        };

        var label = new Label
        {
            Text = $"Save changes to \"{dashboardName}\"?",
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleLeft,
            Dock = DockStyle.Top,
            Height = 75,
            Padding = new Padding(16, 12, 16, 0)
        };

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 58,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(10),
            WrapContents = false
        };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };
        var dontSave = new Button { Text = "Don't Save", DialogResult = DialogResult.No, AutoSize = true };
        var save = new Button { Text = "Save", DialogResult = DialogResult.Yes, AutoSize = true };
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(dontSave);
        buttons.Controls.Add(save);

        dialog.Controls.Add(label);
        dialog.Controls.Add(buttons);
        dialog.AcceptButton = save;
        dialog.CancelButton = cancel;

        return dialog.ShowDialog(owner) switch
        {
            DialogResult.Yes => SaveChangesChoice.Save,
            DialogResult.No => SaveChangesChoice.DontSave,
            _ => SaveChangesChoice.Cancel
        };
    }
}
