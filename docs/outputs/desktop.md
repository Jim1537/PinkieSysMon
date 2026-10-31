# Desktop

A **Desktop Output** displays a saved dashboard in an independent, per-pixel-transparent top-level Windows panel. Several panels can run at once, each with its own dashboard, position and settings. No USB display is required. Panels are initially positioned near the back of the normal window stack, above the desktop wallpaper; this is **best effort**, not a permanent Windows Z-order guarantee. A later application activation, Windows shell transition or virtual-desktop change may interleave another window and a Desktop panel.

An **Output** is the configured destination; a **dashboard** is the saved Canvas and widgets assigned to it. Confirming a *new* Desktop panel can generate its initial dashboard once; removing a panel never deletes its dashboard files. For hardware display setup, use [Physical Devices](physical_devices.md). For application-wide coordination, see [Application Architecture](../development/architecture.md#runtime-output-ownership).

## User Guide

### Step 1. Prepare a saved dashboard

Open the Dashboard Editor and open any saved dashboard, or create and save one. No stock dashboard is bundled with a clean installation. Use the [widget guides](../README.md#available-widgets) to build its content.

Select **Canvas** in the Editor to configure [Width](../widgets/properties.md#width), [Height](../widgets/properties.md#height), [Orientation](../widgets/properties.md#orientation) and [Background Color](../widgets/properties.md#background-color). These properties determine the panel's size and appearance; the panel has no resize border. A 90° or 270° orientation exchanges the displayed width and height.

Transparent Canvas, image and widget pixels remain transparent on the Windows desktop. An opaque Canvas remains opaque. Use the [image-layer properties](../widgets/properties-by-tab.md#image) for artwork and opacity; **Lock** changes mouse behavior, not transparency. See the [native alpha implementation](#session-and-native-alpha-presentation).

### Step 2. Create and configure the panel

1. Choose **Desktop**.
2. Click **Create New**. The new panel is selected in the list on the left; select an existing entry instead to edit it.
3. Set **Display name** and choose a saved **Dashboard**. The display name identifies the panel in menus; it does not rename the dashboard.
4. Configure the selected panel's controls:

| Control | Effect |
| --- | --- |
| **Enabled** | Permits the panel to appear and render its dashboard. |
| **Provider Enabled** | Makes this Output's own status/performance metrics available for use in dashboards. It does not show the panel or enable the telemetry providers its widgets need. See the [Output metric reference](../telemetry/system.md#runtime-and-output-performance-metrics). |
| **Lock (click-through)** | Passes input through except at [Shortcut](../widgets/shortcut.md) graphic rectangles. Clear it to move the panel with the mouse. |
| **Refresh interval (ms)** | Requested redraw interval, at least 10 ms. A smaller value may increase rendering load; sensor polling follows its [separate schedule](../development/architecture.md#runtime-data-path). |
| **Position X / Position Y** | Panel position in physical desktop pixels, including negative coordinates on monitors left of or above the primary monitor. |

A clean installation also contains an initially **disabled** Desktop placeholder; it does not render without a saved board. You can enable that initial placeholder in **Desktop → OK** to generate its first saved board, or use **Create New** for an additional panel. Both first-use paths create the board only after OK; no installed or previously edited board is silently regenerated. A panel created with **Create New** starts with **Enabled** and **Lock (click-through)** on, **Provider Enabled** off, and a 1000 ms refresh interval. Its initial assignment is `desktop_default`: an ordinary editable **640×640**, **0°** dashboard generated only after **OK**. An existing valid `desktop_default` is reused without overwriting edits. You may choose another saved dashboard before OK; that explicit choice wins. **Cancel** creates neither the panel nor the default file.

Hover over a control or its help icon for the current parameter explanation. The list selects which panel you are editing; it does not select a global active dashboard.

### Step 3. Apply the settings and display the panel

Settings controls stage changes until **OK**, which validates and saves the panel configuration. **Cancel** discards the draft, including new panels, removals and reorder operations. The timing note at the bottom of Settings explains when Runtime and Editor changes become effective.

- If Runtime is running and responds to IPC, changed Output settings are applied through a configuration reload.
- If Runtime cannot be reached, settings are saved for its next start; Settings does not launch it. Check the Editor's status message rather than assuming the panel is already visible.
- Use **Outputs (toolbar icon) → &lt;panel name&gt; → Reload** in the Editor to load saved edits into an enabled panel. This can launch an absent Runtime normally, starting all saved enabled Outputs. A disabled panel must first be enabled in Settings or the Runtime tray.

The shared [Settings save/apply transaction](../development/architecture.md#settings-save-and-apply) describes concurrent-change rejection and failed-apply recovery. The Editor preview is independent of the live panel.

### Move a panel and set its stacking order

1. Clear **Lock (click-through)** for the panel and apply with **OK**, or clear **&lt;panel name&gt; → Lock** in Runtime's tray menu.
2. Drag a visible part of the panel to its new position. Fully transparent areas may let input through even while unlocked. Deliberate moves save that panel's X/Y automatically after a short debounce; check the local Runtime log if placement fails to persist.
3. Restore **Lock** when you want ordinary dashboard areas to pass clicks through. Shortcut graphic rectangles remain interactive. You can always adjust **Position X/Y** in Settings instead of dragging.

For overlapping panels, drag their **list entries in Desktop**; the horizontal insertion line shows the exact destination before releasing the mouse. Click **OK** to commit the new order. Earlier entries are behind later entries. This order controls stacking among the **owned** Desktop panels; dashboard widget layers are separate. Clicking or finishing a drag does not establish a new saved stacking order. Panels are ordinary non-topmost top-level windows, not Explorer children. Runtime puts them near the back when they appear, reload, resume, or complete an unlocked drag, but it cannot guarantee they will always remain behind every other application's window. Desktop icons are not guaranteed to stay above the panels.

If a monitor disappears or a panel has too little visible area across the available monitors, Runtime recovers its placement to the primary monitor without resizing the Canvas or overwriting the saved coordinates. A panel split across adjacent monitors is not treated as lost merely because neither monitor individually exposes the minimum usable area. Display/DPI-triggered recovery waits for an active native drag to finish instead of repositioning the window under the pointer. Windows display scaling does not multiply the panel's configured physical pixel dimensions.

### Show, hide and reload

Desktop panels are configured not to appear as individual Windows taskbar buttons or Alt+Tab entries. Manage them using Runtime's notification-area icon: right-click it and choose the panel name directly below the **Desktop** heading. The heading is not a submenu. Use **Control Center** to open the Editor and its Settings.

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

The Editor offers target **Reload** under **Outputs (toolbar icon)**, plus [**Reload All**](../development/architecture.md#reload-all-saved-configuration) and the existing global **Stop / Unload All Outputs** shutdown command. The linked common-settings contract owns aggregate reload and its enablement/recovery boundaries.

For saved enablement, disabling the final Output, global shutdown and normal restart, see the [Runtime lifetime contract](../development/architecture.md#control-path-and-reconfiguration). Its [tray-control section](../development/architecture.md#output-status-and-tray-control) defines the storage and status projection.

### Change the dashboard or load saved edits

- **Settings:** select the panel and choose **Dashboard**, then **OK**.
- **Runtime tray:** choose **&lt;panel name&gt; → Dashboard → &lt;saved dashboard&gt;**. Assignment changes are persisted and reload the enabled panel; enable a disabled panel before using this tray action.
- **Editor:** use the selected Output's **Reload**. If its assignment differs from the open Editor document, choose **Use editor dashboard**, **Keep assigned dashboard**, or **Cancel**.

**Save** writes a dashboard to disk. It does not silently replace any Runtime-loaded document. When prompted about unsaved edits, **Save** supplies the new saved content, **Don't Save** uses the last saved content while retaining the editing draft, and **Cancel** aborts the action. See the complete [Editor Reload workflow](../development/architecture.md#editor-output-reload), including all-target partial results.

Two panels can use the same saved dashboard. Reload each panel that should receive changes; a target reload does not replace a sibling's loaded copy. Open **Control Center**, then open the desired saved dashboard in the Editor; tray assignment does not edit its content.

### Remove a panel

In **Desktop**, select the panel, click **Remove**, confirm **Yes**, then **OK**. Cancel leaves the saved configuration unchanged. Removal unloads that panel when applied; dashboard files and other Outputs remain.

At least one **configured Output** must remain, counting the Desktop draft plus the saved physical Outputs. This guard is independent of how many sessions are currently active. Create/adopt another Output before removing the final configured one.

### Troubleshooting

| Observation | Check or next action |
| --- | --- |
| No panel after OK | Check Runtime status, Enabled in Settings, the target's Reload result and its assigned saved dashboard. Look for `WINDOW_UNAVAILABLE` or `ERROR` and inspect the Runtime log for native-window creation or rendering errors. Also check for off-screen placement or overlapping windows; the bottom-stack placement is best effort. |
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
| `Dashboard`, `RefreshIntervalMs` | Per-Output saved dashboard reference and redraw interval. A missing dashboard assignment uses the Desktop profile name (`desktop_default`); refresh interval inherits the shared application setting. |
| `ProviderEnabled` | Independent metric-publication policy; metric IDs and availability are owned by the [metric reference](../telemetry/system.md#runtime-and-output-performance-metrics). |
| `Desktop.BoardEnabled`, `Desktop.Lock` | Presentation enablement and native pointer policy. |
| `Desktop.X`, `Desktop.Y`, `Desktop.ZOrder` | Physical-pixel placement and sibling stacking order. |

`OutputTargetContract` rejects Desktop records with hardware bindings, `DeviceEnabled`, JPEG or USB settings. Fresh application defaults configure a disabled Desktop placeholder, with no physical Output enabled and no prepackaged dashboard files. The [persistence/schema boundary](../development/architecture.md#persistence-and-schema-boundary) separates application settings from dashboard content and Editor metadata.

`DesktopSettingsModel` owns copied Desktop targets until OK. It preserves physical target objects, assigns a new stable ID on creation, renumbers Z-order on reorder/removal and counts saved physical Outputs for the last-Output guard. `DesktopSettingsDialog` binds only its detached Desktop draft. Validation and commit use the shared [Settings transaction](../development/architecture.md#settings-save-and-apply).

### Session and native alpha presentation

`OutputSessionManager` creates a `DesktopOutputSession` only for an eligible board. Each session owns its renderer and borderless, non-resizable `DesktopOutputWindow`. It remains an ordinary non-topmost **top-level** window; it is never parented to Explorer. WinForms `ShowInTaskbar = false` suppresses a separate taskbar button, while native `WS_EX_TOOLWINDOW` (without `WS_EX_APPWINDOW`) excludes the panel from Alt+Tab. Its redraw timer runs on the WinForms UI thread at the Output's requested interval. Hidden, suspended and disposed sessions suppress frame work, including late timer callbacks. Suspend hides an active window; resume restores it.

The session consumes the canonical renderer's [logical BGRA frame](../development/architecture.md#runtime-output-ownership). `DesktopLayeredSurface` copies premultiplied BGRA8888 rows, retaining Canvas `#AARRGGBB` color alpha and image/widget alpha, into a reusable top-down 32-bit GDI DIB. `UpdateLayeredWindow` uses `ULW_ALPHA` and `AC_SRC_ALPHA` for native per-pixel compositing. There is no JPEG conversion or opaque-black flattening in this presentation path; the [Trofeo wire path](../development/devices/trofeo-vision-9.16.md#geometry-and-configuration) remains separate.

The session disposes each caller-owned rendered frame after presentation. The window owns its DIB/DC, refresh/move timers and display-change subscription and releases them on disposal; the session releases its renderer and invalidates its own metrics. Prepared renderer replacements retain ownership until commit, so failed preparation/configuration can be cleaned up or rolled back without adopting a sibling's renderer.

### Bottom-of-stack placement and limitations

`DesktopBottomPlacement` inserts only a panel's own top-level HWND into the normal desktop Z-order with `SetWindowPos` and `SWP_NOACTIVATE`. Literal `HWND_BOTTOM` is the fallback when no `Progman` window can be found. When `Progman` exists, blindly using `HWND_BOTTOM` could bury the panel **under the wallpaper**; instead the runtime locates the top-level window immediately above `Progman` and inserts the panel just below that predecessor, leaving it immediately above the shell's wallpaper. If `Progman` is already first in the normal Z-order, it uses `HWND_TOP` (not `HWND_TOPMOST`) for the owned panel. The shell-owned windows are **never reparented or moved**.

This is a **best-effort initial/back-placement policy**, not a guarantee against future global window reordering. No global enforcement timer is installed; output stacking is reconciled on session start, reload, resume and completed native drag. A native out-of-context WinEvent listener also observes foreground changes and window/client reorder events (including the `OBJID_CLIENT` notification observed **after** `Win+D` moves the desktop to the foreground); after such an event it checks whether any **visible owned** Desktop panel is below `Progman`. Only then does Runtime reinsert its panels above the desktop in saved sibling order, without activating them or moving Explorer/other application windows. The listener is unhooked on Runtime exit. This is an event-driven *best-effort* repair, not a permanent guarantee: Windows may omit an event or rearrange windows after it. `Win+D`, virtual desktops and shell transitions need real-device validation; neither static inspection nor a successful regression build proves recovery.

The change replaces Explorer child-window hosting following unsuccessful visible-output checks on the owner's Windows 11 build 26300 (Issue #78). `SetParent`, DPI-awareness comparisons against Explorer, `WS_CHILD` conversions, wallpaper `WorkerW` selection and shell-host probes are **not used** by this presentation path. `UpdateLayeredWindow` supplies `NULL` for its destination point while changing pixels, leaving top-level window placement to native `SetWindowPos` and WinForms. The remaining native placement is in physical desktop pixels; multi-monitor coordinates may be negative.

### Placement, DPI and mouse routing

`FrameGeometry.GetLogicalSize` maps the oriented Canvas to physical client pixels. `AutoScaleMode.None` and PerMonitorV2 handling preserve that client size across DPI changes. `DesktopPlacement.Recover` tests the combined usable visibility of monitor working areas (without counting gaps as visible) and recovers unusable positions without modifying the configured Canvas or saved monitor coordinates. During native dragging, display/DPI-triggered recovery is deferred until the drag ends.

`DesktopPositionStore` patches only the matching Desktop target's X/Y in `app.json`, preserving unrelated and unknown JSON fields. The 500 ms move debounce avoids a save on each native movement event; programmatic recovery and locked/configuration moves do not overwrite saved placement.

`DesktopPointerStyle` preserves layered and unrelated extended styles. A locked layered visual window adds `WS_EX_TRANSPARENT`; Unlock removes it and maps client hit-testing to native caption dragging. A Lock reload updates the existing window without changing alpha, Canvas dimensions or physical sessions.

An Output containing Shortcuts also owns one non-layered `NOREDIRECTIONBITMAP | NOACTIVATE | TOOLWINDOW` input companion immediately above its visual HWND. Its native region is the union of rotated **graphic rectangles**, clipped to the logical Canvas. Transparent or opacity-zero graphic pixels remain interactive; label, border, rounded corners, shadow and other widgets do not enlarge input regions. Native scanlines and dispatch use the same pixel-center rule, with right/bottom edges exclusive. Overlapping Shortcuts dispatch by descending widget Z, then descending original list index. The region has a separate 4 MiB RECT budget (262,144 scanline rectangles); exceeding it fails configuration rather than installing unrestricted input.

The companion follows the visual's location and normal panel stacking. No extra DPI multiplier or physical wire rotation applies. In unlocked mode a completed click can activate a Shortcut; crossing the Windows drag threshold hands a caption drag to this panel's visual HWND. Hiding, reconfiguration, capture cancellation and teardown cancel pending gestures; queued Shell requests are generation-checked. Existing renderer retention also retains the matching interaction snapshot for rollback. No global input hook, cross-process event replay, alpha fill or `HTTRANSPARENT` fallback is used. Native lifecycle and touchscreen acceptance remain tracked in [Issue #101](https://github.com/Jim1537/PinkieSysMon/issues/101).

### Sibling stacking and shared boundaries

`DesktopZOrderPolicy` orders enabled active panels by ascending `Desktop.ZOrder`, with persisted target order breaking ties. The manager places the owned windows **back-to-front**, inserting each just ahead of the desktop shell; this preserves the saved relative order at reconciliation time. It runs on creation, configuration reload, target enable/disable, resume and `WM_EXITSIZEMOVE` after unlocked drag. It does not continuously correct global Z-order, and no third-party HWND is modified.

Shared telemetry demand, per-Output document isolation, IPC transactions and notifications remain owned by [Application Architecture](../development/architecture.md#runtime-data-path). Provider fields, push cadence and passive versus frame measurements remain with the [Output metric reference](../telemetry/system.md#runtime-and-output-performance-metrics).

### Implementation and regression references

| Contract | Sources and existing checks |
| --- | --- |
| Settings and persistence | [DesktopSettingsModel](https://github.com/Jim1537/PinkieSysMon/blob/main/src/PinkieSysMon.Editor/DesktopSettingsModel.cs), [Settings dialog](https://github.com/Jim1537/PinkieSysMon/blob/main/src/PinkieSysMon.Editor/DesktopSettingsDialog.cs), [configuration model tests](https://github.com/Jim1537/PinkieSysMon/blob/main/tests/PinkieSysMon.RegressionTests/OutputConfigurationModelTests.cs) |
| Lifecycle and frame suppression | [DesktopOutputSession/window](https://github.com/Jim1537/PinkieSysMon/blob/main/src/PinkieSysMon/DesktopOutputSession.cs), [lifecycle checks](https://github.com/Jim1537/PinkieSysMon/blob/main/tests/PinkieSysMon.RegressionTests/Outputs/Desktop/DesktopSessionStage09Tests.cs) |
| Alpha and native resources | [DesktopLayeredSurface](https://github.com/Jim1537/PinkieSysMon/blob/main/src/PinkieSysMon/DesktopLayeredSurface.cs), [alpha checks](https://github.com/Jim1537/PinkieSysMon/blob/main/tests/PinkieSysMon.RegressionTests/Outputs/Desktop/DesktopTransparencyStage10Tests.cs) |
| Placement and Lock | [DesktopPositioning](https://github.com/Jim1537/PinkieSysMon/blob/main/src/PinkieSysMon/DesktopPositioning.cs), [position checks](https://github.com/Jim1537/PinkieSysMon/blob/main/tests/PinkieSysMon.RegressionTests/Outputs/Desktop/DesktopPositionStage11Tests.cs), [Lock checks](https://github.com/Jim1537/PinkieSysMon/blob/main/tests/PinkieSysMon.RegressionTests/Outputs/Desktop/DesktopLockStage12Tests.cs) |
| Stacking and multi-panel isolation | [DesktopBottomPlacement](https://github.com/Jim1537/PinkieSysMon/blob/main/src/PinkieSysMon/DesktopBottomPlacement.cs), [OutputSessionManager](https://github.com/Jim1537/PinkieSysMon/blob/main/src/PinkieSysMon/OutputSessionManager.cs), [Z-order checks](https://github.com/Jim1537/PinkieSysMon/blob/main/tests/PinkieSysMon.RegressionTests/Outputs/Desktop/DesktopZOrderStage14Tests.cs), [multi-session checks](https://github.com/Jim1537/PinkieSysMon/blob/main/tests/PinkieSysMon.RegressionTests/Outputs/Desktop/DesktopMultiSessionStage15Tests.cs) |

These are implementation references, not a claim that native Windows behavior has been exercised for every documentation revision. Tested platforms belong to [Compatibility & Testing](../compatibility.md); current checks and acceptance belong to [GitHub Issues](https://github.com/Jim1537/PinkieSysMon/issues).
