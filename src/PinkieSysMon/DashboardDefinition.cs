using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Nodes;

namespace PinkieSysMon;

internal sealed class DashboardDefinition
{
    public int SchemaVersion { get; set; } = DashboardSchemaMigration.CanvasImageLayerSchemaVersion;
    public CanvasDefinition Canvas { get; set; } = new();
    public List<WidgetDefinition> Widgets { get; set; } = [];

    [JsonIgnore]
    public string BaseDirectory { get; private set; } = string.Empty;

    public static DashboardDefinition Load(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException("Dashboard definition not found.", path);

        return Parse(File.ReadAllText(path), path);
    }

    public static DashboardDefinition Parse(string json, string path)
    {
        var root = JsonNode.Parse(json, documentOptions: new JsonDocumentOptions
        {
            CommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        }) as JsonObject ?? throw new InvalidDataException("Dashboard definition is empty.");

        ValidateSerializedCanvasSchemaContract(root);
        ValidateSerializedWidgetSchemaContract(root);

        var definition = root.Deserialize<DashboardDefinition>(new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }) ?? throw new InvalidDataException("Dashboard definition is empty.");

        definition.BaseDirectory = Path.GetDirectoryName(Path.GetFullPath(path))
            ?? throw new InvalidDataException("Dashboard path has no parent directory.");

        // Older editor builds may leave a concrete overflow mode with Width=0, or None with a fixed Width.
        // Normalize compatible stored state in memory before validation. The editor mirrors the normalized
        // value back to JSON on load.
        foreach (var widget in definition.Widgets)
        {
            TextOverflowStateContract.NormalizeStoredState(widget);
            LiteralNumericContract.NormalizeStoredState(widget);
        }

