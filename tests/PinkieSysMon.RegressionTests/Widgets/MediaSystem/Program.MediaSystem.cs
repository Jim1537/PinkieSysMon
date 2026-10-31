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
    private static void MediaSystemStatePropertyHierarchy()
    {
        var mediaSystem = new WidgetDefinition
        {
            Type = WidgetTypeContract.MediaSystem,
            Id = "media-system-state-property-hierarchy",
            Width = 64f,
            Height = 64f,
            Source = MediaMetricContract.OutputSource
        };
        mediaSystem.EnsureStateVisualProfiles();
        var currentMediaSystem = (C.MediaSystemWidgetDefinition)CurrentWidget(mediaSystem);

        static string StateProperty(string key, StateVisualProfileField field) =>
            $"{StateVisualProfilePropertyDescriptor.PropertyPrefix}{key}:{field}";

        var speakersSourceTypeName = StateProperty(
            MediaMetricContract.EndpointSpeakers,
            StateVisualProfileField.SourceType);
        var speakersSourceName = StateProperty(
            MediaMetricContract.EndpointSpeakers,
            StateVisualProfileField.Source);

        var states = new EditorPropertyView(
            currentMediaSystem,
            section: EditorPropertySection.States).GetProperties();
        var speakersSourceType = GetNestedProperty(states, "Type: Speakers", speakersSourceTypeName)
            ?? throw new InvalidOperationException("Media System Speakers Source Type property is missing.");
        var sourceTypeOptions = speakersSourceType.Converter.GetStandardValues()
            ?.Cast<object>()
            .Select(value => Convert.ToString(value) ?? string.Empty)
            .ToArray() ?? [];
        Assert(sourceTypeOptions.Contains("Icon", StringComparer.OrdinalIgnoreCase) &&
               sourceTypeOptions.Contains("Image", StringComparer.OrdinalIgnoreCase) &&
               sourceTypeOptions.Contains("Value", StringComparer.OrdinalIgnoreCase),
            "Media System Source Type must offer icon, file, and value.");

        speakersSourceType.SetValue(null, "Value");
        currentMediaSystem.Profiles[MediaMetricContract.EndpointSpeakers].TextPresentation.OverflowMode = ValueOverflowContract.Clip;

        states = new EditorPropertyView(
            currentMediaSystem,
            section: EditorPropertySection.States).GetProperties();
        var speakersValueSource = GetNestedProperty(states, "Type: Speakers", speakersSourceName);
        Assert(GetNestedProperty(states, "Type: Speakers", speakersSourceTypeName) is not null &&
               speakersValueSource is { IsReadOnly: true } &&
               speakersValueSource.Description == "Not applicable for selected Source Type.",
            "Value-backed Media System state must keep Source visible but disabled.");

        var headphones = currentMediaSystem.Profiles[MediaMetricContract.EndpointHeadphones];
        headphones.ContentType = C.StateContentType.Image;
        headphones.Asset.SourceType = C.ImageAssetSourceType.File;
        headphones.Asset.Source = "headphones.gif";
        headphones.Asset.Fit = StateVisualProfileContract.FitCover;
        headphones.Asset.Loop = false;

        var image = new EditorPropertyView(
            currentMediaSystem,
            section: EditorPropertySection.Image).GetProperties();
        Assert(image["Type: Speakers"] is null &&
               image["Type: Headphones"] is PropertyGroupPropertyDescriptor &&
               image["Type: Line Level"] is PropertyGroupPropertyDescriptor,
            "Media System Image tab must include only icon/file-backed states.");
        Assert(GetNestedProperty(
                   image,
                   "Type: Headphones",
                   StateProperty(MediaMetricContract.EndpointHeadphones, StateVisualProfileField.Fit)) is { DisplayName: "Fit" } &&
               GetNestedProperty(
                   image,
                   "Type: Headphones",
                   StateProperty(MediaMetricContract.EndpointHeadphones, StateVisualProfileField.Loop)) is { DisplayName: "Loop" } &&
               GetNestedProperty(
                   image,
                   "Type: Headphones",
                   StateProperty(MediaMetricContract.EndpointHeadphones, StateVisualProfileField.Opacity)) is { DisplayName: "Opacity" },
            "File-backed Media System state must expose standard Fit, Loop, and Opacity image presentation.");
        Assert(GetNestedProperty(
                   image,
                   "Type: Line Level",
                   StateProperty(MediaMetricContract.EndpointLineLevel, StateVisualProfileField.Fit)) is { DisplayName: "Fit" } &&
               GetNestedProperty(
                   image,
                   "Type: Line Level",
                   StateProperty(MediaMetricContract.EndpointLineLevel, StateVisualProfileField.Color)) is { DisplayName: "Color" } &&
               GetNestedProperty(
                   image,
                   "Type: Line Level",
                   StateProperty(MediaMetricContract.EndpointLineLevel, StateVisualProfileField.Opacity)) is { DisplayName: "Opacity" } &&
               GetNestedProperty(
                   image,
                   "Type: Line Level",
                   StateProperty(MediaMetricContract.EndpointLineLevel, StateVisualProfileField.Loop)) is { IsReadOnly: true, Description: "Not applicable for selected Source." },
            "Icon-backed Media System state must keep Loop visible but disabled.");

        var text = new EditorPropertyView(
            currentMediaSystem,
            section: EditorPropertySection.Text).GetProperties();
        Assert(text.Count == 1 && text["Type: Speakers"] is PropertyGroupPropertyDescriptor,
            "Media System Text tab must expose only value-backed state profiles.");
        foreach (var group in new[] { "Foreground", "Font", "Alignment", "Outline", "Overflow" })
        {
            Assert(GetNestedProperty(text, "Type: Speakers", group) is PropertyGroupPropertyDescriptor,
                $"Media System value state Text presentation is missing '{group}'.");
        }

        var endpoints = new[]
        {
            new WindowsMediaTelemetrySource.MediaEndpointInfo("endpoint-1", "Speakers A"),
            new WindowsMediaTelemetrySource.MediaEndpointInfo("endpoint-2", "Headset B")
        };
        var overrides = new Dictionary<string, string>(StringComparer.Ordinal);
        var data = new EditorPropertyView(
            currentMediaSystem,
            mediaEndpoints: endpoints,
            endpointTypeOverrides: overrides,
            section: EditorPropertySection.Data).GetProperties();
        Assert(data["Endpoint Overrides"] is PropertyGroupPropertyDescriptor,
            "Media System Endpoint Overrides must be exposed on the Data tab.");
        Assert(GetNestedProperty(
                   data,
                   "Endpoint Overrides",
                   EndpointOverridePropertyDescriptor.PropertyPrefix + "endpoint-1") is { DisplayName: "Speakers A" } &&
               GetNestedProperty(
                   data,
                   "Endpoint Overrides",
                   EndpointOverridePropertyDescriptor.PropertyPrefix + "endpoint-2") is { DisplayName: "Headset B" },
            "Media System Data tab must contain endpoint override properties for discovered endpoints.");

    }

    private static void MediaSystemStatePresentationSchema()
    {
        var tempRoot = CreateTemporaryApplicationRoot("PinkieSysMon-media-system-state-schema-regression");
        try
        {
            var dashboardPath = Path.Combine(tempRoot, "dashboards", "media-system-state-schema.json");
            Directory.CreateDirectory(Path.GetDirectoryName(dashboardPath)!);

            var mediaSystem = new WidgetDefinition
            {
                Type = WidgetTypeContract.MediaSystem,
                Id = "media-system-state-schema",
                Width = 0f,
                Height = 0f,
                Source = MediaMetricContract.OutputSource
            };
            mediaSystem.EnsureStateVisualProfiles();
            Assert(mediaSystem.Profiles!.Values.All(profile =>
                       profile.Fit == StateVisualProfileContract.FitContain &&
                       profile.Loop == true &&
                       profile.Text is not null),
                "Current Media System profiles must provide standard Fit/Loop/Text presentation for every state.");

            foreach (var stateProfile in mediaSystem.Profiles.Values)
            {
                stateProfile.SourceType = StateVisualProfileContract.SourceValue;
                stateProfile.Source = string.Empty;
                stateProfile.Text!.OverflowMode = ValueOverflowContract.None;
            }

            var mediaSystemNode = new JsonObject
            {
                ["type"] = WidgetTypeContract.MediaSystem,
                ["id"] = mediaSystem.Id,
                ["width"] = 0f,
                ["height"] = 0f,
                ["source"] = MediaMetricContract.OutputSource,
                ["profiles"] = JsonSerializer.SerializeToNode(mediaSystem.Profiles)
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
                ["widgets"] = new JsonArray(mediaSystemNode)
            };

            var definition = DashboardDefinition.Parse(root.ToJsonString(), dashboardPath);
            var parsed = definition.Widgets.Single();
            Assert(StateVisualProfileContract.UsesOnlyValueSources(parsed),
                "Current schema must deserialize value-backed Media System profiles.");

            var missingText = JsonNode.Parse(root.ToJsonString()) as JsonObject
                ?? throw new InvalidOperationException("Could not clone Media System schema document.");
            var missingWidget = ((missingText["widgets"] as JsonArray)![0] as JsonObject)!;
            var missingProfiles = (missingWidget["profiles"] as JsonObject)!;
            ((missingProfiles[MediaMetricContract.EndpointSpeakers] as JsonObject)!).Remove("text");
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
                "Current Media System profiles must explicitly persist Text presentation for every state.");

            var obsolete = JsonNode.Parse(root.ToJsonString()) as JsonObject
                ?? throw new InvalidOperationException("Could not clone Media System obsolete-presentation document.");
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
                "Current schema must reject obsolete top-level Media System image/text presentation.");
        }
        finally
        {
            try { Directory.Delete(tempRoot, recursive: true); } catch { }
        }
    }

    private static void MediaSystemStateTextRendering()
    {
        var tempRoot = CreateTemporaryApplicationRoot("PinkieSysMon-media-system-state-text-rendering");
        try
        {
            var widget = new WidgetDefinition
            {
                Type = WidgetTypeContract.MediaSystem,
                Id = "media-system-state-text-rendering",
                X = 10f,
                Y = 10f,
                Width = 0f,
                Height = 0f,
                Source = MediaMetricContract.OutputSource,
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

            var speakersText = widget.Profiles[MediaMetricContract.EndpointSpeakers].Text!;
            speakersText.FontSize = 40f;
            speakersText.Align = "left";
            speakersText.VerticalAlign = "top";

            var lineText = widget.Profiles[MediaMetricContract.EndpointLineLevel].Text!;
            lineText.FontSize = 14f;
            lineText.Align = "left";
            lineText.VerticalAlign = "top";

            widget.Validate(tempRoot);

            using var typefaces = new PinkieSysMon.Widgets.TypefaceCache(tempRoot, new[] { CurrentWidget(widget) });
            using var context = new PinkieSysMon.Widgets.WidgetRenderContext(
                new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                {
                    [MediaMetricContract.OutputAvailable] = true,
                    [MediaMetricContract.OutputType] = MediaMetricContract.EndpointSpeakers
                },
                antialias: false,
                typefaces);

            var canonicalWidget = (PinkieSysMon.DashboardModel.MediaSystemWidgetDefinition)
                PropertyModelNormalizationMigration.MapLegacyWidget(widget);
            Assert(context.TryResolveMediaSystemVisualProfile(canonicalWidget, out var stateKey, out var activeProfile) &&
                   stateKey == MediaMetricContract.EndpointSpeakers &&
                   StateVisualProfileContract.IsValueSource(activeProfile.SourceType) &&
                   activeProfile.Text?.FontSize == 40f,
                "Media System state resolver must select the profile driven by the active endpoint type.");

            var speakersBounds = PinkieSysMon.Widgets.WidgetGeometry.GetLayoutBounds(CurrentWidget(widget), context);
            context.UpdateMetrics(
                new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                {
                    [MediaMetricContract.OutputAvailable] = true,
                    [MediaMetricContract.OutputType] = MediaMetricContract.EndpointLineLevel
                });
            var lineBounds = PinkieSysMon.Widgets.WidgetGeometry.GetLayoutBounds(CurrentWidget(widget), context);

            Assert(speakersBounds.Height > lineBounds.Height * 1.8f,
                $"Media System geometry did not follow active state Font Size: speakers={speakersBounds.Height}, line-level={lineBounds.Height}.");
            Assert(speakersBounds.Height < 80f && lineBounds.Height < 40f,
                "Media System geometry appears to be using obsolete widget-level Font Size instead of state Text profiles.");
        }
        finally
        {
            try { Directory.Delete(tempRoot, recursive: true); } catch { }
        }
    }

    private static void MediaSystemSchema15Migration()
    {
        var root = JsonNode.Parse(
            $$"""
            {
              "schemaVersion": 15,
              "canvas": { "orientation": 0, "backgroundColor": "#000000" },
              "widgets": [
                {
                  "type": "media.system",
                  "id": "media-system-schema15",
                  "width": 64,
                  "height": 64,
                  "source": "{{MediaMetricContract.OutputSource}}",
                  "profiles": {
                    "{{MediaMetricContract.EndpointSpeakers}}": {
                      "sourceType": "icon",
                      "source": "lucide:speaker",
                      "color": "#FF123456",
                      "opacity": 0.75
                    }
                  }
                }
              ]
            }
            """) as JsonObject ?? throw new InvalidOperationException("Could not create schema 15 Media System migration document.");

        Assert(DashboardSchemaMigration.UpgradeToCurrent(root),
            "Schema 15 dashboard must migrate Media System presentation to the current schema.");
        Assert(root["schemaVersion"]!.GetValue<int>() == DashboardSchemaMigration.CurrentSchemaVersion,
            "Media System migration must end at current schema 21.");

        var widget = ((root["widgets"] as JsonArray)?[0] as JsonObject)
            ?? throw new InvalidOperationException("Migrated Media System widget is missing.");
        Assert(widget["mediaSource"]?.GetValue<string>() == MediaMetricContract.OutputSource &&
               !widget.ContainsKey("source"),
            "Schema 15 migration must replace overloaded Media System source with canonical mediaSource.");

        var profiles = widget["profiles"] as JsonObject
            ?? throw new InvalidOperationException("Migrated Media System profiles are missing.");
        Assert(profiles.Count == StateVisualProfileContract.GetSpecs(WidgetTypeContract.MediaSystem).Count,
            "Media System migration must materialize every canonical state profile.");

        foreach (var spec in StateVisualProfileContract.GetSpecs(WidgetTypeContract.MediaSystem))
            AssertCanonicalMigratedStateProfile(profiles, spec.Key, StateVisualProfileContract.FitContain);

        var speakers = profiles[MediaMetricContract.EndpointSpeakers] as JsonObject
            ?? throw new InvalidOperationException("Migrated Media System Speakers profile is missing.");
        var asset = speakers["asset"] as JsonObject
            ?? throw new InvalidOperationException("Migrated Media System Speakers asset is missing.");
        Assert(speakers["contentType"]?.GetValue<string>() == C.StateContentType.Image &&
               asset["sourceType"]?.GetValue<string>() == C.ImageAssetSourceType.Icon &&
               asset["source"]?.GetValue<string>() == "lucide:speaker" &&
               speakers["color"]?.GetValue<string>() == "#FF123456" &&
               Math.Abs(speakers["opacity"]!.GetValue<float>() - 0.75f) < 0.001f,
            "Media System migration must preserve existing state source/color/opacity settings in canonical profile structure.");
    }

}
