# Power

The **Power** widget displays the current state of a Windows-reported UPS or battery using a state-specific icon, image, or text presentation. It is intended for status indicators such as **Online**, **On Battery**, **Charging**, **Low**, **Critical**, and **Unavailable**.

Power uses a defined set of power states rather than evaluating an arbitrary metric. Each state has an independent visual profile, so its appearance can be customized without changing how the underlying power status is detected.

The widget is a **status indicator**, not a power-management control. It does not change the power source, send commands to a UPS, or calculate battery capacity and runtime on its own.

---
## User Guide

### Step 1. Add a Power widget

Open a dashboard in **PinkieSysMon Dashboard Editor**, select `Power` from the `Add Widget` or `Add widget` toolbar menu, and select the new widget on the Canvas.

!!! info
    A new Power widget starts with `Source = power.ups`, `Width = 96`, `Height = 96`, and `X = 100`, `Y = 100`.

    The nine states initially use battery-themed icons. These are default visual assets, not nine separate Power widgets.


### Step 2. Select UPS or Battery

Open `General → Source` and change `Source` to one of the supported logical power sources:

| Source | Meaning | State metric used internally |
| --- | --- | --- |
| `power.ups` | UPS device classified by the built-in Windows power telemetry source. | `system.power.ups.state` |
| `power.battery` | Battery device classified by the built-in Windows power telemetry source. | `system.power.battery.state` |

These are **logical source selections**, not generic metric IDs. Power does not have an arbitrary `Metric` chooser like [Binary](binary.md) or [Gauge](gauge.md).

The source uses the built-in [System](../telemetry/system.md) provider, which obtains power information through Windows `Win32_Battery`. Libre Hardware Monitor and iCUE are not interchangeable Power-source options for this widget.

!!! warning
    Choosing `power.ups` does not guarantee that Windows reports a UPS. When no matching device is available, the appropriate visual result is **Unavailable**, not a fabricated online or battery state.

    Power's UPS detection follows the Windows device information it receives. It is not a general-purpose UPS network-protocol client.


