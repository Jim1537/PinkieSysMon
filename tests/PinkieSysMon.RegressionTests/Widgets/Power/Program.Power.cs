using System.ComponentModel;
using System.Drawing.Design;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows.Forms;
using PinkieSysMon;
using PinkieSysMon.Editor;
using C = PinkieSysMon.DashboardModel;

namespace PinkieSysMon.RegressionTests;

internal static partial class Program
{
    private static void PowerStatePropertyHierarchy()
    {
        var power = new WidgetDefinition
        {
            Type = WidgetTypeContract.Power,
            Id = "power-state-property-hierarchy",
            Source = PowerMetricContract.UpsSource,
            Width = 48f,
            Height = 48f
        };
        power.EnsureStateVisualProfiles();
        var currentPower = (C.PowerWidgetDefinition)CurrentWidget(power);

        static string StateProperty(string key, StateVisualProfileField field) =>
            $"{StateVisualProfilePropertyDescriptor.PropertyPrefix}{key}:{field}";

        var onlineSourceTypeName = StateProperty(
            PowerMetricContract.StateOnline,
            StateVisualProfileField.SourceType);
        var onlineSourceName = StateProperty(
            PowerMetricContract.StateOnline,
            StateVisualProfileField.Source);

        var states = new EditorPropertyView(
            currentPower,
            section: EditorPropertySection.States).GetProperties();
        var onlineSourceType = GetNestedProperty(states, "State: Online", onlineSourceTypeName)
            ?? throw new InvalidOperationException("Power Online Source Type property is missing.");
        var sourceTypeOptions = onlineSourceType.Converter.GetStandardValues()
            ?.Cast<object>()
            .Select(value => Convert.ToString(value) ?? string.Empty)
            .ToArray() ?? [];
        Assert(sourceTypeOptions.Contains("Icon", StringComparer.OrdinalIgnoreCase) &&
               sourceTypeOptions.Contains("Image", StringComparer.OrdinalIgnoreCase) &&
               sourceTypeOptions.Contains("Value", StringComparer.OrdinalIgnoreCase),
            "Power Source Type must offer icon, file, and value.");

        onlineSourceType.SetValue(null, "Value");
        currentPower.Profiles[PowerMetricContract.StateOnline].TextPresentation.OverflowMode = ValueOverflowContract.Clip;

        states = new EditorPropertyView(
            currentPower,
            section: EditorPropertySection.States).GetProperties();
        var onlineValueSource = GetNestedProperty(states, "State: Online", onlineSourceName);
        Assert(GetNestedProperty(states, "State: Online", onlineSourceTypeName) is not null &&
               onlineValueSource is { IsReadOnly: true } &&
               onlineValueSource.Description == "Not applicable for selected Source Type.",
            "Value-backed Power state must keep Source visible but disabled.");

        var charging = currentPower.Profiles[PowerMetricContract.StateCharging];
        charging.ContentType = C.StateContentType.Image;
        charging.Asset.SourceType = C.ImageAssetSourceType.File;
        charging.Asset.Source = "charging.gif";
        charging.Asset.Fit = StateVisualProfileContract.FitCover;
        charging.Asset.Loop = false;

        var image = new EditorPropertyView(
            currentPower,
            section: EditorPropertySection.Image).GetProperties();
        Assert(image["State: Online"] is null &&
               image["State: Charging"] is PropertyGroupPropertyDescriptor &&
               image["State: Low"] is PropertyGroupPropertyDescriptor,
            "Power Image tab must include only icon/file-backed states.");
        Assert(GetNestedProperty(
                   image,
                   "State: Charging",
                   StateProperty(PowerMetricContract.StateCharging, StateVisualProfileField.Fit)) is { DisplayName: "Fit" } &&
               GetNestedProperty(
                   image,
                   "State: Charging",
                   StateProperty(PowerMetricContract.StateCharging, StateVisualProfileField.Loop)) is { DisplayName: "Loop" } &&
               GetNestedProperty(
                   image,
                   "State: Charging",
                   StateProperty(PowerMetricContract.StateCharging, StateVisualProfileField.Opacity)) is { DisplayName: "Opacity" },
            "File-backed Power state must expose standard Fit, Loop, and Opacity image presentation.");
        Assert(GetNestedProperty(
                   image,
                   "State: Low",
                   StateProperty(PowerMetricContract.StateLow, StateVisualProfileField.Fit)) is { DisplayName: "Fit" } &&
               GetNestedProperty(
                   image,
                   "State: Low",
                   StateProperty(PowerMetricContract.StateLow, StateVisualProfileField.Color)) is { DisplayName: "Color" } &&
               GetNestedProperty(
                   image,
                   "State: Low",
                   StateProperty(PowerMetricContract.StateLow, StateVisualProfileField.Opacity)) is { DisplayName: "Opacity" } &&
               GetNestedProperty(
                   image,
                   "State: Low",
                   StateProperty(PowerMetricContract.StateLow, StateVisualProfileField.Loop)) is { IsReadOnly: true, Description: "Not applicable for selected Source." },
            "Icon-backed Power state must keep Loop visible but disabled.");

        var text = new EditorPropertyView(
            currentPower,
            section: EditorPropertySection.Text).GetProperties();
        Assert(text.Count == 1 && text["State: Online"] is PropertyGroupPropertyDescriptor,
            "Power Text tab must expose only value-backed state profiles.");
        foreach (var group in new[] { "Foreground", "Font", "Alignment", "Outline", "Overflow" })
        {
            Assert(GetNestedProperty(text, "State: Online", group) is PropertyGroupPropertyDescriptor,
                $"Power value state Text presentation is missing '{group}'.");
        }

    }

