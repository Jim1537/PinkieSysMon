# Compatibility & Testing

## Status and scope

Pinkie's System Monitor is considered release-ready by its author for the originally accepted workstation setup. Evidence remains limited and is **not** a guarantee of compatibility with other Windows builds, hardware, or USB controllers.

**Test update: October 10, 2026.** Candidate `1.20.0` from integration commit [`4f97717`](https://github.com/Jim1537/PinkieSysMon/commit/4f977170de284d91c9e8ea3398b538019f322b59) completed a Windows build/regression run and a **basic portable/physical-display smoke test on a second PC**. This is a **candidate build**, not proof of completed USB lifecycle acceptance or an independently verified GitHub Release.

## Observed Windows configurations

These are tested environments, **not minimum hardware specifications**.

| Environment | Windows configuration | Candidate and observed results |
| --- | --- | --- |
| Development workstation | Windows 11 **Pro** x64, **26H2**, OS `10.0.26300`, build **26300.9457** (previously recorded snapshot) | `1.20.0`: Windows Runtime/Editor publish **PASS**, local deployment **PASS**, and **167/167 regressions PASS**. Physical Trofeo lifecycle acceptance for this candidate **not performed** here. |
| Second workstation | Windows 11 **Home** x64, **26H2**, OS `10.0.26300`, build **26300.9550** (reported October 10, 2026) | `1.20.0` portable ZIP: Runtime and Editor worked, public `Default` dashboard rendered, a new dashboard was created/edited, iCUE Sensor Logging metrics worked **without Libre Hardware Monitor installed**, and a connected **Trofeo Vision 9.16** showed the expected output. **Basic functional smoke test PASS**; bundled root shortcuts failed (see below). |

The two systems differ in Windows edition and cumulative-update build. This comparison **does not establish** whether that difference affects USB stability.

## Known limitations of this evidence

- This was a **brief basic functional test**, not a controlled stress/soak test. **No immediate system crash** was observed on the second workstation, but neither sustained reliability nor a cause of the separate [PinkiePie platform instability investigation (#17)](https://github.com/Jim1537/PinkiePie/issues/17) has been established.
- Physical lifecycle scenarios — **unplug/replug**, **suspend/resume**, **explicitly configured device missing**, and **two compatible devices** — have **not passed physical acceptance**. They remain tracked in [Issues #33–36](https://github.com/Jim1537/PinkieSysMon/issues).
- Safe cancellation of blocked native WinUSB I/O remains a separate acceptance question under [Issue #37](https://github.com/Jim1537/PinkieSysMon/issues/37); passing deterministic regressions does not establish driver-level completion behavior.
- In the tested portable `1.20.0`, the two bundled root **`.lnk` shortcuts did not work after relocation**. The applications themselves launched successfully by other means; see [Issue #52](https://github.com/Jim1537/PinkieSysMon/issues/52).
- Direct hardware output supports only explicitly [listed devices](supported-devices.md). No compatibility with similar-looking models or other firmware is implied.

## Community Testing & Feedback

Additional independent reports remain valuable, including failures. Please [open a GitHub issue](https://github.com/Jim1537/PinkieSysMon/issues/new) with the Windows edition, release and OS build, hardware and output-device model, PinkieSysMon version, enabled telemetry providers, and exact actions/results.

Exclude personal information, device serial numbers, credentials, and private host logs.
