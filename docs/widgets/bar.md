# Bar

The **Bar** widget represents a numeric telemetry metric as a horizontal progress indicator. It fills a defined portion of its available range from one end of the bar toward the other.

Bar supports two distinct rendering modes:

- **Fill** — a colored bar with optional threshold-based segments, gradients, or state coloring.
- **Image** — a dashboard image that is scaled, moved, or progressively revealed according to the metric value.

Typical applications include CPU or memory utilization, temperatures, fan speeds, network throughput, battery levels, and custom graphical progress indicators.

The Bar widget does not print the numeric value or unit as text. To display a reading or caption beside the bar, add a separate [Text / Value](text-value.md) widget.

---
## User Guide

### Step 1. Add a Bar widget

Open a dashboard in **PinkieSysMon Dashboard Editor**, then choose `Bar` from `Add Widget` or the `Add widget` toolbar menu. Select the widget on the Canvas to edit its properties.

!!! info
    A new Bar starts with `Width = 300`, `Height = 20`, `Metric = system.runtime.fps`, `Min = 0`, `Max = 100`, and `Mode = Fill`.

    These defaults demonstrate the widget; choose the metric, range, and appearance appropriate for your dashboard.


### Step 2. Select a numeric metric

Open `General → Source → Metric` and select the telemetry metric to display.

Supported metrics may come from [System](../telemetry/system.md), [Libre Hardware Monitor](../telemetry/libre-hardware-monitor.md), or [iCUE Sensor Logging](../telemetry/icue-sensor-logging.md), depending on which providers are configured and supplying data.

Bar needs a usable numeric value. An unavailable metric or a value that cannot be converted to a finite number cannot produce an active fill.

