# Widget Properties — Tabs


## General

### Identity

#### Name
--8<-- "widgets/properties.md:name"

### Source

#### Value Source Type
--8<-- "widgets/properties.md:value-source-type"

#### Metric
--8<-- "widgets/properties.md:metric"

#### Text
--8<-- "widgets/properties.md:text"

#### Image Source Type
--8<-- "widgets/properties.md:image-source-type"

#### Image Source
--8<-- "widgets/properties.md:image-source"

#### Power Source
--8<-- "widgets/properties.md:power-source"

#### Media Source
--8<-- "widgets/properties.md:media-source"

#### Media Player Metric
--8<-- "widgets/properties.md:media-player-metric"

### Background Image

#### Image Source Type
--8<-- "widgets/properties.md:image-source-type"

#### Image Source
--8<-- "widgets/properties.md:image-source"

### Foreground Image

#### Image Source Type
--8<-- "widgets/properties.md:image-source-type"

#### Image Source
--8<-- "widgets/properties.md:image-source"

### Geometry

#### X
--8<-- "widgets/properties.md:x"

#### Y
--8<-- "widgets/properties.md:y"

#### Width
--8<-- "widgets/properties.md:width"

#### Height
--8<-- "widgets/properties.md:height"

#### Rotation
--8<-- "widgets/properties.md:rotation"


#### Orientation
--8<-- "widgets/properties.md:orientation"

## Appearance

### Foreground

#### Foreground Color
--8<-- "widgets/properties.md:foreground-color"

### Background

#### Background Color
--8<-- "widgets/properties.md:background-color"


### Border

#### Border Color
--8<-- "widgets/properties.md:border-color"

#### Border Width
--8<-- "widgets/properties.md:border-width"

#### Corner Radius
--8<-- "widgets/properties.md:corner-radius"

### Shadow

#### Shadow Enabled
--8<-- "widgets/properties.md:shadow-enabled"

#### Shadow Offset X
--8<-- "widgets/properties.md:shadow-offset-x"

#### Shadow Offset Y
--8<-- "widgets/properties.md:shadow-offset-y"

#### Shadow Blur
--8<-- "widgets/properties.md:shadow-blur"

#### Shadow Opacity
--8<-- "widgets/properties.md:shadow-opacity"

#### Shadow Color
--8<-- "widgets/properties.md:shadow-color"

### Thresholds

#### Threshold Mode
--8<-- "widgets/properties.md:threshold-mode"

#### Threshold 1

##### Threshold Enabled
--8<-- "widgets/properties.md:threshold-enabled"

##### Threshold Value
--8<-- "widgets/properties.md:threshold-value"

##### Threshold Color
--8<-- "widgets/properties.md:threshold-color"

#### Threshold 2

##### Threshold Enabled
--8<-- "widgets/properties.md:threshold-enabled"

##### Threshold Value
--8<-- "widgets/properties.md:threshold-value"

##### Threshold Color
--8<-- "widgets/properties.md:threshold-color"

#### Threshold 3

##### Threshold Enabled
--8<-- "widgets/properties.md:threshold-enabled"

##### Threshold Value
--8<-- "widgets/properties.md:threshold-value"

##### Threshold Color
--8<-- "widgets/properties.md:threshold-color"

## Data

### Value

#### Source Unit
--8<-- "widgets/properties.md:source-unit"

#### Unit
--8<-- "widgets/properties.md:unit"

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

#### Min
--8<-- "widgets/properties.md:min"

#### Max
--8<-- "widgets/properties.md:max"

### Endpoint Overrides

#### Endpoint Type Override
--8<-- "widgets/properties.md:endpoint-type-override"

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

## Image

The Image tab shows only properties applicable to the current graphical context: Image widget, Bar with [Mode](properties.md#bar-mode)=`Image`, graphical state profiles, or Canvas image layers.

### Image

#### Image Source
--8<-- "widgets/properties.md:image-source"

#### Image Fit
--8<-- "widgets/properties.md:image-fit"

#### Image Fit
--8<-- "widgets/properties.md:image-fit"

#### Progress Mode
--8<-- "widgets/properties.md:progress-mode"

#### Image Loop
--8<-- "widgets/properties.md:image-loop"

#### Image Loop
--8<-- "widgets/properties.md:image-loop"

#### Opacity
--8<-- "widgets/properties.md:opacity"

### State

#### Image Fit
--8<-- "widgets/properties.md:image-fit"

#### Image Loop
--8<-- "widgets/properties.md:image-loop"

#### State Color
--8<-- "widgets/properties.md:state-color"

#### Opacity
--8<-- "widgets/properties.md:opacity"

### Background Image

#### Image Fit
--8<-- "widgets/properties.md:image-fit"

#### Opacity
--8<-- "widgets/properties.md:opacity"

#### Image Loop
--8<-- "widgets/properties.md:image-loop"

#### Image Layer Color
--8<-- "widgets/properties.md:image-layer-color"

### Foreground Image

#### Image Fit
--8<-- "widgets/properties.md:image-fit"

#### Opacity
--8<-- "widgets/properties.md:opacity"

#### Image Loop
--8<-- "widgets/properties.md:image-loop"

#### Image Layer Color
--8<-- "widgets/properties.md:image-layer-color"

## Text

For **Text / Value**, the Font, Alignment, Outline, and Overflow groups belong to `textPresentation`. For state-driven widgets, the same canonical `TextPresentationDefinition` is used inside each Value state profile; foreground state color/opacity remain fields of the profile itself.

### Foreground

#### State Color
--8<-- "widgets/properties.md:state-color"

#### Opacity
--8<-- "widgets/properties.md:opacity"

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
