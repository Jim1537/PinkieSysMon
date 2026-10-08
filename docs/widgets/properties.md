# Widget Property Dictionary


## Background Color


<!-- --8<-- [start:background-color] -->


**Editor name:** Color  
**Semantic key:** `appearance.background.color`  
**Canonical model:** `WidgetDefinition.BackgroundColor / CanvasDefinition.BackgroundColor`  
**JSON:** `backgroundColor` (widgets); `canvas.backgroundColor` (Canvas)


**Values:**
- Color.


Background color of the widget container or Canvas.

**Used by:** All widgets, Canvas

<!-- --8<-- [end:background-color] -->

---
## Bar Mode


<!-- --8<-- [start:bar-mode] -->


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
## Border Color


<!-- --8<-- [start:border-color] -->


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
## Corner Radius


<!-- --8<-- [start:corner-radius] -->


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
## Font Family


<!-- --8<-- [start:font-family] -->


**Editor name:** Family  
**Semantic key:** `text.value.font.family` (Text / Value); `text.state.{state}.font.family` (Value state)  
**Canonical model:** `TextPresentationDefinition.FontFamily`  
**JSON:** `textPresentation.fontFamily` (Text / Value); `profiles.{state}.textPresentation.fontFamily` (Value state)


**Values:**
- Font family text; default `Roboto`.


Font family. In the current canonical view, this is an editable text property.

**Used by:** Text / Value and Value state profiles

<!-- --8<-- [end:font-family] -->

---
## Font Size


<!-- --8<-- [start:font-size] -->


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


**Editor name:** Color  
**Semantic key:** `appearance.foreground.color`  
**Canonical model:** `WidgetDefinition.Color`  
**JSON:** `color`


**Values:**
- Color.


Primary foreground color. For state-driven widgets, it serves as a fallback when the active profile does not define its own Color.

**Used by:** Text / Value, Gauge, Binary, Power, Media System, Media Player, Bar in Fill mode, Image in Icon mode