        definition.Validate();
        return definition;
    }

    private static void ValidateSerializedCanvasSchemaContract(JsonObject root)
    {
        if (root["schemaVersion"] is not JsonValue schemaNode ||
            !schemaNode.TryGetValue<int>(out var schemaVersion) ||
            schemaVersion != DashboardSchemaMigration.CanvasImageLayerSchemaVersion)
        {
            return;
        }

        if (root["canvas"] is not JsonObject canvas)
            throw new InvalidDataException($"Dashboard canvas is required in schemaVersion {DashboardSchemaMigration.CanvasImageLayerSchemaVersion}.");

        if (!canvas.ContainsKey("width") || !canvas.ContainsKey("height"))
        {
            throw new InvalidDataException(
                $"Canvas must explicitly define both Width and Height in schemaVersion {DashboardSchemaMigration.CanvasImageLayerSchemaVersion}.");
        }

        if (canvas.ContainsKey("background") || canvas.ContainsKey("foreground"))
        {
            throw new InvalidDataException(
                $"Canvas uses obsolete Background/Foreground path properties. Canvas image layers must use BackgroundImage/ForegroundImage in schemaVersion {DashboardSchemaMigration.CanvasImageLayerSchemaVersion}.");
        }
    }

    private static void ValidateSerializedWidgetSchemaContract(JsonObject root)
    {
        if (root["schemaVersion"] is not JsonValue schemaNode ||
            !schemaNode.TryGetValue<int>(out var schemaVersion) ||
            schemaVersion != DashboardSchemaMigration.CanvasImageLayerSchemaVersion)
        {
            return;
        }

        if (root["widgets"] is not JsonArray widgets)
            return;

        foreach (var item in widgets)
        {
            if (item is not JsonObject widget)
                continue;

            var id = widget["id"]?.GetValue<string>() ?? "<unknown>";
            if (!widget.ContainsKey("width") || !widget.ContainsKey("height"))
            {
                throw new InvalidDataException(
                    $"Widget '{id}' must explicitly define both Width and Height in schemaVersion {DashboardSchemaMigration.CanvasImageLayerSchemaVersion}.");
            }

            if (widget.ContainsKey("diameter"))
            {
                throw new InvalidDataException(
                    $"Widget '{id}' uses obsolete Diameter geometry. Gauge size is represented by Width with derived Height.");
            }

            var type = widget["type"]?.GetValue<string>();
            if (widget.ContainsKey("fillColor"))
            {
                throw new InvalidDataException(
                    $"Widget '{id}' uses removed FillColor. This property is not part of schemaVersion {DashboardSchemaMigration.CanvasImageLayerSchemaVersion}.");
            }
            if (widget.ContainsKey("spacing"))
            {
                throw new InvalidDataException(
                    $"Widget '{id}' uses removed Spacing. This property is not part of schemaVersion {DashboardSchemaMigration.CanvasImageLayerSchemaVersion}.");
            }

            foreach (var removedGraphProperty in new[] { "history", "sampleIntervalMs", "strokeWidth" })
            {
                if (widget.ContainsKey(removedGraphProperty))
                {
                    throw new InvalidDataException(
                        $"Widget '{id}' uses removed Graph property '{removedGraphProperty}'. This property is not part of schemaVersion {DashboardSchemaMigration.CanvasImageLayerSchemaVersion}.");
                }
            }

            if (StateVisualProfileContract.SupportsValueSource(type))
            {
                if (widget.ContainsKey("fit") || widget.ContainsKey("loop"))
                {
                    throw new InvalidDataException(
                        $"State-content widget '{id}' uses obsolete top-level Fit/Loop. Image presentation is stored per state profile in schemaVersion {DashboardSchemaMigration.CanvasImageLayerSchemaVersion}.");
                }

                var obsoleteTextProperty = new[]
                {
                    "fontFamily", "fontSize", "fontWeight", "italic", "align", "verticalAlign",
                    "outlineWidth", "outlineColor", "overflowMode", "scrollSpeed", "bumpPauseMs"
                }.FirstOrDefault(widget.ContainsKey);
                if (obsoleteTextProperty is not null)
                {
                    throw new InvalidDataException(
                        $"State-content widget '{id}' uses obsolete top-level text presentation '{obsoleteTextProperty}'. Text presentation is stored per state profile in schemaVersion {DashboardSchemaMigration.CanvasImageLayerSchemaVersion}.");
                }

                if (widget["profiles"] is not JsonObject profiles)
                    throw new InvalidDataException($"State-content widget '{id}' requires state profiles.");

                foreach (var spec in StateVisualProfileContract.GetSpecs(type))
                {
                    if (profiles[spec.Key] is not JsonObject profile)
                    {
                        throw new InvalidDataException(
                            $"State-content widget '{id}' requires profile '{spec.Key}'.");
                    }

                    if (!profile.ContainsKey("fit") || !profile.ContainsKey("loop"))
                    {
                        throw new InvalidDataException(
                            $"State-content widget '{id}' profile '{spec.Key}' must explicitly define Fit and Loop in schemaVersion {DashboardSchemaMigration.CanvasImageLayerSchemaVersion}.");
                    }

                    if (profile["text"] is not JsonObject text)
                    {
                        throw new InvalidDataException(
                            $"State-content widget '{id}' profile '{spec.Key}' must explicitly define Text presentation in schemaVersion {DashboardSchemaMigration.CanvasImageLayerSchemaVersion}.");
                    }

                    foreach (var textProperty in new[]
                    {
                        "fontFamily", "fontSize", "fontWeight", "italic", "align", "verticalAlign",
                        "outlineWidth", "outlineColor", "overflowMode", "scrollSpeed", "bumpPauseMs"
                    })
                    {
                        if (!text.ContainsKey(textProperty))
                        {
                            throw new InvalidDataException(
                                $"State-content widget '{id}' profile '{spec.Key}' Text presentation must explicitly define '{textProperty}' in schemaVersion {DashboardSchemaMigration.CanvasImageLayerSchemaVersion}.");
                        }
                    }
                }
            }
        }
    }

    internal void Validate()
    {
        if (SchemaVersion != DashboardSchemaMigration.CanvasImageLayerSchemaVersion)
            throw new InvalidDataException($"Unsupported dashboard schemaVersion: {SchemaVersion}.");

        _ = FrameGeometry.GetLogicalSize(Canvas.Width, Canvas.Height, Canvas.Orientation);
        _ = ColorParser.Parse(Canvas.BackgroundColor);

        if (Canvas.BackgroundImage is null || Canvas.ForegroundImage is null)
            throw new InvalidDataException("Canvas BackgroundImage and ForegroundImage definitions must not be null.");

        Canvas.BackgroundImage.Validate(BaseDirectory, "background");
        Canvas.ForegroundImage.Validate(BaseDirectory, "foreground");

        var widgetIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var widget in Widgets)
        {
            if (!WidgetTypeContract.IsKnown(widget.Type))
                throw new InvalidDataException($"Unknown dashboard widget type: '{widget.Type}'.");
            if (string.IsNullOrWhiteSpace(widget.Id))
                throw new InvalidDataException($"Dashboard widget of type '{widget.Type}' requires a non-empty Id.");
            if (!widgetIds.Add(widget.Id))
                throw new InvalidDataException($"Duplicate dashboard widget Id: '{widget.Id}'.");

            widget.Validate(BaseDirectory);
        }
    }
}

internal sealed class CanvasDefinition
{
    public int Width { get; set; } = FrameGeometry.DefaultNativeWidth;
    public int Height { get; set; } = FrameGeometry.DefaultNativeHeight;
    public int Orientation { get; set; } = 0;
    public string BackgroundColor { get; set; } = "#000000";

    [Browsable(false)]
    public CanvasImageLayerDefinition BackgroundImage { get; set; } = new();

    [Browsable(false)]
    public CanvasImageLayerDefinition ForegroundImage { get; set; } = new();
}

internal sealed class CanvasImageLayerDefinition
{
    [JsonPropertyName("sourceType")]
    public string SourceType { get; set; } = StateVisualProfileContract.SourceFile;

    [JsonPropertyName("source")]
    public string? Source { get; set; }

    [JsonPropertyName("fit")]
    public string Fit { get; set; } = StateVisualProfileContract.FitStretch;

    [JsonPropertyName("opacity")]
    public float Opacity { get; set; } = 1f;

    [JsonPropertyName("loop")]
    public bool Loop { get; set; } = true;

    [JsonPropertyName("color")]
    public string Color { get; set; } = "#FFFFFFFF";

