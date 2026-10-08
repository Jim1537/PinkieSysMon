# Trofeo Vision 9.16 — Device Integration

This is a **maintainer-facing reference** for the output implementation currently used by Pinkie's System Monitor. It describes behavior established by the project's source code; it is **not** a manufacturer-issued USB protocol specification and does not establish compatibility with other devices or firmware variants.

[Supported Devices](../../supported-devices.md) · [Compatibility & Testing](../../compatibility.md)

## Scope and implementation ownership

PinkieSysMon renders dashboard frames in the native Windows Runtime, encodes them as JPEG, and sends them over the selected device's WinUSB interface. This is a dedicated USB frame path, **not** a Windows extended/mirrored monitor or a generic USB-display driver.

| Responsibility | Implementation |
| --- | --- |
| USB interface enumeration and device identity | [`DeviceDiscovery.cs`](https://github.com/Jim1537/PinkieSysMon/blob/main/src/PinkieSysMon/DeviceDiscovery.cs) |
| Transport open, handshake, framing, USB I/O, ACK | [`TrofeoTransport.cs`](https://github.com/Jim1537/PinkieSysMon/blob/main/src/PinkieSysMon/TrofeoTransport.cs) |
| Per-target rendering, retry, connection state and metrics | [`FramePump.cs`](https://github.com/Jim1537/PinkieSysMon/blob/main/src/PinkieSysMon/FramePump.cs) |
| Output session coordination and reconfiguration | [`OutputSessionManager.cs`](https://github.com/Jim1537/PinkieSysMon/blob/main/src/PinkieSysMon/OutputSessionManager.cs) |
| Canvas orientation and device-side rotation | [`FrameGeometry.cs`](https://github.com/Jim1537/PinkieSysMon/blob/main/src/PinkieSysMon/FrameGeometry.cs) |
| Target, renderer and USB configuration contracts | [`AppConfig.cs`](https://github.com/Jim1537/PinkieSysMon/blob/main/src/PinkieSysMon/AppConfig.cs) |

## Protocol research and acknowledgments

**Special thanks to [@Lexonight1](https://github.com/Lexonight1)**, creator of [thermalright-trcc-linux](https://github.com/Lexonight1/thermalright-trcc-linux), for publishing the [reverse-engineered USBLCDNEW / LY USB bulk protocol reference](https://github.com/Lexonight1/thermalright-trcc-linux/blob/main/doc/PROTOCOL_USBLCDNEW.md#protocol-4-ly-lcd-04165408). That research documents the Thermalright software's LY transport for USB `0416:5408`, including endpoint selection, handshake, 512-byte JPEG frame records, transfer grouping, and acknowledgments. Making these low-level details publicly inspectable is a valuable contribution to independent device integration and verification.

The exact behavior described on this page remains the behavior of PinkieSysMon's own [`TrofeoTransport.cs`](https://github.com/Jim1537/PinkieSysMon/blob/main/src/PinkieSysMon/TrofeoTransport.cs). The upstream research is an **external reference**, not an official Thermalright specification. PinkieSysMon's public repository history does not independently establish whether its original transport implementation was derived directly from that project; this acknowledgment credits the published protocol research without asserting a verbatim code import.

## Hardware identification and output selection

- **Target model:** Thermalright Trofeo Vision 9.16; **native resolution:** 1920 × 480; **connection:** USB / WinUSB.
- **Windows device-interface class GUID:** `DEE824EF-729B-4A0E-9C14-B7117D33A817`.
- **USB VID/PID token in the interface path:** `vid_0416&pid_5408`.
- **Configured transport key:** `trofeo` (in `Outputs.Targets[].Transport`).
- **Device identity:** `DeviceDiscovery.CreateStableDeviceId` trims and uppercases the enumerated interface path, computes its SHA-256 digest, and prefixes the hexadecimal digest with `trofeo:`. This is **derived from the Windows interface path**, not a verified manufacturer serial number; do not assume it remains unchanged if the interface path changes.

`OutputDeviceSelection.Resolve` binds an explicitly configured `DeviceId` only to its exact discovered match. With no explicit ID, it accepts **exactly one** matching device; zero means missing and more than one means ambiguous. An unsupported transport is rejected. Multiple output targets require explicit device IDs, and two targets cannot bind to the same identity. **Never silently substitute the first compatible device for a configured or ambiguous target.**

## Opening the transport and handshake

1. Enumerate present device interfaces through Windows Configuration Manager, filter for the above VID/PID, deduplicate interface paths and form descriptors.
2. Open the selected device path using `CreateFileW`: `GENERIC_READ | GENERIC_WRITE`, shared read/write, `OPEN_EXISTING`, and `FILE_ATTRIBUTE_NORMAL | FILE_FLAG_OVERLAPPED`. **Preserve the `FILE_FLAG_OVERLAPPED` requirement** even though the current `WinUsb_ReadPipe`/`WinUsb_WritePipe` calls use a null overlapped pointer.
3. Call `WinUsb_Initialize`, then apply `PIPE_TRANSFER_TIMEOUT` to the two currently used pipes: **OUT `0x09`** and **IN `0x81`**.
4. Send a **2,048-byte** zero-initialized handshake request to OUT with bytes `[0]=0x02`, `[1]=0xFF`, and `[8]=0x01`.
5. Read up to **512 bytes** from IN. A valid response must contain at least **23 bytes** and markers `[0]=0x03`, `[1]=0xFF`, `[8]=0x01`. Otherwise the connection is rejected.
6. Derive `ProtocolPm = 64 + (response[20] <= 3 ? 1 : response[20])` and `ProtocolSub = response[22] + 1`. The current wire-rotation rule is **180°** when `ProtocolSub < 2` or `ProtocolSub > 4`, otherwise **0°**. These are implementation rules, not an independent claim about vendor firmware semantics.

On failure, `TrofeoTransport.TryOpen` disposes the partially initialized transport. Its handle and WinUSB interface are released on disposal.

## JPEG frame wire format

The Runtime renderer produces JPEG bytes and `TrofeoTransport.SendJpeg` prepares the device's frame records before USB transmission. The following exact details are **protocol-sensitive**:

- Each frame record occupies **512 bytes**: **16-byte header + up to 496 JPEG payload bytes**.
- The implementation computes `numChunks = jpegLength / 496 + 1`; this deliberately includes a final record with **zero payload bytes** when the JPEG length is an exact multiple of 496.
- The number of transmitted records is rounded up to a **multiple of four**. The padding records are zero-filled. Header reserved bytes and unused payload space are cleared on every frame so prior frame data cannot leak into a new record.
- Each active record's header is little-endian for the numeric fields:

| Offset | Size | Current value / meaning |
| --- | --- | --- |
| `0`–`1` | 2 bytes | `01 FF` |
| `2`–`5` | 4 bytes | Total JPEG payload size (`uint32`) |
| `6`–`7` | 2 bytes | Payload bytes in this record (`uint16`) |
| `8` | 1 byte | `01` |
| `9`–`10` | 2 bytes | Actual record count before padding (`uint16`) |
| `11`–`12` | 2 bytes | Zero-based record index (`uint16`) |
| `13`–`15` | 3 bytes | Zeroed reserved bytes |

Records are transferred through OUT `0x09` in writes of at most **4,096 bytes**, using a reusable pinned managed buffer. The sender then reads an ACK from IN `0x81` into a **512-byte** buffer. The current implementation requires a **non-empty** ACK and does **not** validate its contents beyond that; do not describe stricter ACK validation as implemented.

**Do not change** header layout, the extra terminal record, four-record padding, endpoints, transfer sizing, pinned-buffer lifetime, handshake, or ACK sequencing based on speculative optimization. A protocol change requires controlled testing against the real device.

## Geometry and configuration

- The supported device's native frame size is **1920 × 480**. `FrameGeometry.DefaultNativeWidth/Height` are **historical/default and schema-migration constants**; actual dashboard geometry comes from the dashboard's canvas definition, not an unconditional hard-coded render size.
- Logical dashboard orientation permits **0°, 90°, 180°, 270°**. The separate protocol-dependent wire rotation permits **0° or 180°**. `FrameGeometry.DrawLogicalToWire` applies the wire transform and the logical orientation in the established order.
- Defaults from `AppConfig.cs`: `Renderer.JpegQuality = 92`, `Display.RefreshIntervalMs = 1000`, `Usb.RetryIntervalMs = 3000`, `Usb.TransferTimeoutMs = 3000` (milliseconds). Configuration validation requires the display interval to be at least 10 ms; both USB intervals at least 500 ms; and JPEG quality from 10 through 100.
- Output targets are configured under `Outputs.Targets`, each with `Id`, `Name`, `Transport`, and optional `DeviceId`. These target identifiers and device selection rules must remain deterministic.

## Connection lifetime and evidence limits

`FramePump` owns the live `TrofeoTransport` for each output target, tracks connection state, and retries after a missing device or connection/transfer error. A failed transfer drops and disposes the current transport, sets an explicit disconnected/error state as appropriate, and schedules a new attempt. Device-selection ambiguity is surfaced instead of silently choosing a device.

**Real-device acceptance remains incomplete** for unplug/replug, suspend/resume, configured-device-missing, and multiple-device ambiguity scenarios. The relevant checks are tracked as deferred physical output acceptance work. Code paths for recovery exist, but the outstanding physical scenarios must **not** be reported as passed merely because the implementation or documentation exists.

### Maintenance and verification checklist

When changing device integration, verify the exact source and distinguish: static inspection; Windows compilation/regression; Runtime behavior; and **physical-device acceptance**. In particular, test identity resolution, stale-handle rejection, recovery after lifecycle transitions, JPEG record framing/padding, handshake, and ACK behavior against the real device before declaring a transport change proven. Preserve other output sessions when a single target is unavailable.

Do not infer compatibility from matching USB VID/PID, resolution, or visual similarity alone. New hardware requires its own evidenced discovery, selection, transport contract, and documented physical validation.
