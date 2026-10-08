# Shared Quantitative Widget Contracts

This is an **internal shared contract** for the quantitative **Gauge** and **Bar** widgets, not a third widget or a new Editor-properties reference. It owns only the behavior genuinely common to `DashboardModel.QuantitativeWidgetDefinition`, `WidgetRenderContext.TryGetMetricDouble`, and `CompiledQuantitativeIndicator`. [Gauge](gauge.md) and [Bar](bar.md) keep their independent geometry, drawing, image modes, unavailable appearances, and property applicability. The [Property Dictionary](properties.md) owns the definitions and values of each individual setting.

## Metric resolution and normalized position

<!-- --8<-- [start:quantitative-metric] -->

Gauge and Bar inherit `metric`, `unit`, `min`, `max`, `gap`, `reverse`, and `thresholds` from `QuantitativeWidgetDefinition`. They receive a **current telemetry snapshot** through `WidgetRenderContext`, not by polling a provider during rendering. `DashboardMetricUsage.Collect` requests the configured metric on demand.

`WidgetRenderContext.TryGetMetricDouble` retrieves the reading and checks whether its descriptor identifies a numeric metric. For recognized numeric metrics, `MetricValueConverter` applies the requested `Unit`; for an unknown or nonnumeric descriptor it can accept a finite numeric conversion **only without a requested unit conversion**. Missing, null, unconvertible, or non-finite readings are unavailable; neither widget reuses a stale last-good numeric value.

For a valid **converted** reading `v`, the shared geometric position is:

`ratio = clamp((v - Min) / (Max - Min), 0, 1)`

`Min` and `Max` are interpreted **after conversion, in the selected unit**. The ratio is bounded to the configured interval; the original converted `v` remains available for threshold state-color evaluation. A value below `Min` is a **valid zero-position value**, distinct from an unavailable reading. `Max` must exceed `Min` and both bounds must be finite.

What the ratio **moves or fills** is widget-specific: [Gauge](gauge.md#arc-geometry-and-direction) applies it to arc sweep and needle angle; [Bar](bar.md#geometry-and-reverse-direction) applies it to horizontal content progress and its Fill/Image transforms.

<!-- --8<-- [end:quantitative-metric] -->

## Threshold compilation and color selection

<!-- --8<-- [start:quantitative-thresholds] -->

For an **active threshold-color rendering path**, both widgets use `CompiledQuantitativeIndicator`, built from the widget foreground `color`, `Min`/`Max`, and the persisted `ThresholdSetDefinition`. That definition holds a mode and **exactly three threshold slots**, each with `enabled`, `value`, and `color`. Enabled values must be finite, in range, and in ascending stored order; disabled slots do not create color boundaries.

- `SegmentSolid`: the base color and enabled thresholds establish fixed color intervals along the scale.
- `SegmentTransition`: the base color and enabled thresholds establish color stops for a continuous gradient along the scale, including reversed stop order when required.
- `State`: the **entire active indicator** takes the base color until thresholds are crossed, then uses the color of the last crossed enabled threshold. A threshold `t` is crossed when the **converted** reading `v >= t`, even when the geometric ratio has been clamped.

The active progress still uses the bounded ratio; threshold `State` selection uses `v`. Neither mode automatically recolors the shared widget background, border, or unrelated pointer/needle paints.

The **drawing surface is deliberately not shared**: [Gauge](gauge.md#threshold-modes-and-color-evaluation) paints arc segments/sweep gradients and has a separately colored needle; [Bar Fill](bar.md#fill-rendering-and-thresholds) paints horizontally clipped solid segments/linear gradients/state fill. **Bar Image mode** uses an image-progress path instead: its renderer does not compile threshold colors and its model does not apply the Fill threshold range/order validation in Image mode. The stored thresholds object still exists.

The detailed Editor fields and supported values have one owner: [Threshold Mode](properties.md#threshold-mode), [Threshold Enabled](properties.md#threshold-enabled), [Threshold Value](properties.md#threshold-value), and [Threshold Color](properties.md#threshold-color).

<!-- --8<-- [end:quantitative-thresholds] -->