**Disabled / read-only when:**
- Image / [Type](properties.md#image-source-type)=`Icon`: when [Source](properties.md#image-source) is empty or the selected icon does not support tint.

**Hidden / tab omitted when:**
- Bar / [Mode](properties.md#bar-mode)=`Image`.
- Image / [Type](properties.md#image-source-type)!=`Icon`.

<!-- --8<-- [end:foreground-color] -->

---
## Format


<!-- --8<-- [start:format] -->


**Editor name:** Format  
**Semantic key:** `data.value.format / data.binary.format`  
**Canonical model:** `ValueWidgetDefinition.Format / BinaryWidgetDefinition.Format`  
**JSON:** `format`


**Values:**
- Number / Percent / DataSize: `raw`, `0`, `0.0`, `0.00`.
- Duration additionally supports `h:mm` and `h:mm:ss` when [Unit](properties.md#unit) is not `auto`.
- DateTime presets include `yyyy-MM-dd HH:mm:ss`, `yyyy-MM-dd`, `yyyy`, `MMMM`, `MMM`, `MM`, `dd`, `dddd`, `ddd`, `HH:mm`, `HH:mm:ss`, `hh:mm tt`, `hh:mm:ss tt`, `MMMM d`, `MMMM d, yyyy`, `dddd, MMMM d`, `dddd, MMMM d, yyyy`; custom .NET DateTime formats are validated before acceptance.


Format of the displayed value.

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
- With the track enabled, `2*[Border Width](properties.md#track-border-width) + 2*Gap + [Thickness](properties.md#track-thickness)` must fit within `[Width](properties.md#width)/2`.

<!-- --8<-- [end:gap] -->

---
## Height


<!-- --8<-- [start:height] -->


**Editor name:** Height  
**Semantic key:** `general.geometry.height`  
**Canonical model:** `WidgetDefinition.Height / CanvasDefinition.Height`  
**JSON:** `height` (widgets); `canvas.height` (Canvas)


**Values:**
- **Bar / Image:** `> 0`.
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
## Horizontal Alignment


<!-- --8<-- [start:horizontal-alignment] -->


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


**Editor name:** Fit

**Semantic keys:**
- `image.asset.fit`
- `image.state.{state}.fit`
- `image.background_image.asset.fit`
- `image.foreground_image.asset.fit`
- `bar.image.fit`

**Canonical model:**
- `ImageAssetPresentationDefinition.Fit` (Image, state assets, Canvas layers)
- `BarImagePresentationDefinition.Fit` (Bar Image mode)

**JSON:**
- Image: `asset.fit`
- State profile: `profiles.{state}.asset.fit`
- Canvas background: `canvas.backgroundImage.asset.fit`
- Canvas foreground: `canvas.foregroundImage.asset.fit`
- Bar Image: `image.fit`


**Values:**
- Image / state / Canvas assets: `contain`, `cover`, `stretch`.
- Bar Image mode: `Clip`, `Contain`, `Cover`, `Stretch`.


Controls how an image source is fitted inside the target bounds.

**Used by:** Image, graphical state profiles, Canvas background/foreground image layers, Bar / [Mode](properties.md#bar-mode)=`Image`

**Context notes:**
- Canvas layer Fit is read-only when [Source](properties.md#image-source) is empty.

<!-- --8<-- [end:image-fit] -->

---
## Image Layer Color


<!-- --8<-- [start:image-layer-color] -->


**Editor name:** Color

**Semantic keys:**
- `image.background_image.color`
- `image.foreground_image.color`

**Canonical model:** `CanvasImageLayerDefinition.Color`

**JSON:**
- `canvas.backgroundImage.color`
- `canvas.foregroundImage.color`


**Values:**
- Color.


Tint color of a Canvas image layer.

**Used by:** Canvas background image layer, Canvas foreground image layer

**Disabled / read-only when:**
- [Source](properties.md#image-source) is empty.
- [Type](properties.md#image-source-type)=`Image` (file).
- [Type](properties.md#image-source-type)=`Icon` but the selected icon does not expose tint capability.

<!-- --8<-- [end:image-layer-color] -->

---
## Image Loop


<!-- --8<-- [start:image-loop] -->


**Editor name:** Loop

**Semantic keys:**
- `image.asset.loop`
- `image.state.{state}.loop`
- `image.background_image.asset.loop`
- `image.foreground_image.asset.loop`
- `bar.image.loop`

**Canonical model:**
- `ImageAssetPresentationDefinition.Loop` (Image, state assets, Canvas layers)
- `BarImagePresentationDefinition.Loop` (Bar Image mode)

**JSON:**
- Image: `asset.loop`
- State profile: `profiles.{state}.asset.loop`
- Canvas background: `canvas.backgroundImage.asset.loop`
- Canvas foreground: `canvas.foregroundImage.asset.loop`
- Bar Image: `image.loop`


**Values:**
- Boolean.


Controls looping of an animated graphical source.

**Used by:** Image, graphical state profiles, Canvas background/foreground image layers, Bar / [Mode](properties.md#bar-mode)=`Image`

**Context notes:**
- Icon-backed Image/state content is read-only when the selected icon does not expose animated capability.
- Canvas layers require a non-empty [Source](properties.md#image-source) and an animated file/icon capability.
- Bar Image mode exposes Loop directly.

<!-- --8<-- [end:image-loop] -->

---
## Image Source


<!-- --8<-- [start:image-source] -->


**Editor name:** Source; standalone Image displays Image or Icon dynamically

**Semantic keys:**
- `general.source.image.source`
- `general.background_image.asset.source`
- `general.foreground_image.asset.source`
- `states.{state}.asset.source`
- `bar.image.source`

**Canonical model:**
- `ImageAssetPresentationDefinition.Source` (Image, Canvas layers, state assets)
- `BarImagePresentationDefinition.Source` (Bar Image mode)

**JSON:**
- Image: `asset.source`
- Canvas background: `canvas.backgroundImage.asset.source`
- Canvas foreground: `canvas.foregroundImage.asset.source`
- State profile: `profiles.{state}.asset.source`
- Bar Image: `image.source`


**Values:**
- File-backed contexts: dashboard-relative image file.
- Icon-backed contexts: global icon logical name.
- Bar Image mode: dashboard-relative image file.


Active graphical source.

**Used by:** Image, graphical state profiles, Bar / [Mode](properties.md#bar-mode)=`Image`, Canvas background/foreground image layers

**Context notes:**
- State profiles disable Source when [Source Type](properties.md#state-source-type)=`Value`.
- Shared image assets clear Source when [Image Source Type](properties.md#image-source-type) changes between file and icon.
- Bar Image mode is file-backed only.

<!-- --8<-- [end:image-source] -->

---
## Image Source Type


<!-- --8<-- [start:image-source-type] -->


**Editor name:** Type

**Semantic keys:**
- `general.source.image.kind`
- `general.background_image.asset.kind`
- `general.foreground_image.asset.kind`

**Canonical model:** `ImageAssetPresentationDefinition.SourceType`

**JSON:**
- Image: `asset.sourceType`
- Canvas background: `canvas.backgroundImage.asset.sourceType`
- Canvas foreground: `canvas.foregroundImage.asset.sourceType`


**Values:**
- Editor `Image` → canonical/JSON `file`.
- Editor `Icon` → canonical/JSON `icon`.


Selects whether a shared image asset uses a dashboard-relative file or a global icon.

**Used by:** Image, Canvas background image layer, Canvas foreground image layer

**Notes:**
- Changing `file` ↔ `icon` uses canonical `ChangeSourceType()` and clears the current [Source](properties.md#image-source).

<!-- --8<-- [end:image-source-type] -->

---
## Italic


<!-- --8<-- [start:italic] -->


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


**Editor name:** Max  
**Semantic key:** `data.range.max`  
**Canonical model:** `GaugeWidgetDefinition.Max` / `BarWidgetDefinition.Max`  
**JSON:** `max`


**Values:**
- Finite number; must be > [Min](properties.md#min).


Upper bound of the quantitative range.

**Used by:** Gauge, Bar

<!-- --8<-- [end:max] -->

---
## Media Player Metric


<!-- --8<-- [start:media-player-metric] -->


**Editor name:** Metric  
**Semantic key:** `general.source.media_player.metric`  
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


**Editor name:** Metric  
**Semantic key:** `general.source.metric`  
**Canonical model:** `ValueWidgetDefinition.Metric / BinaryWidgetDefinition.Metric / GaugeWidgetDefinition.Metric / BarWidgetDefinition.Metric`  
**JSON:** `metric`


**Values:**
- Metric ID selected through the metric picker.


Telemetry metric used by the widget as its data source.

**Used by:** Text / Value, Binary, Gauge, Bar

**Disabled / read-only when:**
- Text / Value: [Type](properties.md#value-source-type)!=`Metric`.

<!-- --8<-- [end:metric] -->

---
## Min


<!-- --8<-- [start:min] -->


**Editor name:** Min  
**Semantic key:** `data.range.min`  
**Canonical model:** `GaugeWidgetDefinition.Min` / `BarWidgetDefinition.Min`  
**JSON:** `min`


**Values:**
- Finite number; must be < [Max](properties.md#max).


Lower bound of the quantitative range.

**Used by:** Gauge, Bar

<!-- --8<-- [end:min] -->

---
## Name


<!-- --8<-- [start:name] -->


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


**Editor name:** Opacity

**Semantic keys:**
- `image.opacity`
- `image.state.{state}.opacity`
- `text.state.{state}.opacity`
- `image.background_image.opacity`
- `image.foreground_image.opacity`

**Canonical model:**
- `ImageWidgetDefinition.Opacity`
- `StateVisualProfileDefinition.Opacity`
- `CanvasImageLayerDefinition.Opacity`

**JSON:**
- Image: `opacity`
- State profile: `profiles.{state}.opacity`
- Canvas background: `canvas.backgroundImage.opacity`
- Canvas foreground: `canvas.foregroundImage.opacity`

**Values:**
- Number `0..1`.


Opacity of the visual content represented by the current context.

**Used by:** Image, Value/graphical state profiles, Canvas background/foreground image layers

**Context notes:**
- Canvas layer Opacity is read-only when [Source](properties.md#image-source) is empty.

<!-- --8<-- [end:opacity] -->

---
## Orientation


<!-- --8<-- [start:orientation] -->


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


**Editor name:** Reverse

**Semantic keys:**
- `gauge.reverse`
- `gauge.bar.reverse`

**Canonical model:**
- `GaugeWidgetDefinition.Reverse`
- `BarWidgetDefinition.Reverse`

**JSON:** `reverse`


**Values:**
- Boolean.


Reverses the direction in which the quantitative indicator progresses.

**Used by:** Gauge, Bar

<!-- --8<-- [end:reverse] -->

---
## Rotation


<!-- --8<-- [start:rotation] -->


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
## Scroll Speed


<!-- --8<-- [start:scroll-speed] -->


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


**Editor name:** Setpoint  
**Semantic key:** `data.binary.evaluation.setpoint`  
**Canonical model:** `BinaryWidgetDefinition.Setpoint`  
**JSON:** `setpoint`


**Values:**
- Text representation of reference value.


Reference value used when [Mode](properties.md#evaluation-mode)=`Setpoint`.

**Used by:** Binary

**Disabled / read-only when:**
- [Mode](properties.md#evaluation-mode)!=`Setpoint`.

<!-- --8<-- [end:setpoint] -->

---
## Shadow Blur


<!-- --8<-- [start:shadow-blur] -->


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


**Editor name:** Color  
**Semantic key:** `appearance.shadow.color`  
**Canonical model:** `WidgetDefinition.ShadowColor`  
**JSON:** `shadowColor`


**Values:**
- Color.


Shadow color.

**Used by:** All widgets

**Disabled / read-only when:**
- [Enabled](properties.md#shadow-enabled)=`false`.

<!-- --8<-- [end:shadow-color] -->

---
## Shadow Enabled


<!-- --8<-- [start:shadow-enabled] -->


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
## Shadow Opacity


<!-- --8<-- [start:shadow-opacity] -->


**Editor name:** Opacity  
**Semantic key:** `appearance.shadow.opacity`  
**Canonical model:** `WidgetDefinition.ShadowOpacity`  
**JSON:** `shadowOpacity`


**Values:**
- Number `0..1`.


Shadow opacity.

**Used by:** All widgets

**Disabled / read-only when:**
- [Enabled](properties.md#shadow-enabled)=`false`.

<!-- --8<-- [end:shadow-opacity] -->

---
## Source Unit


<!-- --8<-- [start:source-unit] -->


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

**Used by:** Text / Value, Binary, Gauge, Bar

**Disabled / read-only when:**
- Text / Value: [Type](properties.md#value-source-type)=`Text` and [Text](properties.md#text) is not a finite invariant number.
- Binary, Gauge, and Bar obtain [Source Unit](properties.md#source-unit) from the selected [Metric](properties.md#metric).

<!-- --8<-- [end:source-unit] -->

---
## Start Angle


<!-- --8<-- [start:start-angle] -->


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
- For a file-backed Image state, Color is not applicable.
- If no state Color is stored, the widget [foreground Color](properties.md#foreground-color) may serve as a fallback.
- Icon tint is read-only when the selected icon does not expose tint capability.

<!-- --8<-- [end:state-color] -->

---
## State Source Type


<!-- --8<-- [start:state-source-type] -->


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
## Suffix


<!-- --8<-- [start:suffix] -->


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

**Used by:** Gauge, Bar / [Mode](properties.md#bar-mode)=`Fill`

**Disabled / read-only when:**
- The corresponding [Enabled](properties.md#threshold-enabled) property is `false`.

**Notes:**
- Threshold properties are hidden for Bar when [Mode](properties.md#bar-mode)=`Image`.

<!-- --8<-- [end:threshold-color] -->

---
## Threshold Enabled


<!-- --8<-- [start:threshold-enabled] -->


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

**Used by:** Gauge, Bar / [Mode](properties.md#bar-mode)=`Fill`

**Context notes:**
- A disabled threshold does not participate in threshold rendering.
- Threshold properties are hidden for Bar when [Mode](properties.md#bar-mode)=`Image`.

<!-- --8<-- [end:threshold-enabled] -->

---
## Threshold Mode


<!-- --8<-- [start:threshold-mode] -->


**Editor name:** Threshold Mode  
**Semantic key:** `appearance.thresholds.mode`  
**Canonical model:** `GaugeWidgetDefinition.Thresholds.Mode` / `BarWidgetDefinition.Thresholds.Mode`  
**JSON:** `thresholds.mode`


**Values:**
- `SegmentTransition`
- `SegmentSolid`
- `State`


Controls how threshold colors are applied.

**Used by:** Gauge, Bar / [Mode](properties.md#bar-mode)=Fill

**Hidden when:**
- Bar: [Mode](properties.md#bar-mode)=`Image`.

<!-- --8<-- [end:threshold-mode] -->

---
## Threshold Value


<!-- --8<-- [start:threshold-value] -->


**Editor name:** Value

**Semantic keys:**
- `appearance.thresholds.1.value`
- `appearance.thresholds.2.value`
- `appearance.thresholds.3.value`

**Canonical model:** `ThresholdDefinition.Value` through `Thresholds.Items[0..2]`

**JSON:** `thresholds.items[0..2].value`


**Values:**
- Finite number within [Min](properties.md#min)..[Max](properties.md#max) when the slot is enabled.


Numeric boundary for the corresponding threshold slot.

**Used by:** Gauge, Bar / [Mode](properties.md#bar-mode)=`Fill`

**Disabled / read-only when:**
- The corresponding [Enabled](properties.md#threshold-enabled) property is `false`.

**Notes:**
- Enabled threshold values must be ordered in ascending order.
- Threshold properties are hidden for Bar when [Mode](properties.md#bar-mode)=`Image`.

<!-- --8<-- [end:threshold-value] -->

---
## Track Background Color


<!-- --8<-- [start:track-background-color] -->


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


Comparison operator against [Setpoint](properties.md#setpoint) when [Mode](properties.md#evaluation-mode)=`Setpoint`.
**Used by:** Binary

**Disabled / read-only when:**
- [Mode](properties.md#evaluation-mode)!=`Setpoint`.

<!-- --8<-- [end:true-if] -->

---
## Unit


<!-- --8<-- [start:unit] -->


**Editor name:** Unit  
**Semantic key:** `data.value.unit / data.binary.unit / data.quantitative.unit`  
**Canonical model:** `ValueWidgetDefinition.Unit / BinaryWidgetDefinition.Unit / GaugeWidgetDefinition.Unit / BarWidgetDefinition.Unit`  
**JSON:** `unit`


**Values:**
- Duration: `raw`, `auto`, `seconds`, `minutes`, `hours`; Gauge/Bar exclude `auto`.
- Data size: `raw`, `B`, `KB`, `MB`, `GB`, `TB`, `KiB`, `MiB`, `GiB`, `TiB`.
- Temperature: `raw`, `C`, `F`.
- Frequency: `raw`, `Hz`, `kHz`, `MHz`, `GHz`.
- Data rate: `raw`, `B/s`, `KB/s`, `MB/s`, `GB/s`, `TB/s`, `KiB/s`, `MiB/s`, `GiB/s`, `TiB/s`, `bit/s`, `kbit/s`, `Mbit/s`, `Gbit/s`, `Tbit/s`.
- Other metric base units may have no conversion options.


Display unit and, where applicable, the conversion target.

**Used by:** Text / Value, Binary, Gauge, Bar

**Disabled / read-only when:**
- Text / Value literal is not numeric.
- The selected metric / [Source Unit](properties.md#source-unit) provides no applicable conversions.

<!-- --8<-- [end:unit] -->

---
## Value Source Type


<!-- --8<-- [start:value-source-type] -->


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


**Editor name:** Width  
**Semantic key:** `general.geometry.width`  
**Canonical model:** `WidgetDefinition.Width / CanvasDefinition.Width`  
**JSON:** `width` (widgets); `canvas.width` (Canvas)


**Values:**
- **Text / Value:** `>= 0`; `0` enables auto width.
- **Gauge:** `> 0`; [Height](properties.md#height) is derived and equal to Width.
- **Bar / Image:** `> 0`.
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
