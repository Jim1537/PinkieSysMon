# Application Architecture

## Process and assembly boundaries

There are two primary application processes and an optional standalone Launcher process:

| Boundary | Ownership |
| --- | --- |
| **Runtime** (`src/PinkieSysMon/`) | Application lifetime, telemetry scheduling, metric snapshots, live output sessions, and the local control endpoint. `PinkieApplicationContext` coordinates these services. |
| **Dashboard Editor** (`src/PinkieSysMon.Editor/`) | Interactive document editing, Editor-only metadata, preview state, and the client side of Runtime control. `EditorForm` coordinates these services. |
| **Launcher** (`src/PinkieSysMon.Launcher/`) | Optional portable entry point; resolves `bin/` Runtime and Editor from its current application root. Launch buttons and user-requested shortcut installation are independent of telemetry and dashboard state. |

The Launcher has no dependency on the application implementation assembly. Its `ProjectInfoWindow.cs` is a single canonical **source file** compiled into both Launcher and Editor, giving the Launcher and the Editor About command the same text, link, expandable Advanced area, and shortcut maintenance; only the three launch buttons are omitted in About mode. This source sharing does not create a shared process or IPC endpoint.

Both primary application processes use classes from `PinkieSysMon.dll`. **The shared assembly is not a shared process or shared memory:** Editor and Runtime instantiate separate dashboards, metric sources, renderers, and caches. The Editor project references the published Runtime assembly, rather than a third shared-class-library project. See [Editor project configuration](https://github.com/Jim1537/PinkieSysMon/blob/main/src/PinkieSysMon.Editor/PinkieSysMon.Editor.csproj).

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
      IMetricSource[]     per-target session                       |
          |                    |                                    |
          v                    |                                    v
      MetricStore -------------+                           Runtime IPC server
     immutable snapshots       |                                    |
                               v                           Runtime control path
                         DashboardRenderer
                               |
                           frame bytes
                               |
                    Desktop window / device transport

The horizontal link from `MetricStore` to output sessions carries **data snapshots**, not commands. The IPC connection carries **control requests and status**, not live widget objects or a stream of rendered frames. The persistence link is file-based.

## Runtime data path

[The Runtime composition root](https://github.com/Jim1537/PinkieSysMon/blob/main/src/PinkieSysMon/PinkieApplicationContext.cs) loads configuration and each Output's assigned canonical dashboard. Telemetry demand is the **deduplicated union of metrics referenced by actively running, visible Outputs**: enabled physical sessions and active Desktop sessions with `BoardEnabled=true`. `ProviderEnabled` independently controls an Output's own metrics publication and does not create dashboard demand. The union is reconciled on output start/stop/reload, and configuration changes use the current committed active-session set. Desktop-specific rendering and frame suppression are described in [Desktop Outputs](../outputs/desktop.md#session-and-native-alpha-presentation).

`TelemetryEngine` runs an acquisition worker independently of rendering. It combines dashboard-demanded metric IDs (`DashboardMetricUsage`), enabled providers, and `telemetry.json` policies to schedule captures; individual sources supply default intervals when no metric-specific policy exists. Deactivated metrics are cleared instead of retaining stale valid readings. It publishes completed capture batches into `MetricStore`.

**This is the shared scheduling contract.** Provider-specific metric IDs, intervals, discovery, source freshness and recovery remain with [System](../telemetry/system.md#demand-driven-polling), [Libre Hardware Monitor](../telemetry/libre-hardware-monitor.md#demand-driven-polling), and [iCUE](../telemetry/icue-sensor-logging.md#demand-driven-polling).

`MetricStore` replaces the published dictionary on updates. Readers obtain its current snapshot without holding a per-frame read lock, and previously published dictionaries are never subsequently mutated. This is the **ownership boundary between polling and frame production**; neither side requires the other to complete a poll for every frame.

Some metrics use a publication path outside the normal source scheduler. Their ownership and namespace rules are specified in [System → Runtime and Output Performance Metrics](../telemetry/system.md#runtime-and-output-performance-metrics), not here.

**Source anchors:** [TelemetryEngine](https://github.com/Jim1537/PinkieSysMon/blob/main/src/PinkieSysMon/Telemetry.cs), [MetricStore](https://github.com/Jim1537/PinkieSysMon/blob/main/src/PinkieSysMon/MetricStore.cs), [DashboardMetricUsage](https://github.com/Jim1537/PinkieSysMon/blob/main/src/PinkieSysMon/DashboardMetricUsage.cs).

## Runtime output ownership

`OutputSessionManager` owns the active-session set. [Desktop Outputs](../outputs/desktop.md#technical-information) owns native panel configuration, presentation, placement and mouse input; [Device Integration](devices/trofeo-vision-9.16.md) owns physical rendering/transport details. Both consume the shared `MetricStore` snapshot through the canonical widget renderer.

Configured Outputs register dynamic provider descriptors independently of telemetry acquisition. The [Output metric reference](../telemetry/system.md#runtime-and-output-performance-metrics) owns provider enablement, namespaces, fields, publication and unavailable values.

**Runtime owns independently loaded canonical dashboards keyed by stable Output ID.** Each active session receives its assigned document rather than a global active board. Outputs selecting the same saved dashboard receive separate in-memory definitions/renderers. Target Start/Reload commits only that target's board; full configuration reload validates all assignments and has rollback paths for failed session commits.

`DashboardRenderer` owns Skia surfaces and resource caches and rasterizes the canonical logical Canvas, including image layers and widgets, in premultiplied BGRA. `RenderBgra` returns a caller-owned logical-size bitmap with alpha intact; it remains independent of subsequent renders and must be disposed by the caller. Desktop's [native alpha presentation](../outputs/desktop.md#session-and-native-alpha-presentation) consumes that raster. Trofeo's [wire rendering](devices/trofeo-vision-9.16.md#geometry-and-configuration) composites/rotates it onto an opaque black native-size surface before JPEG encoding. Session/transport ownership remains separate from the shared renderer contract.

Startup and configuration reload validate every assigned canonical dashboard. Missing/corrupt assignments produce an output-specific error rather than silently selecting another document or rewriting `app.json`. The old global tray dashboard switch is retired; the tray provides per-Output Edit Dashboard entries. The IPC status and tray control contract is defined [below](#output-status-and-tray-control).

**Source anchors:** [OutputSessionManager](https://github.com/Jim1537/PinkieSysMon/blob/main/src/PinkieSysMon/OutputSessionManager.cs), [FramePump](https://github.com/Jim1537/PinkieSysMon/blob/main/src/PinkieSysMon/FramePump.cs), [DashboardRenderer](https://github.com/Jim1537/PinkieSysMon/blob/main/src/PinkieSysMon/DashboardRenderer.cs), [OutputTargetConfig](https://github.com/Jim1537/PinkieSysMon/blob/main/src/PinkieSysMon/AppConfig.cs).

## Editor versus Runtime state

The Editor creates its own `DashboardPreviewRenderer` from the canonical document and uses the same widget renderer registry as Runtime. Its preview produces in-memory pixels for the Editor window, **not** output frames for the live device.

Editor telemetry is an independently captured **dashboard-demanded snapshot**. The preview render timer consumes that snapshot and does not initiate potentially blocking provider capture calls. Specific [System](../telemetry/system.md#editor-integration), [LHM](../telemetry/libre-hardware-monitor.md#editor-integration), and [iCUE](../telemetry/icue-sensor-logging.md#editor-integration) pages own each provider's Editor-side discovery, caching, and failure behavior.

!!! info "Preview Runtime metrics are illustrative"
    The Editor preview can synthesize illustrative `system.output.<OutputId>.*` performance values only for configured, provider-enabled Outputs with rendering/device enablement. It can show passive status with no active device or visible Desktop panel. These preview fixtures are not measurements of the live Runtime, USB connection, or Desktop rendering.

The two processes can load the same persisted dashboard from disk without sharing an editable object. **Save** commits the Editor's document to disk; the already-running Runtime retains its own loaded definition until an explicit reload/start operation is requested.

**Source anchors:** [Editor preview](https://github.com/Jim1537/PinkieSysMon/blob/main/src/PinkieSysMon.Editor/EditorForm.Preview.cs), [Preview renderer](https://github.com/Jim1537/PinkieSysMon/blob/main/src/PinkieSysMon/DashboardPreviewRenderer.cs), [Editor document persistence](https://github.com/Jim1537/PinkieSysMon/blob/main/src/PinkieSysMon.Editor/EditorForm.Document.cs).

### Editor Output Start / Reload

Before an Output Start/Reload action, the Editor uses its existing **Save / Don't Save / Cancel** prompt for a dirty document. Save failure or Cancel prevents the action. Don't Save retains the unsaved editing state; Runtime loads the last saved dashboard version. The initiating dashboard name is captured for the action, and comparison uses fresh `STATUS` metadata for the selected Output rather than the top-level compatibility summary.

| Assignment choice | Editor request |
| --- | --- |
| Same assigned dashboard | Start/Reload that Output directly. |
| Use editor dashboard | Request target assignment through Runtime IPC, confirm it, and start the Output if assignment did not already reload an active session. |
| Keep assigned dashboard | Start/Reload the target's existing assignment. |
| Cancel | Send no assignment/start request and do not launch an absent Runtime. |

**Start/Reload All** collects every enabled Output's individual choice before dispatch. It then addresses those explicit IDs in order, preserving disabled Device/Board ownership. This Editor sequence is not an atomic multi-output transaction: a later failure reports the earlier confirmed assignments/loads instead of claiming that every change was rolled back. A single-target action never uses global configuration `RELOAD`. The Output menu shows each reported assignment and distinguishes Desktop display state from physical connection state.

When Runtime is absent, local configuration provides a provisional choice only. After the choices, Editor launches Runtime with `--outputs-stopped`, confirms actual IPC status and rechecks the planned target assignments/enablement before dispatch. An existing process with unavailable IPC blocks a duplicate launch. The Editor does not persist Output assignments directly; Runtime owns those writes. Missing legacy per-Output metadata or detected assignment/target-set drift produces an actionable failure rather than a guessed assignment.

Completed requests are checked against observed assignment and active-session status. `WAITING`/`SUSPENDED` are reported as such and do not imply a connected or visible display; reported Output error/ambiguity is a failed result. These client-side snapshots are checks, not a cross-process atomic reservation; concurrent state can change between IPC operations.

**Source anchors:** [Editor Output commands](https://github.com/Jim1537/PinkieSysMon/blob/main/src/PinkieSysMon.Editor/EditorForm.Commands.cs), [Start workflow](https://github.com/Jim1537/PinkieSysMon/blob/main/src/PinkieSysMon.Editor/EditorOutputStartWorkflow.cs), [Choice dialogs](https://github.com/Jim1537/PinkieSysMon/blob/main/src/PinkieSysMon.Editor/EditorDialogs.cs).

## Persistence and schema boundary

These files have different owners and must not be conflated:

| Data | Owner |
| --- | --- |
| `dashboards/<name>/dashboard.json` | Portable canonical canvas and widget model consumed by both processes. |
| `dashboards/<name>/.editor.json` | Editor-specific group/layer hierarchy sidecar; it is **not** the canonical widget schema. |
| `config/editor.json` | Local Editor theme, toolbar icon size and per-dashboard view preferences; no physical Output settings. |
| `config/app.json` / `config/telemetry.json` | Application/output/provider configuration and telemetry scheduling policy, respectively. New application defaults configure a Desktop output with no physical device enabled. |
| Windows HKCU Run entry | App-wide Runtime autostart setting, managed by the Editor. |

`DashboardModel.DashboardDefinition.Load` upgrades supported older persisted documents through `DashboardSchemaMigration`, then validates the canonical model through `DashboardJson`. Current-schema persistence requires validation; Editor undo/redo snapshots use a separate serialization path that can represent intermediate, not-yet-persistable editing state. **A schema change is therefore a shared Runtime/Editor compatibility change**, not a local change to a widget property panel.

**Source anchors:** [DashboardDefinition](https://github.com/Jim1537/PinkieSysMon/blob/main/src/PinkieSysMon/DashboardModel/DashboardDefinition.cs), [DashboardJson](https://github.com/Jim1537/PinkieSysMon/blob/main/src/PinkieSysMon/DashboardJson.cs), [DashboardSchemaMigration](https://github.com/Jim1537/PinkieSysMon/blob/main/src/PinkieSysMon/DashboardSchemaMigration.cs), [Editor metadata](https://github.com/Jim1537/PinkieSysMon/blob/main/src/PinkieSysMon.Editor/DashboardEditorMetadata.cs).

## Control path and reconfiguration

The Editor's Runtime controls communicate with the independent Runtime process over a **local, current-user Windows named pipe**. `RuntimeIpcServer` receives commands; `PinkieApplicationContext` routes state-changing work to its UI-thread orchestration path. The Editor does not call Runtime's live `FramePump` or WinUSB objects directly.

Reload is a coordinated operation across the current dashboard, output sessions, telemetry schedule, and media configuration. It loads and validates candidate input, prepares replacement resources/plans, and applies changes with explicit rollback attempts if a stage fails. The output manager also prepares its session replacements before committing them. **These precautions do not guarantee that recovery from an arbitrary external failure is infallible.**

The control plane additionally governs session start/stop, system suspend/resume, and shutdown. Stopping the final active output through the explicit output-control command path can cause the Runtime process to exit; stopping a target is therefore not necessarily equivalent to leaving an idle background Runtime running.

### Settings save and apply

**Edit → Settings…** edits detached configuration drafts. **OK** validates/saves changed application settings; **Cancel** discards those drafts. Changed provider/Output settings update the Editor preview and request a configuration reload only from a Runtime that answers IPC. An absent or unreachable Runtime is not launched by Settings; the Editor reports that settings were saved for its next start. This does not establish that an unresponsive existing process has stopped. Output edits validate every assigned saved dashboard before persistence, including disabled Outputs.

`EditorForm.ShowSettingsAsync` captures the original application file before opening the dialog. `EditorSettingsPersistence` rejects a detected intervening file change before save. After a failed Runtime apply it restores the original file byte-for-byte and attempts to reapply the previous Runtime configuration, provided the file still matches this operation's write. A newer concurrent write is preserved; failed or unconfirmed recovery is reported explicitly. These checks do not provide a cross-process compare-and-swap guarantee. Desktop position/tray writes can therefore require reopening a stale Settings draft.

The kind-specific workflows are [Desktop](../outputs/desktop.md#step-3-apply-the-settings-and-display-the-panel) and, for physical hardware, [Device Settings](devices/trofeo-vision-9.16.md#settings-and-device-ownership). Their target validation does not change dashboard schema or a USB wire contract. Startup preferences and Editor theme changes have their own application paths; a theme change may require restarting the Editor.

**Source anchors:** [Settings coordination](https://github.com/Jim1537/PinkieSysMon/blob/main/src/PinkieSysMon.Editor/EditorForm.cs), [Settings persistence](https://github.com/Jim1537/PinkieSysMon/blob/main/src/PinkieSysMon.Editor/EditorSettingsPersistence.cs), [apply/recovery checks](https://github.com/Jim1537/PinkieSysMon/blob/main/tests/PinkieSysMon.RegressionTests/DeviceSettingsStage23Tests.cs).

### Output status and tray control

`STATUS` returns the Runtime's manager-owned snapshot for every configured Output. Each Output includes its assigned `Dashboard`, `Kind`, refresh interval, independent `OutputEnabled` and `ProviderEnabled`, and kind-specific Device Enabled or Desktop Board Enabled/Lock/visibility. `IsActive` describes an instantiated active session; `IsConnected` is the physical transport connection or Desktop native-window visibility, not passive USB presence. Desktop `IsVisible` is unavailable for a physical Output; device-only fields are unavailable for Desktop. The existing `UsbState` payload field carries the session lifecycle/status string for either kind, without fabricating Desktop USB measurements.

The v1 named pipe, existing commands and top-level `Dashboard` compatibility summary remain unchanged. Per-Output `Dashboard` is authoritative for assignments. The added nullable fields are **unknown** when an older Runtime omits them; clients must not infer false enablement, a board assignment or a device kind from a missing property.

The Runtime tray's **Outputs** submenu shows each assignment and state and invokes the same target Start/Reload/Stop control path as IPC. Start/Reload cannot enable a disabled Device or Desktop; Provider Enabled does not grant output ownership. Summary counts distinguish visible Desktop windows from connected devices. Tray and IPC actions share telemetry-demand reconciliation and retain the existing final-active-Output shutdown behavior. This status/presentation change does not add device discovery polling or alter USB transport.

Target configuration requests use the same v1 pipe and Runtime UI-thread control path:

| Command | JSON payload after the tab separator | Effect |
| --- | --- | --- |
| `OUTPUT_ASSIGN_DASHBOARD` | `{"TargetId":"desktop-1","Dashboard":"Default"}` | Validate and assign only that Output's saved dashboard; reload it if active, preserve inactivity if stopped or disabled. |
| `DESKTOP_SET_BOARD_ENABLED` | `{"TargetId":"desktop-1","Value":true}` | Explicitly start/show or disable/unload that Desktop; preserve Provider Enabled, Lock and siblings. |
| `DESKTOP_SET_LOCK` | `{"TargetId":"desktop-1","Value":true}` | Change only that Desktop's Lock; retain its loaded board and active/stopped state. |

These requests prepare only the selected session and preserve sibling document instances and pending local configuration values. Persistence patches only the requested field in existing `app.json`, retaining unknown fields; checks reject a changed target binding/requested field or a detected intervening write. Session, persistence and telemetry failures attempt to restore the prior state. Disk rollback checks that the file still matches this operation's write before restoring the original bytes; detected newer content is retained and incomplete recovery is reported. These byte checks and atomic file replacement do not provide a cross-process compare-and-swap guarantee; native/driver recovery can also fail as described above. The tray exposes dashboard assignment and Desktop Board Enabled/Lock through these same requests. Hiding the final Desktop through Board Enabled leaves an idle Runtime available for a later enable; explicit Stop commands retain their shutdown policy. No configuration request grants physical Device Enabled or changes Provider Enabled.

**Source anchors:** [Runtime IPC](https://github.com/Jim1537/PinkieSysMon/blob/main/src/PinkieSysMon/RuntimeIpc.cs), [Runtime output control](https://github.com/Jim1537/PinkieSysMon/blob/main/src/PinkieSysMon/RuntimeOutputControl.cs), [Target configuration transaction](https://github.com/Jim1537/PinkieSysMon/blob/main/src/PinkieSysMon/RuntimeOutputConfigurationControl.cs), [Application coordination](https://github.com/Jim1537/PinkieSysMon/blob/main/src/PinkieSysMon/PinkieApplicationContext.cs).

### Runtime command feedback

Tray and Editor IPC mutations use one Runtime-owned command-feedback path. The response and Windows app notification carry the same target name/ID, assigned dashboard and observed session state; request acceptance alone does not establish a visible panel or physical frame delivery. Requested assignment, enablement, Lock and active/stopped state are checked after apply. A failed or unverifiable apply reports its cause, current state where available and observed partial changes without assuming complete rollback. IPC timeout/disconnect after a command write attempt reports an unknown outcome and asks the client to query status before retrying.

| Outcome | Feedback |
| --- | --- |
| Applied control with observed ready Output | Information notification; a connected transport does not assert delivered frames. |
| Applied control with pending readiness | Warning and explicit current state. The existing tray status snapshot may later produce one terminal connection/presentation or failure notification. |
| Rejected, unverified or reported failed Output | Error with target and actionable cause; applied settings/sessions may remain. |
| Shutdown request | Acceptance only; unloading completes after the IPC response and existing stopping handshake. |

Pending completion ownership is bounded by configured Outputs. A superseding command, changed target assignment/enablement/session ownership or shutdown removes it; the established full reload used by tray physical settings also clears prior completions. Each Runtime All command produces aggregate feedback, while target commands do not adopt sibling failures. PING/STATUS, passive discovery, telemetry/frame timers and automatic transport retries never create notification requests. Observation adds no timer or device polling and may miss transient states between existing snapshots. Physical enable feedback follows the existing tray consent path; its internal feedback kind has no IPC verb and grants no new device ownership route.

The established `WindowsAppNotificationService` owns native Windows slide-in delivery, registration fallback and disposal. Delivery availability does not change a command result. Its activation callback only logs; command feedback does not activate an Editor/Runtime window or modify Desktop Lock. Actual shell delivery, focus behavior and native integration require Windows observation.

**Source anchors:** [Command feedback](https://github.com/Jim1537/PinkieSysMon/blob/main/src/PinkieSysMon/RuntimeCommandFeedback.cs), [Windows notification service](https://github.com/Jim1537/PinkieSysMon/blob/main/src/PinkieSysMon/WindowsAppNotificationService.cs), [Runtime coordination](https://github.com/Jim1537/PinkieSysMon/blob/main/src/PinkieSysMon/PinkieApplicationContext.cs).

## Concurrency and transport lifetime

Runtime telemetry acquisition and physical frame delivery use separate workers; [Desktop presentation](../outputs/desktop.md#session-and-native-alpha-presentation) uses the WinForms UI thread. The Editor has its own UI and preview cadence. Published snapshots separate acquisition from rendering, while lifecycle and reconfiguration require stronger synchronization.

!!! warning "USB cancellation still depends on native completion"
    Each `FramePump` worker owns its transport's open/send/disposal lifecycle. Native WinUSB I/O runs outside the shared session-state lock; lifecycle changes request cancellation and invalidate superseded connection generations. Buffers and handles remain alive until pending native I/O reaches terminal completion. **A stalled driver may not complete promptly after `CancelIoEx`**, so shutdown is not guaranteed to be bounded. See [Issue #37](https://github.com/Jim1537/PinkieSysMon/issues/37) for unresolved physical acceptance.

