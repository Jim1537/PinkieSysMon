# Physical Devices

A **physical Output** assigns a saved dashboard to a supported hardware display. This guide covers discovery, explicit adoption, Settings and daily management. The [Supported Devices inventory](../supported-devices.md) defines which models are implemented; a similar USB connector, resolution or appearance does not establish compatibility.

For Windows panels without display hardware, use [Desktop](desktop.md). A configured Output, the physical device it binds to and the dashboard it displays are separate objects.

## User Guide

### Step 1. Prepare the device and dashboard

1. Check the [supported model](../supported-devices.md) and its linked integration reference. PinkieSysMon currently provides one physical adapter; this common workflow does not promise support for arbitrary USB displays or future adapters.
2. Connect the supported hardware through its required Windows interface. PinkieSysMon's current direct USB output is separate from Windows extended/mirrored monitors.
3. Close vendor display-control software and other applications that could control the same hardware before enabling PinkieSysMon output.
4. In the Dashboard Editor, open or create and **save** the desired dashboard. Configure its Canvas and widgets using the [widget/property guides](../README.md#available-widgets) and the device's [geometry requirements](../development/devices/trofeo-vision-9.16.md#geometry-and-configuration).

Adopting an Output creates an application configuration record. It does not create hardware, author a dashboard or install a generic display driver.

### Step 2. Find the Device tab

Open **Edit → Settings…**. After **General** and **Desktop**, **Device #1**, **Device #2**, and subsequent tabs represent saved physical Outputs and newly observed hardware candidates. The tab number is a presentation position, not a permanent identity. Inspect its display name, **Type** and status.

| Settings state | Meaning and available actions |
| --- | --- |
| **Connected**, configured | A saved Output resolves to one present device. Its settings are editable. This is passive presence, not proof of a Runtime connection or delivered frames. |
| **Connected**, **Not configured** | A newly detected disabled candidate. Edit it, then explicitly adopt it with **Add Device**, or through confirmed Device Enabled consent. |
| **Disconnected** | Saved values remain visible, but settings and Add are disabled. Remove remains available subject to the last-Output guard. A candidate already seen in this open dialog also retains its draft during absence. |
| **Ambiguous identity — controls unavailable** | Device identity is not safe to select. Settings/Add are disabled; do not assume that one matching device was chosen. |
| **Discovery unavailable — controls unavailable** | Passive discovery failed. Settings/Add are disabled rather than retaining stale editable Connected controls. Reopen Settings or reconnect to retry discovery. |

Hardware that has never been detected and has no saved record creates no tab. Settings refreshes presence on opening and Windows device-change notifications; disconnect/reconnect retains existing draft values. Presence alone never saves a device or opens its transport. See the [inventory mechanism](#passive-inventory-and-explicit-adoption).

### Step 3. Configure and adopt the Output

1. Select the intended **Device #N** tab with an unambiguous **Connected** state.
2. Set **Display name** and select the saved **Dashboard**. Renaming the Output does not rename the dashboard or change its hardware binding.
3. Set **Refresh interval (ms)** for this Output. This is the requested frame interval, not a guaranteed FPS or a sensor-polling interval.
4. Choose **Provider Enabled** if dashboards should use this Output's status/performance metrics. Its [metric contract](../telemetry/system.md#runtime-and-output-performance-metrics) is independent of hardware enablement and the global System provider.
5. For a **Not configured** candidate, click **Add Device** to include it in the Settings draft **without enabling hardware**. Changing fields alone does not adopt an unsaved candidate.

Model-dependent controls belong to the selected adapter. The current adapter exposes **JPEG quality**, **USB retry (ms)**, **USB timeout (ms)** and **USB diagnostic journal**. Their [defaults, bounds, inheritance and transport effects](../development/devices/trofeo-vision-9.16.md#geometry-and-configuration) remain in the integration reference. Retry delay and per-operation timeout are different settings. Journaling adds diagnostic disk I/O; it does not repair a USB fault.

Hover over a control or its help icon for its effect and dependencies. These device settings belong to the selected Device tab, rather than General or Desktop.

### Step 4. Grant device-use consent and apply

To permit hardware output, turn on **Device Enabled**. Read the **Enable Device** Yes/No warning and confirm **Yes** only after closing competing display-control software. **No** is the default and leaves enablement unchanged. Confirmed enabling of a new candidate also adopts it, so a separate Add Device click is not required in that route.

Click **OK** to validate/save the draft and apply it through the shared [Settings save/apply transaction](../development/architecture.md#settings-save-and-apply). A newly enabled device is rechecked after consent and immediately before saving. If it has disappeared or become ambiguous, enabling fails instead of selecting replacement hardware.

**Cancel** discards the draft, including adoption, device enablement and removals. Granting consent inside an uncommitted Settings draft does not itself open the device. Settings reloads a responsive running Runtime; it does not launch an absent or unreachable one. Check whether the Editor reports **saved and applied** or **saved for next start**.

| Switch | What it permits |
| --- | --- |
| **Device Enabled** | Runtime hardware use for this configured Output. Off prevents its USB session from starting. |
| **Provider Enabled** | This Output's metric publication. On does not authorize USB use; off does not stop an otherwise enabled device. |

### Start, reload and choose the dashboard

- In the Editor, choose **Output → &lt;Output name&gt; → Start / Reload**. It can launch an absent Runtime, but cannot enable a disabled physical Output.
- In Runtime's tray menu, use **Outputs → &lt;Output name&gt; → Start / Reload**. The submenu reports its assignment, current state, Device Enabled and Provider Enabled.
- Change assignment in **Settings → Device #N → Dashboard → OK**, or through **Outputs → &lt;Output&gt; → Dashboard: &lt;current name&gt;** in the tray. A tray assignment reloads an active session and preserves an inactive session's stopped state.

Runtime uses saved dashboard content. Saving an Editor document alone does not reload it. The Editor's [Start/Reload workflow](../development/architecture.md#editor-output-start-reload) handles **Save / Don't Save / Cancel** for unsaved edits and, when assignments differ, **Use editor dashboard / Keep assigned dashboard / Cancel**. Each Output owns its loaded copy, even when several Outputs select the same saved dashboard.

**Start / Reload All** addresses eligible Outputs without granting new Device Enabled consent. Its per-target assignment choices and possible partial results are documented in Architecture. A successful command with **WAITING** or **SUSPENDED** is not a claim that frames reached the display; consult [Runtime command feedback](../development/architecture.md#runtime-command-feedback).

### Stop, disable, disconnect or remove

| Operation | Effect |
| --- | --- |
| **Stop / Unload Output** under Editor **Output** or tray **Outputs** | Stop that live session; retain its assignment, saved record and Device Enabled value. An enabled Output can restart on a later normal Runtime launch. |
| Turn **Device Enabled** off | Persistently prevent hardware output. Use an editable connected Device tab and OK, or the tray's **Physical Devices** entry. |
| Physically disconnect | Leave the saved Output configured. An enabled session can wait/retry for its configured binding; disconnecting does not revoke Device Enabled or remove its record. |
| **Remove** and confirm | Delete the selected saved Output record (or dismiss an unsaved candidate), stop its applied session and retain dashboard files and other Outputs. In Settings, removal takes effect after **OK**. |

The Runtime tray's **Physical Devices** menu offers enable/disable for an explicitly configured device, with fresh presence checks and the same consent warning before enabling. An enabled disconnected device can still be **disabled from the tray**, although disconnected Settings fields are read-only. Newly discovered unconfigured devices cannot be enabled from the tray; adopt them in Settings.

For removal, either use **Settings → Device #N → Remove → Yes → OK**, or **Physical Devices → Remove &lt;name&gt;…** in the tray and confirm. At least one configured Desktop or physical Output must remain. The Settings guard counts the combined configured draft, including adopted additions/removals on other tabs; an unsaved unadopted candidate does not count.

An attached removed device is not silently restored. It is suppressed for the rest of that Settings draft and can reappear as **Not configured** on reopening; the tray can show it as unconfigured. Explicit adoption is required again. A newly adopted record receives a new Output identity, so old Output-metric widget references are not silently reassigned.

!!! info "Final active Output"
    Explicitly stopping the final active Output, or using **Stop / Unload All Outputs**, requests Runtime shutdown. Saved Device Enabled consent remains separate from session lifetime. See the [shared output-control contract](../development/architecture.md#output-status-and-tray-control).

### Troubleshooting and recovery

| Observation | Check or next action |
| --- | --- |
| No Device tab | Verify model/interface support and physical connection. Never-detected absent hardware has no placeholder tab. |
| Connected in Settings, but no display output | Check Device Enabled, Runtime's target state and Start/Reload result, then the saved assignment. Passive detection does not acquire the transport. |
| **WAITING**, **ERROR**, or a USB-open failure | Close competing display software, check the connection and local reported error. Automatic retry does not establish that recovery succeeded. |
| Ambiguous identity or a changed interface path | Resolve the actual binding using the [adapter's selection/identity rules](../development/devices/trofeo-vision-9.16.md#hardware-identification-and-output-selection). No first-match fallback or automatic rebinding is performed. |
| Disconnected controls cannot be edited | Reconnect that device to edit; use the tray to disable an already-enabled absent device, or Remove if appropriate. |
| Missing/corrupt dashboard | Correct the saved document or assignment and retry. Runtime reports the affected Output rather than substituting another dashboard. |
| Stale Settings draft or apply/recovery failure | Follow the [shared transaction's recovery result](../development/architecture.md#settings-save-and-apply); reopen current settings and inspect Runtime status before retrying. |

The current adapter's [native completion limits](../development/devices/trofeo-vision-9.16.md#connection-lifetime-and-evidence-limits) still apply to a stalled USB operation. Documentation, a passive presence match or a green software build does not prove physical recovery. Tested configurations belong to [Compatibility & Testing](../compatibility.md).

## Technical Information

### Common contract and adapter boundary

| Common physical-Output responsibilities | Adapter-specific responsibilities |
| --- | --- |
| Named saved Output, dashboard assignment and independent consent/provider policy | Supported model, native geometry, transport key and configuration options |
| Passive inventory and explicit candidate adoption | Windows interface discovery and hardware identity derivation |
| Detached editing, validation, removal and session coordination | Open/handshake, encoding, framing, ACK and native I/O lifetime |

`DeviceSettingsModel` and `PhysicalDeviceInventory` currently operate on `Kind = trofeo`; the common workflow is implemented through this adapter, not an open-ended plug-in compatibility promise. Exact interface IDs, path-derived binding/fingerprint rules and legacy selection fallback remain with [Trofeo Device Integration](../development/devices/trofeo-vision-9.16.md#hardware-identification-and-output-selection).

### Configuration and identity

Physical Outputs use local `Outputs.Targets` records. Their `Id` identifies the application Output; `Name` is its label; `DeviceId` identifies the adapter's hardware binding. Neither the display name nor Settings tab number is a substitute for either identity. A hardware path/fingerprint observation is not the Output metric identity.

`Dashboard`, `RefreshIntervalMs` and `ProviderEnabled` belong to each Output. Physical records additionally own explicit `DeviceEnabled`, `Transport`, `DeviceId` and the current adapter's `JpegQuality`/`Usb` overrides; they must not contain Desktop configuration. `OutputTargetContract` rejects invalid or duplicate identities/bindings. The adapter owns its [normalization, default/inherited values and bounds](../development/devices/trofeo-vision-9.16.md#geometry-and-configuration).

Provider descriptors and unavailable values are defined by the [Output metric owner](../telemetry/system.md#runtime-and-output-performance-metrics). The shared [application/dashboard persistence boundary](../development/architecture.md#persistence-and-schema-boundary) prevents Output edits from becoming dashboard-schema changes.

### Passive inventory and explicit adoption

`DeviceDiscovery` uses Windows Configuration Manager/PnP metadata without opening a WinUSB transport. `PhysicalDeviceInventory.Build` retains configured devices as Connected/Disconnected/Ambiguous and identifies unconfigured candidates. Duplicate configured identities, duplicate discovered bindings and duplicated verified fingerprints fail closed. A changed path is not rebound merely because an optional fingerprint matches.

Runtime captures inventory at startup and successful configuration reload and refreshes it when the tray Physical Devices menu is opened. It retains the snapshot in memory, with no periodic scan or live Runtime hotplug subscription. Private Runtime logging contains aggregate status/fingerprint counts rather than raw serials or per-device paths; an unexpected discovery failure does not stop active sessions.

The open Settings window refreshes passive discovery on `WM_DEVICECHANGE / DBT_DEVNODES_CHANGED`, without a timer or transport acquisition. `DeviceSettingsModel` keeps separate configured targets, unsaved candidates, removed bindings and consented enable transitions. Discovery alone cannot make its draft dirty, adopt hardware or persist a target. Add explicitly adopts a disabled candidate; a confirmed enabled transition also adopts it. A removed binding is suppressed within the dialog, while reopening can offer it as a new unsaved candidate.

### Draft validation and application

The [Device tab states](#step-2-find-the-device-tab) and [consent procedure](#step-4-grant-device-use-consent-and-apply) expose this validation to users. `DeviceSettingsPage` disables every setting when the passive entry is not Connected, while keeping allowed removal independent. Draft values survive status refresh. Before a new enabled transition is committed, `CanEnable` requires an explicit exact binding resolving to one current device; `ValidateForApply` refreshes discovery and rechecks consent. The check does not reserve a device against another process acquiring it later.

The two detached Settings models form one transaction: physical edits preserve Desktop targets, Desktop edits preserve physical targets, and the final combined configured set cannot be empty. The shared [save/apply transaction](../development/architecture.md#settings-save-and-apply) owns byte checks, persistence, IPC reload and rollback limits. The tray uses `PhysicalDeviceRemovalPolicy` and the existing configuration-reload lifecycle for its saved physical changes; failed apply restores prior configuration and attempts live recovery rather than claiming an infallible rollback.

### Session lifecycle and evidence limits

`OutputSessionManager` owns the active-session set and isolates each Output's loaded dashboard and renderer. Device Enabled is required before a physical session can start. Current physical sessions use a dedicated `FramePump` worker; that worker owns the device transport. A target Stop/unavailable state does not authorize another device, replace a sibling's document or clear a sibling's metrics.

Transport retry, stale-generation rejection, cancellation, buffer lifetime and diagnostics are specified by the [adapter lifecycle contract](../development/devices/trofeo-vision-9.16.md#connection-lifetime-and-evidence-limits). Shared start/reload/stop, status, IPC and notification behavior belongs to [Architecture](../development/architecture.md#output-status-and-tray-control). A configured, passively present, active or transport-connected device represents a different observation at each boundary; none alone proves a displayed frame.

### Implementation and regression references

| Contract | Sources and existing checks |
| --- | --- |
| Discovery and inventory | [DeviceDiscovery](https://github.com/Jim1537/PinkieSysMon/blob/main/src/PinkieSysMon/DeviceDiscovery.cs), [PhysicalDeviceInventory/removal policy](https://github.com/Jim1537/PinkieSysMon/blob/main/src/PinkieSysMon/PhysicalDeviceInventory.cs) |
| Settings and consent | [DeviceSettingsModel](https://github.com/Jim1537/PinkieSysMon/blob/main/src/PinkieSysMon.Editor/DeviceSettingsModel.cs), [Device Settings UI](https://github.com/Jim1537/PinkieSysMon/blob/main/src/PinkieSysMon.Editor/EditorSettingsDialog.Devices.cs), [discovery/adoption/consent/recovery checks](https://github.com/Jim1537/PinkieSysMon/blob/main/tests/PinkieSysMon.RegressionTests/DeviceSettingsStage23Tests.cs) |
| Configuration and assignment | [AppConfig/OutputTargetContract](https://github.com/Jim1537/PinkieSysMon/blob/main/src/PinkieSysMon/AppConfig.cs), [configuration tests](https://github.com/Jim1537/PinkieSysMon/blob/main/tests/PinkieSysMon.RegressionTests/OutputConfigurationModelTests.cs), [assignment isolation tests](https://github.com/Jim1537/PinkieSysMon/blob/main/tests/PinkieSysMon.RegressionTests/OutputDashboardOwnershipTests.cs) |
| Live session/control | [OutputSessionManager](https://github.com/Jim1537/PinkieSysMon/blob/main/src/PinkieSysMon/OutputSessionManager.cs), [Runtime tray/application](https://github.com/Jim1537/PinkieSysMon/blob/main/src/PinkieSysMon/PinkieApplicationContext.cs), [target-control checks](https://github.com/Jim1537/PinkieSysMon/blob/main/tests/PinkieSysMon.RegressionTests/RuntimeConfigurationStage24Tests.cs), [Editor workflow checks](https://github.com/Jim1537/PinkieSysMon/blob/main/tests/PinkieSysMon.RegressionTests/EditorOutputStartStage25Tests.cs) |

These source/regression contracts do not establish compatibility with additional hardware or a new real-device PASS. Current investigations and acceptance remain in [GitHub Issues](https://github.com/Jim1537/PinkieSysMon/issues).
