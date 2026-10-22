# Application Architecture

## Process and assembly boundaries

There are two primary application processes and an optional standalone Launcher process:

| Boundary | Ownership |
| --- | --- |
| **Runtime** (`src/PinkieSysMon/`) | Application lifetime, telemetry scheduling, metric snapshots, live output sessions, and the local control endpoint. `PinkieApplicationContext` coordinates these services. |
| **Dashboard Editor** (`src/PinkieSysMon.Editor/`) | Interactive document editing, Editor-only metadata, preview state, and the client side of Runtime control. `EditorForm` coordinates these services. |
| **Launcher** (`src/PinkieSysMon.Launcher/`) | Optional portable entry point; resolves `bin/` Runtime and Editor from its current application root. Launch buttons and user-requested shortcut installation are independent of telemetry and dashboard state. |

The Launcher has no dependency on the application implementation assembly. Its `ProjectInfoWindow.cs` is a single canonical **source file** compiled into both Launcher and Editor, giving the Launcher and the Editor About command the same text, link, expandable Advanced area, and shortcut maintenance; only the three launch buttons are omitted in About mode. This source sharing does not create a shared process or IPC endpoint. After a successful button launch, the standalone Launcher window closes; if process startup fails, it remains open and reports the error.

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

An active Output can render its first frame before asynchronous acquisition completes (Desktop on display, physical Output once its transport is ready). After the first completed telemetry batch for each activated/reconfigured or idle-resumed schedule, Runtime requests one additional frame from each active Output: Desktop presents on the UI thread, while physical Outputs wake their existing frame worker. This does not change the configured telemetry or Output refresh intervals, force unsupported sensor reads, or wake disabled/suspended/idle-paused Outputs. Individual capture failures still produce unavailable values rather than stale readings.

