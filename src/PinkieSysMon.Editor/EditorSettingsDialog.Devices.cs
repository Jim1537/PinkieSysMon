namespace PinkieSysMon.Editor;

internal sealed partial class EditorSettingsDialog
{
    private readonly DeviceSettingsModel _devices;
    private readonly Func<IReadOnlyList<OutputDeviceDescriptor>> _discoverDevices;
    private readonly TabControl _settingsTabs = new() { Dock = DockStyle.Fill };
    private readonly Label _discoveryNotice = new()
    {
        Dock = DockStyle.Bottom, AutoSize = true, Padding = new Padding(12, 6, 12, 6), Visible = false,
        Text = "Physical device discovery is unavailable. Reopen Settings or reconnect the device to retry."
    };
    private readonly Dictionary<string, DeviceSettingsPage> _devicePages = new(StringComparer.OrdinalIgnoreCase);
    private bool _refreshingDevices;

    public bool DevicesChanged => _devices.IsDirty;

    public void ApplyOutputChanges(AppConfig config)
    {
        if (_desktops.Desktops.Count + _devices.ConfiguredCount == 0)
            throw new InvalidOperationException("At least one Output must remain configured.");
        // Both detached models form one transaction; an intermediate draft can be empty.
        if (DevicesChanged)
            _devices.ValidateForApply(_discoverDevices());
        _desktops.ApplyTo(config, allowEmptyDraft: true);
        _devices.ApplyTo(config, allowEmptyDraft: true);
    }

    private bool CanRemoveDesktop(OutputTargetConfig target) =>
        _desktops.CanRemove(target) && _desktops.Desktops.Count + _devices.ConfiguredCount > 1;

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        RefreshDeviceDiscovery();
    }

    protected override void WndProc(ref Message m)
    {
        var devicesChanged = m.Msg == 0x0219 && m.WParam.ToInt64() == 0x0007;
        base.WndProc(ref m);
        // WM_DEVICECHANGE / DBT_DEVNODES_CHANGED is broadcast by Windows.
        // No timer, transport open, or global event subscription is needed.
        if (devicesChanged && _devices is not null && !IsDisposed && !Disposing)
            RefreshDeviceDiscovery();
    }

    internal void RefreshDeviceDiscovery()
    {
        if (_refreshingDevices)
            return;
        _refreshingDevices = true;
        try
        {
            try
            {
                _devices.Refresh(_discoverDevices());
                _discoveryNotice.Visible = false;
            }
            catch (Exception ex) when (ex is InvalidOperationException or DllNotFoundException or EntryPointNotFoundException)
            {
                _devices.DiscoveryUnavailable();
                _discoveryNotice.Visible = true;
            }
            UpdateDevicePages();
        }
        finally { _refreshingDevices = false; }
    }

    private void UpdateDevicePages()
    {
        var entries = _devices.Entries;
        var ids = entries.Select(e => e.Target.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var id in _devicePages.Keys.Where(id => !ids.Contains(id)).ToArray())
        {
            var page = _devicePages[id];
            _settingsTabs.TabPages.Remove(page);
            page.Dispose();
            _devicePages.Remove(id);
        }
        for (var index = 0; index < entries.Count; index++)
        {
            var entry = entries[index];
            if (!_devicePages.TryGetValue(entry.Target.Id, out var page))
            {
                page = new DeviceSettingsPage(entry.Target, _dashboardChoices,
                    () => _devices.MarkChanged(entry.Target),
                    enabled => ChangeDeviceEnabled(entry.Target, enabled),
                    () => AddDevice(entry.Target), () => RemoveDevice(entry.Target), _help.ApplicationRoot);
                _devicePages.Add(entry.Target.Id, page);
                _settingsTabs.TabPages.Add(page);
            }
            page.Text = $"Device #{index + 1}";
            page.UpdateState(entry, _devices.CanRemove(entry.Target, _desktops.Desktops.Count));
        }
        _removeDesktop.Enabled = _selectedDesktop is not null && CanRemoveDesktop(_selectedDesktop);
    }

    private void AddDevice(OutputTargetConfig target)
    {
        try
        {
            RefreshDeviceDiscovery();
            _devices.Add(target);
            UpdateDevicePages();
        }
        catch (Exception ex) { ShowDeviceError(ex); }
    }

    private bool ChangeDeviceEnabled(OutputTargetConfig target, bool enabled)
    {
        try
        {
            RefreshDeviceDiscovery();
            var confirmed = false;
            if (enabled)
            {
                if (!_devices.CanEnable(target))
                    throw new InvalidOperationException("This device needs an explicit, unambiguous connected identity before it can be enabled.");
                confirmed = MessageBox.Show(this,
                    $"Enable PinkieSysMon control of '{target.Name}'?\n\n" +
                    "Vendor software or another application controlling the same USB display may conflict. " +
                    "Close other display-control software first. The device will be saved and may start after OK.",
                    "Enable Device", MessageBoxButtons.YesNo, MessageBoxIcon.Warning,
                    MessageBoxDefaultButton.Button2) == DialogResult.Yes;
                if (!confirmed)
                    return target.DeviceEnabled;
                RefreshDeviceDiscovery();
            }
            if (!_devices.SetEnabled(target, enabled, confirmed))
                throw new InvalidOperationException("The device is no longer available or its identity is ambiguous.");
            UpdateDevicePages();
        }
        catch (Exception ex) { ShowDeviceError(ex); }
        return target.DeviceEnabled;
    }

    private void RemoveDevice(OutputTargetConfig target)
    {
        if (!_devices.CanRemove(target, _desktops.Desktops.Count))
            return;
        if (MessageBox.Show(this,
                $"Remove Device Output '{target.Name}'?\n\n" +
                "Its running session will stop after OK. Dashboard files and other Outputs will remain.",
                "Remove Device", MessageBoxButtons.YesNo, MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2) != DialogResult.Yes)
            return;
        _devices.Remove(target, _desktops.Desktops.Count);
        UpdateDevicePages();
    }

    private void ShowDeviceError(Exception ex) => MessageBox.Show(this, ex.Message,
        "Device Settings", MessageBoxButtons.OK, MessageBoxIcon.Error);
}

