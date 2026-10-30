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
    private readonly Dictionary<EditorPropertySection, Dictionary<string, bool>> _state = new();

    public IReadOnlyDictionary<string, bool> Get(EditorPropertySection section) =>
        GetMutable(section);

    public void Remember(EditorPropertySection section, IReadOnlyDictionary<string, bool> visibleState)
    {
        if (visibleState.Count == 0)
            return;

        var sectionState = GetMutable(section);
        foreach (var pair in visibleState)
            sectionState[pair.Key] = pair.Value;
    }

    private Dictionary<string, bool> GetMutable(EditorPropertySection section)
    {
        if (!_state.TryGetValue(section, out var sectionState))
        {
            sectionState = new Dictionary<string, bool>(GetDefaults(section), StringComparer.Ordinal);
            _state[section] = sectionState;
        }

        return sectionState;
    }

    private static IReadOnlyDictionary<string, bool> GetDefaults(EditorPropertySection section) => section switch
    {
        EditorPropertySection.General => new Dictionary<string, bool>(StringComparer.Ordinal)
        {
            ["Identity"] = false,
            ["Source"] = true,
            ["Background Image"] = true,
            ["Foreground Image"] = true,
            ["Geometry"] = true
        },
        EditorPropertySection.Appearance => new Dictionary<string, bool>(StringComparer.Ordinal)
        {
            ["Foreground"] = true,
            ["Background"] = true,
            ["Border"] = true,
            ["Shadow"] = false,
            ["Threshold 1"] = false,
            ["Threshold 2"] = false,
            ["Threshold 3"] = false
        },
        EditorPropertySection.Data => new Dictionary<string, bool>(StringComparer.Ordinal)
        {
            ["Value"] = true,
            ["Display"] = true,
            ["Range"] = true,
            ["Evaluation"] = true
        },
        EditorPropertySection.Gauge => new Dictionary<string, bool>(StringComparer.Ordinal)
        {
            ["Horseshoe"] = true,
            ["Needle"] = true,
            ["Bar"] = true
        },
        EditorPropertySection.Image => new Dictionary<string, bool>(StringComparer.Ordinal)
        {
            ["Image"] = true,
            ["Background Image"] = true,
            ["Foreground Image"] = true
        },
        EditorPropertySection.Text => new Dictionary<string, bool>(StringComparer.Ordinal)
        {
            ["Font"] = true,
            ["Alignment"] = true,
            ["Outline"] = false,
            ["Overflow"] = false
        },
        _ => new Dictionary<string, bool>(StringComparer.Ordinal)
    };
}
