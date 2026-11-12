using System.ComponentModel;
using System.Drawing;
using System.Text.Json.Nodes;
using PinkieSysMon.Editor;
using C = PinkieSysMon.DashboardModel;

namespace PinkieSysMon.RegressionTests;

internal static class CalculatorWidgetTests
{
    private static C.CalculatorWidgetDefinition Widget(string id = "calc") =>
        new() { Id = id, X = 100, Y = 100, Width = 240, Height = 330 };

    private static C.DashboardDefinition Board(params C.WidgetDefinition[] widgets) =>
        new() { BaseDirectory = Path.GetTempPath(), Canvas = new() { Width = 600, Height = 600 }, Widgets = [..widgets] };

    private static void Check(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }

    private static void Keys(CalculatorState state, params string[] keys)
    {
        foreach (var key in keys)
            Check(state.Press(key), $"Calculator key {key} was unexpectedly rejected.");
    }

    public static void ArithmeticAndErrorRecovery()
    {
        var state = new CalculatorState();
        Check(state.Readout == "0", "Calculator starts at zero.");
        Keys(state, "1", "2", "+", "3", "×", "4", "=");
        Check(state.Readout == "60", "Chain arithmetic executes left-to-right without precedence.");
        Keys(state, "=");
        Check(state.Readout == "240", "Repeated equals reuses the final operator and right operand.");

        Keys(state, "C", "9", "÷", "3", "=");
        Check(state.Readout == "3", "Division must evaluate correctly.");
        Keys(state, "+", "−", "2", "=");
        Check(state.Readout == "1", "The second operator must replace the pending addition with subtraction.");
        Keys(state, "C", "3", "−", "+", "2", "=");
        Check(state.Readout == "5", "The second operator must also replace pending subtraction with addition.");
        Keys(state, "C", "8", "−", "2", "=");
        Check(state.Readout == "6", "Subtraction must evaluate correctly.");

        Keys(state, "C", "1", ".", "2", "5", "+", "0", ".", "7", "5", "=");
        Check(state.Readout == "2", "Decimals must use exact locale-invariant entry syntax.");
        Keys(state, "C", "5", "±");
        Check(state.Readout == "-5", "Sign reversal must affect current operand.");
        Keys(state, "±", "⌫");
        Check(state.Readout == "0", "Sign reversal and backspace must remain recoverable.");

        Keys(state, "C", "1", "2", "3", "⌫");
        Check(state.Readout == "12", "Backspace removes one entered digit.");
        Keys(state, "+", "7", "CE", "8", "=");
        Check(state.Readout == "20", "CE clears only the pending current entry.");
        Keys(state, "C", "3", "÷", "0", "=");
        Check(state.Readout == "Error", "Division by zero must display an error.");
        Check(!state.Press("9") && state.Readout == "Error", "Error blocks normal entry.");
        Keys(state, "CE", "7");
        Check(state.Readout == "7", "CE recovers from an error.");
        Keys(state, "C");
        Check(state.Readout == "0", "All-clear restores the default state.");

        state.Press("Paste", () => "1e308");
        Keys(state, "×", "2", "=");
        Check(state.Readout == "Error", "Finite operands producing infinity must be trapped.");
        Keys(state, "C");
        for (var i = 0; i < 16; i++) Keys(state, "9");
        Check(!state.Press("9"), "Manual entry must remain bounded.");
    }

