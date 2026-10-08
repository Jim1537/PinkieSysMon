# Media System

The **Media System** widget displays the type of the current default Windows audio output or input device. It chooses a visual profile for the detected endpoint type and can display an icon, an image, or the normalized type name as text.

For example, an output indicator can switch between speaker, headphone, and display-audio icons when the default playback device changes. An input indicator can represent the current default capture device.

Media System is a **device-type indicator**. It does not change the default audio device, adjust volume, mute an endpoint, or control media playback.

---
## User Guide

### Step 1. Add a Media System widget

Open a dashboard in **PinkieSysMon Dashboard Editor**. From `Add Widget` (or `Add widget` on the toolbar), select `Media System`, then select the new widget on the Canvas.

!!! info
    A newly added Media System widget starts with `Source = media.output`, `Width = 96`, `Height = 96`, and `X = 100`, `Y = 100`.

    Its state profiles initially use distinct icons appropriate to the recognized audio device types.


### Step 2. Choose Output or Input

Open `General → Source` and choose the `Source` value:

| Source | Monitored device | Availability metric | Type metric |
| --- | --- | --- | --- |
| `media.output` | Current default Windows **playback** endpoint | `system.media.output.available` | `system.media.output.type` |
| `media.input` | Current default Windows **recording** endpoint | `system.media.input.available` | `system.media.input.type` |

The built-in [System](../telemetry/system.md) provider supplies both sets of metrics. Media System follows the current Windows default endpoint for the **Multimedia** role, not an arbitrary device selected by its friendly name.

These source names are logical widget selectors, **not** metric IDs. There is no free-form `Metric` setting on this widget.

!!! warning
    `media.output` and `media.input` select the device **type**. They do not represent playback status or microphone activity. For those readings, use the relevant System metric in a separate [Text / Value](text-value.md) or [Binary](binary.md) widget, as appropriate.


