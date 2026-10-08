# Shared State-Profile Contracts

Binary, Power, Media System, and Media Player use the same persisted state-profile structure and state-specific rendering pipeline. This page owns **only the common mechanism**. Each [widget guide](../README.md#available-widgets) owns its own required state keys, default icons, source selection, value text, unavailable behavior, and validation exceptions. The [Property Dictionary](properties.md) owns individual Editor-property definitions.

## Persisted profile structure

<!-- --8<-- [start:profile-schema] -->

Every entry in the canonical `profiles` dictionary is a `DashboardModel.StateVisualProfileDefinition`:

| Profile JSON field | Meaning |
| --- | --- |
| `contentType` | `image` or `value`. |
| `asset.sourceType` | `icon` or `file` when `contentType = image`. |
| `asset.source` | Logical icon identifier or dashboard-relative image path. |
| `asset.fit` | Graphical fitting mode (Contain, Cover, or Stretch). |
| `asset.loop` | Animation looping for supported animated sources. |
| `color` | Profile color and supported icon tint, or Value-state text foreground. |
| `opacity` | State-specific opacity from 0 to 1. |
| `textPresentation` | State-local font, alignment, outline, and overflow settings for Value content. |

The Editor's `Source Type` options map to persisted content as follows:

- **Icon** → `contentType = image`, `asset.sourceType = icon`.
- **Image** → `contentType = image`, `asset.sourceType = file`.
- **Value** → `contentType = value`.

The asset and text-presentation objects remain distinct in the canonical model. Changing between Icon and Image can clear an incompatible asset source; switching to Value does not convert the asset into text. Each state has an independent presentation, so changing one state must not change the other states.

Every widget requires its own complete set of recognized profile keys, without unsupported extra keys. Validation requires a supported content type, non-null asset and text-presentation structures, finite opacity in `0..1`, and a valid icon or an existing dashboard-relative file for active graphical content. An inactive graphical asset is not automatically a required active text source.

<!-- --8<-- [end:profile-schema] -->

## Shared rendering and animation

<!-- --8<-- [start:profile-rendering] -->

State-driven widgets delegate their graphical and Value-state drawing to `StateProfileWidgetRenderer`, using a projection of the canonical state profile into the common render profile. An active Image/Icon uses the shared image/icon cache, image fit, state opacity, and supported icon tint. File-backed images preserve their own colors rather than acquiring an icon-only tint.

For Value content, `TextContentRenderer` uses the selected state's own `TextPresentationDefinition`, foreground color, opacity, alignment, and outline. **The text being displayed is widget-specific**; choosing Value does not turn every state widget into a general metric reader.

Where an asset supports animation, playback can be measured relative to the activation of its current state/source; a change of state can restart that animation. `Loop` has a visible effect only when the asset is animated and supports looping. If no state profile is active, the state renderer deactivates its playback tracker. None of these visual behaviors controls the underlying hardware or media application.

Shared widget appearance (background, border, shadow, geometry, and rotation) remains outside individual profiles. Editor preview and Runtime use the same canonical projection and widget rendering contract.

<!-- --8<-- [end:profile-rendering] -->

## Shared geometry and Value-state overflow

<!-- --8<-- [start:profile-geometry] -->

The widget has a shared Width and Height even though each state has its own content and text presentation:

- **Any Icon/Image profile:** graphical or mixed content requires positive Width **and** Height. All states share a fixed-size widget container; Value-state text must fit its effective bounds.
- **Every profile is Value:** non-negative stored dimensions are allowed; text layout can be content-derived. `Width = 0` permits automatic text width, with `Overflow Mode = None` for Value states.
- **Positive Width:** Value-state text uses a constrained overflow mode: `Clip`, `Ellipsis`, `ShrinkToFit`, `Wrap`, `Scroll`, or `Bump`. Mixed Icon/Image/Value content cannot use auto-width `None`.

`TextOverflowStateContract` reconciles content-source changes, shared Width edits, and per-state overflow settings instead of persisting contradictory combinations. Returning even one state to graphical content removes Value-only auto-width eligibility. `WidgetGeometry` and `ValueTextLayout` provide effective layout bounds where appropriate.

The detailed Editor contracts are in [Width](properties.md#width) and [Overflow Mode](properties.md#overflow-mode).

<!-- --8<-- [end:profile-geometry] -->
