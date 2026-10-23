# Graph

Graph plots one metric over a rolling time window. Choose a connected **Line** with optional area fill, or discrete **Columns**. The horizontal axis is elapsed time; new observations appear at their actual publication times. [Reverse](properties.md#reverse) selects the scrolling direction.

## User Guide

### Add and configure

Choose **Add Widget → Graph**. A new Graph uses `system.uptime`, a 300 × 120 rectangle, Line mode and Auto scale. It begins empty and collects observations while its dashboard is active; it does not backfill earlier readings.

| Editor location | Settings |
| --- | --- |
| General → Source | [Metric](properties.md#metric) |
| General → Background Image | Shared [Type](properties.md#image-source-type) and [Source](properties.md#image-source) |
| General → Geometry | Position, independent Width/Height, Rotation |
| Data → Value | [Source Unit](properties.md#source-unit), [Unit](properties.md#unit), DateTime [Component and Format](properties.md#component) |
| Data → Range | [Scale Mode](properties.md#scale-mode), [Min](properties.md#min), [Max](properties.md#max) |
| Graph → Timeline | [History Window](properties.md#history-window), [Mode](properties.md#graph-mode), [Reverse](properties.md#reverse) |
| Graph → Line | [Stroke Width](properties.md#stroke-width), [Fill Enabled](properties.md#fill-enabled), [Fill Opacity](properties.md#fill-opacity) |
| Image → Background Image | Shared [Fit](properties.md#image-fit), [Opacity](properties.md#opacity), [Loop](properties.md#image-loop), [Color](properties.md#image-layer-color) |
| Appearance | Foreground, background, border, rounded corners, shadow and three threshold slots |

Auto follows the visible observations. Fixed uses the saved Min/Max; changing to Auto retains those settings for a later return to Fixed. Unit changes affect the plotted values: review Fixed bounds and threshold values in the selected unit.

Graph's background image is independent of Line/Columns. It uses the normal dashboard file/icon picker and asset capabilities. A file remains dashboard-relative and follows the standard clipboard import workflow. An empty source draws no image. Selecting or clearing Source refreshes the shared capability controls; only tintable icons expose Color.

### Interpret the plot

- A constant value remains a valid series. Auto adds a small range around it; it does not invent a zero baseline.
- Missing or invalid publications create breaks. Silence does not extend the last reading to the newest-time edge, and old observations eventually leave the window.
- Columns are individual observations, not counts or averages of a time bucket. Line joins only continuous observations; isolated observations are dots.
- Built-in axes, ticks, grid, labels and legend are absent. Compose captions, values or background artwork with the existing widgets.

Shared [threshold settings](properties.md#threshold-mode) apply in both plot modes. Their historical interpretation is described below. Runtime and Editor collect independent data; illustrative Output performance in Editor is excluded from Graph history, per [preview ownership](../development/architecture.md#editor-versus-runtime-state).

## Technical Information

### Canonical model and compatibility

`DashboardModel.GraphWidgetDefinition` uses discriminator `graph` and inherits the shared `NumericWidgetDefinition`. Common numeric conversion and threshold ownership are documented in [Shared Quantitative Widget Contracts](quantitative.md).

| JSON field | Canonical member |
| --- | --- |
| `metric`, `unit`, `component`, `componentFormat` | Shared metric binding and projection |
| `min`, `max`, `thresholds` | Shared range and threshold definitions |
| `mode` | `Mode` |
| `reverse` | Shared `NumericWidgetDefinition.Reverse` (schema 24) |
| `windowSeconds` | `WindowSeconds` |
| `scaleMode` | `ScaleMode` |
| `strokeWidth` | `StrokeWidth` |
| `fillEnabled`, `fillOpacity` | `FillEnabled`, `FillOpacity` |
| `image` | `CanvasImageLayerDefinition`; `asset` plus layer opacity/color |

--8<-- "widgets/properties.md:widget-base-fields"

Schema **23** introduces this Graph contract. Migration from schema 22 changes only the version for Graph-free documents, preserving their semantics; earlier supported versions run their established migrations first. Any pre-23 `graph` is rejected before migration rather than reinterpreted as this widget. Schema **24** adds Graph Reverse. Migration from 23 sets `reverse=true` on existing Graphs to retain their original right-to-left history direction; Graph-free documents change only version. Current Graphs default to `false` when the field is absent. A schema-23 Graph containing the then-unsupported reverse field remains invalid. History is RAM-only and is never serialized into dashboard saves, clipboard or undo snapshots.

Older application versions cannot read schema-24 dashboards. An ordinary source revert does not downgrade a saved document; retain the earlier dashboard file when testing compatibility. Graph shares `reverse` with numeric widgets but has no Bar/Gauge `gap` field and no private polling or sampling-interval property.

### Historical projection and geometry

The [publication history contract](../development/architecture.md#publication-history) owns acquisition, demand union, resource bounds, eviction, continuity and measurement epochs. `GraphWidgetRenderer` queries a consistent frame at monotonic time `now`; X is `(timestamp - (now - window)) / window`. No sample is generated by drawing. DateTime/DateTimeOffset projection uses the original stored payload and pinned descriptor, never the current frame clock.

Auto takes extrema from every finite converted **in-window** observation before drawing reduction. A boundary predecessor may clip a connecting line at the older-time edge but cannot affect those extrema. A constant value `v` receives padding `max(1e-6, abs(v) × 1e-6)` on each side, saturated to finite endpoints. Fixed retains the configured bounds; line intersections use the original converted values before clipping, so an out-of-range peak does not move its crossing in time.

Dense continuous runs retain chronological first/minimum/maximum/last observations per pixel bin, preserving spikes and gaps. Columns use bounded widths derived from neighboring real observation times. A Line fill closes each continuous run to the effective lower bound; gaps and isolated points do not gain a filled bridge. Fill opacity multiplies the selected color alpha once, including segment joins.

Common border width insets the plot and optional background image. Both are clipped to the remaining rounded interior. Rotation and common container effects use the same enclosing rendering pipeline as other widgets. Scrolling Graph content uses uncached shadow rendering so an unchanged latest scalar cannot freeze a shadow.

### Historical threshold colors

Thresholds stay in absolute converted value coordinates when Auto changes the viewport. They are not rescaled into percentages of its span.

| Mode | Historical result |
| --- | --- |
| SegmentSolid | Vertical value bands use the foreground below the first enabled threshold, then the last reached threshold color. |
| SegmentTransition | Colors interpolate between absolute stops; Auto's foreground anchor is `min(visible minimum, first enabled threshold)`. Stops outside the viewport still affect boundary colors. |
| State | Each Line segment and its area use the newer-in-time observation's state color; each isolated point or Column uses its own value. The latest value never recolors earlier history. |

Equal enabled boundaries are stable in stored slot order: the later slot wins at equality. State evaluates the original converted value even when Fixed clips its geometry. The common background image, border and shadow colors are independent of these thresholds.

### Implementation references

`DashboardModel/GraphWidgetDefinition.cs`, `Widgets/GraphWidgetRenderer.cs`, `Widgets/QuantitativeColorScale.cs`, `MetricHistoryStore.cs`, `Telemetry.cs`, and `EditorPreviewTelemetry.cs` own the model, drawing, color evaluation, history, acquisition and preview lifecycle respectively. Tests are in `GraphHistoryTests`, `GraphWidgetTests`, `GraphTelemetryTests`, `GraphPreviewLifecycleTests`, and `GraphEditorTests`.
