# Text / Value

The **Text / Value** widget displays either user-defined text or the current value of a selected telemetry metric on a dashboard. Typical uses include labels, temperatures, clock frequencies, utilization percentages, system uptime, and other readings that are best presented as text.

The same widget can display a literal string or data supplied by a telemetry provider. Numeric values support formatting and unit conversion. When a display width is specified, long text can be clipped, shortened, wrapped, scaled down, or animated.

---
## User Guide

### Step 1. Add the widget

Open the desired dashboard in **PinkieSysMon Dashboard Editor** and add **Text / Value** using the `Add Widget` command or the `Add widget` toolbar button.

Select the new widget on the Canvas. Its settings appear in the Properties panel, where you can configure its content, appearance, and placement.

!!! info
    A new Text / Value widget starts in `Metric` mode, with automatic text width. Its initial metric is `system.runtime.version`. You can select a different metric or change the source type to `Text`.


### Step 2. Choose a data source

On the `General` tab, locate the `Source` group and set `Type` to one of the following:

- `Metric` — display a value provided by System, Libre Hardware Monitor, iCUE, or another available telemetry provider.
- `Text` — display a literal string entered in the `Text` property.

For `Metric`, choose a metric using the `Metric` property. For `Text`, enter the desired content in `Text`.

!!! info
    If the selected provider is disabled, stops delivering data, or the individual metric is unavailable, the `Metric` source displays the configured `Fallback` text rather than inventing or retaining a stale reading.

    `Text` does not require a telemetry provider.


