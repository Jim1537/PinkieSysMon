namespace PinkieSysMon.Editor;

// Desktop Output settings have an independent draft and save boundary.
internal sealed class DesktopSettingsDialog : PinkieEditorForm
{
    private readonly EditorSettingsHelp _help;
    private readonly DesktopSettingsModel _desktops;
    private readonly IReadOnlyList<string> _dashboardChoices;
    private readonly ListBox _desktopList = new()
    {
        Dock = DockStyle.Fill, IntegralHeight = false, AllowDrop = true,
        DrawMode = DrawMode.OwnerDrawFixed
    };
    private readonly Button _createDesktop = new() { Text = "Create New", AutoSize = true };
    private readonly Button _removeDesktop = new() { Text = "Remove", AutoSize = true };
    private readonly TextBox _desktopName = new() { Dock = DockStyle.Fill };
    private readonly ComboBox _desktopDashboard = new()
    {
        Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList
    };
    private readonly CheckBox _boardEnabled = new() { Text = EditorSettingsHelp.OutputEnabledCaption, AutoSize = true };
    private readonly CheckBox _providerEnabled = new() { Text = "Provider Enabled", AutoSize = true };
    private readonly CheckBox _desktopLock = new() { Text = "Lock (click-through)", AutoSize = true };
    private readonly NumericUpDown _desktopInterval = new()
    {
        Minimum = 10, Maximum = int.MaxValue, Width = 160, ThousandsSeparator = true
    };
    private readonly NumericUpDown _desktopX = new()
    {
        Minimum = int.MinValue, Maximum = int.MaxValue, Width = 160
    };
    private readonly NumericUpDown _desktopY = new()
    {
        Minimum = int.MinValue, Maximum = int.MaxValue, Width = 160
    };
    private readonly Panel _desktopDetails = new() { Dock = DockStyle.Fill, AutoScroll = true };
    private OutputTargetConfig? _selectedDesktop;
    private bool _bindingDesktop;
    private int _desktopDragIndex = -1;
    private Point _desktopDragStart;
    private int _desktopInsertionSlot = -1;

