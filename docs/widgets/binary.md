# Binary

The **Binary** widget turns a selected telemetry metric into one of two logical states, **True** or **False**, and displays the visual representation configured for that state. It can be used for connectivity indicators, microphone activity, status signals, and threshold-based warnings.

Each state has its own visual profile. A profile can show an icon, an image, or the current metric value as text. The two profiles can use different presentation types.

Binary evaluates a metric; it is not a manually operated switch and does not control the source hardware.

---
## User Guide

### Step 1. Add the widget

Open a dashboard in **PinkieSysMon Dashboard Editor**, then use `Add Widget` or the `Add widget` toolbar button and choose `Binary`.

Select the widget on the Canvas to display its settings in the Properties panel.

!!! info
    A new Binary widget initially uses the `system.media.input.active` metric with `Mode = Auto`. Its default size is 96 × 96.

    The default `True` and `False` profiles both use a check icon. The `False` profile is displayed at lower opacity, so the two states have visibly different presentations.


### Step 2. Select the metric

On the `General` tab, open the `Source` group and select `Metric`.

Binary always uses a telemetry metric. Unlike [Text / Value](text-value.md), it has no independent `Text` source mode.

You can select a metric from an enabled provider, including [System](../telemetry/system.md), [Libre Hardware Monitor](../telemetry/libre-hardware-monitor.md), or [iCUE Sensor Logging](../telemetry/icue-sensor-logging.md).

!!! info
    Binary can work with Boolean, numeric, and text metrics. The metric's value type matters because it determines how `Auto` and `Setpoint` evaluate it.

    The selected provider must be supplying a usable current value for Binary to select a visual state.


