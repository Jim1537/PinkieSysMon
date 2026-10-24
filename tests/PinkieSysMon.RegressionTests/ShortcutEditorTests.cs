using System.ComponentModel;
using System.Drawing.Design;
using PinkieSysMon.Editor;
using C = PinkieSysMon.DashboardModel;

namespace PinkieSysMon.RegressionTests;

internal static class ShortcutEditorTests
{
    public static void TargetEditingAndSnapshots()
    {
        var shortcut = ShortcutModelTests.Shortcut(); shortcut.Label = "";
        var board = ShortcutModelTests.Board(shortcut);
        var before = DashboardJson.SerializeEditorSnapshot(board);
        var general = new EditorPropertyView(shortcut, section: EditorPropertySection.General).GetProperties();
        var target = (CanonicalPropertyDescriptor)TypeDescriptor.GetProperties(general["Activation"]!.GetValue(null)!)["Target"]!;
        string? checkpoint = null;
        target.BeforeValueChange = () => checkpoint = DashboardJson.SerializeEditorSnapshot(board);
        target.SetValue(null, Path.Combine(Path.GetTempPath(), "Chosen shortcut.lnk"));
        Check(shortcut.Label == "Chosen shortcut" && checkpoint == before,
            "The actual Target property must fill the label after the existing pre-edit undo checkpoint.");
        var after = DashboardJson.SerializeEditorSnapshot(board);
        var path = Path.Combine(Path.GetTempPath(), "shortcut-label-history.json");
        var undone = (C.ShortcutWidgetDefinition)DashboardJson.ParseEditorSnapshot(checkpoint!, path).Widgets[0];
        var redone = (C.ShortcutWidgetDefinition)DashboardJson.ParseEditorSnapshot(after, path).Widgets[0];
        Check(undone.Label == "" && undone.Target != shortcut.Target && redone.Label == shortcut.Label && redone.Target == shortcut.Target,
            "Target and the initial label must share one snapshot transition for undo/redo.");

        var manual = ShortcutModelTests.Shortcut(); manual.Label = "Keep this label";
        shortcut.Label = "";
        var multi = new EditorMultiPropertyView([
            new EditorPropertyView(shortcut, section: EditorPropertySection.General),
            new EditorPropertyView(manual, section: EditorPropertySection.General)
        ]);
        var activation = TypeDescriptor.GetProperties(multi.GetProperties()["Activation"]!.GetValue(null)!);
        var selected = Path.Combine(Path.GetTempPath(), "Shared choice.exe");
        activation["Target"]!.SetValue(null, selected);
        Check(shortcut.Target == selected && manual.Target == selected && shortcut.Label == "Shared choice" && manual.Label == "Keep this label",
            "Multi-selection must apply the same Target while preserving each widget's independent label policy.");
    }