    internal void Validate(string dashboardBaseDirectory, string role)
    {
        var sourceType = SourceType?.Trim();
        if (!string.Equals(sourceType, StateVisualProfileContract.SourceFile, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(sourceType, StateVisualProfileContract.SourceIcon, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                $"Canvas {role} image SourceType must be file or icon.");
        }

        if (!StateVisualProfileContract.SupportedFits.Contains(Fit, StringComparer.OrdinalIgnoreCase))
            throw new InvalidDataException($"Canvas {role} image Fit must be contain, cover, or stretch.");

        if (!float.IsFinite(Opacity) || Opacity is < 0f or > 1f)
            throw new InvalidDataException($"Canvas {role} image Opacity must be in the range 0..1.");

        _ = ColorParser.Parse(Color);

        if (string.IsNullOrWhiteSpace(Source))
            return;

        if (StateVisualProfileContract.IsFileSource(SourceType))
        {
            if (Path.IsPathRooted(Source))
            {
                throw new InvalidDataException(
                    $"Canvas {role} image Source must be relative to the dashboard directory when SourceType=file.");
            }

            _ = AssetPathResolver.ResolveExistingFile(
                dashboardBaseDirectory,
                Source,
                $"Canvas {role} image source");
        }
        else
        {
            _ = GlobalAssetResolver.ResolveIcon(dashboardBaseDirectory, Source);
        }
    }
}

internal sealed class WidgetDefinition
{
    [ReadOnly(true)]
    public string Type { get; set; } = string.Empty;
    [ReadOnly(true)]
    public string? Id { get; set; }
    public string? Name { get; set; }
    public int Z { get; set; }

    public float X { get; set; }
    public float Y { get; set; }
    public float Width { get; set; }
    public float Height { get; set; }

    // Universal widget transform. Positive values rotate clockwise around the widget center.
    public double Rotation { get; set; }

    [DisplayName("Content Source")]
    public string ValueSource { get; set; } = TextValueContract.SourceMetric;
    public string Text { get; set; } = string.Empty;
    [TypeConverter(typeof(MetricNameConverter))]
    public string? Metric { get; set; }

    // Manual numeric Text can declare the semantic unit of the literal source value.
    // Provider metrics carry this metadata in MetricDescriptor and ignore this field.
    [DisplayName("Source Unit")]
    public string? SourceUnit { get; set; }

    // Binary widgets can either use the native truthiness contract or compare against a setpoint.
    [DisplayName("Evaluation Mode")]
    public string EvaluationMode { get; set; } = BinarySignalContract.EvaluationModeAuto;
    [DisplayName("Setpoint")]
    [Description("Reference value used when Evaluation Mode is Setpoint.")]
    public string Setpoint { get; set; } = string.Empty;
    [DisplayName("True If")]
    [Description("Numeric comparison operator used when Evaluation Mode is Setpoint.")]
    public string TrueIf { get; set; } = BinarySignalContract.TrueIfGreaterThan;

    public string Prefix { get; set; } = string.Empty;
    public string Suffix { get; set; } = string.Empty;
    public string? Unit { get; set; }
    public string? Format { get; set; }
    public string Fallback { get; set; } = "--";

    public string Color { get; set; } = "#FFFFFFFF";
    public string BackgroundColor { get; set; } = "#00000000";
    public string BorderColor { get; set; } = "#00000000";
    public float BorderWidth { get; set; }
    public float CornerRadius { get; set; }

    public string FontFamily { get; set; } = "Roboto";
    public float FontSize { get; set; } = 32;
    public int FontWeight { get; set; } = 400;
    public bool Italic { get; set; }
    public string Align { get; set; } = "left";
    public string VerticalAlign { get; set; } = "baseline";

    // Fixed-width text/value overflow. Width=0 preserves auto-size behavior.
    [DisplayName("Overflow Mode")]
    public string OverflowMode { get; set; } = ValueOverflowContract.None;
    [DisplayName("Scroll Speed (px/s)")]
    [Description("0 = Auto (FontSize × 2 px/s). Used by Scroll and Bump.")]
    public float ScrollSpeed { get; set; }
    [DisplayName("Bump Pause (ms)")]
    public int BumpPauseMs { get; set; } = 500;

    // universal visible-pixel effects; outline remains text/value-specific
    public float OutlineWidth { get; set; }
    public string OutlineColor { get; set; } = "#FF000000";
    public bool ShadowEnabled { get; set; }
    public float ShadowOffsetX { get; set; } = 2f;
    public float ShadowOffsetY { get; set; } = 2f;
    public float ShadowBlur { get; set; } = 4f;
    public float ShadowOpacity { get; set; } = 0.5f;
    public string ShadowColor { get; set; } = "#FF000000";

    public double Min { get; set; }
    public double Max { get; set; } = 100;

    // Bar content can use the traditional threshold-colored fill or a raster image.
    [DisplayName("Content Mode")]
    public string BarContentMode { get; set; } = BarImageContract.ContentModeFill;
    [DisplayName("Image Source")]
    public string? BarImageSource { get; set; }
    [DisplayName("Image Fit")]
    public string BarImageFit { get; set; } = BarImageContract.ImageFitStretch;
    [DisplayName("Progress Mode")]
    public string BarProgressMode { get; set; } = BarImageContract.ProgressModeScale;

    // Gauge geometry uses the universal Width/Height contract. Width is editable; Height is derived and must equal Width.
    public float Thickness { get; set; } = 20f;
    public float StartAngle { get; set; } = 30f;
    public float EndAngle { get; set; } = 330f;