See [Media Source](properties.md#media-source).

### Step 3. Understand the endpoint types

Open the `States` tab. Each recognized endpoint type has a separate visual profile.

| Editor group | Canonical key | Default icon |
| --- | --- | --- |
| `Type: Remote Network` | `remote-network` | `lucide:router` |
| `Type: Speakers` | `speakers` | `lucide:speaker` |
| `Type: Line Level` | `line-level` | `lucide:audio-lines` |
| `Type: Headphones` | `headphones` | `lucide:headphones` |
| `Type: Microphone` | `microphone` | `lucide:mic` |
| `Type: Handset` | `handset` | `lucide:phone` |
| `Type: Digital Passthrough` | `digital-passthrough` | `lucide:audio-lines` |
| `Type: S/PDIF` | `spdif` | `lucide:cable` |
| `Type: Display Audio` | `display-audio` | `lucide:monitor-speaker` |
| `Type: Unknown` | `unknown` | `lucide:circle-help` |
| `State: Unavailable` | `unavailable` | `lucide:circle-slash` |

There are **11 visual profiles**: ten possible endpoint classifications and one Unavailable state. Each is configurable independently.

The device class comes from the Windows audio endpoint's form-factor metadata when it is available. Windows `Headset` form-factor devices currently share the `headphones` classification; there is no separate Headset profile.

!!! info
    **Unknown** and **Unavailable** are intentionally different.

    - **Unknown** means that a default endpoint is available, but its type is not recognized or cannot be determined from the available metadata.
    - **Unavailable** means that the selected default endpoint is not available according to the current System telemetry reading.

    A missing or non-true availability reading selects Unavailable rather than Unknown.


### Step 4. Choose the visual content for each profile

For each profile on the `States` tab, select `Source Type`:

| Source Type | Presentation |
| --- | --- |
| `Icon` | An icon from the shared application icon library. |
| `Image` | An image file associated with the dashboard. |
| `Value` | The current **normalized endpoint type key** (or `unavailable`). |

When using `Icon` or `Image`, select the corresponding `Source` asset.

When using `Value`, the text is generated automatically from the selected profile's key. For example, the `Type: Headphones` profile displays `headphones`, and `Type: Display Audio` displays `display-audio`.

You can mix source types. For instance, use an animated icon for Speakers, a custom image for Headphones, and Value text for Unknown.

!!! warning
    `Source Type = Value` does **not** display the Windows device's friendly name, volume, mute state, or audio activity. It displays the canonical device-type identifier.

    To display the friendly name or a numerical reading, add a separate Text / Value widget with the appropriate `system.media.*` metric.


See [State Source Type](properties.md#state-source-type) and [Image Source](properties.md#image-source).

### Step 5. Customize image and icon states

For any profile using `Icon` or `Image`, open the `Image` tab. Configure its state-specific properties:

- `Fit` — `Contain`, `Cover`, or `Stretch` within the widget bounds.
- `Loop` — whether an animated source repeats.
- `Color` — tint for a source that supports icon tinting.
- `Opacity` — transparency of that state's visual content.

File-backed images retain their own image colors; the icon Color control is not a general file-recoloring feature. Loop has an observable effect only for an animated asset.

Different endpoint profiles may deliberately use different colors, shapes, sizes, and animation behavior.

See [Image Fit](properties.md#image-fit), [Image Loop](properties.md#image-loop), [State Color](properties.md#state-color), and [Opacity](properties.md#opacity).

### Step 6. Customize Value-based states

For profiles using `Value`, open the `Text` tab to configure:

- foreground color and opacity;
- font family, size, weight, and italic style;
- horizontal and vertical alignment;
- text outline;
- overflow behavior, including the applicable Scroll and Bump settings.

These settings belong to the individual profile. For example, `unknown` can appear in gray while `unavailable` appears in red.

`Appearance` controls the shared widget background, border, and shadow. `General → Geometry` controls position, Width, Height, and rotation.

The widget does not provide an arbitrary custom label field for the Value profile. If you want a descriptive phrase such as `Audio Output`, use a separate Text / Value widget.

See [Font Family](properties.md#font-family), [Overflow Mode](properties.md#overflow-mode), and [Widget Properties — Tabs](properties-by-tab.md).

### Step 7. Override an incorrectly classified endpoint

Windows or an audio driver may report a generic or unexpected endpoint form factor. The Editor supports a manual type override for discovered active audio endpoints.

In the `Data → Endpoint Overrides` group, find the endpoint by its Windows friendly name and choose:

- `Auto` to use the type detected from Windows;
- one of the supported canonical types, such as `headphones`, `speakers`, or `display-audio`, to override the classification.

For example, if Windows describes a headset device as Speakers but you want the Media System indicator to use the Headphones appearance, choose `headphones` for the corresponding endpoint.

!!! warning
    Endpoint Overrides are **application-wide settings**, stored by Windows endpoint ID in `AppConfig.Media.EndpointTypeOverrides`. They are **not** part of the Media System widget's dashboard JSON, and they do not change Windows' default device or the device's actual audio capabilities.

    The group appears when the Editor has active endpoints to list. An override takes effect when that endpoint is the selected default input or output device. Returning to `Auto` removes the stored override.


See [Endpoint Type Override](properties.md#endpoint-type-override).

### Step 8. Add related audio readings if needed

The System provider exposes other media metrics that can be displayed by separate widgets:

| Information | Output metric | Input metric |
| --- | --- | --- |
| Default endpoint name | `system.media.output.name` | `system.media.input.name` |
| Endpoint ID | `system.media.output.id` | `system.media.input.id` |
| Endpoint type | `system.media.output.type` | `system.media.input.type` |
| Availability | `system.media.output.available` | `system.media.input.available` |
| Master volume | `system.media.output.volume` | `system.media.input.volume` |
| Muted | `system.media.output.muted` | `system.media.input.muted` |
| Active input session | — | `system.media.input.active` |

These metrics are separate from the Media System state renderer. Availability and mute are Boolean readings; volume is a percent reading. Input activity describes the input-session telemetry and should not be mistaken for the mere presence of a microphone.

See [System Provider](../telemetry/system.md) for the provider's media telemetry contract.

### Usage Examples

| Scenario | Configuration | Expected result |
| --- | --- | --- |
| Default output icon | `Source = media.output`; keep the default Icon state profiles. | Displays the classified current Windows default Multimedia output endpoint. |
| Input device icon | `Source = media.input`; customize Microphone, Headset-equivalent Headphones, and Unavailable profiles. | Displays the current default Multimedia capture endpoint's classified type. |
| Headset classification correction | In `Data → Endpoint Overrides`, assign `headphones` to the active endpoint. | That endpoint uses the Headphones profile whenever it becomes the relevant default endpoint. |
| Text-only endpoint type | Set every profile to `Source Type = Value`. | Displays a canonical type name such as `speakers`, `headphones`, or `unavailable`. |
| Output device and volume | Media System with `media.output` plus Text / Value with `system.media.output.volume`. | Shows the output type icon and a separate volume percentage. |
| No default endpoint | Use a distinctive `State: Unavailable` profile. | Shows the Unavailable appearance when the provider does not report an available default endpoint. |
| Unknown driver metadata | Give `Type: Unknown` a different image/color from Unavailable. | Shows Unknown for an available but unrecognized device type. |

---
## Technical Information

### Purpose and Architecture

Media System is a canonical state-driven widget. It selects an audio endpoint domain and maps the current default endpoint's **classification**, not its playback or recording state, onto a visual profile.

- User-facing name: `Media System`.
- Persisted type discriminator: `media.system`.
- Canonical model: `DashboardModel.MediaSystemWidgetDefinition`.
- Status and endpoint identifiers: `MediaMetricContract`.
- Selection logic: `WidgetRenderContext.TryResolveMediaSystemVisualProfile`.
- Visual contract: `StateVisualProfileContract`.
- Renderer: `Widgets.MediaSystemWidgetRenderer` (inherits `StateProfileWidgetRenderer`).
- Telemetry producer: `WindowsMediaTelemetrySource`, through the built-in System provider.

Media System consumes already collected telemetry. It does not enumerate endpoints independently during rendering, reconfigure Windows audio devices, or request a particular non-default endpoint.

### Canonical Model and Persistence

`MediaSystemWidgetDefinition` inherits `StateVisualWidgetDefinition` and the common `WidgetDefinition` properties.

| Persisted JSON field | Canonical member | Meaning |
| --- | --- | --- |
| `type` | `Type` | Discriminator `media.system`. |
| `mediaSource` | `MediaSource` | One of `media.output` or `media.input`. |
| `profiles` | `Profiles` | Eleven required visual profiles, keyed by canonical endpoint classification or `unavailable`. |

--8<-- "widgets/properties.md:widget-base-fields"

No arbitrary `metric` field, playback-state property, or endpoint ID selector belongs to this widget model. Endpoint-type overrides belong to `AppConfig.Media.EndpointTypeOverrides` and must not be serialized inside an individual Media System widget.

### Source-to-Metric Mapping

`DashboardMetricUsage.Collect` requests two metrics for the selected Media System source:

| Logical widget source | Availability metric | Type metric |
| --- | --- | --- |
| `media.output` | `system.media.output.available` | `system.media.output.type` |
| `media.input` | `system.media.input.available` | `system.media.input.type` |

These are demand-driven telemetry dependencies. Adding a Media System widget does not automatically require volume, mute, endpoint friendly name, input activity, or playback metadata.

`WindowsMediaTelemetrySource` uses Windows audio endpoint APIs through NAudio. It resolves the current default `DataFlow.Render` or `DataFlow.Capture` endpoint with `Role.Multimedia` and provides the corresponding type and availability values.

The telemetry provider, not the Media System widget, reads endpoint metadata and applies endpoint-type overrides.

### Endpoint Classification

`WindowsMediaTelemetrySource.EndpointType` reads the Windows audio endpoint form-factor property and maps it to a canonical type.

| Windows form factor | Media System type |
| --- | --- |
| Remote Network | `remote-network` |
| Speakers | `speakers` |
| Line Level | `line-level` |
| Headphones | `headphones` |
| Microphone | `microphone` |
| Headset | `headphones` |
| Handset | `handset` |
| Digital Passthrough | `digital-passthrough` |
| S/PDIF | `spdif` |
| Display Audio | `display-audio` |
| Missing/unrecognized form factor | `unknown` |

The Headset-to-Headphones normalization is intentional. There is no extra `headset` persisted profile key.

`MediaMetricContract.NormalizeEndpointType` trims and lowercases an incoming type, matches the ten canonical endpoint categories, and maps anything else to `unknown`.

An available endpoint with unrecognized type remains **Unknown**. It is not classified as Unavailable simply because Windows supplied incomplete device metadata.

### Unavailable Selection Semantics

`WidgetRenderContext.TryResolveMediaSystemVisualProfile` selects its state in this order:

- Identify the selected logical source (`media.input` or `media.output`).
- Choose that source's availability metric and type metric.
- Read the current snapshot.
- Consider the endpoint available only if the availability metric is the Boolean value `true`.
- If available, normalize the type metric; a missing or unexpected type becomes `unknown`.
- Otherwise choose the `unavailable` key.
- Project the corresponding canonical state profile into the shared render representation.

The resolver always selects a profile for valid Media System widgets because `unknown` and `unavailable` have explicit entries in the contract.

In particular, a missing metric, `null`, or false availability result selects `unavailable`. A type metric containing `unknown` does not override the absence of a valid availability signal.

An endpoint reconfiguration or provider failure must not cause a stale previous type to remain indefinitely displayed as if it were current.

### Endpoint Overrides

Editor overrides are indexed by the exact Windows endpoint identifier in:

  `AppConfig.Media.EndpointTypeOverrides[EndpointId]`

The Editor exposes an `Endpoint Overrides` group in the Data tab for enumerated active endpoints. The displayed property name is the current Windows friendly name; its internal property identity includes the endpoint ID.

The supported override values are `Auto` and the ten canonical endpoint types. `Auto` removes the corresponding entry from the map.

`WindowsMediaTelemetrySource` normalizes configured override values and, when the default endpoint's ID is present in the map, publishes the override type instead of the detected form factor.

After an override edit, the Editor saves the application configuration, applies the normalized override map to its media telemetry source, and refreshes the preview.

These overrides change the **reported classification**. They do not select a different endpoint, alter the Windows audio driver, or control system volume. Since the override is not stored in dashboard JSON, a dashboard moved to a differently configured machine need not receive the same endpoint classification.

### Eleven Visual Profiles

The required profile keys are:

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
- `unavailable`

--8<-- "widgets/profiles.md:profile-schema"

These eleven state presentations are independently configurable. `unknown` and `unavailable` are separate profiles; endpoint selection and the application-level endpoint override contract are documented above.

### Value-State Text and Rendering

For Media System, `WidgetRenderContext.ResolveStateValueText(widget, stateKey)` returns **the state key itself**:

- `speakers`
- `headphones`
- `display-audio`
- `unknown`
- `unavailable`

It does not read `system.media.output.name`, `system.media.input.name`, volume, mute, or activity values. Value is a presentation **of the classified state**, not a general telemetry-value source.

`MediaSystemWidgetRenderer` delegates state-specific drawing to `StateProfileWidgetRenderer`.

--8<-- "widgets/profiles.md:profile-rendering"

### Geometry and Overflow

--8<-- "widgets/profiles.md:profile-geometry"

For Media System, **all eleven** profiles must be Value-backed for auto-width to be eligible. X, Y, Width, Height, and Rotation remain the common widget geometry properties.

### Editor Property Applicability

| Tab | Group | Controls |
| --- | --- | --- |
| `General` | `Source` | `Source` = `media.output` or `media.input`. |
| `General` | `Geometry` | X, Y, Width, Height, Rotation. |
| `Appearance` | Foreground, Background, Border, Shadow | Shared appearance configuration. |
| `Data` | `Endpoint Overrides` | Available when active endpoints can be enumerated and the override configuration is accessible. |
| `States` | Eleven type/unavailable groups | State-specific `Source Type` and graphical `Source`. |
| `Image` | Graphical profiles | Fit, Loop, supported Color/Tint, Opacity. |
| `Text` | Value profiles | Foreground, Font, Alignment, Outline, Overflow. |

No free-form Metric, numeric Range, Threshold, Unit, or Setpoint properties belong to Media System. Its `Gauge` section has no Media System-specific controls.

See [Widget Properties — Tabs](properties-by-tab.md) and [Widget Property Dictionary](properties.md) for the exact property names and applicability rules.

### Validation and Compatibility Contracts

Canonical validation requires:

- a supported `mediaSource` (`media.output` or `media.input`);
- all eleven required visual profiles and no unsupported extra keys;
- valid state content types and corresponding graphical assets when applicable;
- valid text-presentation structures and finite profile opacity;
- positive geometry for graphical or mixed state content; non-negative dimensions for all-Value content;
- valid common widget colors, geometry, background, border, and shadow definitions.

Compatibility-sensitive behavior includes:

- The persisted widget type remains `media.system`.
- `mediaSource` is a fixed logical selector, not a general metric ID or endpoint ID.
- The selected device is the Windows default Multimedia endpoint for the chosen direction.
- Availability is established separately from endpoint classification.
- Unknown and Unavailable remain distinct and independently configurable.
- Windows Headset form factor maps to the Headphones profile.
- Endpoint overrides are keyed by Windows endpoint ID and stored in application configuration, not dashboard widgets.
- Value profiles display canonical endpoint type keys rather than device names or audio controls.
- All eleven state profiles retain their independent graphical and text presentation semantics.
- Shared Editor/Runtime rendering, state normalization, asset resolution, and text-overflow contracts remain consistent.
- Changes to persisted schemas, profile names, endpoint normalization, override handling, or rendering require explicit compatibility review and appropriate deterministic regression coverage.

For the implementation, see [PinkieSysMon on GitHub](https://github.com/Jim1537/PinkieSysMon).