Some metrics use a publication path outside the normal source scheduler. Their ownership and namespace rules are specified in [System → Runtime and Output Performance Metrics](../telemetry/system.md#runtime-and-output-performance-metrics), not here.

**Source anchors:** [TelemetryEngine](https://github.com/Jim1537/PinkieSysMon/blob/main/src/PinkieSysMon/Telemetry.cs), [MetricStore](https://github.com/Jim1537/PinkieSysMon/blob/main/src/PinkieSysMon/MetricStore.cs), [DashboardMetricUsage](https://github.com/Jim1537/PinkieSysMon/blob/main/src/PinkieSysMon/DashboardMetricUsage.cs).

### Global Windows user-idle pause

The opt-in Runtime-wide idle setting is **Edit → Editor settings → Runtime activity → Idle timeout (minutes)**. It persists as `Runtime.IdleTimeoutMinutes` in `config/app.json`; `0` (also the default for older files) **disables** idle pausing, while `1..1440` selects a timeout. It is not a dashboard/widget property. The saved number is not overwritten when a Windows power setting changes.

When enabled, the effective threshold is the lesser of the selected minutes and the active Windows power scheme's finite **display-off idle** time (AC or DC as appropriate). Windows **Never** imposes no ceiling. The existing one-second Runtime timer samples `GetLastInputInfo` for this logon session; it refreshes the active Windows display timeout periodically and on power changes. If the input API is unavailable, idle suspension fails open rather than leaving outputs permanently hidden. A missing power-policy reading leaves the configured Runtime timeout in force.

The composition root coordinates two separate suppression causes: Windows suspend and ordinary user idle. At idle, `TelemetryEngine` admits no new scheduled captures and discards superseded in-flight batches; returning input schedules fresh captures without replaying elapsed periods. The manager retains each enabled Output and its assigned dashboard but removes idle demand from acquisition. Desktop sessions hide their existing windows and stop native repaint timers; physical `FramePump` workers suppress raster/JPEG work and periodic USB transfers **without retiring an otherwise valid USB transport**. An in-flight native transfer may complete. A real Windows suspend or changed USB configuration still retires the transport under its normal ownership/cancellation rules; resume does not override a remaining idle gate. Disabled Outputs stay disabled, and Desktop identity, Lock, placement and sibling stacking are preserved. IPC, the tray and inexpensive lifecycle monitoring remain responsive.

No history samples are fabricated during idle. Physical display blanking and post-idle USB continuity on a particular device are **hardware-dependent and require physical acceptance**; a successful simulated transport regression does not establish the real Trofeo behavior.

## Runtime output ownership

Before ordinary Runtime initializes sessions, its process acquires a Windows account-scoped named-mutex ownership lease, shared across installations and Windows sessions for that account. An already-owned or inaccessible lease prevents a second Runtime from starting Outputs even when the first Runtime's IPC endpoint is unresponsive. The process retains the lease through shutdown and fatal-error UI so a potentially stalled native transfer cannot be followed by another Runtime acquiring USB. This prevents **competing PinkieSysMon Runtime processes**; it does not claim ownership against unrelated vendor software or establish device compatibility. Runtime IPC remains the control endpoint, not the ownership lease.

`OutputSessionManager` owns the active-session set. [Desktop Outputs](../outputs/desktop.md#technical-information) owns native panel configuration, presentation, placement and mouse input; [Physical Devices](../outputs/physical_devices.md#technical-information) owns physical inventory, consent and session boundaries, with transport details in its adapter reference. Both consume the shared `MetricStore` snapshot through the canonical widget renderer.

Configured Outputs register dynamic provider descriptors independently of telemetry acquisition. The [Output metric reference](../telemetry/system.md#runtime-and-output-performance-metrics) owns provider enablement, namespaces, fields, publication and unavailable values.

**Runtime owns independently loaded canonical dashboards keyed by stable Output ID.** Each active session receives its assigned document rather than a global active board. Outputs selecting the same saved dashboard receive separate in-memory definitions/renderers. A [selected saved reload](#selected-output-saved-reload) commits only that target's configuration/document; [Reload All](#reload-all-saved-configuration) coordinates the saved common configuration and enabled assignments.

`DashboardRenderer` owns Skia surfaces and resource caches and rasterizes the canonical logical Canvas, including image layers and widgets, in premultiplied BGRA. `RenderBgra` returns a caller-owned logical-size bitmap with alpha intact; it remains independent of subsequent renders and must be disposed by the caller. Desktop's [native alpha presentation](../outputs/desktop.md#session-and-native-alpha-presentation) consumes that raster. Trofeo's [wire rendering](devices/trofeo-vision-9.16.md#geometry-and-configuration) composites/rotates it onto an opaque black native-size surface before JPEG encoding. Session/transport ownership remains separate from the shared renderer contract.

The renderer rejects canvases above 16,777,216 pixels before allocating surfaces: 64 MiB per BGRA raster, independent of device identity. Logical/wire surfaces, exported frames, assets and caches are separate allocations; this is not a total process-memory guarantee. The adaptive generator retains its smaller layout/resource budget. Dashboard validation and schema remain unchanged; an oversized saved document cannot start a renderer.

`FramePump` consumes `IPhysicalFrameTransport`; protocol selection and geometry are specified by the [generic physical Output contract](../outputs/physical_devices.md#common-contract-and-adapter-boundary). `PhysicalFrameProfile` supplies the genuine wire rotation and encoded-payload limit. Ordinary production profiles use the assigned Canvas size without model fitting. Each worker retains its own renderer, transport, cancellation and metrics lifetime. The internal frame budget belongs to bounded diagnostics; ordinary sessions have no frame budget, and neither mode promises bounded native cleanup.

Runtime startup and Reload All validate every configuration record but load canonical dashboard files only for saved enabled Outputs. Disabled assignments can remain unloaded, represented by an absent document-map entry rather than a dummy Default board. Enabling an Output or explicitly changing its assignment loads its actual saved dashboard. A missing/corrupt required document produces an output-specific error without selecting another board or rewriting configuration. Settings retains its [stricter draft validation](#settings-save-and-apply). Tray navigation and Editor launch are defined by the [control contract below](#output-status-and-tray-control).

**Source anchors:** [OutputSessionManager](https://github.com/Jim1537/PinkieSysMon/blob/main/src/PinkieSysMon/OutputSessionManager.cs), [FramePump](https://github.com/Jim1537/PinkieSysMon/blob/main/src/PinkieSysMon/FramePump.cs), [DashboardRenderer](https://github.com/Jim1537/PinkieSysMon/blob/main/src/PinkieSysMon/DashboardRenderer.cs), [OutputTargetConfig](https://github.com/Jim1537/PinkieSysMon/blob/main/src/PinkieSysMon/AppConfig.cs).

## Editor versus Runtime state

On launch **without** `--dashboard`, Control Center opens with no dashboard document selected or created, even when saved dashboards exist. New/Open are available; canvas, layers, preview and document-only commands stay inactive until a document is opened. An explicit `--dashboard <name>` retains the Editor's existing named-document selection (including its fallback when the name is missing). A theme-change restart passes the current document name only if one is open; an empty workspace restarts empty. `config/app.json` `Dashboard.Active` is not an Editor startup selector and Runtime Output assignments are unaffected.

The Editor creates its own `DashboardPreviewRenderer` from the canonical document and uses the same widget renderer registry as Runtime. Its preview produces in-memory pixels for the Editor window, **not** output frames for the live device.

Editor telemetry is an independently captured **dashboard-demanded snapshot**. The preview render timer consumes that snapshot and does not initiate potentially blocking provider capture calls. Specific [System](../telemetry/system.md#editor-integration), [LHM](../telemetry/libre-hardware-monitor.md#editor-integration), and [iCUE](../telemetry/icue-sensor-logging.md#editor-integration) pages own each provider's Editor-side discovery, caching, and failure behavior.

!!! info "Preview Runtime metrics are illustrative"
    The Editor preview can synthesize illustrative `system.output.<OutputId>.*` performance values only for configured, provider-enabled Outputs with rendering/device enablement. It can show passive status with no active device or visible Desktop panel. These preview fixtures are not measurements of the live Runtime, USB connection, or Desktop rendering.

The two processes can load the same persisted dashboard from disk without sharing an editable object. **Save** commits the Editor's document to disk; the already-running Runtime retains its own loaded definition until an explicit reload/start operation is requested.

**Source anchors:** [Editor preview](https://github.com/Jim1537/PinkieSysMon/blob/main/src/PinkieSysMon.Editor/EditorForm.Preview.cs), [Preview renderer](https://github.com/Jim1537/PinkieSysMon/blob/main/src/PinkieSysMon/DashboardPreviewRenderer.cs), [Editor document persistence](https://github.com/Jim1537/PinkieSysMon/blob/main/src/PinkieSysMon.Editor/EditorForm.Document.cs).

### Editor Output Reload

When no Editor document is open, a selected Output reload or Reload All uses each Output's current saved dashboard assignment without presenting an unavailable "Use editor dashboard" choice or sending an empty dashboard name. Stop All and other Runtime controls remain independent of Editor document state.

Before **Outputs (toolbar icon) → &lt;Output name&gt; → Reload** or **Reload All**, the Editor uses its existing **Save / Don't Save / Cancel** prompt for a dirty document. Save failure or Cancel prevents the action. Don't Save retains the unsaved editing state; Runtime loads the last saved dashboard version. The initiating dashboard name is captured for the action, and comparison uses fresh `STATUS` metadata for the selected Output rather than the top-level compatibility summary.

| Assignment choice | Editor request |
| --- | --- |
| Same assigned dashboard | Request the selected Output's saved reload. |
| Use editor dashboard | Request target assignment through Runtime IPC, confirm it, and request reload if assignment did not already reload an active session. |
| Keep assigned dashboard | Request selected saved reload and verify its observed assignment still matches the choice. |
| Cancel | Send no assignment/reload request and do not launch an absent Runtime. |

**Reload All** collects every enabled Output's individual choice before dispatch. It applies and confirms requested per-ID assignments in order, then sends one [aggregate saved-configuration request](#reload-all-saved-configuration), including when no Output is enabled. An assignment can already reload an active target; this does not replace the subsequent common-settings operation. This Editor sequence is not atomic: a later assignment or aggregate failure reports earlier confirmed actions without claiming complete rollback. A single-target action never uses global configuration `RELOAD`. The **Outputs** toolbar dropdown shows reported assignments and distinguishes Desktop display state from physical connection state.

The Editor groups its Outputs dropdown under non-actionable **Desktop** and **Physical Devices** headings, with each configured Output directly accessible as a submenu beneath its heading (no additional navigation level). Entries retain configured order within each kind. Empty groups are omitted; the menu refreshes from saved configuration when opened. Shared **Reload All** and **Stop / Unload All Outputs** commands remain below a separator.

When Runtime is absent, local configuration provides a provisional choice only. After the choices, Editor launches Runtime normally: every saved enabled Output can start, including eligible siblings of a selected target; disabled devices remain disabled. Editor confirms actual IPC status, reports the observed startup count and rechecks planned assignments/enablement before target dispatch. A later failure retains that confirmed startup in its partial result. An existing process with unavailable IPC blocks a duplicate launch. The retired `--outputs-stopped` argument and other unsupported startup arguments are rejected before output sessions are created. The Editor does not persist Output assignments directly; Runtime owns those writes. Missing legacy per-Output metadata or detected assignment/target-set drift produces an actionable failure rather than a guessed assignment.

Completed requests are checked against observed assignment and active-session status. A selected saved reload can apply a newer disk assignment; detected drift from the Editor's plan is reported rather than claiming that the planned board was loaded. `WAITING`/`SUSPENDED` are reported as such and do not imply a connected or visible display; reported Output error/ambiguity is a failed result. These client-side snapshots are checks, not a cross-process atomic reservation; concurrent state can change between IPC operations.

**Source anchors:** [Editor Output commands](https://github.com/Jim1537/PinkieSysMon/blob/main/src/PinkieSysMon.Editor/EditorForm.Commands.cs), [Reload workflow](https://github.com/Jim1537/PinkieSysMon/blob/main/src/PinkieSysMon.Editor/EditorOutputStartWorkflow.cs), [Choice dialogs](https://github.com/Jim1537/PinkieSysMon/blob/main/src/PinkieSysMon.Editor/EditorDialogs.cs).

## Persistence and schema boundary

These files have different owners and must not be conflated:

| Data | Owner |
| --- | --- |
| `dashboards/<name>/dashboard.json` | Portable canonical canvas and widget model consumed by both processes. |
| `dashboards/<name>/.editor.json` | Editor-specific group/layer hierarchy sidecar; it is **not** the canonical widget schema. |
| `config/editor.json` | Local Editor theme, toolbar icon size and per-dashboard view preferences; no physical Output settings. Two independent Editor processes serialize writes via a named mutex, use unique temporary files, and merge independently changed dashboard view entries. Existing settings use `File.Replace`; the first save uses non-overwriting `File.Move`. Readers retain their opened snapshot and exclude in-place writes. If replacement fails with the destination absent, the prepared temporary file is retained for recovery. Shared window/theme preferences remain last-writer-wins. |
| `config/app.json` / `config/telemetry.json` | Application/output/provider configuration and telemetry scheduling policy, respectively. A clean application config contains a disabled Desktop placeholder assigned `desktop_default`; no stock dashboard file is bundled. `ApplicationRootLocator` identifies the application root by `config/app.json` even when an empty `dashboards/` directory has not yet been created. Its strictly identified pristine first-install form can be provisioned after an explicit successful Settings OK, whereas saved user assignments and later missing files remain errors. Per-Output assignments are authoritative, and the retired global `Dashboard.Active` key is ignored on read and omitted on save. Runtime loads only enabled assignments and does not silently reconstruct missing files. See the [Desktop setup guide](../outputs/desktop.md). |
| Windows HKCU Run entry | App-wide Runtime autostart setting, managed by the Editor. |

Two simultaneously running Editors exchange selected widgets through a versioned application-specific Windows clipboard payload. In Editor A, select widgets or groups and choose **Edit → Copy** (Ctrl+C while the canvas/layers have focus); in Editor B, choose **Edit → Paste** (Ctrl+V). The editor offers to import referenced dashboard-local images; declining clears their image sources, and differing destination filenames require an explicit unique-name/use-existing/cancel choice. The operation preserves remaining widget properties and supports one Undo step. The payload contains canonical widget snapshots, selected group hierarchy and the source dashboard directory for resolving file-backed image dependencies. Paste clones destination-local identities, handles image imports separately, and remains an Editor-only operation; no Runtime IPC or dashboard schema change is involved.

`DashboardModel.DashboardDefinition.Load` upgrades supported older persisted documents through `DashboardSchemaMigration`, then validates the canonical model through `DashboardJson`. Current-schema persistence requires validation; Editor undo/redo snapshots use a separate serialization path that can represent intermediate, not-yet-persistable editing state. **A schema change is therefore a shared Runtime/Editor compatibility change**, not a local change to a widget property panel.

**Source anchors:** [DashboardDefinition](https://github.com/Jim1537/PinkieSysMon/blob/main/src/PinkieSysMon/DashboardModel/DashboardDefinition.cs), [DashboardJson](https://github.com/Jim1537/PinkieSysMon/blob/main/src/PinkieSysMon/DashboardJson.cs), [DashboardSchemaMigration](https://github.com/Jim1537/PinkieSysMon/blob/main/src/PinkieSysMon/DashboardSchemaMigration.cs), [Editor metadata](https://github.com/Jim1537/PinkieSysMon/blob/main/src/PinkieSysMon.Editor/DashboardEditorMetadata.cs).

## Control path and reconfiguration

The Editor's Runtime controls communicate with the independent Runtime process over a **local, current-user Windows named pipe**. `RuntimeIpcServer` receives commands; `PinkieApplicationContext` routes state-changing work to its UI-thread orchestration path. The Editor does not call Runtime's live `FramePump` or WinUSB objects directly.

The shared IPC server rejects any request line longer than 8192 characters and bounds the time spent receiving a connected client's line. This input limit is separate from native transfer completion. The named pipe restricts callers to the current Windows account and elevation level; GUID correlation is not an additional authentication mechanism. Candidate testing uses the [transient USB Test coordinator](#transient-usb-test-coordination).

Runtime owns session reconfiguration and shared-subsystem recovery. The [selected saved reload](#selected-output-saved-reload) and [aggregate saved configuration](#reload-all-saved-configuration) contracts below define their distinct preparation, scope and recovery boundaries. New renderers are prepared from saved documents while each previously live renderer is retained for transactional rollback without reopening its image files. A deleted asset referenced only by the former in-memory dashboard must not block a valid saved replacement. The retained native resources are released after successful commit or transferred back on rollback.

The control plane additionally governs global output lifetime, system suspend/resume, and shutdown. An Output's saved enablement is its only per-target activation policy: Desktop uses `Desktop.BoardEnabled`, and physical Outputs use `DeviceEnabled`. There is no independent manually stopped target set. A normal Runtime launch starts sessions for saved enabled Outputs. Applying disabled configuration unloads that session; applying enabled configuration starts it while Runtime's global output lifetime is running. Disabling the final Output keeps Runtime available for configuration. Global Stop All/Exit shuts down Runtime without changing saved enablement; target reload and configuration requests cannot bypass a globally stopped lifetime. Suspend/resume preserves saved enablement and suppresses frame work through the existing session primitives.

### Transient USB Test coordination

`EditorUsbTestClient` sends the typed `USB_TEST_BEGIN`, `USB_TEST_STATUS` and `USB_TEST_CANCEL` messages with an exact client/attempt/snapshot identity. `RuntimeUsbTestService` owns admission and completion through `RuntimeUsbTestSessionGate`. Passive catalog/WinUSB preflight runs both before reserving the attempt and before worker creation; the common transport factory resolves the exact binding again when opening. A catalog match alone is insufficient. Persistent per-instance exclusions are not integrated yet; the candidate Forget action remains disabled.

| Boundary | Implemented behavior |
| --- | --- |
| Process | Use a responsive existing Runtime. If IPC fails, inspect the existing account-wide Runtime mutex; an existing or unverifiable owner is not absence. Only proven absence permits launching the same executable with exactly `--usb-test-only`. The existing exclusive process lease arbitrates concurrent launches across installations for that Windows account. |
| Test-only host | Loads no saved Output sessions or assignments and cannot apply ordinary Output commands. It exits after released completion and a terminal status read, or after a ten-second result-retention interval. With no admitted attempt it expires after thirty seconds. It never becomes ordinary Runtime. |
| Ownership | At most one transient Test. Reject an ordinary enabled/active binding on the same physical node, or an unresolved ordinary owner whose separation cannot be proved. Existing sibling Outputs continue. Ordinary configuration/enable/reload mutations are rejected during Test; explicit global shutdown cancels and waits for native release. |
| Worker | Reuse the common `FramePump`, renderer, protocol adapters, diagnostics and System clock. The adaptive factory produces a virtual board at the requested dimensions and 0°; optional unavailable System metrics retain their normal presentation. No saved Output, board or assignment is created. |
| Observation | Admit at most twenty frames at 500 ms cadence, approximately ten seconds excluding acquisition and transfer delays. A six-second client silence or thirty-second absolute lifetime requests cancellation. Suspend also cancels. These limits stop fresh work; they cannot force a stalled driver to finish. |
| Strict result | The first open/render/encode/send/lifecycle error is sticky and stops fresh frame admission without recovery/reopen for this attempt. Dispose runs on the worker; only completed cleanup permits a terminal released result. Failed cleanup retains ownership and reports `CleanupPending`. Ordinary worker retry policy remains unchanged. |
| Client | Poll only the same attempt; never resend Begin after a lost reply. Correlate handle, phase and technical outcome. A lost/malformed response or local Cancel cannot later become visual success. An unknown terminal outcome remains unsaved and cleanup-unconfirmed. |

The Editor asks the visual question only after a clean, fully released technical result. User-facing actions and approval invalidation are owned by the [candidate workflow](../outputs/physical_devices.md#step-3-configure-and-adopt-the-output). Runtime logs failed attempts and requests the existing Windows failure notification; the Editor also exposes failure. Protocol-level I/O and native cleanup semantics remain with each adapter. Software checks do not establish observed screen behavior or prompt recovery from a hung driver.

### Settings save and apply

**Edit → Editor settings**, **Desktop**, and **Devices** open three independent dialogs from the Editor's main menu. Only Physical Devices retains dynamic device tabs. Editor/Desktop settings use detached drafts saved by **OK** and discarded by **Cancel**. A physical card's **Save Configuration** commits that selected card immediately; configured-card saves compare its original record with current disk state and preserve other records, while confirmed candidate saves additionally reject duplicate bindings and provision the separate working board. Candidate **Forget** uses the same fresh-file apply path for one hashed PnP node; Editor discovery hides that node and Runtime Test checks exclusions both before admission and before acquisition. Configured **Remove** has its own two-phase selected-card operation: Runtime reserves that Output ID, stops its session and confirms native cleanup before Editor deletes only that saved record and requests a reload. A lost response or failed release is not success; after a confirmed stop, a later file/reload failure is reported as partial without restarting the removed session. When no Runtime exists, Editor holds the process lease while writing. The Devices dialog's **Close/Cancel** discard remaining drafts, not earlier completed card commits. Changed provider/Output settings update the Editor preview and request a configuration reload only from a Runtime that answers IPC. Saving Settings does not launch an absent or unreachable Runtime; the Editor reports that settings were saved for its next start. Explicit candidate Test has the separate process policy above. Output edits validate assigned saved dashboards before persistence, including disabled Outputs.

`EditorForm.ShowEditorSettingsAsync` and the Desktop OK path capture the original application file before opening their dialogs. Candidate Save Configuration reads the current file at commit time so unrelated newer settings survive; the selected binding must still be absent and the exact interface/node freshly present. `EditorSettingsPersistence` rejects a detected intervening file change before save. For Save/Forget, failed Runtime apply restores previous file bytes and attempts to reapply the previous Runtime configuration when the file still matches this operation's write. Remove is deliberately different: after terminal selected USB release, it must not restore an enabled old binding if the subsequent reload fails; the deleted record remains saved and a partial state is reported. A newer concurrent write is preserved; failed or unconfirmed recovery is reported explicitly. These checks do not provide a cross-process compare-and-swap guarantee. Desktop position/tray writes can therefore require reopening a stale Settings draft.

The kind-specific workflows are [Desktop](../outputs/desktop.md#step-3-apply-the-settings-and-display-the-panel) and, for physical hardware, [Physical Devices](../outputs/physical_devices.md#step-4-grant-device-use-consent-and-apply). Their target validation does not change dashboard schema or a USB wire contract. Startup preferences and Editor theme changes have their own application paths; a theme change may require restarting the Editor.

**Source anchors:** [Settings coordination](https://github.com/Jim1537/PinkieSysMon/blob/main/src/PinkieSysMon.Editor/EditorForm.cs), [Settings persistence](https://github.com/Jim1537/PinkieSysMon/blob/main/src/PinkieSysMon.Editor/EditorSettingsPersistence.cs), [apply/recovery checks](https://github.com/Jim1537/PinkieSysMon/blob/main/tests/PinkieSysMon.RegressionTests/DeviceSettingsStage23Tests.cs).

### Output status and tray control

`STATUS` returns the Runtime's manager-owned snapshot for every configured Output. Each Output includes its assigned `Dashboard`, `Kind`, refresh interval, independent `OutputEnabled` and `ProviderEnabled`, and kind-specific `DeviceEnabled` or Desktop `BoardEnabled`/Lock/visibility. Settings and the tray present one **Enabled** activation checkbox for each Output, and Runtime command feedback reports its observed value. Their Enabled value follows the shared `OutputTargetContract.IsEnabled` policy: `Desktop.BoardEnabled` for Desktop or `DeviceEnabled` for physical Outputs. `OutputEnabled` is derived from this existing kind-specific storage, not a second saved switch; JSON names, defaults and inheritance remain compatible. `IsActive` describes an instantiated active session; `IsConnected` is the physical transport connection or Desktop native-window visibility, not passive USB presence. Desktop `IsVisible` is unavailable for a physical Output; device-only fields are unavailable for Desktop. The existing `UsbState` payload field carries the session lifecycle/status string for either kind, without fabricating Desktop USB measurements.

The v1 named pipe and top-level `Dashboard` compatibility summary remain unchanged. The retired target `OUTPUT_STOP` verb is rejected; global `OUTPUT_STOP_ALL` remains the explicit shutdown control. Per-Output `Dashboard` is authoritative for assignments. The added nullable fields are **unknown** when an older Runtime omits them; clients must not infer false enablement, a board assignment or a device kind from a missing property.

The Runtime tray uses this root order, with a separator between each row group:

| Root group | Ownership/action |
| --- | --- |
| **Control Center** | Launch the existing Editor with the current application root; startup document behavior is owned by [Editor versus Runtime state](#editor-versus-runtime-state). |
| **Desktop**, followed by configured panel names | Non-clickable heading and root-level per-panel submenus; controls are in the [Desktop guide](../outputs/desktop.md#show-hide-and-reload). |
| **Physical Devices**, followed by configured device names | Non-clickable heading and root-level per-device submenus; controls are in the [Physical Devices guide](../outputs/physical_devices.md#reload-and-choose-the-dashboard). |
| **Reload All** | The shared [saved common-configuration request](#reload-all-saved-configuration). |
| Read-only overall **Status**, then **Open Logs Folder** | Aggregate active/visible/connected counts and the existing local logs folder. |
| **Exit** | Explicit Runtime shutdown. |

A left-button double-click on the Runtime notification-area icon activates the same **Control Center** menu item, opening the Editor without changing any Output state. The menu remains available via right-click.

Each configured Output appears once. Its parent remains accessible when disabled; Enabled is the only remaining activation control. Dashboard choices, Reload and Desktop Lock are disabled when saved enablement is off. Physical enabling additionally requires fresh identity/consent eligibility. The retired Outputs/Edit Output Dashboard/removal branches and per-target State/Visible rows are absent. Tray and IPC actions share telemetry-demand reconciliation and the existing selected-target configuration requests.

`RuntimeTrayMenu` owns ordinary WinForms menu items. Root opening discovers saved dashboard choices and projects manager/session snapshots; passive inventory follows the [physical discovery contract](../outputs/physical_devices.md#passive-inventory-and-explicit-adoption). Replacement prepares new entries before adoption and disposes retired items while still owned, letting WinForms remove them and release their nested submenus; a projection failure cancels opening rather than displaying stale interactive entries. The existing status timer updates only aggregate status/notifications/tooltip, without dashboard discovery, USB probes or an additional timer.

Tray frame values come directly from session observations, independently of Provider Enabled. Counts require a saved enabled, active, connected/visible ready session with a completed frame; inactive, disconnected, suspended/error or no-frame observations display N/A. Invalid durations are unavailable rather than fabricated; a measured completed zero duration is valid. Physical JPEG Size uses observed bytes divided by 1024, while JPEG Quality is effective configuration. Desktop has neither physical field. Metric publication/units remain with the [Output metric owner](../telemetry/system.md#runtime-and-output-performance-metrics).

Target configuration requests use the same v1 pipe and Runtime UI-thread control path:

| Command | JSON payload after the tab separator | Effect |
| --- | --- | --- |
| `OUTPUT_ASSIGN_DASHBOARD` | `{"TargetId":"desktop-1","Dashboard":"MyPanel"}` | Validate and assign only that Output's saved dashboard; reload an active session, retain saved disabled state and respect global lifetime. |
| `DESKTOP_SET_BOARD_ENABLED` | `{"TargetId":"desktop-1","Value":true}` | Save that Desktop's enablement; start/show or disable/unload it within the running global lifetime, preserving Provider Enabled, Lock and siblings. |
| `DESKTOP_SET_LOCK` | `{"TargetId":"desktop-1","Value":true}` | Change only that Desktop's Lock; retain its loaded board, enablement and global lifetime. |

Field-writing operations prepare only the selected session and preserve sibling document instances and pending local configuration values. Persistence patches only the requested field in existing `app.json`, retaining unknown fields; checks reject a changed target binding/requested field or a detected intervening write. Session, persistence and telemetry failures attempt to restore the prior state. Disk rollback checks that the file still matches this operation's write before restoring the original bytes; detected newer content is retained and incomplete recovery is reported. These byte checks and atomic file replacement do not provide a cross-process compare-and-swap guarantee; native/driver recovery can also fail as described above. The tray exposes Dashboard assignment and Desktop Enabled/Lock through the listed IPC requests. They do not grant physical Device Enabled or change Provider Enabled. The tray's consented [physical Enabled operation](../outputs/physical_devices.md#tray-enabled-transaction) shares the field transaction through a dedicated internal manager entry. Activation and global shutdown follow the [lifetime contract](#control-path-and-reconfiguration).

**Source anchors:** [Native tray menu](https://github.com/Jim1537/PinkieSysMon/blob/main/src/PinkieSysMon/RuntimeTrayMenu.cs), [tray projection](https://github.com/Jim1537/PinkieSysMon/blob/main/src/PinkieSysMon/RuntimeTrayOutputContract.cs), [Runtime IPC](https://github.com/Jim1537/PinkieSysMon/blob/main/src/PinkieSysMon/RuntimeIpc.cs), [Target configuration transaction](https://github.com/Jim1537/PinkieSysMon/blob/main/src/PinkieSysMon/RuntimeOutputConfigurationControl.cs), [Application coordination](https://github.com/Jim1537/PinkieSysMon/blob/main/src/PinkieSysMon/PinkieApplicationContext.cs), [native menu/ownership checks](https://github.com/Jim1537/PinkieSysMon/blob/main/tests/PinkieSysMon.RegressionTests/RuntimeTrayMenuTests.cs).

### Selected-Output saved reload

The existing v1 request `OUTPUT_START_RELOAD`, followed by a tab and `{"TargetId":"desktop-1"}`, now routes through `RuntimeOutputConfigurationControl` rather than a board-only manager operation. Runtime and Editor use this same target request.

| Boundary | Contract |
| --- | --- |
| Saved input | Read and normalize current `app.json`, retaining application structure, duplicate-ID/binding and value validation. Load only the selected target's assigned saved dashboard; a missing sibling dashboard file does not need to be loaded. |
| Scope | Replace only the selected normalized target and its loaded document. Keep current common application/media/provider/telemetry settings, sibling target objects and sibling loaded documents. Per-target values inherited from saved common defaults become that selected target's effective settings without applying the common defaults globally. |
| Ownership | Require an enabled target in a running global output lifetime. Reject changed saved enablement, kind/transport/binding or a removed target; apply such changes through the established configuration/consent path. Reload does not adopt hardware, enable a disabled Output or wake global shutdown. Existing suspension remains in force. |
| Provider policy | Apply that target's saved Provider Enabled along with its other saved settings; it remains independent of output enablement and common telemetry-provider settings. |
| Persistence | Reload is read-only on disk. It retains pending/unknown fields and unrelated saved edits, with byte checks rejecting detected intervening application-file writes. |

The manager prepares the selected session's replacement and old-state recovery plan before commit, and rechecks input after preparation. A failed session or composition-root apply attempts to restore the selected session/configuration/document and shared telemetry coordination; incomplete recovery is reported explicitly. Prepared resources are released. File checks and client status snapshots do not reserve files or device ownership across processes; a detected newer file is retained rather than overwritten.

**Source anchors:** [Saved-target controller](https://github.com/Jim1537/PinkieSysMon/blob/main/src/PinkieSysMon/RuntimeOutputConfigurationControl.cs), [Selected-session transaction](https://github.com/Jim1537/PinkieSysMon/blob/main/src/PinkieSysMon/OutputSessionManager.cs), [saved reload isolation/recovery checks](https://github.com/Jim1537/PinkieSysMon/blob/main/tests/PinkieSysMon.RegressionTests/SavedOutputReloadTests.cs).

### Reload All saved configuration

The tray and Editor **Reload All** command retains the v1 wire verb `OUTPUT_START_RELOAD_ALL`. It invokes `RuntimeConfigurationReloadControl`, the same common-settings transaction used by internal Settings `RELOAD`; the composition root owns the committed configuration. The tray's former separate **Reload Configuration** command is replaced by **Reload All**. There is no board-only All fallback or manual global-start behavior.

| Boundary | Contract |
| --- | --- |
| Saved input | Require existing `config/app.json`; load optional `config/telemetry.json` using its established defaults if absent. Validate application structure, identities, bindings and values for every record, including disabled Outputs. Use the Runtime document-loading policy [above](#runtime-output-ownership). |
| Common scope | Apply application/provider policy, output sessions/documents, telemetry scheduling and actual session demand, media endpoint overrides, Output descriptors and Runtime version publication. With zero saved enabled Outputs, apply common settings and retain an empty session/document set. |
| Lifetime | Saved enablement selects eligible Outputs; Provider Enabled does not select them. User All requires a running global output lifetime. Internal Settings RELOAD can apply configuration while that lifetime is stopped but cannot start sessions. Both retain actual suspension. |
| Persistence | Read-only on disk. Capture application and optional telemetry bytes; reject detected content/presence changes before and immediately after Output commit, retaining newer files and unknown fields. These checks do not reserve files across processes. |
| Recovery | Prepare Output replacement/recovery plans before session commit. A later subsystem failure attempts to restore prior sessions/documents, telemetry policy/demand, media, descriptors/version and composition-root state. Release prepared resources and report incomplete recovery explicitly; native or external failures can prevent complete restoration. |

Editor planning and partial client results belong to the [Editor workflow](#editor-output-reload); Runtime notifications and observed readiness belong to [command feedback](#runtime-command-feedback).

**Source anchors:** [Common reload controller](https://github.com/Jim1537/PinkieSysMon/blob/main/src/PinkieSysMon/RuntimeConfigurationReloadControl.cs), [Enabled-assignment loader](https://github.com/Jim1537/PinkieSysMon/blob/main/src/PinkieSysMon/RuntimeConfigurationValidator.cs), [common reload/recovery checks](https://github.com/Jim1537/PinkieSysMon/blob/main/tests/PinkieSysMon.RegressionTests/SavedConfigurationReloadTests.cs), [Editor planning checks](https://github.com/Jim1537/PinkieSysMon/blob/main/tests/PinkieSysMon.RegressionTests/EditorOutputStartStage25Tests.cs).

### Runtime command feedback

Tray and Editor IPC mutations use one Runtime-owned command-feedback path. IPC responses and local Runtime logs retain the complete target name/ID, assigned dashboard, command outcome and observed session state. Windows slide-in notifications instead summarize the event and outcome, using a readable Output name when available but not identifiers, assignments or diagnostic state dumps. The Windows application header already supplies the application identity; the notification title conveys only severity. Request acceptance alone does not establish a visible panel or physical frame delivery. Requested assignment, enablement, Lock and active-session state are checked after apply. A failed or unverifiable apply reports its cause, current state where available and observed partial changes without assuming complete rollback. IPC timeout/disconnect after a command write attempt reports an unknown outcome and asks the client to query status before retrying.

| Outcome | Feedback |
| --- | --- |
| Applied control with observed ready Output | Information notification; a connected transport does not assert delivered frames. |
| Applied control with pending readiness | Warning and explicit current state. The existing tray status snapshot may later produce one terminal connection/presentation or failure notification. |
| Rejected, unverified or reported failed Output | Error with target and actionable cause; applied settings/sessions may remain. |
| Shutdown request | Acceptance only; unloading completes after the IPC response and existing stopping handshake. |

Pending completion ownership is bounded by configured Outputs. A superseding command, changed target assignment/enablement/session ownership or shutdown removes it. A physical Enabled change supersedes only that Output's prior completion. Each Runtime All command produces aggregate feedback, while target commands do not adopt sibling failures. PING/STATUS, passive discovery, telemetry/frame timers and automatic transport retries never create notification requests. Observation adds no timer or device polling and may miss transient states between existing snapshots. Physical enable feedback follows the existing tray consent path; its internal feedback kind has no IPC verb and grants no new device ownership route.

The established `WindowsAppNotificationService` owns native Windows slide-in delivery, registration fallback and disposal. Delivery availability does not change a command result. Its activation callback only logs; command feedback does not activate an Editor/Runtime window or modify Desktop Lock. Actual shell delivery, focus behavior and native integration require Windows observation.

**Source anchors:** [Command feedback](https://github.com/Jim1537/PinkieSysMon/blob/main/src/PinkieSysMon/RuntimeCommandFeedback.cs), [Windows notification service](https://github.com/Jim1537/PinkieSysMon/blob/main/src/PinkieSysMon/WindowsAppNotificationService.cs), [Runtime coordination](https://github.com/Jim1537/PinkieSysMon/blob/main/src/PinkieSysMon/PinkieApplicationContext.cs).

## Concurrency and transport lifetime

Runtime telemetry acquisition and physical frame delivery use separate workers; [Desktop presentation](../outputs/desktop.md#session-and-native-alpha-presentation) uses the WinForms UI thread. The Editor has its own UI and preview cadence. Published snapshots separate acquisition from rendering, while lifecycle and reconfiguration require stronger synchronization.

!!! warning "USB cancellation still depends on native completion"
    Each `FramePump` serializes transport ownership with its worker. Native WinUSB I/O runs outside the shared session-state lock; lifecycle transitions reject superseded work. The adapter reference owns [ordinary shutdown grace and forced cancellation](devices/trofeo-vision-9.16.md#connection-lifetime-and-evidence-limits). Buffers and handles remain alive until pending native I/O reaches terminal completion. **A stalled driver may not complete promptly after `CancelIoEx`**, so shutdown is not guaranteed to be bounded. See [Issue #37’s closure disposition and untested limits](https://github.com/Jim1537/PinkieSysMon/issues/37#issuecomment-6171393130); further forced-device acceptance was closed as not planned, not newly passed.

