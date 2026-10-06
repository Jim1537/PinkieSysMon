using System.Text.Json.Serialization;

namespace PinkieSysMon.DashboardModel;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(ValueWidgetDefinition), global::PinkieSysMon.WidgetTypeContract.Value)]
[JsonDerivedType(typeof(BinaryWidgetDefinition), global::PinkieSysMon.WidgetTypeContract.Binary)]
[JsonDerivedType(typeof(GaugeWidgetDefinition), global::PinkieSysMon.WidgetTypeContract.Gauge)]
[JsonDerivedType(typeof(BarWidgetDefinition), global::PinkieSysMon.WidgetTypeContract.Bar)]
[JsonDerivedType(typeof(ImageWidgetDefinition), global::PinkieSysMon.WidgetTypeContract.Image)]
[JsonDerivedType(typeof(PowerWidgetDefinition), global::PinkieSysMon.WidgetTypeContract.Power)]
[JsonDerivedType(typeof(MediaSystemWidgetDefinition), global::PinkieSysMon.WidgetTypeContract.MediaSystem)]
[JsonDerivedType(typeof(MediaPlayerWidgetDefinition), global::PinkieSysMon.WidgetTypeContract.MediaPlayer)]
internal abstract class WidgetDefinition
{
    [JsonIgnore]
    public abstract string Type { get; }

    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("z")]
    public int Z { get; set; }

    [JsonPropertyName("x")]
    public float X { get; set; }

    [JsonPropertyName("y")]
    public float Y { get; set; }

    [JsonPropertyName("width")]
    public float Width { get; set; }

    [JsonPropertyName("height")]
    public float Height { get; set; }

    [JsonPropertyName("rotation")]
    public double Rotation { get; set; }

    [JsonPropertyName("color")]
    public string Color { get; set; } = "#FFFFFFFF";

    [JsonPropertyName("backgroundColor")]
    public string BackgroundColor { get; set; } = "#00000000";

    [JsonPropertyName("borderColor")]
    public string BorderColor { get; set; } = "#00000000";

    [JsonPropertyName("borderWidth")]
    public float BorderWidth { get; set; }

    [JsonPropertyName("cornerRadius")]
    public float CornerRadius { get; set; }

    [JsonPropertyName("shadowEnabled")]
    public bool ShadowEnabled { get; set; }

    [JsonPropertyName("shadowOffsetX")]
    public float ShadowOffsetX { get; set; } = 2f;

    [JsonPropertyName("shadowOffsetY")]
    public float ShadowOffsetY { get; set; } = 2f;

    [JsonPropertyName("shadowBlur")]
    public float ShadowBlur { get; set; } = 4f;

    [JsonPropertyName("shadowOpacity")]
    public float ShadowOpacity { get; set; } = 0.5f;

    [JsonPropertyName("shadowColor")]
    public string ShadowColor { get; set; } = "#FF000000";

    internal virtual void Validate(string dashboardBaseDirectory)
    {
        RequireFinite(X, nameof(X));
        RequireFinite(Y, nameof(Y));
        RequireFinite(Width, nameof(Width));
        RequireFinite(Height, nameof(Height));
        RequireFinite(Rotation, nameof(Rotation));
        RequireFinite(BorderWidth, nameof(BorderWidth));
        RequireFinite(CornerRadius, nameof(CornerRadius));
        RequireFinite(ShadowOffsetX, nameof(ShadowOffsetX));
        RequireFinite(ShadowOffsetY, nameof(ShadowOffsetY));
        RequireFinite(ShadowBlur, nameof(ShadowBlur));
        RequireFinite(ShadowOpacity, nameof(ShadowOpacity));

        if (string.IsNullOrWhiteSpace(Id))
            throw new InvalidDataException($"Dashboard widget of type '{Type}' requires a non-empty Id.");
        if (BorderWidth < 0f)
            throw new InvalidDataException($"Widget '{Id}' BorderWidth cannot be negative.");
        if (CornerRadius < 0f)
            throw new InvalidDataException($"Widget '{Id}' CornerRadius cannot be negative.");
        if (ShadowBlur < 0f)
            throw new InvalidDataException($"Widget '{Id}' ShadowBlur cannot be negative.");
        if (ShadowOpacity is < 0f or > 1f)
            throw new InvalidDataException($"Widget '{Id}' ShadowOpacity must be in the range 0..1.");

        _ = global::PinkieSysMon.ColorParser.Parse(Color);
        _ = global::PinkieSysMon.ColorParser.Parse(BackgroundColor);
        _ = global::PinkieSysMon.ColorParser.Parse(BorderColor);
        _ = global::PinkieSysMon.ColorParser.Parse(ShadowColor);
    }

    protected void RequireMetric(string? metric)
    {
        if (string.IsNullOrWhiteSpace(metric))
            throw new InvalidDataException($"{Type} widget '{Id}' requires Metric.");
    }

    protected void ValidatePositiveGeometry()
    {
        if (Width <= 0f || Height <= 0f)
            throw new InvalidDataException($"{Type} widget '{Id}' requires Width > 0 and Height > 0.");
    }

    protected static void RequireFinite(float value, string propertyName)
    {
        if (!float.IsFinite(value))
            throw new InvalidDataException($"{propertyName} must be a finite number.");
    }

    protected static void RequireFinite(double value, string propertyName)
    {
        if (!double.IsFinite(value))
            throw new InvalidDataException($"{propertyName} must be a finite number.");
    }
}