    // Gauge track styling is independent from the universal widget container styling above.
    [DisplayName("Track Enabled")]
    public bool TrackEnabled { get; set; } = true;
    [DisplayName("Track Background Color")]
    public string TrackBackgroundColor { get; set; } = "#00000000";
    [DisplayName("Track Border Color")]
    public string TrackBorderColor { get; set; } = "#00000000";
    [DisplayName("Track Border Width")]
    public float TrackBorderWidth { get; set; }
    [DisplayName("Track Corner Radius")]
    public float TrackCornerRadius { get; set; }

    // Needle and pointer are rendered on the same metric axis above the gauge track.
    [DisplayName("Needle Enabled")]
    public bool NeedleEnabled { get; set; }
    [DisplayName("Needle Thickness")]
    public float NeedleThickness { get; set; } = 2f;
    [DisplayName("Needle Color")]
    public string NeedleColor { get; set; } = "#FFFFFFFF";
    [DisplayName("Needle Start Offset")]
    [Description("Visible needle starts this many pixels from the widget center.")]
    public float NeedleStartOffset { get; set; }
    [DisplayName("Needle End Offset")]
    [Description("Visible needle ends this many pixels before the widget radius.")]
    public float NeedleEndOffset { get; set; }
    [DisplayName("Pointer Length")]
    [Description("Length of the distal pointer segment measured back from the end-offset-adjusted needle end.")]
    public float NeedlePointerLength { get; set; }
    [DisplayName("Pointer Thickness")]
    public float NeedlePointerThickness { get; set; } = 2f;
    [DisplayName("Pointer Color")]
    public string NeedlePointerColor { get; set; } = "#FFFFFFFF";

    // Shared quantitative-indicator presentation for bar and gauge widgets.
    public float Gap { get; set; }
    public bool Reverse { get; set; }
    public string ThresholdMode { get; set; } = "SegmentSolid";
    public bool Threshold1Enabled { get; set; }
    public double Threshold1Value { get; set; }
    public string Threshold1Color { get; set; } = "#FFFFFFFF";
    public bool Threshold2Enabled { get; set; }
    public double Threshold2Value { get; set; }
    public string Threshold2Color { get; set; } = "#FFFFFFFF";
    public bool Threshold3Enabled { get; set; }
    public double Threshold3Value { get; set; }
    public string Threshold3Color { get; set; } = "#FFFFFFFF";

    // image widget
    [DisplayName("Source Type")]
    public string SourceType { get; set; } = "file";
    public string? Source { get; set; }
    public string Fit { get; set; } = "stretch";
    public bool Loop { get; set; } = true;
    public float Opacity { get; set; } = 1f;

    // State-driven icon widgets share one declarative visual-profile schema.
    [Browsable(false)]
    public Dictionary<string, StateVisualProfileDefinition>? Profiles { get; set; }

    internal void EnsureStateVisualProfiles() =>
        StateVisualProfileContract.EnsureProfiles(this);

    internal StateVisualProfile GetStateVisualProfile(object? stateOrType) =>
        StateVisualProfileContract.GetProfile(this, stateOrType);

    internal IEnumerable<StateVisualProfile> EnumerateStateVisualProfiles() =>
        StateVisualProfileContract.EnumerateProfiles(this);

