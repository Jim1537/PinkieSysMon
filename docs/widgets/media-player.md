# Media Player

The **Media Player** widget is a state-driven indicator of the current Windows media playback session. It displays the visual profile associated with **Playing**, **Paused**, **Stopped**, or **Unavailable**.

Each state can use an icon, an image, or a text representation of the playback state. The widget follows the media session exposed by Windows; it does not control playback, select a player, or display song metadata by itself.

Use Media Player for a compact playback-status symbol or label. For track titles, artists, and progress indicators, combine it with other widgets reading the corresponding System metrics.

---
## User Guide

### Step 1. Add the widget

Open a dashboard in **PinkieSysMon Dashboard Editor**, choose `Add Widget` (or the `Add widget` toolbar action), and select `Media Player`. Select the newly added widget on the Canvas to edit its properties.

!!! info
    A new Media Player widget starts at `X = 100`, `Y = 100`, with `Width = 96` and `Height = 96`.

    The default profiles use these icons: `lucide:play` for Playing, `lucide:pause` for Paused, `lucide:square` for Stopped, and `lucide:circle-slash` for Unavailable.


### Step 2. Understand its fixed source

On `General → Source`, the `Metric` field is read-only:

  `system.media.playback.status`

Media Player does **not** offer a selectable player, an editable metric ID, or a general `Source` property. The source is fixed by the widget type and provided by the built-in [System](../telemetry/system.md) telemetry provider.

Windows supplies the **current media transport session**. The widget does not independently pick a particular media application, browser tab, or audio output endpoint.

!!! warning
    The presence of audio on the speakers does not necessarily mean that Windows exposes a usable media playback session. Availability and status depend on the playback application's integration with Windows media session APIs.

    For the type of the default speaker/headphone endpoint, use [Media System](media-system.md) instead.