internal abstract class StateVisualWidgetDefinition : WidgetDefinition
{
    [JsonPropertyName("profiles")]
    public Dictionary<string, StateVisualProfileDefinition> Profiles { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    protected void ValidateProfiles(string dashboardBaseDirectory)
    {
        if (Profiles is null)
            throw new InvalidDataException($"{Type} widget '{Id}' requires state profiles.");

        var specs = global::PinkieSysMon.StateVisualProfileContract.GetSpecs(Type);
        var allowed = new HashSet<string>(specs.Select(spec => spec.Key), StringComparer.OrdinalIgnoreCase);
        var unknown = Profiles.Keys.FirstOrDefault(key => !allowed.Contains(key));
        if (unknown is not null)
            throw new InvalidDataException($"Widget '{Id}' contains unsupported state visual profile '{unknown}'.");

        foreach (var spec in specs)
        {
            if (!Profiles.TryGetValue(spec.Key, out var profile) || profile is null)
                throw new InvalidDataException($"Widget '{Id}' requires profile '{spec.Key}'.");
            profile.Validate(dashboardBaseDirectory, $"Widget '{Id}' {spec.Category}");
        }

        var valueOnly = specs.Count > 0 && specs.All(spec => Profiles[spec.Key].UsesValueContent);
        if (valueOnly)
        {
            if (Width < 0f || Height < 0f)
                throw new InvalidDataException($"Value-only {Type} widget '{Id}' cannot have negative Width or Height.");
        }
        else
        {
            ValidatePositiveGeometry();
        }
    }
}

internal abstract class QuantitativeWidgetDefinition : WidgetDefinition
{
    [JsonPropertyName("metric")]
    public string? Metric { get; set; }

    [JsonPropertyName("unit")]
    public string? Unit { get; set; }

    [JsonPropertyName("min")]
    public double Min { get; set; }

    [JsonPropertyName("max")]
    public double Max { get; set; } = 100d;

    [JsonPropertyName("gap")]
    public float Gap { get; set; }

    [JsonPropertyName("reverse")]
    public bool Reverse { get; set; }

    [JsonPropertyName("thresholds")]
    public ThresholdSetDefinition Thresholds { get; set; } = new();

    protected void ValidateQuantitative(bool validateThresholds)
    {
        RequireMetric(Metric);
        RequireFinite(Min, nameof(Min));
        RequireFinite(Max, nameof(Max));
        RequireFinite(Gap, nameof(Gap));
        if (Max <= Min)
            throw new InvalidDataException($"{Type} widget '{Id}' requires Max > Min.");
        if (Gap < 0f)
            throw new InvalidDataException($"{Type} widget '{Id}' Gap cannot be negative.");
        if (Thresholds is null)
            throw new InvalidDataException($"{Type} widget '{Id}' Thresholds must not be null.");
        if (validateThresholds)
            Thresholds.Validate(Min, Max, $"{Type} widget '{Id}' Thresholds");
    }
}

internal sealed class ValueWidgetDefinition : WidgetDefinition
{
    [JsonIgnore]
    public override string Type => global::PinkieSysMon.WidgetTypeContract.Value;

    [JsonPropertyName("sourceKind")]
    public string SourceKind { get; set; } = ValueSourceKind.Metric;

    [JsonPropertyName("metric")]
    public string? Metric { get; set; }

    [JsonPropertyName("text")]
    public string Text { get; set; } = string.Empty;

    [JsonPropertyName("sourceUnit")]
    public string? SourceUnit { get; set; }

    [JsonPropertyName("unit")]
    public string? Unit { get; set; }

    [JsonPropertyName("format")]
    public string? Format { get; set; }

    [JsonPropertyName("prefix")]
    public string Prefix { get; set; } = string.Empty;

    [JsonPropertyName("suffix")]
    public string Suffix { get; set; } = string.Empty;

    [JsonPropertyName("fallback")]
    public string Fallback { get; set; } = "--";

    [JsonPropertyName("textPresentation")]
    public TextPresentationDefinition TextPresentation { get; set; } = new();