    public void Validate(string dashboardBaseDirectory)
    {
        ValidateFiniteNumbers();

        _ = ColorParser.Parse(Color);
        _ = ColorParser.Parse(BackgroundColor);
        _ = ColorParser.Parse(BorderColor);
        _ = ColorParser.Parse(OutlineColor);
        _ = ColorParser.Parse(ShadowColor);
        _ = ColorParser.Parse(Threshold1Color);
        _ = ColorParser.Parse(Threshold2Color);
        _ = ColorParser.Parse(Threshold3Color);
        _ = ColorParser.Parse(TrackBackgroundColor);
        _ = ColorParser.Parse(TrackBorderColor);
        _ = ColorParser.Parse(NeedleColor);
        _ = ColorParser.Parse(NeedlePointerColor);

        if (BorderWidth < 0)
            throw new InvalidDataException($"Widget '{Id}' BorderWidth cannot be negative.");
        if (CornerRadius < 0)
            throw new InvalidDataException($"Widget '{Id}' CornerRadius cannot be negative.");
        if (TrackBorderWidth < 0)
            throw new InvalidDataException($"Widget '{Id}' TrackBorderWidth cannot be negative.");
        if (TrackCornerRadius < 0)
            throw new InvalidDataException($"Widget '{Id}' TrackCornerRadius cannot be negative.");
        if (NeedleThickness < 0)
            throw new InvalidDataException($"Widget '{Id}' NeedleThickness cannot be negative.");
        if (NeedleStartOffset < 0)
            throw new InvalidDataException($"Widget '{Id}' NeedleStartOffset cannot be negative.");
        if (NeedleEndOffset < 0)
            throw new InvalidDataException($"Widget '{Id}' NeedleEndOffset cannot be negative.");
        if (NeedlePointerLength < 0)
            throw new InvalidDataException($"Widget '{Id}' NeedlePointerLength cannot be negative.");
        if (NeedlePointerThickness < 0)
            throw new InvalidDataException($"Widget '{Id}' NeedlePointerThickness cannot be negative.");
        if (Gap < 0)
            throw new InvalidDataException($"Widget '{Id}' Gap cannot be negative.");
        if (OutlineWidth < 0)
            throw new InvalidDataException($"Widget '{Id}' OutlineWidth cannot be negative.");
        if (ShadowBlur < 0)
            throw new InvalidDataException($"Widget '{Id}' ShadowBlur cannot be negative.");
        if (ShadowOpacity is < 0f or > 1f)
            throw new InvalidDataException($"Widget '{Id}' ShadowOpacity must be in the range 0..1.");
        if (WidgetTypeContract.Is(Type, WidgetTypeContract.Gauge))
        {
            ValidateGaugeGeometry();
        }
        else if (StateVisualProfileContract.SupportsValueSource(Type))
        {
            EnsureStateVisualProfiles();
            var valueOnly = StateVisualProfileContract.UsesOnlyValueSources(this);

            if (valueOnly)
            {
                // A value-only state-content widget remains stateful, but its height is
                // content-driven just like text content. Width=0 keeps intrinsic width.
                // A dormant positive Height may be preserved for a future icon/file state,
                // but it is not authoritative while every state uses Source Type=value.
                if (Width < 0 || Height < 0)
                {
                    throw new InvalidDataException(
                        $"Value-only {Type} widget '{Id}' cannot have negative Width or Height.");
                }
            }
            else if (Width <= 0 || Height <= 0)
            {
                throw new InvalidDataException(
                    $"{Type} widget '{Id}' requires Width > 0 and Height > 0 when any state uses Source Type=icon or file.");
            }
        }
        else if (WidgetTypeContract.Is(Type, WidgetTypeContract.Bar) ||
                 WidgetTypeContract.Is(Type, WidgetTypeContract.Image))
        {
            if (Width <= 0 || Height <= 0)
                throw new InvalidDataException($"{Type} widget '{Id}' has invalid geometry.");
        }

        var valueUsesMetric = WidgetTypeContract.Is(Type, WidgetTypeContract.Value) &&
                              TextValueContract.IsMetricSource(ValueSource);
        if ((valueUsesMetric ||
             WidgetTypeContract.Is(Type, WidgetTypeContract.Bar) ||
             WidgetTypeContract.Is(Type, WidgetTypeContract.Gauge) ||
             WidgetTypeContract.Is(Type, WidgetTypeContract.Binary)) &&
            string.IsNullOrWhiteSpace(Metric))
        {
            throw new InvalidDataException($"{Type} widget '{Id}' requires Metric.");
        }

        if ((WidgetTypeContract.Is(Type, WidgetTypeContract.Bar) ||
             WidgetTypeContract.Is(Type, WidgetTypeContract.Gauge)) && Max <= Min)
        {
            throw new InvalidDataException($"{Type} widget '{Id}' requires Max > Min.");
        }

        if (WidgetTypeContract.Is(Type, WidgetTypeContract.Bar))
        {
            if (!BarImageContract.SupportedContentModes.Contains(BarContentMode, StringComparer.OrdinalIgnoreCase))
                throw new InvalidDataException($"Bar widget '{Id}' has invalid Content Mode '{BarContentMode}'.");

            if (BarImageContract.IsImageMode(BarContentMode))
            {
                if (string.IsNullOrWhiteSpace(BarImageSource))
                    throw new InvalidDataException($"Bar widget '{Id}' requires Image Source when Content Mode is Image.");
                if (Path.IsPathRooted(BarImageSource))
                    throw new InvalidDataException($"Bar widget '{Id}' Image Source must be relative to the dashboard directory.");
                if (!BarImageContract.SupportedImageFits.Contains(BarImageFit, StringComparer.OrdinalIgnoreCase))
                    throw new InvalidDataException($"Bar widget '{Id}' has invalid Image Fit '{BarImageFit}'.");
                if (!BarImageContract.SupportedProgressModes.Contains(BarProgressMode, StringComparer.OrdinalIgnoreCase))
                    throw new InvalidDataException($"Bar widget '{Id}' has invalid Progress Mode '{BarProgressMode}'.");

                _ = AssetPathResolver.ResolveExistingFile(
                    dashboardBaseDirectory,
                    BarImageSource,
                    $"Bar widget '{Id}' image source");
            }
        }

        if ((WidgetTypeContract.Is(Type, WidgetTypeContract.Bar) && !BarImageContract.IsImageMode(BarContentMode)) ||
            WidgetTypeContract.Is(Type, WidgetTypeContract.Gauge))
        {
            var widgetType = WidgetTypeContract.Is(Type, WidgetTypeContract.Gauge) ? "Gauge" : "Bar";
            if (ThresholdMode is not ("SegmentTransition" or "SegmentSolid" or "State"))
                throw new InvalidDataException($"{widgetType} widget '{Id}' has invalid ThresholdMode '{ThresholdMode}'.");

            var thresholds = new[]
            {
                (Threshold1Enabled, Threshold1Value),
                (Threshold2Enabled, Threshold2Value),
                (Threshold3Enabled, Threshold3Value)
            }.Where(item => item.Item1).Select(item => item.Item2).ToArray();

            if (thresholds.Any(value => value < Min || value > Max))
                throw new InvalidDataException($"{widgetType} widget '{Id}' threshold values must be within Min..Max.");
            if (!thresholds.SequenceEqual(thresholds.OrderBy(value => value)))
                throw new InvalidDataException($"{widgetType} widget '{Id}' enabled thresholds must be ordered by value.");
        }

        if (WidgetTypeContract.Is(Type, WidgetTypeContract.Binary))
        {
            if (!BinarySignalContract.SupportedEvaluationModes.Contains(EvaluationMode, StringComparer.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    $"Binary widget '{Id}' has invalid EvaluationMode '{EvaluationMode}'.");
            }

            if (!BinarySignalContract.SupportedTrueIfOperators.Contains(TrueIf, StringComparer.Ordinal))
                throw new InvalidDataException($"Binary widget '{Id}' has invalid TrueIf operator '{TrueIf}'.");
        }

        if (WidgetTypeContract.Is(Type, WidgetTypeContract.Image))
        {
            if (Fit.ToLowerInvariant() is not ("contain" or "cover" or "stretch"))
                throw new InvalidDataException($"{Type} widget '{Id}' Fit must be contain, cover, or stretch.");
        }

        if (WidgetTypeContract.Is(Type, WidgetTypeContract.Image))
        {
            if (string.IsNullOrWhiteSpace(Source))
                throw new InvalidDataException($"Image widget '{Id}' requires Source.");

            var sourceType = string.IsNullOrWhiteSpace(SourceType) ? "file" : SourceType.Trim().ToLowerInvariant();
            if (sourceType is not ("file" or "icon"))
                throw new InvalidDataException($"Image widget '{Id}' SourceType must be file or icon.");

            if (Opacity is < 0f or > 1f)
                throw new InvalidDataException($"Image widget '{Id}' Opacity must be in the range 0..1.");

            if (sourceType == "file")
            {
                if (Path.IsPathRooted(Source))
                    throw new InvalidDataException($"Image widget '{Id}' Source must be relative to the dashboard directory when SourceType=file.");

                _ = AssetPathResolver.ResolveExistingFile(
                    dashboardBaseDirectory,
                    Source,
                    $"Image widget '{Id}' source");
            }
            else
            {
                _ = GlobalAssetResolver.ResolveIcon(
                    dashboardBaseDirectory,
                    Source);
            }
        }

        if (StateVisualProfileContract.IsStateVisualWidget(Type))
        {
            StateVisualProfileContract.ValidateKeys(this);

            if (WidgetTypeContract.Is(Type, WidgetTypeContract.Power) &&
                !PowerMetricContract.IsSupportedSource(Source))
            {
                throw new InvalidDataException(
                    $"Widget '{Id}' Source must be power.ups or power.battery.");
            }

            if (WidgetTypeContract.Is(Type, WidgetTypeContract.MediaSystem) &&
                Source is not (MediaMetricContract.OutputSource or MediaMetricContract.InputSource))
            {
                throw new InvalidDataException(
                    $"media.system widget '{Id}' Source must be media.output or media.input.");
            }

            foreach (var spec in StateVisualProfileContract.GetSpecs(Type))
            {
                var profile = Profiles![spec.Key];
                ValidateStateVisualProfile(
                    dashboardBaseDirectory,
                    spec.Category,
                    profile.SourceType,
                    profile.Source,
                    profile.Color,
                    profile.Opacity,
                    profile.Fit,
                    profile.Loop,
                    profile.Text);
            }
        }

        if (WidgetTypeContract.Is(Type, WidgetTypeContract.Value))
        {
            if (FontSize <= 0)
                throw new InvalidDataException($"Widget '{Id}' has invalid FontSize.");
            if (string.IsNullOrWhiteSpace(FontFamily))
                throw new InvalidDataException($"Widget '{Id}' requires FontFamily.");
            if (FontWeight is < 1 or > 1000)
                throw new InvalidDataException($"Widget '{Id}' FontWeight must be in the range 1..1000.");
            if (!ValueOverflowContract.SupportedModes.Contains(OverflowMode, StringComparer.OrdinalIgnoreCase))
                throw new InvalidDataException($"{Type} widget '{Id}' has invalid OverflowMode '{OverflowMode}'.");

            if (TextOverflowStateContract.UsesTextPresentation(this))
            {
                var normalizedOverflow = ValueOverflowContract.Normalize(OverflowMode);
                if (TextOverflowStateContract.SupportsAutoWidth(this) && Width <= 0f)
                {
                    if (normalizedOverflow != ValueOverflowContract.None)
                        throw new InvalidDataException($"{Type} widget '{Id}' must use OverflowMode=None when Width=0.");
                }
                else if (normalizedOverflow == ValueOverflowContract.None)
                {
                    throw new InvalidDataException($"{Type} widget '{Id}' cannot use OverflowMode=None with a fixed Width.");
                }
            }

            if (ScrollSpeed < 0f)
                throw new InvalidDataException($"{Type} widget '{Id}' ScrollSpeed must be >= 0.");
            if (BumpPauseMs < 0)
                throw new InvalidDataException($"{Type} widget '{Id}' BumpPauseMs cannot be negative.");
        }

        if (WidgetTypeContract.Is(Type, WidgetTypeContract.Value))
        {
            if (!TextValueContract.SupportedSources.Contains(ValueSource, StringComparer.OrdinalIgnoreCase))
                throw new InvalidDataException($"Value widget '{Id}' has invalid Content Source '{ValueSource}'.");
            if (Width < 0f || Height < 0f)
                throw new InvalidDataException($"Value widget '{Id}' Width and Height must be >= 0.");
        }
    }

