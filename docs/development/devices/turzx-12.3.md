# TURZX 12.3-inch — Integration Contract

This reference owns the encrypted JPEG wire protocol and its historically tested TURZX hardware. Common discovery, configuration and onboarding belong to [Physical Devices](../../outputs/physical_devices.md). The shared renderer/`FramePump` path uses `TurzxFrameTransport`. Exact passive interface discovery supplies a normal Device candidate; explicit adoption/consent permits a saved Runtime session. Closed [Issue #71](https://github.com/Jim1537/PinkieSysMon/issues/71) records the earlier implementation and physical acceptance; [Issue #95](https://github.com/Jim1537/PinkieSysMon/issues/95) tracks current generic onboarding acceptance. The [supported-device inventory](../../supported-devices.md) lists observed hardware.

## Passive interface discovery

`TurzxDeviceDiscovery` remains a product-specific diagnostic helper for USB product `1CBE:0123`. Ordinary Settings and Runtime candidate inventory use the common catalog-scoped discovery, not a union of product-specific scanners. This helper uses Windows metadata only and requires:

- A present, non-composite-function USB devnode with complete metadata, the `WinUSB` service and no reported device problem. Unevidenced composite function layouts are excluded.
- Application interface GUIDs read from that devnode's existing hardware registry key through `CM_Open_DevNode_Key`, with query-only access and `OpenExisting`. Both documented `DeviceInterfaceGUIDs` (multi-string) and `DeviceInterfaceGUID` (string) registrations are supported; neither is created or changed.
- Present interfaces enumerated with each registered class GUID **and that exact devnode**. The path must match the exact product and registered class, and `DEVPKEY_Device_InstanceId` must independently confirm the same devnode. Presence/driver status is checked again before returning a descriptor.

List/property sizes and buffer-growth retries are bounded. Missing registration or denied registry access supplies no usable interface; malformed data and scan failures are explicit errors. No Trofeo GUID, inferred path, hardware serial or first-match fallback is substituted. Discovery opens no device handle, initializes no WinUSB transport, sends no commands and installs no driver. Actual native discovery requires separate Windows verification.

Case aliases of one interface are deduplicated. Different interfaces on one devnode retain their separate bindings and shared hashed node key; the common ambiguity guard rejects selecting one arbitrarily. Equal product IDs do not merge different units.

Microsoft documents [hardware-key access](https://learn.microsoft.com/en-us/windows-hardware/drivers/install/opening-a-device-s-hardware-key), [WinUSB interface registration](https://learn.microsoft.com/en-us/windows-hardware/drivers/usbcon/winusb-installation) and [devnode-scoped interface enumeration](https://learn.microsoft.com/en-us/windows/win32/api/cfgmgr32/nf-cfgmgr32-cm_get_device_interface_listw). These API contracts do not prove a particular device's installed registration or firmware behavior.

### Manual native discovery checkpoint

After a current [Windows build and regression run](../building.md#build-and-regression), invoke the already-built regression runner from the repository root:

```powershell
dotnet .\artifacts\tests\PinkieSysMon.RegressionTests.dll --turzx-passive-check |
    Tee-Object .\artifacts\turzx-passive-check.log
```

This explicit mode runs the actual passive discovery and exact binding selection once, using existing Windows metadata. It prints the Runtime assembly version and aggregate devnode/class/interface counts, without raw paths, instance IDs, binding hashes or exception messages. It opens no USB transport, sends no command and reads/writes no application configuration. Exit 0 requires exactly one usable, correlated interface; exit 1 reports an incomplete/failed scan; exit 2 rejects unsupported arguments or a non-Windows host. Missing registration, removal and multiple interfaces do not select a fallback device.

Without arguments, the runner executes deterministic regressions and does not invoke this native checkpoint. `PASSIVE DISCOVERY: PASS` establishes only the observed metadata/binding result; the transport, image, brightness and Runtime gates remain separate. Retain both `builder.log` and this diagnostic log before another run overwrites either. The [Issue stage reports](https://github.com/Jim1537/PinkieSysMon/issues/71) own exact revisions and observed results.

### Manual transport checkpoint

After a current Windows build/regression, an explicit active mode can test the actual C# transport independently of Runtime adoption. Follow the owning Issue's current single checkpoint and keep other TURZX control applications closed. The historical PNG reference invocation is:

```powershell
dotnet .\artifacts\tests\PinkieSysMon.RegressionTests.dll --turzx-transport-check png --consent YES |
    Tee-Object .\artifacts\turzx-transport-png.log
```

This replaces the selected TURZX's picture with a synthetic pattern. Hardware use requires the explicit `--consent YES` argument; absent consent or any other value declines before discovery or USB open. Argument consent also works when output is piped to the log, without depending on interactive stdin. The mode captures one exact candidate and the production open path revalidates that saved-in-memory binding before sync. It does not load or save application configuration, adopt a Runtime Output, alter a dashboard or select an alternate unit. A missing/multiple candidate, encoding/open/sync/send error stops the test without automatic retry.

Historical modes explicitly use the internal `PrimaryOnly` reply policy; their read/write sequence remains available for comparison with prior logs. They do not select the default JPEG-stream policy described [below](#reply-and-failure-boundaries).

The single-command image/brightness modes open/synchronize once, execute **one** selected command, then dispose the transport; the explicit `jpeg-pair` variants below are the two-frame exception. `png` and `jpeg` generate the matching encoded 720×1920 raster with the recovered clockwise correction. The expected physical landscape has red top-left, green top-right, blue bottom-right and yellow bottom-left corners, an upright codec label and a right-pointing arrow. A recognized response establishes an exchange result, not a visible/correct image; report those observations separately. Each invocation prints a random run ID, UTC timestamp and validated action to distinguish separate attempts without device identifiers.

The diagnostic PNG uses 8-bit, noninterlaced RGBA (PNG color type 6), with alpha 255 for every pixel and its own opaque background. Its alpha-capable render surface retains the fourth channel during encoding; declaring that surface opaque would let Skia emit RGB instead. The log prints the actual IHDR depth, color type and interlace before USB open. Software decoding/profile checks do not establish firmware compatibility; physical image acceptance remains separate.

The same mode accepts `jpeg`, or `brightness <0..100>` for a separately planned brightness checkpoint. Brightness changes hardware and is not automatically restored; there is no save/reset command or automatic later frame. A successful empty brightness read produces `UNACKNOWLEDGED` and no subsequent command; it is not a positive ACK or proof of physical effect. Do not chain further tests after an unexplained result.

To inspect the reply boundary independently of image decoding, use the separately planned `sync` action:

```powershell
dotnet .\artifacts\tests\PinkieSysMon.RegressionTests.dll --turzx-transport-check sync --consent YES |
    Tee-Object .\artifacts\turzx-sync-reply.log
```

This sends only sync and requires its recognized initial response and normal settling delay, then performs exactly one additional read of up to 512 bytes with the existing 3000 ms transfer timeout. It sends no image/brightness/save/reset command. The log records the follow-up byte count and whether a full response has the known status-marker shape; no raw response bytes or identifiers are printed. Empty, short and full observations remain distinct. Timeout, cancellation or native failure stops execution and disposal still drains native completion; no extra attempt follows. Neither this one read nor closing the connection proves the device queue empty, correlates a later response or authorizes automatic draining in production. The action does not deliberately change the picture, but firmware side effects of sync are not guaranteed absent.

The separately planned `png-sync` action uses that same single post-sync read and sends **one PNG only if the read succeeds with zero bytes**. Any nonempty/invalid count, timeout, cancellation or native failure stops before the image command. The subsequent PNG reply uses the unchanged strict image-response policy; no extra image-response read, automatic retry or drain loop is added. The pattern has a **blue background** and **TURZX PNG SYNC** label so a retained earlier frame can be distinguished. This explicit developer experiment tests the observed reply boundary; it is not an automatic recovery or a production queue-empty guarantee.

```powershell
dotnet .\artifacts\tests\PinkieSysMon.RegressionTests.dll --turzx-transport-check png-sync --consent YES |
    Tee-Object .\artifacts\turzx-png-sync.log
```

The corresponding `jpeg-sync` action applies the same single-empty-read gate before one JPEG command. Its pattern has a **burgundy background** and **TURZX JPEG SYNC** label. The encoded format, command ID and image-response classification are unchanged from `jpeg`; physical JPEG compatibility still requires its own observation.

```powershell
dotnet .\artifacts\tests\PinkieSysMon.RegressionTests.dll --turzx-transport-check jpeg-sync --consent YES |
    Tee-Object .\artifacts\turzx-jpeg-sync.log
```

`brightness-sync <0..100>` uses that same gate before one explicit brightness command, without an image or automatic restoration. It retains the existing brightness conversion and empty/unacknowledged result. It is available only for a separately planned checkpoint; do not chain it after an image test. These single-command controlled variants stop before the selected command on any nonempty/invalid post-sync read or read failure, and dispose after their one command without further reads/retries.

The separately planned `jpeg-pair` experiment tests two **different JPEG frames on one open connection**, rather than reopening/resynchronizing between invocations. Both payloads are encoded and validated before USB open. One initial sync, the normal settling delay and the same successful-empty-read gate precede frame A. Its image reply must pass the unchanged strict classifier before a two-second observation pause and frame B. The pause helps identify the pictures; it is not a measured firmware timing requirement. There is no additional sync, post-image read/drain, resend, codec switch, brightness command or third frame. Any failed write/reply or interrupted pause stops the sequence and disposes the connection.

```powershell
dotnet .\artifacts\tests\PinkieSysMon.RegressionTests.dll --turzx-transport-check jpeg-pair --consent YES |
    Tee-Object .\artifacts\turzx-jpeg-pair.log
"Process exit code: $LASTEXITCODE" |
    Tee-Object .\artifacts\turzx-jpeg-pair.log -Append
```

Frame A has an **orange background / TURZX JPEG PAIR A**; frame B has a **teal background / TURZX JPEG PAIR B**. Both retain the established corners and arrow. Record whether A appeared and whether B replaced it, including a retained earlier picture or partial sequence. `PAIR CHECK: EXCHANGES COMPLETE` reports two classified exchanges, never two displayed frames. This experiment can investigate a failure inside a continuous connection; it does not prove why a previous reopened invocation failed, eliminate firmware-state differences, or establish sustained Runtime operation. Follow the owning Issue's current preconditions; preserve this attempt's log and observation before any further hardware action.

The separately planned `jpeg-pair-read` variant keeps the same pair, initial sync/empty-read gate and observation pause, and adds **exactly one read after each recognized image response**. Each additional read is at most 512 bytes with the existing 3000 ms transfer timeout; its count and marker shape are logged separately. A must have both a strictly recognized primary response and a successful empty follow-up before B is sent. B must meet those same conditions before the diagnostic reports exchange completion. A nonempty/invalid follow-up, timeout, cancellation or native error stops immediately without another probe or command. An empty **primary image response still fails**; it is not skipped or reclassified as an ACK.

```powershell
dotnet .\artifacts\tests\PinkieSysMon.RegressionTests.dll --turzx-transport-check jpeg-pair-read --consent YES |
    Tee-Object .\artifacts\turzx-jpeg-pair-read.log
"Process exit code: $LASTEXITCODE" |
    Tee-Object .\artifacts\turzx-jpeg-pair-read.log -Append
```

This explicitly probes a possible response boundary after each command; it is not a read-until-empty loop or a production flush policy. There are at most three additional reads in the complete invocation: after sync, A and B. These observations neither prove a bus ZLP/empty queue nor correlate generic markers to commands. Native ownership/disposal and the original `jpeg-pair` behavior are retained. Actual firmware behavior and physical results belong in the owning Issue's dated reports.

The `jpeg-stream` action sends the same A/B pair through the **default transport-owned reply policy**, with no diagnostic-owned follow-up reads. It reports the actual primary and follow-up counts returned by each completed image operation. Consent, exact binding, pre-encoding of both images, observation pause and disposal rules remain the same. This compares transport ownership with the earlier `jpeg-pair-read` experiment; it is still a bounded developer checkpoint, not a Runtime dashboard or a sustained-stream test.

```powershell
dotnet .\artifacts\tests\PinkieSysMon.RegressionTests.dll --turzx-transport-check jpeg-stream --consent YES |
    Tee-Object .\artifacts\turzx-jpeg-stream.log
"Process exit code: $LASTEXITCODE" |
    Tee-Object .\artifacts\turzx-jpeg-stream.log -Append
```

| Exit | Meaning |
| --- | --- |
| 0 | Exchange completed with the current response policy, or the explicit sync follow-up observation was recorded; neither proves physical effect or reply correlation. |
| 1 | Incomplete or failed execution. |
| 2 | Invalid arguments, declined consent or unsupported host. |
| 3 | Command written, successful empty brightness read, no positive ACK. |

Capture and inspect one bounded result before the next action. Preserve the current `builder.log`, per-action diagnostic log and the owner's physical observation before another run; source is retained in Git. This developer-only checkpoint does not enable TURZX in Devices or change the production session/consent contract.

### Private initial-sync reply capture

For an unexplained short initial response, the separately planned **sync-capture** action records the actual primary bytes before the unchanged strict classifier runs:

```powershell
dotnet .\artifacts\tests\PinkieSysMon.RegressionTests.dll --turzx-transport-check sync-capture --consent YES |
    Tee-Object .\artifacts\turzx-sync-capture.log
"Process exit code: $LASTEXITCODE" |
    Tee-Object .\artifacts\turzx-sync-capture.log -Append
```

Use a current Windows build/regression and close Runtime and all other TURZX controllers first. Follow the owning Issue's current device-state precondition; do not reconnect or reset the device merely to make this capture succeed. One exact binding is captured and revalidated before one native open, one normal sync write and one primary read of at most **512 bytes** with the existing **3000 ms** transfer timeout. No follow-up read, drain, image, brightness, sync retry, pipe reset or firmware-save command follows, including after a recognized primary. Native disposal retains terminal-completion ownership. Sync itself may have firmware side effects; this is an explicitly consented active observation.

The log includes the invocation/version, actual count and precisely those returned bytes in hexadecimal, with no added binding/path metadata. **Keep the raw-reply log private; do not paste its contents into public Issues.** A failed transfer supplies no fabricated bytes or arbitrary exception text. Exit 1 for a short/unrecognized reply is a useful captured result, not a reason to retry. Exit 0 means only that the primary passed existing validation, not that JPEG output or recovery works. Preserve the Builder/capture logs and stop after this one invocation.

## Application configuration

The generic physical Output selects this implementation with `Transport = encrypted-jpeg`. Its protocol-neutral exact binding and user dimensions follow the [common Output contract](../../outputs/physical_devices.md#configuration-and-identity). The optional `EncryptedJpeg` object belongs only to this protocol. Old model-specific device settings are not migrated; dashboard documents remain untouched.

| Field / boundary | Implemented contract |
| --- | --- |
| `EncryptedJpeg.BrightnessPercent` | Nullable integer. `null` preserves the device's current brightness; `0..100` stores an explicitly selected application percentage. No factory/default brightness is inferred. |
| Controller conversion | `floor(percent * 102 / 100)`, with exact endpoints 0 and 102. This pure conversion sends no command. |
| Settings ownership | Detached target copies retain independent TURZX options through save/clone operations. Discovery does not dirty or save a draft. |
| Admission | Only separately confirmed, configured Outputs can be enabled. Enabled remains opt-in with fresh binding checks and default-No consent. Enabled records require `BrightnessPercent = null`; unsupported saved brightness fails validation before acquisition. |
| Other adapter options | Refresh/JPEG/USB values use existing physical normalization and are consumed by the common frame worker and this transport. Settings can clear previously saved unsupported brightness values without a hardware command. Retired logging preferences follow the [common physical-device policy](../../outputs/physical_devices.md#automatic-device-logging). |

Hardware-brightness implementation and acceptance are tracked separately in [Issue #74](https://github.com/Jim1537/PinkieSysMon/issues/74).

## Standalone transport

`TurzxTransport.TryOpen` requires an explicitly enabled TURZX target, resolves only its exact binding through passive discovery, verifies the selected path/product/hash, and returns no transport when that binding is absent. Duplicate/ambiguous bindings fail before open. The common frame adapter invokes it only for an explicitly enabled target; discovery, preview and Provider Enabled do not grant hardware use.

`WindowsTurzxNativeIo` owns the file and WinUSB interface. It accepts the initial interface 0 / alternate setting 0 with bulk OUT `01` and IN `81`, verifies their descriptors and nonzero packet sizes, and sets a finite pipe-transfer timeout of at least 500 ms. Other interfaces/endpoints are not tried. It retains WinUSB's default non-RAW I/O policy: [WinUSB splits larger writes internally](https://learn.microsoft.com/en-us/windows/win32/api/winusb/nf-winusb-winusb_writepipe), while the adapter submits one combined control/image buffer and checks the complete byte count. It does not append arbitrary packets, flush replies or reset stalled endpoints.

### Framing and commands

The independent C# implementation follows public interoperability facts from [the pinned protocol reference](https://github.com/mathoudebine/turing-smart-screen-python/blob/2b33ab4f00a096916dd6a1174441a53a7ec33b03/library/lcd/lcd_comm_turing_usb.py). That GPL-3.0-or-later implementation is research evidence, not an imported dependency or copied source. The fixed public DES wire-format material in `TurzxWireProtocol` is an interoperability constant, not an authentication credential or protection for application secrets.

| Wire field / command | Adapter contract |
| --- | --- |
| Control packet | 500-byte zero-initialized command body, zero-padded to 504 for DES-CBC without additional padding; 504 ciphertext bytes, six reserved zero bytes, then `A1 1A`, total 512 bytes. |
| Plaintext fields | Command at 0; `1A 6D` at 2–3; local milliseconds since midnight as uint32 little-endian at 4–7. |
| Sync `10` | Required before image/brightness commands. After its recognized response, one cancellation-aware 200 ms settling delay follows the recovered probe sequence. This is an adapter policy, not a measured minimum for every firmware. |
| JPEG `101` / PNG `102` | uint32 big-endian encoded-image length at 8–11; encoded bytes immediately follow the control packet. The caller must provide the matching format signature. This check is not full image decoding or geometry validation. |
| Brightness `14` | Controller value at byte 8; percent conversion uses the [configuration policy above](#application-configuration). No implicit brightness command on open. |
| Payload bound | Encoded image limited to 1,048,576 bytes, following the recovered probe/upstream software bound. It is not a measured firmware capacity. No implicit downscaling, codec switch or quality reduction. |

The low-level transport accepts encoded bytes without rotating or decoding them. Its shared frame profile applies the established clockwise 90° wire rotation and encoded payload bound to the assigned dashboard Canvas. There is no 1920×720 fitting surface or automatic black-bar insertion: a 1920×480 Canvas now produces a 480×1920 JPEG. Correct physical orientation and image dimensions still require observation on the selected device.

### Reply and failure boundaries

The default `JpegStream` mode implements the observed sync/JPEG sequence on one connection. Sync requires its strict primary reply, the existing cancellation-aware 200 ms settling delay, then **exactly one successful empty follow-up read**. Each JPEG requires its strict primary reply followed by the same single-empty-read boundary. Both reads execute under the command lock using the existing buffer, timeout and native owner. `TurzxCommandCompletion` reports the primary count/reply and a separate nullable `FollowUpResponseBytes`; zero records a completed empty follow-up, while null means none was performed.

A nonempty/invalid follow-up or read error faults the transport before another command; there is no read-until-empty loop, retry, third read, extra sync, automatic reopen or brightness replay. An empty primary sync/JPEG reply still fails. The follow-up is an observed completion policy, not proof of a bus ZLP, command correlation or an empty device queue. Native timeout/cancellation/error results never become successful empty completions.

`JpegStream` rejects PNG and brightness **before I/O**, because their sequencing into this stream has not been physically established. The explicitly selected internal `PrimaryOnly` mode retains one write/primary-read per command for the historical codec/brightness diagnostics; those modes own any separately planned extra probe. They do not establish a production brightness-to-frame policy. A short write fails before any read, and a failed admitted command in either mode forbids reuse.

| Completed read | Result and allowed continuation |
| --- | --- |
| Exactly 512 primary bytes with `C8` at offset 1 or 8 | `StatusMarker`, identifying the researched response shape only. The default stream also requires its separate empty follow-up before returning completion. Neither proves physical effect or a displayed frame. |
| Zero primary bytes for brightness in `PrimaryOnly` | Return `Empty`, meaning write completed and read returned zero, **without a positive ACK**. Forbid subsequent commands on this transport because a delayed reply could be misattributed. |
| Empty sync/image reply, short/oversize reply, or unrecognized full reply | Explicit protocol failure; no further command or automatic resend. A prior response buffer's remaining bytes cannot supply a marker. |

The empty-brightness rule preserves the historically observed physical effect without turning every empty read into success. Safe brightness-to-image sequencing and delayed replies remain physical acceptance questions. Closing this connection alone does not prove device-side queues are empty or authorize blindly replaying a state-changing command.

### Cancellation and native ownership

`TurzxUsbConnection` serializes native operations and owns the pinned transfer buffer, manual-reset event and x64 `OVERLAPPED`. Each operation has both the native pipe timeout and a managed deadline. Caller cancellation, deadline or concurrent Dispose can request cancellation of the exact operation. [Cancellation is a request, not completion](https://learn.microsoft.com/en-us/windows/win32/api/ioapiset/nf-ioapiset-cancelioex): even if that request fails, the code waits for the terminal native result through [WinUsb_GetOverlappedResult](https://learn.microsoft.com/en-us/windows/win32/api/winusb/nf-winusb-winusb_getoverlappedresult) before releasing or reusing memory. Successful empty completion stays distinct from timeout, disconnect and cancellation.

Dispose cancels admission/in-flight work and waits for that ownership boundary before closing native handles. Ordinary open/initialization calls must return before cancellation can be observed and their resources released. A faulty driver can prevent completion: neither timeout nor Dispose promises forced bounded cleanup. No buffer, event or handle is freed merely because the application deadline elapsed.

`Issue71TransportTests` covers both the default JPEG stream and explicitly selected primary-only diagnostic policy, including full primary/follow-up sequencing, rejection/faulting, command serialization and native cancellation during a pending follow-up. It also checks independent packet fixtures, format/size bounds, strict reply policy, exact acquisition and failed-sync cleanup, error identity, pending completion, cancellation failure, deadlines and concurrent disposal using synthetic I/O. These tests do not execute the real Windows backend or establish this unit's native discovery, command responses, brightness sequencing, image orientation or integrated Runtime behavior.

## Runtime sessions and recovery

`OutputSessionManager` routes a generic `physical` Output with `encrypted-jpeg` transport to the common physical `FramePump`. The assigned canonical document, telemetry demand/publication, idle/suspend, selected reload, global lifetime and consented Enabled controls retain the [common architecture](../architecture.md#output-status-and-tray-control). There is no fixed frame budget in normal Runtime operation. The bounded developer checkpoint below remains a separate tool.

A missing exact binding returns no connection and can be checked again at the configured retry interval without a native exchange. A failed TURZX open/handshake or admitted frame exchange puts that session in **ERROR** and retires any acquired transport through terminal completion. The worker then checks passive Windows metadata immediately and at the configured USB retry interval; it opens no device during these checks. Idle pause and suspend suppress this polling.

| Passive recovery observation | Result |
| --- | --- |
| Exact binding still present, without a preceding observed absence | Keep **ERROR**; do not replay an uncertain exchange. A native error number alone does not establish removal. |
| Successful complete scan finds the binding missing | Record absence and show **WAITING**. Continue passive polling without USB acquisition. |
| Exact binding uniquely returns after that absence | Permit a fresh connection through normal binding revalidation and sync, then resume dashboard frames. Saved Enabled, dashboard and sibling Outputs are retained. |
| Failed/incomplete scan or ambiguous binding | Show **ERROR** or **AMBIGUOUS** and make no native attempt. Present nodes with unavailable metadata, driver problems or no correlated usable interface cannot establish absence. |

Ordinary reconfiguration, Reload, idle wake or suspend/resume does not itself clear the failure. Superseded scan results are discarded; changing the connection invalidates prior absence evidence. If a brief unplug/replug occurs entirely between observations, automatic recovery cannot infer it: reconnect long enough for absence to be observed. After preserving the error, **Enabled off → on** or full Runtime stop/start remains an explicit new-attempt path. A new attempt does not prove recovery or reset firmware/USB state. Trofeo retains its existing retry behavior.

A transport-disposal failure blocks automatic recovery for that session: ownership must not be assumed released. Preserve the local error before a deliberate restart; native completion and cleanup limits remain as defined above.

Brightness adjustment remains unavailable. `null` brightness preserves the current hardware setting; lifecycle/errors follow [shared physical-device logging](../../outputs/physical_devices.md#automatic-device-logging). These limits are enforced in configuration validation, worker preparation and adapter acquisition, including when a caller bypasses file loading. No PNG switching, brightness replay, quality reduction, firmware save or automatic USB reset is added.

## Shared-worker live-dashboard checkpoint

After a current [Windows build and regression run](../building.md#build-and-regression), close other applications controlling this TURZX device and run the already-built runner **once from the repository root**:

```powershell
dotnet .\artifacts\tests\PinkieSysMon.RegressionTests.dll --turzx-runtime-check --consent YES |
    Tee-Object .\artifacts\turzx-runtime-check.log
"Process exit code: $LASTEXITCODE" |
    Tee-Object .\artifacts\turzx-runtime-check.log -Append
```

This generates an ordinary current-schema **1920×480 adaptive dashboard in memory** through the shared `AdaptiveDashboardFactory`; no stock or saved dashboard file is loaded or required. Its title is changed in memory to **PINKIESYSMON / TURZX LIVE**; its `system.os.datetime` clock already uses `HH:mm:ss`. Real local time is published to the shared metric store; without normal telemetry acquisition, the other generated PC/OS metrics display their normal unavailable state rather than fabricated values. The checkpoint uses the actual `DashboardRenderer`, `FramePump` and JPEG-stream transport, rather than a synthetic A/B picture loop. It does not start ordinary telemetry acquisition/providers or the normal saved-configuration session manager, load a private panel, or save dashboard/configuration files.

Asset/font resolution and a bounded JPEG preflight precede discovery. Exactly one usable interface and explicit `--consent YES` are required. The selected binding is verified again on native acquisition. One native open attempt, one initial sync and at most **20 completed JPEG frames** are admitted at a 500 ms refresh interval. A 20-second observation deadline stops the checkpoint if that budget is not established. Native ownership still drains terminal I/O completion during disposal; neither this deadline nor Ctrl+C guarantees bounded shutdown of a stalled driver. The checkpoint never automatically reopens the connection, changes codecs/brightness, or saves/resets firmware.

Expected owner observation: the generated diagnostic layout replaces the previous test, its title is readable, seconds advance in place, and the assigned composition is visible. This historical 1920×480 diagnostic no longer inserts model-sized black bars; do not treat its old fitted-image acceptance as validation of the current geometry. After roughly ten seconds the worker stops and the last picture remains. `FRAMES COMPLETE` requires 20 completed exchanges and at least two different consecutive encoded payloads; it does **not** prove physical animation, correct orientation or ongoing Runtime readiness. Preserve both `artifacts/turzx-runtime-check.log` and `artifacts/turzx-runtime-worker.log`, alongside the build log. The worker log may contain local paths/errors and is private evidence. On failure or a retained/incorrect picture, preserve the result and stop before another attempt or USB reconnect.

`TurzxFrameTransport` uses only the transport-owned JPEG reply policy. It rejects a non-null brightness setting before acquisition because that integration is not verified. The [common connection/synchronization failure recorder](../../outputs/physical_devices.md#connection-and-synchronization-failure-diagnostics) and worker lifecycle/error logging require no device-specific option. Normal Settings/Runtime sessions follow the recovery contract above; this bounded checkpoint retains its single-attempt policy and performs no automatic recovery. The earlier image-output acceptance is recorded in closed [Issue #71](https://github.com/Jim1537/PinkieSysMon/issues/71); current generic onboarding requires separate acceptance in [Issue #95](https://github.com/Jim1537/PinkieSysMon/issues/95).

`Issue71RuntimeTests` exercises legacy raster parity across all canvas orientations, clockwise wire mapping, dashboard-sized rendering/alpha isolation and payload bounds, adapter/consent admission, bounded worker transmission and no-reopen failure behavior using synthetic USB. It distinguishes changing dashboard payloads from identical completed frames. A focused Issue #94 assertion also verifies that this checkpoint generates a canonical dashboard without reading or writing any prepackaged board. These regressions do not execute Windows discovery/native I/O or establish physical acceptance.

## Integration boundaries

`OutputTargetContract.IsPhysicalKind` classifies the generic physical record; `HasIntegratedTransport` identifies implemented protocol handlers. Inventory, controls and metrics use the logical Output identity. The factory selects the handler from the saved protocol, with current exact catalog-interface revalidation before native acquisition. A model label or passive protocol hint is never dispatch authority.

The existing canonical renderer, application transactions, global lifetime and idle policy remain with [Architecture](../architecture.md). Shared adoption, consent, removal and troubleshooting remain with [Physical Devices](../../outputs/physical_devices.md); model-specific recovery is defined [above](#runtime-sessions-and-recovery).
