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
| Graph → Processing | [Filter](properties.md#filter) |
| Graph → Line | [Interpolation](properties.md#interpolation), [Stroke Width](properties.md#stroke-width), [Fill Enabled](properties.md#fill-enabled), [Fill Opacity](properties.md#fill-opacity) |
| Image → Background Image | Shared [Fit](properties.md#image-fit), [Opacity](properties.md#opacity), [Loop](properties.md#image-loop), [Color](properties.md#image-layer-color) |
| Appearance | Foreground, background, border, rounded corners, shadow and three threshold slots |

Auto follows the visible observations. Fixed uses the saved Min/Max; changing to Auto retains those settings for a later return to Fixed. Unit changes affect the plotted values: review Fixed bounds and threshold values in the selected unit.

Graph's background image is independent of Line/Columns. It uses the normal dashboard file/icon picker and asset capabilities. A file remains dashboard-relative and follows the standard clipboard import workflow. An empty source draws no image. Selecting or clearing Source refreshes the shared capability controls; only tintable icons expose Color.

### Interpolation and filtering

[Interpolation](properties.md#interpolation) controls Line geometry. Linear joins observations with straight segments; Bezier uses a cubic curve through the displayed observations, with matching area fill. It does not average values. Columns retain their discrete geometry.

[Filter](properties.md#filter) independently processes values in either plot mode. It can suppress brief spikes and introduces response lag; use None when every raw peak matters. Auto range and all threshold colors use the displayed, filtered values. Both settings default to the existing raw Linear appearance.

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
| `interpolation`, `filter` | `Interpolation`, `Filter` (schema 25) |
| `windowSeconds` | `WindowSeconds` |
| `scaleMode` | `ScaleMode` |
| `strokeWidth` | `StrokeWidth` |
| `fillEnabled`, `fillOpacity` | `FillEnabled`, `FillOpacity` |
| `image` | `CanvasImageLayerDefinition`; `asset` plus layer opacity/color |

--8<-- "widgets/properties.md:widget-base-fields"

| Schema | Compatibility event |
| --- | --- |
| 23 | Introduces this Graph contract. Any pre-23 `graph` is rejected before migration rather than reinterpreted. |
| 24 | Adds Reverse. Existing schema-23 Graphs receive `reverse=true`, preserving their original right-to-left direction. A schema-23 Graph containing the then-unsupported reverse field remains invalid. Current Graphs default to false when the field is absent. |
| 25 | Adds interpolation and filtering. Existing Graphs receive `Linear` and `None`, preserving all other fields and direction. Pre-25 Graphs containing either then-unsupported processing field are rejected before mutation. |

Supported earlier schemas run their established migrations first. The Graph-specific migrations change only the version for Graph-free documents. History and filter state are RAM-only; dashboard saves, clipboard and undo snapshots persist settings, never samples or working state.

Older application versions cannot read schema-25 dashboards. An ordinary source revert does not downgrade a saved document; retain the earlier dashboard file when testing compatibility. Graph shares `reverse` with numeric widgets but has no Bar/Gauge `gap` field and no private polling or sampling-interval property.

### Historical projection and geometry

The [publication history contract](../development/architecture.md#publication-history) owns acquisition, demand union, resource bounds, eviction, continuity and measurement epochs. `GraphWidgetRenderer` queries a consistent frame at monotonic time `now`; X is `(timestamp - (now - window)) / window`. No sample is generated by drawing. DateTime/DateTimeOffset projection uses the original stored payload and pinned descriptor, never the current frame clock.

Auto takes extrema from every finite displayed (converted and optionally filtered) **in-window** observation before drawing reduction. A boundary predecessor may clip a connecting line at the older-time edge but cannot affect those extrema. A constant value `v` receives padding `max(1e-6, abs(v) × 1e-6)` on each side, saturated to finite endpoints. Fixed retains the configured bounds; line intersections use the displayed values before geometric clipping, so an out-of-range peak does not move its crossing in time.

Dense continuous runs retain chronological first/minimum/maximum/last observations per pixel bin, preserving spikes and gaps. Columns use bounded widths derived from neighboring real observation times. A Line fill closes each continuous run to the effective lower bound; gaps and isolated points do not gain a filled bridge. Fill opacity multiplies the selected color alpha once, including segment joins.

Common border width insets the plot and optional background image. Both are clipped to the remaining rounded interior. Rotation and common container effects use the same enclosing rendering pipeline as other widgets. Scrolling Graph content uses uncached shadow rendering so an unchanged latest scalar cannot freeze a shadow.

### Per-Graph signal processing

Unit/component conversion precedes filtering; filtering precedes Auto extrema, drawing reduction, interpolation and color evaluation. The common raw history and provider schedule are unchanged. Each filtered Graph caches its results by publication sequence and descriptor epoch. Repeated frames and window scrolling reuse those values; neither filters old samples again nor admits synthetic samples. The first render primes from the oldest available query observation, including its boundary predecessor. A different initial retained window can therefore give a recursive filter a different initial state.

Unavailable observations and continuity breaks reset the filter. Epoch changes, renderer reconstruction (including processing edits), or loss of all retained overlap clear its cache and prime from available data. Nothing is extrapolated through silence or a gap. The fixed cache has at most the history capacity; it adds 24 bytes per record plus five doubles to the existing per-Graph render scratch reservation, still subject to the 32 MiB per-dashboard scratch limit. This is separate from the shared history storage budget.

Bezier uses piecewise cubic Hermite geometry expressed as Bezier controls, with shared minmod-limited tangents using actual timestamp spacing. Each segment's controls stay ordered between its endpoint values, preventing overshoot; equal timestamps and gaps do not obtain a smooth connecting tangent. Endpoint tangents use the adjacent secant. Curves are computed from the retained drawing points, and range/time clipping slices the curve rather than clamping its original samples. Stroke and fill use the same geometry. The newest segment can change shape when a later neighboring observation arrives, while its sampled values remain unchanged.

### Historical threshold colors

Thresholds stay in absolute displayed-value coordinates when Auto changes the viewport. They are not rescaled into percentages of its span.

| Mode | Historical result |
| --- | --- |
| SegmentSolid | Vertical value bands use the foreground below the first enabled threshold, then the last reached threshold color. |
| SegmentTransition | Colors interpolate between absolute stops; Auto's foreground anchor is `min(visible minimum, first enabled threshold)`. Stops outside the viewport still affect boundary colors. |
| State | Each Line segment and its area use the newer-in-time displayed observation's state color; each isolated point or Column uses its own value. The latest value never recolors earlier history. |

Equal enabled boundaries are stable in stored slot order: the later slot wins at equality. State evaluates the displayed value before geometric clipping. The common background image, border and shadow colors are independent of these thresholds.

### Implementation references

`DashboardModel/GraphWidgetDefinition.cs`, `Widgets/GraphWidgetRenderer.cs`, `Widgets/QuantitativeColorScale.cs`, `Widgets/GraphSignalProcessing.cs`, `MetricHistoryStore.cs`, `Telemetry.cs`, and `EditorPreviewTelemetry.cs` own the model, drawing, color evaluation, signal processing, history, acquisition and preview lifecycle respectively. Tests are in `GraphHistoryTests`, `GraphWidgetTests`, `GraphSignalTests`, `GraphTelemetryTests`, `GraphPreviewLifecycleTests`, and `GraphEditorTests`.
