# Widget Properties — Tabs


Select a tab below to browse the same canonical property definitions by their location in the Editor. Each property is presented in a standard Material info block; entries are maintained in the [Property Dictionary](properties.md).

When multiple widgets are selected, the Editor shows only compatible shared properties. Different current values appear as `...`; predefined dropdown options retain their normal labels, and selecting an option applies it to all compatible selected widgets.

Property groups are **expanded by default**, including nested groups. A group you collapse stays collapsed when switching widgets or tabs and after restarting the Editor. These UI-only preferences are saved in `config/editor.json`, independently of dashboard JSON; expanding a group again clears its saved collapsed override.

=== "General"

    ## General

    ### Identity

    #### Name
    --8<-- "widgets/properties.md:name"

    #### Type and ID
    Widget type and instance identity are read-only.

    ### Data Source

    #### Metric
    --8<-- "widgets/properties.md:metric"

    #### Source selection for Value, Power, and Media System
    --8<-- "widgets/properties.md:value-source-type"
    --8<-- "widgets/properties.md:power-source"
    --8<-- "widgets/properties.md:media-source"

    #### Literal Text (Value)
    --8<-- "widgets/properties.md:text"

    #### Media Player Metric
    --8<-- "widgets/properties.md:media-player-metric"

    ### Image Source (Image widget only)

    This Image-specific group follows the universal Data Source group; it is not nested inside or substituted for Metric.

    #### Type (Image or Icon)
    --8<-- "widgets/properties.md:image-source-type"

    #### Image / Icon (contextual source selector)
    --8<-- "widgets/properties.md:image-source"

    Canvas is not a widget: General shows root Identity and Orientation. Its Width/Height and other common properties belong to Panel; Canvas has no widget ID or Data Source.

