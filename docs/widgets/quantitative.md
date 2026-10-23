# Shared Quantitative Widget Contracts

This is an **internal shared contract** for **Gauge**, **Bar**, and **Graph**, not a widget or a separate Editor-properties reference. `DashboardModel.NumericWidgetDefinition` owns their metric, unit, DateTime projection, range, thresholds and Reverse. `QuantitativeWidgetDefinition` adds Gauge/Bar `gap`; each widget owns the meaning of its drawing direction. Each widget page owns geometry, drawing, image modes, unavailable appearances, and property applicability. The [Property Dictionary](properties.md) owns the definitions and values of each individual setting.

## Metric resolution and normalized position

<!-- --8<-- [start:quantitative-metric] -->

The shared numeric model provides `metric`, `unit`, `component`, `componentFormat`, `min`, `max`, `reverse`, and `thresholds`. `DashboardMetricUsage.Collect` requests the configured metric on demand; no renderer polls a provider. Gauge and Bar receive a **current telemetry snapshot** through `WidgetRenderContext`; [Graph](graph.md#historical-projection-and-geometry) consumes publication history through the same conversion contract.

`MetricValueConverter.TryResolveQuantitative`, also used by `WidgetRenderContext.TryGetMetricDouble`, checks whether its descriptor identifies a numeric metric. For recognized numeric metrics, `MetricValueConverter` applies the requested `Unit`; unknown or non-DateTime nonnumeric descriptors can accept a finite numeric conversion only without a unit or Component. Missing, null, unconvertible, or non-finite readings are unavailable; no widget substitutes a last-good value for an unavailable observation.

For DateTime metrics, the shared DateTime component converter projects the selected
[Component](properties.md#component) to a finite scalar before the same quantitative
geometry is evaluated. Optional numeric `componentFormat` selects the scalar's
quantized precision, per the canonical [Component](properties.md#component) contract.
Current-value numeric caching is keyed by metric, unit, component and precision; different
clock hands therefore remain independent. The unavailable state is preserved
for migrated widgets without a selected Component.

For Gauge/Bar and Graph Fixed, a valid **displayed** reading `v` (after conversion/projection and Graph's optional [processing](graph.md#per-graph-signal-processing)) has the bounded geometric position:

`ratio = clamp((v - Min) / (Max - Min), 0, 1)`

`Min` and `Max` are interpreted **after conversion, in the selected unit**. The ratio is bounded to the configured interval; the unclipped displayed `v` remains available for threshold state-color evaluation. A value below `Min` is a **valid zero-position value**, distinct from an unavailable reading. `Max` must exceed `Min` and both bounds must be finite.

What the ratio **moves or fills** is widget-specific: [Gauge](gauge.md#arc-geometry-and-direction) applies it to arc sweep and needle angle; [Bar](bar.md#geometry-and-reverse-direction) applies it to horizontal content progress and its Fill/Image transforms. Graph uses vertical position; its [Auto range and time geometry](graph.md#historical-projection-and-geometry) have their own contract.

<!-- --8<-- [end:quantitative-metric] -->

## Threshold compilation and color selection

<!-- --8<-- [start:quantitative-thresholds] -->

For an **active threshold-color rendering path**, `QuantitativeColorScale` compiles absolute-value thresholds and evaluates State colors. Gauge/Bar wrap it in `CompiledQuantitativeIndicator` for their normalized progress drawing; Graph uses it with a historical viewport. All start from widget foreground `color` and the persisted `ThresholdSetDefinition`. That definition holds a mode and **exactly three threshold slots**, each with `enabled`, `value`, and `color`. Enabled values must be finite and in ascending stored order; disabled slots do not create color boundaries. Range requirements belong to [Threshold Value](properties.md#threshold-value).

- `SegmentSolid`: the base color and enabled thresholds establish fixed color intervals along the scale.
- `SegmentTransition`: the base color and enabled thresholds establish color stops for a continuous gradient along the scale, including reversed stop order when required.
- `State`: the **active indicator** takes the base color until thresholds are crossed, then uses the color of the last crossed enabled threshold. A threshold `t` is crossed when the **displayed numeric** reading `v >= t`, even when the geometric ratio has been clamped.

Gauge/Bar active progress still uses the bounded ratio; threshold `State` selection uses `v`. These modes do not automatically recolor the shared widget background, border, or unrelated pointer/needle paints.

The **drawing surface is deliberately not shared**: [Gauge](gauge.md#threshold-modes-and-color-evaluation) paints arc segments/sweep gradients and has a separately colored needle; [Bar Fill](bar.md#fill-rendering-and-thresholds) paints horizontally clipped solid segments/linear gradients/state fill. **Bar Image mode** uses an image-progress path instead: its renderer does not compile threshold colors and its model does not apply the Fill threshold range/order validation in Image mode. The stored thresholds object still exists.

Graph uses [historical segment/column colors](graph.md#historical-threshold-colors), rather than recoloring the whole history from the latest reading.

The detailed Editor fields and supported values have one owner: [Threshold Mode](properties.md#threshold-mode), [Threshold Enabled](properties.md#threshold-enabled), [Threshold Value](properties.md#threshold-value), and [Threshold Color](properties.md#threshold-color).

<!-- --8<-- [end:quantitative-thresholds] -->