    public static void ClipboardAndIsolation()
    {
        var first = new CalculatorState();
        var second = new CalculatorState();
        Check(!first.Press("Paste", () => "not a number") && first.Readout == "0",
            "Invalid Paste must preserve state.");
        Check(!first.Press("Paste", () => "1,234") && first.Readout == "0",
            "Ambiguous thousands-separated Paste must be rejected.");
        Check(!first.Press("Paste", () => "NaN") &&
              !first.Press("Paste", () => "Infinity") &&
              !first.Press("Paste", () => "1e9999"),
            "Nonfinite literals must be rejected.");
        Check(!first.Press("Paste", () => throw new InvalidOperationException("busy")) &&
              first.Readout == "0", "Busy clipboard read must not change state.");

        Keys(first, "2", "+");
        Check(first.Press("Paste", () => "-3.5"), "Signed decimal Paste must be accepted.");
        Keys(first, "=");
        Check(first.Readout == "-1.5" && second.Readout == "0",
            "Pending operator remains active while independent widget state stays isolated.");

        var clipboard = "";
        Check(!first.Press("Copy", clipboardWrite: text => clipboard = text) &&
              clipboard == "-1.5", "Copy produces undecorated invariant numeric text.");
        Keys(first, "C");
        Check(first.Press("Paste", () => clipboard) && first.Readout == "-1.5",
            "Copy and Paste must round-trip a negative decimal value.");
        Check(!first.Press("Copy", clipboardWrite: _ => throw new InvalidOperationException("busy")) &&
              first.Readout == "-1.5", "Busy clipboard write leaves calculation intact.");

        Keys(first, "C", "3", "÷", "0", "=");
        Check(!first.Press("Copy", clipboardWrite: _ => throw new Exception("Must not access clipboard")),
            "Copy in Error state must never touch clipboard.");
        Check(!first.Press("Paste", () => "4") && first.Readout == "Error",
            "Paste cannot silently clear the error state.");
        Check(new CalculatorState().Readout == "0", "A new session/reload begins with a fresh state.");
    }

    public static void InteractionAndGeometry()
    {
        var calculator = Widget();
        var board = Board(calculator);
        var map = new WidgetActionInteractionMap(board);
        var keys = CalculatorGeometry.Keys(calculator);
        Check(keys.Count == 22 && map.Rasterize().Count > 0 && !map.IsEmpty,
            "Calculator keys must create active native regions without an Action.");
        Check(CalculatorGeometry.DisplayCaption("⌫") == "BS" &&
              CalculatorGeometry.DisplayCaption("9") == "9" &&
              keys.Any(key => key.Caption == "⌫"),
            "Backspace keeps its native operation while rendering a font-safe ASCII caption.");
        var keyByCaption = keys.ToDictionary(key => key.Caption);
        var equals = keyByCaption["="].Bounds;
        var sign = keyByCaption["±"].Bounds;
        var copy = keyByCaption["Copy"].Bounds;
        var paste = keyByCaption["Paste"].Bounds;
        var dot = keyByCaption["."].Bounds;
        var tolerance = 0.01f;
        Check(Math.Abs(equals.Top - sign.Top) < tolerance &&
              Math.Abs(equals.Bottom - copy.Bottom) < tolerance &&
              Math.Abs(equals.Width - sign.Width) < tolerance,
            "Equals must span both bottom rows without changing the standard key width.");
        Check(Math.Abs(copy.Top - paste.Top) < tolerance &&
              Math.Abs(copy.Bottom - paste.Bottom) < tolerance &&
              Math.Abs(copy.Width - paste.Width) < tolerance &&
              Math.Abs(copy.Left - sign.Left) < tolerance &&
              Math.Abs(paste.Right - dot.Right) < tolerance &&
              copy.Right < paste.Left,
            "Copy and Paste must divide the lower-left three columns equally, with a real gap.");
        Check(map.Hit(new Point((int)(equals.Left + equals.Width / 2),
                                (int)(equals.Bottom - 3)))?.CalculatorKey == "=",
            "The lower half of the vertically merged Equals key must remain clickable.");
        for (var i = 0; i < keys.Count; i++)
            for (var j = i + 1; j < keys.Count; j++)
                Check(RectangleF.Intersect(keys[i].Bounds, keys[j].Bounds).IsEmpty,
                    "Calculator key regions must not overlap after merging the bottom rows.");
        foreach (var key in keys)
        {
            var sample = new Point((int)(key.Bounds.Left + key.Bounds.Width / 2),
                                   (int)(key.Bounds.Top + key.Bounds.Height / 2));
            Check(map.Hit(sample)?.CalculatorKey == key.Caption,
                "Hit-testing must match the drawn key and its label.");
        }
        var readout = CalculatorGeometry.Readout(calculator);
        Check(map.Hit(new Point((int)readout.Left + 8, (int)readout.Top + 8)) is null,
            "Readout must be click-through when Action is inactive.");

        calculator.Action = new() { Activation = C.WidgetActionDefinition.SingleClick, Target = "shell.exe" };
        map = new WidgetActionInteractionMap(board);
        var centerKey = keys.First(key => key.Caption == "5");
        var centerPixel = new Point((int)(centerKey.Bounds.Left + centerKey.Bounds.Width / 2),
                                    (int)(centerKey.Bounds.Top + centerKey.Bounds.Height / 2));
        Check(map.Hit(centerPixel)?.CalculatorKey == "5",
            "Calculator key must take priority over the inherited panel Action.");
        Check(map.Hit(new Point((int)readout.Left + 8, (int)readout.Top + 8)) is
              { CalculatorKey: null, Target: "shell.exe" }, "Readout Action must remain reachable.");

        calculator.Rotation = 90;
        map = new WidgetActionInteractionMap(board);
        var cx = calculator.X + calculator.Width / 2f;
        var cy = calculator.Y + calculator.Height / 2f;
        var px = centerKey.Bounds.Left + centerKey.Bounds.Width / 2f;
        var py = centerKey.Bounds.Top + centerKey.Bounds.Height / 2f;
        var rotated = new Point((int)(cx - (py - cy)), (int)(cy + (px - cx)));
        Check(map.Hit(rotated)?.CalculatorKey == "5",
            "Rotated key hit-testing must use the whole widget's rotation pivot.");

        var raster = map.Rasterize();
        Check(raster.Any(rect => rect.Contains(rotated)),
            "Native input region must contain all rotated interactive key pixels.");
        var gesture = new WidgetActionGesture(500, new Size(6, 6), new Size(8, 8));
        gesture.Down("calc|key:5", 1, rotated, 100);
        Check(gesture.Up("calc|key:5", 1, rotated, true, true), "Key first click activates.");
        gesture.Down("calc|key:5", 1, rotated, 200);
        Check(gesture.Up("calc|key:5", 1, rotated, true, true),
            "Two rapid key clicks must each activate, unlike single-click Shell Actions.");
        gesture.Down("calc|key:5", 1, rotated, 300);
        Check(gesture.Move(new Point(rotated.X + 30, rotated.Y)) &&
              !gesture.Up("calc|key:5", 1, rotated, true, true),
            "Cancelled native drag must not enter a calculator digit.");
        gesture.Down("calc|key:5", 2, rotated, 400);
        Check(!gesture.Up("calc|key:6", 2, rotated, true, true),
            "Release over another key must cancel activation.");
    }

