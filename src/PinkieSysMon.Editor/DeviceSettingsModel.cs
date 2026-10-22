using System.Text.Json;

namespace PinkieSysMon.Editor;

internal sealed record DeviceSettingsEntry(
    OutputTargetConfig Target,
    PhysicalDeviceInventoryStatus? Status,
    bool IsConfigured)
{
    public bool CanEdit => Status == PhysicalDeviceInventoryStatus.Connected &&
        (!IsConfigured || OutputTargetContract.HasIntegratedTransport(Target.Transport));
}

// A detached physical-output draft. Discovery never saves or opens a device.
internal sealed class DeviceSettingsModel
{
    private readonly List<OutputTargetConfig> _targets;
    private readonly List<OutputTargetConfig> _candidates = [];
    private readonly Dictionary<string, CatalogUsbCandidateDraft> _drafts = new(StringComparer.OrdinalIgnoreCase);

    internal CatalogUsbCandidateDraft? CandidateDraft(OutputTargetConfig target) => _drafts.GetValueOrDefault(target.Id);
    private readonly HashSet<string> _removedBindings = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _ignoredNodes;
    private readonly Dictionary<string, string> _initialEnabledBindings;
    private readonly HashSet<string> _enableConsent = new(StringComparer.OrdinalIgnoreCase);
    private readonly AppConfig _defaults;
    private readonly Dictionary<string, OutputTargetConfig> _originalConnections;
    private IReadOnlyList<OutputDeviceDescriptor> _discovered = [];
    private bool _discoveryAvailable;
    private IReadOnlySet<string> _unavailableTransports = new HashSet<string>();

    public DeviceSettingsModel(AppConfig config)
    {
        _defaults = config;
        _ignoredNodes = config.Outputs.IgnoredDeviceNodeKeys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        _targets = config.Outputs.Targets
            .Where(t => OutputTargetContract.IsPhysicalKind(t.Kind)).Select(Copy).ToList();
        _originalConnections = _targets.ToDictionary(t => t.Id, Copy, StringComparer.OrdinalIgnoreCase);
        _initialEnabledBindings = _targets.Where(t => t.DeviceEnabled)
            .ToDictionary(t => t.Id, t => t.DeviceId, StringComparer.OrdinalIgnoreCase);
    }

    public bool IsDirty { get; private set; }
    internal bool HasPendingRemoval => _originalConnections.Keys.Any(id =>
        !_targets.Any(target => target.Id.Equals(id, StringComparison.OrdinalIgnoreCase)));
    public int ConfiguredCount => _targets.Count;
    internal IReadOnlySet<string> IgnoredNodes => _ignoredNodes;

    public IReadOnlyList<DeviceSettingsEntry> Entries
    {
        get
        {
            var entries = PhysicalDeviceInventory.Build(_targets, _discovered)
                .Where(e => e.ConfiguredTarget is not null)
                .Select(e => new DeviceSettingsEntry(e.ConfiguredTarget!,
                    _discoveryAvailable && (e.DiscoveredDevice is not null ||
                        !_unavailableTransports.Contains(e.ConfiguredTarget!.Transport)) ? e.Status : null, true)).ToList();
            var ambiguous = OutputDeviceSelection.FindAmbiguousDeviceIds(_discovered);
            foreach (var candidate in _candidates)
            {
                var present = _discovered.Any(d => d.DeviceId.Equals(candidate.DeviceId, StringComparison.OrdinalIgnoreCase));
                PhysicalDeviceInventoryStatus? status = !_discoveryAvailable ? null :
                    ambiguous.Contains(candidate.DeviceId) ? PhysicalDeviceInventoryStatus.Ambiguous :
                    present ? PhysicalDeviceInventoryStatus.Connected :
                    _unavailableTransports.Count != 0 ? null : PhysicalDeviceInventoryStatus.Disconnected;
                entries.Add(new(candidate, status, false));
            }
            return entries;
        }
    }

