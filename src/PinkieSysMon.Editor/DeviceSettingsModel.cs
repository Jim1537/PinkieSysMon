namespace PinkieSysMon.Editor;

internal sealed record DeviceSettingsEntry(
    OutputTargetConfig Target,
    PhysicalDeviceInventoryStatus? Status,
    bool IsConfigured)
{
    public bool CanEdit => Status == PhysicalDeviceInventoryStatus.Connected &&
        OutputTargetContract.HasIntegratedTransport(Target.Transport);
}

// A detached physical-output draft. Discovery never saves or opens a device.
internal sealed class DeviceSettingsModel
{
    private readonly List<OutputTargetConfig> _targets;
    private readonly List<OutputTargetConfig> _candidates = [];
    private readonly HashSet<string> _removedBindings = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _initialEnabledBindings;
    private readonly HashSet<string> _enableConsent = new(StringComparer.OrdinalIgnoreCase);
    private readonly string _defaultDashboard;
    private readonly AppConfig _defaults;
    private IReadOnlyList<OutputDeviceDescriptor> _discovered = [];
    private bool _discoveryAvailable;
    private IReadOnlySet<string> _unavailableTransports = new HashSet<string>();

    public DeviceSettingsModel(AppConfig config, string defaultDashboard)
    {
        _defaults = config;
        _defaultDashboard = defaultDashboard;
        _targets = config.Outputs.Targets
            .Where(t => OutputTargetContract.IsPhysicalKind(t.Kind)).Select(Copy).ToList();
        _initialEnabledBindings = _targets.Where(t => t.DeviceEnabled)
            .ToDictionary(t => t.Id, t => t.DeviceId, StringComparer.OrdinalIgnoreCase);
    }

    public bool IsDirty { get; private set; }
    public int ConfiguredCount => _targets.Count;

    public IReadOnlyList<DeviceSettingsEntry> Entries => PhysicalDeviceInventory
        .Build(_targets.Concat(_candidates).ToArray(), _discovered)
        .Where(e => e.ConfiguredTarget is not null)
        .Select(e => new DeviceSettingsEntry(e.ConfiguredTarget!,
            _discoveryAvailable && !_unavailableTransports.Contains(e.ConfiguredTarget!.Transport) ? e.Status : null, _targets.Contains(e.ConfiguredTarget!)))
        .ToArray();

    public void Refresh(IReadOnlyList<OutputDeviceDescriptor> discovered, IReadOnlySet<string>? unavailableTransports = null)
    {
        _discovered = discovered;
        _unavailableTransports = unavailableTransports ?? new HashSet<string>();
        _discoveryAvailable = true;
        foreach (var entry in PhysicalDeviceInventory.Build(_targets, discovered))
        {
            if (entry.IsPersisted || entry.DiscoveredDevice is not { } device ||
                _removedBindings.Contains(device.DeviceId) ||
                _targets.Any(t => t.DeviceId.Equals(device.DeviceId, StringComparison.OrdinalIgnoreCase)) ||
                _candidates.Any(t => t.DeviceId.Equals(device.DeviceId, StringComparison.OrdinalIgnoreCase)))
                continue;
            _candidates.Add(new OutputTargetConfig
            {
                Id = "screen-" + Guid.NewGuid().ToString("N"),
                Name = device.DisplayName,
                Kind = device.Transport.Equals(OutputTargetContract.TurzxTransport, StringComparison.OrdinalIgnoreCase)
                    ? OutputTargetContract.TurzxKind : OutputTargetContract.TrofeoKind,
                Transport = device.Transport,
                DeviceId = device.DeviceId,
                Dashboard = _defaultDashboard,
                RefreshIntervalMs = _defaults.Display.RefreshIntervalMs,
                JpegQuality = _defaults.Renderer.JpegQuality,
                Usb = device.Transport.Equals(OutputTargetContract.TurzxTransport, StringComparison.OrdinalIgnoreCase)
                    ? new UsbConfig { RetryIntervalMs = _defaults.Usb.RetryIntervalMs, TransferTimeoutMs = _defaults.Usb.TransferTimeoutMs }
                    : CopyUsb(_defaults.Usb),
                Turzx = device.Transport.Equals(OutputTargetContract.TurzxTransport, StringComparison.OrdinalIgnoreCase)
                    ? new TurzxOutputConfig() : null
            });
        }
    }

    public void DiscoveryUnavailable() => _discoveryAvailable = false;

    public void Add(OutputTargetConfig target)
    {
        RequireEditable(target);
        if (!_candidates.Remove(target))
            throw new InvalidOperationException("This device is already configured.");
        _targets.Add(target);
        IsDirty = true;
    }

    public void MarkChanged(OutputTargetConfig target)
    {
        RequireEditable(target);
        if (_targets.Contains(target))
            IsDirty = true;
    }