    private static void PowerStatePresentationSchema()
    {
        var tempRoot = CreateTemporaryApplicationRoot("PinkieSysMon-power-state-schema-regression");
        try
        {
            var dashboardPath = Path.Combine(tempRoot, "dashboards", "power-state-schema.json");
            var power = new WidgetDefinition
            {
                Type = WidgetTypeContract.Power,
                Id = "power-state-schema",
                Source = PowerMetricContract.UpsSource,
                Width = 0f,
                Height = 0f
            };
            power.EnsureStateVisualProfiles();

            Assert(power.Profiles!.Count == PowerMetricContract.States.Length &&
                   power.Profiles.Values.All(profile =>
                       profile.Fit == StateVisualProfileContract.FitContain &&
                       profile.Loop == true &&
                       profile.Text is not null),
                "Schema 14 Power profiles must provide standard Fit/Loop/Text presentation for every state.");

            foreach (var profile in power.Profiles.Values)
            {
                profile.SourceType = StateVisualProfileContract.SourceValue;
                profile.Source = string.Empty;
                profile.Text!.OverflowMode = ValueOverflowContract.None;
            }

            var powerNode = new JsonObject
            {
                ["type"] = WidgetTypeContract.Power,
                ["id"] = power.Id,
                ["width"] = 0f,
                ["height"] = 0f,
                ["source"] = PowerMetricContract.UpsSource,
                ["profiles"] = JsonSerializer.SerializeToNode(power.Profiles)
            };
            var root = new JsonObject
            {
                ["schemaVersion"] = DashboardSchemaMigration.CanvasImageLayerSchemaVersion,
                ["canvas"] = new JsonObject
                {
                    ["width"] = FrameGeometry.DefaultNativeWidth,
                    ["height"] = FrameGeometry.DefaultNativeHeight,
                    ["orientation"] = 0,
                    ["backgroundColor"] = "#000000"
                },
                ["widgets"] = new JsonArray(powerNode)
            };

            var definition = DashboardDefinition.Parse(root.ToJsonString(), dashboardPath);
            var parsed = definition.Widgets.Single();
            Assert(StateVisualProfileContract.UsesOnlyValueSources(parsed),
                "Schema 14 must deserialize value-backed Power profiles.");

            var missingText = JsonNode.Parse(root.ToJsonString()) as JsonObject
                ?? throw new InvalidOperationException("Could not clone Power schema document.");
            var missingWidget = ((missingText["widgets"] as JsonArray)![0] as JsonObject)!;
            var missingProfiles = (missingWidget["profiles"] as JsonObject)!;
            ((missingProfiles[PowerMetricContract.StateCritical] as JsonObject)!).Remove("text");
            var missingRejected = false;
            try
            {
                _ = DashboardDefinition.Parse(missingText.ToJsonString(), dashboardPath);
            }
            catch (InvalidDataException)
            {
                missingRejected = true;
            }
            Assert(missingRejected,
                "Schema 14 Power profiles must explicitly persist Text presentation for every state.");

            var obsolete = JsonNode.Parse(root.ToJsonString()) as JsonObject
                ?? throw new InvalidOperationException("Could not clone Power obsolete-presentation document.");
            var obsoleteWidget = ((obsolete["widgets"] as JsonArray)![0] as JsonObject)!;
            obsoleteWidget["fit"] = StateVisualProfileContract.FitContain;
            var obsoleteRejected = false;
            try
            {
                _ = DashboardDefinition.Parse(obsolete.ToJsonString(), dashboardPath);
            }
            catch (InvalidDataException)
            {
                obsoleteRejected = true;
            }
            Assert(obsoleteRejected,
                "Schema 14 must reject obsolete top-level Power image/text presentation.");
        }
        finally
        {
            try { Directory.Delete(tempRoot, recursive: true); } catch { }
        }
    }