See [Power Source](properties.md#power-source) and [System Provider](../telemetry/system.md).

### Step 3. Understand the nine power states

Open the `States` tab. Power provides a visual profile for each supported state:

| Editor group | Canonical state | General meaning |
| --- | --- | --- |
| `State: Online` | `online` | Windows reports external/AC power available without discharging. |
| `State: On Battery` | `on-battery` | Windows reports discharge / battery-powered operation. |
| `State: Charging` | `charging` | Windows reports charging. |
| `State: Low` | `low` | Windows reports a low condition. |
| `State: Critical` | `critical` | Windows reports a critical condition. |
| `State: Fully Charged` | `fully-charged` | Windows reports the battery as fully charged. |
| `State: Normal` | `normal` | Windows reports the designated normal battery status. |
| `State: Unknown` | `unknown` | Windows reports an unrecognized or unspecified battery status (including a missing status code), or the state token is unrecognized. |
| `State: Unavailable` | `unavailable` | No usable power-status reading is available. |

The exact state depends on what Windows reports. Power does not infer `Low` or `Critical` from custom charge-percentage thresholds in the widget.

!!! info
    **Unknown** and **Unavailable** are different. Unknown means the state cannot be classified into one of the recognized operating states; Unavailable means there is no usable status reading or the provider reports the unavailable condition.

    Unlike [Binary](binary.md), Power has a dedicated `State: Unavailable` profile. It can draw that profile even when no state metric is present in the current snapshot.


### Step 4. Configure the visual representation of each state

For every group on the `States` tab, choose `Source Type`:

| Source Type | What it displays |
| --- | --- |
| `Icon` | An icon from the application's installed shared icon library. |
| `Image` | A dashboard-relative image file. |
| `Value` | The **normalized state text**, such as `online`, `on-battery`, or `unavailable`. |

For `Icon` and `Image`, select the corresponding `Source`.

For `Value`, the visual is text generated from the resolved state; the `Source` asset field is not applicable. This is a state text presentation, not a user-entered label and not a numerical power measurement.

You may mix the three types across the nine states. For example, use an animated icon for `Charging`, an orange icon for `Low`, a red image for `Critical`, and text for `Unavailable`.

!!! warning
    `Source Type = Value` does **not** display charge percentage or remaining battery runtime. It displays the current canonical state key. To show charge or runtime, add a separate [Text / Value](text-value.md) widget using one of the System power metrics listed below.


See [State Source Type](properties.md#state-source-type) and [Image Source](properties.md#image-source).

### Step 5. Customize graphical states

For profiles using `Icon` or `Image`, open the `Image` tab. The properties are organized by state:

- `Fit` controls how the source is positioned within the widget bounds: `Contain`, `Cover`, or `Stretch`.
- `Loop` controls repetition for a source that supports animation. A static icon does not gain animation when Loop is enabled.
- `Color` controls tint for icons that support tinting. It is not a universal recoloring control for file-backed images.
- `Opacity` controls the visual transparency for the selected state.

Set these independently for states that need different appearances. The `Appearance` tab supplies shared widget background, border, and shadow settings.

See [Image Fit](properties.md#image-fit), [Image Loop](properties.md#image-loop), [State Color](properties.md#state-color), and [Opacity](properties.md#opacity).

### Step 6. Customize text states

If a state uses `Value`, configure its presentation in the `Text` tab. Each Value state has independent settings for:

- foreground color and opacity;
- font family, size, weight, and italic styling;
- horizontal and vertical alignment;
- outline color and width;
- overflow mode and applicable `Scroll` / `Bump` controls.

For example, a Value-based `State: Critical` can show `critical` with a large red font, while `State: Unavailable` shows `unavailable` in a muted color.

The words are the canonical state identifiers. Power does not provide an editor field for replacing `on-battery` with an arbitrary phrase such as `Running on UPS`.

See [Font Family](properties.md#font-family), [Overflow Mode](properties.md#overflow-mode), and [Widget Properties — Tabs](properties-by-tab.md).

### Step 7. Add charge and runtime readings if needed

Power selects a state, while Text / Value can display the associated numeric measurements. The System provider exposes these power metrics when the corresponding data is available:

| Reading | UPS metric ID | Battery metric ID |
| --- | --- | --- |
| Charge | `system.power.ups.charge` | `system.power.battery.charge` |
| Estimated remaining runtime | `system.power.ups.runtime.remaining` | `system.power.battery.runtime.remaining` |
| Current state as a metric | `system.power.ups.state` | `system.power.battery.state` |

Charge is a percentage-type metric. Remaining runtime uses seconds as its canonical source unit and can be formatted through the Text / Value widget's supported duration presentation.

Place a Text / Value widget alongside Power when you want the status icon **and** a numerical reading. Missing charge or runtime data must remain unavailable; selecting a Power source does not manufacture measurements.

For metric availability, acquisition details, and limitations, see [System Provider — Power Telemetry](../telemetry/system.md).

### Usage Examples

| Scenario | Suggested configuration | Expected result |
| --- | --- | --- |
| UPS status icon | `Source = power.ups`; use an Icon source in each state profile. | Icon changes when Windows reports a different UPS state; Unavailable is used if no usable state exists. |
| Laptop battery status | `Source = power.battery`. | Displays the normalized state of the Windows-classified battery device. |
| Critical power warning | Customize `State: Critical` with a conspicuous red icon/image; customize `State: Low` separately. | The two operating states have distinguishable presentations, using Windows-reported state classifications. |
| Text-only status | Set all nine states to `Source Type = Value`; configure state-specific colors and fonts. | Displays canonical state text with state-dependent styling. |
| UPS information cluster | Power icon with `power.ups` plus separate Text / Value widgets bound to `system.power.ups.charge` and `system.power.ups.runtime.remaining`. | Combines a categorical state indicator with available numeric charge/runtime readings. |
| Missing UPS | Select `power.ups` on a system where Windows does not report a classified UPS. | Uses the `State: Unavailable` visual profile, rather than retaining an old state or selecting Online. |

---
## Technical Information

### Purpose and Contract Boundary

Power is a canonical state-driven widget with a fixed logical power-source selector and a nine-state visual-profile contract.

- User-facing widget name: `Power`.
- Persisted type discriminator: `power`.
- Canonical model: `DashboardModel.PowerWidgetDefinition`.
- Renderer: `Widgets.PowerWidgetRenderer` (inherits `StateProfileWidgetRenderer`).
- Source and normalization contract: `PowerMetricContract`.
- State evaluation and profile resolution: `WidgetRenderContext.TryResolvePowerVisualProfile`.
- Visual-profile specification: `StateVisualProfileContract`.
- Numeric power telemetry ownership: the built-in Windows power source, not the Power widget.

Power does not independently interrogate hardware. It selects a predefined System telemetry metric based on `powerSource`, normalizes its value, and renders the matching state profile.

### Canonical Model and Persistence

`PowerWidgetDefinition` extends `StateVisualWidgetDefinition`, which extends the common `WidgetDefinition`.

| JSON field | Model member | Meaning |
| --- | --- | --- |
| `type` | `PowerWidgetDefinition.Type` | Fixed discriminator `power`. |
| `powerSource` | `PowerSource` | `power.ups` or `power.battery`. |
| `profiles` | `StateVisualWidgetDefinition.Profiles` | Nine required state-specific visual definitions. |

The widget also inherits `id`, `name`, `z`, `x`, `y`, `width`, `height`, `rotation`, background/border/shadow settings, and common visual properties from `WidgetDefinition`.

`powerSource` and `profiles` are semantically distinct: the source decides **which status is read**, while the profile dictionary decides **how that status appears**.

Power does not persist a generic `metric` field. In particular, `power.ups` is **not** a telemetry metric ID.

### Source-to-Metric Mapping

`PowerMetricContract.StateMetric` provides the fixed mapping:

| Power source | Required status metric |
| --- | --- |
| `power.ups` | `system.power.ups.state` |
| `power.battery` | `system.power.battery.state` |

`DashboardMetricUsage.Collect` requests only the selected Power state metric for a Power widget. Charge and runtime are separate metrics, requested when another dashboard element actually needs them.

The Power widget depends on the System power telemetry source, which reads `Win32_Battery` through WMI and publishes separate UPS and Battery metric groups. Its supported classification uses the device's WMI identity to distinguish UPS-like devices from non-UPS batteries; current code selects the first member of each group.

The provider normalizes charge to a percent reading and translates estimated runtime from WMI minutes into seconds where available. It must not invent values for missing power devices.

The Power widget itself only consumes the state metric. For telemetry-source details, see [System Provider](../telemetry/system.md).

### State Normalization

`PowerMetricContract.NormalizeState` converts the incoming state value to text, trims it, and maps it case-insensitively to the persisted state keys.

| Canonical key | Accepted status tokens |
| --- | --- |
| `online` | `online` |
| `on-battery` | `on-battery`, `onbattery` |
| `charging` | `charging` |
| `low` | `low` |
| `critical` | `critical` |
| `fully-charged` | `fully-charged`, `fullycharged` |
| `normal` | `normal` |
| `unknown` | `unknown` and other non-empty unrecognized values |
| `unavailable` | `unavailable`, missing value, `null`, or whitespace-only value |

State normalization is intentionally not a generic Boolean or numerical comparison. It does not accept arbitrary custom threshold definitions or try to derive a battery state from a charge percentage.

#### WMI battery-status mapping

`PowerMetricContract.FromWmiBatteryStatus` recognizes the following Windows status codes:

| WMI code | Normalized state |
| --- | --- |
| `1` | `on-battery` |
| `2` | `online` |
| `3` | `fully-charged` |
| `4`, `8` | `low` |
| `5`, `9` | `critical` |
| `6`, `7` | `charging` |
| `10` | `unavailable` |
| `11` | `normal` |
| Other or missing code | `unknown` |

The mapping preserves reported low/critical conditions for the WMI combinations that indicate charging and low/critical simultaneously. It does not replace the reported state with a calculation from charge percentage.

### State Selection and Unavailable Semantics

The canonical render-context sequence is:

- Resolve the state metric corresponding to `PowerSource`.
- Read that metric from the current telemetry snapshot.
- Pass the raw value to `PowerMetricContract.NormalizeState`.
- Select the profile with the normalized state key.
- Render the chosen profile using the common state-profile renderer.

`WidgetRenderContext.TryResolvePowerVisualProfile` returns a resolved profile even when the metric is absent: the missing value normalizes to `unavailable`. Thus, **Unavailable is a valid Power state**, not an instruction to render nothing.

`unknown` is distinct from `unavailable`: a non-empty unrecognized token maps to Unknown, whereas missing/empty input maps to Unavailable.

This difference must survive provider failure, recovery, dashboard serialization, and Editor preview. Stale historical status values must not remain displayed indefinitely after a source is unavailable.

### Nine Visual Profiles

Power requires exactly these state keys in its `profiles` dictionary:

| State | Default icon |
| --- | --- |
| `online` | `lucide:battery-full` |
| `on-battery` | `lucide:battery` |
| `charging` | `lucide:battery-charging` |
| `low` | `lucide:battery-low` |
| `critical` | `lucide:battery-warning` |
| `fully-charged` | `lucide:battery-full` |
| `normal` | `lucide:battery` |
| `unknown` | `lucide:battery-warning` |
| `unavailable` | `lucide:battery-warning` |

--8<-- "widgets/profiles.md:profile-schema"

The default image profiles use `Fit = contain`, `Loop = true`, and full opacity. Loop only produces animation when the selected asset supports it.

### Value-State Text Semantics

When the selected profile is `Value`, `StateProfileWidgetRenderer` calls `WidgetRenderContext.ResolveStateValueText(widget, stateKey)`.

For Power, that function returns **the normalized state key itself**. Examples include `online`, `on-battery`, `charging`, and `unavailable`.

This is **not** `system.power.*.charge`, `system.power.*.runtime.remaining`, a formatted percentage, or a user-supplied label. That distinction is an important compatibility contract.

State text is rendered through `TextContentRenderer` and the profile's own `TextPresentationDefinition`. Its color and opacity belong to the profile rather than to a global text value field.

The Text tab exposes only Value-backed state groups; the Image tab exposes only graphical state groups.

### Image/Icon Rendering and Animation

`PowerWidgetRenderer` is a specialization of the shared state-profile renderer.

--8<-- "widgets/profiles.md:profile-rendering"

### Geometry and Text Overflow

--8<-- "widgets/profiles.md:profile-geometry"

For Power, **all nine** profiles must use Value before auto-width is allowed. Its widget-level geometry and appearance still include logical X/Y, Width/Height, rotation, background, border, and shadow.

### Editor Property Applicability

| Tab | Group | Configuration |
| --- | --- | --- |
| `General` | `Source` | `Source` chooses UPS or Battery. |
| `General` | `Geometry` | X, Y, Width, Height, Rotation. |
| `Appearance` | `Foreground / Background / Border / Shadow` | Common widget appearance. |
| `States` | Nine state groups | `Source Type` and graphical `Source` (where applicable). |
| `Image` | Graphical states | Fit, Loop, applicable Color/Tint, Opacity. |
| `Text` | Value states | Foreground, Font, Alignment, Outline, Overflow. |

Power has no general Data-tab Metric, Setpoint, Range, Unit, or Format controls; these belong to other widget types. There is no Gauge-specific configuration for Power.

Use [Widget Properties — Tabs](properties-by-tab.md) and [Widget Property Dictionary](properties.md) for authoritative property descriptions and applicability notes.

### Validation and Compatibility Contracts

The canonical definition requires:

- a supported `powerSource` value (`power.ups` or `power.battery`);
- all nine required state profiles, without unknown additional profile keys;
- valid graphical asset references for profiles whose `contentType` is `image`;
- valid text presentation definitions and finite profile opacity;
- positive Width and Height for graphical/mixed content or non-negative dimensions for all-Value content;
- valid inherited geometry, color, background, border, and shadow properties.

Compatibility-sensitive behavior includes:

- The persisted widget type remains `power`, with separate source and profile contracts.
- The Power source IDs remain logical dashboard selectors rather than System metric IDs.
- `power.ups` and `power.battery` map to their respective System state metrics.
- Unknown and Unavailable remain distinct, with their own configurable visual profiles.
- Missing telemetry selects Unavailable; it is not substituted with Online or an older status.
- Value-backed Power states display canonical state text, not charge or remaining runtime.
- Every state's presentation remains independently configurable.
- State source types preserve their Image/Icon/Value semantics, asset resolution rules, and text overflow constraints.
- Changes to state definitions, normalization, serialization, or rendering require deliberate compatibility assessment and deterministic regression coverage where possible.

For current implementation source, see [PinkieSysMon on GitHub](https://github.com/Jim1537/PinkieSysMon).
