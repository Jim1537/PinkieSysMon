namespace PinkieSysMon.Editor;

internal sealed class PhysicalDeviceSettingsDialog : PinkieEditorForm
{
    private readonly EditorSettingsHelp _help;
    private readonly IReadOnlyList<string> _dashboardChoices;
    private readonly int _desktopCount;

    public PhysicalDeviceSettingsDialog(AppConfig config, IReadOnlyList<string> dashboards,
        Func<IReadOnlyList<OutputDeviceDescriptor>>? discoverDevices = null,
        string? applicationRoot = null,
        Func<IReadOnlyList<UsbDisplayObservation>>? discoverRecognizedDevices = null)
    {
        _help = new EditorSettingsHelp(applicationRoot);
        _dashboardChoices = dashboards;
        _devices = new DeviceSettingsModel(config);
        _desktopCount = config.Outputs.Targets.Count(t => t.Kind == OutputTargetContract.DesktopKind);
        _discoverDevices = discoverDevices ?? PhysicalDeviceDiscovery.Capture;
        // Existing injected discovery callers stay fully native-free. Production
        // uses the independent, metadata-only USB family scan.
        _discoverRecognizedDevices = discoverRecognizedDevices ??
            (discoverDevices is null ? UsbDisplayDiscovery.Capture : () => []);
        Text = "Physical Devices Settings";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(900, 600);
        Padding = new Padding(12);

        Controls.Add(_settingsTabs);
        Controls.Add(_discoveryNotice);
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
            Text = "Changes take effect after OK (Cancel discards them). " +
                   "A responsive Runtime reloads saved device settings; otherwise they take effect on its next start."
        };
        Controls.Add(notice);
        Controls.Add(buttons);
        RefreshDeviceDiscovery();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _help.Dispose();
        base.Dispose(disposing);
    }

    private readonly DeviceSettingsModel _devices;
    private readonly Func<IReadOnlyList<OutputDeviceDescriptor>> _discoverDevices;
    private readonly Func<IReadOnlyList<UsbDisplayObservation>> _discoverRecognizedDevices;
    private readonly RecognizedDeviceSettingsModel _recognizedDevices = new();
    private readonly TabControl _settingsTabs = new() { Dock = DockStyle.Fill };
    private readonly Label _discoveryNotice = new()
    {
        Dock = DockStyle.Bottom, AutoSize = true, Padding = new Padding(12, 6, 12, 6), Visible = false,
        Text = "Physical device discovery is unavailable. Reopen Settings or reconnect the device to retry."
    };
    private readonly Dictionary<string, DeviceSettingsPage> _devicePages = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, RecognizedDeviceSettingsPage> _recognizedPages = new(StringComparer.Ordinal);
    private bool _refreshingDevices;

    public bool DevicesChanged => _devices.IsDirty;

    public void ApplyOutputChanges(AppConfig config)
    {
        if (_desktopCount + _devices.ConfiguredCount == 0)
            throw new InvalidOperationException("At least one Output must remain configured.");
        if (DevicesChanged)
        {
            try { _devices.ValidateForApply(_discoverDevices()); }
            catch (PhysicalDeviceDiscoveryException error)
            {
                _devices.ValidateForApply(error.Devices, error.UnavailableTransports);
            }
        }
        _devices.ApplyTo(config);
    }

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
            IReadOnlyList<OutputDeviceDescriptor> supported = [];
            var unavailable = false;
            try
            {
                supported = _discoverDevices();
                _devices.Refresh(supported);
            }
            catch (PhysicalDeviceDiscoveryException error)
            {
                supported = error.Devices;
                _devices.Refresh(supported, error.UnavailableTransports);
                unavailable = true;
            }
            catch (Exception ex) when (ex is InvalidOperationException or DllNotFoundException or EntryPointNotFoundException)
            {
                _devices.DiscoveryUnavailable();
                unavailable = true;
            }
            try { _recognizedDevices.Refresh(_discoverRecognizedDevices(), supported); }
            catch (Exception ex) when (ex is InvalidOperationException or DllNotFoundException or EntryPointNotFoundException)
            {
                _recognizedDevices.DiscoveryUnavailable();
                unavailable = true;
            }
            _discoveryNotice.Visible = unavailable;
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
                    () => AddDevice(entry.Target), () => RemoveDevice(entry.Target), _help.ApplicationRoot, _devices.CandidateDraft(entry.Target));
                _devicePages.Add(entry.Target.Id, page);
                _settingsTabs.TabPages.Add(page);
            }
            page.UpdateState(entry, _devices.CanRemove(entry.Target, _desktopCount));
        }
        var recognized = _recognizedDevices.Entries;
        var recognizedIds = recognized.Select(e => e.Observation.ObservationId).ToHashSet(StringComparer.Ordinal);
        foreach (var id in _recognizedPages.Keys.Where(id => !recognizedIds.Contains(id)).ToArray())
        {
            var page = _recognizedPages[id];
            _settingsTabs.TabPages.Remove(page);
            page.Dispose();
            _recognizedPages.Remove(id);
        }
        foreach (var entry in recognized)
        {
            var id = entry.Observation.ObservationId;
            if (!_recognizedPages.TryGetValue(id, out var page))
            {
                page = new RecognizedDeviceSettingsPage();
                _recognizedPages.Add(id, page);
                _settingsTabs.TabPages.Add(page);
            }
            page.UpdateState(entry);
        }
        // Retain existing pages and selection through PnP refreshes. Number the
        // actual tab order, including informational pages and later arrivals.
        for (var index = 0; index < _settingsTabs.TabPages.Count; index++)
            _settingsTabs.TabPages[index].Text = $"Device #{index + 1}";
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
                if (target.Transport == OutputTargetContract.EncryptedJpegTransport)
                    EncryptedJpegOutputConfig.ValidateFrameOptions(target.EncryptedJpeg?.BrightnessPercent, target.Usb?.DiagnosticJournalEnabled == true);
                if (!_devices.CanEnable(target))
                    throw new InvalidOperationException("This device needs an explicit, unambiguous connected identity before it can be enabled.");
                confirmed = MessageBox.Show(this,
                    $"Enable PinkieSysMon control of '{target.Name}'?\n\n" +
                    "Vendor software or another application controlling the same USB display may conflict. " +
                    "Close other display-control software first. The device will be saved and may start after OK.",
                    "Enable Device", MessageBoxButtons.YesNo, MessageBoxIcon.Warning,
                    MessageBoxDefaultButton.Button2) == DialogResult.Yes;
                if (!confirmed)
                    return OutputTargetContract.IsEnabled(target);
                RefreshDeviceDiscovery();
            }
            if (!_devices.SetEnabled(target, enabled, confirmed))
                throw new InvalidOperationException("The device is no longer available or its identity is ambiguous.");
            UpdateDevicePages();
        }
        catch (Exception ex) { ShowDeviceError(ex); }
        return OutputTargetContract.IsEnabled(target);
    }

    private void RemoveDevice(OutputTargetConfig target)
    {
        if (!_devices.CanRemove(target, _desktopCount))
            return;
        if (MessageBox.Show(this,
                $"Remove Device Output '{target.Name}'?\n\n" +
                "Its running session will stop after OK. Dashboard files and other Outputs will remain.",
                "Remove Device", MessageBoxButtons.YesNo, MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2) != DialogResult.Yes)
            return;
        _devices.Remove(target, _desktopCount);
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
    private readonly Button _add = new() { Text = "Save Configuration", Name = "AddDevice", AutoSize = true };
    private readonly Button _remove = new() { Text = "Remove", Name = "RemoveDevice", AutoSize = true };
    private readonly CheckBox _enabled = new() { Text = EditorSettingsHelp.OutputEnabledCaption, Name = "DeviceEnabled", AutoSize = true };
    private bool _binding;
    private readonly CatalogUsbCandidateDraft? _candidate;

    public DeviceSettingsPage(OutputTargetConfig target, IReadOnlyList<string> dashboards,
        Action changed, Func<bool, bool> enable, Action add, Action remove,
        string? applicationRoot = null, CatalogUsbCandidateDraft? candidate = null)
    {
        _candidate = candidate;
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

        void AddButtons()
        {
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

        var name = new TextBox { Text = target.Name, Name = "DeviceName", Dock = DockStyle.Fill };
        name.TextChanged += (_, _) => Change(() => target.Name = name.Text);
        Row("Display name", name, EditorSettingsHelp.DisplayName);
        var isTurzx = target.Transport == OutputTargetContract.EncryptedJpegTransport;
        if (candidate is not null)
        {
            Row("Width", Number("DeviceWidth", target.Width, 0, int.MaxValue, value => target.Width = value),
                "Required screen width at zero rotation. No model dimensions are inferred.");
            Row("Height", Number("DeviceHeight", target.Height, 0, int.MaxValue, value => target.Height = value),
                "Required screen height at zero rotation. Runtime Test validates this geometry.");
            var separator = new Label { Name = "DeviceProtocolSeparator", BorderStyle = BorderStyle.Fixed3D,
                Height = 2, Dock = DockStyle.Fill, Margin = new Padding(3, 8, 3, 8) };
            _fields.RowStyles.Add(new RowStyle(SizeType.Absolute, 18));
            _fields.Controls.Add(separator, 0, _fields.RowCount++);
            _fields.SetColumnSpan(separator, 3);
            var protocols = new FlowLayoutPanel
            {
                Name = "DeviceProtocol", AutoSize = true, FlowDirection = FlowDirection.TopDown,
                WrapContents = false, Dock = DockStyle.Fill
            };
            foreach (var choice in candidate.ProtocolChoices)
            {
                var option = new RadioButton
                {
                    Name = "DeviceProtocol_" + choice.Id, Text = choice.Label, AutoSize = true,
                    Tag = choice.Id, Enabled = choice.IsImplemented && candidate.ApplicableProtocols.Contains(choice.Id),
                    Checked = string.Equals(candidate.Protocol, choice.Id, StringComparison.OrdinalIgnoreCase)
                };
                option.CheckedChanged += (_, _) =>
                {
                    if (!option.Checked || !option.Enabled) return;
                    Change(() =>
                    {
                        target.Transport = choice.Id;
                        target.EncryptedJpeg = choice.Id == OutputTargetContract.EncryptedJpegTransport ? new() : null;
                        target.Usb!.DiagnosticJournalEnabled = false;
                    });
                };
                protocols.Controls.Add(option);
            }
            Row("Protocol", protocols, "Select the implemented wire protocol. A catalog hint is only a trial suggestion; compatibility requires a successful Test.");
            Row("Test", new Button { Name = "DeviceTest", Text = "Test", Enabled = false, AutoSize = true },
                "Runtime USB Test integration is pending. This candidate cannot yet be saved or enabled.");
            AddButtons();
            return;
        }
        else
        {
            Row("Type", new Label { Text = "Physical USB display", AutoSize = true }, EditorSettingsHelp.DeviceType);
            Row("Protocol", new Label { Text = target.Transport, AutoSize = true },
                "Confirmed protocol. To change protocol or geometry, Remove and repeat Test / Save Configuration.");
            Row("Screen size", new Label { Text = $"{target.Width} × {target.Height}", AutoSize = true },
                "User-confirmed dimensions used for the first working dashboard. Ordinary frames use the assigned dashboard canvas.");
        }
        var dashboard = new ComboBox { Name = "DeviceDashboard", Dock = DockStyle.Fill,
            DropDownStyle = ComboBoxStyle.DropDownList };
        dashboard.Items.AddRange(dashboards.Cast<object>().ToArray());
        if (target.Dashboard is { } assigned && !dashboard.Items.Contains(assigned))
            dashboard.Items.Add(assigned);
        dashboard.SelectedItem = target.Dashboard;
        dashboard.SelectedIndexChanged += (_, _) => Change(() => target.Dashboard = dashboard.SelectedItem as string);
        Row("Dashboard", dashboard, EditorSettingsHelp.Dashboard);
        _enabled.Checked = OutputTargetContract.IsEnabled(target);
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
            value => usb.RetryIntervalMs = value), isTurzx
                ? "Interval for checking a missing exact binding and passive recovery after a TURZX failure. Automatic reconnect requires observing that binding absent and then uniquely present. Enabled off/on allows an explicit new attempt; recovery is not guaranteed."
                : EditorSettingsHelp.UsbRetry);
        Row("USB timeout (ms)", Number("DeviceTimeout", usb.TransferTimeoutMs, 500, int.MaxValue,
            value => usb.TransferTimeoutMs = value), EditorSettingsHelp.UsbTimeout);
        if (isTurzx)
        {
            Control brightness = new Label { Name = "DeviceKeepBrightness", Text = "Device setting (unchanged)", AutoSize = true };
            if (target.EncryptedJpeg?.BrightnessPercent is not null)
            {
                var clear = new Button { Name = "DeviceKeepBrightness", Text = "Use current device setting", AutoSize = true };
                clear.Click += (_, _) => Change(() =>
                {
                    target.EncryptedJpeg ??= new(); target.EncryptedJpeg.BrightnessPercent = null; clear.Enabled = false;
                });
                brightness = clear;
            }
            Row("Brightness", brightness, "Brightness adjustment is not yet available. Current device brightness is preserved. A previously saved percentage can be cleared without sending a brightness command.");
            Control diagnostics = new Label { Name = "DeviceNoJournal", Text = "Application log", AutoSize = true };
            if (usb.DiagnosticJournalEnabled)
            {
                var clear = new Button { Name = "DeviceNoJournal", Text = "Use application log only", AutoSize = true };
                clear.Click += (_, _) => Change(() => { usb.DiagnosticJournalEnabled = false; clear.Enabled = false; });
                diagnostics = clear;
            }
            Row("USB diagnostics", diagnostics, "Detailed TURZX USB journaling is not yet available. A previously saved option can be cleared. Runtime lifecycle and errors remain in the application log.");
        }
        else
        {
            var journal = new CheckBox { Text = "USB diagnostic journal", Name = "DeviceJournal",
                Checked = usb.DiagnosticJournalEnabled, AutoSize = true };
            journal.CheckedChanged += (_, _) => Change(() => usb.DiagnosticJournalEnabled = journal.Checked);
            Row("USB diagnostics", journal, EditorSettingsHelp.UsbJournal);
        }

        AddButtons();
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
            PhysicalDeviceInventoryStatus.Connected when !OutputTargetContract.HasIntegratedTransport(entry.Target.Transport) =>
                "Unsupported / protocol not available",
            PhysicalDeviceInventoryStatus.Connected => "Connected",
            PhysicalDeviceInventoryStatus.Disconnected => "Disconnected",
            PhysicalDeviceInventoryStatus.Ambiguous => "Ambiguous identity — controls unavailable",
            _ => "Discovery unavailable — controls unavailable"
        };
        _status.Text = $"{entry.Target.Name} — {status}\n" +
            (entry.IsConfigured ? "Changes are saved and applied after OK." :
                "Not configured. Enter screen size and protocol. Runtime Test integration is pending; saving is unavailable.");
        _fields.Enabled = entry.CanEdit;
        _add.Visible = !entry.IsConfigured;
        _add.Enabled = entry.CanEdit && _candidate?.CanSaveConfiguration == true;
        _enabled.Enabled = entry.IsConfigured;
        _remove.Visible = entry.IsConfigured;
        _remove.Enabled = entry.IsConfigured && canRemove;
        var wasBinding = _binding;
        _binding = true;
        try { _enabled.Checked = OutputTargetContract.IsEnabled(entry.Target); }
        finally { _binding = wasBinding; }
    }
}
