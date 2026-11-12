using PinkieSysMon;

namespace PinkieSysMon.Editor;

/// <summary>Editor-only names and grouping for addable widget types. Persisted IDs are never renamed.</summary>
internal static class EditorAddWidgetMenuContract
{
    private enum Group { Basic, Specialized }

    private sealed record Entry(string Type, string Label, Group Group);

    // Explicitly classify each addable widget here. Both Editor menus consume this single catalog.
    private static readonly Entry[] Entries =
    [
        new(WidgetTypeContract.Binary, "Binary Indicator", Group.Basic),
        new(WidgetTypeContract.Calculator, "Calculator", Group.Basic),
        new(WidgetTypeContract.Gauge, "Gauge", Group.Basic),
        new(WidgetTypeContract.Graph, "History Graph", Group.Basic),
        new(WidgetTypeContract.Image, "Image / Icon", Group.Basic),
        new(WidgetTypeContract.Bar, "Progress Bar", Group.Basic),
        new(WidgetTypeContract.Value, "Text / Value", Group.Basic),
        new(WidgetTypeContract.MediaSystem, "Audio Devices", Group.Specialized),
        new(WidgetTypeContract.MediaPlayer, "Playback Status", Group.Specialized),
        new(WidgetTypeContract.Power, "Power Status", Group.Specialized)
    ];

    // The menu catalog is the sole source of Editor addability.
    internal static bool IsAddable(string? type) =>
        Entries.Any(entry => WidgetTypeContract.Is(type, entry.Type));

    internal static void Populate(ToolStripItemCollection target, Action<string> onAddWidget)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(onAddWidget);

        Group? precedingGroup = null;
        foreach (var entry in Entries.OrderBy(entry => entry.Group)
                                     .ThenBy(entry => entry.Label, StringComparer.Ordinal))
        {
            if (precedingGroup is not null && precedingGroup != entry.Group)
                target.Add(new ToolStripSeparator());

            var type = entry.Type;
            target.Add(new ToolStripMenuItem(entry.Label, null, (_, _) => onAddWidget(type))
            {
                Tag = type
            });
            precedingGroup = entry.Group;
        }
    }
}
