namespace PinkieSysMon.Editor;

internal sealed class PhysicalDeviceSettingsDialog : PinkieEditorForm
{
    private readonly EditorSettingsHelp _help;
    private readonly IReadOnlyList<string> _dashboardChoices;
    private readonly int _desktopCount;
    private readonly EditorUsbTestClient _testClient;
    private CancellationTokenSource? _testCancellation;
    private bool _testUiBusy;
    private bool _saveUiBusy;
    private bool _closeAfterTest;
    private readonly Func<AppConfig, byte[]?, Task<bool>>? _persistAndApply;
    private readonly Action<AppConfig, bool>? _onCommitted;
    private readonly FileLogger? _log;
    private readonly Button _ok = new() { Text = "Close", DialogResult = DialogResult.Cancel, AutoSize = true };

    public PhysicalDeviceSettingsDialog(AppConfig config, IReadOnlyList<string> dashboards,
        Func<IReadOnlyList<OutputDeviceDescriptor>>? discoverDevices = null,
        string? applicationRoot = null,
        Func<IReadOnlyList<UsbDisplayObservation>>? discoverRecognizedDevices = null,
        FileLogger? log = null,
        Func<AppConfig, byte[]?, Task<bool>>? persistAndApply = null,
        Action<AppConfig, bool>? onCommitted = null)
    {
        _log = log;
        _persistAndApply = persistAndApply;
        _onCommitted = onCommitted;
        _help = new EditorSettingsHelp(applicationRoot);
        _testClient = new EditorUsbTestClient(_help.ApplicationRoot);
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
        buttons.Controls.Add(_ok);
        AcceptButton = _ok;
        CancelButton = _ok;
        var notice = new Label
        {
            Dock = DockStyle.Bottom, Height = 54,
            Padding = new Padding(12, 4, 12, 4),
            TextAlign = ContentAlignment.MiddleLeft,
            Text = "Save Configuration and Forget commit only this card immediately; Close cannot undo them. " +
                   "Remove commits its selected card after confirmation. Save each edited card separately. A responsive Runtime reloads saved settings."
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
    public bool HasPendingRemove => _devices.HasPendingRemoval;

    public void ApplyOutputChanges(AppConfig config)
    {
        if (_devices.Entries.Any(e => e.IsConfigured && _devices.HasPendingConfiguredEdit(e.Target)))
            throw new InvalidOperationException("Save each edited device card before applying a pending Remove.");
        if (!HasPendingRemove) return;
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
            try { _recognizedDevices.Refresh(_discoverRecognizedDevices().Where(o =>
                !o.NodeKeys.Any(_devices.IgnoredNodes.Contains)).ToArray(), supported); }
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
            var wasSelected = false;
            if (_devicePages.TryGetValue(entry.Target.Id, out var oldPage) &&
                oldPage.IsCandidate && entry.IsConfigured)
            {
                wasSelected = _settingsTabs.SelectedTab == oldPage;
                _settingsTabs.TabPages.Remove(oldPage);
                oldPage.Dispose();
                _devicePages.Remove(entry.Target.Id);
            }
            if (!_devicePages.TryGetValue(entry.Target.Id, out var page))
            {
                page = new DeviceSettingsPage(entry.Target, _dashboardChoices,
                    () =>
                    {
                        _devices.MarkChanged(entry.Target);
                        if (_devices.CandidateDraft(entry.Target)?.CanSaveConfiguration != true &&
                            _devicePages.TryGetValue(entry.Target.Id, out var edited)) edited.SetTestMessage(null);
                        UpdateDevicePages();
                    },
                    enabled => ChangeDeviceEnabled(entry.Target, enabled),
                    () => entry.IsConfigured ? SaveConfiguredAsync(entry.Target) : SaveCandidateAsync(entry.Target),
                    () => { _ = RemoveDeviceAsync(entry.Target); }, _help.ApplicationRoot, _devices.CandidateDraft(entry.Target),
                    () => TestDeviceAsync(entry.Target), CancelCandidate,
                    () => ForgetCandidateAsync(entry.Target));
                _devicePages.Add(entry.Target.Id, page);
                _settingsTabs.TabPages.Add(page);
                if (wasSelected) _settingsTabs.SelectedTab = page;
            }
            page.UpdateState(entry, _devices.CanRemove(entry.Target, _desktopCount), _testUiBusy || _saveUiBusy,
                _persistAndApply is not null, _devices.HasPendingConfiguredEdit(entry.Target));
        }
        var recognized = _recognizedDevices.Entries.Where(e =>
            !e.Observation.NodeKeys.Any(_devices.IgnoredNodes.Contains)).ToArray();
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
        {
            var tab = _settingsTabs.TabPages[index];
            var configured = entries.FirstOrDefault(entry => entry.IsConfigured &&
                _devicePages.GetValueOrDefault(entry.Target.Id) == tab);
            tab.Text = configured?.Target.Name ?? $"Device #{index + 1}";
        }
        _ok.Enabled = !_saveUiBusy;
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (_saveUiBusy) e.Cancel = true;
        if (_testUiBusy)
        {
            e.Cancel = true;
            _closeAfterTest = true;
            _testCancellation?.Cancel();
        }
        base.OnFormClosing(e);
    }

    private void CancelCandidate()
    {
        _testCancellation?.Cancel();
    }

    private async Task TestDeviceAsync(OutputTargetConfig target)
    {
        if (_testUiBusy || _devices.CandidateDraft(target) is not { } draft || !draft.CanTest) return;
        if (MessageBox.Show(this,
            "Test will send display frames using the selected protocol. Close vendor display-control software first. " +
            "Watch the current test image and check that seconds change. Configuration will not be saved.",
            "USB Test", MessageBoxButtons.OKCancel, MessageBoxIcon.Information) != DialogResult.OK) return;
        _testUiBusy = true;
        _testCancellation = new CancellationTokenSource();
        try
        {
            var snapshot = CatalogUsbInterfaceDiscovery.CaptureSnapshot();
            var intent = await _testClient.TestAsync(draft, snapshot, message =>
            {
                if (!IsDisposed && _devicePages.TryGetValue(target.Id, out var page))
                { page.SetTestMessage(message); UpdateDevicePages(); }
            }, _testCancellation.Token);
            if (intent is not null && !_closeAfterTest && !_testCancellation.IsCancellationRequested)
            {
                var yes = MessageBox.Show(this,
                    "During this Test, did you see the current test dashboard with correctly readable geometry and seconds changing? " +
                    "The transmission has now stopped.\n\nYes confirms this attempt only; it does not save configuration or enable an Output.",
                    "Confirm USB Test", MessageBoxButtons.YesNo, MessageBoxIcon.Question,
                    MessageBoxDefaultButton.Button2) == DialogResult.Yes;
                draft.ConfirmVisibleChangingSeconds(intent, yes);
                _devicePages[target.Id].SetTestMessage(yes
                    ? "Test confirmed. Save Configuration is available; the Output will remain disabled."
                    : "Test not passed. Adjust the fields and retry; configuration was not saved.");
            }
            else if (!_closeAfterTest && !_testCancellation.IsCancellationRequested)
                MessageBox.Show(this, "Test not passed. Configuration was not saved. See the card status and Runtime log for details.",
                    "USB Test", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        catch (Exception error) { ShowDeviceError(error); }
        finally
        {
            _testCancellation.Dispose(); _testCancellation = null; _testUiBusy = false;
            if (!IsDisposed) UpdateDevicePages();
            if (_closeAfterTest) { DialogResult = DialogResult.Cancel; Close(); }
        }
    }

    private async Task SaveCandidateAsync(OutputTargetConfig target)
    {
        if (_saveUiBusy || _testUiBusy || _persistAndApply is null || _log is null) return;
        _saveUiBusy = true;
        try
        {
            RefreshDeviceDiscovery();
            var confirmed = _devices.PrepareCandidate(target);
            var node = _devices.CandidateDraft(target)!.PnpNodeKey;
            var (saved, reloaded) = await PhysicalDeviceSettingsTransaction.SaveCandidateAsync(
                _help.ApplicationRoot!, Path.Combine(_help.ApplicationRoot!, "config", "app.json"),
                _log, confirmed, node, _discoverDevices, _persistAndApply);
            _devices.AcceptSavedCandidate(target, confirmed);
            ReportCommitted(saved, reloaded);
            _devicePages[target.Id].SetTestMessage(null);
            UpdateDevicePages();
        }
        catch (Exception ex) { ShowDeviceError(ex); }
        finally { _saveUiBusy = false; if (!IsDisposed) UpdateDevicePages(); }
    }

    private async Task SaveConfiguredAsync(OutputTargetConfig target)
    {
        if (_saveUiBusy || _testUiBusy || _persistAndApply is null || _log is null) return;
        _saveUiBusy = true;
        try
        {
            RefreshDeviceDiscovery();
            var (original, updated) = _devices.PrepareConfigured(target);
            var (saved, reloaded) = await PhysicalDeviceSettingsTransaction.SaveConfiguredAsync(
                _help.ApplicationRoot!, Path.Combine(_help.ApplicationRoot!, "config", "app.json"),
                _log, original, updated, _discoverDevices, _persistAndApply);
            _devices.AcceptSavedConfigured(target, updated);
            ReportCommitted(saved, reloaded);
        }
        catch (Exception ex) { ShowDeviceError(ex); }
        finally { _saveUiBusy = false; if (!IsDisposed) UpdateDevicePages(); }
    }

    private async Task ForgetCandidateAsync(OutputTargetConfig target)
    {
        if (_saveUiBusy || _testUiBusy || _persistAndApply is null || _log is null) return;
        var draft = _devices.CandidateDraft(target);
        if (draft is null || draft.IsTesting) return;
        if (MessageBox.Show(this,
            $"Permanently forget '{target.Name}' on this installation?\n\n" +
            "This specific device will not be offered or tested again. To allow it later, manually remove its hashed node key from config/app.json. No dashboard file is deleted.",
            "Forget Device", MessageBoxButtons.YesNo, MessageBoxIcon.Warning,
            MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
        _saveUiBusy = true;
        try
        {
            RefreshDeviceDiscovery();
            var node = _devices.PrepareForget(target);
            var (saved, reloaded) = await PhysicalDeviceSettingsTransaction.ForgetCandidateAsync(
                Path.Combine(_help.ApplicationRoot!, "config", "app.json"), _log,
                draft.InterfaceId, node, _discoverDevices, _persistAndApply);
            _devices.AcceptForgot(target, node);
            ReportCommitted(saved, reloaded);
            RefreshDeviceDiscovery();
        }
        catch (Exception ex) { ShowDeviceError(ex); }
        finally { _saveUiBusy = false; if (!IsDisposed) UpdateDevicePages(); }
    }

    private void ReportCommitted(AppConfig saved, bool reloaded)
    {
        try { _onCommitted?.Invoke(saved, reloaded); }
        catch (Exception refreshError)
        {
            MessageBox.Show(this,
                $"Configuration was saved, but the Editor view could not refresh: {refreshError.Message}\nReopen Settings before further edits.",
                "Device Settings", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
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
                    "Close other display-control software first. The device may start after Save Configuration.",
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

    private async Task RemoveDeviceAsync(OutputTargetConfig target)
    {
        if (_saveUiBusy || _testUiBusy || !_devices.CanRemove(target, _desktopCount) ||
            _persistAndApply is null || _log is null)
            return;
        if (MessageBox.Show(this,
                $"Remove Device Output '{target.Name}'?\n\n" +
                "Runtime will stop and release only this Output before its saved record is removed. Dashboard files and other Outputs will remain.",
                "Remove Device", MessageBoxButtons.YesNo, MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2) != DialogResult.Yes)
            return;
        _saveUiBusy = true;
        try
        {
            var original = _devices.PrepareRemove(target, _desktopCount);
            var (saved, reloaded, warning) = await PhysicalDeviceSettingsTransaction.RemoveConfiguredAsync(
                Path.Combine(_help.ApplicationRoot!, "config", "app.json"), _log, original,
                () => RuntimeIpc.GetStatusAsync(), RuntimeProcessLease.ExistsForCurrentUser,
                () => RuntimeProcessLease.TryAcquireForCurrentUser(),
                (id, hash) => RuntimeIpc.PreparePhysicalRemovalAsync(id, hash),
                () => RuntimeIpc.ReloadAsync());
            _devices.AcceptRemovedConfigured(target);
            ReportCommitted(saved, reloaded);
            RefreshDeviceDiscovery();
            if (warning is not null)
                MessageBox.Show(this, warning, "Remove partially applied", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        catch (Exception ex) { ShowDeviceError(ex); }
        finally { _saveUiBusy = false; if (!IsDisposed) UpdateDevicePages(); }
    }

    private void ShowDeviceError(Exception ex) => MessageBox.Show(this, ex.Message,
        "Device Settings", MessageBoxButtons.OK, MessageBoxIcon.Error);
}

internal sealed class DeviceSettingsPage : TabPage
{
    private readonly EditorSettingsHelp _help;
    private readonly Panel _scrollableFields = new() { Dock = DockStyle.Fill, AutoScroll = true };
    private readonly Label _status = new() { Dock = DockStyle.Top, Height = 110, Padding = new Padding(12) };
    private readonly TableLayoutPanel _fields = new()
    {
        Name = "DeviceFields", Dock = DockStyle.Top, AutoSize = true,
        ColumnCount = 3, Padding = new Padding(12)
    };
    private readonly Button _add = new() { Text = "Save Configuration", Name = "AddDevice", AutoSize = true };
    private readonly Button _remove = new() { Text = "Remove", Name = "RemoveDevice", AutoSize = true };
    private readonly CheckBox _enabled = new() { Text = EditorSettingsHelp.OutputEnabledCaption, Name = "DeviceEnabled", AutoSize = true };
    private readonly Button _test = new() { Text = "Test", Name = "DeviceTest", AutoSize = true };
    private readonly Button _forget = new() { Text = "Forget", Name = "ForgetDevice", AutoSize = true, Enabled = false };
    private readonly Button _cancel = new() { Text = "Cancel Test", Name = "CancelDevice", AutoSize = true, Visible = false };
    private readonly bool _hasTest;
    private string? _testMessage;
    internal void SetTestMessage(string? message) => _testMessage = message;
    private bool _binding;
    private readonly CatalogUsbCandidateDraft? _candidate;
    internal bool IsCandidate => _candidate is not null;

    public DeviceSettingsPage(OutputTargetConfig target, IReadOnlyList<string> dashboards,
        Action changed, Func<bool, bool> enable, Func<Task> add, Action remove,
        string? applicationRoot = null, CatalogUsbCandidateDraft? candidate = null,
        Func<Task>? test = null, Action? cancel = null, Func<Task>? forget = null)
    {
        _candidate = candidate;
        _hasTest = test is not null;
        _help = new EditorSettingsHelp(applicationRoot);
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
            if (_candidate is not null && (name == "DeviceWidth" || name == "DeviceHeight"))
                control.TextChanged += (_, _) =>
                {
                    if (_binding || !_fields.Enabled) return;
                    var parsed = decimal.TryParse(control.Text, System.Globalization.NumberStyles.Number,
                        System.Globalization.CultureInfo.CurrentCulture, out var entered) &&
                        entered == decimal.Truncate(entered) && entered >= minimum && entered <= maximum;
                    Change(() => set(parsed ? decimal.ToInt32(entered) : 0));
                };
            return control;
        }

        void AddButtons()
        {
            var buttons = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 48, Padding = new Padding(12, 4, 12, 4) };
            if (_candidate is not null)
            {
                buttons.Controls.Add(_test); buttons.Controls.Add(_add);
                buttons.Controls.Add(_forget); buttons.Controls.Add(_cancel);
                _test.Click += async (_, _) => { if (test is not null) await test(); };
                _forget.Click += async (_, _) => { if (forget is not null) await forget(); };
                _cancel.Click += (_, _) => cancel?.Invoke();
                _help.Bind(_test, "Send a temporary changing-seconds dashboard through Runtime. No configuration is saved.");
                _help.Bind(_forget, "Persist an exclusion for this exact physical node. Manually remove its hash from app.json to allow it again.");
                _help.Bind(_cancel, "Request cancellation of the current Test. Native release remains owned by Runtime.");
            }
            else { buttons.Controls.Add(_add); buttons.Controls.Add(_remove); }
            _help.Bind(_add, _candidate is null
                ? "Save only this configured Output now. Close cannot undo a completed Save."
                : EditorSettingsHelp.AddDevice);
            _help.Bind(_remove, EditorSettingsHelp.RemoveDevice);
            _add.Click += async (_, _) => await add();
            _remove.Click += (_, _) => remove();
            _scrollableFields.Controls.Add(_fields);
            Controls.Add(_scrollableFields);
            Controls.Add(buttons);
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
        Row("Dashboard", dashboard,
            "Select this Output's saved dashboard. Save Configuration applies this card; other Outputs retain their assignments.");
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
        Row("Display", _enabled,
            "Permit Runtime to open this USB display after Save Configuration. Enabling requires confirmation and a fresh exact binding; Provider Enabled is independent.");
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

    public void UpdateState(DeviceSettingsEntry entry, bool canRemove, bool operationBusy = false,
        bool canPersist = false, bool pendingConfiguredEdit = false)
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
            (entry.IsConfigured ? pendingConfiguredEdit
                ? "Unsaved changes. Use Save Configuration above to apply this Output's settings."
                : "Use Save Configuration above for edits, or Remove this Output." :
                _testMessage ?? "Not configured. Enter screen size and protocol, then Test. Save after a confirmed Test, or Forget this candidate.");
        _fields.Enabled = entry.CanEdit && _candidate?.IsTesting != true && !operationBusy;
        _test.Enabled = _hasTest && entry.CanEdit && _candidate?.CanTest == true && !operationBusy;
        _add.Visible = true;
        _add.Enabled = canPersist && entry.CanEdit && !operationBusy &&
            (entry.IsConfigured ? pendingConfiguredEdit :
                _candidate?.CanSaveConfiguration == true);
        _forget.Enabled = !entry.IsConfigured && entry.CanEdit && canPersist && !operationBusy && _candidate?.IsTesting != true;
        _cancel.Visible = _candidate?.IsTesting == true && operationBusy;
        _cancel.Enabled = _cancel.Visible;
        _enabled.Enabled = entry.IsConfigured;
        _remove.Visible = entry.IsConfigured;
        _remove.Enabled = entry.IsConfigured && canRemove && !operationBusy;
        var wasBinding = _binding;
        _binding = true;
        try { _enabled.Checked = OutputTargetContract.IsEnabled(entry.Target); }
        finally { _binding = wasBinding; }
    }
}
