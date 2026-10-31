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
    private static void MediaPlayerStatePropertyHierarchy()
    {
        var mediaPlayer = new WidgetDefinition
        {
            Type = WidgetTypeContract.MediaPlayer,
            Id = "media-player-state-property-hierarchy",
            Width = 48f,
            Height = 48f
        };
        mediaPlayer.EnsureStateVisualProfiles();
        var currentMediaPlayer = (C.MediaPlayerWidgetDefinition)CurrentWidget(mediaPlayer);

        static string StateProperty(string key, StateVisualProfileField field) =>
            $"{StateVisualProfilePropertyDescriptor.PropertyPrefix}{key}:{field}";

        var playingSourceTypeName = StateProperty(
            MediaMetricContract.StatusPlaying,
            StateVisualProfileField.SourceType);
        var playingSourceName = StateProperty(
            MediaMetricContract.StatusPlaying,
            StateVisualProfileField.Source);

        var states = new EditorPropertyView(
            currentMediaPlayer,
            section: EditorPropertySection.States).GetProperties();
        var playingSourceType = GetNestedProperty(states, "State: Playing", playingSourceTypeName)
            ?? throw new InvalidOperationException("Media Player Playing Source Type property is missing.");
        var sourceTypeOptions = playingSourceType.Converter.GetStandardValues()
            ?.Cast<object>()
            .Select(value => Convert.ToString(value) ?? string.Empty)
            .ToArray() ?? [];
        Assert(sourceTypeOptions.Contains("Icon", StringComparer.OrdinalIgnoreCase) &&
               sourceTypeOptions.Contains("Image", StringComparer.OrdinalIgnoreCase) &&
               sourceTypeOptions.Contains("Value", StringComparer.OrdinalIgnoreCase),
            "Media Player Source Type must offer icon, file, and value.");

        playingSourceType.SetValue(null, "Value");
        currentMediaPlayer.Profiles[MediaMetricContract.StatusPlaying].TextPresentation.OverflowMode = ValueOverflowContract.Clip;

        states = new EditorPropertyView(
            currentMediaPlayer,
            section: EditorPropertySection.States).GetProperties();
        var playingValueSource = GetNestedProperty(states, "State: Playing", playingSourceName);
        Assert(GetNestedProperty(states, "State: Playing", playingSourceTypeName) is not null &&
               playingValueSource is { IsReadOnly: true } &&
               playingValueSource.Description == "Not applicable for selected Source Type.",
            "Value-backed Media Player state must keep Source visible but disabled.");

        var paused = currentMediaPlayer.Profiles[MediaMetricContract.StatusPaused];
        paused.ContentType = C.StateContentType.Image;
        paused.Asset.SourceType = C.ImageAssetSourceType.File;
        paused.Asset.Source = "paused.gif";
        paused.Asset.Fit = StateVisualProfileContract.FitCover;
        paused.Asset.Loop = false;

        var image = new EditorPropertyView(
            currentMediaPlayer,
            section: EditorPropertySection.Image).GetProperties();
        Assert(image["State: Playing"] is null &&
               image["State: Paused"] is PropertyGroupPropertyDescriptor &&
               image["State: Stopped"] is PropertyGroupPropertyDescriptor,
            "Media Player Image tab must include only icon/file-backed states.");
        Assert(GetNestedProperty(
                   image,
                   "State: Paused",
                   StateProperty(MediaMetricContract.StatusPaused, StateVisualProfileField.Fit)) is { DisplayName: "Fit" } &&
               GetNestedProperty(
                   image,
                   "State: Paused",
                   StateProperty(MediaMetricContract.StatusPaused, StateVisualProfileField.Loop)) is { DisplayName: "Loop" } &&
               GetNestedProperty(
                   image,
                   "State: Paused",
                   StateProperty(MediaMetricContract.StatusPaused, StateVisualProfileField.Opacity)) is { DisplayName: "Opacity" },
            "File-backed Media Player state must expose standard Fit, Loop, and Opacity image presentation.");
        Assert(GetNestedProperty(
                   image,
                   "State: Stopped",
                   StateProperty(MediaMetricContract.StatusStopped, StateVisualProfileField.Fit)) is { DisplayName: "Fit" } &&
               GetNestedProperty(
                   image,
                   "State: Stopped",
                   StateProperty(MediaMetricContract.StatusStopped, StateVisualProfileField.Color)) is { DisplayName: "Color" } &&
               GetNestedProperty(
                   image,
                   "State: Stopped",
                   StateProperty(MediaMetricContract.StatusStopped, StateVisualProfileField.Opacity)) is { DisplayName: "Opacity" } &&
               GetNestedProperty(
                   image,
                   "State: Stopped",
                   StateProperty(MediaMetricContract.StatusStopped, StateVisualProfileField.Loop)) is { IsReadOnly: true, Description: "Not applicable for selected Source." },
            "Icon-backed Media Player state must keep Loop visible but disabled.");

        var text = new EditorPropertyView(
            currentMediaPlayer,
            section: EditorPropertySection.Text).GetProperties();
        Assert(text.Count == 1 && text["State: Playing"] is PropertyGroupPropertyDescriptor,
            "Media Player Text tab must expose only value-backed state profiles.");
        foreach (var group in new[] { "Foreground", "Font", "Alignment", "Outline", "Overflow" })
        {
            Assert(GetNestedProperty(text, "State: Playing", group) is PropertyGroupPropertyDescriptor,
                $"Media Player value state Text presentation is missing '{group}'.");
        }

    }

    private static void MediaPlayerStatePresentationSchema()
    {
        var tempRoot = CreateTemporaryApplicationRoot("PinkieSysMon-media-player-state-schema-regression");
        try
        {
            var dashboardPath = Path.Combine(tempRoot, "dashboards", "media-player-state-schema.json");
            var mediaPlayer = new WidgetDefinition
            {
                Type = WidgetTypeContract.MediaPlayer,
                Id = "media-player-state-schema",
                Width = 0f,
                Height = 0f
            };
            mediaPlayer.EnsureStateVisualProfiles();

            Assert(mediaPlayer.Profiles!.Count == 4 &&
                   mediaPlayer.Profiles.Values.All(profile =>
                       profile.Fit == StateVisualProfileContract.FitContain &&
                       profile.Loop == true &&
                       profile.Text is not null),
                "Current Media Player profiles must provide standard Fit/Loop/Text presentation for every state.");

            foreach (var stateProfile in mediaPlayer.Profiles.Values)
            {
                stateProfile.SourceType = StateVisualProfileContract.SourceValue;
                stateProfile.Source = string.Empty;
                stateProfile.Text!.OverflowMode = ValueOverflowContract.None;
            }

            var mediaPlayerNode = new JsonObject
            {
                ["type"] = WidgetTypeContract.MediaPlayer,
                ["id"] = mediaPlayer.Id,
                ["width"] = 0f,
                ["height"] = 0f,
                ["profiles"] = JsonSerializer.SerializeToNode(mediaPlayer.Profiles)
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
                ["widgets"] = new JsonArray(mediaPlayerNode)
            };

            var definition = DashboardDefinition.Parse(root.ToJsonString(), dashboardPath);
            var parsed = definition.Widgets.Single();
            Assert(StateVisualProfileContract.UsesOnlyValueSources(parsed),
                "Current schema must deserialize value-backed Media Player profiles.");

            var missingText = JsonNode.Parse(root.ToJsonString()) as JsonObject
                ?? throw new InvalidOperationException("Could not clone Media Player schema document.");
            var missingWidget = ((missingText["widgets"] as JsonArray)![0] as JsonObject)!;
            var missingProfiles = (missingWidget["profiles"] as JsonObject)!;
            ((missingProfiles[MediaMetricContract.StatusPaused] as JsonObject)!).Remove("text");
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
                "Current Media Player profiles must explicitly persist Text presentation for every state.");

            var obsolete = JsonNode.Parse(root.ToJsonString()) as JsonObject
                ?? throw new InvalidOperationException("Could not clone Media Player obsolete-presentation document.");
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
                "Current schema must reject obsolete top-level Media Player image/text presentation.");
        }
        finally
        {
            try { Directory.Delete(tempRoot, recursive: true); } catch { }
        }
    }

    private static void MediaPlayerStateTextRendering()
    {
        var tempRoot = CreateTemporaryApplicationRoot("PinkieSysMon-media-player-state-text-rendering");
        try
        {
            var widget = new WidgetDefinition
            {
                Type = WidgetTypeContract.MediaPlayer,
                Id = "media-player-state-text-rendering",
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

            var playingText = widget.Profiles[MediaMetricContract.StatusPlaying].Text!;
            playingText.FontSize = 40f;
            playingText.Align = "left";
            playingText.VerticalAlign = "top";

            var pausedText = widget.Profiles[MediaMetricContract.StatusPaused].Text!;
            pausedText.FontSize = 14f;
            pausedText.Align = "left";
            pausedText.VerticalAlign = "top";

            widget.Validate(tempRoot);

            using var typefaces = new PinkieSysMon.Widgets.TypefaceCache(tempRoot, new[] { CurrentWidget(widget) });
            using var context = new PinkieSysMon.Widgets.WidgetRenderContext(
                new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                {
                    [MediaMetricContract.PlaybackStatus] = MediaMetricContract.StatusPlaying
                },
                antialias: false,
                typefaces);

            var canonicalWidget = (PinkieSysMon.DashboardModel.MediaPlayerWidgetDefinition)
                PropertyModelNormalizationMigration.MapLegacyWidget(widget);
            Assert(context.TryResolveMediaPlayerVisualProfile(canonicalWidget, out var stateKey, out var activeProfile) &&
                   stateKey == MediaMetricContract.StatusPlaying &&
                   StateVisualProfileContract.IsValueSource(activeProfile.SourceType) &&
                   activeProfile.Text?.FontSize == 40f,
                "Media Player state resolver must select the profile driven by Playback Status.");

            var playingBounds = PinkieSysMon.Widgets.WidgetGeometry.GetLayoutBounds(CurrentWidget(widget), context);
            context.UpdateMetrics(
                new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                {
                    [MediaMetricContract.PlaybackStatus] = MediaMetricContract.StatusPaused
                });
            var pausedBounds = PinkieSysMon.Widgets.WidgetGeometry.GetLayoutBounds(CurrentWidget(widget), context);

            Assert(playingBounds.Height > pausedBounds.Height * 1.8f,
                $"Media Player geometry did not follow active state Font Size: playing={playingBounds.Height}, paused={pausedBounds.Height}.");
            Assert(playingBounds.Height < 80f && pausedBounds.Height < 40f,
                "Media Player geometry appears to be using obsolete widget-level Font Size instead of state Text profiles.");
        }
        finally
        {
            try { Directory.Delete(tempRoot, recursive: true); } catch { }
        }
    }

    private static void MediaPlayerSchema14Migration()
    {
        var root = JsonNode.Parse(
            $$"""
            {
              "schemaVersion": 14,
              "canvas": { "orientation": 0, "backgroundColor": "#000000" },
              "widgets": [
                {
                  "type": "media.player",
                  "id": "media-player-schema14",
                  "width": 48,
                  "height": 48,
                  "source": "{{MediaMetricContract.PlaybackSource}}",
                  "profiles": {
                    "{{MediaMetricContract.StatusPlaying}}": {
                      "sourceType": "icon",
                      "source": "lucide:play",
                      "color": "#FF123456",
                      "opacity": 0.75
                    }
                  }
                }
              ]
            }
            """) as JsonObject ?? throw new InvalidOperationException("Could not create schema 14 Media Player migration document.");

        Assert(DashboardSchemaMigration.UpgradeToCurrent(root),
            "Schema 14 dashboard must migrate Media Player presentation to the current schema.");
        Assert(root["schemaVersion"]!.GetValue<int>() == DashboardSchemaMigration.CurrentSchemaVersion,
            "Media Player migration must end at current schema 21.");

        var widget = ((root["widgets"] as JsonArray)?[0] as JsonObject)
            ?? throw new InvalidOperationException("Migrated Media Player widget is missing.");
        Assert(!widget.ContainsKey("source"),
            "Canonical Media Player must not persist the obsolete playback Source field.");

        var profiles = widget["profiles"] as JsonObject
            ?? throw new InvalidOperationException("Migrated Media Player profiles are missing.");
        Assert(profiles.Count == 4,
            "Media Player migration must materialize every canonical state profile.");

        foreach (var spec in StateVisualProfileContract.GetSpecs(WidgetTypeContract.MediaPlayer))
            AssertCanonicalMigratedStateProfile(profiles, spec.Key, StateVisualProfileContract.FitContain);

        var playing = profiles[MediaMetricContract.StatusPlaying] as JsonObject
            ?? throw new InvalidOperationException("Migrated Media Player Playing profile is missing.");
        var asset = playing["asset"] as JsonObject
            ?? throw new InvalidOperationException("Migrated Media Player Playing asset is missing.");
        Assert(playing["contentType"]?.GetValue<string>() == C.StateContentType.Image &&
               asset["sourceType"]?.GetValue<string>() == C.ImageAssetSourceType.Icon &&
               asset["source"]?.GetValue<string>() == "lucide:play" &&
               playing["color"]?.GetValue<string>() == "#FF123456" &&
               Math.Abs(playing["opacity"]!.GetValue<float>() - 0.75f) < 0.001f,
            "Media Player migration must preserve existing state source/color/opacity settings in canonical profile structure.");
    }

}