    private static void PowerStateTextRendering()
    {
        var tempRoot = CreateTemporaryApplicationRoot("PinkieSysMon-power-state-text-rendering");
        try
        {
            var widget = new WidgetDefinition
            {
                Type = WidgetTypeContract.Power,
                Id = "power-state-text-rendering",
                Source = PowerMetricContract.UpsSource,
                X = 10f,
                Y = 10f,
                Width = 0f,
                Height = 0f,
                FontSize = 96f,
                Align = "right",
                VerticalAlign = "bottom",
                OverflowMode = ValueOverflowContract.Clip
            };
            widget.EnsureStateVisualProfiles();
            foreach (var stateProfile in widget.Profiles!.Values)
            {
                stateProfile.SourceType = StateVisualProfileContract.SourceValue;
                stateProfile.Source = string.Empty;
                stateProfile.Text!.OverflowMode = ValueOverflowContract.None;
            }

            var onlineText = widget.Profiles[PowerMetricContract.StateOnline].Text!;
            onlineText.FontSize = 40f;
            onlineText.Align = "left";
            onlineText.VerticalAlign = "top";

            var chargingText = widget.Profiles[PowerMetricContract.StateCharging].Text!;
            chargingText.FontSize = 14f;
            chargingText.Align = "left";
            chargingText.VerticalAlign = "top";

            widget.Validate(tempRoot);

            using var typefaces = new PinkieSysMon.Widgets.TypefaceCache(tempRoot, new[] { CurrentWidget(widget) });
            using var context = new PinkieSysMon.Widgets.WidgetRenderContext(
                new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                {
                    [PowerMetricContract.UpsStateMetric] = PowerMetricContract.StateOnline
                },
                antialias: false,
                typefaces);

            var canonicalWidget = (PinkieSysMon.DashboardModel.PowerWidgetDefinition)
                PropertyModelNormalizationMigration.MapLegacyWidget(widget);
            Assert(context.TryResolvePowerVisualProfile(canonicalWidget, out var stateKey, out var profile) &&
                   stateKey == PowerMetricContract.StateOnline &&
                   StateVisualProfileContract.IsValueSource(profile.SourceType) &&
                   profile.Text?.FontSize == 40f,
                "Power state resolver must select the profile driven by the semantic state metric.");

            var onlineBounds = PinkieSysMon.Widgets.WidgetGeometry.GetLayoutBounds(CurrentWidget(widget), context);
            context.UpdateMetrics(
                new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                {
                    [PowerMetricContract.UpsStateMetric] = PowerMetricContract.StateCharging
                });
            var chargingBounds = PinkieSysMon.Widgets.WidgetGeometry.GetLayoutBounds(CurrentWidget(widget), context);

            Assert(onlineBounds.Height > chargingBounds.Height * 1.8f,
                $"Power geometry did not follow active state Font Size: online={onlineBounds.Height}, charging={chargingBounds.Height}.");
            Assert(onlineBounds.Height < 80f && chargingBounds.Height < 40f,
                "Power geometry appears to be using obsolete widget-level Font Size instead of state Text profiles.");
        }
        finally
        {
            try { Directory.Delete(tempRoot, recursive: true); } catch { }
        }
    }

    private static void PowerSchema13Migration()
    {
        var root = JsonNode.Parse(
            $$"""
            {
              "schemaVersion": 13,
              "canvas": { "orientation": 0, "backgroundColor": "#000000" },
              "widgets": [
                {
                  "type": "power",
                  "id": "power-schema13",
                  "width": 48,
                  "height": 48,
                  "source": "{{PowerMetricContract.UpsSource}}",
                  "profiles": {
                    "{{PowerMetricContract.StateOnline}}": {
                      "sourceType": "icon",
                      "source": "lucide:battery-full",
                      "color": "#FF123456",
                      "opacity": 0.75
                    }
                  }
                }
              ]
            }
            """) as JsonObject ?? throw new InvalidOperationException("Could not create schema 13 Power migration document.");

        Assert(DashboardSchemaMigration.UpgradeToCurrent(root),
            "Schema 13 dashboard must migrate Power presentation to the current schema.");
        Assert(root["schemaVersion"]!.GetValue<int>() == DashboardSchemaMigration.CurrentSchemaVersion,
            "Power migration must end at current schema 21.");

        var widget = ((root["widgets"] as JsonArray)?[0] as JsonObject)
            ?? throw new InvalidOperationException("Migrated Power widget is missing.");
        Assert(widget["powerSource"]?.GetValue<string>() == PowerMetricContract.UpsSource &&
               !widget.ContainsKey("source"),
            "Schema 13 migration must replace overloaded Power source with canonical powerSource.");

        var profiles = widget["profiles"] as JsonObject
            ?? throw new InvalidOperationException("Migrated Power profiles are missing.");
        Assert(profiles.Count == PowerMetricContract.States.Length,
            "Power migration must materialize every canonical state profile.");

        foreach (var spec in StateVisualProfileContract.GetSpecs(WidgetTypeContract.Power))
            AssertCanonicalMigratedStateProfile(profiles, spec.Key, StateVisualProfileContract.FitContain);

        var online = profiles[PowerMetricContract.StateOnline] as JsonObject
            ?? throw new InvalidOperationException("Migrated Power Online profile is missing.");
        var asset = online["asset"] as JsonObject
            ?? throw new InvalidOperationException("Migrated Power Online asset is missing.");
        Assert(online["contentType"]?.GetValue<string>() == C.StateContentType.Image &&
               asset["sourceType"]?.GetValue<string>() == C.ImageAssetSourceType.Icon &&
               asset["source"]?.GetValue<string>() == "lucide:battery-full" &&
               online["color"]?.GetValue<string>() == "#FF123456" &&
               Math.Abs(online["opacity"]!.GetValue<float>() - 0.75f) < 0.001f,
            "Power migration must preserve existing state source/color/opacity settings in canonical profile structure.");
    }

}