    public static void PropertiesAndApplicability()
    {
        var s = ShortcutModelTests.Shortcut(); s.LabelPosition = ShortcutContract.Bottom;
        PropertyDescriptorCollection View(EditorPropertySection section) => new EditorPropertyView(s, section: section).GetProperties();
        static PropertyDescriptorCollection Rows(PropertyDescriptorCollection groups, string name) => TypeDescriptor.GetProperties(groups[name]!.GetValue(null)!);
        var general = View(EditorPropertySection.General); var activation = Rows(general, "Activation");
        Check(activation["Target"]!.GetEditor(typeof(UITypeEditor)) is ShortcutTargetEditor && activation["Arguments"] is not null,
            "Target must have the standard picker; Arguments must remain an independent editable field.");
        var target = s.Target; var arguments = s.Arguments;
        var source = Rows(general, "Source");
        Check(source["Source"]!.IsReadOnly, "Auto has no custom asset source.");
        source["SourceType"]!.SetValue(null, "Image"); source["Source"]!.SetValue(null, "images/custom.png");
        source["SourceType"]!.SetValue(null, "Auto");
        Check(s.Asset.Source is null && s.Target == target && s.Arguments == arguments && source["Source"]!.IsReadOnly,
            "Changing artwork must clear only the custom Source, preserving the exact Shell payload.");
        activation["Activation"]!.SetValue(null, "Double click");
        Check(s.Activation == ShortcutContract.DoubleClick && s.Target == target, "Activation selection must update only its canonical field.");
        Check(!Rows(general, "Geometry")["Height"]!.IsReadOnly && View(EditorPropertySection.Data).Count == 0 && View(EditorPropertySection.States).Count == 0,
            "Shortcut has independent rectangular geometry and no invented telemetry/state properties.");
        var text = View(EditorPropertySection.Text); var label = Rows(text, "Label");
        label["Gap"]!.SetValue(null, -40f); label["LabelPosition"]!.SetValue(null, "Middle");
        Check(s.Gap == -40 && label["Gap"]!.IsReadOnly, "Middle must retain signed Gap while disabling its editor.");
        label["LabelPosition"]!.SetValue(null, "Top");
        Check(!label["Gap"]!.IsReadOnly && s.Gap == -40, "Leaving Middle must restore the retained signed Gap.");
        var overflow = Rows(text, "Overflow");
        Check(!overflow["OverflowMode"]!.Converter.GetStandardValues()!.Cast<object>().Contains("None"), "Shortcut must not offer unconstrained None.");
        overflow["OverflowMode"]!.SetValue(null, "Scroll");
        Check(!overflow["ScrollSpeed"]!.IsReadOnly && overflow["BumpPauseMs"]!.IsReadOnly, "Scroll applicability must use the shared rules.");
        overflow["OverflowMode"]!.SetValue(null, "Bump");
        Check(!overflow["BumpPauseMs"]!.IsReadOnly, "Bump must expose its pause.");
        var image = Rows(View(EditorPropertySection.Image), "Image");
        Check(image["Loop"]!.IsReadOnly && !image["Opacity"]!.IsReadOnly, "Auto is static; graphic opacity remains editable.");
        Check(EditorForm.IsPropertyContextSwitch("LabelPosition"), "Changing label position must refresh applicability in the actual property UI.");
        var second = ShortcutModelTests.Shortcut(); second.LabelPosition = ShortcutContract.Top; second.Gap = -5;
        var multi = new EditorMultiPropertyView([
            new EditorPropertyView(s, section: EditorPropertySection.Text),
            new EditorPropertyView(second, section: EditorPropertySection.Text)
        ]);
        Rows(multi.GetProperties(), "Label")["Gap"]!.SetValue(null, -20f);
        Check(s.Gap == -20 && second.Gap == -20, "Compatible multi-selection must edit both canonical signed Gap fields.");
        var snapshot = DashboardJson.SerializeEditorSnapshot(ShortcutModelTests.Board(s));
        var restored = DashboardJson.ParseEditorSnapshot(snapshot, Path.Combine(Path.GetTempPath(), "shortcut-editor.json"));
        Check(DashboardJson.SerializeEditorSnapshot(restored) == snapshot, "Undo/redo snapshots must preserve Shortcut and its independent properties.");
    }

    public static void ClipboardCopiesOnlyArtwork()
    {
        var root = Path.Combine(Path.GetTempPath(), "Pinkie-Shortcut-clipboard-" + Guid.NewGuid().ToString("N"));
        var source = Path.Combine(root, "source"); var destination = Path.Combine(root, "destination");
        Directory.CreateDirectory(Path.Combine(source, "images")); Directory.CreateDirectory(destination);
        try
        {
            File.WriteAllBytes(Path.Combine(source, "images", "custom.png"), [1, 2, 3]);
            var s = ShortcutModelTests.Shortcut(); s.Asset.SourceType = "file"; s.Asset.Source = "images/custom.png";
            var target = s.Target; var arguments = s.Arguments; var files = new List<string>();
            Check(EditorForm.ResolveClipboardImages([s], source, destination, files, _ => System.Windows.Forms.DialogResult.Yes,
                _ => System.Windows.Forms.DialogResult.No), "Paste must import a custom dashboard image through the existing consent path.");
            Check(files.Count == 1 && File.ReadAllBytes(files[0]).SequenceEqual(new byte[] { 1, 2, 3 }) && s.Target == target && s.Arguments == arguments,
                "Paste must copy actual artwork without importing or rewriting the Shell target/arguments.");
            var auto = ShortcutModelTests.Shortcut();
            Check(EditorForm.ResolveClipboardImages([auto], source, destination, [], _ => throw new InvalidOperationException("Auto must not request asset import."),
                _ => throw new InvalidOperationException("Auto must not request a collision choice.")), "Auto has no clipboard asset dependency.");
        }
        finally { Directory.Delete(root, true); }
    }

    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
