# Image

The **Image** widget places a static or animated graphic on a dashboard. It can display a file stored with the dashboard or an icon selected from PinkieSysMon's shared icon library.

Use Image for decorative artwork, logos, labels supplied as graphics, symbols, separators, animated accents, and other visual elements that do not need telemetry input. The widget is positioned and styled like other dashboard widgets, but it does **not** read a metric or react to a telemetry value.

There are two source types:

- **Image** — a file associated with the current dashboard.
- **Icon** — an asset from the application's shared icon library, addressed by a logical name.

These are two source types of the **same** Image widget. They are not separate widget classes.

---
## User Guide

### Step 1. Add an Image widget

Open a dashboard in **PinkieSysMon Dashboard Editor** and select `Image` from `Add Widget` or the `Add widget` toolbar menu.

Unlike most other widget types, Image opens a `Source type` dialog **before** the widget is added. Choose:

- `File` to use an image stored with the dashboard;
- `Icon` to select an item from the shared icon library.

The Editor then opens the corresponding file selector or icon browser. Complete that selection to create the widget on the Canvas.

!!! info
    A newly added icon starts at **96 × 96** logical pixels. A newly added image file uses the decoded image's dimensions, capped at **400 pixels wide** and **300 pixels high** independently.

    New Image widgets start at `X = 100`, `Y = 100`, `Fit = stretch`, `Loop = true`, `Opacity = 1`, and a white foreground color. You can change their size, placement, and appearance afterward.


### Step 2. Select or change the source

Open `General → Source` to configure the widget.

| Property | Meaning |
| --- | --- |
| `Type` | `Image` for a dashboard file or `Icon` for a shared library icon. |
| `Source` | The selected file path or logical icon name. Its editor depends on `Type`. |

#### Image: dashboard file

For `Type = Image`, use `Source` to select an image file. The Editor opens a file picker, initially focused on the dashboard's `images` directory.

If you choose a file **outside** the dashboard directory, the Editor copies it into the dashboard's `images` directory and stores a relative path. If a conflicting filename exists with different contents, it generates a distinct destination name instead of silently overwriting the existing file.

If the file is already inside the dashboard directory, the Editor stores its relative path without copying it.

The file picker includes filters for PNG, JPEG, WebP, BMP, and GIF. Decoding still depends on the application's image codec; simply renaming a file to one of these extensions does not make it a valid image.

#### Icon: shared library asset

For `Type = Icon`, `Source` opens the global icon browser. The browser allows selection by library, text search, and visual preview. It also reports the selected icon's relevant capabilities, including whether it is animated and whether it can be tinted.

An icon source is recorded as a logical name, for example `lucide:cpu`, not as a dashboard-relative filename.

!!! warning
    Changing `Type` between `Image` and `Icon` clears `Source`. Select a new source after changing the type; an old file path is not interpreted as an icon name, and an old icon name is not interpreted as an image file.