    private void ValidateFiniteNumbers()
    {
        RequireFinite(X, nameof(X));
        RequireFinite(Y, nameof(Y));
        RequireFinite(Width, nameof(Width));
        RequireFinite(Height, nameof(Height));
        RequireFinite(Rotation, nameof(Rotation));

        RequireFinite(BorderWidth, nameof(BorderWidth));
        RequireFinite(CornerRadius, nameof(CornerRadius));

        RequireFinite(FontSize, nameof(FontSize));
        RequireFinite(ScrollSpeed, nameof(ScrollSpeed));
        RequireFinite(OutlineWidth, nameof(OutlineWidth));
        RequireFinite(ShadowOffsetX, nameof(ShadowOffsetX));
        RequireFinite(ShadowOffsetY, nameof(ShadowOffsetY));
        RequireFinite(ShadowBlur, nameof(ShadowBlur));
        RequireFinite(ShadowOpacity, nameof(ShadowOpacity));

        RequireFinite(Min, nameof(Min));
        RequireFinite(Max, nameof(Max));
        RequireFinite(Thickness, nameof(Thickness));
        RequireFinite(StartAngle, nameof(StartAngle));
        RequireFinite(EndAngle, nameof(EndAngle));
        RequireFinite(TrackBorderWidth, nameof(TrackBorderWidth));
        RequireFinite(TrackCornerRadius, nameof(TrackCornerRadius));
        RequireFinite(NeedleThickness, nameof(NeedleThickness));
        RequireFinite(NeedleStartOffset, nameof(NeedleStartOffset));
        RequireFinite(NeedleEndOffset, nameof(NeedleEndOffset));
        RequireFinite(NeedlePointerLength, nameof(NeedlePointerLength));
        RequireFinite(NeedlePointerThickness, nameof(NeedlePointerThickness));
        RequireFinite(Gap, nameof(Gap));
        RequireFinite(Threshold1Value, nameof(Threshold1Value));
        RequireFinite(Threshold2Value, nameof(Threshold2Value));
        RequireFinite(Threshold3Value, nameof(Threshold3Value));

        RequireFinite(Opacity, nameof(Opacity));
    }

