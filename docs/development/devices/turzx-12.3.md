# TURZX 12.3-inch — Integration Contract

This reference owns the TURZX-specific identity and application configuration contract. **Runtime transport and image output are not integrated yet.** The device remains an informational, unsupported family in Devices; [Issue #71](https://github.com/Jim1537/PinkieSysMon/issues/71) owns implementation and physical acceptance. The [supported-device inventory](../../supported-devices.md) lists currently usable models.

## Passive interface discovery

`TurzxDeviceDiscovery` prepares exact descriptors for USB product `1CBE:0123` using Windows metadata only. Its capture API is not yet used by Settings adoption or Runtime discovery. It requires:

- A present, non-composite-function USB devnode with complete metadata, the `WinUSB` service and no reported device problem. Unevidenced composite function layouts are excluded.
- Application interface GUIDs read from that devnode's existing hardware registry key through `CM_Open_DevNode_Key`, with query-only access and `OpenExisting`. Both documented `DeviceInterfaceGUIDs` (multi-string) and `DeviceInterfaceGUID` (string) registrations are supported; neither is created or changed.
- Present interfaces enumerated with each registered class GUID **and that exact devnode**. The path must match the exact product and registered class, and `DEVPKEY_Device_InstanceId` must independently confirm the same devnode. Presence/driver status is checked again before returning a descriptor.

List/property sizes and buffer-growth retries are bounded. Missing registration or denied registry access supplies no usable interface; malformed data and scan failures are explicit errors. No Trofeo GUID, inferred path, hardware serial or first-match fallback is substituted. Discovery opens no device handle, initializes no WinUSB transport, sends no commands and installs no driver. Actual native discovery requires separate Windows verification.

Case aliases of one interface are deduplicated. Different interfaces on one devnode retain their separate bindings and shared hashed node key; the common ambiguity guard rejects selecting one arbitrarily. Equal product IDs do not merge different units.

Microsoft documents [hardware-key access](https://learn.microsoft.com/en-us/windows-hardware/drivers/install/opening-a-device-s-hardware-key), [WinUSB interface registration](https://learn.microsoft.com/en-us/windows-hardware/drivers/usbcon/winusb-installation) and [devnode-scoped interface enumeration](https://learn.microsoft.com/en-us/windows/win32/api/cfgmgr32/nf-cfgmgr32-cm_get_device_interface_listw). These API contracts do not prove a particular device's installed registration or firmware behavior.

## Application configuration

A prepared TURZX record uses `Kind = turzx`, `Transport = turzx` and an explicit `DeviceId` containing `turzx:` plus the 64 hexadecimal SHA-256 digits of its trimmed, uppercased Windows interface path. This is a local interface binding, not a serial or cross-port stability promise. A missing or changed binding never selects another interface, even when only one TURZX is present. The [common Output contract](../../outputs/physical_devices.md#configuration-and-identity) owns logical Output identity and dashboard assignment.

The optional `Turzx` object belongs only to this adapter; Desktop and Trofeo reject it. Validation creates it for a TURZX record if absent. Existing Desktop/Trofeo records omit the new object and retain their previous normalization and settings. No dashboard schema migration is involved. Older application binaries cannot read a TURZX kind; preserve the earlier local configuration before downgrading after deliberate adoption.

| Field / boundary | Implemented contract |
| --- | --- |
| `Turzx.BrightnessPercent` | Nullable integer. `null` preserves the device's current brightness; `0..100` stores an explicitly selected application percentage. No factory/default brightness is inferred. |
| Controller conversion | `floor(percent * 102 / 100)`, with exact endpoints 0 and 102. This pure conversion sends no command. |
| Settings ownership | Detached target copies retain independent TURZX options through save/clone operations. Discovery does not dirty or save a draft. |
| Transport gate | Only the Trofeo transport is currently integrated. Saving an enabled TURZX record fails; preparing a physical enable also rejects it before consent or session creation. Prepared TURZX candidates cannot be adopted or edited. |
| Other adapter options | Refresh/JPEG/USB values use existing physical configuration normalization, but no TURZX encoder, USB session, diagnostic journal or brightness command currently consumes them. They do not establish supported capabilities. |

A future adapter must apply an explicit saved brightness on its own worker at open/reconnect or after a committed settings change. A `null` setting must send no brightness command. Neither draft editing nor discovery may change the hardware. Firmware persistence, restart and pixel dimming are outside this contract.

## Integration boundaries

`OutputTargetContract.IsPhysicalKind` classifies the two physical record kinds; `HasIntegratedTransport` separately controls actual availability. The shared inventory, detached Settings model, metric identity and control/status projections accept the new record type without routing it to Trofeo. The default session factory explicitly rejects adapters that have not been integrated. A descriptor, known kind or registry match does not authorize hardware use.

The existing canonical renderer, application transactions, global lifetime and idle policy remain with [Architecture](../architecture.md). Shared adoption, consent, removal and recovery remain with [Physical Devices](../../outputs/physical_devices.md). No production dashboard or private configuration is changed by this preparation.