See [Image Source Type](properties.md#image-source-type) and [Image Source](properties.md#image-source).

### Step 3. Set the size and position

Use `General → Geometry` or the Canvas handles to configure the widget's position and dimensions. Unlike Gauge, Image does not force a square layout.

- `X` / `Y` — logical position on the dashboard Canvas.
- `Width` / `Height` — the bounds into which the graphic is drawn.
- `Rotation` — rotates the widget through the common dashboard transform.

The Image widget requires positive `Width` and `Height` values. The source's original aspect ratio is not automatically preserved by the widget dimensions; aspect-ratio handling depends on the selected Fit mode.

See [Width](properties.md#width), [Height](properties.md#height), and [Rotation](properties.md#rotation).

### Step 4. Choose how the graphic fits

Open `Image → Image → Fit`. The Image widget supports three fitting modes:

| Fit | What happens |
| --- | --- |
| `contain` | Fits the whole graphic inside the widget bounds without distortion. Empty space may remain along one axis. |
| `cover` | Preserves the aspect ratio while filling the widget bounds. Parts of the graphic outside the bounds are clipped. |
| `stretch` | Fits the graphic to the exact widget rectangle. Width and height are scaled independently, so the image may be distorted. |

For an icon or logo whose proportions matter, `contain` is generally the best choice. Use `cover` when filling the rectangle is more important than showing every edge. Use `stretch` for artwork intentionally designed to fill a particular rectangle.

!!! info
    `Fit` controls the drawing of the **source graphic inside the widget**, not the dimensions of the widget itself. Changing Fit does not resize the widget or change the file on disk.


See [Image Fit](properties.md#image-fit).

### Step 5. Set opacity and optional icon tint

Open `Image → Image → Opacity` to adjust the graphic's transparency.

- `Opacity = 1` — fully visible, subject to the source graphic's own transparency.
- `Opacity = 0.5` — approximately half opacity.
- `Opacity = 0` — the graphic is invisible.

For `Type = Icon`, `Appearance → Foreground → Color` is available **only when the selected icon supports tinting**. SVG icons in the shared library commonly provide this capability; raster icons generally retain their own colors. The icon browser's capability information is authoritative for the selected asset.

For `Type = Image`, the original file colors are used. The Foreground Color property is not applicable to file-backed Image widgets. To change a file's colors, edit the image itself rather than relying on an icon-tint control.

!!! warning
    `Opacity` applies to the image content; it does not replace the common widget's background, border, or shadow settings. A transparent image may still occupy a widget with a separately configured background or border.


See [Opacity](properties.md#opacity) and [Foreground Color](properties.md#foreground-color).

### Step 6. Configure animation

An Image widget can display animated sources supported by the current decoder or icon implementation, including animated raster assets such as suitable GIF files.

Open `Image → Image → Loop`:

- When enabled, a multi-frame asset repeats its playback cycle.
- When disabled, a multi-frame file runs through its sequence and holds its final frame.

A single-frame image has no animation to repeat. For icons, the Editor makes `Loop` read-only when the chosen icon has no animated capability. For file-backed Image widgets, the property is available, but it has no visible effect on a static file.

!!! info
    `Loop` is a playback setting, not an animation generator. A static PNG or JPEG remains static regardless of the Loop value. The actual frame timing and available animation depend on the decoded source asset.


See [Image Loop](properties.md#image-loop).

### Step 7. Configure common appearance and layering

The `Appearance` tab contains the regular widget presentation controls:

- `Background` — optional widget background color.
- `Border` — color, width, and corner radius.
- `Shadow` — optional shadow with color, opacity, blur, and offsets.

The Image widget also participates in the dashboard's normal layering, selection, grouping, and Z-order operations.

A regular Image widget is **not** the same as a Canvas Background Image or Canvas Foreground Image layer. Those layers use separate Canvas properties and have their own placement and ordering semantics.

See [Background Color](properties.md#background-color), [Border Color](properties.md#border-color), [Border Width](properties.md#border-width), and [Shadow Enabled](properties.md#shadow-enabled).

### Usage Examples

The following are configuration examples, not downloadable dashboard templates.

| Scenario | Suggested configuration | Expected result |
| --- | --- | --- |
| Dashboard logo | `Type = Image`; select a transparent PNG; `Fit = contain`; `Opacity = 1` | A proportional logo in a movable widget rectangle. |
| Single-color indicator symbol | `Type = Icon`; select a tintable icon; set `Foreground Color`; `Fit = contain` | An icon displayed using the configured tint, where supported. |
| Decorative full-frame artwork | `Type = Image`; select a suitable dashboard image; `Fit = cover` | The source covers its bounds with aspect ratio preserved; edges may be cropped. |
| Animated accent | `Type = Image`; select an animated GIF; `Loop = true` | Repeating playback of the supported multi-frame image. |
| One-shot animated decoration | `Type = Image`; select a supported animated file; `Loop = false` | Playback progresses to the final frame and remains there. |
| Scaled separator | `Type = Image`; use an appropriately designed graphic; `Fit = stretch` | Graphic stretched to fill the given width and height. |

---
## Technical Information

### Purpose and Implementation

Image is a standalone graphical widget with:

- User-facing name: `Image`.
- Persisted type discriminator: `image`.
- Canonical model: `DashboardModel.ImageWidgetDefinition`.
- Renderer: `Widgets.ImageWidgetRenderer`.

Relevant implementation components:

- `DashboardModel.ImageAssetPresentationDefinition` — persistent source type, source, fit, and loop settings.
- `DashboardModel.ImageWidgetDefinition` — widget-level image opacity and validation.
- `ImageVisual` — common file/icon image presentation with fit, tint, opacity, and playback.
- `ImageAssetCache` and `IconAssetCache` — caching and asset loading.
- `AssetPathResolver` — dashboard-relative image path resolution.
- `GlobalAssetResolver` — application-level icon library lookup.
- `IconAssetLoader` — loading and capability detection for shared icons.
- `Editor.ImageSourceTypeDialog`, `Editor.IconBrowserDialog`, and `EditorImageSourceTools` — Editor asset selection.
- `Editor.CanonicalPropertyViewBuilder` — property placement and applicability.

Unlike metric-driven widgets, Image requires no `metric`, `unit`, `format`, `fallback`, thresholds, or numeric evaluation state. It is an independent visual element.

### Canonical Model and Persistence

`ImageWidgetDefinition` extends the common `WidgetDefinition` and includes the following persisted fields:

| JSON field | Canonical member | Meaning |
| --- | --- | --- |
| `type` | Polymorphic type discriminator | `image`. |
| `asset` | `Asset` | Source and presentation object. |
| `asset.sourceType` | `Asset.SourceType` | `file` or `icon`. |
| `asset.source` | `Asset.Source` | Dashboard-relative file path or global icon logical name. |
| `asset.fit` | `Asset.Fit` | `contain`, `cover`, or `stretch`. |
| `asset.loop` | `Asset.Loop` | Whether supported animation repeats. |
| `opacity` | `Opacity` | Widget graphic opacity in the range `0..1`. |
| `color` | Inherited `Color` | Foreground color for tint-capable icons. |

Inherited properties also include `id`, `name`, `z`, `x`, `y`, `width`, `height`, `rotation`, `backgroundColor`, `borderColor`, `borderWidth`, `cornerRadius`, and shadow settings.

The Editor's display term `Image` for a file source maps to the canonical token `file`. `Icon` maps to `icon`. The asset model defaults to file source type, stretch fitting, and looping enabled; the widget opacity defaults to `1`.

#### File source

For `sourceType = file`, `source` must identify an existing image using a path relative to the dashboard directory. Absolute paths and paths that escape the dashboard directory are rejected.

`AssetPathResolver` first checks the relative path exactly as stored under the dashboard directory. For a **bare filename** without a directory, it can also check the dashboard-local `images` subdirectory. An explicitly named subdirectory is not silently rewritten.

The Editor's picker handles files outside the dashboard by copying them to `images`, with collision-safe naming. Persistence uses a relative path; it does not store an absolute import source path.

#### Icon source

For `sourceType = icon`, the source uses the logical `library:name` convention. Icons are resolved beneath the application's `assets/icons/`<library>`/` tree; icon identifiers are not ordinary paths to files in the dashboard directory.

An extensionless logical icon name checks for the legacy SVG identity first (for example `lucide:cpu`). A raster icon ordinarily uses its filename extension in the logical name. If an extensionless name would match several non-SVG formats, resolution requires an explicit extension instead of guessing.

The global icon library supports extensions `.svg`, `.png`, `.jpg`, `.jpeg`, `.bmp`, `.gif`, `.ico`, and `.webp`, subject to successful decoding and validation.

`ImageAssetPresentationDefinition.ChangeSourceType` clears `Source` when the stored source type changes. This prevents a stale file reference from silently becoming an icon reference or vice versa.

### Renderer and Fit Contract

`ImageWidgetRenderer` creates or reuses an `ImageVisual` compiled presentation for a canonical Image widget. It draws inside the widget's logical rectangle, defined by `X`, `Y`, `Width`, and `Height`, using the shared SkiaSharp dashboard rendering pipeline.

`ImageVisual` selects the file or icon pipeline according to `asset.sourceType`. In the standalone Image context, an empty source-type token is internally treated as file; canonical validation nonetheless requires a supported explicit source type.

For file-backed images:

- `contain` computes the largest proportional destination rectangle that stays inside the requested bounds.
- `cover` computes the smallest proportional destination that fully covers the bounds and clips rendering to the widget rectangle.
- `stretch` draws directly into the requested bounds, scaling both axes independently.

For icon-backed images, `ImageVisual` delegates drawing and fit behavior to the selected icon implementation, passing the requested fit token, tint color, opacity, loop flag, and antialiasing setting.

These are the same three canonical Fit options regardless of which Image source type is selected. The fourth `Clip` option used by **Bar Image mode** is not an Image-widget Fit value.

### Color and Opacity Contract

`ImageWidgetDefinition.Opacity` is validated as a finite number between `0` and `1` inclusive.

For file-backed images, rendering preserves the source's intrinsic colors. When widget opacity is less than approximately 1, the file pipeline uses a separate drawing paint with adjusted alpha.

For icon-backed images, `ImageVisual` passes the inherited widget `Color` together with opacity to the icon renderer. Whether the color is applied as tint is determined by the actual icon's capabilities; raster icons normally preserve their intrinsic colors.

The Editor enables Foreground Color only for selected icon assets that report tint capability. For a file source, or an untintable icon, this setting is not applicable even though the inherited color field remains in the canonical widget model.

Common background, border, and shadow settings are separate from the graphic's opacity and are applied through the widget rendering framework.

### Animation and Playback Contract

File-backed Image widgets obtain frames from `ImageAsset.GetFrame(loop)`, which uses the asset's loaded-time playback clock and decoded frame durations. Looping playback wraps to the beginning; non-looping playback clamps to the last frame.

An icon-backed Image widget delegates animation to its `IIconAsset` implementation. Icon capability detection distinguishes static from animated assets and also reports tint and alpha support.

Not all supported file extensions are necessarily animated. Animation is based on decoded frame count and the capabilities of the specific asset, not on a claim that every file of a given format animates.

The current raster image loader enforces resource limits:

- Maximum decoded frames per file: `1000`.
- Maximum decoded bitmap memory per image asset: `128 MiB`.
- Maximum aggregate decoded bitmap memory per dashboard image cache: `256 MiB`.

These are decoded-memory limits, not input-file size limits. A highly compressed animation can consume much more memory when decoded than its file size suggests.

### Editor Behavior and Property Applicability

On addition, `EditorForm.AddImageWidget` first asks for File or Icon and then requires a successful source selection. Cancellation leaves no new widget. File-based initial dimensions come from the decoded dimensions, independently capped at 400 × 300; icon dimensions start at 96 × 96.

The principal Editor properties are:

| Tab | Group | Settings |
| --- | --- | --- |
| `General` | `Source` | `Type` and dynamic `Source` (Image or Icon). |
| `General` | `Geometry` | X, Y, Width, Height, and Rotation. |
| `Appearance` | `Foreground` | Color, applicable to tint-capable Icon sources. |
| `Appearance` | `Background / Border / Shadow` | Common widget presentation. |
| `Image` | `Image` | Fit, Loop, and Opacity. |

The `Image` tab's `Loop` property is read-only for an icon that lacks animated capability. A file-backed Image exposes Loop directly; whether it changes anything depends on the file's actual frame count.

The source picker selects existing files and imports external files as dashboard-local assets. The global icon browser instead enumerates the installed icon libraries and records a logical name.

The ordinary Image widget has no Data, Gauge, States, or Text-specific controls. Canvas background/foreground image layers and graphical state profiles reuse some common asset concepts but have different ownership and applicability rules.

See [Widget Properties — Tabs](properties-by-tab.md) and [Widget Property Dictionary](properties.md) for the current property descriptions and context-dependent availability.

### Validation and Compatibility Contracts

The canonical model requires:

- A valid Image widget type and the inherited common widget structure.
- Positive Width and Height.
- A non-null `asset`.
- A supported `asset.sourceType` of `file` or `icon`.
- A supported `asset.fit` of `contain`, `cover`, or `stretch`.
- A non-empty, resolvable `asset.source`.
- For file sources, an existing image within the dashboard directory and no path escape.
- For icon sources, a valid logical library/icon reference resolving to an installed asset.
- A finite `opacity` in `0..1`.

Compatibility-sensitive behavior includes:

- The persisted widget type remains `image`.
- `Image` and `Icon` in the Editor map to canonical `file` and `icon`; no third source mode is implied.
- Changing source type clears stale Source.
- File assets remain dashboard-local and portable with their owning dashboard; global icon references require a corresponding installed library.
- Fit behavior remains distinct for contain, cover, and stretch; it must not be confused with Bar Image's additional Clip mode.
- Color tinting is capability-dependent; it is not imposed on file-backed images.
- Animation and Loop behavior depend on actual source frames/capabilities.
- Common widget rendering and asset caches remain shared between Editor preview and Runtime where applicable.
- Source modifications should not silently alter the persisted dashboard schema or unrelated widget presentations.

For implementation source, see [PinkieSysMon on GitHub](https://github.com/Jim1537/PinkieSysMon).