    private void RequireFinite(float value, string propertyName)
    {
        if (!float.IsFinite(value))
            throw new InvalidDataException($"Widget '{Id}' {propertyName} must be a finite number.");
    }

    private void RequireFinite(double value, string propertyName)
    {
        if (!double.IsFinite(value))
            throw new InvalidDataException($"Widget '{Id}' {propertyName} must be a finite number.");
    }

    private void ValidateGaugeGeometry()
    {
        if (!float.IsFinite(Width) || Width <= 0f)
            throw new InvalidDataException($"Gauge widget '{Id}' Width must be a finite value greater than zero.");
        if (!float.IsFinite(Height) || Height <= 0f)
            throw new InvalidDataException($"Gauge widget '{Id}' Height must be a finite value greater than zero.");
        if (Math.Abs(Height - Width) > 0.0001f)
            throw new InvalidDataException($"Gauge widget '{Id}' Height is derived and must equal Width.");
        if (!float.IsFinite(StartAngle) || !float.IsFinite(EndAngle))
            throw new InvalidDataException($"Gauge widget '{Id}' StartAngle and EndAngle must be finite values.");
        if (StartAngle < 0f || EndAngle > 360f || StartAngle >= EndAngle)
        {
            throw new InvalidDataException(
                $"Gauge widget '{Id}' requires 0 <= StartAngle < EndAngle <= 360; crossing 360 degrees is not allowed.");
        }

        if (Thickness < 0f)
            throw new InvalidDataException($"Gauge widget '{Id}' Thickness cannot be negative.");

        var availableRadius = Width / 2f;
        if (TrackEnabled)
        {
            if (Thickness <= 0f)
                throw new InvalidDataException($"Gauge widget '{Id}' Thickness must be greater than zero when TrackEnabled is true.");

            var radialDepth = 2f * TrackBorderWidth + 2f * Gap + Thickness;
            if (!float.IsFinite(radialDepth) || radialDepth > availableRadius)
            {
                throw new InvalidDataException(
                    $"Gauge widget '{Id}' track geometry does not fit inside Width: " +
                    $"2*TrackBorderWidth + 2*Gap + Thickness ({radialDepth:0.###}) must be <= Width/2 ({availableRadius:0.###}).");
            }
        }

        if (NeedleEnabled)
        {
            if (NeedleThickness <= 0f)
                throw new InvalidDataException($"Gauge widget '{Id}' NeedleThickness must be greater than zero when NeedleEnabled is true.");
            if (NeedlePointerLength > 0f && NeedlePointerThickness <= 0f)
                throw new InvalidDataException($"Gauge widget '{Id}' NeedlePointerThickness must be greater than zero when NeedlePointerLength is positive.");
            if (NeedleStartOffset + NeedleEndOffset >= availableRadius)
            {
                throw new InvalidDataException(
                    $"Gauge widget '{Id}' needle offsets leave no visible needle: " +
                    $"NeedleStartOffset + NeedleEndOffset must be < Width/2 ({availableRadius:0.###}).");
            }
        }
    }

