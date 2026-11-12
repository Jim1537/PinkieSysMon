using System.Text.Json.Nodes;

namespace PinkieSysMon.RegressionTests;

// Tests that exercise historical migrations must use a historically representable
// Canvas/widget Panel shape, not just a lowered schemaVersion on schema-30 JSON.
internal static class LegacyPanelMigrationFixture
{
    internal static JsonObject FromCurrent(JsonObject current, int schemaVersion)
    {
        if (schemaVersion < DashboardSchemaMigration.PropertyModelNormalizationSchemaVersion ||
            schemaVersion >= DashboardSchemaMigration.WidgetPanelLayersSchemaVersion)
            throw new ArgumentOutOfRangeException(nameof(schemaVersion));

        var legacy = (JsonObject)current.DeepClone();
        legacy["schemaVersion"] = schemaVersion;
        var canvas = legacy["canvas"] as JsonObject
            ?? throw new InvalidOperationException("Current fixture is missing Canvas.");
        var background = canvas["panelBackground"] as JsonObject
            ?? throw new InvalidOperationException("Current fixture has no Canvas Panel Background.");
        var foreground = canvas["panelForeground"] as JsonObject
            ?? throw new InvalidOperationException("Current fixture has no Canvas Panel Foreground.");
        if (background["mode"]?.GetValue<string>() != "Color" ||
            foreground["mode"]?.GetValue<string>() != "None")
            throw new InvalidOperationException(
                "This historical fixture supports a solid Canvas background without foreground artwork.");

        legacy["canvas"] = new JsonObject
        {
            ["width"] = canvas["width"]?.DeepClone(),
            ["height"] = canvas["height"]?.DeepClone(),
            ["orientation"] = canvas["orientation"]?.DeepClone(),
            ["backgroundColor"] = background["color"]?.DeepClone()
        };

        if (legacy["widgets"] is JsonArray widgets)
        {
            foreach (var widget in widgets.OfType<JsonObject>())
            {
                var panel = widget["panelBackground"] as JsonObject
                    ?? throw new InvalidOperationException("Current fixture has no widget Panel Background.");
                var foregroundPanel = widget["panelForeground"] as JsonObject
                    ?? throw new InvalidOperationException("Current fixture has no widget Panel Foreground.");
                if (panel["mode"]?.GetValue<string>() != "None" ||
                    foregroundPanel["mode"]?.GetValue<string>() != "None")
                    throw new InvalidOperationException(
                        "This historical fixture supports widgets with inactive Panel layers only.");
                // Historical schemas carried a separate Shadow Opacity. An
                // explicit unit multiplier preserves the canonical RGBA alpha
                // when the 31→32 migration folds both factors together.
                widget["shadowOpacity"] = 1f;
                widget["backgroundColor"] = panel["color"]?.DeepClone();
                widget.Remove("panelBackground");
                widget.Remove("panelForeground");
            }
        }

        return legacy;
    }
}
