namespace PinkieSysMon.Editor;

internal enum EditorInputSurface
{
    None,
    Canvas,
    Layers,
    Properties
}

internal readonly record struct EditorInputFocusSnapshot(EditorInputSurface Surface, int Revision);

internal sealed class EditorInputFocusState
{
    public EditorInputSurface Surface { get; private set; } = EditorInputSurface.None;
    public int Revision { get; private set; }

    public void Claim(EditorInputSurface surface)
    {
        Surface = surface;
        Revision++;
    }

    public EditorInputFocusSnapshot Capture() => new(Surface, Revision);

    public EditorInputFocusSnapshot CaptureForPreservation(EditorInputSurface actualSurface)
    {
        // Explicit Enter/pointer claims are authoritative. During a native control's
        // mouse activation, GetActualEditorInputSurface() can still report the control
        // that is losing focus while selection notifications are already being raised.
        // Only seed an unclaimed state from actual focus; never overwrite an explicit
        // user-surface claim with that transient, lagging value.
        if (Surface == EditorInputSurface.None && actualSurface != EditorInputSurface.None)
            Claim(actualSurface);

        return Capture();
    }

    public bool CanRestore(EditorInputFocusSnapshot snapshot) =>
        snapshot.Surface != EditorInputSurface.None &&
        snapshot.Revision == Revision &&
        snapshot.Surface == Surface;
}

internal sealed class EditorPropertyExpansionState
{
    // Persist only user-collapsed groups. A missing entry always means expanded,
    // including groups added by future widget types or property tabs.
    private readonly Dictionary<EditorPropertySection, Dictionary<string, bool>> _state = new();

    public EditorPropertyExpansionState(IReadOnlyDictionary<string, Dictionary<string, bool>>? saved = null)
    {
        if (saved is null)
            return;

        foreach (var (name, groups) in saved)
        {
            if (!Enum.TryParse<EditorPropertySection>(name, ignoreCase: false, out var section) ||
                section == EditorPropertySection.All || !Enum.IsDefined(section) || groups is null)
                continue;

            foreach (var (path, expanded) in groups)
            {
                if (!string.IsNullOrEmpty(path) && !expanded)
                    GetMutable(section)[path] = false;
            }
        }
    }

    public IReadOnlyDictionary<string, bool> Get(EditorPropertySection section) =>
        GetMutable(section);

    public bool IsExpanded(EditorPropertySection section, string groupPath) =>
        !GetMutable(section).TryGetValue(groupPath, out var expanded) || expanded;

    public void Remember(EditorPropertySection section, IReadOnlyDictionary<string, bool> visibleState)
    {
        if (visibleState.Count == 0)
            return;

        var sectionState = GetMutable(section);
        foreach (var (path, expanded) in visibleState)
        {
            if (string.IsNullOrEmpty(path))
                continue;

            if (expanded)
                sectionState.Remove(path);
            else
                sectionState[path] = false;
        }
    }

    public Dictionary<string, Dictionary<string, bool>> Snapshot() =>
        _state.Where(pair => pair.Value.Count > 0)
            .ToDictionary(
                pair => pair.Key.ToString(),
                pair => new Dictionary<string, bool>(pair.Value, StringComparer.Ordinal),
                StringComparer.Ordinal);

    private Dictionary<string, bool> GetMutable(EditorPropertySection section)
    {
        if (!_state.TryGetValue(section, out var sectionState))
        {
            sectionState = new Dictionary<string, bool>(StringComparer.Ordinal);
            _state[section] = sectionState;
        }

        return sectionState;
    }
}