    internal override void Validate(string dashboardBaseDirectory)
    {
        base.Validate(dashboardBaseDirectory);
        if (!ValueSourceKind.IsMetric(SourceKind) && !ValueSourceKind.IsText(SourceKind))
            throw new InvalidDataException($"Value widget '{Id}' has invalid SourceKind '{SourceKind}'.");
        if (ValueSourceKind.IsMetric(SourceKind))
            RequireMetric(Metric);
        if (Width < 0f || Height < 0f)
            throw new InvalidDataException($"Value widget '{Id}' Width and Height must be >= 0.");
        if (TextPresentation is null)
            throw new InvalidDataException($"Value widget '{Id}' TextPresentation must not be null.");

        TextPresentation.Validate($"Value widget '{Id}' TextPresentation");
        var overflow = global::PinkieSysMon.ValueOverflowContract.Normalize(TextPresentation.OverflowMode);
        if (Width <= 0f && overflow != global::PinkieSysMon.ValueOverflowContract.None)
            throw new InvalidDataException($"Value widget '{Id}' must use OverflowMode=None when Width=0.");
        if (Width > 0f && overflow == global::PinkieSysMon.ValueOverflowContract.None)
            throw new InvalidDataException($"Value widget '{Id}' cannot use OverflowMode=None with a fixed Width.");
    }
}

internal sealed class BinaryWidgetDefinition : StateVisualWidgetDefinition
{
    [JsonIgnore]
    public override string Type => global::PinkieSysMon.WidgetTypeContract.Binary;

    [JsonPropertyName("metric")]
    public string? Metric { get; set; }

    [JsonPropertyName("unit")]
    public string? Unit { get; set; }

    [JsonPropertyName("format")]
    public string? Format { get; set; }

    [JsonPropertyName("prefix")]
    public string Prefix { get; set; } = string.Empty;

    [JsonPropertyName("suffix")]
    public string Suffix { get; set; } = string.Empty;

    [JsonPropertyName("fallback")]
    public string Fallback { get; set; } = "--";

    [JsonPropertyName("evaluationMode")]
    public string EvaluationMode { get; set; } = global::PinkieSysMon.BinarySignalContract.EvaluationModeAuto;

    [JsonPropertyName("setpoint")]
    public string Setpoint { get; set; } = string.Empty;

    [JsonPropertyName("trueIf")]
    public string TrueIf { get; set; } = global::PinkieSysMon.BinarySignalContract.TrueIfGreaterThan;

    internal override void Validate(string dashboardBaseDirectory)
    {
        base.Validate(dashboardBaseDirectory);
        RequireMetric(Metric);
        if (!global::PinkieSysMon.BinarySignalContract.SupportedEvaluationModes.Contains(EvaluationMode, StringComparer.OrdinalIgnoreCase))
            throw new InvalidDataException($"Binary widget '{Id}' has invalid EvaluationMode '{EvaluationMode}'.");
        if (!global::PinkieSysMon.BinarySignalContract.SupportedTrueIfOperators.Contains(TrueIf, StringComparer.Ordinal))
            throw new InvalidDataException($"Binary widget '{Id}' has invalid TrueIf operator '{TrueIf}'.");
        ValidateProfiles(dashboardBaseDirectory);
    }
}

internal sealed class GaugeWidgetDefinition : QuantitativeWidgetDefinition
{
    [JsonIgnore]
    public override string Type => global::PinkieSysMon.WidgetTypeContract.Gauge;

    [JsonPropertyName("startAngle")]
    public float StartAngle { get; set; } = 30f;

    [JsonPropertyName("endAngle")]
    public float EndAngle { get; set; } = 330f;

    [JsonPropertyName("track")]
    public GaugeTrackDefinition Track { get; set; } = new();

    [JsonPropertyName("needle")]
    public GaugeNeedleDefinition Needle { get; set; } = new();

    internal override void Validate(string dashboardBaseDirectory)
    {
        base.Validate(dashboardBaseDirectory);
        ValidateQuantitative(validateThresholds: true);
        ValidatePositiveGeometry();
        if (Math.Abs(Height - Width) > 0.0001f)
            throw new InvalidDataException($"Gauge widget '{Id}' Height is derived and must equal Width.");
        if (!float.IsFinite(StartAngle) || !float.IsFinite(EndAngle) || StartAngle < 0f || EndAngle > 360f || StartAngle >= EndAngle)
            throw new InvalidDataException($"Gauge widget '{Id}' requires 0 <= StartAngle < EndAngle <= 360.");
        if (Track is null)
            throw new InvalidDataException($"Gauge widget '{Id}' Track must not be null.");
        if (Needle is null)
            throw new InvalidDataException($"Gauge widget '{Id}' Needle must not be null.");

        Track.Validate($"Gauge widget '{Id}' Track");
        var availableRadius = Width / 2f;
        if (Track.Enabled)
        {
            var radialDepth = 2f * Track.BorderWidth + 2f * Gap + Track.Thickness;
            if (!float.IsFinite(radialDepth) || radialDepth > availableRadius)
                throw new InvalidDataException($"Gauge widget '{Id}' track geometry must fit inside Width/2.");
        }
        Needle.Validate(availableRadius, $"Gauge widget '{Id}' Needle");
    }
}

internal sealed class BarWidgetDefinition : QuantitativeWidgetDefinition
{
    [JsonIgnore]
    public override string Type => global::PinkieSysMon.WidgetTypeContract.Bar;

