# TURZX 12.3-inch — Integration Contract

This reference owns the TURZX-specific identity, configuration and standalone transport contract. **The transport is not connected to Runtime sessions, rendering or Settings adoption yet.** The device remains an informational, unsupported family in Devices; [Issue #71](https://github.com/Jim1537/PinkieSysMon/issues/71) owns implementation and physical acceptance. The [supported-device inventory](../../supported-devices.md) lists currently usable models.

## Passive interface discovery

`TurzxDeviceDiscovery` prepares exact descriptors for USB product `1CBE:0123` using Windows metadata only. Its capture API is not yet used by Settings adoption or Runtime discovery. It requires:

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

After a current Windows build/regression, an explicit active mode can test the actual C# transport independently of Runtime adoption. Start with the known PNG reference path; keep other TURZX control applications closed:

```powershell
dotnet .\artifacts\tests\PinkieSysMon.RegressionTests.dll --turzx-transport-check png --consent YES |
    Tee-Object .\artifacts\turzx-transport-png.log
```

This replaces the selected TURZX's picture with a synthetic pattern. Hardware use requires the explicit `--consent YES` argument; absent consent or any other value declines before discovery or USB open. Argument consent also works when output is piped to the log, without depending on interactive stdin. The mode captures one exact candidate and the production open path revalidates that saved-in-memory binding before sync. It does not load or save application configuration, adopt a Runtime Output, alter a dashboard or select an alternate unit. A missing/multiple candidate, encoding/open/sync/send error stops the test without automatic retry.

The single-command image/brightness modes open/synchronize once, execute **one** selected command, then dispose the transport; the explicit `jpeg-pair` experiment below is the two-frame exception. `png` and `jpeg` generate the matching encoded 720×1920 raster with the recovered clockwise correction. The expected physical landscape has red top-left, green top-right, blue bottom-right and yellow bottom-left corners, an upright codec label and a right-pointing arrow. A recognized response establishes an exchange result, not a visible/correct image; report those observations separately. Each invocation prints a random run ID, UTC timestamp and validated action to distinguish separate attempts without device identifiers.

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

| Exit | Meaning |
| --- | --- |
| 0 | Exchange completed with the current response policy, or the explicit sync follow-up observation was recorded; neither proves physical effect or reply correlation. |
| 1 | Incomplete or failed execution. |
| 2 | Invalid arguments, declined consent or unsupported host. |
| 3 | Command written, successful empty brightness read, no positive ACK. |

Capture and inspect one bounded result before the next action. Preserve the current `builder.log`, per-action diagnostic log and the owner's physical observation before another run; source is retained in Git. This developer-only checkpoint does not enable TURZX in Devices or change the production session/consent contract.

## Application configuration

A prepared TURZX record uses `Kind = turzx`, `Transport = turzx` and an explicit `DeviceId` containing `turzx:` plus the 64 hexadecimal SHA-256 digits of its trimmed, uppercased Windows interface path. This is a local interface binding, not a serial or cross-port stability promise. A missing or changed binding never selects another interface, even when only one TURZX is present. The [common Output contract](../../outputs/physical_devices.md#configuration-and-identity) owns logical Output identity and dashboard assignment.

The optional `Turzx` object belongs only to this adapter; Desktop and Trofeo reject it. Validation creates it for a TURZX record if absent. Existing Desktop/Trofeo records omit the new object and retain their previous normalization and settings. No dashboard schema migration is involved. Older application binaries cannot read a TURZX kind; preserve the earlier local configuration before downgrading after deliberate adoption.

| Field / boundary | Implemented contract |
| --- | --- |
| `Turzx.BrightnessPercent` | Nullable integer. `null` preserves the device's current brightness; `0..100` stores an explicitly selected application percentage. No factory/default brightness is inferred. |
| Controller conversion | `floor(percent * 102 / 100)`, with exact endpoints 0 and 102. This pure conversion sends no command. |
| Settings ownership | Detached target copies retain independent TURZX options through save/clone operations. Discovery does not dirty or save a draft. |
| Transport gate | Only the Trofeo transport is currently integrated. Saving an enabled TURZX record fails; preparing a physical enable also rejects it before consent or session creation. Prepared TURZX candidates cannot be adopted or edited. |
| Other adapter options | Refresh/JPEG/USB values use existing physical configuration normalization. The standalone transport accepts an explicit transfer timeout, but no Runtime TURZX session/encoder or diagnostic journal consumes these saved options yet. They do not establish supported capabilities. |

A future adapter must apply an explicit saved brightness on its own worker at open/reconnect or after a committed settings change. A `null` setting must send no brightness command. Neither draft editing nor discovery may change the hardware. Firmware persistence, restart and pixel dimming are outside this contract.

## Standalone transport

`TurzxTransport.TryOpen` requires an explicitly enabled TURZX target, resolves only its exact binding through passive discovery, verifies the selected path/product/hash, and returns no transport when that binding is absent. Duplicate/ambiguous bindings fail before open. This internal acquisition API does not bypass the application's pending-adapter or consent gates; no production caller currently invokes it.

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

The caller owns encoding and the 720×1920 transport raster. This layer neither rotates the dashboard nor proves JPEG/PNG display compatibility. Application geometry, renderer selection and physical corner/orientation acceptance remain pending.

### Reply and failure boundaries

One command write and one read of up to 512 bytes execute under a serialized command lock. A short write fails before reading. There are no automatic resends, extra sync attempts or read retries. Native errors, timeout and cancellation propagate separately from successful zero-byte reads. A failed admitted command makes this transport unusable; dispose it before any further work. There is no automatic reopen or brightness replay in this layer.

| Completed read | Result and allowed continuation |
| --- | --- |
| Exactly 512 bytes with `C8` at offset 1 or 8 | `StatusMarker`, identifying the researched response shape only. It does not prove command correlation, physical effect or a displayed frame. |
| Zero bytes for brightness only | Return `Empty`, meaning write completed and read returned zero, **without a positive ACK**. Forbid subsequent commands on this transport because a delayed reply could be misattributed. |
| Empty sync/image reply, short/oversize reply, or unrecognized full reply | Explicit protocol failure; no further command or automatic resend. A prior response buffer's remaining bytes cannot supply a marker. |

The empty-brightness rule preserves the historically observed physical effect without turning every empty read into success. Safe brightness-to-image sequencing and delayed replies remain physical acceptance questions. Closing this connection alone does not prove device-side queues are empty or authorize blindly replaying a state-changing command.

### Cancellation and native ownership

`TurzxUsbConnection` serializes native operations and owns the pinned transfer buffer, manual-reset event and x64 `OVERLAPPED`. Each operation has both the native pipe timeout and a managed deadline. Caller cancellation, deadline or concurrent Dispose can request cancellation of the exact operation. [Cancellation is a request, not completion](https://learn.microsoft.com/en-us/windows/win32/api/ioapiset/nf-ioapiset-cancelioex): even if that request fails, the code waits for the terminal native result through [WinUsb_GetOverlappedResult](https://learn.microsoft.com/en-us/windows/win32/api/winusb/nf-winusb-winusb_getoverlappedresult) before releasing or reusing memory. Successful empty completion stays distinct from timeout, disconnect and cancellation.

Dispose cancels admission/in-flight work and waits for that ownership boundary before closing native handles. Ordinary open/initialization calls must return before cancellation can be observed and their resources released. A faulty driver can prevent completion: neither timeout nor Dispose promises forced bounded cleanup. No buffer, event or handle is freed merely because the application deadline elapsed.

`Issue71TransportTests` checks independent packet fixtures, format/size bounds, strict reply policy, exact acquisition and failed-sync cleanup, error identity, pending completion, cancellation failure, deadlines and concurrent disposal using synthetic I/O. These tests do not execute the real Windows backend or establish this unit's native discovery, command responses, brightness sequencing, image orientation or integrated Runtime behavior.

## Integration boundaries

`OutputTargetContract.IsPhysicalKind` classifies the two physical record kinds; `HasIntegratedTransport` separately controls actual availability. The shared inventory, detached Settings model, metric identity and control/status projections accept the new record type without routing it to Trofeo. The default session factory explicitly rejects adapters that have not been integrated. A descriptor, known kind or registry match does not authorize hardware use.

The existing canonical renderer, application transactions, global lifetime and idle policy remain with [Architecture](../architecture.md). Shared adoption, consent, removal and recovery remain with [Physical Devices](../../outputs/physical_devices.md). No production dashboard or private configuration is changed by this preparation.
