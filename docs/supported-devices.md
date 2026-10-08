# Supported Devices

Pinkie's System Monitor currently implements direct hardware output for the device listed below. Compatibility is **model-specific**: a similar screen size, USB connector, or display appearance does not establish support.

## Thermalright Trofeo Vision 9.16

- **Support status:** Direct output implemented; used on the author's tested setup.
- **Native resolution:** 1920 × 480 pixels.
- **Connection:** USB using the Windows WinUSB interface.

The Trofeo Vision 9.16 is suitable for PinkieSysMon because it operates as a dedicated frame-output device rather than an additional Windows desktop monitor. PinkieSysMon renders the dashboard locally and transfers its JPEG frames directly through USB, allowing a wide, independent telemetry display without extending or mirroring the Windows desktop.

For verified system coverage and testing limitations, see [Compatibility & Testing](compatibility.md). For USB identification, protocol framing, lifecycle behavior, and implementation contracts, see the [Trofeo Vision 9.16 device integration reference](development/devices/trofeo-vision-9.16.md).
