# Compatibility & Testing

## Status and scope

Pinkie's System Monitor is considered release-ready by its author for the originally accepted workstation setup. Evidence remains limited and is **not** a guarantee of compatibility with other Windows builds, hardware, or USB controllers.

**Test update: October 10, 2026.** PinkieSysMon `1.20.0` passed Windows build/regression checks and a basic physical-display smoke test on a second computer. USB lifecycle and long-term stability acceptance remain outstanding.

## Tested Configuration

These are confirmed test configurations, **not minimum hardware requirements**:

| Component | Tested configuration |
| --- | --- |
| Computer | Two Windows 11 x64 workstations |
| Operating system | Windows 11 Pro x64, 26H2 (OS build 26300.9457)<br>Windows 11 Home x64, 26H2 (OS build 26300.9550) |
| Output hardware | [Thermalright Trofeo Vision 9.16](supported-devices.md) |

For current known issues and outstanding checks, see [GitHub Issues](https://github.com/Jim1537/PinkieSysMon/issues).

## Community Testing & Feedback

Additional independent reports remain valuable, including failures. Please [open a GitHub issue](https://github.com/Jim1537/PinkieSysMon/issues/new) with the Windows edition, release and OS build, hardware and output-device model, PinkieSysMon version, enabled telemetry providers, and exact actions/results.

Exclude personal information, device serial numbers, credentials, and private host logs.