    private void ValidateStateTextPresentation(
        string stateName,
        string sourceType,
        TextPresentationDefinition? text)
    {
        if (text is null)
            throw new InvalidDataException($"Widget '{Id}' {stateName} Text presentation must be defined.");

        if (string.IsNullOrWhiteSpace(text.FontFamily))
            throw new InvalidDataException($"Widget '{Id}' {stateName} Text FontFamily is required.");
        if (!float.IsFinite(text.FontSize) || text.FontSize <= 0f)
            throw new InvalidDataException($"Widget '{Id}' {stateName} Text FontSize must be finite and greater than zero.");
        if (text.FontWeight is < 1 or > 1000)
            throw new InvalidDataException($"Widget '{Id}' {stateName} Text FontWeight must be in the range 1..1000.");
        if (!float.IsFinite(text.OutlineWidth) || text.OutlineWidth < 0f)
            throw new InvalidDataException($"Widget '{Id}' {stateName} Text OutlineWidth must be finite and >= 0.");
        _ = ColorParser.Parse(text.OutlineColor);

        if (!ValueOverflowContract.SupportedModes.Contains(text.OverflowMode, StringComparer.OrdinalIgnoreCase))
            throw new InvalidDataException($"Widget '{Id}' {stateName} Text has invalid OverflowMode '{text.OverflowMode}'.");
        if (!float.IsFinite(text.ScrollSpeed) || text.ScrollSpeed < 0f)
            throw new InvalidDataException($"Widget '{Id}' {stateName} Text ScrollSpeed must be finite and >= 0.");
        if (text.BumpPauseMs < 0)
            throw new InvalidDataException($"Widget '{Id}' {stateName} Text BumpPauseMs cannot be negative.");

        if (!StateVisualProfileContract.IsValueSource(sourceType))
            return;

        var normalizedOverflow = ValueOverflowContract.Normalize(text.OverflowMode);
        if (TextOverflowStateContract.SupportsAutoWidth(this) && Width <= 0f)
        {
            if (normalizedOverflow != ValueOverflowContract.None)
            {
                throw new InvalidDataException(
                    $"State-content widget '{Id}' {stateName} must use OverflowMode=None when shared Width=0.");
            }
        }
        else if (normalizedOverflow == ValueOverflowContract.None)
        {
            throw new InvalidDataException(
                $"State-content widget '{Id}' {stateName} cannot use OverflowMode=None with a fixed shared Width.");
        }
    }

    private void ValidateStateVisualProfile(
        string dashboardBaseDirectory,
        string stateName,
        string sourceTypeValue,
        string? sourceValue,
        string? colorValue,
        float opacityValue,
        string? fitValue,
        bool? loopValue,
        TextPresentationDefinition? textPresentation)
    {
        var sourceType = string.IsNullOrWhiteSpace(sourceTypeValue)
            ? StateVisualProfileContract.SourceIcon
            : sourceTypeValue.Trim().ToLowerInvariant();
        var supported = StateVisualProfileContract.GetSupportedSourceTypes(Type);
        if (!supported.Contains(sourceType, StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                $"Widget '{Id}' {stateName} Source Type must be one of: {string.Join(", ", supported)}.");
        }

        if (!float.IsFinite(opacityValue) || opacityValue is < 0f or > 1f)
            throw new InvalidDataException($"Widget '{Id}' {stateName} Opacity must be a finite value in the range 0..1.");

        if (!string.IsNullOrWhiteSpace(colorValue))
            _ = ColorParser.Parse(colorValue);

        if (StateVisualProfileContract.SupportsValueSource(Type))
        {
            if (string.IsNullOrWhiteSpace(fitValue) ||
                !StateVisualProfileContract.SupportedFits.Contains(fitValue.Trim(), StringComparer.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    $"Widget '{Id}' {stateName} Fit must be contain, cover, or stretch.");
            }

            if (loopValue is null)
                throw new InvalidDataException($"Widget '{Id}' {stateName} Loop must be defined.");

            ValidateStateTextPresentation(stateName, sourceType, textPresentation);
        }

        if (StateVisualProfileContract.IsValueSource(sourceType))
            return;

        if (string.IsNullOrWhiteSpace(sourceValue))
            throw new InvalidDataException($"Widget '{Id}' {stateName} Source is required.");

        if (StateVisualProfileContract.IsIconSource(sourceType))
        {
            _ = GlobalAssetResolver.ResolveIcon(dashboardBaseDirectory, sourceValue);
        }
        else
        {
            if (Path.IsPathRooted(sourceValue))
                throw new InvalidDataException($"Widget '{Id}' {stateName} Source must be relative to the dashboard directory when Source Type=file.");
            _ = AssetPathResolver.ResolveExistingFile(
                dashboardBaseDirectory,
                sourceValue,
                $"Widget '{Id}' {stateName} source");
        }
    }
}



public sealed class MetricNameConverter : StringConverter
{
    private static readonly string[] MetricNames =
        MetricContract.Descriptors
            .Select(x => x.Id)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    public override bool GetStandardValuesSupported(ITypeDescriptorContext? context) => true;

    public override bool GetStandardValuesExclusive(ITypeDescriptorContext? context) => false;

    private static readonly StandardValuesCollection StandardMetricNames = new(MetricNames);

    public override StandardValuesCollection GetStandardValues(ITypeDescriptorContext? context) =>
        StandardMetricNames;
}
