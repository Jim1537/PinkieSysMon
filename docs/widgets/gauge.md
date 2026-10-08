# Gauge

The **Gauge** widget displays a numeric telemetry metric as a circular or horseshoe-shaped indicator. The current value is represented by a filled portion of the track and, optionally, a needle.

Gauge is suitable for temperatures, utilization, fan speeds, frequencies, battery charge, and other numeric readings that can be mapped to a defined range. It supports adjustable arc geometry, colored thresholds, a separate track background and border, and a configurable needle with an optional pointer section.

Gauge does not display the numeric reading as text. To show a number or a label alongside it, add a separate [Text / Value](text-value.md) widget.

---
## User Guide

### Step 1. Add the widget

Open a dashboard in **PinkieSysMon Dashboard Editor** and choose `Gauge` from `Add Widget` or the `Add widget` toolbar menu.

Select the new widget on the Canvas to edit its properties.

!!! info
    A new Gauge starts at 200 × 200, with `Metric = system.runtime.fps`, `Min = 0`, `Max = 100`, `Start Angle = 30`, and `End Angle = 330`.

    Its track is enabled with a thickness of 20 and a translucent background. The needle is disabled by default. These are starting values, not requirements for your own dashboard.


### Step 2. Select a numeric metric

Open `General → Source → Metric` and choose a numeric telemetry metric.

You can use a supported metric from [System](../telemetry/system.md), [Libre Hardware Monitor](../telemetry/libre-hardware-monitor.md), or [iCUE Sensor Logging](../telemetry/icue-sensor-logging.md), provided that the corresponding provider is enabled and the metric is available.

Gauge expects a number. Text, Boolean, or missing metric values that cannot be converted into a finite numeric reading are not usable for its active indication.

