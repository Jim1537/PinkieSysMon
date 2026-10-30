# Supported Devices

The models below are historical hardware evidence for the two implemented wire protocols. Their tested dimensions are not model presets or the full candidate admission list. The current [generic physical Output contract and onboarding integration status](outputs/physical_devices.md) distinguish catalog admission, protocol selection and actual device acceptance; a similar screen size, connector or product name does not establish firmware compatibility.

## Thermalright Trofeo Vision 9.16

- **Support status:** Direct frame output implemented.
- **Native resolution:** 1920 × 480 pixels.
- **Connection:** USB using the Windows WinUSB interface.

The Trofeo Vision 9.16 is suitable for PinkieSysMon because it operates as a dedicated frame-output device rather than an additional Windows desktop monitor. PinkieSysMon renders the dashboard locally and transfers its JPEG frames directly through USB, allowing a wide, independent telemetry display without extending or mirroring the Windows desktop.

## TURZX 12.3-inch

- **Support status:** JPEG frame output and ordinary Settings/Runtime integration implemented. Historical implementation and acceptance are tracked in [Issue #71](https://github.com/Jim1537/PinkieSysMon/issues/71).
- **Physical resolution:** 1920 × 720 pixels; protocol wire rotation is owned by the [TURZX integration reference](development/devices/turzx-12.3.md).
- **Connection:** Windows WinUSB. The reviewed unit remains a hardware acceptance fixture; other catalog-listed products are candidates under the common admission contract, not guaranteed compatible models.

The integration reference owns current hardware-control and recovery limits. To connect, adopt and manage a supported display, follow [Physical Devices](outputs/physical_devices.md). The [recognition-only Device tabs](outputs/physical_devices.md#passive-usb-family-recognition) can also show researched families without an integrated protocol; their visibility does not extend this support list. For hardware-free Windows panels, use [Desktop](outputs/desktop.md).

See [Compatibility & Testing](compatibility.md) for tested Windows configurations and the [device integration reference](development/devices/trofeo-vision-9.16.md) for protocol and implementation details.