See [Value Source Type](properties.md#value-source-type), [Metric](properties.md#metric), and [Text](properties.md#text).

### Step 3. Configure units and formatting

The `Data` tab contains the following settings:

- `Source Unit` — the original unit associated with a numeric value.
- `Unit` — the desired display unit, when a supported conversion is available.
- `Format` — numeric, duration, or date/time formatting.
- `Prefix` and `Suffix` — text inserted before and after the displayed value.
- `Fallback` — the text displayed when a metric cannot provide a usable value.

With `Type = Metric`, the source unit comes from the selected metric's contract and is read-only. Available `Unit` and `Format` choices depend on that metric's value type; inapplicable properties are disabled in the Editor.

With `Type = Text`, an ordinary string is displayed without numeric conversion. However, if `Text` contains a standalone numeric literal, such as `25` or `12.5`, you can assign its `Source Unit`, convert it using `Unit`, and apply a numeric `Format`.

!!! warning
    A numeric literal must use an unambiguous, culture-independent number format, such as `12.5` with a decimal point. `12.5 °C` is not treated as a numeric literal. Enter `12.5` as the text and choose `C` separately as `Source Unit` if conversion is needed.

    `Unit` controls conversion; it does not automatically append a unit symbol to the displayed text. Use `Suffix` (for example, ` °C`) when you want the unit label to appear.

    `Unit` and `Format` cannot turn arbitrary text into a number.


See [Source Unit](properties.md#source-unit), [Unit](properties.md#unit), [Format](properties.md#format), [Prefix](properties.md#prefix), [Suffix](properties.md#suffix), and [Fallback](properties.md#fallback).

### Step 4. Customize text appearance

The `Text` tab provides these groups:

- `Font` — font family, size, weight, and italic style.
- `Alignment` — horizontal and vertical text alignment.
- `Outline` — text outline color and width.
- `Overflow` — how text behaves when its available width is limited.

Text color, background, border, and shadow settings are on the `Appearance` tab. The widget name, coordinates, width, and rotation are on `General`.

Text / Value uses a content-derived height rather than an independently adjustable rectangular height. Font size, wrapping, and width can therefore change the effective bounds of the displayed content.

See [Widget Properties — Tabs](properties-by-tab.md) and [Widget Property Dictionary](properties.md).

### Step 5. Set width and overflow behavior

Text / Value supports two basic layout modes:

- `Width = 0` — automatic width based on the text; `Overflow Mode = None`.
- `Width > 0` — fixed width with one of the constrained overflow modes.

The available fixed-width modes are:

| Overflow Mode | Behavior |
| --- | --- |
| `Clip` | Hides text extending beyond the available width. |
| `Ellipsis` | Shortens the text and adds an ellipsis. |
| `ShrinkToFit` | Reduces the font size so the line fits within the width. |
| `Wrap` | Wraps text onto additional lines. |
| `Scroll` | Continuously scrolls a long line horizontally in a repeating cycle. |
| `Bump` | Moves a long line toward the opposite edge and back, pausing at each end. |

`Scroll Speed` controls movement for `Scroll` and `Bump`. Setting it to `0` selects an automatically calculated speed; it does not stop the animation. `Bump Pause` applies only to `Bump`.

The Editor keeps `Width` and `Overflow Mode` consistent: selecting `None` restores automatic width, while choosing a constrained mode when width is automatic establishes a positive width.

See [Width](properties.md#width), [Overflow Mode](properties.md#overflow-mode), [Scroll Speed](properties.md#scroll-speed), and [Bump Pause](properties.md#bump-pause).

### Usage Examples

These examples illustrate useful property combinations. They are not prebuilt dashboard files.

| Scenario | Key settings | Result |
| --- | --- | --- |
| Static label | `Type = Text`; `Text = CPU`; `Width = 0`; `Overflow Mode = None` | Displays `CPU` without using a telemetry provider. |
| Temperature reading | `Type = Metric`; select a temperature metric; choose `Unit = C` or `F` if supported; set `Format` and `Suffix` | Displays the current reading using the chosen unit, formatting, and optional unit label. |
| System uptime | `Type = Metric`; `Metric = system.os.uptime`; `Unit = auto` | Displays uptime as automatically formatted duration text. |
| Manually entered number | `Type = Text`; `Text = 25`; `Source Unit = C`; `Unit = F` | Converts a numeric literal from Celsius to Fahrenheit (`77` before any suffix). |
| Long label | `Type = Text`; enter a long string; `Width > 0`; `Overflow Mode = Bump` | Moves the text within the configured width, pausing at the edges. |
| Missing telemetry | `Type = Metric`; `Fallback = --` | Displays `--` instead of an unavailable or stale reading. |

!!! info
    For a complete property reference, use [Widget Properties — Tabs](properties-by-tab.md) or [Widget Property Dictionary](properties.md). This page focuses on how Text / Value works rather than repeating the entire property dictionary.


---
## Technical Information

### Purpose and Contract Boundary

Text / Value is a canonical widget with the persisted type discriminator `value` and the user-facing Editor name `Text / Value`.

Canonical model:

  `DashboardModel.ValueWidgetDefinition`

Renderer:

  `Widgets.ValueWidgetRenderer`

Related components:

- `DashboardModel.TextPresentationDefinition` — canonical text presentation data.
- `Widgets.WidgetRenderContext` — value resolution and per-frame formatting.
- `Widgets.TextContentRenderer` — text drawing, width constraints, and animation.
- `Widgets.ValueTextLayout` — text measurement, placement, and wrapping.
- `TextOverflowStateContract` — coordination of Width and Overflow settings.
- `LiteralNumericContract` — numeric-literal interpretation in Text mode.
- `MetricValueFormatter` and `MetricValueConverter` — semantic formatting and unit conversion.

The widget does not access LHM, iCUE, or Windows telemetry APIs directly. In `Metric` mode it reads a value already present in the shared metric snapshot; in `Text` mode it uses the stored literal.

### Canonical Model and Persisted Fields

`ValueWidgetDefinition` inherits common fields from `WidgetDefinition` and adds:

| JSON field | Canonical member | Purpose |
| --- | --- | --- |
| `type` | Discriminator `value` | Persistent widget type. |
| `sourceKind` | `SourceKind` | `Metric` or `Text`. |
| `metric` | `Metric` | Metric ID used in Metric mode. |
| `text` | `Text` | Literal used in Text mode. |
| `sourceUnit` | `SourceUnit` | Native unit assigned to a numeric literal. |
| `unit` | `Unit` | Display unit. |
| `format` | `Format` | Presentation format. |
| `prefix` | `Prefix` | Text preceding the resolved value. |
| `suffix` | `Suffix` | Text following the resolved value. |
| `fallback` | `Fallback` | Text used when a value cannot be presented. |
| `textPresentation` | `TextPresentation` | Font, alignment, outline, overflow, and text layout settings. |

The common `WidgetDefinition` also persists `id`, `name`, `z`, `x`, `y`, `width`, `height`, `rotation`, and shared visual properties such as colors, border, and shadow.

`name` is the element's user-facing label in the Editor; it does not replace its stable `id`. `metric` is the data-source metric ID, not the widget's identity.

Model defaults include `SourceKind = Metric`, `Fallback = --`, empty `Prefix` and `Suffix`, and a `TextPresentation` with `Roboto`, size `32`, weight `400`, and `OverflowMode = None`.

The Editor creates a Text / Value widget with `Width = 0`, `Height = 0`, `SourceKind = Metric`, and `Metric = system.runtime.version`. These insertion defaults must not be mistaken for immutable requirements on its subsequent content.

### Data Sources and Demand-Driven Telemetry

`SourceKind` accepts `Metric` or `Text` (case-insensitive validation).

In Metric mode, `Metric` must be non-empty for a valid canonical widget. In Text mode, any stored `Metric` member is inactive.

`DashboardMetricUsage.Collect()` includes the Text / Value metric ID in the requested metric set only when `SourceKind = Metric`. A static label therefore creates no telemetry demand for its content and does not trigger provider polling on its own.

Changing source type does not make previously stored, inactive fields simultaneous inputs. `SourceKind` determines the active value-resolution path.

### Value Resolution and Fallback

`ValueWidgetRenderer.Render()` calls `WidgetRenderContext.ResolveValueText()` and passes the resolved string to `TextContentRenderer` using the canonical `TextPresentation`.

The Metric-mode resolution sequence is:

- Look up the configured metric in the current frame's metric snapshot.
- If the metric is missing or its value is `null`, produce `Prefix + Fallback + Suffix`.
- If a value and its registered `MetricDescriptor` are present, call `MetricValueFormatter` using the selected `Unit` and `Format`.
- If the expected conversion or formatting exceptions occur, use `Fallback`.
- If the metric has a value but no registered descriptor, convert the value to a string without semantic unit conversion.

`Fallback` is a presentation result. It does not modify or replace the underlying value in `MetricStore`.

In Text mode, a nonnumeric literal, including an empty string, is displayed directly with `Prefix` and `Suffix`. It does not become unavailable merely because a provider is absent. A recognized numeric literal uses the numeric formatting path; if formatting or conversion fails, that path produces `Prefix + Fallback + Suffix`.

The Editor disables the `Fallback` property in Text mode because it normally applies to Metric mode, although the internal numeric-literal error path can still read its persisted value.

Metric-backed formatted text is cached per widget in the render context and the cache is cleared when the metric snapshot is updated.

### Numeric Literals and Unit Semantics

`LiteralNumericContract.TryParse()` accepts finite values parsed using `NumberStyles.Float` and `CultureInfo.InvariantCulture`. A string that is not a standalone numeric literal, including a number followed by a typed-in unit, remains ordinary text.

For a successfully parsed literal, `SourceUnit` establishes a synthetic `literal.text` descriptor for the standard `MetricValueFormatter` path:

- No `SourceUnit` — `Number / None`.
- `%` — `Percent / None`.
- Time units — `Duration`.
- Data-size units — `DataSize`.
- Other supported numeric units — `Number` with the corresponding base unit.

Unlike Metric mode, Text mode allows `Source Unit` to be selected for a numeric literal. Supported `Unit` and `Format` choices are derived from this descriptor. When the source unit changes, the Editor reconciles related options rather than keeping an incompatible conversion selection.

In Metric mode, the source unit comes from `MetricDescriptor.BaseUnit` and is exposed as a read-only label. `Unit` selects the target presentation unit only.

Formatting supports semantic value kinds including `Number`, `Percent`, `DataSize`, `Duration`, `DateTime`, `Boolean`, and `Text`, subject to the selected metric's contract. `DateTime` uses .NET format strings; `Duration` supports an `auto` presentation and supported duration formats. Numeric output uses `CultureInfo.CurrentCulture`.

For numeric values, unit conversion changes the displayed number but does not append the unit label. A visible unit label is supplied separately through `Suffix`.

Source and display units must remain distinct: changing `Unit` does not alter the value published by a telemetry provider.

### Text Presentation

`TextPresentationDefinition` is a separate canonical object stored in `textPresentation`, not an unstructured collection of legacy root-level text properties.

Its JSON members include:

- `fontFamily`, `fontSize`, `fontWeight`, `italic`;
- `align`, `verticalAlign`;
- `outlineColor`, `outlineWidth`;
- `overflowMode`, `scrollSpeed`, `bumpPauseMs`.

Text presentation properties appear on the `Text` tab; common widget color, background, border, shadow, position, and rotation properties are handled by the other property groups.

`FontSize` must be finite and greater than zero. `FontWeight` must be within `1..1000`. `OutlineWidth`, `ScrollSpeed`, and `BumpPauseMs` cannot be negative.

`TextContentRenderer` uses SkiaSharp and a shared text layout path for Editor preview and Runtime. Text is drawn using the selected typeface, with an optional outline and then the fill. Background and shadow are handled by the shared widget-rendering pipeline, not by the value-source logic.

### Geometry: Automatic Width and Derived Height

Unlike Image or Bar, Text / Value does not use independently adjustable fixed width and height as a rectangular content box.

The geometry contract is:

- `Width = 0` — the displayed text is measured using the current typeface to determine its intrinsic width.
- `Width > 0` — horizontal layout is constrained to the specified width.
- Effective height is derived from font metrics and the resulting line layout rather than a separately editable fixed height.
- `WidgetSizeContract.IsHeightDerived()` returns true for `ValueWidgetDefinition`.
- `WidgetSizeContract.IsWidthOnlyResizable()` also returns true.

`ValueTextLayout` uses SkiaSharp text metrics to compute baselines, alignment, wrapping, and bounds. In `Wrap` mode, additional lines affect the effective height. `WidgetGeometry` uses this layout for widget bounds, selection geometry, and visual effects.

Rotation is handled by the common widget pipeline. A separately fixed Text / Value height must not override the content-derived geometry contract.

### Overflow and Text Animation

`ValueOverflowContract` supports:

- `None` — intrinsic width, without a fixed-width overflow constraint.
- `Clip` — horizontal clipping.
- `Ellipsis` — truncation with an ellipsis character.
- `ShrinkToFit` — reduce the font size to fit the specified width.
- `Wrap` — break text into multiple lines.
- `Scroll` — continuous, repeating horizontal scrolling.
- `Bump` — back-and-forth movement with pauses at the ends.

The canonical invariant is:

  `Width = 0` => `OverflowMode = None`

  `Width > 0` => `OverflowMode != None`

`TextOverflowStateContract` normalizes transitions between modes in the Editor and when persisted data is prepared. Selecting `None` restores automatic width. Selecting a constrained mode for an auto-width widget materializes a positive width; editing Width also reconciles the selected mode with geometry.

`TextContentRenderer` clips fixed-width Text / Value content horizontally, without imposing an arbitrary fixed vertical height on the standalone widget.

`Scroll` and `Bump` use elapsed display time and the measured line width; animation progress does not depend on the arrival rate of new telemetry samples. No movement is needed when the string already fits.

When `ScrollSpeed > 0`, the specified speed is used. At `ScrollSpeed = 0`, the automatic speed is `FontSize × 2` pixels per second. For `Scroll`, the repeat gap is `max(Width × 0.25, FontSize × 3)`. `Bump` adds `BumpPauseMs` at each end of its travel.

Animation state is keyed by widget identity, displayed text, mode, and motion-related parameters. Changing these inputs establishes a fresh animation phase rather than continuing an outdated one.

### Editor Property Applicability

The canonical Properties panel applies settings contextually:

- `General → Source → Type` selects `Metric` or `Text`.
- `General → Source → Metric` is inapplicable for `Type = Text`.
- `General → Source → Text` is inapplicable for `Type = Metric`.
- `Data → Value → Source Unit` is read-only for a metric and user-selectable for a numeric literal.
- `Data → Value → Unit / Format` depend on the semantic type of the active source.
- `Data → Display → Fallback` is inapplicable for `Type = Text`.
- `Text → Overflow → Scroll Speed` applies to `Scroll` and `Bump`.
- `Text → Overflow → Bump Pause` applies only to `Bump`.
- `General → Geometry → Height` is derived rather than independently adjustable.

The Editor reconciles `SourceUnit`, `Unit`, `Format`, and `OverflowMode` after relevant edits instead of leaving contradictory selections. For complete property names, values, and applicability rules, see [Widget Properties — Tabs](properties-by-tab.md) and [Widget Property Dictionary](properties.md).

### Validation, Errors, and Unavailable Values

Canonical validation requires:

- A supported `SourceKind`.
- A non-empty `Metric` when `SourceKind = Metric`.
- `Width >= 0` and `Height >= 0`.
- A valid `TextPresentation` object and supported `OverflowMode`.
- A consistent Width/Overflow invariant.
- Finite common geometry values and valid shared appearance properties.

An unavailable metric is represented by a missing or `null` value at the telemetry boundary, not by indefinitely reusing the last numeric value in the renderer. Text / Value must not interpret `null` as zero or silently switch to another metric ID; it presents the configured `Fallback` instead.

For a nonnumeric Text literal, telemetry unavailability is not an error. Invalid formatting or an unsupported conversion for a recognized numeric literal produces `Fallback`, whereas ordinary arbitrary text remains text.

### Integration with Other Components

- `DashboardMetricUsage` determines which metric IDs the widget requests.
- `MetricContract` defines a metric's semantic value kind and base unit.
- `MetricValueFormatter` and `MetricValueConverter` provide the shared conversion and formatting semantics. Text / Value must not introduce an incompatible parallel unit-conversion path.
- `TextContentRenderer` and `ValueTextLayout` are also used by other text-bearing widgets; Text / Value specifically uses the `Anchor` layout and its intrinsic-width/derived-height behavior.
- `WidgetGeometry` uses the text layout bounds for shared Editor/Runtime geometry.

Changes to Text / Value can therefore affect the shared text-rendering path. Validation should cover static labels, metric-backed values, changing string lengths, unavailable data, units, and each overflow mode.

### Maintenance Contracts

The following provider-independent contracts should be preserved unless intentionally changed:

- The user-facing widget name remains `Text / Value` and its persisted type remains `value`.
- `Metric` and `Text` are mutually exclusive value-resolution paths.
- `Text` must not request telemetry merely because an inactive `metric` value remains stored.
- A numeric literal must be recognized through invariant numeric parsing, not by searching for digits within arbitrary text.
- `Source Unit`, `Unit`, and `Format` have distinct roles; presentation conversion must not alter provider-native values.
- `Fallback` is presentation behavior for missing or unrepresentable values, not a substitute for updating `MetricStore`.
- Text / Value height remains content-derived; `Width = 0` means automatic width.
- `OverflowMode = None` is valid only with automatic width; a fixed width requires a constrained overflow mode.
- `Scroll` and `Bump` maintain animation state independently of provider polling frequency.
- Editor preview and Runtime must preserve compatible canonical text-presentation and rendering semantics.
- Changes to persisted fields, text geometry, literal parsing, unavailable-value handling, or overflow behavior are compatibility-sensitive and require deterministic regression coverage when the changed contract can be reliably tested.

Source code: [PinkieSysMon on GitHub](https://github.com/Jim1537/PinkieSysMon).