    public bool CanEnable(OutputTargetConfig target)
    {
        var entry = Entries.SingleOrDefault(e => ReferenceEquals(e.Target, target));
        if (entry?.CanEdit != true || string.IsNullOrWhiteSpace(target.DeviceId))
            return false;
        if (target.Kind == OutputTargetContract.TurzxKind)
        {
            try { TurzxOutputConfig.ValidateFrameOptions(target.Turzx?.BrightnessPercent, (target.Usb ?? _defaults.Usb).DiagnosticJournalEnabled); }
            catch (NotSupportedException) { return false; }
        }
        var resolved = OutputDeviceSelection.Resolve(target, _discovered);
        return resolved.Status == OutputDeviceResolutionStatus.Found &&
               resolved.Device!.DeviceId.Equals(target.DeviceId, StringComparison.OrdinalIgnoreCase);
    }

    public bool SetEnabled(OutputTargetConfig target, bool enabled, bool confirmed)
    {
        RequireEditable(target);
        if (enabled && (!confirmed || !CanEnable(target)))
            return false;
        if (target.DeviceEnabled == enabled)
            return true;
        if (enabled)
        {
            _enableConsent.Add(target.Id);
            if (_candidates.Contains(target))
                Add(target);
        }
        target.DeviceEnabled = enabled;
        MarkChanged(target);
        return true;
    }

    public bool CanRemove(OutputTargetConfig target, int desktopCount) =>
        _candidates.Contains(target) || (_targets.Contains(target) && _targets.Count + desktopCount > 1);

    public void Remove(OutputTargetConfig target, int desktopCount)
    {
        if (!CanRemove(target, desktopCount))
            throw new InvalidOperationException("Cannot remove the last Output or an unknown device.");
        if (_targets.Remove(target))
            IsDirty = true;
        _candidates.Remove(target);
        _removedBindings.Add(target.DeviceId);
        _enableConsent.Remove(target.Id);
    }

    // Recheck discovery just before persistence; consent cannot survive identity loss.
    public void ValidateForApply(IReadOnlyList<OutputDeviceDescriptor> discovered, IReadOnlySet<string>? unavailableTransports = null)
    {
        Refresh(discovered, unavailableTransports);
        foreach (var target in _targets.Where(t => t.DeviceEnabled))
        {
            if (_initialEnabledBindings.TryGetValue(target.Id, out var binding) &&
                binding.Equals(target.DeviceId, StringComparison.OrdinalIgnoreCase) &&
                !_enableConsent.Contains(target.Id))
                continue;
            if (!_enableConsent.Contains(target.Id) || !CanEnable(target))
                throw new InvalidOperationException(
                    $"Device '{target.Name}' cannot be enabled: its identity is missing or ambiguous. Reopen Settings and retry.");
        }
    }

    public void ApplyTo(AppConfig config, bool allowEmptyDraft = false)
    {
        if (!IsDirty)
            return;
        var byId = _targets.ToDictionary(t => t.Id, StringComparer.OrdinalIgnoreCase);
        var updated = new List<OutputTargetConfig>();
        foreach (var target in config.Outputs.Targets)
        {
            if (!OutputTargetContract.IsPhysicalKind(target.Kind))
                updated.Add(target);
            else if (byId.Remove(target.Id, out var replacement))
                updated.Add(Copy(replacement));
        }
        updated.AddRange(_targets.Where(t => byId.ContainsKey(t.Id)).Select(Copy));
        if (updated.Count == 0 && !allowEmptyDraft)
            throw new InvalidOperationException("At least one Output must remain configured.");
        config.Outputs.Targets = updated;
    }

    private void RequireEditable(OutputTargetConfig target)
    {
        if (Entries.SingleOrDefault(e => ReferenceEquals(e.Target, target))?.CanEdit != true)
            throw new InvalidOperationException("Device settings require an unambiguous connected device.");
    }

    private static UsbConfig CopyUsb(UsbConfig source) => new()
    {
        RetryIntervalMs = source.RetryIntervalMs,
        TransferTimeoutMs = source.TransferTimeoutMs,
        DiagnosticJournalEnabled = source.DiagnosticJournalEnabled
    };

    private OutputTargetConfig Copy(OutputTargetConfig source) => new()
    {
        Id = source.Id, Name = source.Name, Kind = source.Kind, Transport = source.Transport,
        DeviceId = source.DeviceId, Dashboard = source.Dashboard ?? _defaults.Dashboard.Active,
        RefreshIntervalMs = source.RefreshIntervalMs ?? _defaults.Display.RefreshIntervalMs,
        ProviderEnabled = source.ProviderEnabled, DeviceEnabled = source.DeviceEnabled,
        JpegQuality = source.JpegQuality ?? _defaults.Renderer.JpegQuality,
        Usb = CopyUsb(source.Usb ?? _defaults.Usb),
        Turzx = source.Turzx is null ? null : new TurzxOutputConfig
        { BrightnessPercent = source.Turzx.BrightnessPercent }
    };
}