See [Metric](properties.md#metric).

### Step 3. Set the unit and range

Open the `Data` tab.

Under `Value`:

- `Source Unit` shows the unit supplied by the metric contract. It is read-only.
- `Unit` selects a supported numeric conversion, when the metric has compatible units.

Under `Range`:

- `Min` is the value represented by the start of the scale.
- `Max` is the value represented by the end of the scale.

`Max` must be greater than `Min`. The displayed progress is proportional to the reading within that range. Values below Min appear at the empty end; values above Max appear at the full end.

!!! warning
    The range and threshold numbers must be understood in the selected `Unit`. For example, if a temperature metric is converted to Fahrenheit, configure `Min`, `Max`, and threshold values in Fahrenheit as well.

    `Unit` changes numeric interpretation; it does not add a unit label to the gauge. Use Text / Value for a visible number or unit symbol.


See [Source Unit](properties.md#source-unit), [Unit](properties.md#unit), [Min](properties.md#min), and [Max](properties.md#max).

### Step 4. Configure the horseshoe

Open `Gauge → Horseshoe`.

The primary settings are:

| Property | Purpose |
| --- | --- |
| `Enabled` | Turns the track on or off. |
| `Start Angle` / `End Angle` | Select the arc's start and end positions. |
| `Reverse` | Reverses the direction in which the indicator fills. |
| `Thickness` | Sets the width of the colored track band. |
| `Background Color` | Sets the unfilled track color. |
| `Border Width` / `Border Color` | Control the track's separate outline. |
| `Corner Radius` | Controls rounding at the ends of an open arc. |
| `Gap` | Controls the track's inset from its surrounding border envelope. |

The usual horseshoe shape leaves a gap at the bottom. The default 30° to 330° arc spans 300°; a complete circle uses 0° to 360°.

The angles must satisfy:

  `0 <= Start Angle < End Angle <= 360`

When `Reverse` is enabled, the same arc is used, but the active fill progresses from the opposite end. If a needle is enabled, its movement is reversed too.

!!! info
    A track can be disabled while leaving the needle enabled. This produces a needle-only indicator. Conversely, the needle can be disabled to use only the colored arc.

    Track thickness, border width, and gap must fit inside the available gauge radius. If they do not, adjust the values or increase the gauge size.


See [Track Enabled](properties.md#track-enabled), [Start Angle](properties.md#start-angle), [End Angle](properties.md#end-angle), [Reverse](properties.md#reverse), [Track Thickness](properties.md#track-thickness), and [Gap](properties.md#gap).

### Step 5. Set up the needle (optional)

Open `Gauge → Needle` and turn on `Enabled`.

The needle follows the same Min/Max range and arc angles as the track. Its settings include thickness and color, offsets from the center and outer edge, and an optional pointer section at the outer end.

| Property | Meaning |
| --- | --- |
| `Thickness` / `Color` | Appearance of the main needle. |
| `Start Offset` | How far the needle begins from the center. |
| `End Offset` | How far its tip is inset from the gauge's outer radius. |
| `Pointer Length` | Length of the differently styled outer needle section; zero disables that section. |
| `Pointer Thickness` / `Pointer Color` | Appearance of the outer pointer section. |

The pointer uses the outer portion of the needle, rather than adding extra length beyond its tip. Its effective length is limited to the visible needle length.

!!! warning
    With the needle enabled, `Start Offset` and `End Offset` must leave a positive visible length. A needle is drawn only when the selected metric has a usable numeric value.


See [Needle Enabled](properties.md#needle-enabled), [Needle Start Offset](properties.md#needle-start-offset), [Needle End Offset](properties.md#needle-end-offset), and [Pointer Length](properties.md#pointer-length).

### Step 6. Configure threshold colors (optional)

Open `Appearance → Thresholds`.

Gauge supports **three configurable threshold slots**. Each slot has an `Enabled` setting, a numeric `Value`, and a `Color`. Enabled thresholds must be arranged in ascending numeric order and lie within Min..Max.

`Threshold Mode` controls how the threshold colors appear:

| Mode | Result |
| --- | --- |
| `SegmentSolid` | The arc is divided into solid-color ranges. Each range uses the color assigned at its starting boundary. |
| `SegmentTransition` | Colors blend along the scale between the base color and enabled threshold colors. |
| `State` | The entire active portion of the arc uses the color corresponding to the latest crossed threshold. |

The `Appearance → Foreground → Color` property supplies the base color before the first enabled threshold.

For example, with `Min = 0`, `Max = 100`, and thresholds at 60 (yellow) and 85 (red):

- With `SegmentSolid`, the relevant portions of the active arc are painted in the base, yellow, and red colors according to their positions on the scale.
- With `SegmentTransition`, the scale blends between the configured colors.
- With `State`, the whole active portion remains the base color below 60, changes to yellow at 60, and changes to red at 85.

!!! info
    Thresholds color the **active fill**, not the unfilled track background. They do not change the needle color; its appearance is configured independently in `Gauge → Needle`.

    An enabled threshold is triggered at its boundary value in `State` mode.


See [Threshold Mode](properties.md#threshold-mode), [Threshold Enabled](properties.md#threshold-enabled), [Threshold Value](properties.md#threshold-value), and [Threshold Color](properties.md#threshold-color).

### Step 7. Adjust size and general appearance

Under `General → Geometry`, set the position and `Width`, or resize the gauge directly on the Canvas.

Gauge is always **square**. `Height` is derived from `Width` and is not independently editable. Rotation can be applied through the shared `Rotation` property.

The `Appearance` tab also provides widget-wide background, border, shadow, and foreground color settings. These are distinct from the track's own background and border and from the needle's colors.

See [Width](properties.md#width), [Height](properties.md#height), [Rotation](properties.md#rotation), and [Widget Properties — Tabs](properties-by-tab.md).

### Step 8. Understand unavailable values

If a metric is missing, `null`, non-numeric, or cannot be converted into a valid numeric value, Gauge does not invent a reading.

When the track is enabled, its configured background and border may remain visible, but the value-dependent active fill is not drawn. An enabled needle is also not drawn until a usable number is available.

A zero reading and an unavailable reading are not equivalent: zero is a valid number that maps to the scale; unavailable does not have a position on the scale.

### Usage Examples

These examples describe property combinations, not bundled dashboard presets.

| Scenario | Suggested setup | Effect |
| --- | --- | --- |
| CPU temperature | Choose a CPU temperature metric; use Celsius; Min=20, Max=100; enable thresholds at 70 and 85 | Displays temperature within a meaningful band, with warning colors at higher readings. |
| GPU utilization | Select a numeric GPU load metric; Min=0, Max=100; track enabled; needle optional | Shows load as a percentage of the configured scale. |
| Fan speed | Choose a fan RPM metric; Min=0 and Max matching the expected fan range | Displays RPM proportionally without assuming the fan has a universal maximum speed. |
| Classic dial | Track enabled, Needle enabled; adjust start/end angles and offsets | Combines an arc and moving needle. |
| Minimal arc | Track enabled, Needle disabled; transparent track border | Produces a compact fill-only indicator. |
| Needle-only instrument | Track disabled, Needle enabled | Shows the current position on the arc without a visible track. |
| Colored warning arc | Threshold Mode=State, with two ascending warning thresholds | Uses one color for the active portion, based on the current value band. |

---
## Technical Information

### Identity and Architecture

Gauge is a quantitative dashboard widget using the canonical dashboard model and the shared SkiaSharp rendering path.

- Persisted type discriminator: `gauge`
- Model: `DashboardModel.GaugeWidgetDefinition`
- Base model: `DashboardModel.QuantitativeWidgetDefinition`
- Renderer: `Widgets.GaugeWidgetRenderer`
- Quantitative helper: `Widgets.CompiledQuantitativeIndicator`
- Metric conversion: `Widgets.WidgetRenderContext.TryGetMetricDouble` / `MetricValueConverter`

The renderer receives the current metric snapshot through `WidgetRenderContext`. It does not poll Windows, LHM, iCUE, or the hardware device on its own.

Gauge uses the same metric-conversion and quantitative threshold contracts as other quantitative widgets where those contracts apply. Its arc, track, and needle geometry is Gauge-specific.

### Canonical Model and JSON Fields

The Gauge model inherits common widget geometry and appearance fields including `id`, `name`, `z`, `x`, `y`, `width`, `height`, `rotation`, foreground `color`, background, border, and shadow settings.

Gauge-specific and quantitative JSON fields:

| JSON field | Canonical member | Contract |
| --- | --- | --- |
| `type` | Discriminator | `gauge`. |
| `metric` | `Metric` | Required metric identifier. |
| `unit` | `Unit` | Optional numeric unit conversion. |
| `min` / `max` | `Min` / `Max` | Scale boundaries; Max must exceed Min. |
| `reverse` | `Reverse` | Reverses progression along the arc. |
| `gap` | `Gap` | Track inset spacing. |
| `thresholds` | `Thresholds` | Threshold mode and exactly three slots. |
| `startAngle` / `endAngle` | `StartAngle` / `EndAngle` | Arc limits in degrees. |
| `track` | `GaugeTrackDefinition` | Track geometry and styling. |
| `needle` | `GaugeNeedleDefinition` | Needle, offsets, and pointer. |

`track` contains `enabled`, `thickness`, `backgroundColor`, `borderColor`, `borderWidth`, and `cornerRadius`.

`needle` contains `enabled`, `thickness`, `color`, `startOffset`, `endOffset`, and a nested `pointer` object with `length`, `thickness`, and `color`.

`thresholds` contains `mode` and `items`. Each item contains `enabled`, `value`, and `color`.

Property names are part of the persisted dashboard contract; Editor labels and stored JSON members are not interchangeable.

### Metric Resolution and Normalization

`GaugeWidgetRenderer.Render` requests the configured metric through `context.TryGetMetricDouble(gauge, out raw)`.

The conversion path:

- looks up the metric value in the current snapshot;
- uses `MetricContract` to identify recognized numeric metric types and their native units;
- applies the widget's selected `Unit` through `MetricValueConverter`;
- rejects missing values and unsuccessful or non-finite conversions.

When a metric descriptor is absent or not numeric, a raw value can be used only if no conversion unit is requested and it can be converted into a finite number.

The renderer does not preserve a previous valid reading as an implicit fallback when the current reading becomes unavailable.

For a valid numeric value `v`, normalized position is:

  `ratio = clamp((v - Min) / (Max - Min), 0, 1)`

This ratio controls fill length and needle angle. Values outside the range are visually clamped, not automatically rescaled. The original converted value is still available to threshold state-color selection.

### Arc Geometry and Direction

Gauge is a square instrument: `Height = Width` and the renderer uses a center at `(Width/2, Width/2)` with `OuterRadius = Width/2` in local coordinates.

The arc contract is:

  `0 <= StartAngle < EndAngle <= 360`

Its sweep is `EndAngle - StartAngle`. The canonical angle is converted to the Skia rendering angle by adding 90 degrees; in the rendered coordinate system, 0° is at the bottom. The default 30°–330° range produces an open horseshoe, while 0°–360° produces a full circle.

The renderer computes an active sweep proportional to the normalized metric ratio.

- Normal progression fills from the configured start toward the end.
- `Reverse` fills from the end toward the start.
- The needle angle follows the same direction choice and ratio.

A full circle uses a separate closed-ring path. End-cap corner rounding does not apply in that case.

### Track Rendering

When `Track.Enabled` is true, the renderer can draw:

- the inactive track background;
- the value-dependent active track fill;
- the track border.

The track's geometry is based on `Width`, `Track.BorderWidth`, `Gap`, and `Track.Thickness`. The outer and inner fill radii are determined independently of the needle geometry.

The required radial fit constraint, when the track is enabled, is:

  `2 * Track.BorderWidth + 2 * Gap + Track.Thickness <= Width / 2`

The track background and border are not automatically colored by the current threshold state. The active fill uses the threshold mode and base foreground color.

The track's `CornerRadius` is used for rounded end geometry on open arcs, with effective rounding limited by available band geometry. The full-circle path has no rounded open ends.

Disabling the track suppresses its background, fill, and border. It does not disable the needle.

### Threshold Modes and Color Evaluation

Gauge compiles enabled thresholds with `CompiledQuantitativeIndicator`. Its base color is the widget's foreground `color`.

The persisted `ThresholdSetDefinition` has exactly three slots. Enabled thresholds must lie within Min..Max and must appear in ascending numeric order. Disabled slots do not create active color boundaries.

Supported modes:

| Mode | Rendering contract |
| --- | --- |
| `SegmentSolid` | Constructs fixed color segments from Min through enabled thresholds to Max; only the part covered by the active fill is drawn. |
| `SegmentTransition` | Constructs a sweep gradient through the base and threshold colors and clips it to the active sweep. |
| `State` | Uses a single active-fill color: the base color until the first crossed threshold, then the color of the last crossed threshold. |

For `State`, a threshold at value `t` is considered crossed when `v >= t`. The selected color is applied to the entire active arc, not only to its highest-value segment.

For `SegmentSolid` and `SegmentTransition`, colors are associated with fixed positions on the scale; the current reading determines how much of that colored scale is visible.

`Reverse` reverses the segment positions and gradient orientation consistently with fill progression.

The needle has its own explicit color fields and is not recolored by threshold mode.

### Needle and Pointer Rendering

The needle is independently enabled. Its default state is disabled.

When enabled and a valid numeric reading exists, it uses the normalized ratio to compute its angular position.

Radial construction:

- `startRadius = Needle.StartOffset`
- `endRadius = OuterRadius - Needle.EndOffset`
- `visibleLength = endRadius - startRadius`
- `pointerLength = clamp(Needle.Pointer.Length, 0, visibleLength)`

If pointer length is zero, the renderer draws an ordinary needle line. If pointer length is positive, the outer portion may be rendered with a separate pointer color and thickness while the inner portion uses the main needle style.

The pointer extends to `endRadius`, not beyond the instrument's outer radius. The offset constraint must leave positive needle length whenever the needle is enabled.

A missing numeric value prevents needle drawing, even if `Needle.Enabled` is true.

### Unavailable Values

The value-dependent drawing code is guarded by `hasMetric`.

When numeric metric resolution fails:

- the active fill is not drawn;
- the needle is not drawn;
- enabled track background and border can still be drawn;
- any common widget-level background/border remains governed by the shared presentation path.

This represents an unavailable metric. It must not be conflated with a valid value equal to Min, which produces a zero-length active fill but is still a valid numeric observation.

Gauge has no separate text fallback or dedicated Unavailable state profile. A standalone text status or numeric value requires another widget.

### Editor Integration

The Editor exposes properties through `CanonicalPropertyViewBuilder`:

- `General → Source → Metric` and geometry;
- `Data → Value` for read-only Source Unit and selectable numeric Unit;
- `Data → Range` for Min and Max;
- `Appearance → Foreground`, common background/border/shadow, and Thresholds;
- `Gauge → Horseshoe` for track, angles, reverse, and gap;
- `Gauge → Needle` for needle and pointer controls.

Gauge Height is a derived, read-only property. `WidgetSizeContract` maintains the square geometry during Editor adjustments.

Properties of an inactive track or needle remain in the canonical model. Their associated Editor controls can be disabled while the corresponding `Enabled` toggle is off.

`Data → Unit` uses numeric options from the selected metric descriptor; it is not a text-formatting or label-placement option.

For individual property contracts, consult [Widget Properties — Tabs](properties-by-tab.md) and [Widget Property Dictionary](properties.md).

### Rendering Resources and Cache Invalidation

`GaugeWidgetRenderer` compiles fixed geometry and paints into a per-widget `CompiledGauge` cache. This includes arc paths, colors, threshold-mode resources, and optional needle paints.

`Render` uses the compiled geometry together with the current value snapshot. `InvalidateGeometry` removes a compiled entry and disposes its resources so the next render can rebuild them.

The renderer uses SkiaSharp for paths, fills, gradient shaders, clipping, and strokes. Its compiled geometry is separate from the value-dependent drawing operation.

The Editor and Runtime use the shared canonical model and rendering semantics. Static source inspection is not a substitute for visual or physical-device acceptance.

### Validation and Compatibility Contracts

Canonical Gauge validation requires:

- a non-empty metric ID;
- finite geometry, with positive Width and Height and `Height = Width`;
- finite Min and Max, with `Max > Min`;
- finite non-negative Gap;
- `0 <= StartAngle < EndAngle <= 360`;
- a valid non-null track and needle;
- valid track thickness, border, and color fields;
- the radial fit constraint when the track is enabled;
- valid needle offsets and pointer geometry;
- a supported threshold mode and exactly three threshold slots;
- finite, ordered, in-range enabled threshold values and valid colors.

The following behavior is compatibility-sensitive:

- persisted type remains `gauge`;
- Height remains derived from Width;
- unit conversion precedes range normalization and threshold evaluation;
- values outside Min..Max are clamped for geometric progression;
- Reverse affects progression, segments, gradients, and needle angle coherently;
- track and needle enablement remain independent;
- unavailable metrics do not generate an invented fill or needle position;
- threshold color modes retain their distinct semantics;
- the pointer remains a portion of the needle, not a separate extension past its tip;
- rendering semantics are shared between Editor preview and Runtime.

Changes to persisted Gauge fields, range/threshold interpretation, arc geometry, conversion, or unavailable behavior require compatibility review and deterministic regression coverage where reliably testable.

Source: [Pinkie's System Monitor on GitHub](https://github.com/Jim1537/PinkieSysMon).
