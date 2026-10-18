# Supported Devices

Pinkie's System Monitor currently implements direct hardware output for the models listed below. Implementation and integration acceptance are identified separately. Compatibility is **model-specific**: a similar screen size, USB connector, or display appearance does not establish support.

## Thermalright Trofeo Vision 9.16

- **Support status:** Direct frame output implemented.
- **Native resolution:** 1920 × 480 pixels.
- **Connection:** USB using the Windows WinUSB interface.

The Trofeo Vision 9.16 is suitable for PinkieSysMon because it operates as a dedicated frame-output device rather than an additional Windows desktop monitor. PinkieSysMon renders the dashboard locally and transfers its JPEG frames directly through USB, allowing a wide, independent telemetry display without extending or mirroring the Windows desktop.

## TURZX 12.3-inch

- **Support status:** JPEG frame output and ordinary Settings/Runtime integration implemented. Remaining capability and recovery acceptance gates are tracked in [Issue #71](https://github.com/Jim1537/PinkieSysMon/issues/71).
- **Physical resolution:** 1920 × 720 pixels; model-specific wire geometry is owned by the [TURZX integration reference](development/devices/turzx-12.3.md).
- **Connection:** The exact Windows WinUSB interface for the reviewed model. Other Turing/TURZX sizes and products are not admitted by this adapter.

The integration reference owns current hardware-control and recovery limits. To connect, adopt and manage a supported display, follow [Physical Devices](outputs/physical_devices.md). The [recognition-only Device tabs](outputs/physical_devices.md#passive-usb-family-recognition) can also show researched families without an integrated protocol; their visibility does not extend this support list. For hardware-free Windows panels, use [Desktop](outputs/desktop.md).

See [Compatibility & Testing](compatibility.md) for tested Windows configurations and the [device integration reference](development/devices/trofeo-vision-9.16.md) for protocol and implementation details.
