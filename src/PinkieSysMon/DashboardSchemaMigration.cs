using System.Text.Json;
using System.Text.Json.Nodes;

namespace PinkieSysMon;

internal static class DashboardSchemaMigration
{
    public const int BinaryStatePresentationSchemaVersion = 11;
    public const int LegacyPrimitiveRemovalSchemaVersion = 12;
    public const int LegacyGraphRemovalSchemaVersion = 13;
    public const int PowerStatePresentationSchemaVersion = 14;
    public const int MediaPlayerStatePresentationSchemaVersion = 15;
    public const int MediaSystemStatePresentationSchemaVersion = 16;
    public const int CanvasGeometrySchemaVersion = 17;
    public const int CanvasImageLayerSchemaVersion = 18;
    public const int PropertyModelNormalizationSchemaVersion = 19;
    public const int DateTimeComponentSchemaVersion = 20;
    public const int BinaryDateTimeComponentSchemaVersion = 21;
    public const int DateTimeComponentPrecisionSchemaVersion = 22;
    public const int GraphSchemaVersion = 23;
    public const int GraphReverseSchemaVersion = 24;
    public const int GraphSignalSchemaVersion = 25;
    public const int ShortcutSchemaVersion = 26;
    public const int CommonWidgetGeometrySchemaVersion = 27;
    public const int BarTrackSchemaVersion = 28;
    public const int WidgetPanelLayersSchemaVersion = 29;
    public const int CanvasPanelSchemaVersion = 30;
    public const int WidgetActionSchemaVersion = 31;
    public const int ShadowColorAlphaSchemaVersion = 32;
    public const int WidgetActionRunAsSchemaVersion = 33;
    public const int CurrentSchemaVersion = WidgetActionRunAsSchemaVersion;
    private const int LegacyBinarySchemaVersion = 10;

