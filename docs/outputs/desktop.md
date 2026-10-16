# Desktop

A **Desktop Output** displays a saved dashboard in an independent, per-pixel-transparent panel hosted by the Windows Explorer wallpaper desktop layer, beneath ordinary application windows. Several panels can run at once, each with its own dashboard, position and settings. No USB display is required. Hosting depends on Explorer's undocumented desktop window layout and may be unavailable on some Windows configurations; an unavailable panel must not appear as an ordinary overlay.

An **Output** is the configured destination; a **dashboard** is the saved Canvas and widgets assigned to it. Creating or removing a panel does not create or delete its dashboard files. For hardware display setup, use [Physical Devices](physical_devices.md). For application-wide coordination, see [Application Architecture](../development/architecture.md#runtime-output-ownership).

## User Guide

### Step 1. Prepare a saved dashboard

Open the Dashboard Editor and open an existing dashboard, such as `Default`, or create and save one. Use the [widget guides](../README.md#available-widgets) to build its content.

Select **Canvas** in the Editor to configure [Width](../widgets/properties.md#width), [Height](../widgets/properties.md#height), [Orientation](../widgets/properties.md#orientation) and [Background Color](../widgets/properties.md#background-color). These properties determine the panel's size and appearance; the panel has no resize border. A 90° or 270° orientation exchanges the displayed width and height.

Transparent Canvas, image and widget pixels remain transparent on the Windows desktop. An opaque Canvas remains opaque. Use the [image-layer properties](../widgets/properties-by-tab.md#image) for artwork and opacity; **Lock** changes mouse behavior, not transparency. See the [native alpha implementation](#session-and-native-alpha-presentation).

### Step 2. Create and configure the panel

1. Choose **Edit → Settings… → Desktop**.
2. Click **Create New**. The new panel is selected in the list on the left; select an existing entry instead to edit it.
3. Set **Display name** and choose a saved **Dashboard**. The display name identifies the panel in menus; it does not rename the dashboard.
4. Configure the selected panel's controls:

| Control | Effect |
| --- | --- |
| **Enabled** | Permits the panel to appear and render its dashboard. |
| **Provider Enabled** | Makes this Output's own status/performance metrics available for use in dashboards. It does not show the panel or enable the telemetry providers its widgets need. See the [Output metric reference](../telemetry/system.md#runtime-and-output-performance-metrics). |
| **Lock (click-through)** | Passes mouse input through the panel to the underlying Explorer desktop surface. Clear it to move the panel with the mouse. |
| **Refresh interval (ms)** | Requested redraw interval, at least 10 ms. A smaller value may increase rendering load; sensor polling follows its [separate schedule](../development/architecture.md#runtime-data-path). |
| **Position X / Position Y** | Panel position in physical desktop pixels, including negative coordinates on monitors left of or above the primary monitor. |

A panel created with **Create New** starts with **Enabled** and **Lock (click-through)** on, **Provider Enabled** off, and a 1000 ms refresh interval. Its initial dashboard is taken from the selected panel, or the first saved dashboard when none is selected. Review that choice before applying.

Hover over a control or its help icon for the current parameter explanation. The list selects which panel you are editing; it does not select a global active dashboard.

### Step 3. Apply the settings and display the panel

Settings controls stage changes until **OK**, which validates and saves the panel configuration. **Cancel** discards the draft, including new panels, removals and reorder operations. The timing note at the bottom of Settings explains when Runtime and Editor changes become effective.

- If Runtime is running and responds to IPC, changed Output settings are applied through a configuration reload.
- If Runtime cannot be reached, settings are saved for its next start; Settings does not launch it. Check the Editor's status message rather than assuming the panel is already visible.
- Use **Output → &lt;panel name&gt; → Reload** in the Editor to load saved edits into an enabled panel. This can launch an absent Runtime normally, starting all saved enabled Outputs. A disabled panel must first be enabled in Settings or the Runtime tray.

The shared [Settings save/apply transaction](../development/architecture.md#settings-save-and-apply) describes concurrent-change rejection and failed-apply recovery. The Editor preview is independent of the live panel.

### Move a panel and set its stacking order

1. Clear **Lock (click-through)** for the panel and apply with **OK**, or clear **&lt;panel name&gt; → Lock** in Runtime's tray menu.
2. Drag a visible part of the panel to its new position. Fully transparent areas may let input through even while unlocked. Deliberate moves save that panel's X/Y automatically after a short debounce; check the local Runtime log if placement fails to persist.
3. Restore **Lock** when you want clicks to reach the applications underneath. You can always adjust **Position X/Y** in Settings instead of dragging.

For overlapping panels, drag their **list entries in Settings → Desktop**; the horizontal insertion line shows the exact destination before releasing the mouse. Click **OK** to commit the new order. Earlier entries are behind later entries. This order controls sibling Desktop windows; dashboard widget layers are separate. Clicking or finishing a drag does not establish a new saved stacking order. Panels are children of the Explorer desktop host, not always-on-top or independent top-level overlays; ordinary application windows remain above them. On the verified Windows 11 raised-desktop layout, panels are siblings of the wallpaper `WorkerW` and placed below `SHELLDLL_DefView` (desktop icons). Classic layouts may instead host panels within a separate wallpaper `WorkerW`; the icon relationship is Explorer-version-dependent.

If a monitor disappears or a panel has too little visible area, Runtime recovers its placement to the primary monitor without resizing the Canvas or overwriting the saved coordinates. Windows display scaling does not multiply the panel's configured physical pixel dimensions.

### Show, hide and reload

Desktop panels do not appear as individual Windows taskbar buttons. Manage them using Runtime's notification-area icon: right-click it and choose the panel name directly below the **Desktop** heading. The heading is not a submenu. Use **Control Center** to open the Editor and its Settings.

| Submenu item, in order | Effect |
| --- | --- |
| **Enabled** | Persistently unload/hide or start/show this panel; preserve its assignment, Lock and provider setting. |
| **Dashboard** | Choose a saved dashboard; the assigned choice is checked. |
| **Reload** | Apply this panel's saved edits through the [selected saved-reload contract](../development/architecture.md#selected-output-saved-reload). |
| **Lock** | Change native mouse pass-through; preserve enablement and global Runtime lifetime. |
| **Provider Enabled** | Read-only publication setting; change it in Settings. |
| **Refresh interval** | Read-only configured interval in ms. |
| **Frame count** | Existing completed panel-frame count. |
| **Frame time** | Existing measured completed-frame duration in ms. |

When Enabled is off, the panel name and Enabled remain reachable; Dashboard, Reload and Lock are unavailable. The shared [tray projection contract](../development/architecture.md#output-status-and-tray-control) owns frame availability and provider-independent observations.

The Editor offers target **Reload** under **Output**, plus [**Reload All**](../development/architecture.md#reload-all-saved-configuration) and the existing global **Stop / Unload All Outputs** shutdown command. The linked common-settings contract owns aggregate reload and its enablement/recovery boundaries.

For saved enablement, disabling the final Output, global shutdown and normal restart, see the [Runtime lifetime contract](../development/architecture.md#control-path-and-reconfiguration). Its [tray-control section](../development/architecture.md#output-status-and-tray-control) defines the storage and status projection.

### Change the dashboard or load saved edits

- **Settings:** select the panel and choose **Dashboard**, then **OK**.
- **Runtime tray:** choose **&lt;panel name&gt; → Dashboard → &lt;saved dashboard&gt;**. Assignment changes are persisted and reload the enabled panel; enable a disabled panel before using this tray action.
- **Editor:** use the selected Output's **Reload**. If its assignment differs from the open Editor document, choose **Use editor dashboard**, **Keep assigned dashboard**, or **Cancel**.

**Save** writes a dashboard to disk. It does not silently replace any Runtime-loaded document. When prompted about unsaved edits, **Save** supplies the new saved content, **Don't Save** uses the last saved content while retaining the editing draft, and **Cancel** aborts the action. See the complete [Editor Reload workflow](../development/architecture.md#editor-output-reload), including all-target partial results.

Two panels can use the same saved dashboard. Reload each panel that should receive changes; a target reload does not replace a sibling's loaded copy. Open **Control Center**, then open the desired saved dashboard in the Editor; tray assignment does not edit its content.

### Remove a panel

In **Edit → Settings… → Desktop**, select the panel, click **Remove**, confirm **Yes**, then **OK**. Cancel leaves the saved configuration unchanged. Removal unloads that panel when applied; dashboard files and other Outputs remain.

At least one **configured Output** must remain, counting Desktop and physical Outputs in the same Settings draft. This guard is independent of how many sessions are currently active. Create/adopt another Output before removing the final configured one.

### Troubleshooting

| Observation | Check or next action |
| --- | --- |
| No panel after OK | Check Runtime status, Enabled in Settings, the target's Reload result and its assigned saved dashboard. If the session reports `HOST_UNAVAILABLE`, inspect the Runtime log for missing WorkerW, DPI-awareness mismatch or an Explorer-hosting failure. No ordinary overlay fallback is allowed. |
| Panel is visible but cannot be dragged | Clear Lock through Settings or the tray; drag a visible part, or set X/Y directly. |
| Empty or partly invisible panel | Inspect Canvas/image/widget alpha and size. Missing telemetry uses each widget's unavailable presentation; it does not necessarily hide the whole panel. |
| Old dashboard content | Save the document, then reload the intended Output. Verify its assignment rather than the currently open Editor tab alone. |
| Missing/corrupt dashboard or **ERROR** | Read the reported target-specific failure and local log; repair the saved assignment/content and retry. Runtime does not silently select another dashboard. |
| Configuration changed while Settings was open | Reopen Settings to load current state. A tray change or saved panel move may have changed the file. |
| Apply failed or recovery unconfirmed | Inspect current Runtime status and the reported disk/recovery result before retrying; rollback is not guaranteed after every external failure. |

## Technical Information

### Configuration and detached editing

The [panel creation procedure](#step-2-create-and-configure-the-panel) edits these records. Desktop records live under `Outputs.Targets` in local `config/app.json`. They use `Kind = desktop`, `Transport = desktop`, and a `Desktop` object:

| Fields | Responsibility |
| --- | --- |
| `Id`, `Name` | Stable Output identity and editable display label. |
| `Dashboard`, `RefreshIntervalMs` | Per-Output saved dashboard reference and redraw interval. Legacy absent values inherit application defaults. |
| `ProviderEnabled` | Independent metric-publication policy; metric IDs and availability are owned by the [metric reference](../telemetry/system.md#runtime-and-output-performance-metrics). |
| `Desktop.BoardEnabled`, `Desktop.Lock` | Presentation enablement and native pointer policy. |
| `Desktop.X`, `Desktop.Y`, `Desktop.ZOrder` | Physical-pixel placement and sibling stacking order. |

`OutputTargetContract` rejects Desktop records with hardware bindings, `DeviceEnabled`, JPEG or USB settings. Fresh application defaults configure a Desktop Output rather than implicitly enabling hardware. The [persistence/schema boundary](../development/architecture.md#persistence-and-schema-boundary) separates application settings from dashboard content and Editor metadata.

`DesktopSettingsModel` owns copied Desktop targets until OK. It preserves physical target objects, assigns a new stable ID on creation, renumbers Z-order on reorder/removal and uses the combined Device/Desktop draft count for the last-Output guard. `EditorSettingsDialog` binds only the selected draft. Validation and commit use the shared [Settings transaction](../development/architecture.md#settings-save-and-apply).

### Session and native alpha presentation

`OutputSessionManager` creates a `DesktopOutputSession` only for an eligible board. Each session owns its renderer and borderless, non-resizable `DesktopOutputWindow`, with WinForms `ShowInTaskbar = false` set before showing the native window. The HWND is reparented *while hidden* to the verified Explorer-owned desktop host (`Progman` sibling on raised Windows 11, wallpaper `WorkerW` child on classic Windows), with native child/popup styles synchronized before showing. Its redraw timer runs on the WinForms UI thread at the Output's requested interval. Hidden, suspended and disposed sessions suppress frame work, including late timer callbacks. Suspend hides an active window; resume restores it.

The session consumes the canonical renderer's [logical BGRA frame](../development/architecture.md#runtime-output-ownership). `DesktopLayeredSurface` copies premultiplied BGRA8888 rows, retaining Canvas `#AARRGGBB` color alpha and image/widget alpha, into a reusable top-down 32-bit GDI DIB. `UpdateLayeredWindow` uses `ULW_ALPHA` and `AC_SRC_ALPHA` for native per-pixel compositing. There is no JPEG conversion or opaque-black flattening in this presentation path; the [Trofeo wire path](../development/devices/trofeo-vision-9.16.md#geometry-and-configuration) remains separate.

The session disposes each caller-owned rendered frame after presentation. The window owns its DIB/DC, refresh/move timers and display-change subscription and releases them on disposal; the session releases its renderer and invalidates its own metrics. Prepared renderer replacements retain ownership until commit, so failed preparation/configuration can be cleaned up or rolled back without adopting a sibling's renderer.

### Explorer desktop hosting and recovery

`DesktopExplorerHost` verifies the wallpaper-side `WorkerW` and Explorer ownership. On a raised Windows 11 desktop with `SHELLDLL_DefView` and wallpaper `WorkerW` as direct `Progman` siblings, it instead parents each **owned panel** directly to `Progman`, placing it immediately below the icon sibling and above the wallpaper sibling. This avoids the nested wallpaper `WorkerW` hosting path that left native-visible panels visually absent in the owner's Windows 11 build 26300; actual visibility of the alternative host still requires real Windows acceptance. The Runtime does not move or re-order shell-owned windows. On classic desktops the panel remains a child of a verified top-level wallpaper `WorkerW`. Missing or unverified shell topology fails closed. Classic Explorer may receive a **single** `0x052C` WorkerW-creation request; there is no repeated forced creation or global window-order enforcement. This is an undocumented Explorer technique, **not a supported stable Windows desktop-layer API**.

Before native `SetParent`, Runtime checks the target HWND's DPI-awareness context against its own; a mismatch is rejected because cross-process `SetParent` can reset the process DPI awareness. Native `WS_CHILD`/`WS_POPUP` transitions are explicit. Persisted coordinates remain physical **screen** pixels; `MapWindowPoints` converts them to parent-client coordinates for native placement. `UpdateLayeredWindow` supplies `NULL` for its destination point when refreshing pixels, so rendering cannot accidentally relocate the Explorer child window; drag persistence reads physical screen coordinates. Per-pixel alpha remains unchanged.

A five-second, UI-thread host-lifetime probe checks only for unavailable/recreated Explorer hosts, **not** the global window Z-order. It attempts to restore hosting and the existing Desktop-only stack after recovery. An unsupported/missing/invalid host is reported as `HOST_UNAVAILABLE` and `IsConnected = false`; Runtime keeps the panel session without showing a top-level overlay. Normal app windows are never explicitly moved. Suspend, idle pause and disable stop this probe. Actual Explorer restart, Win+D, multi-monitor, virtual-desktop and DPI behavior require runtime/visual acceptance on the owner's Windows installation; static inspection or a green build does **not** prove those cases.

### Placement, DPI and mouse routing

`FrameGeometry.GetLogicalSize` maps the oriented Canvas to physical client pixels. `AutoScaleMode.None` and PerMonitorV2 handling preserve that client size across DPI changes. `DesktopPlacement.Recover` tests monitor working-area visibility and recovers unusable positions without modifying the configured Canvas or saved monitor coordinates.

`DesktopPositionStore` patches only the matching Desktop target's X/Y in `app.json`, preserving unrelated and unknown JSON fields. The 500 ms move debounce avoids a save on each native movement event; programmatic recovery and locked/configuration moves do not overwrite saved placement.

`DesktopPointerStyle` preserves layered and unrelated extended styles. A locked layered window adds `WS_EX_TRANSPARENT`, passing even opaque-widget hits to the Explorer desktop beneath it (not to application windows above the wallpaper layer). Unlock removes that flag and maps client hit-testing to native caption dragging. A Lock reload updates the existing window without changing alpha, Canvas dimensions or physical sessions.

### Sibling stacking and shared boundaries

`DesktopZOrderPolicy` orders enabled active panels by ascending `Desktop.ZOrder`, with persisted target order breaking ties. The manager restores sibling order on start, reload, target enable/disable and resume. `WM_EXITSIZEMOVE` restores it immediately after an unlocked native drag. Non-topmost `SetWindowPos` uses no-activation flags and references only owned, Explorer-hosted Desktop sibling windows; no third-party window is explicitly moved.

Shared telemetry demand, per-Output document isolation, IPC transactions and notifications remain owned by [Application Architecture](../development/architecture.md#runtime-data-path). Provider fields, push cadence and passive versus frame measurements remain with the [Output metric reference](../telemetry/system.md#runtime-and-output-performance-metrics).

### Implementation and regression references

| Contract | Sources and existing checks |
| --- | --- |
| Settings and persistence | [DesktopSettingsModel](https://github.com/Jim1537/PinkieSysMon/blob/main/src/PinkieSysMon.Editor/DesktopSettingsModel.cs), [Settings dialog](https://github.com/Jim1537/PinkieSysMon/blob/main/src/PinkieSysMon.Editor/EditorDialogs.cs), [configuration model tests](https://github.com/Jim1537/PinkieSysMon/blob/main/tests/PinkieSysMon.RegressionTests/OutputConfigurationModelTests.cs) |
| Lifecycle and frame suppression | [DesktopOutputSession/window](https://github.com/Jim1537/PinkieSysMon/blob/main/src/PinkieSysMon/DesktopOutputSession.cs), [lifecycle checks](https://github.com/Jim1537/PinkieSysMon/blob/main/tests/PinkieSysMon.RegressionTests/DesktopSessionStage09Tests.cs) |
| Alpha and native resources | [DesktopLayeredSurface](https://github.com/Jim1537/PinkieSysMon/blob/main/src/PinkieSysMon/DesktopLayeredSurface.cs), [alpha checks](https://github.com/Jim1537/PinkieSysMon/blob/main/tests/PinkieSysMon.RegressionTests/DesktopTransparencyStage10Tests.cs) |
| Placement and Lock | [DesktopPositioning](https://github.com/Jim1537/PinkieSysMon/blob/main/src/PinkieSysMon/DesktopPositioning.cs), [position checks](https://github.com/Jim1537/PinkieSysMon/blob/main/tests/PinkieSysMon.RegressionTests/DesktopPositionStage11Tests.cs), [Lock checks](https://github.com/Jim1537/PinkieSysMon/blob/main/tests/PinkieSysMon.RegressionTests/DesktopLockStage12Tests.cs) |
| Stacking and multi-panel isolation | [OutputSessionManager](https://github.com/Jim1537/PinkieSysMon/blob/main/src/PinkieSysMon/OutputSessionManager.cs), [Z-order checks](https://github.com/Jim1537/PinkieSysMon/blob/main/tests/PinkieSysMon.RegressionTests/DesktopZOrderStage14Tests.cs), [multi-session checks](https://github.com/Jim1537/PinkieSysMon/blob/main/tests/PinkieSysMon.RegressionTests/DesktopMultiSessionStage15Tests.cs) |

These are implementation references, not a claim that native Windows behavior has been exercised for every documentation revision. Tested platforms belong to [Compatibility & Testing](../compatibility.md); current checks and acceptance belong to [GitHub Issues](https://github.com/Jim1537/PinkieSysMon/issues).