See [Metric](properties.md#metric).

### Step 3. Configure units and the value range

Open the `Data` tab:

- `Value → Source Unit` shows the unit associated with the selected metric. It is read-only.
- `Value → Unit` selects a compatible numeric display/conversion unit when the metric supports one.
- `Range → Min` sets the value corresponding to an empty bar.
- `Range → Max` sets the value corresponding to a completely filled bar.

`Max` must be greater than `Min`.

For example, a percentage metric typically uses `Min = 0` and `Max = 100`. A temperature indicator might use a different range that reflects the expected readings for that sensor.

!!! warning
    `Min`, `Max`, and threshold values are interpreted in the selected `Unit`. If you change a temperature unit from Celsius to Fahrenheit, review the range and thresholds as well.

    Choosing a unit affects the value used for drawing; it does not automatically display a unit symbol on the bar.


Values below `Min` are visually clamped to empty. Values above `Max` are visually clamped to full. Bar does not automatically expand its range to accommodate out-of-range readings.

See [Source Unit](properties.md#source-unit), [Unit](properties.md#unit), [Min](properties.md#min), and [Max](properties.md#max).

### Step 4. Choose Fill or Image mode

Open `Gauge → Bar`.

| Property | Purpose |
| --- | --- |
| `Mode` | Selects `Fill` or `Image` rendering. |
| `Reverse` | Reverses the progression direction. |
| `Gap` | Insets the active content from the widget's border. |

With `Reverse` disabled, the bar progresses from left to right. With `Reverse` enabled, it progresses from right to left.

`Gap` adds space between the outer widget boundary and the active content. The common border width also contributes to this inset. Excessive gap or border width can leave no drawable interior.

Bar's underlying layout is horizontal. Rotating the widget rotates the complete rendered element; `Reverse` is the control for changing the progression direction within its local coordinates.

See [Bar Mode](properties.md#bar-mode), [Reverse](properties.md#reverse), and [Gap](properties.md#gap).

### Step 5. Configure Fill mode

Set `Gauge → Bar → Mode = Fill`.

The active part of the bar uses `Appearance → Foreground → Color` as its base color. Use `Appearance → Background → Color` for the widget background, and the `Border` group for an outline and rounded corners.

The unfilled region is the widget's background, not a separately configured Bar track. With a fully transparent widget background, the unfilled portion may appear transparent.

#### Optional thresholds

In `Fill` mode, the `Appearance` tab exposes `Threshold Mode` and up to three threshold slots. Each slot has an `Enabled` flag, a `Value`, and a `Color`.

| Threshold Mode | Visual result |
| --- | --- |
| `SegmentSolid` | Applies distinct colors to fixed sections of the bar's range; only the filled portion of those sections is drawn. |
| `SegmentTransition` | Uses color transitions across the defined range and reveals the gradient up to the current value. |
| `State` | Colors the entire active fill with the color corresponding to the highest threshold reached by the current value. |

Threshold colors supplement the base foreground color. Before the first enabled threshold, the base color is used.

For an illustration, consider a bar with `Min = 0`, `Max = 100`, base color green, an enabled threshold at `70` colored orange, and another at `90` colored red.

- `SegmentSolid` creates green, orange, and red regions at fixed positions along the bar. A current reading of 80 reveals the green region and the first part of the orange region.
- `SegmentTransition` blends through the configured colors along the bar's scale instead of changing them at hard boundaries.
- `State` draws the entire active length in orange at a reading of 80, or red at a reading of 95.

!!! warning
    Enabled threshold values must lie within `Min..Max` and must be ordered from lowest to highest. Disabled slots do not contribute a boundary.

    Threshold settings apply to `Fill` mode. When `Mode = Image`, the threshold controls are hidden and threshold coloring does not affect image progress.


See [Foreground Color](properties.md#foreground-color), [Threshold Mode](properties.md#threshold-mode), [Threshold Enabled](properties.md#threshold-enabled), [Threshold Value](properties.md#threshold-value), and [Threshold Color](properties.md#threshold-color).

### Step 6. Configure Image mode

Set `Gauge → Bar → Mode = Image` and open the `Image` tab.

| Property | Purpose |
| --- | --- |
| `Source` | Selects a dashboard-relative image file. |
| `Fit` | Determines how the source image occupies the available interior. |
| `Progress Mode` | Determines how the metric reveals or transforms the image. |
| `Loop` | Controls looping when the selected image supports animation. |

Bar Image mode uses **image files**, not logical names from the shared icon library. Select an image using the Editor's file selection workflow.

#### Image Fit

| Fit | Behavior |
| --- | --- |
| `Clip` | Keeps the image at its native dimensions, centered and clipped to the bar's interior. |
| `Contain` | Scales the image proportionally to fit entirely within the interior. |
| `Cover` | Scales proportionally to cover the interior, cropping excess content. |
| `Stretch` | Scales independently in both dimensions to fill the interior. |

#### Image Progress Mode

| Progress Mode | Behavior |
| --- | --- |
| `Scale` | Compresses or stretches the prepared image horizontally in proportion to the progress value, anchored at the active starting edge. |
| `Slide` | Moves the prepared image horizontally while displaying only the active part of the bar. |
| `Reveal` | Keeps the prepared image stationary and progressively exposes more of it through a clipping region. |

These modes do not change the source metric or range; they only change how the same normalized progress value affects the image.

!!! info
    For a custom illustrated progress bar, `Reveal` usually preserves the original proportions and positions of the artwork. `Scale` is useful when intentional horizontal compression is desired, while `Slide` produces a moving-image effect.

    The resulting image is clipped to the bar's drawable interior and current progress region. `Reverse` changes the active side and the image progression direction.


Image-based progress can use an animated file supported by the image asset pipeline. `Loop` controls whether its animation repeats; it is not a setting for reversing the progress direction.

See [Image Source](properties.md#image-source), [Image Fit](properties.md#image-fit), [Progress Mode](properties.md#progress-mode), and [Image Loop](properties.md#image-loop).

### Step 7. Adjust geometry and common appearance

The `General → Geometry` group controls position, width, height, and rotation. Unlike Gauge, Bar has independent width and height and is not constrained to a square.

The `Appearance` tab provides:

- `Foreground → Color` in Fill mode;
- `Background → Color` for the outer widget background;
- `Border` color, width, and corner radius;
- optional `Shadow` settings;
- threshold controls in Fill mode.

The widget's `Corner Radius` also influences the rounded active-fill clipping geometry. `Gap` and border width reduce the internal space available for Fill or Image content.

!!! warning
    A Bar with a nonzero reading may still show no active content if its interior is reduced to zero size by the border and gap, or if the selected metric is unavailable. In Image mode, an invalid or missing image source is a dashboard validation problem rather than an alternate fallback rendering mode.


See [Width](properties.md#width), [Height](properties.md#height), [Rotation](properties.md#rotation), [Background Color](properties.md#background-color), [Border Width](properties.md#border-width), and [Corner Radius](properties.md#corner-radius).

### Step 8. Understand unavailable data

If the current metric is absent, null, non-numeric, or cannot be converted into a usable finite numeric value, Bar does **not** draw its active Fill or Image progress.

The widget's background, border, and other common visual effects are handled separately from its active progress content. Bar does not retain the previous fill level as an implicit substitute for missing telemetry.

Unlike [Binary](binary.md), Bar does not select logical True/False state profiles. It also does not display a text `Fallback` value of its own.

### Usage Examples

These are suggested combinations of properties, not prebuilt dashboard presets.

| Scenario | Suggested configuration | Result |
| --- | --- | --- |
| CPU load | Select a numeric CPU utilization metric; `Min = 0`, `Max = 100`; `Mode = Fill` | A horizontal percentage-based resource indicator. |
| Temperature warning | Select a temperature metric; choose an appropriate unit and range; `Mode = Fill`; use `Threshold Mode = State` | The entire active bar changes color as configured boundaries are reached. |
| Segmented temperature scale | `Mode = Fill`; `Threshold Mode = SegmentSolid`; two enabled thresholds | Fixed colored regions are progressively exposed as the value rises. |
| Network activity | Select a numeric throughput metric; set a suitable range and unit; optionally use `Reverse` | A left-to-right or right-to-left throughput indicator. |
| Illustrated progress | `Mode = Image`; select a bar image; `Fit = Stretch`; `Progress Mode = Reveal` | The image is revealed without rescaling its prepared shape as progress increases. |
| Sliding indicator | `Mode = Image`; select an image; `Progress Mode = Slide` | The image moves horizontally within the visible portion. |

---
## Technical Information

### Purpose and Implementation

Bar is a metric-driven quantitative widget with the persistent type discriminator `bar` and canonical model `DashboardModel.BarWidgetDefinition`.

Relevant implementation components:

- `DashboardModel.BarWidgetDefinition` and `DashboardModel.QuantitativeWidgetDefinition` — persisted model, validation, and common numeric properties.
- `DashboardModel.BarImagePresentationDefinition` — image source, fit, progress mode, and loop properties.
- `BarImageContract` — supported Fill/Image modes and Image mode option tokens.
- `Widgets.BarWidgetRenderer` — active fill and image-progress rendering.
- `Widgets.CompiledQuantitativeIndicator` — threshold color stops, normalized ratio, and state colors.
- `Widgets.WidgetRenderContext.TryGetMetricDouble` — numeric metric resolution and unit conversion.
- `MetricContract` and `MetricValueConverter` — metric value kinds, native units, and supported numeric conversions.
- `Editor.CanonicalPropertyViewBuilder` — Editor property visibility and applicability.

Bar uses the common dashboard rendering pipeline for container background, border, shadows, placement, and rotation. Its own renderer is responsible for the value-dependent active region.

### Canonical Model and Persistence

`BarWidgetDefinition` inherits from `QuantitativeWidgetDefinition`, which inherits common `WidgetDefinition` geometry and appearance properties.

| JSON field | Canonical member | Meaning |
| --- | --- | --- |
| `type` | Type discriminator | `bar`. |
| `metric` | `Metric` | Required telemetry metric identifier. |
| `unit` | `Unit` | Optional numeric presentation unit. |
| `min` / `max` | `Min` / `Max` | Numeric range defining 0% and 100% progress. |
| `reverse` | `Reverse` | Reverses local progression direction. |
| `gap` | `Gap` | Inset added inside the common widget border. |
| `thresholds` | `Thresholds` | Threshold mode and three stored threshold slots. |
| `contentMode` | `ContentMode` | `Fill` or `Image`. |
| `image` | `Image` | Image-mode configuration. |

The `image` object contains `source`, `fit`, `progressMode`, and `loop`. The `thresholds` object contains `mode` and `items` with `enabled`, `value`, and `color` per slot.

--8<-- "widgets/properties.md:widget-base-fields"

The display labels in the Editor are not a substitute for these persisted JSON property names.

### Metric Resolution and Normalized Progress

--8<-- "widgets/quantitative.md:quantitative-metric"

For **Bar**, `ratio = 0` draws no active Fill/Image content, `ratio = 1` covers the full drawable interior, and intermediate values control proportional **horizontal** progress. The metric is requested only when needed; a missing reading does not leave the prior fill visible.

### Geometry and Reverse Direction

Bar requires positive `Width` and `Height`. Both can be configured independently.

The renderer computes the active content rectangle in local coordinates:

  `inset = max(0, BorderWidth) + Gap`
  `inner = [inset, inset, Width - inset, Height - inset]`

The active width is `inner.Width * progress` when the interior has positive width and height.

- Normal direction: active content extends from the left edge toward the right.
- Reverse direction: active content extends from the right edge toward the left.

The common widget border and background are separate from the active content. Effective inner corner rounding is derived from the widget's outer `CornerRadius` reduced by the inset, constrained to the inner rectangle.

Rotation applies through the common widget rendering pipeline, around the widget geometry. It does not change the horizontal local-axis progression algorithm.

### Fill Rendering and Thresholds

--8<-- "widgets/quantitative.md:quantitative-thresholds"

In **Bar Fill mode**, `SegmentSolid` draws fixed color intervals across the horizontal interior and clips them to the active rectangle. `SegmentTransition` applies a **linear** gradient across the whole interior, then clips it to the active region; its direction follows `Reverse`. `State` colors the **entire horizontal active rectangle** according to the last reached threshold.

Threshold coloring affects only Bar's active Fill, not its shared background. **Image mode** instead uses `ImageAssetCache` with `Scale`, `Slide`, or `Reveal` transformations; threshold controls are hidden by the Editor, threshold range/order validation is not used in this mode, and the persisted thresholds definition remains separate.

### Image Rendering

The `Image` mode stores a dashboard-relative image `source`, a `fit` option, a `progressMode`, and `loop`.

The active image source must exist and resolve within the dashboard directory. Bar Image mode is file-backed; it does not store or resolve a global icon identifier as its image source.

The renderer loads the source through `ImageAssetCache` and prepares a destination rectangle using `fit`:

- `Clip` — centered native-size destination, clipped to the drawable bounds.
- `Contain` — proportional scaling that fits inside the bounds.
- `Cover` — proportional scaling that covers the bounds.
- `Stretch` — direct scaling to the inner rectangle.

The prepared image is then rendered through the selected progress transform:

- `Scale` — applies a horizontal scale transform according to progress, anchored at the leading edge.
- `Slide` — shifts the prepared image as progress changes.
- `Reveal` — retains the prepared destination and reveals it using the active clip.

These operations use both an active-region clipping boundary and an interior clipping boundary; corner rounding applies where configured. `Reverse` changes the anchor, active region, and Slide direction.

Animation frames are retrieved from the image asset using the stored `loop` preference. A static image is unaffected by looping. The Bar widget does not expose a third content mode for animated images: animation is an asset capability within Image mode.

### Unavailable and Empty States

When `metric` is missing from the current metric snapshot, null, or not convertible into a finite numeric reading, the Bar renderer returns without drawing active content.

A valid reading at or below `Min` also produces no active content, but it is **not** an unavailable metric: these are distinct states that may look similar if only the active fill is visible.

The common widget background, border, and shadow are rendered by the enclosing widget pipeline. Bar does not expose a dedicated unavailable profile or textual fallback.

At zero drawable interior area, the active renderer likewise has nothing to paint, even if telemetry is available.

### Editor Property Applicability

The current canonical Editor model organizes Bar properties as follows:

- `General → Source → Metric` — telemetry binding.
- `General → Geometry` — X, Y, Width, Height, Rotation.
- `Data → Value` — read-only Source Unit and eligible numeric Unit options.
- `Data → Range` — Min and Max.
- `Gauge → Bar` — Mode, Reverse, Gap.
- `Appearance` — common background, border, shadow; foreground and thresholds where applicable.
- `Image → Image` — Source, Fit, Progress Mode, Loop, only when Mode is Image.

In `Fill` mode, the Editor exposes `Appearance → Foreground` and the threshold groups. In `Image` mode, those Fill-specific groups are absent and the Image tab becomes applicable.

See [Widget Properties — Tabs](properties-by-tab.md) and [Widget Property Dictionary](properties.md) for the authoritative list of property names, supported values, and conditional availability.

### Validation and Compatibility Contracts

The canonical Bar model requires:

- a non-empty `Metric`;
- positive finite `Width` and `Height`;
- finite `Min` and `Max`, with `Max > Min`;
- finite, non-negative `Gap`;
- a supported `ContentMode` (`Fill` or `Image`);
- a non-null `Thresholds` object and a non-null `Image` definition;
- supported Image `Fit` and `ProgressMode` tokens;
- valid common geometry and visual properties.

In Fill mode, the three threshold slots are validated, including ordered enabled thresholds within the range. In Image mode, an image source is mandatory, dashboard-relative, and resolvable; the threshold color contract is not used for Image rendering.

Compatibility-sensitive details include:

- the persisted type discriminator remains `bar`;
- `contentMode = Fill` and `contentMode = Image` have different property applicability and render paths;
- no stale numeric metric is reused when the current source is unavailable;
- unit conversion occurs before normalization and threshold interpretation;
- normalized progress is clamped to `0..1`;
- `Reverse` changes progression direction without rewriting the source metric;
- Image Fit and Progress Mode are independent concepts;
- image file paths are dashboard-relative, not arbitrary absolute paths;
- Editor preview and Runtime share the canonical renderer semantics.

Changing these persisted or rendering contracts requires intentional compatibility review and deterministic regression coverage where applicable.

### Implementation References

Source reviewed: [GitHub commit 84728cd](https://github.com/Jim1537/PinkieSysMon/tree/84728cd957c7b674065a4783c84e7c40d19eb286) (`main` at the time of review).

Key files: `src/PinkieSysMon/DashboardModel/WidgetDefinitions.cs`, `src/PinkieSysMon/DashboardModel/ImageDefinitions.cs`, `src/PinkieSysMon/BarImageContract.cs`, `src/PinkieSysMon/Widgets/BarWidgetRenderer.cs`, `src/PinkieSysMon/Widgets/QuantitativeIndicator.cs`, and `src/PinkieSysMon.Editor/CanonicalPropertyViews.cs`.