    public DesktopSettingsDialog(AppConfig config, IReadOnlyList<string> dashboards,
        string? applicationRoot = null)
    {
        _help = new EditorSettingsHelp(applicationRoot);
        _dashboardChoices = dashboards;
        _desktops = new DesktopSettingsModel(config);
        Text = "Desktop Settings";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(900, 600);
        Padding = new Padding(12);

        Controls.Add(BuildDesktopPage());
        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom, Height = 52, FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(8), WrapContents = false
        };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };
        var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, AutoSize = true };
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(ok);
        AcceptButton = ok;
        CancelButton = cancel;
        var notice = new Label
        {
            Dock = DockStyle.Bottom, Height = 54,
            Padding = new Padding(12, 4, 12, 4),
            TextAlign = ContentAlignment.MiddleLeft,
            Text = ApplyTimingNotice
        };
        Controls.Add(notice);
        Controls.Add(buttons);
    }

    internal const string ApplyTimingNotice =
        "Changes take effect after OK (Cancel discards them). " +
        "A responsive Runtime reloads the saved Desktop Outputs; otherwise they take effect on its next start.";

    public bool DesktopChanged => _desktops.IsDirty;

    public void ApplyOutputChanges(AppConfig config) => _desktops.ApplyTo(config);

    private bool CanRemoveDesktop(OutputTargetConfig target) => _desktops.CanRemove(target);

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _help.Dispose();
        base.Dispose(disposing);
    }

    private Panel BuildDesktopPage()
    {
        var page = new Panel { Dock = DockStyle.Fill };
        var columns = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Padding = new Padding(8)
        };
        columns.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 235));
        columns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        columns.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var left = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
        left.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        left.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        var leftButtons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false
        };
        leftButtons.Controls.Add(_createDesktop);
        leftButtons.Controls.Add(_removeDesktop);
        _help.Bind(_createDesktop, EditorSettingsHelp.CreateDesktop);
        _help.Bind(_removeDesktop, EditorSettingsHelp.RemoveDesktop);
        _help.Bind(_desktopList, EditorSettingsHelp.Stacking);
        left.Controls.Add(_desktopList, 0, 0);
        left.Controls.Add(leftButtons, 0, 1);
        columns.Controls.Add(left, 0, 0);

        var details = new TableLayoutPanel
        {
            Dock = DockStyle.Top, AutoSize = true, ColumnCount = 3,
            Padding = new Padding(12)
        };
        details.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 165));
        details.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        details.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        void AddRow(string caption, Control control, string description)
        {
            var row = details.RowCount++;
            details.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            details.Controls.Add(new Label
            {
                Text = caption, AutoSize = true, Anchor = AnchorStyles.Left,
                Margin = new Padding(3, 9, 3, 9)
            }, 0, row);
            control.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            control.Margin = new Padding(3, 5, 3, 5);
            details.Controls.Add(control, 1, row);
            details.Controls.Add(_help.Marker(caption, control, description), 2, row);
        }

        AddRow("Display name", _desktopName, EditorSettingsHelp.DisplayName);
        AddRow("Dashboard", _desktopDashboard, EditorSettingsHelp.Dashboard);
        AddRow("Display", _boardEnabled, EditorSettingsHelp.BoardEnabled);
        AddRow("Diagnostics", _providerEnabled, EditorSettingsHelp.OutputProvider);
        AddRow("Mouse input", _desktopLock, EditorSettingsHelp.Lock);
        AddRow("Refresh interval (ms)", _desktopInterval, EditorSettingsHelp.DesktopRefresh);
        AddRow("Position X", _desktopX, EditorSettingsHelp.PositionX);
        AddRow("Position Y", _desktopY, EditorSettingsHelp.PositionY);
        var orderHint = new Label
        {
            Text = "Drag the Desktop entries on the left to change their stacking order.",
            AutoSize = true, MaximumSize = new Size(470, 0), Margin = new Padding(4, 12, 4, 4)
        };
        details.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        details.Controls.Add(orderHint, 0, details.RowCount);
        details.SetColumnSpan(orderHint, 3);
        _help.Bind(orderHint, EditorSettingsHelp.Stacking);
        details.RowCount++;

        _desktopDashboard.Items.AddRange(_dashboardChoices.Cast<object>().ToArray());
        _desktopDetails.Controls.Add(details);
        columns.Controls.Add(_desktopDetails, 1, 0);
        page.Controls.Add(columns);

        _desktopList.SelectedIndexChanged += (_, _) =>
        {
            if (!_bindingDesktop)
                BindSelectedDesktop();
        };
        _desktopList.DrawItem += (_, e) =>
        {
            if (e.Index < 0)
                return;
            e.DrawBackground();
            TextRenderer.DrawText(e.Graphics, _desktopList.Items[e.Index]?.ToString() ?? string.Empty,
                e.Font ?? _desktopList.Font, Rectangle.Inflate(e.Bounds, -4, 0), e.ForeColor,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter |
                TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            e.DrawFocusRectangle();
            if (_desktopInsertionSlot == e.Index ||
                (_desktopInsertionSlot == _desktopList.Items.Count &&
                 e.Index == _desktopList.Items.Count - 1))
            {
                var y = _desktopInsertionSlot == e.Index ? e.Bounds.Top + 1 : e.Bounds.Bottom - 2;
                using var marker = new Pen(SystemColors.Highlight, 2);
                e.Graphics.DrawLine(marker, e.Bounds.Left + 3, y, e.Bounds.Right - 4, y);
            }
        };

        void ShowInsertionSlot(int slot)
        {
            if (_desktopInsertionSlot == slot)
                return;
            _desktopInsertionSlot = slot;
            _desktopList.Invalidate();
        }

        int InsertionSlotAt(Point point)
        {
            var itemIndex = _desktopList.IndexFromPoint(point);
            if (itemIndex < 0)
                return point.Y < 0 ? _desktopList.TopIndex : _desktopList.Items.Count;
            var bounds = _desktopList.GetItemRectangle(itemIndex);
            return DesktopInsertionPolicy.SlotForItem(itemIndex, point.Y >= bounds.Top + bounds.Height / 2);
        }

        _desktopList.MouseDown += (_, e) =>
        {
            _desktopDragIndex = e.Button == MouseButtons.Left
                ? _desktopList.IndexFromPoint(e.Location) : -1;
            if (_desktopDragIndex >= 0)
                _desktopDragStart = e.Location;
        };
        _desktopList.MouseMove += (_, e) =>
        {
            if (e.Button == MouseButtons.Left && _desktopDragIndex >= 0 &&
                DesktopDragStartPolicy.ShouldStart(_desktopDragStart, e.Location,
                    SystemInformation.DragSize))
            {
                var source = _desktopDragIndex;
                _desktopDragIndex = -1;
                try { _desktopList.DoDragDrop(source, DragDropEffects.Move); }
                finally { ShowInsertionSlot(-1); }
            }
        };
        _desktopList.MouseUp += (_, _) => _desktopDragIndex = -1;
        _desktopList.DragEnter += (_, e) =>
            e.Effect = e.Data?.GetDataPresent(typeof(int)) == true
                ? DragDropEffects.Move : DragDropEffects.None;
        _desktopList.DragOver += (_, e) =>
        {
            if (e.Data?.GetData(typeof(int)) is not int source ||
                source < 0 || source >= _desktops.Desktops.Count)
            {
                e.Effect = DragDropEffects.None;
                ShowInsertionSlot(-1);
                return;
            }
            e.Effect = DragDropEffects.Move;
            ShowInsertionSlot(InsertionSlotAt(_desktopList.PointToClient(new Point(e.X, e.Y))));
        };
        _desktopList.DragLeave += (_, _) => ShowInsertionSlot(-1);
        _desktopList.DragDrop += (_, e) =>
        {
            ShowInsertionSlot(-1);
            if (e.Data?.GetData(typeof(int)) is not int source)
                return;
            var slot = InsertionSlotAt(_desktopList.PointToClient(new Point(e.X, e.Y)));
            var dest = DesktopInsertionPolicy.DestinationIndex(_desktops.Desktops.Count, source, slot);
            if (dest < 0 || source == dest)
                return;
            var selection = _desktops.Desktops[source];
            _desktops.Move(source, dest);
            ReloadDesktopList(selection);
        };

        _createDesktop.Click += (_, _) =>
        {
            var dashboard = _selectedDesktop?.Dashboard ?? _dashboardChoices.FirstOrDefault();
            if (string.IsNullOrWhiteSpace(dashboard))
            {
                MessageBox.Show("Create a dashboard first.", "Desktop Output",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            var created = _desktops.Create(dashboard);
            ReloadDesktopList(created);
            _removeDesktop.Enabled = _selectedDesktop is not null && CanRemoveDesktop(_selectedDesktop);
        };
        _removeDesktop.Click += (_, _) =>
        {
            var selected = _selectedDesktop;
            if (selected is null || !CanRemoveDesktop(selected))
                return;
            var decision = MessageBox.Show(
                $"Remove Desktop Output '{selected.Name}'? Its dashboard files and other Outputs will remain.",
                "Remove Desktop Output", MessageBoxButtons.YesNo, MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2);
            if (decision != DialogResult.Yes)
                return;
            _desktops.Remove(selected);
            ReloadDesktopList(_desktops.Desktops.FirstOrDefault());
            _removeDesktop.Enabled = _selectedDesktop is not null && CanRemoveDesktop(_selectedDesktop);
        };

        _desktopName.TextChanged += (_, _) => ChangeSelectedDesktop(t => {
            t.Name = _desktopName.Text;
            RefreshDesktopListLabel();
        });
        _desktopDashboard.SelectedIndexChanged += (_, _) =>
            ChangeSelectedDesktop(t => t.Dashboard = _desktopDashboard.SelectedItem as string);
        _boardEnabled.CheckedChanged += (_, _) =>
            ChangeSelectedDesktop(t => t.Desktop!.BoardEnabled = _boardEnabled.Checked);
        _providerEnabled.CheckedChanged += (_, _) =>
            ChangeSelectedDesktop(t => t.ProviderEnabled = _providerEnabled.Checked);
        _desktopLock.CheckedChanged += (_, _) =>
            ChangeSelectedDesktop(t => t.Desktop!.Lock = _desktopLock.Checked);
        _desktopInterval.ValueChanged += (_, _) =>
            ChangeSelectedDesktop(t => t.RefreshIntervalMs = Decimal.ToInt32(_desktopInterval.Value));
        _desktopX.ValueChanged += (_, _) =>
            ChangeSelectedDesktop(t => t.Desktop!.X = Decimal.ToInt32(_desktopX.Value));
        _desktopY.ValueChanged += (_, _) =>
            ChangeSelectedDesktop(t => t.Desktop!.Y = Decimal.ToInt32(_desktopY.Value));

        ReloadDesktopList(_desktops.Desktops.FirstOrDefault());
        return page;
    }

    private void ChangeSelectedDesktop(Action<OutputTargetConfig> change)
    {
        if (_bindingDesktop || _selectedDesktop is null)
            return;
        change(_selectedDesktop);
        _desktops.MarkChanged();
    }

    private void RefreshDesktopListLabel()
    {
        var index = _desktopList.SelectedIndex;
        if (index < 0 || _selectedDesktop is null)
            return;
        _bindingDesktop = true;
        try
        {
            _desktopList.Items[index] = new DesktopListItem(_selectedDesktop);
            _desktopList.SelectedIndex = index;
        }
        finally { _bindingDesktop = false; }
    }

    private void ReloadDesktopList(OutputTargetConfig? select)
    {
        _bindingDesktop = true;
        try
        {
            _desktopList.Items.Clear();
            foreach (var desktop in _desktops.Desktops)
                _desktopList.Items.Add(new DesktopListItem(desktop));
            _desktopList.SelectedIndex = select is null ? -1 :
                _desktops.Desktops.ToList().IndexOf(select);
        }
        finally { _bindingDesktop = false; }
        BindSelectedDesktop();
    }

    private void BindSelectedDesktop()
    {
        _bindingDesktop = true;
        try
        {
            _selectedDesktop = (_desktopList.SelectedItem as DesktopListItem)?.Target;
            var target = _selectedDesktop;
            _desktopDetails.Enabled = target is not null;
            _removeDesktop.Enabled = target is not null && CanRemoveDesktop(target);
            _desktopName.Text = target?.Name ?? string.Empty;
            var dashboard = target?.Dashboard;
            if (!string.IsNullOrWhiteSpace(dashboard) &&
                !_desktopDashboard.Items.Cast<string>().Contains(dashboard, StringComparer.OrdinalIgnoreCase))
                _desktopDashboard.Items.Add(dashboard);
            _desktopDashboard.SelectedItem = dashboard;
            _boardEnabled.Checked = target is not null && OutputTargetContract.IsEnabled(target);
            _providerEnabled.Checked = target?.ProviderEnabled ?? false;
            _desktopLock.Checked = target?.Desktop?.Lock ?? false;
            _desktopInterval.Value = target?.RefreshIntervalMs ?? 1000;
            _desktopX.Value = target?.Desktop?.X ?? 0;
            _desktopY.Value = target?.Desktop?.Y ?? 0;
        }
        finally { _bindingDesktop = false; }
    }

    private sealed record DesktopListItem(OutputTargetConfig Target)
    {
        public override string ToString() => Target.Name;
    }
}