    public void Refresh(IReadOnlyList<OutputDeviceDescriptor> discovered, IReadOnlySet<string>? unavailableTransports = null)
    {
        _discovered = discovered;
        _unavailableTransports = unavailableTransports ?? new HashSet<string>();
        _discoveryAvailable = true;
        foreach (var entry in PhysicalDeviceInventory.Build(_targets, discovered))
        {
            if (entry.IsPersisted || entry.DiscoveredDevice is not { } device ||
                device.PnpNodeKey is { } node && _ignoredNodes.Contains(node) ||
                _removedBindings.Contains(device.DeviceId) ||
                _targets.Any(t => t.DeviceId.Equals(device.DeviceId, StringComparison.OrdinalIgnoreCase)) ||
                _candidates.Any(t => t.DeviceId.Equals(device.DeviceId, StringComparison.OrdinalIgnoreCase)))
                continue;
            var candidate = new OutputTargetConfig
            {
                Id = "screen-" + Guid.NewGuid().ToString("N"),
                Name = device.DisplayName == "USB display" ? $"Device #{_targets.Count + _candidates.Count + 1}" : device.DisplayName,
                Kind = OutputTargetContract.PhysicalKind,
                Transport = device.Transport,
                DeviceId = device.DeviceId,
                Dashboard = null,
                RefreshIntervalMs = _defaults.Display.RefreshIntervalMs,
                JpegQuality = _defaults.Renderer.JpegQuality,
                Usb = device.Transport.Equals(OutputTargetContract.EncryptedJpegTransport, StringComparison.OrdinalIgnoreCase)
                    ? new UsbConfig { RetryIntervalMs = _defaults.Usb.RetryIntervalMs, TransferTimeoutMs = _defaults.Usb.TransferTimeoutMs }
                    : CopyUsb(_defaults.Usb),
                EncryptedJpeg = device.Transport.Equals(OutputTargetContract.EncryptedJpegTransport, StringComparison.OrdinalIgnoreCase)
                    ? new EncryptedJpegOutputConfig() : null
            };
            if (device.Candidate is { } observed)
                _drafts.Add(candidate.Id, new CatalogUsbCandidateDraft(observed, candidate.Name,
                    UsbDisplayCatalog.ProtocolChoices(observed.Family).Where(choice => choice.IsImplemented).Select(choice => choice.Id)));
            _candidates.Add(candidate);
        }
    }

    public void DiscoveryUnavailable() => _discoveryAvailable = false;

    internal string PrepareForget(OutputTargetConfig target)
    {
        if (!_candidates.Contains(target) || CandidateDraft(target) is not { } draft || draft.IsTesting)
            throw new InvalidOperationException("Only an idle unconfigured candidate can be forgotten.");
        return draft.PnpNodeKey;
    }

    internal void AcceptForgot(OutputTargetConfig target, string node)
    {
        if (!_candidates.Remove(target))
            throw new InvalidOperationException("The candidate changed during Forget.");
        _drafts.Remove(target.Id);
        _ignoredNodes.Add(node);
    }

    public void Add(OutputTargetConfig target)
    {
        var confirmed = PrepareCandidate(target);
        AcceptSavedCandidate(target, confirmed);
        IsDirty = true;
    }

