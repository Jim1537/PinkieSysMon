using PinkieSysMon.Editor;
using C = PinkieSysMon.DashboardModel;

namespace PinkieSysMon.RegressionTests;

internal static class ShortcutTargetEditingTests
{
    public static void InitialLabelAndPreservation()
    {
        foreach (var (file, expected) in new[]
        {
            ("My App.exe", "My App"), ("My App.lnk", "My App"),
            ("Report.final.txt", "Report.final"), ("Library.dll", "Library"),
            ("Инструмент.EXE", "Инструмент"), ("extensionless", "extensionless")
        })
        {
            var shortcut = ShortcutModelTests.Shortcut(); shortcut.Label = "";
            var arguments = shortcut.Arguments;
            var target = Path.Combine(Path.GetTempPath(), "missing-shortcut-targets", file);
            ShortcutTargetEditing.SetTarget(shortcut, target);
            Check(shortcut.Target == target && shortcut.Label == expected && shortcut.Arguments == arguments,
                "An explicit Target edit must suggest the filename without its last extension, preserving the literal Shell payload.");

            ShortcutTargetEditing.SetTarget(shortcut, target + ".different");
            Check(shortcut.Label == expected, "Any nonempty label, including an earlier suggestion, must survive a later Target edit.");
            shortcut.Label = "";
            ShortcutTargetEditing.SetTarget(shortcut, shortcut.Target);
            Check(shortcut.Label == "", "Reassigning the same Target must not refill a deliberately cleared label.");
        }

        foreach (var label in new[] { "My custom label", " ", "00123", "First\nSecond" })
        {
            var shortcut = ShortcutModelTests.Shortcut(); shortcut.Label = label;
            ShortcutTargetEditing.SetTarget(shortcut, "another target.lnk");
            Check(shortcut.Label == label, "Nonempty literal labels must never be overwritten or normalized.");
        }

        var empty = ShortcutModelTests.Shortcut(); empty.Label = "";
        ShortcutTargetEditing.SetTarget(empty, "");
        Check(empty.Target == "" && empty.Label == "", "Clearing Target must not invent a label.");
    }

    public static void PersistenceNeverSuggestsLabel()
    {
        var shortcut = ShortcutModelTests.Shortcut(); shortcut.Label = "";
        shortcut.Target = "Existing application.lnk";
        var board = ShortcutModelTests.Board(shortcut);
        var path = Path.Combine(Path.GetTempPath(), "shortcut-label-persistence.json");
        var json = DashboardJson.Serialize(board, false);
        var loaded = C.DashboardDefinition.Parse(json, path);
        Check(((C.ShortcutWidgetDefinition)loaded.Widgets[0]).Label == "" && DashboardJson.Serialize(loaded, false) == json,
            "Loading and saving an existing Target with an empty Label must preserve the document exactly.");
        Check(((C.ShortcutWidgetDefinition)DashboardJson.CloneWidget(shortcut)).Label == "",
            "Clone/paste must not suggest a label outside an explicit Target edit.");
        var snapshot = DashboardJson.SerializeEditorSnapshot(board);
        Check(DashboardJson.SerializeEditorSnapshot(DashboardJson.ParseEditorSnapshot(snapshot, path)) == snapshot,
            "Undo/redo must preserve a deliberately empty label with an existing Target.");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
