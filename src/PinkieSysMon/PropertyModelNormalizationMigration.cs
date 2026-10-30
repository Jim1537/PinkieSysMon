using System.Text.Json;
using System.Text.Json.Nodes;
using CanonicalDashboard = PinkieSysMon.DashboardModel;

namespace PinkieSysMon;

internal static class PropertyModelNormalizationMigration
{
    private static readonly JsonSerializerOptions LegacyReadOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip
    };

    public static void UpgradeSchema18To19(JsonObject root)
    {
        ArgumentNullException.ThrowIfNull(root);

        if (root["schemaVersion"] is not JsonValue schemaNode ||
            !schemaNode.TryGetValue<int>(out var schemaVersion) ||
            schemaVersion != DashboardSchemaMigration.CanvasImageLayerSchemaVersion)
        {
            throw new InvalidDataException(
                $"Property-model normalization requires schemaVersion {DashboardSchemaMigration.CanvasImageLayerSchemaVersion} input.");
        }

        DashboardDefinition legacy;
        try
        {
            legacy = root.Deserialize<DashboardDefinition>(LegacyReadOptions)
                ?? throw new InvalidDataException("Schema 18 dashboard definition is empty.");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("Schema 18 dashboard cannot be read for property-model normalization.", ex);
        }

        var canonical = MapLegacyDefinition(legacy);

        var normalized = DashboardJson.SerializeToObjectUnchecked(canonical);
        root.Clear();
        foreach (var pair in normalized)
            root[pair.Key] = pair.Value?.DeepClone();
    }


    internal static CanonicalDashboard.DashboardDefinition MapLegacyDefinition(DashboardDefinition legacy)
    {
        ArgumentNullException.ThrowIfNull(legacy);

        var canonical = new CanonicalDashboard.DashboardDefinition
        {
            SchemaVersion = DashboardSchemaMigration.PropertyModelNormalizationSchemaVersion,
            Canvas = MapCanvas(legacy.Canvas),
            Widgets = legacy.Widgets.Select(MapLegacyWidgetWithStoredStateNormalization).ToList(),
            BaseDirectory = legacy.BaseDirectory
        };

        // Migration is intentionally path-independent. Full canonical validation, including
        // asset existence/resolution, runs only after the migrated JSON is deserialized with
        // the real dashboard path and BaseDirectory is known.
        return canonical;
    }

    internal static CanonicalDashboard.WidgetDefinition MapLegacyWidget(WidgetDefinition legacy)
    {
        ArgumentNullException.ThrowIfNull(legacy);
        return MapLegacyWidgetWithStoredStateNormalization(legacy);
    }

    private static CanonicalDashboard.WidgetDefinition MapLegacyWidgetWithStoredStateNormalization(WidgetDefinition legacy)
    {
        // Schema-18 parsing historically normalized compatible persisted state before validation.
        // Migration reads the legacy DTO directly, but it must not synthesize missing state profiles
        // before interpreting the state that was actually persisted. Doing so can turn a stored
        // value profile into a mixed value/image widget and incorrectly rewrite Overflow=None to Clip.
        NormalizeLegacyTextOverflowForMigration(legacy);
        _ = LiteralNumericContract.NormalizeStoredState(legacy);
        return MapWidget(legacy);
    }

    private static void NormalizeLegacyTextOverflowForMigration(WidgetDefinition legacy)
    {
        if (WidgetTypeContract.Is(legacy.Type, WidgetTypeContract.Value))
        {
            _ = TextOverflowStateContract.NormalizeStoredState(legacy);
            return;
        }

        var profiles = legacy.Profiles;
        if (!StateVisualProfileContract.SupportsValueSource(legacy.Type) ||
            profiles is null ||
            profiles.Count == 0)
        {
            return;
        }

        var specs = StateVisualProfileContract.GetSpecs(legacy.Type);
        var hasCompleteStoredProfileSet = specs.Count > 0 &&
                                          profiles.Count == specs.Count &&
                                          specs.All(spec =>
                                              profiles.TryGetValue(spec.Key, out var profile) &&
                                              profile is not null);

        if (hasCompleteStoredProfileSet)
        {
            // A complete schema-18 state set has the same context that the old reader used, so keep
            // its width/overflow reconciliation exactly.
            _ = TextOverflowStateContract.NormalizeStoredState(legacy);
            return;
        }

        // An incomplete schema-18 DTO can reach this mapper because migration intentionally does not
        // run current-schema pre-validation. Canonicalize only the text state that was actually stored;
        // MapProfiles() will create deterministic dormant defaults for missing states without allowing
        // those defaults to change persisted semantics.
        foreach (var profile in profiles.Values)
        {
            if (profile is null ||
                !StateVisualProfileContract.IsValueSource(profile.SourceType) ||
                profile.Text is null)
            {
                continue;
            }

            profile.Text.OverflowMode = ValueOverflowContract.Normalize(profile.Text.OverflowMode);
        }
    }

    private static CanonicalDashboard.CanvasDefinition MapCanvas(CanvasDefinition legacy) => new()
    {
        Width = legacy.Width,
        Height = legacy.Height,
        Orientation = legacy.Orientation,
        BackgroundColor = legacy.BackgroundColor,
        BackgroundImage = MapCanvasLayer(legacy.BackgroundImage),
        ForegroundImage = MapCanvasLayer(legacy.ForegroundImage)
    };

    private static CanonicalDashboard.CanvasImageLayerDefinition MapCanvasLayer(CanvasImageLayerDefinition legacy) => new()
    {
        Asset = new CanonicalDashboard.ImageAssetPresentationDefinition
        {
            SourceType = NormalizeImageSourceType(legacy.SourceType, "Canvas image"),
            Source = legacy.Source,
            Fit = NormalizeImageFit(legacy.Fit, StateVisualProfileContract.FitStretch),
            Loop = legacy.Loop
        },
        Opacity = legacy.Opacity,
        Color = legacy.Color
    };

    private static CanonicalDashboard.WidgetDefinition MapWidget(WidgetDefinition legacy)
    {
        CanonicalDashboard.WidgetDefinition canonical = legacy.Type?.Trim().ToLowerInvariant() switch
        {
            WidgetTypeContract.Value => MapValue(legacy),
            WidgetTypeContract.Binary => MapBinary(legacy),
            WidgetTypeContract.Gauge => MapGauge(legacy),
            WidgetTypeContract.Bar => MapBar(legacy),
            WidgetTypeContract.Image => MapImage(legacy),
            WidgetTypeContract.Power => MapPower(legacy),
            WidgetTypeContract.MediaSystem => MapMediaSystem(legacy),
            WidgetTypeContract.MediaPlayer => MapMediaPlayer(legacy),
            _ => throw new InvalidDataException(
                $"Dashboard widget '{legacy.Id ?? "<unknown>"}' uses unsupported schema-18 type '{legacy.Type}'.")
        };

        CopyCommon(legacy, canonical);
        return canonical;
    }

    private static CanonicalDashboard.ValueWidgetDefinition MapValue(WidgetDefinition legacy) => new()
    {
        SourceKind = TextValueContract.NormalizeSource(legacy.ValueSource),
        Metric = legacy.Metric,
        Text = legacy.Text,
        SourceUnit = legacy.SourceUnit,
        Unit = legacy.Unit,
        Format = legacy.Format,
        Prefix = legacy.Prefix,
        Suffix = legacy.Suffix,
        Fallback = legacy.Fallback,
        TextPresentation = MapTextPresentation(legacy)
    };

    private static CanonicalDashboard.BinaryWidgetDefinition MapBinary(WidgetDefinition legacy) => new()
    {
        Metric = legacy.Metric,
        Unit = legacy.Unit,
        Format = legacy.Format,
        Prefix = legacy.Prefix,
        Suffix = legacy.Suffix,
        Fallback = legacy.Fallback,
        EvaluationMode = NormalizeBinaryEvaluationMode(legacy.EvaluationMode),
        Setpoint = legacy.Setpoint,
        TrueIf = legacy.TrueIf,
        Profiles = MapProfiles(legacy)
    };

    private static CanonicalDashboard.GaugeWidgetDefinition MapGauge(WidgetDefinition legacy) => new()
    {
        Metric = legacy.Metric,
        Unit = legacy.Unit,
        Min = legacy.Min,
        Max = legacy.Max,
        Gap = legacy.Gap,
        Reverse = legacy.Reverse,
        Thresholds = MapThresholds(legacy),
        StartAngle = legacy.StartAngle,
        EndAngle = legacy.EndAngle,
        Track = new CanonicalDashboard.GaugeTrackDefinition
        {
            Enabled = legacy.TrackEnabled,
            Thickness = legacy.Thickness,
            BackgroundColor = legacy.TrackBackgroundColor,
            BorderColor = legacy.TrackBorderColor,
            BorderWidth = legacy.TrackBorderWidth,
            CornerRadius = legacy.TrackCornerRadius
        },
        Needle = new CanonicalDashboard.GaugeNeedleDefinition
        {
            Enabled = legacy.NeedleEnabled,
            Thickness = legacy.NeedleThickness,
            Color = legacy.NeedleColor,
            StartOffset = legacy.NeedleStartOffset,
            EndOffset = legacy.NeedleEndOffset,
            Pointer = new CanonicalDashboard.GaugePointerDefinition
            {
                Length = legacy.NeedlePointerLength,
                Thickness = legacy.NeedlePointerThickness,
                Color = legacy.NeedlePointerColor
            }
        }
    };

    private static CanonicalDashboard.BarWidgetDefinition MapBar(WidgetDefinition legacy) => new()
    {
        Metric = legacy.Metric,
        Unit = legacy.Unit,
        Min = legacy.Min,
        Max = legacy.Max,
        Gap = legacy.Gap,
        Reverse = legacy.Reverse,
        Thresholds = MapThresholds(legacy),
        ContentMode = NormalizeBarContentMode(legacy.BarContentMode),
        Image = new CanonicalDashboard.BarImagePresentationDefinition
        {
            Source = legacy.BarImageSource,
            Fit = BarImageContract.NormalizeImageFit(legacy.BarImageFit),
            ProgressMode = BarImageContract.NormalizeProgressMode(legacy.BarProgressMode),
            Loop = legacy.Loop
        }
    };

    private static CanonicalDashboard.ImageWidgetDefinition MapImage(WidgetDefinition legacy) => new()
    {
        Asset = new CanonicalDashboard.ImageAssetPresentationDefinition
        {
            SourceType = NormalizeImageSourceType(legacy.SourceType, $"Image widget '{legacy.Id}'"),
            Source = legacy.Source,
            Fit = NormalizeImageFit(legacy.Fit, StateVisualProfileContract.FitStretch),
            Loop = legacy.Loop
        },
        Opacity = legacy.Opacity
    };

    private static CanonicalDashboard.PowerWidgetDefinition MapPower(WidgetDefinition legacy) => new()
    {
        PowerSource = NormalizePowerSource(legacy.Source, legacy.Id),
        Profiles = MapProfiles(legacy)
    };

    private static CanonicalDashboard.MediaSystemWidgetDefinition MapMediaSystem(WidgetDefinition legacy) => new()
    {
        MediaSource = NormalizeMediaSource(legacy.Source, legacy.Id),
        Profiles = MapProfiles(legacy)
    };

    private static CanonicalDashboard.MediaPlayerWidgetDefinition MapMediaPlayer(WidgetDefinition legacy) => new()
    {
        Profiles = MapProfiles(legacy)
    };

    private static void CopyCommon(WidgetDefinition source, CanonicalDashboard.WidgetDefinition target)
    {
        target.Id = source.Id;
        target.Name = source.Name;
        target.Z = source.Z;
        target.X = source.X;
        target.Y = source.Y;
        target.Width = source.Width;
        target.Height = source.Height;
        target.Rotation = source.Rotation;
        target.Color = source.Color;
        target.BackgroundColor = source.BackgroundColor;
        target.BorderColor = source.BorderColor;
        target.BorderWidth = source.BorderWidth;
        target.CornerRadius = source.CornerRadius;
        target.ShadowEnabled = source.ShadowEnabled;
        target.ShadowOffsetX = source.ShadowOffsetX;
        target.ShadowOffsetY = source.ShadowOffsetY;
        target.ShadowBlur = source.ShadowBlur;
        target.ShadowOpacity = source.ShadowOpacity;
        target.ShadowColor = source.ShadowColor;
    }

    private static CanonicalDashboard.ThresholdSetDefinition MapThresholds(WidgetDefinition legacy) => new()
    {
        Mode = NormalizeThresholdMode(legacy.ThresholdMode),
        Items =
        [
            new CanonicalDashboard.ThresholdDefinition
            {
                Enabled = legacy.Threshold1Enabled,
                Value = legacy.Threshold1Value,
                Color = legacy.Threshold1Color
            },
            new CanonicalDashboard.ThresholdDefinition
            {
                Enabled = legacy.Threshold2Enabled,
                Value = legacy.Threshold2Value,
                Color = legacy.Threshold2Color
            },
            new CanonicalDashboard.ThresholdDefinition
            {
                Enabled = legacy.Threshold3Enabled,
                Value = legacy.Threshold3Value,
                Color = legacy.Threshold3Color
            }
        ]
    };

    private static Dictionary<string, CanonicalDashboard.StateVisualProfileDefinition> MapProfiles(WidgetDefinition legacy)
    {
        var result = new Dictionary<string, CanonicalDashboard.StateVisualProfileDefinition>(StringComparer.OrdinalIgnoreCase);
        var specs = StateVisualProfileContract.GetSpecs(legacy.Type);

        foreach (var spec in specs.OrderBy(item => item.Order))
        {
            StateVisualProfileDefinition profile;
            if (legacy.Profiles is not null &&
                legacy.Profiles.TryGetValue(spec.Key, out var existing) &&
                existing is not null)
            {
                profile = existing;
            }
            else
            {
                profile = spec.CreateDefault();
            }

            var isValue = StateVisualProfileContract.IsValueSource(profile.SourceType);
            var fit = NormalizeImageFit(profile.Fit ?? spec.DefaultFit, StateVisualProfileContract.FitContain);
            var loop = profile.Loop ?? spec.DefaultLoop ?? true;

            var assetSourceType = isValue
                ? NormalizeImageSourceType(spec.DefaultSourceType, $"Widget '{legacy.Id}' profile '{spec.Key}' default")
                : NormalizeImageSourceType(profile.SourceType, $"Widget '{legacy.Id}' profile '{spec.Key}'");
            var assetSource = isValue ? spec.DefaultSource : profile.Source;

            result[spec.Key] = new CanonicalDashboard.StateVisualProfileDefinition
            {
                ContentType = isValue ? CanonicalDashboard.StateContentType.Value : CanonicalDashboard.StateContentType.Image,
                Asset = new CanonicalDashboard.ImageAssetPresentationDefinition
                {
                    SourceType = assetSourceType,
                    Source = assetSource,
                    Fit = fit,
                    Loop = loop
                },
                Color = profile.Color,
                Opacity = profile.Opacity,
                TextPresentation = MapTextPresentation(profile.Text ?? new TextPresentationDefinition())
            };
        }

        return result;
    }

    private static CanonicalDashboard.TextPresentationDefinition MapTextPresentation(WidgetDefinition legacy) => new()
    {
        FontFamily = legacy.FontFamily,
        FontSize = legacy.FontSize,
        FontWeight = legacy.FontWeight,
        Italic = legacy.Italic,
        Align = legacy.Align,
        VerticalAlign = legacy.VerticalAlign,
        OutlineWidth = legacy.OutlineWidth,
        OutlineColor = legacy.OutlineColor,
        OverflowMode = ValueOverflowContract.Normalize(legacy.OverflowMode),
        ScrollSpeed = legacy.ScrollSpeed,
        BumpPauseMs = legacy.BumpPauseMs
    };

    private static CanonicalDashboard.TextPresentationDefinition MapTextPresentation(TextPresentationDefinition legacy) => new()
    {
        FontFamily = legacy.FontFamily,
        FontSize = legacy.FontSize,
        FontWeight = legacy.FontWeight,
        Italic = legacy.Italic,
        Align = legacy.Align,
        VerticalAlign = legacy.VerticalAlign,
        OutlineWidth = legacy.OutlineWidth,
        OutlineColor = legacy.OutlineColor,
        OverflowMode = ValueOverflowContract.Normalize(legacy.OverflowMode),
        ScrollSpeed = legacy.ScrollSpeed,
        BumpPauseMs = legacy.BumpPauseMs
    };

    private static string NormalizeImageSourceType(string? value, string context)
    {
        if (string.IsNullOrWhiteSpace(value))
            return CanonicalDashboard.ImageAssetSourceType.File;
        if (CanonicalDashboard.ImageAssetSourceType.IsFile(value))
            return CanonicalDashboard.ImageAssetSourceType.File;
        if (CanonicalDashboard.ImageAssetSourceType.IsIcon(value))
            return CanonicalDashboard.ImageAssetSourceType.Icon;
        throw new InvalidDataException($"{context} has invalid image source type '{value}'.");
    }

    private static string NormalizeImageFit(string? value, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
            return fallback;
        if (string.Equals(value, StateVisualProfileContract.FitContain, StringComparison.OrdinalIgnoreCase))
            return StateVisualProfileContract.FitContain;
        if (string.Equals(value, StateVisualProfileContract.FitCover, StringComparison.OrdinalIgnoreCase))
            return StateVisualProfileContract.FitCover;
        if (string.Equals(value, StateVisualProfileContract.FitStretch, StringComparison.OrdinalIgnoreCase))
            return StateVisualProfileContract.FitStretch;
        throw new InvalidDataException($"Invalid image Fit '{value}' in schema-18 dashboard.");
    }

    private static string NormalizeThresholdMode(string? value)
    {
        if (string.Equals(value, CanonicalDashboard.ThresholdModeContract.SegmentTransition, StringComparison.OrdinalIgnoreCase))
            return CanonicalDashboard.ThresholdModeContract.SegmentTransition;
        if (string.Equals(value, CanonicalDashboard.ThresholdModeContract.State, StringComparison.OrdinalIgnoreCase))
            return CanonicalDashboard.ThresholdModeContract.State;
        if (string.Equals(value, CanonicalDashboard.ThresholdModeContract.SegmentSolid, StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(value))
            return CanonicalDashboard.ThresholdModeContract.SegmentSolid;
        throw new InvalidDataException($"Invalid ThresholdMode '{value}' in schema-18 dashboard.");
    }

    private static string NormalizeBarContentMode(string? value) =>
        BarImageContract.IsImageMode(value)
            ? BarImageContract.ContentModeImage
            : BarImageContract.ContentModeFill;

    private static string NormalizeBinaryEvaluationMode(string? value)
    {
        var match = BinarySignalContract.SupportedEvaluationModes.FirstOrDefault(
            candidate => string.Equals(candidate, value, StringComparison.OrdinalIgnoreCase));
        if (match is not null)
            return match;
        throw new InvalidDataException($"Invalid Binary EvaluationMode '{value}' in schema-18 dashboard.");
    }

    private static string NormalizePowerSource(string? value, string? id)
    {
        if (string.Equals(value, PowerMetricContract.UpsSource, StringComparison.OrdinalIgnoreCase))
            return PowerMetricContract.UpsSource;
        if (string.Equals(value, PowerMetricContract.BatterySource, StringComparison.OrdinalIgnoreCase))
            return PowerMetricContract.BatterySource;
        throw new InvalidDataException($"Power widget '{id}' has invalid schema-18 Source '{value}'.");
    }

    private static string NormalizeMediaSource(string? value, string? id)
    {
        if (string.Equals(value, MediaMetricContract.OutputSource, StringComparison.OrdinalIgnoreCase))
            return MediaMetricContract.OutputSource;
        if (string.Equals(value, MediaMetricContract.InputSource, StringComparison.OrdinalIgnoreCase))
            return MediaMetricContract.InputSource;
        throw new InvalidDataException($"Media System widget '{id}' has invalid schema-18 Source '{value}'.");
    }
}
