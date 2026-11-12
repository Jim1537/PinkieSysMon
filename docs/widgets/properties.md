# Widget Property Dictionary

## Common inherited widget fields

<!-- --8<-- [start:widget-base-fields] -->

Schema 29 gives **every widget** a common Panel; schema 30 extends those same graphical properties to the **root Canvas**, which remains a separate document root, not a widget. Panel owns geometry, corner radius, border, shadow and two independent graphical layers. Widget content styling is separate.

| Editor | Group | Fields |
| --- | --- | --- |
| General | Identity | Name, Type (read-only), ID (read-only) |
| General | Data Source | Metric and applicable source bindings |
| Panel | Geometry | X, Y, Width, Height, Padding, Rotation, Corner Radius, Z-order (read-only) |
| Panel | Background | Mode and contextual Color or Source |
| Panel | Foreground | Mode and contextual Color or Source |
| Panel | Border | Width, Color, Style, Top, Right, Bottom, Left |
| Panel | Shadow | Enabled, Color, Blur, Offset X, Offset Y |

Each Panel layer independently chooses **None**, **Color**, **Image**, or **Icon**. None draws nothing. Color uses its RGBA alpha. File-backed Image uses only its embedded alpha. Icon supports tint if the asset is tintable, and an additional Opacity. Panel source pickers appear only for Image/Icon; their Fit/Loop and icon-only Opacity/Color appear on the contextual Image tab. Switching modes retains dormant settings, and inactive file references are not resolved.

**Draw order:** Panel Background → widget content → Border → Panel Foreground. Both layers cover the **full panel bounds**, including the Padding area. Padding insets only the content. An opaque Foreground may hide the Border. An Image widget can therefore display three independent images: Background, own padded content image, and Foreground.