internal sealed class DeviceSettingsPage : TabPage
{
    private readonly EditorSettingsHelp _help;
    private readonly Label _status = new() { Dock = DockStyle.Top, Height = 72, Padding = new Padding(12) };
    private readonly TableLayoutPanel _fields = new()
    {
        Name = "DeviceFields", Dock = DockStyle.Top, AutoSize = true,
        ColumnCount = 3, Padding = new Padding(12)
    };
    private readonly Button _add = new() { Text = "Add Device", Name = "AddDevice", AutoSize = true };
    private readonly Button _remove = new() { Text = "Remove", Name = "RemoveDevice", AutoSize = true };
    private readonly CheckBox _enabled = new() { Text = "Device Enabled", Name = "DeviceEnabled", AutoSize = true };
    private bool _binding;

    public DeviceSettingsPage(OutputTargetConfig target, IReadOnlyList<string> dashboards,
        Action changed, Func<bool, bool> enable, Action add, Action remove,
        string? applicationRoot = null)
    {
        _help = new EditorSettingsHelp(applicationRoot);
        AutoScroll = true;
        _fields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 180));
        _fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        _fields.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        void Row(string caption, Control control, string description)
        {
            var row = _fields.RowCount++;
            _fields.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            _fields.Controls.Add(new Label { Text = caption, AutoSize = true,
                Anchor = AnchorStyles.Left, Margin = new Padding(3, 8, 3, 8) }, 0, row);
            control.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            control.Margin = new Padding(3, 4, 3, 4);
            _fields.Controls.Add(control, 1, row);
            _fields.Controls.Add(_help.Marker(caption, control, description), 2, row);
        }
        void Change(Action action)
        {
            if (_binding || !_fields.Enabled)
                return;
            action();
            changed();
        }
        NumericUpDown Number(string name, int value, int minimum, int maximum, Action<int> set)
        {
            var control = new NumericUpDown { Name = name, Minimum = minimum,
                Maximum = maximum, Value = value, Width = 160, ThousandsSeparator = true };
            control.ValueChanged += (_, _) => Change(() => set(Decimal.ToInt32(control.Value)));
            return control;
        }

        var name = new TextBox { Text = target.Name, Name = "DeviceName", Dock = DockStyle.Fill };
        name.TextChanged += (_, _) => Change(() => target.Name = name.Text);
        Row("Display name", name, EditorSettingsHelp.DisplayName);
        Row("Type", new Label { Text = "Thermalright Trofeo Vision 9.16", AutoSize = true }, EditorSettingsHelp.DeviceType);
        var dashboard = new ComboBox { Name = "DeviceDashboard", Dock = DockStyle.Fill,
            DropDownStyle = ComboBoxStyle.DropDownList };
        dashboard.Items.AddRange(dashboards.Cast<object>().ToArray());
        if (target.Dashboard is { } assigned && !dashboard.Items.Contains(assigned))
            dashboard.Items.Add(assigned);
        dashboard.SelectedItem = target.Dashboard;
        dashboard.SelectedIndexChanged += (_, _) => Change(() => target.Dashboard = dashboard.SelectedItem as string);
        Row("Dashboard", dashboard, EditorSettingsHelp.Dashboard);
        _enabled.Checked = target.DeviceEnabled;
        _enabled.CheckedChanged += (_, _) =>
        {
            if (_binding || !_fields.Enabled)
                return;
            var requested = _enabled.Checked;
            _binding = true;
            try { _enabled.Checked = enable(requested); }
            finally { _binding = false; }
        };
        Row("Display", _enabled, EditorSettingsHelp.DeviceEnabled);
        var provider = new CheckBox { Text = "Provider Enabled", Name = "DeviceProvider",
            Checked = target.ProviderEnabled, AutoSize = true };
        provider.CheckedChanged += (_, _) => Change(() => target.ProviderEnabled = provider.Checked);
        Row("Diagnostics provider", provider, EditorSettingsHelp.OutputProvider);
        Row("Refresh interval (ms)", Number("DeviceRefresh", target.RefreshIntervalMs!.Value, 10, int.MaxValue,
            value => target.RefreshIntervalMs = value), EditorSettingsHelp.DeviceRefresh);
        Row("JPEG quality", Number("DeviceJpeg", target.JpegQuality!.Value, 10, 100,
            value => target.JpegQuality = value), EditorSettingsHelp.JpegQuality);
        var usb = target.Usb ?? throw new InvalidOperationException("Physical Output USB settings are missing.");
        Row("USB retry (ms)", Number("DeviceRetry", usb.RetryIntervalMs, 500, int.MaxValue,
            value => usb.RetryIntervalMs = value), EditorSettingsHelp.UsbRetry);
        Row("USB timeout (ms)", Number("DeviceTimeout", usb.TransferTimeoutMs, 500, int.MaxValue,
            value => usb.TransferTimeoutMs = value), EditorSettingsHelp.UsbTimeout);
        var journal = new CheckBox { Text = "USB diagnostic journal", Name = "DeviceJournal",
            Checked = usb.DiagnosticJournalEnabled, AutoSize = true };
        journal.CheckedChanged += (_, _) => Change(() => usb.DiagnosticJournalEnabled = journal.Checked);
        Row("USB diagnostics", journal, EditorSettingsHelp.UsbJournal);

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 48, Padding = new Padding(12, 4, 12, 4) };
        buttons.Controls.Add(_add);
        buttons.Controls.Add(_remove);
        _help.Bind(_add, EditorSettingsHelp.AddDevice);
        _help.Bind(_remove, EditorSettingsHelp.RemoveDevice);
        _add.Click += (_, _) => add();
        _remove.Click += (_, _) => remove();
        Controls.Add(buttons);
        Controls.Add(_fields);
        Controls.Add(_status);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _help.Dispose();
        base.Dispose(disposing);
    }

    public void UpdateState(DeviceSettingsEntry entry, bool canRemove)
    {
        var status = entry.Status switch
        {
            PhysicalDeviceInventoryStatus.Connected => "Connected",
            PhysicalDeviceInventoryStatus.Disconnected => "Disconnected",
            PhysicalDeviceInventoryStatus.Ambiguous => "Ambiguous identity — controls unavailable",
            _ => "Discovery unavailable — controls unavailable"
        };
        _status.Text = $"{entry.Target.Name} — {status}\n" +
            (entry.IsConfigured ? "Changes are saved and applied after OK." :
                "Not configured. Use Add Device to save this device's settings after OK.");
        _fields.Enabled = entry.CanEdit;
        _add.Visible = !entry.IsConfigured;
        _add.Enabled = entry.CanEdit;
        _remove.Enabled = canRemove;
        var wasBinding = _binding;
        _binding = true;
        try { _enabled.Checked = entry.Target.DeviceEnabled; }
        finally { _binding = wasBinding; }
    }
}