=== "Panel"

    ## Panel

    The common Panel tab is available on every widget and on the root Canvas. It owns geometry, both graphical layers, border and shadow, independently of widget-content styling. On Canvas, X/Y are fixed at zero, and Padding, Rotation and Z-order are read-only/not applicable; Width/Height, Corner Radius and graphical layers remain editable. Canvas Shadow controls are persisted here; the Desktop-only rendering and viewport contract is described in the [Desktop guide](../outputs/desktop.md#placement-dpi-and-mouse-routing). Physical Outputs ignore this setting.

    ### Geometry

    #### X / Y
    --8<-- "widgets/properties.md:x"
    --8<-- "widgets/properties.md:y"

    #### Width / Height
    --8<-- "widgets/properties.md:width"
    --8<-- "widgets/properties.md:height"

    #### Padding, Rotation, Corner Radius and Z-order
    --8<-- "widgets/properties.md:widget-base-fields"

    ### Background

    #### Mode / Color / Source
    --8<-- "widgets/properties.md:background-color"

    ### Foreground

    #### Mode / Color / Source
    Same layer contract as Background, drawn last above Border. This is **not** the content Color.

    ### Border

    #### Color / Width / Style / Sides
    --8<-- "widgets/properties.md:border-color"
    --8<-- "widgets/properties.md:border-width"

    ### Shadow

    #### Enabled / Color / Blur / Offsets
    --8<-- "widgets/properties.md:shadow-enabled"
    --8<-- "widgets/properties.md:shadow-color"
    --8<-- "widgets/properties.md:shadow-blur"
    --8<-- "widgets/properties.md:shadow-offset-x"
    --8<-- "widgets/properties.md:shadow-offset-y"

=== "Appearance"

    ## Appearance

    Appearance is content-specific, not a second owner of Panel geometry or decoration.

    ### Content

    #### Default Color (state-driven widgets)
    --8<-- "widgets/properties.md:foreground-color"

    ### Thresholds (Bar / Gauge / Graph)

    #### Threshold Mode
    --8<-- "widgets/properties.md:threshold-mode"

    #### Threshold 1 / 2 / 3
    --8<-- "widgets/properties.md:threshold-enabled"
    --8<-- "widgets/properties.md:threshold-value"
    --8<-- "widgets/properties.md:threshold-color"

=== "Data"

    ## Data

    ### Value

    #### Source Unit
    --8<-- "widgets/properties.md:source-unit"

    #### Unit
    --8<-- "widgets/properties.md:unit"

    #### Component
    --8<-- "widgets/properties.md:component"

    #### Format
    --8<-- "widgets/properties.md:format"

    ### Display

    #### Prefix
    --8<-- "widgets/properties.md:prefix"

    #### Suffix
    --8<-- "widgets/properties.md:suffix"

    #### Fallback
    --8<-- "widgets/properties.md:fallback"

    ### Evaluation

    #### Evaluation Mode
    --8<-- "widgets/properties.md:evaluation-mode"

    #### True If
    --8<-- "widgets/properties.md:true-if"

    #### Setpoint
    --8<-- "widgets/properties.md:setpoint"

    ### Range

    #### Scale Mode
    --8<-- "widgets/properties.md:scale-mode"

    #### Min
    --8<-- "widgets/properties.md:min"

    #### Max
    --8<-- "widgets/properties.md:max"

    ### Endpoint Overrides

    #### Endpoint Type Override
    --8<-- "widgets/properties.md:endpoint-type-override"

=== "Gauge"

    ## Gauge

    ### Horseshoe

    #### Track Enabled
    --8<-- "widgets/properties.md:track-enabled"

    #### Reverse
    --8<-- "widgets/properties.md:reverse"

    #### Start Angle
    --8<-- "widgets/properties.md:start-angle"

    #### End Angle
    --8<-- "widgets/properties.md:end-angle"

    #### Track Thickness
    --8<-- "widgets/properties.md:track-thickness"

    #### Track Background Color
    --8<-- "widgets/properties.md:track-background-color"

    #### Track Border Width
    --8<-- "widgets/properties.md:track-border-width"

    #### Track Border Color
    --8<-- "widgets/properties.md:track-border-color"

    #### Track Corner Radius
    --8<-- "widgets/properties.md:track-corner-radius"

    #### Gap
    --8<-- "widgets/properties.md:gap"

    ### Needle

    #### Needle Enabled
    --8<-- "widgets/properties.md:needle-enabled"

    #### Needle Thickness
    --8<-- "widgets/properties.md:needle-thickness"

    #### Needle Color
    --8<-- "widgets/properties.md:needle-color"

    #### Needle Start Offset
    --8<-- "widgets/properties.md:needle-start-offset"

    #### Needle End Offset
    --8<-- "widgets/properties.md:needle-end-offset"

    #### Pointer Length
    --8<-- "widgets/properties.md:pointer-length"

    #### Pointer Thickness
    --8<-- "widgets/properties.md:pointer-thickness"

    #### Pointer Color
    --8<-- "widgets/properties.md:pointer-color"

    ### Bar

    #### Bar Mode
    --8<-- "widgets/properties.md:bar-mode"

    #### Reverse
    --8<-- "widgets/properties.md:reverse"

    #### Gap
    --8<-- "widgets/properties.md:gap"

    #### Track Color
    --8<-- "widgets/properties.md:bar-track-color"

=== "Graph"

    ## Graph

    ### Timeline

    #### History Window
    --8<-- "widgets/properties.md:history-window"

    #### Mode
    --8<-- "widgets/properties.md:graph-mode"

    #### Reverse
    --8<-- "widgets/properties.md:reverse"

    ### Processing

    #### Filter
    --8<-- "widgets/properties.md:filter"

    ### Line

    #### Interpolation
    --8<-- "widgets/properties.md:interpolation"


    #### Stroke Width
    --8<-- "widgets/properties.md:stroke-width"

    #### Fill Enabled
    --8<-- "widgets/properties.md:fill-enabled"

    #### Fill Opacity
    --8<-- "widgets/properties.md:fill-opacity"

=== "States"

    ## States

    The State group repeats for each profile supported by the selected state-driven widget.

    - **Binary:** State: False, State: True.
    - **Power:** State: Online, On Battery, Charging, Low, Critical, Fully Charged, Normal, Unknown, Unavailable.
    - **Media System:** Type: Remote Network, Speakers, Line Level, Headphones, Microphone, Handset, Digital Passthrough, S/PDIF, Display Audio, Unknown; State: Unavailable.
    - **Media Player:** State: Playing, Paused, Stopped, Unavailable.

    ### State

    #### State Source Type
    --8<-- "widgets/properties.md:state-source-type"

    #### Image Source
    --8<-- "widgets/properties.md:image-source"

=== "Image"

    ## Image

    Image is a contextual tab. A widget’s own graphical content and active graphical Panel Background / Foreground each have a **separate named group**; Canvas uses the same Panel groups without widget content. A None/Color Panel layer contributes no Image group. The tab is hidden when no group applies.

    ### Image (widget content)

    The standalone Image widget chooses its content Type and Image/Icon source on **General → Image Source**. This tab contains its presentation controls only.

    #### Fit / Loop
    --8<-- "widgets/properties.md:image-fit"
    --8<-- "widgets/properties.md:image-loop"

    #### Icon Color / Opacity
    --8<-- "widgets/properties.md:foreground-color"
    --8<-- "widgets/properties.md:opacity"

    ### Bar image (Image mode only)
    --8<-- "widgets/properties.md:progress-mode"

    ### State-specific image / icon
    --8<-- "widgets/properties.md:state-color"
    --8<-- "widgets/properties.md:image-fit"
    --8<-- "widgets/properties.md:image-loop"
    --8<-- "widgets/properties.md:opacity"

    ### Panel Background / Foreground (Image and Icon modes only)

    #### Fit / Loop
    --8<-- "widgets/properties.md:image-fit"
    --8<-- "widgets/properties.md:image-loop"

    #### Icon Color / Opacity
    --8<-- "widgets/properties.md:image-layer-color"
    --8<-- "widgets/properties.md:opacity"

=== "Text"

    ## Text

    For **Text / Value**, the Font, Alignment, Outline, and Overflow groups belong to `textPresentation`. For state-driven widgets, the same canonical `TextPresentationDefinition` is used inside each Value state profile; state Color uses its RGBA alpha; only Icon has an independent iconOpacity.

    ### Color

    #### State Text Color and inheritance
    --8<-- "widgets/properties.md:state-color"

    ### Font

    #### Font Family
    --8<-- "widgets/properties.md:font-family"

    #### Font Size
    --8<-- "widgets/properties.md:font-size"

    #### Font Weight
    --8<-- "widgets/properties.md:font-weight"

    #### Italic
    --8<-- "widgets/properties.md:italic"

    ### Alignment

    #### Horizontal Alignment
    --8<-- "widgets/properties.md:horizontal-alignment"

    #### Vertical Alignment
    --8<-- "widgets/properties.md:vertical-alignment"

    ### Outline

    #### Outline Color
    --8<-- "widgets/properties.md:outline-color"

    #### Outline Width
    --8<-- "widgets/properties.md:outline-width"

    ### Overflow

    #### Overflow Mode
    --8<-- "widgets/properties.md:overflow-mode"

    #### Scroll Speed
    --8<-- "widgets/properties.md:scroll-speed"

    #### Bump Pause
    --8<-- "widgets/properties.md:bump-pause"


=== "Calculator"

    ## Calculator

    The eleven [Calculator-specific properties](properties.md#calculator-specific-properties) are ordered **Font** (Family), **Buttons** (Text Color, Fill Color, Border Width, Border Color, Corner Radius), then **Display** (Text Color, Fill Color, Border Width, Border Color, Corner Radius). Within Buttons and Display, Border Color and Corner Radius are disabled while Border Width is zero. Panel geometry, common layers, and Action remain separate.

=== "Action"

    ## Action

    Always the last tab for all ordinary widgets. Canvas has no Action, and physical Outputs remain display-only. Actions do not modify visual content.

    ### Activation
    --8<-- "widgets/properties.md:action-activation"

    ### Target
    --8<-- "widgets/properties.md:action-target"

    ### Arguments
    --8<-- "widgets/properties.md:action-arguments"

    ### Run as
    --8<-- "widgets/properties.md:action-run-as"