Common persisted widget properties include id, name, z, x, y, width, height, padding, rotation, color (content), panelBackground, panelForeground, borderColor, borderWidth, borderStyle, borderTop/Right/Bottom/Left, cornerRadius, shadowEnabled, shadowOffsetX/Y, shadowBlur, and shadowColor. Schema 29 replaces legacy widget backgroundColor with the panelBackground layer and introduces panelForeground = None. Schema 30 gives Canvas the same layer, border/radius and shadow fields, but **no widget ID, X/Y, rotation, Z-order or padding**; its Width/Height/Orientation remain the root raster geometry. Canvas Shadow is implemented for Desktop-only presentation (see the [Desktop guide](../outputs/desktop.md#session-and-native-alpha-presentation)); physical output ignores it. The existing widget content color remains independent.

Padding accepts one nonnegative value or four numbers in top|right|bottom|left order, such as 0|0|0|8. Border Width 0 disables remaining border controls without deleting their values. Corner Radius clips rounded content and graphical panel layers. Schema 32 replaces separate Shadow Opacity with the alpha channel of Shadow Color for both widgets and Canvas. Migration folds the previous color alpha × opacity into one 8-bit alpha, including disabled shadows. New shadow colors default to `#80000000` (the former opaque black at 0.5 opacity). Legacy documents are upgraded once on loading; retain a pre-migration copy for older applications.

<!-- --8<-- [end:widget-base-fields] -->



## Background Color

<!-- --8<-- [start:background-color] -->

The Canvas uses **Panel → Background → Mode = Color → Color** (JSON: `canvas.panelBackground.mode` / `canvas.panelBackground.color`). Schema 30 removes the legacy `canvas.backgroundColor` and the former color-under-image composition. Canvas Background and Foreground independently choose None, Color, Image or Icon.

Every widget uses **Panel → Background → Mode = Color → Color**, with RGBA alpha and persisted fields panelBackground.mode/panelBackground.color. In Image/Icon mode the same panel layer uses Source instead. Legacy widget backgroundColor is converted automatically when moving from schema 28 to 29.

<!-- --8<-- [end:background-color] -->

---
## Bar Mode


<!-- --8<-- [start:bar-mode] -->

!!! info ""

    **Editor name:** Mode  
    **Semantic key:** `gauge.bar.content_mode`  
    **Canonical model:** `BarWidgetDefinition.ContentMode`  
    **JSON:** `contentMode`


    **Values:**
    - `Fill`
    - `Image`


    Selects standard fill rendering or image-backed Bar rendering.

    **Used by:** Bar

<!-- --8<-- [end:bar-mode] -->

---
## Bar Track Color

<!-- --8<-- [start:bar-track-color] -->

**Gauge → Bar → Track Color** sets the background of the Bar track in Fill and Image modes. It remains visible with zero progress or missing telemetry. Schema 28 stores the setting in `track.backgroundColor`. Older dashboards acquire a transparent track (`#00000000`) on migration. To reopen a dashboard in an older application, use its pre-migration copy.

<!-- --8<-- [end:bar-track-color] -->

## Border Color


<!-- --8<-- [start:border-color] -->

!!! info ""

    **Editor name:** Color  
    **Semantic key:** `appearance.border.color`  
    **Canonical model:** `WidgetDefinition.BorderColor`  
    **JSON:** `borderColor`


    **Values:**
    - Color.


    Border color of the widget container.

    **Used by:** All widgets

<!-- --8<-- [end:border-color] -->

---
## Border Width


<!-- --8<-- [start:border-width] -->

!!! info ""

    **Editor name:** Width  
    **Semantic key:** `appearance.border.width`  
    **Canonical model:** `WidgetDefinition.BorderWidth`  
    **JSON:** `borderWidth`


    **Values:**
    - Finite number `>= 0`.


    Border width of the widget container.

    **Used by:** All widgets

<!-- --8<-- [end:border-width] -->

---
## Bump Pause


<!-- --8<-- [start:bump-pause] -->

!!! info ""

    **Editor name:** Bump Pause  
    **Semantic key:** `text.value.overflow.bump_pause` (Text / Value); `text.state.{state}.overflow.bump_pause` (Value state)

    **Canonical model:** `TextPresentationDefinition.BumpPauseMs`  
    **JSON:** `textPresentation.bumpPauseMs` (Text / Value); `profiles.{state}.textPresentation.bumpPauseMs` (Value state)


    **Values:**
    - Integer `>=0` milliseconds; default `500`.


    Pause at the edges during Bump.

    **Used by:** Text / Value and Value state profiles

    **Disabled / read-only when:**
    - [Overflow Mode](properties.md#overflow-mode)!=`Bump`.

<!-- --8<-- [end:bump-pause] -->

---
## Component

<!-- --8<-- [start:component] -->

!!! info ""

    **Editor name:** Component
    **Semantic keys:** `data.quantitative.datetime_component` / `data.binary.datetime_component`
    **Canonical model:** `NumericWidgetDefinition.Component` / `BinaryWidgetDefinition.Component`
    **JSON:** `component` (optional; Gauge/Bar since schema 20, Binary since schema 21, Graph since schema 23)

    **Choices and whole-component ranges (`Format = 0`):**

    | Choice | JSON token | Discrete value |
    | --- | --- | --- |
    | Select component | `null` | Unavailable until selected |
    | Year | `year` | Civil year, 1–9999 |
    | Month | `month` | 1–12 |
    | Day of month | `dayOfMonth` | 1–28/29/30/31, depending on month |
    | Day of year | `dayOfYear` | 1–365/366, depending on leap year |
    | Day of week (ISO) | `dayOfWeek` | Monday=1 through Sunday=7 |
    | Hour (24-hour) | `hour24` | 0–23 |
    | Hour (12-hour) | `hour12` | 1–12; noon and midnight are 12 |
    | Hour (12-cycle) | `hour12Cycle` | 0–11; noon and midnight are 0 |
    | Minute | `minute` | 0–59 |
    | Second | `second` | 0–59 |

    A DateTime/DateTimeOffset metric requires one selected Component before Gauge,
    Bar, Graph, or Binary can use its numeric value. DateTimeOffset uses local civil time,
    as does DateTime text formatting. Compound date/time masks such as `hh:mm`
    are not Components. Missing or unconvertible readings, or no Component,
    remain unavailable, never a fabricated False state.

    **Numeric precision (Editor: `Data → Value → Format`):**

    **Semantic keys:** `data.quantitative.datetime_component_format` /
    `data.binary.datetime_component_format`
    **Canonical model:** `NumericWidgetDefinition.ComponentFormat` /
    `BinaryWidgetDefinition.ComponentFormat`
    **JSON:** `componentFormat` (optional, schema 22)

    | Format | Effective numeric projection |
    | --- | --- |
    | `0` (default) | Exact whole calendar/clock component; no fraction |
    | `0.0` | Component plus elapsed lower-unit proportion, truncated to tenths |
    | `0.00` | Component plus elapsed lower-unit proportion, truncated to hundredths |

    The fraction is elapsed time within the component's own interval: within a
    year for Year, its actual month for Month, the local day for day/weekday,
    the hour for hour, the minute for Minute and the second for Second.
    Year and Month respect variable lengths and leap years. The projection uses
    tick-based integer quantization **toward the component's whole value**,
    not .NET display rounding. It never reaches the following integer before
    the actual boundary: October stays below 11, even at the last tick of October.
    The default `0` is represented by an absent `componentFormat` on existing
    and newly created dashboards; changing this setting affects geometry and
    Binary evaluation, not just printed text. For the live System clock's
    per-frame updates in current-value widgets, see [System telemetry](../telemetry/system.md#operating-system-and-general-system-metrics).

    Graph projects each stored historical timestamp; it does not substitute the
    current frame time for past DateTime samples.

    With fractional precision, ranges are half-open: Year [1,10000),
    Month [1,13), Day of month [1,32) (with the actual month ending earlier),
    Day of year [1,367) (365/366 days), ISO weekday [1,8),
    Hour24 [0,24), Hour12 [1,13), Hour12Cycle [0,12),
    Minute and Second [0,60).
    Set `Min`/`Max` to the desired scale rather than assuming `Max=12`
    shows fractional progress during month 12 or hour12=12. Existing
    Hour12 can use 1–13 for its 1–12.99 scalar range; Minute/Second use 0–60.
    For a 12-hour analog clock, select **Hour (12-cycle)** with `Min=0` and
    `Max=12`. It projects `hour % 12` plus the truncated hour fraction,
    returning zero at noon and midnight and approaching 12 immediately before
    those boundaries. Binary `Auto` evaluates that genuine zero as False.
    Values wrap at civil calendar boundaries; fractional projection does not
    smooth the discontinuity between the last and first components.

    Binary `Auto` evaluates zero/nonzero on this actual numeric scalar;
    Setpoint compares the same scalar using the existing operators, alternatives,
    and inclusive ranges. With fractional Format, `Month = 10` may match
    only during the initial interval still quantized to 10, not throughout October;
    use `0` for whole-month equality or an appropriate numeric range when
    interpolation is wanted. Unavailable does not mean False.

    Binary's existing persisted `format` **still controls only Value-state
    DateTime text**, with .NET format masks such as `yyyy-MM-dd`. When a Binary
    state displays a Value, that separate field is labeled **Text Format**.
    The numeric `componentFormat` is available in Auto/Setpoint and icon/image-only
    states as well. Schema 21 → 22 migration leaves legacy text masks and
    absent numeric precision untouched; it never creates an unselected Component.

    **Used by:** Gauge, Bar, Graph, Binary when Metric is DateTime.

<!-- --8<-- [end:component] -->

---
## Corner Radius


<!-- --8<-- [start:corner-radius] -->

!!! info ""

    **Editor name:** Corner Radius  
    **Semantic key:** `appearance.border.corner_radius`  
    **Canonical model:** `WidgetDefinition.CornerRadius`  
    **JSON:** `cornerRadius`


    **Values:**
    - Finite number `>= 0`.


    Corner radius of the widget container.

    **Used by:** All widgets

<!-- --8<-- [end:corner-radius] -->

---
## End Angle


<!-- --8<-- [start:end-angle] -->

!!! info ""

    **Editor name:** End Angle  
    **Semantic key:** `gauge.arc.end`  
    **Canonical model:** `GaugeWidgetDefinition.EndAngle`  
    **JSON:** `endAngle`


    **Values:**
    - Degrees; the contract requires `0 <= [Start Angle](properties.md#start-angle) < End Angle <= 360`.


    Ending angle of the arc.

    **Used by:** Gauge

<!-- --8<-- [end:end-angle] -->

---
## Endpoint Type Override


<!-- --8<-- [start:endpoint-type-override] -->

!!! info ""

    **Editor name:** Friendly Name of the discovered Windows media endpoint  
    **Semantic key:** `EndpointOverride:{EndpointId}` (dynamic per endpoint)  
    **Canonical model:** `AppConfig.Media.EndpointTypeOverrides[EndpointId]`  
    **JSON:** `not dashboard JSON; application config`


    **Values:**
    - `Auto`
    - `remote-network`
    - `speakers`
    - `line-level`
    - `headphones`
    - `microphone`
    - `handset`
    - `digital-passthrough`
    - `spdif`
    - `display-audio`
    - `unknown`


    Overrides the automatically detected endpoint type. `Auto` removes the override.

    **Used by:** Media System when Windows media endpoints are discovered

    **Hidden / tab omitted when:**
    - No media endpoints are discovered.

<!-- --8<-- [end:endpoint-type-override] -->

---
## Evaluation Mode


<!-- --8<-- [start:evaluation-mode] -->

!!! info ""

    **Editor name:** Mode  
    **Semantic key:** `data.binary.evaluation.mode`  
    **Canonical model:** `BinaryWidgetDefinition.EvaluationMode`  
    **JSON:** `evaluationMode`


    **Values:**
    - `Auto`
    - `Setpoint`


    Defines how the Metric is converted to True/False.

    **Used by:** Binary

<!-- --8<-- [end:evaluation-mode] -->

---
## Fallback


<!-- --8<-- [start:fallback] -->

!!! info ""

    **Editor name:** Fallback  
    **Semantic key:** `data.display.fallback`  
    **Canonical model:** `ValueWidgetDefinition.Fallback` / `BinaryWidgetDefinition.Fallback`  
    **JSON:** `fallback`


    **Values:**
    - Text; default `--`.


    Text used when the value is unavailable.

    **Used by:** Text / Value, Binary with Value state

<!-- --8<-- [end:fallback] -->

---
## Fill Enabled

<!-- --8<-- [start:fill-enabled] -->

!!! info ""

    **Editor name:** Fill Enabled  
    **Semantic key:** `graph.fill_enabled`  
    **Canonical model:** `GraphWidgetDefinition.FillEnabled`  
    **JSON:** `fillEnabled`

    **Values:** Boolean; default `false`.

    Enables the area below a Line.

    **Used by:** Graph Line; read-only in Columns mode.

<!-- --8<-- [end:fill-enabled] -->

---
## Fill Opacity

<!-- --8<-- [start:fill-opacity] -->

!!! info ""

    **Editor name:** Fill Opacity  
    **Semantic key:** `graph.fill_opacity`  
    **Canonical model:** `GraphWidgetDefinition.FillOpacity`  
    **JSON:** `fillOpacity`

    **Values:** Finite number `0..1`; default `0.25`.

    Multiplies the selected foreground/threshold alpha once for the filled area.

    **Used by:** Graph Line with Fill Enabled; otherwise read-only.

<!-- --8<-- [end:fill-opacity] -->

---
## Filter

<!-- --8<-- [start:filter] -->

!!! info ""

    **Editor name:** Filter  
    **Semantic key:** `graph.filter`  
    **Canonical model:** `GraphWidgetDefinition.Filter`  
    **JSON:** `filter` (schema 25)

    **Values:** `None` (default), `Median`, `Mean`, `Kalman`.

    Processes displayed observations before scaling, interpolation and threshold colors.

    | Mode | Processing |
    | --- | --- |
    | None | Original converted values. |
    | Median | Median of the latest five continuous observations, including the current one; the available count is used during startup, with the middle pair averaged for an even count. |
    | Mean | Arithmetic mean of the latest five continuous observations, including the current one; the available count is used during startup. |
    | Kalman | Scalar random-walk estimate, initialized at the first observation; fixed normalized noise ratio `Q/R=0.1` per publication and initial `P/R=1`. This is a display smoothing preset, not a calibrated sensor model. |

    **Used by:** Graph Line and Columns. Filters can suppress peaks and delay response.
    Missing data and continuity breaks restart the filter. Shared raw history is preserved;
    repeated frames do not advance it. [Graph processing](graph.md#per-graph-signal-processing)
    owns cache, priming and lifetime details.

<!-- --8<-- [end:filter] -->

---
## Font Family


<!-- --8<-- [start:font-family] -->

!!! info ""

    **Editor name:** Family  
    **Semantic key:** `text.value.font.family` (Text / Value); `text.state.{state}.font.family` (Value state)

    **Canonical model:** `TextPresentationDefinition.FontFamily`  
    **JSON:** `textPresentation.fontFamily` (Text / Value); `profiles.{state}.textPresentation.fontFamily` (Value state)


    **Values:**
    - Bundled font family names from `assets/fonts` (TTF/OTF); default `Roboto`.


    The Editor provides an editable drop-down listing families discovered under the application's `assets/fonts` directory. You may type a family name, but Runtime rendering requires that font to be bundled; system-installed fonts are not used.

    **Used by:** Text / Value and Value state profiles

<!-- --8<-- [end:font-family] -->

---
## Font Size


<!-- --8<-- [start:font-size] -->

!!! info ""

    **Editor name:** Size  
    **Semantic key:** `text.value.font.size` (Text / Value); `text.state.{state}.font.size` (Value state)

    **Canonical model:** `TextPresentationDefinition.FontSize`  
    **JSON:** `textPresentation.fontSize` (Text / Value); `profiles.{state}.textPresentation.fontSize` (Value state)


    **Values:**
    - Finite number `>0`.


    Font size.

    **Used by:** Text / Value and Value state profiles

<!-- --8<-- [end:font-size] -->

---
## Font Weight


<!-- --8<-- [start:font-weight] -->

!!! info ""

    **Editor name:** Weight  
    **Semantic key:** `text.value.font.weight` (Text / Value); `text.state.{state}.font.weight` (Value state)

    **Canonical model:** `TextPresentationDefinition.FontWeight`  
    **JSON:** `textPresentation.fontWeight` (Text / Value); `profiles.{state}.textPresentation.fontWeight` (Value state)


    **Values:**
    - Integer `1..1000`.


    Font weight.

    **Used by:** Text / Value and Value state profiles

<!-- --8<-- [end:font-weight] -->

---
## Foreground Color

<!-- --8<-- [start:foreground-color] -->

WidgetDefinition.Color (JSON: color) is the color of the **widget's content**, never the Panel Foreground overlay.

| Widget | Editor owner |
| --- | --- |
| Text/Value | Text → Color: text RGBA |
| Bar | Gauge → Bar → Indicator Color: fill/threshold base, disabled in Image mode |
| Gauge | Gauge → Horseshoe → Indicator Color: active arc/threshold base |
| Graph | Graph → Plot → Color: plotted line/columns/threshold base |
| Image | Image → Image → Color: icon tint where supported; no tint for image files |
| Binary, Power, Media System, Media Player | Appearance → Content → Default Color: inherited by each state unless overridden |

A non-tintable icon retains a disabled Color row, and file-backed images have no Color row. Text transparency is the alpha channel of its RGBA color.

<!-- --8<-- [end:foreground-color] -->

---
## Format


<!-- --8<-- [start:format] -->

!!! info ""

    **Editor name:** Format  
    **Semantic key:** `data.value.format / data.binary.format`  
    **Canonical model:** `ValueWidgetDefinition.Format / BinaryWidgetDefinition.Format`  
    **JSON:** `format`


    **Values:**
    - Number / Percent / DataSize: `raw`, `0`, `0.0`, `0.00`.
    - Duration additionally supports `h:mm` and `h:mm:ss` when [Unit](properties.md#unit) is not `auto`.
    - DateTime presets include `yyyy-MM-dd HH:mm:ss`, `yyyy-MM-dd`, `yyyy`, `MMMM`, `MMM`, `MM`, `dd`, `dddd`, `ddd`, `HH:mm`, `HH:mm:ss`, `hh:mm tt`, `hh:mm:ss tt`, `MMMM d`, `MMMM d, yyyy`, `dddd, MMMM d`, `dddd, MMMM d, yyyy`; custom .NET DateTime formats are validated before acceptance.


    Format of the displayed value. In DateTime Binary Value states the editor labels
    this field **Text Format** so it is distinct from the numeric
    [Component Format](properties.md#component). The persisted `format` field remains
    textual; numeric DateTime precision is persisted separately as `componentFormat`.

    **Used by:** Text / Value, Binary with Value state

    **Disabled / read-only when:**
    - Literal Text is not numeric.
    - The selected [Metric](properties.md#metric) is unavailable or has no descriptor.
    - The Metric has Text or Boolean value kind.
    - Duration [Unit](properties.md#unit)=`auto`.
    - Binary has no Value state.

<!-- --8<-- [end:format] -->

---
## Gap


<!-- --8<-- [start:gap] -->

!!! info ""

    **Editor name:** Gap

    **Semantic keys:**
    - `gauge.track.gap`
    - `gauge.bar.gap`

    **Canonical model:**
    - `GaugeWidgetDefinition.Gap`
    - `BarWidgetDefinition.Gap`

    **JSON:** `gap`


    **Values:**
    - Finite number `>= 0`.


    Spacing used by Gauge/Bar indicator geometry.

    **Used by:** Gauge, Bar

    **Gauge note:**
    - In the current Editor, Gap remains editable regardless of [Track Enabled](properties.md#track-enabled).
    - With the track enabled, `2*[Border Width](properties.md#track-border-width) + 2*Gap + [Thickness](properties.md#track-thickness)` must fit within half the smaller padded content dimension.

<!-- --8<-- [end:gap] -->

---
## Graph Mode

<!-- --8<-- [start:graph-mode] -->

!!! info ""

    **Editor name:** Mode  
    **Semantic key:** `graph.mode`  
    **Canonical model:** `GraphWidgetDefinition.Mode`  
    **JSON:** `mode`

    **Values:** `Line` (default), `Columns`.

    Selects the historical plot geometry. Columns retain but do not use Line settings.

    **Used by:** Graph

<!-- --8<-- [end:graph-mode] -->

---
## Height


<!-- --8<-- [start:height] -->

!!! info ""

    **Editor name:** Height  
    **Semantic key:** `general.geometry.height`  
    **Canonical model:** `WidgetDefinition.Height / CanvasDefinition.Height`  
    **JSON:** `height` (widgets); `canvas.height` (Canvas)


    **Values:**
    - **Bar / Image / Graph:** `> 0`.
    - **Gauge:** derived and equal to [Width](properties.md#width).
    - **Text / Value:** derived from the active text layout.
    - **State-driven:** derived only when all states use `Value`; otherwise a persisted positive dimension.
    - **Canvas:** positive integer.
    **Read-only:**
    - Gauge.
    - Text / Value.
    - Binary / Power / Media System / Media Player, when all states use [Source Type](properties.md#state-source-type) = `Value`.


    Height of the widget bounds in logical pixels; for Canvas, the native canvas height.

    **Used by:** All widgets, Canvas

<!-- --8<-- [end:height] -->

---
## History Window

<!-- --8<-- [start:history-window] -->

!!! info ""

    **Editor name:** History Window (seconds)  
    **Semantic key:** `graph.window_seconds`  
    **Canonical model:** `GraphWidgetDefinition.WindowSeconds`  
    **JSON:** `windowSeconds`

    **Values:** Finite number `1..600`; default `60`.

    Selects the rolling time interval. Increasing it cannot recover observations already evicted.

    **Used by:** Graph

<!-- --8<-- [end:history-window] -->

---
## Horizontal Alignment


<!-- --8<-- [start:horizontal-alignment] -->

!!! info ""

    **Editor name:** Horizontal  
    **Semantic key:** `text.value.align.horizontal` (Text / Value); `text.state.{state}.align.horizontal` (Value state)

    **Canonical model:** `TextPresentationDefinition.Align`  
    **JSON:** `textPresentation.align` (Text / Value); `profiles.{state}.textPresentation.align` (Value state)


    **Values:**
    - `left`
    - `center`
    - `right`


    Horizontal alignment.

    **Used by:** Text / Value and Value state profiles

<!-- --8<-- [end:horizontal-alignment] -->

---
## Image Fit


<!-- --8<-- [start:image-fit] -->

!!! info ""

    **Editor name:** Fit

    **Semantic keys:**
    - `image.asset.fit`
    - `image.state.{state}.fit`
    - `image.background_image.asset.fit` (legacy Graph plot)
    - `image.panel.background.fit`
    - `image.panel.foreground.fit`
    - `bar.image.fit`

    **Canonical model:**
    - `ImageAssetPresentationDefinition.Fit` (Image, state assets, Canvas layers, Graph background)
    - `BarImagePresentationDefinition.Fit` (Bar Image mode)

    **JSON:**
    - Image: `asset.fit`
    - State profile: `profiles.{state}.asset.fit`
    - Canvas background: `canvas.panelBackground.image.fit` or `canvas.panelBackground.icon.fit`
    - Canvas foreground: `canvas.panelForeground.image.fit` or `canvas.panelForeground.icon.fit`
    - Bar Image: `image.fit`
    - Graph background: `image.asset.fit`


    **Values:**
    - Image / state / Canvas / Graph assets: `contain`, `cover`, `stretch`.
    - Bar Image mode: `Clip`, `Contain`, `Cover`, `Stretch`.


    Controls how an image source is fitted inside the target bounds.

    **Used by:** Graph background image, Image, graphical state profiles, Canvas background/foreground image layers, Bar / [Mode](properties.md#bar-mode)=`Image`

    **Context notes:**
    - Canvas Panel image/icon Fit appears in the contextual Image tab when the corresponding Panel Mode selects Image or Icon. Graph legacy layers follow their own source applicability.

<!-- --8<-- [end:image-fit] -->

---
## Image Layer Color


<!-- --8<-- [start:image-layer-color] -->

!!! info ""

    **Editor name:** Color

    **Semantic keys:**
    - `image.background_image.color` (legacy Graph plot)
    - `image.panel.background.icon_color`
    - `image.panel.foreground.icon_color`

    **Canonical model:** `WidgetPanelLayerDefinition.IconColor` (Canvas and widgets); legacy Graph background retains `CanvasImageLayerDefinition.Color`

    **JSON:**
    - `canvas.panelBackground.iconColor`
    - `canvas.panelForeground.iconColor`
    - Graph background: `image.color`


    **Values:**
    - Color.


    Tint color of a tintable Panel icon layer or legacy Graph plot icon. File images retain their original pixel colors and alpha.

    **Used by:** Graph legacy plot image and Canvas/widget Panel icon layers

    **Disabled / read-only when:**
    - [Source](properties.md#image-source) is empty.
    - [Type](properties.md#image-source-type)=`Image` (file).
    - [Type](properties.md#image-source-type)=`Icon` but the selected icon does not expose tint capability.

<!-- --8<-- [end:image-layer-color] -->

---
## Image Loop


<!-- --8<-- [start:image-loop] -->

!!! info ""

    **Editor name:** Loop

    **Semantic keys:**
    - `image.asset.loop`
    - `image.state.{state}.loop`
    - `image.background_image.asset.loop` (legacy Graph plot)
    - `image.panel.background.loop`
    - `image.panel.foreground.loop`
    - `bar.image.loop`

    **Canonical model:**
    - `ImageAssetPresentationDefinition.Loop` (Image, state assets, Canvas layers, Graph background)
    - `BarImagePresentationDefinition.Loop` (Bar Image mode)

    **JSON:**
    - Image: `asset.loop`
    - State profile: `profiles.{state}.asset.loop`
    - Canvas background: `canvas.panelBackground.image.loop` or `canvas.panelBackground.icon.loop`
    - Canvas foreground: `canvas.panelForeground.image.loop` or `canvas.panelForeground.icon.loop`
    - Bar Image: `image.loop`
    - Graph background: `image.asset.loop`


    **Values:**
    - Boolean.


    Controls looping of an animated graphical source.

    **Used by:** Graph background image, Image, graphical state profiles, Canvas background/foreground image layers, Bar / [Mode](properties.md#bar-mode)=`Image`

    **Context notes:**
    - Loop is editable when the configured file or icon reports multiple frames. Static, unresolved and automatic Shell artwork keeps the stored Loop value.

<!-- --8<-- [end:image-loop] -->

---
## Image Source


<!-- --8<-- [start:image-source] -->

!!! info ""

    **Editor name:** Source; standalone Image displays Image or Icon dynamically in General → Image Source

    **Semantic keys:**
    - `image.content.source`
    - `panel.background.source`
    - `panel.foreground.source`
    - `states.{state}.asset.source`
    - `bar.image.source`

    **Canonical model:**
    - `ImageAssetPresentationDefinition.Source` (Image, Canvas layers, state assets, Graph background)
    - `BarImagePresentationDefinition.Source` (Bar Image mode)

    **JSON:**
    - Image: `asset.source`
    - Canvas background: `canvas.panelBackground.image.source` or `canvas.panelBackground.icon.source`
    - Canvas foreground: `canvas.panelForeground.image.source` or `canvas.panelForeground.icon.source`
    - State profile: `profiles.{state}.asset.source`
    - Bar Image: `image.source`
    - Graph background: `image.asset.source`


    **Values:**
    - File-backed contexts: dashboard-relative image file.
    - Icon-backed contexts: global icon logical name.
    - Bar Image mode: dashboard-relative image file.


    Active graphical source. An empty source is a valid unconfigured image for Image, Graph background, Bar Image mode, Canvas layers, and graphical state profiles; each configured source must resolve and decode for normal use.

    If artwork is missing or damaged, Editor opens the dashboard and lists the affected sources. Hover over **Repair artwork** in the status bar to see the remaining references. Replace each source or clear it. Other layers remain visible while you edit. Save checks the files again before writing the dashboard.

    **Used by:** Graph background image, Image, graphical state profiles, Bar / [Mode](properties.md#bar-mode)=`Image`, Canvas background/foreground image layers

    **Context notes:**
    - Standalone Image content selects its source in **General → Image Source**, separately from the Panel layers and the contextual Image presentation tab.
    - State profiles disable Source when [Source Type](properties.md#state-source-type)=`Value`.
    - Shared image assets clear Source when [Image Source Type](properties.md#image-source-type) changes between file and icon.
    - Bar Image mode is file-backed only.

<!-- --8<-- [end:image-source] -->

---
## Image Source Type


<!-- --8<-- [start:image-source-type] -->

!!! info ""

    **Editor name:** Type (standalone Image: **General → Image Source**)

    **Semantic keys:**
    - `image.content.kind`
    - `panel.background.mode`
    - `panel.foreground.mode`

    **Canonical model:** `ImageAssetPresentationDefinition.SourceType`

    **JSON:**
    - Image: `asset.sourceType`
    - Canvas Background/Foreground: select `panelBackground.mode` / `panelForeground.mode`; their Image and Icon assets have fixed `sourceType` (`file` / `icon`)
    - Graph background: `image.asset.sourceType`


    **Values:**
    - Editor `Image` → canonical/JSON `file`.
    - Editor `Icon` → canonical/JSON `icon`.


    Selects whether a shared image asset uses a dashboard-relative file or a global icon.

    **Used by:** Graph background image, Image, Canvas background image layer, Canvas foreground image layer

    **Notes:**
    - Changing the source type clears the current [Source](properties.md#image-source).

<!-- --8<-- [end:image-source-type] -->

---
## Interpolation

<!-- --8<-- [start:interpolation] -->

!!! info ""

    **Editor name:** Interpolation  
    **Semantic key:** `graph.interpolation`  
    **Canonical model:** `GraphWidgetDefinition.Interpolation`  
    **JSON:** `interpolation` (schema 25)

    **Values:** `Linear` (default), `Bezier`.

    Chooses straight segments or a shape-preserving cubic Bezier curve through displayed
    observations. Area fill follows the same boundary. This changes geometry without
    averaging the values; [Filter](#filter) is independent.

    **Used by:** Graph Line; visible but read-only in Columns mode, with its value retained.

<!-- --8<-- [end:interpolation] -->

---
## Italic


<!-- --8<-- [start:italic] -->

!!! info ""

    **Editor name:** Italic  
    **Semantic key:** `text.value.font.italic` (Text / Value); `text.state.{state}.font.italic` (Value state)

    **Canonical model:** `TextPresentationDefinition.Italic`  
    **JSON:** `textPresentation.italic` (Text / Value); `profiles.{state}.textPresentation.italic` (Value state)


    **Values:**
    - Boolean.


    Italic.

    **Used by:** Text / Value and Value state profiles

<!-- --8<-- [end:italic] -->

---
## Max


<!-- --8<-- [start:max] -->

!!! info ""

    **Editor name:** Max  
    **Semantic key:** `data.range.max`  
    **Canonical model:** `NumericWidgetDefinition.Max`  
    **JSON:** `max`


    **Values:**
    - Finite number; must be > [Min](properties.md#min) for Gauge, Bar, and Graph Fixed scale.


    Upper bound of the quantitative range.

    **Used by:** Gauge, Bar, Graph

    **Disabled / read-only when:**
    - Graph [Scale Mode](properties.md#scale-mode)=`Auto`; the stored value is retained for Fixed mode.

<!-- --8<-- [end:max] -->

---
## Media Player Metric


<!-- --8<-- [start:media-player-metric] -->

!!! info ""

    **Editor name:** Metric  
    **Semantic key:** `general.source.metric`<br>
    **Canonical model:** `derived fixed source`  
    **JSON:** `not persisted`


    **Values:**
    - `system.media.playback.status`
    **Read-only:**
    - Always read-only.


    Media Player has no persisted fake source/metric domain property. Editor exposes the fixed playback-status metric as a derived read-only property.

    **Used by:** Media Player

<!-- --8<-- [end:media-player-metric] -->

---
## Media Source


<!-- --8<-- [start:media-source] -->

!!! info ""

    **Editor name:** Source  
    **Semantic key:** `general.source.media`  
    **Canonical model:** `MediaSystemWidgetDefinition.MediaSource`  
    **JSON:** `mediaSource`


    **Values:**
    - `media.output`
    - `media.input`


    Selects the Windows media output or input endpoint domain. Mute and Volume remain telemetry metrics and are not state types.

    **Used by:** Media System

<!-- --8<-- [end:media-source] -->

---
## Metric


<!-- --8<-- [start:metric] -->

!!! info ""

    **Editor name:** Metric  
    **Semantic key:** `general.source.metric`  
    **Canonical model:** `ValueWidgetDefinition.Metric / BinaryWidgetDefinition.Metric / NumericWidgetDefinition.Metric`  
    **JSON:** `metric`


    **Values:**
    - Metric ID selected through the metric picker.


    Telemetry metric used by the widget as its data source.

    **Used by:** Text / Value, Binary, Gauge, Bar, Graph

    **Disabled / read-only when:**
    - Text / Value: [Type](properties.md#value-source-type)!=`Metric`.

<!-- --8<-- [end:metric] -->

---
## Min


<!-- --8<-- [start:min] -->

!!! info ""

    **Editor name:** Min  
    **Semantic key:** `data.range.min`  
    **Canonical model:** `NumericWidgetDefinition.Min`  
    **JSON:** `min`


    **Values:**
    - Finite number; must be < [Max](properties.md#max) for Gauge, Bar, and Graph Fixed scale.


    Lower bound of the quantitative range.

    **Used by:** Gauge, Bar, Graph

    **Disabled / read-only when:**
    - Graph [Scale Mode](properties.md#scale-mode)=`Auto`; the stored value is retained for Fixed mode.

<!-- --8<-- [end:min] -->

---
## Name


<!-- --8<-- [start:name] -->

!!! info ""

    **Editor name:** Name  
    **Semantic key:** `general.identity.name`  
    **Canonical model:** `WidgetDefinition.Name`  
    **JSON:** `name`


    **Values:**
    - Text; an empty value is persisted as `null`.


    Widget name used by Editor as the element label.

    **Used by:** All widgets

<!-- --8<-- [end:name] -->

---
## Needle Color


<!-- --8<-- [start:needle-color] -->

!!! info ""

    **Editor name:** Color  
    **Semantic key:** `gauge.needle.color`  
    **Canonical model:** `GaugeWidgetDefinition.Needle.Color`  
    **JSON:** `needle.color`


    **Values:**
    - Color.


    Color of the main needle line.

    **Used by:** Gauge

    **Disabled / read-only when:**
    - [Needle Enabled](properties.md#needle-enabled)=`false`.

<!-- --8<-- [end:needle-color] -->

---
## Needle Enabled


<!-- --8<-- [start:needle-enabled] -->

!!! info ""

    **Editor name:** Enabled  
    **Semantic key:** `gauge.needle.enabled`  
    **Canonical model:** `GaugeWidgetDefinition.Needle.Enabled`  
    **JSON:** `needle.enabled`


    **Values:**
    - Boolean.


    Enables the needle.

    **Used by:** Gauge

<!-- --8<-- [end:needle-enabled] -->

---
## Needle End Offset


<!-- --8<-- [start:needle-end-offset] -->

!!! info ""

    **Editor name:** End Offset  
    **Semantic key:** `gauge.needle.end_offset`  
    **Canonical model:** `GaugeWidgetDefinition.Needle.EndOffset`  
    **JSON:** `needle.endOffset`


    **Values:**
    - Finite number `>=0`.


    Offset from the outer radius to the end of the visible needle.

    **Used by:** Gauge

    **Disabled / read-only when:**
    - [Needle Enabled](properties.md#needle-enabled)=`false`.

    **Notes:**
    - When the needle is enabled, Start Offset + End Offset must be less than `Width/2`.

<!-- --8<-- [end:needle-end-offset] -->

---
## Needle Start Offset


<!-- --8<-- [start:needle-start-offset] -->

!!! info ""

    **Editor name:** Start Offset  
    **Semantic key:** `gauge.needle.start_offset`  
    **Canonical model:** `GaugeWidgetDefinition.Needle.StartOffset`  
    **JSON:** `needle.startOffset`


    **Values:**
    - Finite number `>=0`.


    Offset from the center to the start of the visible needle.

    **Used by:** Gauge

    **Disabled / read-only when:**
    - [Needle Enabled](properties.md#needle-enabled)=`false`.

    **Notes:**
    - When the needle is enabled, Start Offset + End Offset must be less than `Width/2`.

<!-- --8<-- [end:needle-start-offset] -->

---
## Needle Thickness


<!-- --8<-- [start:needle-thickness] -->

!!! info ""

    **Editor name:** Thickness  
    **Semantic key:** `gauge.needle.thickness`  
    **Canonical model:** `GaugeWidgetDefinition.Needle.Thickness`  
    **JSON:** `needle.thickness`


    **Values:**
    - Finite number `>=0`; when Enabled=true, it must be `>0`.


    Thickness of the main needle line.

    **Used by:** Gauge

    **Disabled / read-only when:**
    - [Needle Enabled](properties.md#needle-enabled)=`false`.

<!-- --8<-- [end:needle-thickness] -->

---
## Opacity

<!-- --8<-- [start:opacity] -->

**Editor label:** Opacity. Canonical properties: ImageWidgetDefinition.IconOpacity, StateVisualProfileDefinition.IconOpacity, and WidgetPanelLayerDefinition.IconOpacity (including root Canvas). Persisted name: **iconOpacity**.

Allowed values: finite 0..1. This property applies **only to icons**. It multiplies icon transparency while keeping the asset's intrinsic color/tint behavior. Icon-only Opacity belongs to the relevant Image-tab group.

File-backed images use their **embedded alpha only**; no independent Opacity or Color control exists. Text also has no standalone Opacity; text and outline RGBA colors have independent alpha. The unrelated Graph Fill Opacity remains independent; Shadow uses its RGBA Color alpha instead.

Schema 29 converts old icon opacity to iconOpacity and removes legacy file/text opacity fields. See the migration discussion in Issue #112 for edge cases.

<!-- --8<-- [end:opacity] -->

---
## Orientation


<!-- --8<-- [start:orientation] -->

!!! info ""

    **Editor name:** Orientation  
    **Semantic key:** `general.geometry.orientation`  
    **Canonical model:** `CanvasDefinition.Orientation`  
    **JSON:** `canvas.orientation`


    **Values:**
    - `0`
    - `90`
    - `180`
    - `270` degrees (values normalize modulo 360 before validation).


    Logical orientation canvas.

    **Used by:** Canvas

<!-- --8<-- [end:orientation] -->

---
## Outline Color


<!-- --8<-- [start:outline-color] -->

!!! info ""

    **Editor name:** Color  
    **Semantic key:** `text.value.outline.color` (Text / Value); `text.state.{state}.outline.color` (Value state)

    **Canonical model:** `TextPresentationDefinition.OutlineColor`  
    **JSON:** `textPresentation.outlineColor` (Text / Value); `profiles.{state}.textPresentation.outlineColor` (Value state)


    **Values:**
    - Color.


    Text outline color.

    **Used by:** Text / Value and Value state profiles

<!-- --8<-- [end:outline-color] -->

---
## Outline Width


<!-- --8<-- [start:outline-width] -->

!!! info ""

    **Editor name:** Width  
    **Semantic key:** `text.value.outline.width` (Text / Value); `text.state.{state}.outline.width` (Value state)

    **Canonical model:** `TextPresentationDefinition.OutlineWidth`  
    **JSON:** `textPresentation.outlineWidth` (Text / Value); `profiles.{state}.textPresentation.outlineWidth` (Value state)


    **Values:**
    - Finite number `>=0`.


    Text outline width.

    **Used by:** Text / Value and Value state profiles

<!-- --8<-- [end:outline-width] -->

---
## Overflow Mode


<!-- --8<-- [start:overflow-mode] -->

!!! info ""

    **Editor name:** Mode  
    **Semantic key:** `text.value.overflow.mode` (Text / Value); `text.state.{state}.overflow.mode` (Value state)

    **Canonical model:** `TextPresentationDefinition.OverflowMode`  
    **JSON:** `textPresentation.overflowMode` (Text / Value); `profiles.{state}.textPresentation.overflowMode` (Value state)


    **Values:**
    - `None`
    - `Clip`
    - `Ellipsis`
    - `ShrinkToFit`
    - `Wrap`
    - `Scroll`
    - `Bump`


    Controls text behavior when the available width is constrained.

    **Used by:** Text / Value and Value state profiles

    **Notes:**
    - `None` is the auto-width mode.
    - Text / Value allows `None` only when [Width](properties.md#width)=0; fixed Width normalizes `None` to `Clip`.
    - A state-driven widget allows `None` only when **all** states have [Source Type](properties.md#state-source-type)=`Value` and [Width](properties.md#width)=0. Mixed Value/Image/Icon state widgets use constrained modes.

<!-- --8<-- [end:overflow-mode] -->

---
## Pointer Color


<!-- --8<-- [start:pointer-color] -->

!!! info ""

    **Editor name:** Pointer Color  
    **Semantic key:** `gauge.needle.pointer.color`  
    **Canonical model:** `GaugeWidgetDefinition.Needle.Pointer.Color`  
    **JSON:** `needle.pointer.color`


    **Values:**
    - Color.


    Color of the pointer segment.

    **Used by:** Gauge

    **Disabled / read-only when:**
    - [Needle Enabled](properties.md#needle-enabled)=`false`.

<!-- --8<-- [end:pointer-color] -->

---
## Pointer Length


<!-- --8<-- [start:pointer-length] -->

!!! info ""

    **Editor name:** Pointer Length  
    **Semantic key:** `gauge.needle.pointer.length`  
    **Canonical model:** `GaugeWidgetDefinition.Needle.Pointer.Length`  
    **JSON:** `needle.pointer.length`


    **Values:**
    - Finite number `>=0`.


    Length of the pointer segment.

    **Used by:** Gauge

    **Disabled / read-only when:**
    - [Needle Enabled](properties.md#needle-enabled)=`false`.

<!-- --8<-- [end:pointer-length] -->

---
## Pointer Thickness


<!-- --8<-- [start:pointer-thickness] -->

!!! info ""

    **Editor name:** Pointer Thickness  
    **Semantic key:** `gauge.needle.pointer.thickness`  
    **Canonical model:** `GaugeWidgetDefinition.Needle.Pointer.Thickness`  
    **JSON:** `needle.pointer.thickness`


    **Values:**
    - Finite number `>=0`; when [Pointer Length](properties.md#pointer-length)>0, it must be `>0`.


    Thickness of the pointer segment.

    **Used by:** Gauge

    **Disabled / read-only when:**
    - [Needle Enabled](properties.md#needle-enabled)=`false`.

<!-- --8<-- [end:pointer-thickness] -->

---
## Power Source


<!-- --8<-- [start:power-source] -->

!!! info ""

    **Editor name:** Source  
    **Semantic key:** `general.source.power`  
    **Canonical model:** `PowerWidgetDefinition.PowerSource`  
    **JSON:** `powerSource`


    **Values:**
    - `power.ups`
    - `power.battery`


    Logical power source whose state is displayed by the widget. This is not a generic telemetry Metric property.

    **Used by:** Power

<!-- --8<-- [end:power-source] -->

---
## Prefix


<!-- --8<-- [start:prefix] -->

!!! info ""

    **Editor name:** Prefix  
    **Semantic key:** `data.display.prefix`  
    **Canonical model:** `ValueWidgetDefinition.Prefix` / `BinaryWidgetDefinition.Prefix`  
    **JSON:** `prefix`


    **Values:**
    - Text.


    Text displayed before the value.

    **Used by:** Text / Value, Binary with Value state

<!-- --8<-- [end:prefix] -->

---
## Progress Mode


<!-- --8<-- [start:progress-mode] -->

!!! info ""

    **Editor name:** Progress Mode  
    **Semantic key:** `bar.image.progress_mode`  
    **Canonical model:** `BarWidgetDefinition.Image.ProgressMode`  
    **JSON:** `image.progressMode`


    **Values:**
    - `Scale`
    - `Slide`
    - `Reveal`


    Controls how the image changes visually with the Bar value.

    **Used by:** Bar / [Mode](properties.md#bar-mode)=Image

<!-- --8<-- [end:progress-mode] -->

---
## Reverse


<!-- --8<-- [start:reverse] -->

!!! info ""

    **Editor name:** Reverse

    **Semantic keys:**
    - `gauge.reverse`
    - `gauge.bar.reverse`
    - `graph.reverse`

    **Canonical model:** `NumericWidgetDefinition.Reverse`

    **JSON:** `reverse`


    **Values:**
    - Boolean; default `false`.


    Reverses the direction in which the quantitative indicator progresses.

    **Used by:** Gauge, Bar, Graph

    For Graph, `false` scrolls history left to right (new observations at left);
    `true` scrolls right to left (new observations at right). This does not mirror the background image or the value axis.

<!-- --8<-- [end:reverse] -->

---
## Rotation


<!-- --8<-- [start:rotation] -->

!!! info ""

    **Editor name:** Rotation  
    **Semantic key:** `general.geometry.rotation`  
    **Canonical model:** `WidgetDefinition.Rotation`  
    **JSON:** `rotation`


    **Values:**
    - Finite number, degrees.


    Rotates the widget around its center; positive values rotate clockwise.

    **Used by:** All widgets

<!-- --8<-- [end:rotation] -->

---
## Scale Mode

<!-- --8<-- [start:scale-mode] -->

!!! info ""

    **Editor name:** Scale Mode  
    **Semantic key:** `data.graph.scale_mode`  
    **Canonical model:** `GraphWidgetDefinition.ScaleMode`  
    **JSON:** `scaleMode`

    **Values:** `Auto` (default), `Fixed`.

    Auto derives its range from finite in-window readings before drawing reduction. Fixed uses stored Min/Max.

    **Used by:** Graph

<!-- --8<-- [end:scale-mode] -->

---
## Scroll Speed


<!-- --8<-- [start:scroll-speed] -->

!!! info ""

    **Editor name:** Scroll Speed  
    **Semantic key:** `text.value.overflow.scroll_speed` (Text / Value); `text.state.{state}.overflow.scroll_speed` (Value state)

    **Canonical model:** `TextPresentationDefinition.ScrollSpeed`  
    **JSON:** `textPresentation.scrollSpeed` (Text / Value); `profiles.{state}.textPresentation.scrollSpeed` (Value state)


    **Values:**
    - Finite number `>=0` pixels/second; `0` is allowed.


    Scroll/Bump speed.

    **Used by:** Text / Value and Value state profiles

    **Disabled / read-only when:**
    - [Overflow Mode](properties.md#overflow-mode) is neither `Scroll` nor `Bump`.

<!-- --8<-- [end:scroll-speed] -->

---
## Setpoint


<!-- --8<-- [start:setpoint] -->

!!! info ""

    **Editor name:** Setpoint  
    **Semantic key:** `data.binary.evaluation.setpoint`  
    **Canonical model:** `BinaryWidgetDefinition.Setpoint`  
    **JSON:** `setpoint`


    **Values:**
    - Text reference. For numeric metrics, single values, `|`-separated alternatives and inclusive `a-b` ranges are supported.
    - Commas remain numeric thousands separators, not alternative separators.


    Reference value used when [Mode](properties.md#evaluation-mode)=`Setpoint`. See the [Binary Setpoint guide](binary.md#setpoint) for syntax, error behavior, and operator interpretation.

    **Used by:** Binary

    **Disabled / read-only when:**
    - [Mode](properties.md#evaluation-mode)!=`Setpoint`.

<!-- --8<-- [end:setpoint] -->

---
## Shadow Blur


<!-- --8<-- [start:shadow-blur] -->

!!! info ""

    **Editor name:** Blur  
    **Semantic key:** `appearance.shadow.blur`  
    **Canonical model:** `WidgetDefinition.ShadowBlur`  
    **JSON:** `shadowBlur`


    **Values:**
    - Finite number `>= 0`.


    Shadow blur radius.

    **Used by:** All widgets

    **Disabled / read-only when:**
    - [Enabled](properties.md#shadow-enabled)=`false`.

<!-- --8<-- [end:shadow-blur] -->

---
## Shadow Color


<!-- --8<-- [start:shadow-color] -->

!!! info ""

    **Editor name:** Color  
    **Semantic key:** `appearance.shadow.color`  
    **Canonical model:** `WidgetDefinition.ShadowColor`  
    **JSON:** `shadowColor`


    **Values:**
    - `#RRGGBB` or `#AARRGGBB` (8-bit alpha).


    Shadow color; its RGBA alpha is the only shadow transparency control. Applies equally to widget shadows and Desktop-only Canvas contour shadows.

    **Used by:** All widgets

    **Disabled / read-only when:**
    - [Enabled](properties.md#shadow-enabled)=`false`.

<!-- --8<-- [end:shadow-color] -->

---
## Shadow Enabled


<!-- --8<-- [start:shadow-enabled] -->

!!! info ""

    **Editor name:** Enabled  
    **Semantic key:** `appearance.shadow.enabled`  
    **Canonical model:** `WidgetDefinition.ShadowEnabled`  
    **JSON:** `shadowEnabled`


    **Values:**
    - Boolean.


    Enables the shadow for the visible widget content.

    **Used by:** All widgets

<!-- --8<-- [end:shadow-enabled] -->

---
## Shadow Offset X


<!-- --8<-- [start:shadow-offset-x] -->

!!! info ""

    **Editor name:** Offset X  
    **Semantic key:** `appearance.shadow.offset_x`  
    **Canonical model:** `WidgetDefinition.ShadowOffsetX`  
    **JSON:** `shadowOffsetX`


    **Values:**
    - Finite number.


    Horizontal shadow offset.

    **Used by:** All widgets

    **Disabled / read-only when:**
    - [Enabled](properties.md#shadow-enabled)=`false`.

<!-- --8<-- [end:shadow-offset-x] -->

---
## Shadow Offset Y


<!-- --8<-- [start:shadow-offset-y] -->

!!! info ""

    **Editor name:** Offset Y  
    **Semantic key:** `appearance.shadow.offset_y`  
    **Canonical model:** `WidgetDefinition.ShadowOffsetY`  
    **JSON:** `shadowOffsetY`


    **Values:**
    - Finite number.


    Vertical shadow offset.

    **Used by:** All widgets

    **Disabled / read-only when:**
    - [Enabled](properties.md#shadow-enabled)=`false`.

<!-- --8<-- [end:shadow-offset-y] -->

---
## Source Unit


<!-- --8<-- [start:source-unit] -->

!!! info ""

    **Editor name:** Source Unit  
    **Semantic key:** `data.value.literal_source_unit / data.value.metric_source_unit / data.binary.metric_source_unit / data.quantitative.metric_source_unit`  
    **Canonical model:** `ValueWidgetDefinition.SourceUnit` (literal) / `MetricDescriptor.BaseUnit` (metric)  
    **JSON:** `sourceUnit` only for numeric literal Text / Value; metric source unit is derived and not persisted


    **Values:**
    - Metric-backed value: read-only unit declared by the selected metric.
    - Numeric literal Text / Value: `raw`, `%`, `milliseconds`, `seconds`, `minutes`, `hours`, `C`, `F`, `W`, `V`, `A`, `Hz`, `kHz`, `MHz`, `GHz`, `RPM`, `L/h`, `ns`, `mWh`, `dBA`, `µS/cm`, `FPS`, `B`, `KB`, `MB`, `GB`, `TB`, `KiB`, `MiB`, `GiB`, `TiB`, `B/s`, `KB/s`, `MB/s`, `GB/s`, `TB/s`, `KiB/s`, `MiB/s`, `GiB/s`, `TiB/s`, `bit/s`, `kbit/s`, `Mbit/s`, `Gbit/s`, `Tbit/s`.
    **Read-only:**
    - Metric-backed contexts.


    Source unit of the value before display conversion.

    **Used by:** Text / Value, Binary, Gauge, Bar, Graph

    **Disabled / read-only when:**
    - Text / Value: [Type](properties.md#value-source-type)=`Text` and [Text](properties.md#text) is not a finite invariant number.
    - Binary, Gauge, Bar, and Graph obtain [Source Unit](properties.md#source-unit) from the selected [Metric](properties.md#metric).

<!-- --8<-- [end:source-unit] -->

---
## Start Angle


<!-- --8<-- [start:start-angle] -->

!!! info ""

    **Editor name:** Start Angle  
    **Semantic key:** `gauge.arc.start`  
    **Canonical model:** `GaugeWidgetDefinition.StartAngle`  
    **JSON:** `startAngle`


    **Values:**
    - Degrees; the contract requires `0 <= [Start Angle](properties.md#start-angle) < End Angle <= 360`.


    Starting angle of the arc.

    **Used by:** Gauge

<!-- --8<-- [end:start-angle] -->

---
## State Color


<!-- --8<-- [start:state-color] -->

!!! info ""

    **Editor name:** Color

    **Semantic keys:**
    - `image.state.{state}.color`
    - `text.state.{state}.color`

    **Canonical model:** `StateVisualProfileDefinition.Color`

    **JSON:** `profiles.{state}.color`


    **Values:**
    - Color.


    State-local foreground color.

    **Used by:** Value and graphical state profiles

    **Context notes:**
    - For a Value state, Color is the text foreground color.
    - For an Icon state, Color is a tint override.
    - For a file-backed Image state, Color is absent; pixel colors and alpha come from the file.
    - If no state Color is stored, the widget's [Default Color](properties.md#foreground-color) is inherited. Selecting an explicit RGBA Color overrides it.
    - Icon Color remains visible but read-only when the selected icon does not expose tint capability.

<!-- --8<-- [end:state-color] -->

---
## State Source Type


<!-- --8<-- [start:state-source-type] -->

!!! info ""

    **Editor name:** Source Type  
    **Semantic key:** `states.{state}.source.kind`  
    **Canonical model:** `StateVisualProfileDefinition.ContentType + Asset.SourceType`  
    **JSON:** `profiles.{state}.contentType + profiles.{state}.asset.sourceType`


    **Values:**
    - `Value` → contentType=`value`.
    - `Image` → contentType=`image`, asset.sourceType=`file`.
    - `Icon` → contentType=`image`, asset.sourceType=`icon`.


    Selects the presentation mode for the specific state.

    **Used by:** Binary, Power, Media System, Media Player

    **Notes:**
    - Switching graphical file ↔ icon uses canonical ChangeSourceType and clears an incompatible asset Source.
    - Switching the state to Value changes contentType; the dormant asset remains a separate canonical subobject.

<!-- --8<-- [end:state-source-type] -->

---
## Stroke Width

<!-- --8<-- [start:stroke-width] -->

!!! info ""

    **Editor name:** Stroke Width  
    **Semantic key:** `graph.stroke_width`  
    **Canonical model:** `GraphWidgetDefinition.StrokeWidth`  
    **JSON:** `strokeWidth`

    **Values:** Positive finite number in logical pixels; default `2`.

    Width of the Line stroke and isolated-point diameter.

    **Used by:** Graph Line; read-only in Columns mode.

<!-- --8<-- [end:stroke-width] -->

---
## Suffix


<!-- --8<-- [start:suffix] -->

!!! info ""

    **Editor name:** Suffix  
    **Semantic key:** `data.display.suffix`  
    **Canonical model:** `ValueWidgetDefinition.Suffix` / `BinaryWidgetDefinition.Suffix`  
    **JSON:** `suffix`


    **Values:**
    - Text.


    Text displayed after the value.

    **Used by:** Text / Value, Binary with Value state

<!-- --8<-- [end:suffix] -->

---
## Text


<!-- --8<-- [start:text] -->

!!! info ""

    **Editor name:** Text  
    **Semantic key:** `general.source.value.text`  
    **Canonical model:** `ValueWidgetDefinition.Text`  
    **JSON:** `text`


    **Values:**
    - Text; may be a numeric literal.


    Literal text for [Type](properties.md#value-source-type)=`Text`. If the string is an invariant numeric literal, it can participate in unit conversion and numeric formatting.

    **Used by:** Text / Value

    **Disabled / read-only when:**
    - [Type](properties.md#value-source-type)!=`Text`.

<!-- --8<-- [end:text] -->

---
## Threshold Color


<!-- --8<-- [start:threshold-color] -->

!!! info ""

    **Editor name:** Color

    **Semantic keys:**
    - `appearance.thresholds.1.color`
    - `appearance.thresholds.2.color`
    - `appearance.thresholds.3.color`

    **Canonical model:** `ThresholdDefinition.Color` through `Thresholds.Items[0..2]`

    **JSON:** `thresholds.items[0..2].color`


    **Values:**
    - Color.


    Color assigned to the corresponding threshold slot.

    **Used by:** Gauge, Graph, Bar / [Mode](properties.md#bar-mode)=`Fill`

    **Disabled / read-only when:**
    - The corresponding [Enabled](properties.md#threshold-enabled) property is `false`.

    **Notes:**
    - Threshold properties are hidden for Bar when [Mode](properties.md#bar-mode)=`Image`.

<!-- --8<-- [end:threshold-color] -->

---
## Threshold Enabled


<!-- --8<-- [start:threshold-enabled] -->

!!! info ""

    **Editor name:** Enabled

    **Semantic keys:**
    - `appearance.thresholds.1.enabled`
    - `appearance.thresholds.2.enabled`
    - `appearance.thresholds.3.enabled`

    **Canonical model:** `ThresholdDefinition.Enabled` through `Thresholds.Items[0..2]`

    **JSON:** `thresholds.items[0..2].enabled`


    **Values:**
    - Boolean.


    Enables the corresponding threshold slot.

    **Used by:** Gauge, Graph, Bar / [Mode](properties.md#bar-mode)=`Fill`

    **Context notes:**
    - A disabled threshold does not participate in threshold rendering.
    - Threshold properties are hidden for Bar when [Mode](properties.md#bar-mode)=`Image`.

<!-- --8<-- [end:threshold-enabled] -->

---
## Threshold Mode


<!-- --8<-- [start:threshold-mode] -->

!!! info ""

    **Editor name:** Threshold Mode  
    **Semantic key:** `appearance.thresholds.mode`  
    **Canonical model:** `NumericWidgetDefinition.Thresholds.Mode`  
    **JSON:** `thresholds.mode`


    **Values:**
    - `SegmentTransition`
    - `SegmentSolid`
    - `State`


    Controls how threshold colors are applied.

    **Used by:** Gauge, Graph, Bar / [Mode](properties.md#bar-mode)=Fill

    **Hidden when:**
    - Bar: [Mode](properties.md#bar-mode)=`Image`.

<!-- --8<-- [end:threshold-mode] -->

---
## Threshold Value


<!-- --8<-- [start:threshold-value] -->

!!! info ""

    **Editor name:** Value

    **Semantic keys:**
    - `appearance.thresholds.1.value`
    - `appearance.thresholds.2.value`
    - `appearance.thresholds.3.value`

    **Canonical model:** `ThresholdDefinition.Value` through `Thresholds.Items[0..2]`

    **JSON:** `thresholds.items[0..2].value`


    **Values:**
    - Finite number. Enabled boundaries must be within [Min](properties.md#min)..[Max](properties.md#max) for Gauge, Bar Fill, and Graph Fixed; Graph Auto accepts absolute boundaries outside its current visible range.


    Numeric boundary for the corresponding threshold slot.

    **Used by:** Gauge, Graph, Bar / [Mode](properties.md#bar-mode)=`Fill`

    **Disabled / read-only when:**
    - The corresponding [Enabled](properties.md#threshold-enabled) property is `false`.

    **Notes:**
    - Enabled threshold values must be ordered in ascending order.
    - Threshold properties are hidden for Bar when [Mode](properties.md#bar-mode)=`Image`.

<!-- --8<-- [end:threshold-value] -->

---
## Track Background Color


<!-- --8<-- [start:track-background-color] -->

!!! info ""

    **Editor name:** Background Color  
    **Semantic key:** `gauge.track.background`  
    **Canonical model:** `GaugeWidgetDefinition.Track.BackgroundColor`  
    **JSON:** `track.backgroundColor`


    **Values:**
    - Color.


    Track background color.

    **Used by:** Gauge

    **Disabled / read-only when:**
    - [Track Enabled](properties.md#track-enabled)=`false`.

<!-- --8<-- [end:track-background-color] -->

---
## Track Border Color


<!-- --8<-- [start:track-border-color] -->

!!! info ""

    **Editor name:** Border Color  
    **Semantic key:** `gauge.track.border.color`  
    **Canonical model:** `GaugeWidgetDefinition.Track.BorderColor`  
    **JSON:** `track.borderColor`


    **Values:**
    - Color.


    Track border color.

    **Used by:** Gauge

    **Disabled / read-only when:**
    - [Track Enabled](properties.md#track-enabled)=`false`.

<!-- --8<-- [end:track-border-color] -->

---
## Track Border Width


<!-- --8<-- [start:track-border-width] -->

!!! info ""

    **Editor name:** Border Width  
    **Semantic key:** `gauge.track.border.width`  
    **Canonical model:** `GaugeWidgetDefinition.Track.BorderWidth`  
    **JSON:** `track.borderWidth`


    **Values:**
    - Finite number `>=0`.


    Track border width.

    **Used by:** Gauge

    **Disabled / read-only when:**
    - [Track Enabled](properties.md#track-enabled)=`false`.

<!-- --8<-- [end:track-border-width] -->

---
## Track Corner Radius


<!-- --8<-- [start:track-corner-radius] -->

!!! info ""

    **Editor name:** Corner Radius  
    **Semantic key:** `gauge.track.corner_radius`  
    **Canonical model:** `GaugeWidgetDefinition.Track.CornerRadius`  
    **JSON:** `track.cornerRadius`


    **Values:**
    - Finite number `>=0`.


    Corner radius track.

    **Used by:** Gauge

    **Disabled / read-only when:**
    - [Track Enabled](properties.md#track-enabled)=`false`.

<!-- --8<-- [end:track-corner-radius] -->

---
## Track Enabled


<!-- --8<-- [start:track-enabled] -->

!!! info ""

    **Editor name:** Enabled  
    **Semantic key:** `gauge.track.enabled`  
    **Canonical model:** `GaugeWidgetDefinition.Track.Enabled`  
    **JSON:** `track.enabled`


    **Values:**
    - Boolean.


    Enables the horseshoe track.

    **Used by:** Gauge

<!-- --8<-- [end:track-enabled] -->

---
## Track Thickness


<!-- --8<-- [start:track-thickness] -->

!!! info ""

    **Editor name:** Thickness  
    **Semantic key:** `gauge.track.thickness`  
    **Canonical model:** `GaugeWidgetDefinition.Track.Thickness`  
    **JSON:** `track.thickness`


    **Values:**
    - Finite number `>=0`; when Enabled=true, it must be `>0`.


    Track thickness.

    **Used by:** Gauge

    **Disabled / read-only when:**
    - [Track Enabled](properties.md#track-enabled)=`false`.

<!-- --8<-- [end:track-thickness] -->

---
## True If


<!-- --8<-- [start:true-if] -->

!!! info ""

    **Editor name:** True If  
    **Semantic key:** `data.binary.evaluation.true_if`  
    **Canonical model:** `BinaryWidgetDefinition.TrueIf`  
    **JSON:** `trueIf`


    **Values:**
    - `>`
    - `<`
    - `>=`
    - `<=`
    - `=`
    - `!=`


    Comparison operator against [Setpoint](properties.md#setpoint) when [Mode](properties.md#evaluation-mode)=`Setpoint`. Numeric alternatives use OR; interval comparisons follow the [Binary Setpoint guide](binary.md#setpoint-evaluation).

    **Used by:** Binary

    **Disabled / read-only when:**
    - [Mode](properties.md#evaluation-mode)!=`Setpoint`.

<!-- --8<-- [end:true-if] -->

---
## Unit


<!-- --8<-- [start:unit] -->

!!! info ""

    **Editor name:** Unit  
    **Semantic key:** `data.value.unit / data.binary.unit / data.quantitative.unit`  
    **Canonical model:** `ValueWidgetDefinition.Unit / BinaryWidgetDefinition.Unit / NumericWidgetDefinition.Unit`  
    **JSON:** `unit`


    **Values:**
    - Duration: `raw`, `auto`, `seconds`, `minutes`, `hours`; Gauge/Bar/Graph exclude `auto`.
    - Data size: `raw`, `B`, `KB`, `MB`, `GB`, `TB`, `KiB`, `MiB`, `GiB`, `TiB`.
    - Temperature: `raw`, `C`, `F`.
    - Frequency: `raw`, `Hz`, `kHz`, `MHz`, `GHz`.
    - Data rate: `raw`, `B/s`, `KB/s`, `MB/s`, `GB/s`, `TB/s`, `KiB/s`, `MiB/s`, `GiB/s`, `TiB/s`, `bit/s`, `kbit/s`, `Mbit/s`, `Gbit/s`, `Tbit/s`.
    - Other metric base units may have no conversion options.


    Display unit and, where applicable, the conversion target.

    **Used by:** Text / Value, Binary, Gauge, Bar, Graph

    **Disabled / read-only when:**
    - Text / Value literal is not numeric.
    - The selected metric / [Source Unit](properties.md#source-unit) provides no applicable conversions.

<!-- --8<-- [end:unit] -->

---
## Value Source Type


<!-- --8<-- [start:value-source-type] -->

!!! info ""

    **Editor name:** Type  
    **Semantic key:** `general.source.value.kind`  
    **Canonical model:** `ValueWidgetDefinition.SourceKind`  
    **JSON:** `sourceKind`


    **Values:**
    - `Metric`
    - `Text`


    Selects a telemetry metric or literal text as the content source.

    **Used by:** Text / Value

<!-- --8<-- [end:value-source-type] -->

---
## Vertical Alignment


<!-- --8<-- [start:vertical-alignment] -->

!!! info ""

    **Editor name:** Vertical  
    **Semantic key:** `text.value.align.vertical` (Text / Value); `text.state.{state}.align.vertical` (Value state)

    **Canonical model:** `TextPresentationDefinition.VerticalAlign`  
    **JSON:** `textPresentation.verticalAlign` (Text / Value); `profiles.{state}.textPresentation.verticalAlign` (Value state)


    **Values:**
    - `baseline`
    - `top`
    - `middle`
    - `bottom`


    Vertical alignment.

    **Used by:** Text / Value and Value state profiles

<!-- --8<-- [end:vertical-alignment] -->

---
## Width


<!-- --8<-- [start:width] -->

!!! info ""

    **Editor name:** Width  
    **Semantic key:** `general.geometry.width`  
    **Canonical model:** `WidgetDefinition.Width / CanvasDefinition.Width`  
    **JSON:** `width` (widgets); `canvas.width` (Canvas)


    **Values:**
    - **Text / Value:** `>= 0`; `0` enables auto width.
    - **Gauge:** `> 0`; [Height](properties.md#height) is derived and equal to Width.
    - **Bar / Image / Graph:** `> 0`.
    - **Binary / Power / Media System / Media Player:** `>= 0` only when all states use [Source Type](properties.md#state-source-type) = `Value`; otherwise `> 0`.
    - **Canvas:** positive integer.


    Width of the widget bounds in logical pixels; for Canvas, the native canvas width.

    **Used by:** All widgets, Canvas

    **Notes:**
    - For Text / Value, [Width](properties.md#width)=0 requires [Overflow Mode](properties.md#overflow-mode)=`None`.
    - For a state-driven value-only widget, [Width](properties.md#width)=0 enables the shared auto-width contract; all value-state [Overflow Mode](properties.md#overflow-mode) values normalize to `None`.
    - Switching a value-only state widget to mixed Value/Image/Icon content materializes a positive Width.

<!-- --8<-- [end:width] -->

---
## X


<!-- --8<-- [start:x] -->

!!! info ""

    **Editor name:** X  
    **Semantic key:** `general.geometry.x`  
    **Canonical model:** `WidgetDefinition.X`  
    **JSON:** `x`


    **Values:**
    - Finite number; Editor normalizes it to an integer logical pixel.


    Horizontal coordinate on the logical canvas.

    **Used by:** All widgets

<!-- --8<-- [end:x] -->

---
## Y


<!-- --8<-- [start:y] -->

!!! info ""

    **Editor name:** Y  
    **Semantic key:** `general.geometry.y`  
    **Canonical model:** `WidgetDefinition.Y`  
    **JSON:** `y`


    **Values:**
    - Finite number; Editor normalizes it to an integer logical pixel.


    Vertical coordinate on the logical canvas.

    **Used by:** All widgets

<!-- --8<-- [end:y] -->

---

---
## Action Activation

<!-- --8<-- [start:action-activation] -->

!!! info ""

    **Editor name:** Activation  
    **Semantic key:** `action.activation`  
    **Canonical model:** `WidgetDefinition.Action.Activation`  
    **JSON:** `action.activation`

    `None` (default), `Double` (Double click), `Single` (Single click). Native Desktop interaction only. `None` disables hit regions without clearing stored Target/Arguments. A blank Target is functionally inactive without changing Activation.

    **Used by:** All widgets; Canvas excluded. Physical Outputs are display-only.

<!-- --8<-- [end:action-activation] -->

---
## Action Target

<!-- --8<-- [start:action-target] -->

!!! info ""

    **Editor name:** Target  
    **Semantic key:** `action.target`  
    **Canonical model:** `WidgetDefinition.Action.Target`  
    **JSON:** `action.target`

    Literal Windows Shell path, URL, folder, file or parsing name. Default empty. A blank or whitespace Target creates no native hotspot, hand cursor, click interception or Shell launch regardless of Activation. The file picker does not import or rewrite the target. Read-only when Activation is `None`.

    **Used by:** All widgets; Canvas excluded. Physical Outputs are display-only.

<!-- --8<-- [end:action-target] -->

---
## Action Arguments

<!-- --8<-- [start:action-arguments] -->

!!! info ""

    **Editor name:** Arguments  
    **Semantic key:** `action.arguments`  
    **Canonical model:** `WidgetDefinition.Action.Arguments`  
    **JSON:** `action.arguments`

    Literal arguments passed separately and unchanged to Windows Shell; no parsing or extension filtering. Default empty. Read-only when Activation is `None`; dormant value is preserved.

    **Used by:** All widgets; Canvas excluded. Physical Outputs are display-only.

<!-- --8<-- [end:action-arguments] -->

---
## Action Run as

<!-- --8<-- [start:action-run-as] -->

!!! info ""

    **Editor name:** Run as  
    **Semantic key:** `action.runAs`  
    **Canonical model:** `WidgetDefinition.Action.RunAs`  
    **JSON:** `action.runAs`

    `User` (default) retains the standard Windows Shell verb and caller's current privileges. `Administrator` requests the native `runas` verb and Windows UAC consent/credentials; it does **not** elevate PinkieSysMon Runtime. The configured Target and literal Arguments are passed unchanged. Only targets supporting `runas` can be elevated; unsupported targets return a native Shell failure. Declining UAC cancels that action without launching the target. Read-only when Activation is `None`; the selected value remains stored. Schema 32 dashboards migrate to 33 with `User` as the implicit default.

    **Used by:** All widgets; Canvas excluded. Physical Outputs are display-only.

<!-- --8<-- [end:action-run-as] -->
