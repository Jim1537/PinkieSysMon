# Physical Devices

A **physical Output** assigns a saved dashboard to a supported hardware display.

A saved physical Output uses the user's name, exact USB interface binding, selected implemented protocol and explicit screen width/height. The first working board is `device_<width>x<height>_default`, at 0° orientation, generated through the shared adaptive dashboard factory. An existing valid board is reused without overwriting edits. Ordinary frames use the assigned dashboard's Canvas; screen dimensions are not a model-size whitelist or an automatic fitting instruction.

Candidate **Test** uses Runtime and asks for separate visual confirmation after clean USB release. **Save Configuration** commits a confirmed candidate as a disabled Output with an editable working board; **Forget** remains unavailable pending its exclusion transaction. Configured-device edits and Remove still use the dialog's OK/Cancel draft path while per-card integration continues in [Issue #95](https://github.com/Jim1537/PinkieSysMon/issues/95). Old model-specific application/device records are deliberately unsupported; dashboard documents and their schema are unchanged.

For Windows panels without display hardware, use [Desktop](desktop.md). A configured Output, the physical device it binds to and the dashboard it displays are separate objects.

## User Guide

### Step 1. Prepare the device and dashboard

1. Check the [observed hardware inventory](../supported-devices.md) and protocol references. Candidate admission requires an exact VID/PID in the maintained USB catalog plus eligible Windows metadata; inclusion does not prove firmware compatibility.
2. Connect the supported hardware through its required Windows interface. PinkieSysMon's current direct USB output is separate from Windows extended/mirrored monitors.
3. Close vendor display-control software and other applications that could control the same hardware before enabling PinkieSysMon output.
4. In the Dashboard Editor, open or create and **save** the desired dashboard. Configure its Canvas and widgets using the [widget/property guides](../README.md#available-widgets) for the dimensions you intend to display.

Adopting an Output creates an application configuration record and its initial editable working board when needed. It does not create hardware or install a display driver.

### Step 2. Find the Device tab

Open **Devices**. Configured tabs use the saved user name. Unsaved candidates and informational entries retain **Device #N** presentation labels; the number is not a permanent identity. Inspect its display name, **Protocol**, user dimensions and status.

| Settings state | Meaning and available actions |
| --- | --- |
| **Connected**, configured | A saved Output resolves to one present device. Its settings are editable. This is passive presence, not proof of a Runtime connection or delivered frames. |
| **Connected**, **Not configured** | An unsaved candidate with editable name, dimensions and protocol. Valid fields enable Test; a clean Test and visual Yes enable Save Configuration. Forget remains unavailable. |
| **Disconnected** | Saved values remain visible, but settings and Test are disabled. Remove remains available subject to the last-Output guard. A candidate already seen in this open dialog also retains its draft during absence. |
| **Ambiguous identity — controls unavailable** | Device identity is not safe to select. Settings/Save are disabled; do not assume that one matching device was chosen. |
| **Discovery unavailable — controls unavailable** | Passive discovery failed. Settings/Save are disabled rather than retaining stale editable Connected controls. Reopen Settings or reconnect to retry discovery. |
| **No matching eligible WinUSB interface was confirmed** | Windows recognizes a catalog product, but an exact eligible-interface correlation is unavailable or ambiguous. The informational entry cannot be adopted or enabled. |

Unrelated USB products are excluded. Hardware that has never been detected and has no saved record creates no tab. Settings refreshes presence on opening and Windows device-change notifications; disconnect/reconnect retains existing draft values. Presence alone never saves a device or opens its transport. See the [inventory mechanism](#passive-inventory-and-explicit-adoption).

### Step 3. Configure and adopt the Output

For a candidate, enter **Display name**, **Width**, **Height** and **Protocol**. A unique passive catalog hint can suggest a protocol; an unhinted interface requires an explicit choice. The radio group keeps both implemented choices available for an eligible catalog interface. A documented family protocol without an implementation is shown disabled with **Not implemented yet**; it cannot authorize a Test. Neither hint nor name proves compatibility. No device dimensions are inferred. Press **Test** and confirm the hardware-use prompt. Watch the current generated dashboard and its changing seconds. The card locks inputs and repeated Test while the operation is active; **Cancel** requests cleanup. Closing the window waits for the client operation to finish before discarding the draft.

After clean technical completion and release, answer whether the current image was readable at the expected geometry **and seconds changed**. The question refers to the observation during Test; transmission has already stopped. **Yes** confirms only that exact attempt. **No**, cancellation, an error or an uncertain response leaves it unconfirmed. Name-only changes preserve local approval; changing dimensions or protocol requires another Test. A visible image cannot override a technical failure.

The Test board stays in memory. Test and Yes do not save a dashboard, configuration, assignment or enablement. **Save Configuration** rechecks the exact interface and physical node, merges this one confirmed card with the current saved settings, then creates or reuses the separate `device_<width>x<height>_default` working board. It does not overwrite an existing board, enable the Output, or start an absent Runtime. A responsive running Runtime receives a reload; failed application restores the prior configuration and releases a newly generated unchanged board when safe. Cancel after a completed Save does not undo it. Runtime ownership and Test cleanup are defined by [USB Test coordination](../development/architecture.md#transient-usb-test-coordination).

For a configured, connected Output, edit its name, saved dashboard assignment, refresh interval, Provider Enabled and supported encoding/USB options. Protocol, dimensions and binding are read-only; changing them requires Remove and fresh onboarding when available. A name-only edit does not require another Test. Hover over a control or its help icon for its effect and dependencies. Protocol-specific capabilities remain in the [LY](../development/devices/trofeo-vision-9.16.md#geometry-and-configuration) and [encrypted JPEG](../development/devices/turzx-12.3.md#application-configuration) references.

### Step 4. Grant device-use consent and apply

To permit hardware output for an already configured Output, turn on **Enabled**. Read the **Enable Device** Yes/No warning and confirm **Yes** only after closing competing display-control software. **No** is the default and leaves enablement unchanged. An unconfigured candidate cannot be enabled or adopted through this switch.

For existing configured-device edits, click **OK** to validate/save the draft and apply it through the shared [Settings save/apply transaction](../development/architecture.md#settings-save-and-apply). Candidate Save Configuration commits immediately and independently of OK. A newly enabled configured device is rechecked after consent and immediately before saving. If it has disappeared or become ambiguous, enabling fails instead of selecting replacement hardware.

**Cancel** discards uncommitted drafts, including configured-device edits, enablement and removals; it cannot reverse a completed candidate Save Configuration. Granting consent inside an uncommitted Settings draft does not itself open the device. Settings reloads a responsive running Runtime; it does not launch an absent or unreachable one. Check whether the Editor reports **saved and applied** or **saved for next start**.

| Switch | What it permits |
| --- | --- |
| **Enabled** | Runtime hardware use for this configured Output. Off prevents its USB session from starting. |
| **Provider Enabled** | This Output's metric publication. On does not authorize USB use; off does not stop an otherwise enabled device. |

### Reload and choose the dashboard

- In the Editor, choose **Outputs (toolbar icon) → &lt;Output name&gt; → Reload**. It can launch an absent Runtime normally, starting all saved enabled Outputs, but cannot enable a disabled physical Output.
- In Runtime's tray menu, choose the configured device name directly below the **Physical Devices** heading, then **Reload**. The heading is not a submenu. **Control Center** opens the Editor and its Settings.
- Change assignment in **Devices → &lt;device name&gt; → Dashboard → OK**, or through **&lt;device name&gt; → Dashboard** in the tray. A tray assignment reloads an active enabled session and respects global Runtime lifetime; the tray action is unavailable for a disabled Output.

Runtime uses saved dashboard content. Saving an Editor document alone does not reload it. The Editor's [Reload workflow](../development/architecture.md#editor-output-reload) handles **Save / Don't Save / Cancel** for unsaved edits and, when assignments differ, **Use editor dashboard / Keep assigned dashboard / Cancel**. Each Output owns its loaded copy, even when several Outputs select the same saved dashboard.

The selected command's saved-input, inheritance, ownership and recovery boundaries are defined by the [selected saved-reload contract](../development/architecture.md#selected-output-saved-reload).

The device submenu orders **Enabled**, **Dashboard**, **Reload**, then read-only **Connected**, **Provider Enabled**, **Refresh interval**, **Frame count**, **Frame time**, **JPEG Size** and **JPEG Quality**. Connected describes Runtime transport, not passive Settings presence. Shared statistics/availability and root navigation are defined by [Architecture](../development/architecture.md#output-status-and-tray-control).

[**Reload All**](../development/architecture.md#reload-all-saved-configuration) uses the shared common-settings contract, including Editor choices and possible partial results. A successful command with **WAITING** or **SUSPENDED** is not a claim that frames reached the display; consult [Runtime command feedback](../development/architecture.md#runtime-command-feedback).

### Disable, disconnect or remove

| Operation | Effect |
| --- | --- |
| Turn **Enabled** off in Settings | Persistently prevent hardware output through an editable connected Device tab and OK. The configured device's tray **Enabled** switch also offers disabling. |
| Physically disconnect | Leave the saved Output configured. An enabled session follows its adapter's binding/recovery policy; disconnecting does not revoke saved enablement or remove its record. |
| **Remove** and confirm | Delete the selected saved Output record, stop its applied session and retain dashboard files and other Outputs. In Settings, removal takes effect after **OK**. |

The configured device's tray **Enabled** switch offers enable/disable, with fresh presence checks and the existing consent warning before enabling. An enabled disconnected device can still be **disabled from the tray**, although disconnected Settings fields are read-only. Newly discovered unconfigured devices cannot be enabled from the tray; adopt them in Settings. Concurrent/stale-input failures follow the [tray transaction](#tray-enabled-transaction).

For removal, use **Control Center → Devices → &lt;device name&gt; → Remove → Yes → OK**. At least one configured Desktop or physical Output must remain. The Settings guard counts the physical draft and the saved Desktop Outputs; adopted additions/removals on other device tabs count, while an unadopted candidate does not.

An attached removed device is not silently restored. It is suppressed for the rest of that Settings draft and can reappear as **Not configured** on reopening. The tray lists only configured Outputs. Explicit adoption is required again. A newly adopted record receives a new Output identity, so old Output-metric widget references are not silently reassigned.

For saved enablement, disabling the final Output, global shutdown and normal restart, see the [Runtime lifetime contract](../development/architecture.md#control-path-and-reconfiguration). Its [tray-control section](../development/architecture.md#output-status-and-tray-control) defines the storage and status projection.

### Troubleshooting and recovery

| Observation | Check or next action |
| --- | --- |
| No Device tab | Verify model/interface support and physical connection. Never-detected absent hardware has no placeholder tab. |
| No eligible interface, or a display outside the recognition catalog | Follow the owning integration Issue shown on its tab, or [request device support](https://github.com/Jim1537/PinkieSysMon/issues). Unlisted VID/PID pairs require a deliberate catalog update; there is no arbitrary WinUSB selector. |
| Connected in Settings, but no display output | Check Enabled in Settings, Runtime's target state and Reload result, then the saved assignment. Passive detection does not acquire the transport. |
| **WAITING**, **ERROR**, or a USB-open failure | Close competing display software, check the connection and local reported error. Follow the selected adapter's recovery policy; a new attempt does not establish that recovery succeeded. |
| Ambiguous identity or a changed interface path | Resolve the actual binding using the selected adapter's identity rules linked from [Supported Devices](../supported-devices.md). No first-match fallback or automatic rebinding is performed. |
| Disconnected controls cannot be edited | Reconnect that device to edit; use the tray to disable an already-enabled absent device, or Remove if appropriate. |
| **Test not passed** / cleanup unconfirmed | Read the card status and Runtime log. Cancel is a request, not proof of released USB. Do not start a competing controller while cleanup is unconfirmed. No image/Yes override is offered after a technical failure. |
| Missing/corrupt dashboard | Correct the saved document or assignment and retry. Runtime reports the affected Output rather than substituting another dashboard. |
| Stale Settings draft or apply/recovery failure | Follow the [shared transaction's recovery result](../development/architecture.md#settings-save-and-apply); reopen current settings and inspect Runtime status before retrying. |

For a support request, include the commercial model, screen dimensions, USB VID:PID, whether Windows sees it, its Windows device name/driver status or interface class when available, a vendor software/protocol link, and expected versus observed behavior. Do not publish serial numbers, Windows instance/interface paths, private logs, dashboards or secrets. No firmware probe or diagnostic export is required.

The selected adapter's native completion limits still apply to a stalled USB operation: [Trofeo](../development/devices/trofeo-vision-9.16.md#connection-lifetime-and-evidence-limits) or [TURZX](../development/devices/turzx-12.3.md#cancellation-and-native-ownership). Documentation, a passive presence match or a green software build does not prove physical recovery. Tested configurations belong to [Compatibility & Testing](../compatibility.md).

## Technical Information

### Common contract and adapter boundary

| Common physical-Output responsibilities | Adapter-specific responsibilities |
| --- | --- |
| Named saved Output, dashboard assignment and independent consent/provider policy | Protocol key, wire rotation, endpoints, payload bounds and protocol options |
| Passive inventory and explicit candidate adoption | Native endpoint/interface checks before protocol commands |
| Detached editing, validation, removal and session coordination | Open/handshake, encoding, framing, ACK and native I/O lifetime |

`DeviceSettingsModel` and `PhysicalDeviceInventory` classify all physical records with `OutputTargetContract.PhysicalKind`. The shared session factory creates `FramePump` for either implemented protocol; the transport factory selects its existing wire implementation by `Transport`. Commercial model names and catalog hints never select a saved session. Each worker retains independent renderer, transport, cancellation and metrics ownership. A missing exact binding remains disconnected even when a single device is present.

### Frame geometry

Ordinary frames use the assigned dashboard's Canvas Width, Height and Orientation. Saved screen dimensions describe onboarding geometry and initial board generation; they do not resize an assigned dashboard. The physical frame profile contains only protocol rotation and encoded-payload capacity. There is no panel rectangle, fitting, stretching, cropping or letterboxing step. Canvas image/widget presentation rules still apply inside the dashboard itself.

The shared [renderer resource budget](../development/architecture.md#runtime-output-ownership) and the [LY wire field bound](../development/devices/trofeo-vision-9.16.md#jpeg-frame-wire-format) / [encrypted-image bound](../development/devices/turzx-12.3.md#framing-and-commands) remain enforced. Rejection does not silently lower quality or change geometry. An oversized replacement is rejected during preparation before replacing the active renderer. A dimension mismatch is not itself an error, but firmware clipping, orientation and visible results require observation on the selected device.

### Configuration and identity

Physical Outputs use local `Outputs.Targets` records:

| Field | Contract |
| --- | --- |
| `Kind` | `physical`; Desktop remains `desktop`. Old model kinds and protocol aliases are not read or migrated. |
| `Transport` | One selected protocol: `ly-bulk` or `encrypted-jpeg`. No model/size dispatch key. |
| `Id` / `Name` | Stable logical Output ID / editable user label. Neither is hardware identity. |
| `DeviceId` | `usb-interface:` plus SHA-256 of the trimmed, uppercased exact interface path. No raw path is persisted and no serial/port-change rebinding is guessed. |
| `Width` / `Height` | Explicit required user geometry, validated through the shared adaptive generator's geometry limits; used for first-board assignment. |
| `Dashboard` | Independent ordinary saved document reference. Missing/corrupt explicit assignments are not replaced. |
| `DeviceEnabled` / `ProviderEnabled` | Independent hardware-use and metric-publication switches. Confirmed candidate conversion starts both false. |
| `RefreshIntervalMs`, `JpegQuality`, `Usb` | Per-Output cadence, encoding and USB options; absent overrides use application defaults. |
| `EncryptedJpeg` | Options owned only by the [encrypted JPEG protocol](../development/devices/turzx-12.3.md#application-configuration). |

Duplicate interface bindings are rejected even when the Outputs choose different protocols. Connection equality includes protocol, exact binding and both dimensions, so selected reload cannot silently apply changed wire settings. Editor copies and JSON transactions retain these fields independently. Desktop records reject physical bindings, dimensions and USB/JPEG options.

Provider descriptors and unavailable values are defined by the [Output metric owner](../telemetry/system.md#runtime-and-output-performance-metrics). The shared [application/dashboard persistence boundary](../development/architecture.md#persistence-and-schema-boundary) prevents Output edits from becoming dashboard-schema changes.

### Connection and synchronization failure diagnostics

Every Runtime physical-transport open runs inside `PhysicalHandshakeDiagnostics` in the shared factory, including Trofeo and TURZX. A failed discovery, native open/initialization or synchronization writes a `PHYSICAL_OPEN_FAILURE` record to the ordinary private `logs/PinkieSysMon.log`. No extra option or detailed USB journal is required. Successful opens, missing bindings and requested lifecycle cancellation produce no such failure record.

| Recorded evidence | Limits and meaning |
| --- | --- |
| Attempt context | Runtime version, unique attempt ID, logical Output ID/name, kind/transport, configured transfer timeout, elapsed time and last adapter stage. The timeout is per transfer, not a total-open deadline. |
| Existing handshake operations | Direction, endpoint, requested and actual completed byte counts. A failed/pending operation has an unknown count (`null`); an actual completed empty read has count `0`. Trofeo write completion is recorded after its existing full-write check; a failed short write retains its count in the original error text. |
| Actual response bytes | Hexadecimal for at most 512 returned bytes per operation and eight operations per attempt; truncation/omissions are explicit. Invalid counts and failed reads supply no bytes. Primary and follow-up reads remain separate, so stale buffer contents cannot become evidence of a new response. |
| Error chain | Exception types, HResults and available native Win32 codes, including nested errors; bounded exception messages remain private. The original error continues through the existing worker path even if diagnostic logging fails. |

The recorder observes existing handshake calls only: it adds no USB commands, reads, retries, resets or changes to reply validation, cancellation or recovery. It retains no image payloads and is not used for frame exchanges. Each failed open attempt gets its own record under the application's existing log rotation (5 MiB per file, five archives); successful-attempt data is discarded. New physical adapters must describe their own handshake stages and completed I/O through this shared context rather than adding a model-specific failure logger.

If an intermittent failure recurs during normal use, preserve the main log and its rotated archives before further diagnostic actions. Raw responses and exception messages can contain device or machine identifiers: share these logs privately, never paste them into public Issues. A captured failure can identify the failing boundary; it does not by itself establish firmware state, root cause or successful recovery. Adapter-specific frame/lifecycle journals remain separate.

### Passive inventory and explicit adoption

`PhysicalDeviceDiscovery` projects the catalog-scoped `CatalogUsbInterfaceDiscovery` snapshot. Only exact listed VID/PID, healthy WinUSB devnodes, registered interface classes and independent exact interface/devnode correlation produce candidates. Composite functions retain their physical parent's identity. Duplicate paths, node claims and available serial-backed fingerprints fail closed; a fingerprint never replaces the persisted interface binding. Names come from passive PnP metadata or a neutral Device #N suggestion.

Incomplete metadata is distinct from absence. Inventory retains proven healthy entries while unavailable unresolved entries lose editable status. Active acquisition revalidates the selected binding against a fresh catalog snapshot. Recovery cannot infer unplugging from an incomplete snapshot. Composite function failures discard provisional candidates for the entire correlated physical parent, irrespective of enumeration order; parent presence and the child-to-parent relationship are rechecked. A failed initial function read with no trustworthy parent relation temporarily makes same-product candidate ownership unavailable, while unrelated products remain discoverable. This is a conservative snapshot result, not a persistent exclusion or a guessed relation. Conflicting claims to the same interface path are also rejected. Discovery opens no WinUSB transport and issues no protocol commands.

Runtime captures inventory at startup and successful configuration reload and refreshes it when the Runtime tray root menu is opened. Tray enablement has a [separate validation sequence](#tray-enabled-transaction). This menu inventory retains its snapshot in memory, with no periodic refresh or live hotplug subscription. An active worker may separately observe passive metadata under its adapter's [recovery policy](#session-lifecycle-and-evidence-limits); that observation does not refresh the menu inventory. Private Runtime logging contains aggregate status/fingerprint counts rather than raw serials or per-device paths; an unexpected discovery failure does not stop active sessions.

The open Settings window refreshes passive discovery on `WM_DEVICECHANGE / DBT_DEVNODES_CHANGED`, without a timer or transport acquisition. `DeviceSettingsModel` keeps separate configured targets, unsaved candidates, removed bindings and consented enable transitions. Discovery alone cannot make its draft dirty, adopt hardware or persist a target. Candidate conversion requires the exact latest clean released Test plus positive visual confirmation; consent alone cannot adopt it. A removed binding is suppressed within the dialog, while reopening can offer it as a new unsaved candidate.

### Passive USB family recognition

The Devices dialog uses `UsbDisplayDiscovery.Capture()` to recognize researched display families independently of their installed WinUSB, HID or mass-storage driver. [UsbDisplayCatalog.cs](https://github.com/Jim1537/PinkieSysMon/blob/main/src/PinkieSysMon/UsbDisplayCatalog.cs) is the single implementation list of exact product pairs, generic family names, candidate handlers, integration Issue references and pinned research evidence. Recognition does not establish an exact model, firmware, native geometry or protocol compatibility; the [supported-model inventory](../supported-devices.md) remains separate.

The [discovery implementation](https://github.com/Jim1537/PinkieSysMon/blob/main/src/PinkieSysMon/UsbDisplayDiscovery.cs) reads present USB devnodes through [cached Windows PnP metadata](https://learn.microsoft.com/en-us/windows/win32/api/cfgmgr32/nf-cfgmgr32-cm_get_device_id_listw). Only matching products receive per-node driver, setup-class, problem-code and function-parent lookups. It opens no device, requests no USB descriptors, installs no driver and sends no commands. There is no timer or background service.

| Observation boundary | Behavior |
| --- | --- |
| Errors and bounds | List size and termination are checked; buffer-growth races retry at most three size/list pairs. A CM list/lookup failure is an unavailable scan, not a successful empty result. A node removed during lookup is omitted; missing driver/class/parent metadata remains an explicit gap. |
| Grouping | Case aliases are deduplicated. Function devnodes join an observed matching non-function parent only through the actual PnP relation. Equal product pairs or similar instance tokens do not merge separate units. Conflicting copies of one devnode are ambiguous. |
| Identity and privacy | A namespaced SHA-256 observation key correlates one devnode/group within snapshots. It is not a hardware serial, persisted Output binding or automatic rebind mechanism. Observations expose no raw instance IDs or interface paths. |
| Eligibility | Recognition results have no openable path and create no Output configuration, provider or session. A catalog candidate handler, driver/setup class or zero problem code does not authorize transport dispatch. Only the catalog-scoped interface discovery described above can supply a usable descriptor. |

`UsbDisplayReconciliation` suppresses an informational duplicate only when exactly one independently discovered supported interface maps to an observed devnode/member and the identities are unambiguous. Equal USB products or serial text cannot establish this match. `RecognizedDeviceSettingsModel` retains informational tabs and their Disconnected/Discovery unavailable states only while the dialog is open; it has no configuration or consent API. The two discovery paths fail independently, so a family-scan failure cannot disable otherwise valid supported-interface settings.

Settings, consent, Runtime inventory and transport dispatch use the common discovery path described [above](#passive-inventory-and-explicit-adoption). A recognition-only entry explains that no eligible interface was confirmed, regardless of the catalog's protocol hint; a proven matching interface receives the normal candidate tab. Informational entries explain missing WinUSB eligibility, unavailable metadata or a Windows device problem; no driver replacement is performed. Known unimplemented protocol labels come from the same catalog metadata used by candidate controls. Recognition supplies no geometry, encoding, brightness or USB defaults. Deterministic tests exercise metadata fakes, reconciliation and detached drafts; native Windows discovery, visual behavior and physical compatibility require separate observations.

### Draft validation and application

The [Device tab states](#step-2-find-the-device-tab) and [consent procedure](#step-4-grant-device-use-consent-and-apply) expose this validation to users. `DeviceSettingsPage` disables fields when the passive entry is not Connected or a Test is in progress. Configured removal remains independent of passive presence. Draft values survive status refresh. Before a new enabled transition is committed, `CanEnable` requires an explicit exact binding resolving to one current device; `ValidateForApply` refreshes discovery and rechecks consent. The check does not reserve a device against another process acquiring it later.

Desktop and Physical Devices now have independent dialogs and detached drafts. Each preserves the other kind's saved Outputs and rejects a final combined configured set that would be empty. The shared [save/apply transaction](../development/architecture.md#settings-save-and-apply) owns byte checks, persistence, IPC reload and rollback limits. The tray has no removal command; its Enabled control uses the selected physical transaction below.

### Tray Enabled transaction

`RuntimeOutputConfigurationControl.PreparePhysicalEnablement` captures the current Runtime/selected-target snapshot and application-file bytes before requesting native Yes/No consent. The confirmation receives a detached target, defaults to No and returns no apply callback when declined.

| Boundary | Physical tray behavior |
| --- | --- |
| Enable eligibility | Require a configured, explicit exact binding resolving to one current device. Refresh passive eligibility before consent, after consent and after candidate preparation before Start; absent, duplicate or ambiguous identity fails closed. |
| Selected input | Enable loads only the current Runtime assignment's saved document. Other pending saved values follow the [selected reload](../development/architecture.md#selected-output-saved-reload) or [common reload](../development/architecture.md#reload-all-saved-configuration) contracts. |
| Disable | Skip discovery, consent, dashboard loading and replacement-session creation; use the selected session's existing [lifecycle](#session-lifecycle-and-evidence-limits). |
| Saved field | Select the physical target's root `DeviceEnabled` member; persistence, sibling isolation and recovery follow the [shared field transaction](../development/architecture.md#output-status-and-tray-control). |

The prepared callback rejects a changed Runtime configuration or selected record during the modal dialog. The shared transaction owns detected file-change rejection and disk/live rollback reporting; global lifetime, suspension and shutdown remain with [Architecture](../development/architecture.md#control-path-and-reconfiguration). Physical selection and native completion retain their [existing evidence limits](#session-lifecycle-and-evidence-limits).

### Session lifecycle and evidence limits

`OutputSessionManager` owns the active-session set and isolates each Output's loaded dashboard and renderer. Saved Enabled and a running global Runtime lifetime are required before a physical session can start. Disabling the Output disposes its session without a separate manual stop flag. Current physical sessions use a dedicated `FramePump` worker; that worker owns the device transport. A disabled/unavailable state does not authorize another device, replace a sibling's document or clear a sibling's metrics.

Transport retry/recovery, cancellation, buffer lifetime and diagnostics are specified by the [Trofeo lifecycle](../development/devices/trofeo-vision-9.16.md#connection-lifetime-and-evidence-limits) and [TURZX lifecycle](../development/devices/turzx-12.3.md#runtime-sessions-and-recovery). Shared enablement, reload, global shutdown, status, IPC and notification behavior belongs to [Architecture](../development/architecture.md#output-status-and-tray-control). A configured, passively present, active or transport-connected device represents a different observation at each boundary; none alone proves a displayed frame.

### Implementation and regression references

| Contract | Sources and existing checks |
| --- | --- |
| Discovery and inventory | [PhysicalDeviceDiscovery](https://github.com/Jim1537/PinkieSysMon/blob/main/src/PinkieSysMon/PhysicalDeviceDiscovery.cs), [PhysicalDeviceInventory](https://github.com/Jim1537/PinkieSysMon/blob/main/src/PinkieSysMon/PhysicalDeviceInventory.cs) |
| Settings and consent | [DeviceSettingsModel](https://github.com/Jim1537/PinkieSysMon/blob/main/src/PinkieSysMon.Editor/DeviceSettingsModel.cs), [Device Settings UI](https://github.com/Jim1537/PinkieSysMon/blob/main/src/PinkieSysMon.Editor/PhysicalDeviceSettingsDialog.cs), [discovery/adoption/consent/recovery checks](https://github.com/Jim1537/PinkieSysMon/blob/main/tests/PinkieSysMon.RegressionTests/DeviceSettingsStage23Tests.cs) |
| Configuration and assignment | [AppConfig/OutputTargetContract](https://github.com/Jim1537/PinkieSysMon/blob/main/src/PinkieSysMon/AppConfig.cs), [configuration tests](https://github.com/Jim1537/PinkieSysMon/blob/main/tests/PinkieSysMon.RegressionTests/OutputConfigurationModelTests.cs), [assignment isolation tests](https://github.com/Jim1537/PinkieSysMon/blob/main/tests/PinkieSysMon.RegressionTests/OutputDashboardOwnershipTests.cs) |
| Live session/control | [OutputSessionManager](https://github.com/Jim1537/PinkieSysMon/blob/main/src/PinkieSysMon/OutputSessionManager.cs), [Runtime tray/application](https://github.com/Jim1537/PinkieSysMon/blob/main/src/PinkieSysMon/PinkieApplicationContext.cs), [target-control checks](https://github.com/Jim1537/PinkieSysMon/blob/main/tests/PinkieSysMon.RegressionTests/RuntimeConfigurationStage24Tests.cs), [Editor workflow checks](https://github.com/Jim1537/PinkieSysMon/blob/main/tests/PinkieSysMon.RegressionTests/EditorOutputStartStage25Tests.cs) |
| Tray activation and projection | [Selected-target controller](https://github.com/Jim1537/PinkieSysMon/blob/main/src/PinkieSysMon/RuntimeOutputConfigurationControl.cs), [physical consent/isolation/recovery checks](https://github.com/Jim1537/PinkieSysMon/blob/main/tests/PinkieSysMon.RegressionTests/PhysicalOutputEnablementTests.cs), [native menu checks](https://github.com/Jim1537/PinkieSysMon/blob/main/tests/PinkieSysMon.RegressionTests/RuntimeTrayMenuTests.cs) |

These source/regression contracts do not establish compatibility with additional hardware or a new real-device PASS. Current investigations and acceptance remain in [GitHub Issues](https://github.com/Jim1537/PinkieSysMon/issues).