    // Build the exact confirmed snapshot without converting the visible card.
    // A failed file/apply transaction leaves its draft and approval intact.
    internal OutputTargetConfig PrepareCandidate(OutputTargetConfig target)
    {
        RequireEditable(target);
        if (!_candidates.Contains(target))
            throw new InvalidOperationException("This device is already configured.");
        var draft = CandidateDraft(target)
            ?? throw new InvalidOperationException("This candidate has no verified interface metadata.");
        var confirmed = draft.CreateConfirmedOutput(target.Id);
        var resolution = OutputDeviceSelection.Resolve(confirmed, _discovered);
        if (resolution.Status != OutputDeviceResolutionStatus.Found ||
            !string.Equals(resolution.Device!.PnpNodeKey, draft.PnpNodeKey, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The confirmed interface is no longer uniquely present on the same physical node.");
        return confirmed;
    }

    internal void AcceptSavedCandidate(OutputTargetConfig target, OutputTargetConfig confirmed)
    {
        if (!_candidates.Contains(target) || target.Id != confirmed.Id)
            throw new InvalidOperationException("The candidate card changed during Save.");
        target.Name = confirmed.Name; target.Transport = confirmed.Transport;
        target.Width = confirmed.Width; target.Height = confirmed.Height;
        target.Dashboard = confirmed.Dashboard; target.DeviceEnabled = false;
        target.EncryptedJpeg = confirmed.EncryptedJpeg;
        target.Usb = confirmed.Usb; target.JpegQuality = confirmed.JpegQuality;
        target.RefreshIntervalMs = confirmed.RefreshIntervalMs; target.ProviderEnabled = false;
        _candidates.Remove(target);
        _drafts.Remove(target.Id);
        _targets.Add(target);
        _originalConnections.Add(target.Id, Copy(target));
    }

    internal (OutputTargetConfig Original, OutputTargetConfig Updated) PrepareConfigured(OutputTargetConfig target)
    {
        RequireEditable(target);
        if (!_targets.Contains(target) || !_originalConnections.TryGetValue(target.Id, out var original))
            throw new InvalidOperationException("This configured device card is no longer available.");
        if (!OutputTargetContract.ConnectionEquals(original, target))
            throw new InvalidOperationException("Changing the connection requires Remove, Test and Save Configuration.");
        if (target.DeviceEnabled && (!original.DeviceEnabled ||
            !original.DeviceId.Equals(target.DeviceId, StringComparison.OrdinalIgnoreCase)) &&
            (!_enableConsent.Contains(target.Id) || !CanEnable(target)))
            throw new InvalidOperationException("Enablement requires current exact identity and explicit consent.");
        var updated = Copy(target);
        OutputTargetContract.ValidateAndNormalize([updated]);
        return (Copy(original), updated);
    }

    internal bool HasPendingConfiguredEdit(OutputTargetConfig target) =>
        _targets.Contains(target) && _originalConnections.TryGetValue(target.Id, out var original) &&
        JsonSerializer.Serialize(target) != JsonSerializer.Serialize(original);

    internal void AcceptSavedConfigured(OutputTargetConfig target, OutputTargetConfig updated)
    {
        if (!_targets.Contains(target) || target.Id != updated.Id)
            throw new InvalidOperationException("The configured device card changed during Save.");
        _originalConnections[target.Id] = Copy(updated);
        _initialEnabledBindings.Remove(target.Id);
        if (updated.DeviceEnabled) _initialEnabledBindings[target.Id] = updated.DeviceId;
        _enableConsent.Remove(target.Id);
        IsDirty = _targets.Count != _originalConnections.Count ||
            _targets.Any(HasPendingConfiguredEdit);
    }

    public void MarkChanged(OutputTargetConfig target)
    {
        RequireEditable(target);
        if (_targets.Contains(target))
            IsDirty = true;
        else if (CandidateDraft(target) is { } draft)
        {
            draft.Name = target.Name; draft.Width = target.Width;
            draft.Height = target.Height; draft.Protocol = target.Transport;
        }
    }

    public bool CanEnable(OutputTargetConfig target)
    {
        var entry = Entries.SingleOrDefault(e => ReferenceEquals(e.Target, target));
        if (!_targets.Contains(target) || entry?.CanEdit != true || string.IsNullOrWhiteSpace(target.DeviceId))
            return false;
        if (target.Transport == OutputTargetContract.EncryptedJpegTransport)
        {
            try { EncryptedJpegOutputConfig.ValidateFrameOptions(target.EncryptedJpeg?.BrightnessPercent, (target.Usb ?? _defaults.Usb).DiagnosticJournalEnabled); }
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
        foreach (var target in _targets)
            if (_originalConnections.TryGetValue(target.Id, out var original) &&
                !OutputTargetContract.ConnectionEquals(original, target))
                throw new InvalidOperationException("Changing a configured connection requires Remove, Test and Save Configuration.");
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
        DeviceId = source.DeviceId, Dashboard = source.Dashboard,
        Width = source.Width, Height = source.Height,
        RefreshIntervalMs = source.RefreshIntervalMs ?? _defaults.Display.RefreshIntervalMs,
        ProviderEnabled = source.ProviderEnabled, DeviceEnabled = source.DeviceEnabled,
        JpegQuality = source.JpegQuality ?? _defaults.Renderer.JpegQuality,
        Usb = CopyUsb(source.Usb ?? _defaults.Usb),
        EncryptedJpeg = source.EncryptedJpeg is null ? null : new EncryptedJpegOutputConfig
        { BrightnessPercent = source.EncryptedJpeg.BrightnessPercent }
    };
}