See [Media Player Metric](properties.md#media-player-metric).

### Step 3. Understand the four playback states

The `States` tab exposes four independent profiles:

| Editor group | Profile key | Default icon | Meaning |
| --- | --- | --- | --- |
| `State: Playing` | `playing` | `lucide:play` | The current Windows media session reports Playing. |
| `State: Paused` | `paused` | `lucide:pause` | The session reports Paused. |
| `State: Stopped` | `stopped` | `lucide:square` | The session reports Stopped. |
| `State: Unavailable` | `unavailable` | `lucide:circle-slash` | No supported playback state can currently be selected. |

**Stopped** and **Unavailable** are deliberately separate. A stopped session is a recognized playback state; Unavailable can mean there is no current session, the source is not reporting status, or Windows reports a transitional or unsupported status.

!!! info
    The underlying Windows session API may also produce `Opened`, `Changing`, or `Closed`. These are not additional Media Player visual profiles.

    - `Opened` and `Changing` resolve to **Unavailable**.
    - The state-mapping contract accepts `Closed` as **Stopped**.
    - The current Windows telemetry producer normally resets a Closed session to **Unavailable**, before the widget's state mapping runs.

    Consequently, do not expect every closing playback application to display the Stopped profile.


### Step 4. Choose a presentation for each state

On `States`, expand each state and select its `Source Type`:

| Source Type | What the profile displays |
| --- | --- |
| `Icon` | An icon selected from the application-wide icon library. |
| `Image` | A dashboard-local image file. |
| `Value` | The normalized state key as text: `playing`, `paused`, `stopped`, or `unavailable`. |

For Icon or Image, select the corresponding `Source`. For Value, no graphical asset is needed.

The four profiles are independent: Playing could use an animated image, Paused a static icon, Stopped a different image, and Unavailable a text label.

!!! warning
    `Source Type = Value` shows **the playback state**, not the current song title, artist, album, application name, or playback position. It does not provide a free-form replacement label.

    To show metadata, use separate [Text / Value](text-value.md) widgets bound to `system.media.playback.title`, `system.media.playback.artist`, and similar metrics.


See [State Source Type](properties.md#state-source-type) and [Image Source](properties.md#image-source).

### Step 5. Configure icons and images

When a profile uses Icon or Image, open the `Image` tab to customize its graphical presentation:

- `Fit` — choose Contain, Cover, or Stretch.
- `Loop` — repeat supported animation or stop after one playback.
- `Color` — use a color tint when the chosen icon supports tinting.
- `Opacity` — adjust transparency of the selected state's content.

Each graphical profile has its own settings. Tinting does not recolor a file-backed image, and the Loop setting has a visible effect only for animated content.

Supported animated state assets may restart playback relative to activation of a different state. A state transition and its visual animation are not commands to the real media player.

See [Image Fit](properties.md#image-fit), [Image Loop](properties.md#image-loop), and [State Color](properties.md#state-color).

### Step 6. Configure Value-state text

For any state using Value, open the `Text` tab. Its appearance is controlled independently of the other states:

- text color and opacity;
- font family, size, weight, and italic style;
- horizontal and vertical alignment;
- outline color and width;
- overflow mode, with scroll speed and bump pause where applicable.

For example, the Playing text can be green, Paused amber, Stopped gray, and Unavailable red.

The `Appearance` tab controls the common widget background, border, shadow, and applicable common foreground settings. `General → Geometry` controls placement, dimensions, and rotation.

See [Font Family](properties.md#font-family), [Overflow Mode](properties.md#overflow-mode), and [Widget Properties — Tabs](properties-by-tab.md).

### Step 7. Add metadata or progress alongside Media Player

The System provider also exposes the following media playback metrics:

| Metric | Value kind | Typical use |
| --- | --- | --- |
| `system.media.playback.status` | Text | Fixed state input to Media Player, or a raw status in Text / Value. |
| `system.media.playback.available` | Boolean | An explicit availability indicator in Binary or Text / Value. |
| `system.media.playback.title` | Text | Track or media title. |
| `system.media.playback.artist` | Text | Artist or creator metadata. |
| `system.media.playback.album` | Text | Album metadata. |
| `system.media.playback.source` | Text | Source application identifier reported by Windows. |
| `system.media.playback.progress` | Percent | Progress in Text / Value, [Bar](bar.md), or [Gauge](gauge.md). |

Some applications do not supply all metadata or usable timeline information. The System provider can report `[unknown]` for missing title, artist, or album, and `null` for unavailable progress. Availability does not guarantee that every optional metadata field is populated.

!!! warning
    The Media Player widget itself requires only the playback **status** metric. Adding it does not make all metadata and progress metrics mandatory telemetry dependencies.

    If you want a percentage bar, add a separate Bar widget using `system.media.playback.progress`, with a range of 0 to 100. Progress availability depends on the current application's media-session support.


### Usage Examples

| Scenario | Configuration | Expected result |
| --- | --- | --- |
| Minimal playback indicator | Keep the default four Icon profiles. | Changes among play, pause, stop, and unavailable symbols as the resolved status changes. |
| Text status label | Set all four profiles to `Source Type = Value` and choose separate text colors. | Displays `playing`, `paused`, `stopped`, or `unavailable`. |
| Animated Playing icon | Set `State: Playing` to an animated Icon/Image; adjust Loop on the Image tab. | Displays the selected animation only when Playing is active. |
| Playback indicator with song title | Use Media Player plus Text / Value bound to `system.media.playback.title`. | A playback-state visual next to an independently updated track title. |
| Playback indicator with progress | Use Media Player plus Bar bound to `system.media.playback.progress`, `Min = 0`, `Max = 100`. | Playback state and track progress appear as separate widgets when the metrics are available. |
| No active session | Customize `State: Unavailable` with a subdued icon or label. | Shows the Unavailable profile rather than falsely suggesting Paused or Stopped. |

---
## Technical Information

### Responsibility and Implementation Boundary

Media Player is a canonical, state-driven status indicator backed by Windows media transport telemetry.

- User-facing widget name: `Media Player`.
- Persisted type discriminator: `media.player`.
- Canonical definition: `DashboardModel.MediaPlayerWidgetDefinition`.
- Telemetry contract: `MediaMetricContract`.
- State selection: `WidgetRenderContext.TryResolveMediaPlayerVisualProfile`.
- State normalization: `StateVisualProfileContract.ResolveKey`.
- Renderer: `Widgets.MediaPlayerWidgetRenderer`, derived from `StateProfileWidgetRenderer`.
- Windows producer: `WindowsMediaTelemetrySource`, part of the built-in System provider.

The renderer consumes a telemetry snapshot; it neither owns a Windows media session nor sends play/pause/stop commands.

### Canonical Model and Persistence

`MediaPlayerWidgetDefinition` inherits from `StateVisualWidgetDefinition` and the common `WidgetDefinition` base.

| JSON field | Canonical member | Meaning |
| --- | --- | --- |
| `type` | `Type` | Persistent discriminator `media.player`. |
| `profiles` | `Profiles` | Map of the four state-specific visual profiles. |

Inherited common fields include widget ID, optional name, Z-order, position, Width, Height, Rotation, background, border, color, and shadow parameters.

There is **no** persisted `metric`, `mediaSource`, `powerSource`, `playbackSource`, or selected application identifier in the Media Player widget model. The Editor's `Metric` property is a derived, read-only view of the fixed telemetry dependency.

### Fixed Metric and Demand-Driven Dependency

`DashboardMetricUsage.Collect` adds exactly this fixed dependency for each Media Player widget:

  `system.media.playback.status`

`WindowsMediaTelemetrySource` publishes this reading from the current Windows `GlobalSystemMediaTransportControlsSessionManager` session. It uses the current session returned by Windows and listens for session, playback-info, metadata, and timeline changes.

It is distinct from:

- `system.media.output.*` and `system.media.input.*` (default audio endpoints);
- `system.media.playback.title` / `.artist` / `.album` (optional metadata);
- `system.media.playback.progress` (optional timeline-based progress);
- `system.media.playback.available` (availability telemetry).

The Media Player renderer selects its state from `system.media.playback.status`; it does **not** separately require `system.media.playback.available` in the state resolver. The System provider publishes consistent availability and status values for its own media-session snapshot.

### Playback State Normalization

`WidgetRenderContext.TryResolveMediaPlayerVisualProfile` reads the playback status metric and calls `StateVisualProfileContract.ResolveKey`.

The mapping is:

| Incoming normalized status | Profile selected |
| --- | --- |
| `playing` | `playing` |
| `paused` | `paused` |
| `stopped` | `stopped` |
| `closed` | `stopped` |
| `opened` | `unavailable` |
| `changing` | `unavailable` |
| `unavailable` | `unavailable` |
| Missing, `null`, or unrecognized text | `unavailable` |

`MediaMetricContract.NormalizePlaybackState` trims and lowercases the input (case-insensitively recognizing known status names). Only Playing, Paused, and Stopped have their own visible status profiles; the remaining recognized transitions have the mapping shown above.

!!! info
    **Provider behavior versus state-mapping capability:** the state contract maps an incoming `closed` token to Stopped, but `WindowsMediaTelemetrySource.RefreshPlaybackInfoLocked` resets a Closed Windows playback session to the Unavailable snapshot. Therefore the usual live Closed-session result is **Unavailable**, not Stopped.


A failed or missing status does not reuse the last valid status. The resolver selects the explicit Unavailable profile, and the shared renderer draws it normally.

### Windows Session and Metadata Semantics

`WindowsMediaTelemetrySource` uses Windows `GlobalSystemMediaTransportControlsSessionManager` (SMTC). When the current session changes, the producer detaches handlers from the previous session, attaches to the new one, and refreshes its playback snapshot.

If no session is available, or playback information cannot be read, the snapshot is reset to `unavailable` with `available = false`. Metadata fields use the canonical `[unknown]` placeholder when not available.

When a session is available, the producer can publish:

- `status` — current playback status;
- `title`, `artist`, `album` — media properties when exposed;
- `source` — the source application identifier;
- `available` — a Boolean indicating session availability;
- `progress` — a percentage where supported timeline information or a source-specific fallback is available.

The progress implementation uses Windows timeline properties, can extrapolate position while Playing, and has a narrowly scoped AIMP-specific fallback when Windows supplies no usable timeline. These are **provider features**, not Media Player widget properties.

A media application can expose a current status while withholding metadata or timing information. Missing optional information must remain unavailable rather than being fabricated by the widget.

### Four Visual Profiles

The canonical `profiles` dictionary requires exactly these four keys:

- `playing`
- `paused`
- `stopped`
- `unavailable`

The default graphical profiles reference `lucide:play`, `lucide:pause`, `lucide:square`, and `lucide:circle-slash`, respectively.

--8<-- "widgets/state-profiles.md:profile-schema"

Selecting Value activates only that state's text presentation; it does not make Media Player a general-purpose metric reader.

### Value Rendering

For Media Player, `WidgetRenderContext.ResolveStateValueText` returns the resolved state key itself: `playing`, `paused`, `stopped`, or `unavailable`.

It never substitutes track title, artist, album, player name, playback progress, or a user-configured arbitrary label. Value-state content has no numerical Unit, Format, Prefix, Suffix, or Fallback pipeline on this widget.

The shared `TextContentRenderer` renders the state text using the profile's canonical `TextPresentationDefinition` and its own color/opacity. The `Text` tab is contextual: only Value-backed profiles expose text-presentation settings.

### Image, Icon, and Animation Rendering

--8<-- "widgets/state-profiles.md:profile-rendering"

These visual animations do **not** control media playback in the external application.

### Geometry and Text Overflow

--8<-- "widgets/state-profiles.md:profile-geometry"

For Media Player, **all four** profiles must use Value for auto-width to be eligible. X, Y, Width, Height, and Rotation remain common widget properties.

### Editor Property Applicability

| Tab | Group | Media Player controls |
| --- | --- | --- |
| `General` | `Source` | Fixed, read-only `Metric = system.media.playback.status`. |
| `General` | `Geometry` | X, Y, Width, Height, Rotation. |
| `Appearance` | Foreground, Background, Border, Shadow | Widget-wide appearance settings. |
| `States` | Playing, Paused, Stopped, Unavailable | State-specific Source Type and Source. |
| `Image` | Graphical state profiles | Fit, Loop, supported Color, Opacity. |
| `Text` | Value state profiles | Foreground, Font, Alignment, Outline, Overflow. |

There are no Media Player-specific `Data`, `Gauge`, or playback-control fields. Media Player has no metric picker, timeline range, media-player selection control, or transport commands.

Refer to [Widget Properties — Tabs](properties-by-tab.md) and [Widget Property Dictionary](properties.md) for canonical property names, availability, and editing conditions.

### Validation and Maintenance Contracts

Canonical validation requires:

- a recognized widget type `media.player`;
- the four required visual profiles, with no unsupported extra state keys;
- valid state content type, asset structure, and asset existence for every active graphical profile;
- valid text-presentation structures and finite state opacity;
- positive Width and Height for graphical or mixed profiles, or non-negative dimensions for all-Value profiles;
- valid shared appearance, geometry, and color fields.

Compatibility-sensitive rules include:

- Persisted type remains `media.player`; it is not `media-player`.
- The widget uses fixed `system.media.playback.status` telemetry and does not persist a fake metric/source selector.
- Four profile keys remain `playing`, `paused`, `stopped`, and `unavailable`.
- `closed` maps to Stopped in the state contract, while the current Windows producer normally resets Closed to Unavailable.
- `opened` and `changing` select Unavailable; absent or invalid telemetry never silently selects Stopped.
- Value-backed states display their own normalized state keys, not track metadata or position.
- Animated and static graphical states share the established icon/image asset and renderer contracts.
- Editor preview and Runtime use shared canonical state projection and rendering behavior.
- Changes to source selection, status mapping, profile schema, geometry, or rendering require explicit compatibility analysis and deterministic regression coverage when feasible.

For current implementation details, see [PinkieSysMon on GitHub](https://github.com/Jim1537/PinkieSysMon).
