# Compatibility & Testing

## Status and scope

Pinkie's System Monitor is considered release-ready by its author for the originally accepted workstation setup. Evidence remains limited and is **not** a guarantee of compatibility with other Windows builds, hardware, or USB controllers.

**Test update: October 10, 2026.** PinkieSysMon `1.20.0` passed Windows build/regression checks and a basic physical-display smoke test on a second computer. USB lifecycle and long-term stability acceptance remain outstanding.

## Tested Configuration

These are confirmed test configurations, **not minimum hardware requirements**:

| Component | Tested configuration |
| --- | --- |
| Computer | Two Windows 11 x64 workstations |
| Operating system | Windows 11 Pro x64, 26H2 (OS build 26300.9457) |
| Operating system | Windows 11 Home x64, 26H2 (OS build 26300.9550) |
| Output hardware | [Thermalright Trofeo Vision 9.16](supported-devices.md) |

## Known limitations of this evidence

- This was a **brief basic functional test**, not a controlled stress/soak test. **No immediate system crash** was observed on the second workstation, but neither sustained reliability nor a cause of the separate [PinkiePie platform instability investigation (#17)](https://github.com/Jim1537/PinkiePie/issues/17) has been established.
- Physical lifecycle scenarios — **unplug/replug**, **suspend/resume**, **explicitly configured device missing**, and **two compatible devices** — have **not passed physical acceptance**. They remain tracked in [Issues #33–36](https://github.com/Jim1537/PinkieSysMon/issues).
- Safe cancellation of blocked native WinUSB I/O remains a separate acceptance question under [Issue #37](https://github.com/Jim1537/PinkieSysMon/issues/37); passing deterministic regressions does not establish driver-level completion behavior.
- In the tested portable `1.20.0`, the two bundled root **`.lnk` shortcuts did not work after relocation**. The applications themselves launched successfully by other means; see [Issue #52](https://github.com/Jim1537/PinkieSysMon/issues/52).
- Direct hardware output supports only explicitly [listed devices](supported-devices.md). No compatibility with similar-looking models or other firmware is implied.

## Community Testing & Feedback

Additional independent reports remain valuable, including failures. Please [open a GitHub issue](https://github.com/Jim1537/PinkieSysMon/issues/new) with the Windows edition, release and OS build, hardware and output-device model, PinkieSysMon version, enabled telemetry providers, and exact actions/results.

Exclude personal information, device serial numbers, credentials, and private host logs.