    public static void PersistenceAndEditorProperties()
    {
        var calculator = Widget();
        var board = Board(calculator);
        var json = DashboardJson.Serialize(board, indented: false);
        Check(json.Contains("\"type\":\"calculator\"", StringComparison.Ordinal) &&
              !json.Contains("readout", StringComparison.OrdinalIgnoreCase),
            "Calculator type and appearance persist, but Runtime calculation state does not.");
        var parsed = C.DashboardDefinition.Parse(json, Path.Combine(Path.GetTempPath(), "calculator.json"));
        Check(parsed.Widgets.Single() is C.CalculatorWidgetDefinition copy &&
              copy.FontFamily == "Roboto" &&
              copy.DisplayFillColor == "#00000000" && copy.DisplayBorderWidth == 0f &&
              copy.DisplayCornerRadius == 0f && copy.ButtonCornerRadius == 0f &&
              DashboardJson.Serialize(parsed, indented: false) == json,
            "Fresh Calculator save/reload must preserve all appearance defaults and serialization.");

        calculator.ButtonCornerRadius = 7f;
        calculator.DisplayFillColor = "#80FF0088";
        calculator.DisplayBorderWidth = 2f;
        calculator.DisplayBorderColor = "#FF00FF00";
        calculator.DisplayCornerRadius = 4f;
        var customized = C.DashboardDefinition.Parse(
            DashboardJson.Serialize(board, indented: false), Path.Combine(Path.GetTempPath(), "custom-calculator.json"));
        Check(customized.Widgets.Single() is C.CalculatorWidgetDefinition selected &&
              selected.ButtonCornerRadius == 7f &&
              selected.DisplayFillColor == "#80FF0088" &&
              selected.DisplayBorderWidth == 2f &&
              selected.DisplayBorderColor == "#FF00FF00" &&
              selected.DisplayCornerRadius == 4f,
            "New button/readout appearance fields must survive persisted save and reload.");

        var legacy = Board(new C.ImageWidgetDefinition { Id = "existing", Width = 24, Height = 24 });
        var old = JsonNode.Parse(DashboardJson.Serialize(legacy, indented: false))!.AsObject();
        var previousWidget = old["widgets"]!.ToJsonString();
        old["schemaVersion"] = DashboardSchemaMigration.WidgetActionRunAsSchemaVersion;
        Check(DashboardSchemaMigration.UpgradeToCurrent(old) &&
              old["schemaVersion"]!.GetValue<int>() == DashboardSchemaMigration.CurrentSchemaVersion,
            "Schema 33 must migrate through Calculator schema 34 to appearance schema 35.");
        Check(old["widgets"]!.ToJsonString() == previousWidget,
            "Migration must preserve every existing widget field exactly.");
        var migrated = old.ToJsonString();
        Check(!DashboardSchemaMigration.UpgradeToCurrent(old) && old.ToJsonString() == migrated,
            "Repeated Calculator schema migration must be an exact no-op.");

        // A genuine schema-34 Calculator lacks all five newly introduced fields.
        // Migration must not edit any existing widget field.
        var schema34 = JsonNode.Parse(json)!.AsObject();
        schema34["schemaVersion"] = DashboardSchemaMigration.CalculatorSchemaVersion;
        var previousCalculator = schema34["widgets"]!.AsArray()[0]!.AsObject();
        foreach (var field in new[]
        {
            "buttonCornerRadius", "displayFillColor", "displayBorderWidth",
            "displayBorderColor", "displayCornerRadius"
        })
            previousCalculator.Remove(field);
        var beforeAppearanceMigration = previousCalculator.ToJsonString();
        Check(DashboardSchemaMigration.UpgradeToCurrent(schema34) &&
              schema34["schemaVersion"]!.GetValue<int>() == DashboardSchemaMigration.CalculatorAppearanceSchemaVersion &&
              previousCalculator.ToJsonString() == beforeAppearanceMigration,
            "Schema 34 must migrate to 35 without rewriting existing Calculator appearance.");
        var upgraded = C.DashboardDefinition.Parse(schema34.ToJsonString(), Path.Combine(Path.GetTempPath(), "old-calculator.json"));
        Check(upgraded.Widgets.Single() is C.CalculatorWidgetDefinition upgradedCalculator &&
              upgradedCalculator.ButtonCornerRadius == 0f &&
              upgradedCalculator.DisplayFillColor == "#00000000" &&
              upgradedCalculator.DisplayBorderWidth == 0f &&
              upgradedCalculator.DisplayBorderColor == "#FF9BA8C0" &&
              upgradedCalculator.DisplayCornerRadius == 0f,
            "Schema-34 Calculator must acquire all new appearance defaults.");
        var migratedAppearanceJson = schema34.ToJsonString();
        Check(!DashboardSchemaMigration.UpgradeToCurrent(schema34) &&
              schema34.ToJsonString() == migratedAppearanceJson,
            "Schema-35 repeated migration must be an exact no-op.");
        var stableJson = DashboardJson.Serialize(upgraded, indented: false);
        Check(DashboardJson.Serialize(C.DashboardDefinition.Parse(stableJson, "idempotent.json"), indented: false) == stableJson,
            "Schema-35 Calculator parse/save must be idempotent.");

        var sections = new EditorPropertyView(calculator, section: EditorPropertySection.Calculator).GetProperties();
        Check(sections.Cast<PropertyDescriptor>().Select(property => property.Name)
                  .SequenceEqual(["Font", "Buttons", "Display"]),
            "Calculator PropertyGrid groups must appear Font, Buttons, then Display.");
        var buttonRows = TypeDescriptor.GetProperties(sections["Buttons"]!.GetValue(null)!);
        var displayRows = TypeDescriptor.GetProperties(sections["Display"]!.GetValue(null)!);
        Check(buttonRows.Cast<PropertyDescriptor>().Select(property => property.DisplayName)
                  .SequenceEqual(["Text Color", "Fill Color", "Border Width", "Border Color", "Corner Radius"]) &&
              displayRows.Cast<PropertyDescriptor>().Select(property => property.DisplayName)
                  .SequenceEqual(["Text Color", "Fill Color", "Border Width", "Border Color", "Corner Radius"]),
            "Both Calculator surface groups must use exactly the five approved rows and ordering.");
        Check(TypeDescriptor.GetProperties(sections["Font"]!.GetValue(null)!).Count == 1,
            "Calculator Font group must retain just Family.");

        // Disabled dependent editors must refuse writes without clearing stored values.
        calculator.ButtonBorderWidth = 0f;
        calculator.DisplayBorderWidth = 0f;
        Check(buttonRows[nameof(C.CalculatorWidgetDefinition.ButtonBorderColor)]!.IsReadOnly && buttonRows[nameof(C.CalculatorWidgetDefinition.ButtonCornerRadius)]!.IsReadOnly &&
              displayRows[nameof(C.CalculatorWidgetDefinition.DisplayBorderColor)]!.IsReadOnly && displayRows[nameof(C.CalculatorWidgetDefinition.DisplayCornerRadius)]!.IsReadOnly,
            "Border Color and Corner Radius must disable independently at zero width.");
        var oldButtonBorderColor = calculator.ButtonBorderColor;
        var oldDisplayBorderColor = calculator.DisplayBorderColor;
        buttonRows[nameof(C.CalculatorWidgetDefinition.ButtonBorderColor)]!.SetValue(null, "#FFFF0000");
        buttonRows[nameof(C.CalculatorWidgetDefinition.ButtonCornerRadius)]!.SetValue(null, 99f);
        displayRows[nameof(C.CalculatorWidgetDefinition.DisplayBorderColor)]!.SetValue(null, "#FFFF0000");
        displayRows[nameof(C.CalculatorWidgetDefinition.DisplayCornerRadius)]!.SetValue(null, 99f);
        Check(calculator.ButtonBorderColor == oldButtonBorderColor && calculator.ButtonCornerRadius == 7f &&
              calculator.DisplayBorderColor == oldDisplayBorderColor && calculator.DisplayCornerRadius == 4f,
            "Disabled border settings must not be modified.");
        calculator.ButtonBorderWidth = 2f;
        calculator.DisplayBorderWidth = 3f;
        Check(!buttonRows[nameof(C.CalculatorWidgetDefinition.ButtonBorderColor)]!.IsReadOnly && !buttonRows[nameof(C.CalculatorWidgetDefinition.ButtonCornerRadius)]!.IsReadOnly &&
              !displayRows[nameof(C.CalculatorWidgetDefinition.DisplayBorderColor)]!.IsReadOnly && !displayRows[nameof(C.CalculatorWidgetDefinition.DisplayCornerRadius)]!.IsReadOnly,
            "Border Color and Corner Radius must re-enable when their own border width is positive.");
        buttonRows[nameof(C.CalculatorWidgetDefinition.ButtonCornerRadius)]!.SetValue(null, 8f);
        displayRows[nameof(C.CalculatorWidgetDefinition.DisplayCornerRadius)]!.SetValue(null, 6f);
        Check(calculator.ButtonCornerRadius == 8f && calculator.DisplayCornerRadius == 6f,
            "Re-enabled radii must be independently editable.");

        var general = new EditorPropertyView(calculator, section: EditorPropertySection.General).GetProperties();
        Check(general["Data Source"] is not null, "Universal Data Source group must remain visible.");
        var action = new EditorPropertyView(calculator, section: EditorPropertySection.Action).GetProperties();
        Check(action["Action"] is not null, "Inherited universal Action remains available.");
    }
}