    [JsonPropertyName("contentMode")]
    public string ContentMode { get; set; } = global::PinkieSysMon.BarImageContract.ContentModeFill;

    [JsonPropertyName("image")]
    public BarImagePresentationDefinition Image { get; set; } = new();

    internal override void Validate(string dashboardBaseDirectory)
    {
        base.Validate(dashboardBaseDirectory);
        ValidatePositiveGeometry();
        if (!global::PinkieSysMon.BarImageContract.SupportedContentModes.Contains(ContentMode, StringComparer.OrdinalIgnoreCase))
            throw new InvalidDataException($"Bar widget '{Id}' has invalid ContentMode '{ContentMode}'.");
        if (Image is null)
            throw new InvalidDataException($"Bar widget '{Id}' Image must not be null.");

        Image.ValidateStructure($"Bar widget '{Id}' Image");
        var imageMode = global::PinkieSysMon.BarImageContract.IsImageMode(ContentMode);
        ValidateQuantitative(validateThresholds: !imageMode);
        if (imageMode)
            Image.ValidateActive(dashboardBaseDirectory, $"Bar widget '{Id}' Image");
    }
}

internal sealed class ImageWidgetDefinition : WidgetDefinition
{
    [JsonIgnore]
    public override string Type => global::PinkieSysMon.WidgetTypeContract.Image;

    [JsonPropertyName("asset")]
    public ImageAssetPresentationDefinition Asset { get; set; } = new();

    [JsonPropertyName("opacity")]
    public float Opacity { get; set; } = 1f;

    internal override void Validate(string dashboardBaseDirectory)
    {
        base.Validate(dashboardBaseDirectory);
        ValidatePositiveGeometry();
        if (!float.IsFinite(Opacity) || Opacity is < 0f or > 1f)
            throw new InvalidDataException($"Image widget '{Id}' Opacity must be in the range 0..1.");
        if (Asset is null)
            throw new InvalidDataException($"Image widget '{Id}' Asset must not be null.");
        Asset.ValidateActiveSource(dashboardBaseDirectory, $"Image widget '{Id}' Asset");
    }
}

internal sealed class PowerWidgetDefinition : StateVisualWidgetDefinition
{
    [JsonIgnore]
    public override string Type => global::PinkieSysMon.WidgetTypeContract.Power;

    [JsonPropertyName("powerSource")]
    public string PowerSource { get; set; } = global::PinkieSysMon.PowerMetricContract.UpsSource;

    internal override void Validate(string dashboardBaseDirectory)
    {
        base.Validate(dashboardBaseDirectory);
        if (!global::PinkieSysMon.PowerMetricContract.IsSupportedSource(PowerSource))
            throw new InvalidDataException($"Power widget '{Id}' PowerSource must be power.ups or power.battery.");
        ValidateProfiles(dashboardBaseDirectory);
    }
}

internal sealed class MediaSystemWidgetDefinition : StateVisualWidgetDefinition
{
    [JsonIgnore]
    public override string Type => global::PinkieSysMon.WidgetTypeContract.MediaSystem;

    [JsonPropertyName("mediaSource")]
    public string MediaSource { get; set; } = global::PinkieSysMon.MediaMetricContract.OutputSource;

    internal override void Validate(string dashboardBaseDirectory)
    {
        base.Validate(dashboardBaseDirectory);
        if (MediaSource is not (global::PinkieSysMon.MediaMetricContract.OutputSource or global::PinkieSysMon.MediaMetricContract.InputSource))
            throw new InvalidDataException($"Media System widget '{Id}' MediaSource must be media.output or media.input.");
        ValidateProfiles(dashboardBaseDirectory);
    }
}

internal sealed class MediaPlayerWidgetDefinition : StateVisualWidgetDefinition
{
    [JsonIgnore]
    public override string Type => global::PinkieSysMon.WidgetTypeContract.MediaPlayer;

    internal override void Validate(string dashboardBaseDirectory)
    {
        base.Validate(dashboardBaseDirectory);
        ValidateProfiles(dashboardBaseDirectory);
    }
}