    public static bool UpgradeToCurrent(JsonObject root)
    {
        ArgumentNullException.ThrowIfNull(root);

        var schemaVersion = ReadSchemaVersion(root);
        RejectLegacyShortcut(root);
        if (schemaVersion < GraphSchemaVersion && root["widgets"] is JsonArray inputWidgets &&
            inputWidgets.OfType<JsonObject>().Any(widget =>
                widget["type"] is JsonValue typeNode && typeNode.TryGetValue<string>(out var type) &&
                string.Equals(type, "graph", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("Legacy graph widgets cannot be reinterpreted as the schema-23 Graph widget.");
        if (schemaVersion < GraphSignalSchemaVersion && root["widgets"] is JsonArray signalWidgets &&
            signalWidgets.OfType<JsonObject>().Any(widget =>
                widget["type"] is JsonValue signalType && signalType.TryGetValue<string>(out var type) &&
                string.Equals(type, "graph", StringComparison.OrdinalIgnoreCase) &&
                (widget.ContainsKey("interpolation") || widget.ContainsKey("filter"))))
            throw new InvalidDataException("Pre-25 Graph widgets cannot contain interpolation or filter fields.");
        if (schemaVersion == CurrentSchemaVersion)
            return false;

        if (schemaVersion is not (PropertyModelNormalizationSchemaVersion or
            DateTimeComponentSchemaVersion or BinaryDateTimeComponentSchemaVersion or DateTimeComponentPrecisionSchemaVersion or GraphSchemaVersion or GraphReverseSchemaVersion or GraphSignalSchemaVersion or ShortcutSchemaVersion or CommonWidgetGeometrySchemaVersion or BarTrackSchemaVersion or WidgetPanelLayersSchemaVersion or CanvasPanelSchemaVersion or WidgetActionSchemaVersion or ShadowColorAlphaSchemaVersion))
        {
            _ = UpgradeToPropertyModelNormalization(root);
            schemaVersion = ReadSchemaVersion(root);
        }

        if (schemaVersion == PropertyModelNormalizationSchemaVersion)
        {
            // No synthetic Component: preserve unavailable legacy DateTime readings.
            root["schemaVersion"] = DateTimeComponentSchemaVersion;
            schemaVersion = DateTimeComponentSchemaVersion;
        }

        if (schemaVersion == DateTimeComponentSchemaVersion)
        {
            // Schema 20 Binary had no Component: never manufacture one.
            root["schemaVersion"] = BinaryDateTimeComponentSchemaVersion;
            schemaVersion = BinaryDateTimeComponentSchemaVersion;
        }

        if (schemaVersion == BinaryDateTimeComponentSchemaVersion)
        {
            // A missing componentFormat means exact integer projection ("0").
            // Leave Binary's existing textual format untouched and add no fields.
            root["schemaVersion"] = DateTimeComponentPrecisionSchemaVersion;
            schemaVersion = DateTimeComponentPrecisionSchemaVersion;
        }

        if (schemaVersion == DateTimeComponentPrecisionSchemaVersion)
        {
            // Graph-free documents keep all content; no history is created or persisted.
            root["schemaVersion"] = GraphSchemaVersion;
            schemaVersion = GraphSchemaVersion;
        }

        if (schemaVersion == GraphSchemaVersion)
        {
            if (root["widgets"] is JsonArray graphWidgets)
            {
                var graphs = graphWidgets.OfType<JsonObject>().Where(widget =>
                    widget["type"] is JsonValue typeNode && typeNode.TryGetValue<string>(out var type) &&
                    string.Equals(type, global::PinkieSysMon.WidgetTypeContract.Graph, StringComparison.OrdinalIgnoreCase)).ToArray();
                if (graphs.Any(graph => graph.ContainsKey("reverse")))
                    throw new InvalidDataException("Schema-23 Graph widgets cannot contain a reverse field.");
                // Preserve the original right-to-left history direction; new Graphs default to false.
                foreach (var graph in graphs) graph["reverse"] = true;
            }
            root["schemaVersion"] = GraphReverseSchemaVersion;
            schemaVersion = GraphReverseSchemaVersion;
        }

        if (schemaVersion == GraphReverseSchemaVersion)
        {
            if (root["widgets"] is JsonArray widgets)
                foreach (var graph in widgets.OfType<JsonObject>().Where(widget =>
                    widget["type"] is JsonValue typeNode && typeNode.TryGetValue<string>(out var type) &&
                    string.Equals(type, global::PinkieSysMon.WidgetTypeContract.Graph, StringComparison.OrdinalIgnoreCase)))
                {
                    graph["interpolation"] = "Linear";
                    graph["filter"] = "None";
                }
            root["schemaVersion"] = GraphSignalSchemaVersion;
            schemaVersion = GraphSignalSchemaVersion;
        }

        if (schemaVersion == GraphSignalSchemaVersion)
        {
            // No existing widget changes meaning; do not synthesize Shortcut fields.
            root["schemaVersion"] = ShortcutSchemaVersion;
            schemaVersion = ShortcutSchemaVersion;
        }

        if (schemaVersion == ShortcutSchemaVersion)
        {
            // Omitted widget fields use the canonical zero-padding, full solid border defaults.
            root["schemaVersion"] = CommonWidgetGeometrySchemaVersion;
            schemaVersion = CommonWidgetGeometrySchemaVersion;
        }

        if (schemaVersion == CommonWidgetGeometrySchemaVersion)
        {
            // Bars without a track object use the transparent model default.
            root["schemaVersion"] = BarTrackSchemaVersion;
            schemaVersion = BarTrackSchemaVersion;
        }

        if (schemaVersion == BarTrackSchemaVersion)
        {
            UpgradeSchema28To29(root);
            root["schemaVersion"] = WidgetPanelLayersSchemaVersion;
            schemaVersion = WidgetPanelLayersSchemaVersion;
        }

        if (schemaVersion == WidgetPanelLayersSchemaVersion)
        {
            UpgradeSchema29To30(root);
            root["schemaVersion"] = CanvasPanelSchemaVersion;
            schemaVersion = CanvasPanelSchemaVersion;
        }

        if (schemaVersion == CanvasPanelSchemaVersion)
        {
            // Ordinary widgets acquire an inactive default without changing their visual meaning.
            root["schemaVersion"] = WidgetActionSchemaVersion;
            schemaVersion = WidgetActionSchemaVersion;
        }

        if (schemaVersion == WidgetActionSchemaVersion)
        {
            UpgradeSchema31To32(root);
            root["schemaVersion"] = ShadowColorAlphaSchemaVersion;
            schemaVersion = ShadowColorAlphaSchemaVersion;
        }

        if (schemaVersion == ShadowColorAlphaSchemaVersion)
        {
            // Schema 32 has no elevation setting; preserve the original Shell default verb.
            if (root["widgets"] is JsonArray actionWidgets && actionWidgets.OfType<JsonObject>().Any(widget =>
                widget["action"] is JsonObject action && action.ContainsKey("runAs")))
                throw new InvalidDataException("Schema-32 Action cannot contain a Run as field.");
            root["schemaVersion"] = WidgetActionRunAsSchemaVersion;
            schemaVersion = WidgetActionRunAsSchemaVersion;
        }

        if (schemaVersion != CurrentSchemaVersion)
            throw new InvalidDataException(
                $"No dashboard migration path from schemaVersion {schemaVersion} to {CurrentSchemaVersion}.");
        return true;
    }


    internal static void RejectLegacyShortcut(JsonObject root)
    {
        if (root["widgets"] is JsonArray widgets && widgets.OfType<JsonObject>().Any(widget =>
            widget["type"] is JsonValue typeNode && typeNode.TryGetValue<string>(out var type) &&
            string.Equals(type, "shortcut", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("Legacy Shortcut widgets are unsupported by the current dashboard schema. Remove or replace them using an older application, then reopen the unchanged source dashboard.");
    }

    // Schema 31 stores two shadow alpha factors; schema 32 folds them into Color.
    // Migrate disabled settings too, so re-enabling the shadow preserves its appearance.
    private static void UpgradeSchema31To32(JsonObject root)
    {
        if (root["canvas"] is JsonObject canvas)
            FoldShadowAlpha(canvas);
        if (root["widgets"] is JsonArray widgets)
            foreach (var widget in widgets.OfType<JsonObject>())
                FoldShadowAlpha(widget);
    }

    private static void FoldShadowAlpha(JsonObject owner)
    {
        var opacity = owner["shadowOpacity"] is JsonNode value
            ? JsonSerializer.Deserialize<float>(value.ToJsonString())
            : 0.5f;
        if (!float.IsFinite(opacity) || opacity is < 0f or > 1f)
            throw new InvalidDataException("Legacy shadowOpacity must be finite and between 0 and 1.");

        var color = owner["shadowColor"]?.GetValue<string>() ?? "#FF000000";
        owner["shadowColor"] = FoldAlpha(color, opacity);
        owner.Remove("shadowOpacity");
    }

    // Canvas's former color-under-image composition is intentionally retired.
    // All schema-29 images are selected by explicit source, not by container presence.
    private static void UpgradeSchema29To30(JsonObject root)
    {
        if (root["canvas"] is not JsonObject canvas)
            return;

        if ((canvas["backgroundImage"] is not null && canvas["backgroundImage"] is not JsonObject) ||
            (canvas["foregroundImage"] is not null && canvas["foregroundImage"] is not JsonObject))
            throw new InvalidDataException("Schema-29 Canvas image layer must be an object.");

        var backgroundColor = canvas["backgroundColor"]?.GetValue<string>() ?? "#000000";
        var background = UpgradeCanvasLayer(canvas["backgroundImage"] as JsonObject, backgroundColor, isBackground: true);
        var foreground = UpgradeCanvasLayer(canvas["foregroundImage"] as JsonObject, "#00000000", isBackground: false);

        canvas["panelBackground"] = background;
        canvas["panelForeground"] = foreground;
        canvas.Remove("backgroundColor");
        canvas.Remove("backgroundImage");
        canvas.Remove("foregroundImage");
    }

    private static JsonObject UpgradeCanvasLayer(JsonObject? legacy, string color, bool isBackground)
    {
        var panel = new JsonObject
        {
            ["mode"] = isBackground
                ? DashboardModel.WidgetPanelLayerMode.Color
                : DashboardModel.WidgetPanelLayerMode.None,
            ["color"] = color
        };
        if (legacy?["asset"] is not null && legacy["asset"] is not JsonObject)
            throw new InvalidDataException("Schema-29 Canvas image asset must be an object.");
        var asset = legacy?["asset"] as JsonObject;
        var kind = asset?["sourceType"]?.GetValue<string>() ?? DashboardModel.ImageAssetSourceType.File;
        if (!DashboardModel.ImageAssetSourceType.IsSupported(kind))
            throw new InvalidDataException("Schema-29 Canvas image layer has an unsupported source type.");
        var source = asset?["source"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(source))
            return panel;

        var isIcon = DashboardModel.ImageAssetSourceType.IsIcon(kind);
        panel["mode"] = isIcon
            ? DashboardModel.WidgetPanelLayerMode.Icon
            : DashboardModel.WidgetPanelLayerMode.Image;
        panel[isIcon ? "icon" : "image"] = asset!.DeepClone();
        if (isIcon)
        {
            if (legacy?["color"] is JsonNode tint)
                panel["iconColor"] = tint.DeepClone();
            if (legacy?["iconOpacity"] is JsonNode opacity)
                panel["iconOpacity"] = opacity.DeepClone();
        }
        return panel;
    }

    private static void UpgradeSchema28To29(JsonObject root)
    {
        if (root["widgets"] is JsonArray widgets)
        {
            foreach (var widget in widgets.OfType<JsonObject>())
            {
                var background = widget["backgroundColor"]?.GetValue<string>() ?? "#00000000";
                var backgroundColor = ColorParser.Parse(background);
                widget["panelBackground"] = new JsonObject
                {
                    ["mode"] = backgroundColor.Alpha == 0
                        ? DashboardModel.WidgetPanelLayerMode.None
                        : DashboardModel.WidgetPanelLayerMode.Color,
                    ["color"] = background
                };
                widget["panelForeground"] = new JsonObject
                {
                    ["mode"] = DashboardModel.WidgetPanelLayerMode.None
                };
                widget.Remove("backgroundColor");

                var type = widget["type"]?.GetValue<string>();
                if (WidgetTypeContract.Is(type, WidgetTypeContract.Image) ||
                    string.Equals(type, "shortcut", StringComparison.OrdinalIgnoreCase))
                {
                    MigrateIconOpacity(widget, widget["asset"] as JsonObject, "opacity");
                }

                if (widget["profiles"] is JsonObject profiles)
                {
                    foreach (var profile in profiles.Select(pair => pair.Value).OfType<JsonObject>())
                    {
                        var opacity = ReadLegacyOpacity(profile);
                        var isValue = DashboardModel.StateContentType.IsValue(
                            profile["contentType"]?.GetValue<string>());
                        if (isValue && opacity != 1f)
                        {
                            // A text state inheriting the default color with its own opacity
                            // cannot be converted without either losing inheritance or changing
                            // the rendered alpha. Reject this rare case instead of silently
                            // breaking the owner's inherited-color contract.
                            if (profile["color"] is null)
                                throw new InvalidDataException(
                                    $"Schema 28 widget '{widget["id"]}' has inherited state text color with separate Opacity. " +
                                    "Set an explicit RGBA state color in the original dashboard before upgrading.");
                            var effective = profile["color"]!.GetValue<string>();
                            profile["color"] = FoldAlpha(effective, opacity);
                            var text = profile["textPresentation"] as JsonObject;
                            if (text is not null)
                            {
                                var outline = text["outlineColor"]?.GetValue<string>() ?? "#FF000000";
                                text["outlineColor"] = FoldAlpha(outline, opacity);
                            }
                        }

                        if (isValue)
                            profile.Remove("opacity");
                        else
                            MigrateIconOpacity(profile, profile["asset"] as JsonObject, "opacity");
                    }
                }
            }
        }

        // Canvas layers have the same resource contract: only library icons retain
        // independent opacity; file image alpha is owned entirely by the file.
        if (root["canvas"] is JsonObject canvas)
        {
            MigrateIconOpacity(canvas["backgroundImage"] as JsonObject,
                (canvas["backgroundImage"] as JsonObject)?["asset"] as JsonObject, "opacity");
            MigrateIconOpacity(canvas["foregroundImage"] as JsonObject,
                (canvas["foregroundImage"] as JsonObject)?["asset"] as JsonObject, "opacity");
        }

        // The historical Graph plot-background resource is deliberately not promoted to
        // the widget Panel; that independent feature is deferred.
        if (root["widgets"] is JsonArray graphWidgets)
        {
            foreach (var widget in graphWidgets.OfType<JsonObject>().Where(widget =>
                WidgetTypeContract.Is(widget["type"]?.GetValue<string>(), WidgetTypeContract.Graph)))
            {
                var graphLayer = widget["image"] as JsonObject;
                MigrateIconOpacity(graphLayer, graphLayer?["asset"] as JsonObject, "opacity");
            }
        }
    }

    private static float ReadLegacyOpacity(JsonObject owner) =>
        owner["opacity"] is JsonValue value
            ? JsonSerializer.Deserialize<float>(value.ToJsonString())
            : 1f;

    private static void MigrateIconOpacity(JsonObject? owner, JsonObject? asset, string legacyProperty)
    {
        if (owner is null)
            return;
        if (DashboardModel.ImageAssetSourceType.IsIcon(asset?["sourceType"]?.GetValue<string>()) ||
            string.Equals(asset?["sourceType"]?.GetValue<string>(), "auto", StringComparison.OrdinalIgnoreCase))
        {
            if (owner[legacyProperty] is JsonNode old)
                owner["iconOpacity"] = old.DeepClone();
        }
        owner.Remove(legacyProperty);
    }

    internal static string FoldAlpha(string color, float opacity)
    {
        var parsed = ColorParser.Parse(color);
        var alpha = (byte)Math.Clamp((int)Math.Round(parsed.Alpha * opacity), 0, 255);
        return $"#{alpha:X2}{parsed.Red:X2}{parsed.Green:X2}{parsed.Blue:X2}";
    }

    public static bool UpgradeToPropertyModelNormalization(JsonObject root)
    {
        ArgumentNullException.ThrowIfNull(root);

        var schemaVersion = ReadSchemaVersion(root);
        if (schemaVersion == PropertyModelNormalizationSchemaVersion)
            return false;

        if (schemaVersion != CanvasImageLayerSchemaVersion)
        {
            _ = UpgradeToCanvasImageLayerSchema(root);
            schemaVersion = ReadSchemaVersion(root);
        }

        if (schemaVersion != CanvasImageLayerSchemaVersion)
        {
            throw new InvalidDataException(
                $"No dashboard migration path from schemaVersion {schemaVersion} to {PropertyModelNormalizationSchemaVersion}.");
        }

        PropertyModelNormalizationMigration.UpgradeSchema18To19(root);
        root["schemaVersion"] = PropertyModelNormalizationSchemaVersion;
        return true;
    }

    private static bool UpgradeToCanvasImageLayerSchema(JsonObject root)
    {
        var schemaVersion = ReadSchemaVersion(root);
        if (schemaVersion == CanvasImageLayerSchemaVersion)
            return false;

        var originalSchemaVersion = schemaVersion;
        var migrated = false;

        if (schemaVersion == LegacyBinarySchemaVersion)
        {
            UpgradeSchema10To11(root);
            schemaVersion = BinaryStatePresentationSchemaVersion;
            root["schemaVersion"] = schemaVersion;
            migrated = true;
        }

        if (schemaVersion == BinaryStatePresentationSchemaVersion)
        {
            UpgradeSchema11To12(root);
            schemaVersion = LegacyPrimitiveRemovalSchemaVersion;
            root["schemaVersion"] = schemaVersion;
            migrated = true;
        }

        if (schemaVersion == LegacyPrimitiveRemovalSchemaVersion)
        {
            UpgradeSchema12To13(root);
            schemaVersion = LegacyGraphRemovalSchemaVersion;
            root["schemaVersion"] = schemaVersion;
            migrated = true;
        }

        if (schemaVersion == LegacyGraphRemovalSchemaVersion)
        {
            UpgradeSchema13To14(root);
            schemaVersion = PowerStatePresentationSchemaVersion;
            root["schemaVersion"] = schemaVersion;
            migrated = true;
        }

        if (schemaVersion == PowerStatePresentationSchemaVersion)
        {
            UpgradeSchema14To15(root);
            schemaVersion = MediaPlayerStatePresentationSchemaVersion;
            root["schemaVersion"] = schemaVersion;
            migrated = true;
        }

        if (schemaVersion == MediaPlayerStatePresentationSchemaVersion)
        {
            UpgradeSchema15To16(root);
            schemaVersion = MediaSystemStatePresentationSchemaVersion;
            root["schemaVersion"] = schemaVersion;
            migrated = true;
        }

        if (schemaVersion == MediaSystemStatePresentationSchemaVersion)
        {
            UpgradeSchema16To17(root);
            schemaVersion = CanvasGeometrySchemaVersion;
            root["schemaVersion"] = schemaVersion;
            migrated = true;
        }

        if (schemaVersion == CanvasGeometrySchemaVersion)
        {
            UpgradeSchema17To18(root);
            schemaVersion = CanvasImageLayerSchemaVersion;
            root["schemaVersion"] = schemaVersion;
            migrated = true;
        }

        if (schemaVersion != CanvasImageLayerSchemaVersion)
        {
            throw new InvalidDataException(
                $"No dashboard migration path from schemaVersion {originalSchemaVersion} to {CanvasImageLayerSchemaVersion}.");
        }

        return migrated;
    }

    private static int ReadSchemaVersion(JsonObject root)
    {
        if (root["schemaVersion"] is not JsonValue schemaNode ||
            !schemaNode.TryGetValue<int>(out var schemaVersion))
        {
            throw new InvalidDataException("Dashboard schemaVersion is missing or invalid.");
        }

        return schemaVersion;
    }

    private static void UpgradeSchema10To11(JsonObject root)
    {
        if (root["widgets"] is not JsonArray widgets)
            return;

        foreach (var item in widgets)
        {
            if (item is JsonObject widget &&
                WidgetTypeContract.Is(widget["type"]?.GetValue<string>(), WidgetTypeContract.Binary))
            {
                UpgradeBinary(widget);
            }
        }
    }

    private static void UpgradeSchema11To12(JsonObject root)
    {
        if (root["widgets"] is not JsonArray widgets)
            return;

        foreach (var item in widgets)
        {
            if (item is not JsonObject widget)
                continue;

            var type = widget["type"]?.GetValue<string>();
            if (string.Equals(type, "grid", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(type, "rect", StringComparison.OrdinalIgnoreCase))
            {
                var id = widget["id"]?.GetValue<string>() ?? "<unknown>";
                throw new InvalidDataException(
                    $"Dashboard widget '{id}' uses removed legacy widget type '{type}'. Remove or replace it before migration.");
            }

            // These fields belonged only to the removed legacy primitives. They were global
            // WidgetDefinition properties, so older documents could retain inert copies on
            // otherwise supported widgets. Schema 12 removes them from the serialized model.
            widget.Remove("spacing");
            widget.Remove("fillColor");
        }
    }

    private static void UpgradeSchema12To13(JsonObject root)
    {
        if (root["widgets"] is not JsonArray widgets)
            return;

        foreach (var item in widgets)
        {
            if (item is not JsonObject widget)
                continue;

            var type = widget["type"]?.GetValue<string>();
            if (string.Equals(type, "graph", StringComparison.OrdinalIgnoreCase))
            {
                var id = widget["id"]?.GetValue<string>() ?? "<unknown>";
                throw new InvalidDataException(
                    $"Dashboard widget '{id}' uses removed legacy widget type '{type}'. Remove or replace it before migration.");
            }

            // Graph-specific fields used to live on the global WidgetDefinition model, so a schema 12
            // document can retain inert copies on otherwise supported widgets. Schema 13 removes them.
            widget.Remove("history");
            widget.Remove("sampleIntervalMs");
            widget.Remove("strokeWidth");
        }
    }

    private static void UpgradeSchema13To14(JsonObject root)
    {
        if (root["widgets"] is not JsonArray widgets)
            return;

        foreach (var item in widgets)
        {
            if (item is JsonObject widget &&
                WidgetTypeContract.Is(widget["type"]?.GetValue<string>(), WidgetTypeContract.Power))
            {
                UpgradePower(widget);
            }
        }
    }

    private static void UpgradeSchema14To15(JsonObject root)
    {
        if (root["widgets"] is not JsonArray widgets)
            return;

        foreach (var item in widgets)
        {
            if (item is JsonObject widget &&
                WidgetTypeContract.Is(widget["type"]?.GetValue<string>(), WidgetTypeContract.MediaPlayer))
            {
                UpgradeMediaPlayer(widget);
            }
        }
    }

    private static void UpgradeSchema16To17(JsonObject root)
    {
        var canvas = root["canvas"] as JsonObject;
        if (canvas is null)
        {
            canvas = new JsonObject();
            root["canvas"] = canvas;
        }

        // Schema 16 had no persisted native Canvas size. Preserve its effective behavior exactly.
        // Any same-named unknown fields in an old document were previously ignored, so migration
        // intentionally writes the established 1920x480 native geometry rather than promoting them.
        canvas["width"] = FrameGeometry.DefaultNativeWidth;
        canvas["height"] = FrameGeometry.DefaultNativeHeight;
    }

    private static void UpgradeSchema17To18(JsonObject root)
    {
        var canvas = root["canvas"] as JsonObject;
        if (canvas is null)
        {
            canvas = new JsonObject();
            root["canvas"] = canvas;
        }

        canvas["backgroundImage"] = CreateCanvasImageLayer(canvas["background"]);
        canvas["foregroundImage"] = CreateCanvasImageLayer(canvas["foreground"]);
        canvas.Remove("background");
        canvas.Remove("foreground");
    }

    private static JsonObject CreateCanvasImageLayer(JsonNode? legacySource)
    {
        string? source = null;
        if (legacySource is JsonValue value && value.TryGetValue<string>(out var text))
            source = text;

        return new JsonObject
        {
            ["sourceType"] = StateVisualProfileContract.SourceFile,
            ["source"] = source,
            ["fit"] = StateVisualProfileContract.FitStretch,
            ["opacity"] = 1f,
            ["loop"] = true,
            ["color"] = "#FFFFFFFF"
        };
    }

    private static void UpgradeSchema15To16(JsonObject root)
    {
        if (root["widgets"] is not JsonArray widgets)
            return;

        foreach (var item in widgets)
        {
            if (item is JsonObject widget &&
                WidgetTypeContract.Is(widget["type"]?.GetValue<string>(), WidgetTypeContract.MediaSystem))
            {
                UpgradeMediaSystem(widget);
            }
        }
    }

    private static void UpgradeMediaSystem(JsonObject widget)
    {
        var profiles = widget["profiles"] as JsonObject;
        if (profiles is null)
        {
            profiles = new JsonObject();
            widget["profiles"] = profiles;
        }

        foreach (var spec in StateVisualProfileContract.GetSpecs(WidgetTypeContract.MediaSystem))
        {
            var profile = profiles[spec.Key] as JsonObject;
            if (profile is null)
            {
                profile = JsonSerializer.SerializeToNode(spec.CreateDefault()) as JsonObject
                    ?? throw new InvalidDataException(
                        $"Could not create Media System profile '{spec.Key}' during dashboard migration.");
                profiles[spec.Key] = profile;
            }

            if (!profile.ContainsKey("fit"))
                profile["fit"] = StateVisualProfileContract.FitContain;
            if (!profile.ContainsKey("loop"))
                profile["loop"] = true;
            if (profile["text"] is not JsonObject)
                profile["text"] = JsonSerializer.SerializeToNode(new TextPresentationDefinition());
        }
    }

    private static void UpgradeMediaPlayer(JsonObject widget)
    {
        var profiles = widget["profiles"] as JsonObject;
        if (profiles is null)
        {
            profiles = new JsonObject();
            widget["profiles"] = profiles;
        }

        foreach (var spec in StateVisualProfileContract.GetSpecs(WidgetTypeContract.MediaPlayer))
        {
            var profile = profiles[spec.Key] as JsonObject;
            if (profile is null)
            {
                profile = JsonSerializer.SerializeToNode(spec.CreateDefault()) as JsonObject
                    ?? throw new InvalidDataException(
                        $"Could not create Media Player profile '{spec.Key}' during dashboard migration.");
                profiles[spec.Key] = profile;
            }

            if (!profile.ContainsKey("fit"))
                profile["fit"] = StateVisualProfileContract.FitContain;
            if (!profile.ContainsKey("loop"))
                profile["loop"] = true;
            if (profile["text"] is not JsonObject)
                profile["text"] = JsonSerializer.SerializeToNode(new TextPresentationDefinition());
        }
    }

    private static void UpgradePower(JsonObject widget)
    {
        var profiles = widget["profiles"] as JsonObject;
        if (profiles is null)
        {
            profiles = new JsonObject();
            widget["profiles"] = profiles;
        }

        foreach (var spec in StateVisualProfileContract.GetSpecs(WidgetTypeContract.Power))
        {
            var profile = profiles[spec.Key] as JsonObject;
            if (profile is null)
            {
                profile = JsonSerializer.SerializeToNode(spec.CreateDefault()) as JsonObject
                    ?? throw new InvalidDataException(
                        $"Could not create Power profile '{spec.Key}' during dashboard migration.");
                profiles[spec.Key] = profile;
            }

            if (!profile.ContainsKey("fit"))
                profile["fit"] = StateVisualProfileContract.FitContain;
            if (!profile.ContainsKey("loop"))
                profile["loop"] = true;
            if (profile["text"] is not JsonObject)
                profile["text"] = JsonSerializer.SerializeToNode(new TextPresentationDefinition());
        }
    }

    private static void UpgradeBinary(JsonObject widget)
    {
        var fit = ReadLegacyFit(widget);
        var loop = ReadLegacyLoop(widget);
        var text = ReadLegacyTextPresentation(widget);

        var profiles = widget["profiles"] as JsonObject;
        if (profiles is null)
        {
            profiles = new JsonObject();
            widget["profiles"] = profiles;
        }

        foreach (var spec in StateVisualProfileContract.GetSpecs(WidgetTypeContract.Binary))
        {
            var profile = profiles[spec.Key] as JsonObject;
            if (profile is null)
            {
                profile = JsonSerializer.SerializeToNode(spec.CreateDefault()) as JsonObject
                    ?? throw new InvalidDataException(
                        $"Could not create Binary profile '{spec.Key}' during dashboard migration.");
                profiles[spec.Key] = profile;
            }

            profile["fit"] = fit;
            profile["loop"] = loop;
            profile["text"] = JsonSerializer.SerializeToNode(text);
        }

        widget.Remove("fit");
        widget.Remove("loop");
        foreach (var propertyName in LegacyBinaryTextProperties)
            widget.Remove(propertyName);
    }

    private static readonly string[] LegacyBinaryTextProperties =
    [
        "fontFamily", "fontSize", "fontWeight", "italic", "align", "verticalAlign",
        "outlineWidth", "outlineColor", "overflowMode", "scrollSpeed", "bumpPauseMs"
    ];

    private static TextPresentationDefinition ReadLegacyTextPresentation(JsonObject widget)
    {
        try
        {
            var legacy = widget.Deserialize<WidgetDefinition>(new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            }) ?? throw new InvalidDataException("Schema 10 Binary text presentation could not be deserialized.");
            return TextPresentationDefinition.FromWidget(legacy);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("Schema 10 Binary text presentation is invalid and cannot be migrated.", ex);
        }
    }

    private static string ReadLegacyFit(JsonObject widget)
    {
        if (!widget.TryGetPropertyValue("fit", out var fitNode) || fitNode is null)
            return StateVisualProfileContract.FitStretch;

        if (fitNode is not JsonValue value || !value.TryGetValue<string>(out var fit) ||
            string.IsNullOrWhiteSpace(fit) ||
            !StateVisualProfileContract.SupportedFits.Contains(fit.Trim(), StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Schema 10 Binary Fit is invalid and cannot be migrated.");
        }

        return fit.Trim();
    }

    private static bool ReadLegacyLoop(JsonObject widget)
    {
        if (!widget.TryGetPropertyValue("loop", out var loopNode) || loopNode is null)
            return true;

        if (loopNode is JsonValue value && value.TryGetValue<bool>(out var loop))
            return loop;

        throw new InvalidDataException("Schema 10 Binary Loop is invalid and cannot be migrated.");
    }
}
