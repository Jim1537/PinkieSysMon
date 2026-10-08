# Shared State-Profile Contracts

Binary, Power, Media System, and Media Player use the same persisted state-profile structure and state-specific rendering pipeline. This page owns **only the common mechanism**. Each [widget guide](../README.md#available-widgets) owns its own required state keys, default icons, source selection, value text, unavailable behavior, and validation exceptions. The [Property Dictionary](properties.md) owns individual Editor-property definitions.

## Persisted profile structure

<!-- --8<-- [start:profile-schema] -->

Each entry in the persisted `profiles` dictionary is a `DashboardModel.StateVisualProfileDefinition`. It combines several **distinct parts of one state**:

- **Content selection:** `contentType` chooses graphical or Value presentation, with `asset.sourceType` identifying the graphical source kind.
- **Graphical payload:** the `asset` object contains the graphical source and its fit/loop presentation.
- **State appearance:** `color` and `opacity` belong to that particular profile.
- **Value payload:** `textPresentation` holds the state's font, alignment, outline, and overflow presentation.

This list describes **model composition**, not a second set of property definitions. The [Property Dictionary](properties.md) exclusively defines Editor labels, supported values, persisted JSON paths, and applicability: [State Source Type](properties.md#state-source-type), [Image Source](properties.md#image-source), [Image Fit](properties.md#image-fit), [Image Loop](properties.md#image-loop), [State Color](properties.md#state-color), [Opacity](properties.md#opacity), and the [Text property groups](properties-by-tab.md#text). For example, the exact Icon/Image/Value-to-JSON mapping is owned by **State Source Type**, not repeated here.

Both payload objects remain separate in the canonical profile, including when one is inactive. A graphical source-kind change can clear an incompatible stored asset source, but selecting Value does not convert that asset into text. Changing one state's presentation must preserve unrelated state profiles.

Each widget owns its required profile keys, defaults, and state-selection/unavailable rules. Shared validation checks supported profile content, non-null graphical and text-presentation structures, and valid state appearance; **only an active graphical source** must resolve to a valid icon or existing dashboard-relative file. The precise field and range contracts are in the Property Dictionary. Unsupported extra profile keys and missing required states are rejected by each widget's canonical validation.

<!-- --8<-- [end:profile-schema] -->

## Shared rendering and animation

<!-- --8<-- [start:profile-rendering] -->

State-driven widgets delegate their graphical and Value-state drawing to `StateProfileWidgetRenderer`, using a projection of the canonical state profile into the common render profile. An active Image/Icon uses the shared `StateIconVisualCache` and image-asset rendering paths, image fit, state opacity, and supported icon tint. File-backed images preserve their own colors rather than acquiring an icon-only tint.

For Value content, `TextContentRenderer` uses the selected state's own `TextPresentationDefinition`, foreground color, opacity, alignment, and outline. **The text being displayed is widget-specific**; choosing Value does not turn every state widget into a general metric reader.

Where an asset supports animation, **activation-relative playback** can be measured from the time the current state/source becomes active; changing state can reset or restart that animation. `Loop` has a visible effect only when the asset is animated and supports looping. If no state profile is active, the state renderer deactivates its playback tracker. None of these visual behaviors controls the underlying hardware or media application.

The shared SkiaSharp widget appearance pipeline (background, border, shadow, geometry, and rotation) remains outside individual profiles. Editor preview and Runtime use the same canonical projection and widget rendering contract.

<!-- --8<-- [end:profile-rendering] -->

## Shared geometry and Value-state overflow

<!-- --8<-- [start:profile-geometry] -->

Width and Height belong to the **widget**, while graphical assets and Value text belong to **individual state profiles**. Their interaction changes the layout:

- **Mixed or graphical configuration:** if any state uses an Icon/Image, the widget uses one explicit positive-size container shared by every state. Value-state text is laid out within and clipped to that container.
- **All-Value configuration:** if every state uses Value content, `WidgetGeometry` and `ValueTextLayout` can derive intrinsic text geometry from the active profile rather than requiring a graphical container.
- **Transition between configurations:** `TextOverflowStateContract` reconciles changes to source kinds, shared dimensions, and per-state text overflow. Switching a state to graphical content can materialize a constrained width and normalize the Value states; no obsolete auto-width/overflow combination may remain.

The **exact dimension limits, automatic-width eligibility, and available overflow modes** are defined only by [Width](properties.md#width), [Height](properties.md#height), and [Overflow Mode](properties.md#overflow-mode) in the Property Dictionary.

<!-- --8<-- [end:profile-geometry] -->
