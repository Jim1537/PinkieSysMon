namespace PinkieSysMon.Editor;

// Edits only virtual Outputs. A dialog owns this detached working copy until OK.
// Physical targets and dashboard files are never owned or modified here.
internal sealed class DesktopSettingsModel
{
    private readonly List<OutputTargetConfig> _desktops;
    private readonly Func<int> _physicalCount;

    public DesktopSettingsModel(AppConfig config, Func<int>? physicalCount = null)
    {
        ArgumentNullException.ThrowIfNull(config);
        var initialPhysicalCount = config.Outputs.Targets.Count(t => t.Kind != OutputTargetContract.DesktopKind);
        _physicalCount = physicalCount ?? (() => initialPhysicalCount);
        _desktops = config.Outputs.Targets
            .Select((target, index) => (target, index))
            .Where(x => x.target.Kind == OutputTargetContract.DesktopKind)
            .OrderBy(x => x.target.Desktop?.ZOrder ?? 0)
            .ThenBy(x => x.index)
            .Select(x => Copy(x.target))
            .ToList();
    }

    public IReadOnlyList<OutputTargetConfig> Desktops => _desktops;
    public bool IsDirty { get; private set; }

    public OutputTargetConfig Create(string dashboard)
    {
        if (string.IsNullOrWhiteSpace(dashboard))
            throw new InvalidOperationException("Create a dashboard before creating a Desktop Output.");
        var suffix = 1;
        while (_desktops.Any(x => x.Name.Equals($"Desktop {suffix}", StringComparison.OrdinalIgnoreCase)))
            suffix++;
        var name = $"Desktop {suffix}";
        // Keep list order and persisted ZOrder consistent even with old gaps/ties.
        for (var i = 0; i < _desktops.Count; i++)
            _desktops[i].Desktop!.ZOrder = i;
        var result = new OutputTargetConfig
        {
            Id = "desktop-" + Guid.NewGuid().ToString("N"),
            Name = name,
            Kind = OutputTargetContract.DesktopKind,
            Transport = OutputTargetContract.DesktopTransport,
            Dashboard = dashboard,
            RefreshIntervalMs = 1000,
            ProviderEnabled = false,
            Desktop = new DesktopOutputConfig
            {
                BoardEnabled = true,
                Lock = true,
                X = 40 * _desktops.Count,
                Y = 40 * _desktops.Count,
                ZOrder = _desktops.Count
            }
        };
        _desktops.Add(result);
        IsDirty = true;
        return result;
    }

    public bool CanRemove(OutputTargetConfig target) =>
        _desktops.Contains(target) && _desktops.Count + _physicalCount() > 1;

    public void Remove(OutputTargetConfig target)
    {
        if (!CanRemove(target))
            throw new InvalidOperationException("Cannot remove the last Output or an unknown Desktop.");
        _desktops.Remove(target);
        Renumber();
    }

    public void Move(int fromIndex, int toIndex)
    {
        if (fromIndex < 0 || toIndex < 0 || fromIndex >= _desktops.Count || toIndex >= _desktops.Count)
            throw new ArgumentOutOfRangeException(nameof(toIndex));
        if (fromIndex == toIndex)
            return;
        var target = _desktops[fromIndex];
        _desktops.RemoveAt(fromIndex);
        _desktops.Insert(toIndex, target);
        Renumber();
    }

    public void MarkChanged() => IsDirty = true;

    public void ApplyTo(AppConfig config, bool allowEmptyDraft = false)
    {
        ArgumentNullException.ThrowIfNull(config);
        if (!IsDirty)
            return;
        var queued = new Queue<OutputTargetConfig>(_desktops);
        var updated = new List<OutputTargetConfig>();
        foreach (var target in config.Outputs.Targets)
        {
            if (target.Kind == OutputTargetContract.DesktopKind)
            {
                if (queued.Count > 0)
                    updated.Add(Copy(queued.Dequeue()));
            }
            else
            {
                updated.Add(target); // Keep each physical Output object, untouched.
            }
        }
        while (queued.Count > 0)
            updated.Add(Copy(queued.Dequeue()));
        if (updated.Count == 0 && !allowEmptyDraft)
            throw new InvalidOperationException("At least one Output must remain configured.");
        config.Outputs.Targets = updated;
    }

    private void Renumber()
    {
        for (var i = 0; i < _desktops.Count; i++)
            _desktops[i].Desktop!.ZOrder = i;
        IsDirty = true;
    }

    private static OutputTargetConfig Copy(OutputTargetConfig original) => new()
    {
        Id = original.Id,
        Name = original.Name,
        Kind = original.Kind,
        Transport = original.Transport,
        Dashboard = original.Dashboard,
        RefreshIntervalMs = original.RefreshIntervalMs,
        ProviderEnabled = original.ProviderEnabled,
        Desktop = new DesktopOutputConfig
        {
            BoardEnabled = original.Desktop?.BoardEnabled ?? true,
            Lock = original.Desktop?.Lock ?? true,
            X = original.Desktop?.X ?? 0,
            Y = original.Desktop?.Y ?? 0,
            ZOrder = original.Desktop?.ZOrder ?? 0
        }
    };
}