See [Metric](properties.md#metric).

### Step 3. Choose how True and False are determined

Open `Data → Evaluation` and configure `Mode`.

| Mode | What it does |
| --- | --- |
| `Auto` | Interprets the metric value directly as True or False. |
| `Setpoint` | Compares the metric against a reference value to determine True or False. |

#### Auto

`Auto` is the simplest option for a Boolean metric such as `system.network.internet.connected` or `system.media.input.active`.

The current evaluation rules are:

| Incoming value | Evaluation |
| --- | --- |
| Boolean `true` | True |
| Boolean `false` | False |
| Numeric zero | False |
| Finite numeric value other than zero | True |
| Empty string | False |
| Non-empty string | True, except for reserved unavailable strings |
| Missing, `null`, or unsupported value | No state can be selected |

!!! warning
    `Auto` does not interpret the text `false` as Boolean false. A non-empty text value such as `false` is treated as True. Use `Setpoint` when you want to match a particular string.

    The text values `nil` and `undefined` are treated as unavailable, regardless of capitalization.


#### Setpoint

Use `Setpoint` when the state should depend on a condition rather than simply on whether the metric is zero.

Set the following properties:

- `Mode` — `Setpoint`.
- `True If` — the comparison operator for a numeric metric.
- `Setpoint` — the reference value.

Supported numeric comparisons are `>`, `<`, `>=`, `<=`, and `=`.

For example, to indicate when a temperature is above 70 °C, select the temperature metric, choose Celsius as the evaluation unit when available, set `True If` to `>`, and enter `70` as `Setpoint`.

!!! warning
    `True If` applies to numeric comparisons. For text metrics, Setpoint uses an exact, case-sensitive string comparison. For Boolean metrics, Setpoint checks whether the value matches the expected Boolean state.

    A numeric `Setpoint` is entered as a number, without a unit symbol. For numeric metrics, the selected `Unit` determines the unit used in the comparison.


See [Evaluation Mode](properties.md#evaluation-mode), [True If](properties.md#true-if), and [Setpoint](properties.md#setpoint).

### Step 4. Configure the two visual states

Open the `States` tab. It contains:

- `State: True`
- `State: False`

Each state has its own `Source Type` with three choices:

| Source Type | Presentation |
| --- | --- |
| `Icon` | An icon selected from the shared icon library. |
| `Image` | An image file belonging to the dashboard. |
| `Value` | The current metric value, formatted and displayed as text. |

Select `Source Type` separately for `True` and `False`, then configure the corresponding `Source` if you chose `Icon` or `Image`.

For example, the True state could show a green check icon and the False state a red warning image. Alternatively, both states could display the current numeric value with different text colors.

!!! info
    The two profiles are independent. Changing the True profile does not automatically change the False profile.

    When `Source Type = Value`, Binary shows the current formatted metric value, **not** the words `True` or `False`.


See [State Source Type](properties.md#state-source-type) and [Image Source](properties.md#image-source).

### Step 5. Adjust appearance and state-specific presentation

For icons and image files, use the `Image` tab. The appropriate group appears for each graphical state.

Available settings include image fit, animation loop, and opacity. Icon tint is available when the selected icon supports it; file-backed images do not use the icon tint setting.

For a `Value` state, use the `Text` tab. Each value-backed state has its own:

- foreground color and opacity;
- font family, size, weight, and italic style;
- horizontal and vertical alignment;
- outline color and width;
- text overflow mode and, where applicable, animation settings.

The `Appearance` tab provides common widget-level background, border, and shadow properties. Position, dimensions, and rotation are configured on `General`.

If one or both states use `Value`, the `Data` tab may also expose source unit, display unit, format, prefix, suffix, and fallback controls. The exact property availability depends on the selected metric, the chosen evaluation mode, and the state source types.

See [Widget Properties — Tabs](properties-by-tab.md), [State Color](properties.md#state-color), [Opacity](properties.md#opacity), [Image Fit](properties.md#image-fit), and [Overflow Mode](properties.md#overflow-mode).

### Step 6. Understand unavailable data

Binary has only two configurable state profiles: True and False.

If the metric is missing, `null`, or cannot be evaluated, Binary does **not** select the False profile and does **not** switch to an imaginary third `Unavailable` profile. No True/False state content is drawn while evaluation is unavailable.

The `Fallback` property is used when an already selected `Value` profile cannot format its text. It does not create a visual state when evaluation itself fails.

!!! warning
    A missing metric and a genuine False result are different conditions. Do not use the False profile as though it automatically meant that the telemetry provider was offline.


### Usage Examples

These are property combinations, not prebuilt dashboard files.

| Scenario | Key settings | Expected behavior |
| --- | --- | --- |
| Microphone activity | `Metric = system.media.input.active`; `Mode = Auto` | Uses True when Windows reports an active default input audio session and False when it reports an inactive one. |
| Internet connectivity | `Metric = system.network.internet.connected`; `Mode = Auto` | Selects True or False from the Windows connectivity metric when a Boolean result is available. |
| Temperature threshold | Select a numeric temperature metric; `Mode = Setpoint`; `True If = >`; `Setpoint = 70`; choose `Unit = C` if supported | True when the converted numeric reading exceeds 70 °C; otherwise False, provided evaluation succeeds. |
| Exact text state | Select a text metric; `Mode = Setpoint`; `Setpoint = online` | True only when the text equals `online` exactly, including letter case. |
| Value-based warning | `Mode = Setpoint`; set True and False `Source Type = Value`; configure each state’s text color | Both states display the current metric reading with the appearance of the evaluated state. |
| Different icons | Set True and False to `Icon` with different sources and opacity | Displays the icon associated with the current logical state. |

---
## Technical Information

### Purpose and Contract Boundary

Binary is a canonical state-driven widget with:

- User-facing name: `Binary`
- Persisted type discriminator: `binary`
- Model: `DashboardModel.BinaryWidgetDefinition`
- Renderer: `Widgets.BinaryWidgetRenderer`

The widget reads an existing metric snapshot. It does not poll providers or interact with hardware directly.

Key implementation components:

- `BinarySignalContract` — Boolean, numeric, and text evaluation.
- `Widgets.WidgetRenderContext.TryResolveBinaryVisualProfile` — metric lookup, optional numeric unit conversion, state resolution, and profile selection.
- `DashboardModel.StateVisualProfileDefinition` — canonical state-specific content and presentation.
- `Widgets.StateProfileWidgetRenderer` — shared Value/Icon/Image state rendering.
- `Widgets.TextContentRenderer` and `Widgets.ValueTextLayout` — text presentation for Value states.
- `TextOverflowStateContract` — text width and overflow normalization.
- `MetricValueFormatter` and `MetricValueConverter` — formatted text and numeric unit conversion.

The Binary renderer delegates its state-specific drawing to the shared state-profile renderer; it is not a separate duplicated graphics implementation.

### Canonical Model and Persisted Fields

`BinaryWidgetDefinition` inherits from `StateVisualWidgetDefinition`, which in turn inherits the common `WidgetDefinition` properties.

Binary-specific JSON fields:

| JSON field | Canonical member | Purpose |
| --- | --- | --- |
| `type` | Discriminator `binary` | Persistent widget identity by type. |
| `metric` | `Metric` | Required metric ID. |
| `unit` | `Unit` | Selected presentation unit; also used for numeric Setpoint evaluation. |
| `format` | `Format` | Value-state text formatting. |
| `prefix` | `Prefix` | Text before a formatted Value-state reading. |
| `suffix` | `Suffix` | Text after a formatted Value-state reading. |
| `fallback` | `Fallback` | Text used by the value-formatting path when a reading cannot be presented. |
| `evaluationMode` | `EvaluationMode` | `Auto` or `Setpoint`. |
| `trueIf` | `TrueIf` | Numeric Setpoint comparison operator. |
| `setpoint` | `Setpoint` | Reference value stored as text. |
| `profiles` | `Profiles` | True/False visual-profile dictionary. |

Common inherited fields include `id`, `name`, `z`, `x`, `y`, `width`, `height`, `rotation`, color, background, border, and shadow parameters.

The `metric` field identifies the telemetry source reading, not the widget instance. The stable `id` is the widget identity within the dashboard.

### Evaluation Semantics

The default evaluation mode is:

  `Auto`

`BinarySignalContract.TryResolve` returns both a success flag and a Boolean result. A failed resolution is not equivalent to a successful False result.

#### Auto evaluation

For `Auto`, the implementation recognizes:

- native `bool` values;
- finite numeric CLR values, with zero considered False;
- string values, where empty is False and non-empty is True except for reserved unavailable strings.

The strings `nil` and `undefined` are considered unavailable with case-insensitive matching. Arbitrary non-empty text, including the literal string `false`, evaluates to True.

A `null`, unsupported object type, or non-finite number does not yield a valid Boolean state.

#### Setpoint evaluation

Setpoint behavior depends on the actual runtime value type.

| Runtime value type | Evaluation contract |
| --- | --- |
| Boolean | Equality against the expected Boolean value. A setpoint of `true` (case-insensitive) or `1` denotes expected True; other setpoint strings denote expected False. |
| String | Ordinal, case-sensitive equality with the stored setpoint, except that `nil` and `undefined` remain unavailable. |
| Numeric | Finite numeric comparison against a finite reference parsed using `InvariantCulture`. |

The numeric operator is selected from `>`, `<`, `>=`, `<=`, and `=`. An unsupported numeric operator or an unparsable/non-finite numeric reference causes evaluation failure.

For Boolean and string values, `TrueIf` does not control evaluation. Their Setpoint behavior is equality-based.

For numeric metrics with a known numeric `MetricDescriptor`, `WidgetRenderContext.TryResolveBinaryVisualProfile` first requests a converted numeric value using `TryGetMetricDouble(widget, ...)`. This uses the widget's `Unit` and the standard `MetricValueConverter` contract. `BinarySignalContract` then compares the converted value with the entered setpoint.

For example, with a Celsius-base temperature metric, selecting `Unit = F` makes numeric Setpoint evaluation operate on the Fahrenheit value. The source metric itself is not changed.

`Format` and `Prefix/Suffix` affect Value-state text presentation, not numeric Setpoint evaluation.

### State Resolution and Unavailable Semantics

State resolution follows this sequence:

- Read the configured metric from the current snapshot.
- If Setpoint mode applies to a numeric metric with a descriptor, perform numeric conversion using the selected unit.
- Evaluate using `BinarySignalContract.TryResolve`.
- On success, select key `true` or `false`.
- Resolve the matching canonical profile and project it to the rendering representation.

When metric lookup or evaluation fails, `TryResolveBinaryVisualProfile` returns failure. `StateProfileWidgetRenderer` then deactivates state-specific animation playback and does not render a True/False profile.

Unlike Power or Media System, Binary defines no dedicated unavailable profile. Absence of an evaluation result must not be silently normalized to `false`.

The formatted Value-state text uses `WidgetRenderContext.FormatMetric(BinaryWidgetDefinition)`. In that path, a missing or unformattable value uses `Prefix + Fallback + Suffix`. However, an unavailable evaluation prevents selection of either state profile, so this formatting fallback is **not** a general missing-metric visualization for Binary.

### State Profiles and Content Types

The canonical `profiles` dictionary contains exactly these required keys:

- `true` — `State: True`
- `false` — `State: False`

--8<-- "widgets/state-profiles.md:profile-schema"

The current default profiles both use `lucide:check`. The True profile has full opacity; the False profile has `0.5` opacity.

### Value-State Text and Formatting

A Value profile displays the selected metric's formatted value, not the logical state label.

`WidgetRenderContext.ResolveStateValueText` uses `FormatMetric(binary)` for Binary. The formatting path applies:

- the metric descriptor's semantic value kind and base unit;
- optional display-unit conversion;
- `Format` where supported;
- `Prefix` and `Suffix`;
- `Fallback` on missing or unformattable values.

The formatting pipeline supports the metric kinds recognized by `MetricValueFormatter`, including Number, Percent, Duration, DataSize, DateTime, Boolean, and Text.

The `Data → Value` group is shown when at least one state uses Value content or when Setpoint evaluation is active. `Source Unit` is informational and read-only.

In Setpoint mode, numeric `Unit` options are constrained to units suitable for numerical comparison; presentation-only choices such as duration `auto` must not be passed into the numeric comparison path.

`Format` is applicable to Value presentation and may be disabled when neither state uses Value, when the metric has no applicable format, or when another selected unit makes the format inapplicable.

`Prefix`, `Suffix`, and `Fallback` belong to the shared Binary value-display path, and appear in the Editor when at least one state uses Value.

### Graphical State Rendering

--8<-- "widgets/state-profiles.md:profile-rendering"

For Binary, missing or failed evaluation leaves no active True/False profile. No graphical or animated state is displayed; this must not be silently treated as False. The canonical schema-19 state definition is projected into the shared immutable render profile.

### Geometry and Overflow

--8<-- "widgets/state-profiles.md:profile-geometry"

Binary has **two** profiles. Both must use Value for auto-width to be eligible. `WidgetGeometry` retains an appropriate selection/layout extent when Binary evaluation is unavailable. This geometry fallback is not an additional visual or unavailable state.

### Editor Property Applicability

The current canonical Editor property model exposes:

- `General → Source → Metric` — required data source.
- `Data → Evaluation → Mode` — always available for Binary.
- `Data → Evaluation → True If / Setpoint` — applicable when `Mode = Setpoint`.
- `States → State: True / State: False` — separate state source selection.
- `Image` — state image fit, loop, color/tint when supported, and opacity for graphical state sources.
- `Text` — per-state foreground and text-presentation settings only for Value sources.
- `Data → Value` — conditionally available for Value presentation or Setpoint evaluation.
- `Data → Display` — present when at least one profile uses Value.
- `Appearance` — common visual settings.

State-specific settings should not be presented as universal properties of every Binary instance. In particular, selecting Icon/Image must not expose active Value-only text controls for that state.

See [Widget Properties — Tabs](properties-by-tab.md) and [Widget Property Dictionary](properties.md) for the exact current property names, applicable contexts, and supported settings.

### Validation and Maintenance Contracts

Canonical validation requires:

- a non-empty metric ID;
- supported `EvaluationMode` and `TrueIf` values;
- valid and complete `true` and `false` profiles, with no unsupported state keys;
- structurally valid and resolvable active image assets;
- finite widget geometry and valid shared visual properties;
- positive Width/Height for graphical or mixed profiles, or non-negative dimensions for Value-only profiles;
- text-presentation and overflow consistency when Value content is used.

Runtime setpoint parsing has its own failure semantics. A stored text setpoint is not automatically guaranteed to be a valid numeric reference for every selected metric; an invalid numeric reference must not be converted into a False result.

The following contracts are compatibility-sensitive:

- Persisted type remains `binary`.
- Public states remain `true` and `false`; missing evaluation is neither.
- Auto evaluation preserves type-specific Boolean, number, and string behavior.
- Numeric Setpoint comparisons use the configured numeric presentation unit.
- Boolean and string Setpoint evaluation use equality semantics, not the numeric operator.
- The current metric value is displayed in a Value profile; it is not automatically replaced with a textual state label.
- Separate True/False profiles preserve their individual asset, color, opacity, and text settings.
- Mixed graphical/value profiles use explicit container geometry; Value-only profiles can use content-derived geometry.
- Editor preview and Runtime share the canonical state-profile projection and rendering semantics.
- Missing telemetry must not reuse stale state data or silently choose the False profile.
- Persisted schema, profile behavior, evaluation semantics, or value rendering changes require intentional compatibility analysis and deterministic regression coverage when reliably testable.

For implementation source, see [PinkieSysMon on GitHub](https://github.com/Jim1537/PinkieSysMon).
