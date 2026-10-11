# Application Architecture

## Process and assembly boundaries

There are two independent Windows processes:

| Boundary | Ownership |
| --- | --- |
| **Runtime** (`src/PinkieSysMon/`) | Application lifetime, telemetry scheduling, metric snapshots, live output sessions, and the local control endpoint. `PinkieApplicationContext` coordinates these services. |
| **Dashboard Editor** (`src/PinkieSysMon.Editor/`) | Interactive document editing, Editor-only metadata, preview state, and the client side of Runtime control. `EditorForm` coordinates these services. |

Both processes use classes from `PinkieSysMon.dll`. **The shared assembly is not a shared process or shared memory:** Editor and Runtime instantiate separate dashboards, metric sources, renderers, and caches. The Editor project references the published Runtime assembly, rather than a third shared-class-library project. See [Editor project configuration](https://github.com/Jim1537/PinkieSysMon/blob/main/src/PinkieSysMon.Editor/PinkieSysMon.Editor.csproj).

## System topology

    Persisted dashboard + application configuration
                    |                   |
              Runtime process     Editor process
                    |                   |
         PinkieApplicationContext   EditorForm
                    |                   |
          +---------+----------+        +-- in-memory document / Editor metadata
          |                    |        +-- independent preview and sample data
    TelemetryEngine      OutputSessionManager
          |                    |        +-- Runtime IPC client ------+
      IMetricSource[]     per-target FramePump                     |
          |                    |                                    |
          v                    |                                    v
      MetricStore -------------+                           Runtime IPC server
     immutable snapshots       |                                    |
                               v                           Runtime control path
                         DashboardRenderer
                               |
                           frame bytes
                               |
                         device transport

The horizontal link from `MetricStore` to output sessions carries **data snapshots**, not commands. The IPC connection carries **control requests and status**, not live widget objects or a stream of rendered frames. The persistence link is file-based.

## Runtime data path

[The Runtime composition root](https://github.com/Jim1537/PinkieSysMon/blob/main/src/PinkieSysMon/PinkieApplicationContext.cs) loads configuration and each Output's assigned canonical dashboard. Telemetry demand is the **deduplicated union of metrics referenced by actively running, visible Outputs**: enabled physical sessions and active Desktop sessions with `BoardEnabled=true`. `ProviderEnabled` independently controls an Output's own metrics publication and does not create dashboard demand. The union is reconciled on output start/stop/reload, and configuration changes use the current committed active-session set. A Desktop session implementation is introduced in Stage 09; invisible-panel render suppression belongs to Stage 13.

`TelemetryEngine` runs an acquisition worker independently of rendering. It combines dashboard-demanded metric IDs (`DashboardMetricUsage`), enabled providers, and `telemetry.json` policies to schedule captures; individual sources supply default intervals when no metric-specific policy exists. Deactivated metrics are cleared instead of retaining stale valid readings. It publishes completed capture batches into `MetricStore`.

**This is the shared scheduling contract.** Provider-specific metric IDs, intervals, discovery, source freshness and recovery remain with [System](../telemetry/system.md#demand-driven-polling), [Libre Hardware Monitor](../telemetry/libre-hardware-monitor.md#demand-driven-polling), and [iCUE](../telemetry/icue-sensor-logging.md#demand-driven-polling).

`MetricStore` replaces the published dictionary on updates. Readers obtain its current snapshot without holding a per-frame read lock, and previously published dictionaries are never subsequently mutated. This is the **ownership boundary between polling and frame production**; neither side requires the other to complete a poll for every frame.

Some metrics use a publication path outside the normal source scheduler. Their ownership and namespace rules are specified in [System → Runtime and Output Performance Metrics](../telemetry/system.md#runtime-and-output-performance-metrics), not here.

**Source anchors:** [TelemetryEngine](https://github.com/Jim1537/PinkieSysMon/blob/main/src/PinkieSysMon/Telemetry.cs), [MetricStore](https://github.com/Jim1537/PinkieSysMon/blob/main/src/PinkieSysMon/MetricStore.cs), [DashboardMetricUsage](https://github.com/Jim1537/PinkieSysMon/blob/main/src/PinkieSysMon/DashboardMetricUsage.cs).

## Runtime output ownership

`OutputSessionManager` owns the set of active output sessions. Each active configured target has its own `FramePump`, renderer instance, connection state, and output worker.

Each configured Output has an independent dynamic metric provider identified by its stable Output ID, not its display label or physical device path. Its passive lifecycle state can remain available without enabling a physical device or a Desktop panel. Per-output performance readings use the `system.output.<OutputId>.*` namespace and are gated by each Output's independent Provider Enabled switch; the global System switch affects only System metrics. The global `system.runtime.version` remains application-level; the former global frame/FPS/JPEG/USB statistics are retired without compatibility aliases. See [System → Runtime and Output Performance Metrics](../telemetry/system.md#runtime-and-output-performance-metrics) for the canonical ID table and physical-versus-Desktop field contract.

**Runtime owns independently loaded canonical dashboards keyed by stable Output ID.** Each active physical session receives its assigned document, not a global active board. Two Outputs may select the same saved dashboard, but each gets a separate in-memory definition/renderer instance. A target-scoped Start/Reload loads and commits only that target's board; a full configuration reload validates all assigned dashboards before applying them and has rollback paths for failed session commits. Desktop output definitions can be assigned and validated but are not displayed until the desktop window session is introduced in Stage 09. Physical Trofeo outputs require explicit `DeviceEnabled=true`; disabled devices never start USB I/O.

A frame worker reads the most recent `MetricStore` snapshot, renders via the shared widget implementation, and delegates the resulting frame to the selected device transport. Runtime's `DashboardRenderer` owns the rendering surfaces and its resource caches; `FramePump` owns the periodic worker and live transport instance.

`DashboardRenderer` rasterizes one canonical logical Canvas in premultiplied BGRA, including `#AARRGGBB` transparency, Canvas image layers and the same widget registry used by the Editor preview. `RenderBgra` returns a **caller-owned** logical-size bitmap with per-pixel alpha intact for future Desktop sessions (Stage 09); its pixels are independent of subsequent renders and must be disposed by the caller. The existing Trofeo `RenderJpeg` continues to rotate/composite that logical frame onto an opaque black native-size wire surface before JPEG encoding. Desktop windows, placement and visible Runtime behavior are not part of Stage 08. Model-specific wire details are documented only in [Device Integration](devices/trofeo-vision-9.16.md).

Startup and configuration reload validate every assigned canonical dashboard. Missing/corrupt assignments produce an output-specific error rather than silently selecting another document or rewriting `app.json`. The old global tray dashboard switch is retired; the tray provides per-Output Edit Dashboard entries. The legacy IPC `Dashboard` string remains a compatibility placeholder for multiple outputs until the status protocol is made target-aware in Stage 24.

**Source anchors:** [OutputSessionManager](https://github.com/Jim1537/PinkieSysMon/blob/main/src/PinkieSysMon/OutputSessionManager.cs), [FramePump](https://github.com/Jim1537/PinkieSysMon/blob/main/src/PinkieSysMon/FramePump.cs), [DashboardRenderer](https://github.com/Jim1537/PinkieSysMon/blob/main/src/PinkieSysMon/DashboardRenderer.cs), [OutputTargetConfig](https://github.com/Jim1537/PinkieSysMon/blob/main/src/PinkieSysMon/AppConfig.cs).

## Editor versus Runtime state

The Editor creates its own `DashboardPreviewRenderer` from the canonical document and uses the same widget renderer registry as Runtime. Its preview produces in-memory pixels for the Editor window, **not** output frames for the live device.

Editor telemetry is an independently captured **dashboard-demanded snapshot**. The preview render timer consumes that snapshot and does not initiate potentially blocking provider capture calls. Specific [System](../telemetry/system.md#editor-integration), [LHM](../telemetry/libre-hardware-monitor.md#editor-integration), and [iCUE](../telemetry/icue-sensor-logging.md#editor-integration) pages own each provider's Editor-side discovery, caching, and failure behavior.

!!! info "Preview Runtime metrics are illustrative"
    The Editor preview can synthesize illustrative `system.output.<OutputId>.*` performance values only for configured, provider-enabled Outputs with rendering/device enablement. It can show passive status with no active device or visible Desktop panel. These preview fixtures are not measurements of the live Runtime, USB connection, or Desktop rendering.

The two processes can load the same persisted dashboard from disk without sharing an editable object. **Save** commits the Editor's document to disk; the already-running Runtime retains its own loaded definition until an explicit reload/start operation is requested.

**Source anchors:** [Editor preview](https://github.com/Jim1537/PinkieSysMon/blob/main/src/PinkieSysMon.Editor/EditorForm.Preview.cs), [Preview renderer](https://github.com/Jim1537/PinkieSysMon/blob/main/src/PinkieSysMon/DashboardPreviewRenderer.cs), [Editor document persistence](https://github.com/Jim1537/PinkieSysMon/blob/main/src/PinkieSysMon.Editor/EditorForm.Document.cs).

## Persistence and schema boundary

These files have different owners and must not be conflated:

| Data | Owner |
| --- | --- |
| `dashboards/<name>/dashboard.json` | Portable canonical canvas and widget model consumed by both processes. |
| `dashboards/<name>/.editor.json` | Editor-specific group/layer hierarchy sidecar; it is **not** the canonical widget schema. |
| `config/editor.json` | Local Editor preferences and per-dashboard view state. |
| `config/app.json` / `config/telemetry.json` | Application/output/provider configuration and telemetry scheduling policy, respectively. New application defaults configure a Desktop output with no physical device enabled. |

`DashboardModel.DashboardDefinition.Load` upgrades supported older persisted documents through `DashboardSchemaMigration`, then validates the canonical model through `DashboardJson`. Current-schema persistence requires validation; Editor undo/redo snapshots use a separate serialization path that can represent intermediate, not-yet-persistable editing state. **A schema change is therefore a shared Runtime/Editor compatibility change**, not a local change to a widget property panel.

**Source anchors:** [DashboardDefinition](https://github.com/Jim1537/PinkieSysMon/blob/main/src/PinkieSysMon/DashboardModel/DashboardDefinition.cs), [DashboardJson](https://github.com/Jim1537/PinkieSysMon/blob/main/src/PinkieSysMon/DashboardJson.cs), [DashboardSchemaMigration](https://github.com/Jim1537/PinkieSysMon/blob/main/src/PinkieSysMon/DashboardSchemaMigration.cs), [Editor metadata](https://github.com/Jim1537/PinkieSysMon/blob/main/src/PinkieSysMon.Editor/DashboardEditorMetadata.cs).

## Control path and reconfiguration

The Editor's Runtime controls communicate with the independent Runtime process over a **local, current-user Windows named pipe**. `RuntimeIpcServer` receives commands; `PinkieApplicationContext` routes state-changing work to its UI-thread orchestration path. The Editor does not call Runtime's live `FramePump` or WinUSB objects directly.

Reload is a coordinated operation across the current dashboard, output sessions, telemetry schedule, and media configuration. It loads and validates candidate input, prepares replacement resources/plans, and applies changes with explicit rollback attempts if a stage fails. The output manager also prepares its session replacements before committing them. **These precautions do not guarantee that recovery from an arbitrary external failure is infallible.**

The control plane additionally governs session start/stop, system suspend/resume, and shutdown. Stopping the final active output through the explicit output-control command path can cause the Runtime process to exit; stopping a target is therefore not necessarily equivalent to leaving an idle background Runtime running.

**Source anchors:** [Runtime IPC](https://github.com/Jim1537/PinkieSysMon/blob/main/src/PinkieSysMon/RuntimeIpc.cs), [Runtime output control](https://github.com/Jim1537/PinkieSysMon/blob/main/src/PinkieSysMon/RuntimeOutputControl.cs), [Application coordination](https://github.com/Jim1537/PinkieSysMon/blob/main/src/PinkieSysMon/PinkieApplicationContext.cs).

## Concurrency and transport lifetime

Runtime telemetry uses a polling worker; each active output session uses a separate frame worker. The Editor has its own UI and preview cadence. These workers share data through published snapshots, while lifecycle and reconfiguration require stronger synchronization.

!!! warning "USB cancellation still depends on native completion"
    Each `FramePump` worker owns its transport's open/send/disposal lifecycle. Native WinUSB I/O runs outside the shared session-state lock; lifecycle changes request cancellation and invalidate superseded connection generations. Buffers and handles remain alive until pending native I/O reaches terminal completion. **A stalled driver may not complete promptly after `CancelIoEx`**, so shutdown is not guaranteed to be bounded. See [Issue #37](https://github.com/Jim1537/PinkieSysMon/issues/37) for unresolved physical acceptance.

