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
    private const int LegacyBinarySchemaVersion = 10;

    public static bool UpgradeToCurrent(JsonObject root) =>
        UpgradeToPropertyModelNormalization(root);

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
