# Compatibility & Testing

## Release Status

**Pinkie's System Monitor is considered release-ready by its author** and is used on the author's own system. It is an independent, personal software project, however, and its real-world compatibility testing is currently limited to a single workstation and display setup.

A release-ready status reflects the author's experience with that setup; it is **not a claim of broad compatibility across Windows hardware or display models**.

## Tested Configuration

The following is the **one confirmed real-world test environment**, not a minimum or recommended hardware specification:

| Component | Tested configuration |
| --- | --- |
| Computer | One Windows 11 Pro workstation |
| Operating system | Windows 11 Pro x64, version 25H2 (OS build 26200.9457, recorded during diagnostics) |
| Display | **Thermalright Trofeo Vision 9.16** |
| Display resolution | **1920 × 480** |
| Display interface | **USB / WinUSB** |

The OS build shown above is a recorded snapshot, not a guarantee that the author's PC still runs that exact build. More detailed CPU, motherboard, memory, and GPU specifications are not included here because they have not yet been confirmed for this published test record.

## Compatibility Limitations

- Testing on **other Windows computers, Windows versions, USB controllers, and hardware configurations** has not yet been independently verified.
- The fact that the application runs on the author's computer does not guarantee identical results on another system.
- Direct display output is currently implemented for the **Thermalright Trofeo Vision 9.16**. Other display models should **not** be assumed compatible, even if they have a similar resolution or use USB.
- These limits describe the **available testing evidence**, not a declaration that other configurations cannot work.

## Community Testing & Feedback

**If you can test Pinkie's System Monitor on another computer or Windows configuration, please share your results — whether it works perfectly or you run into problems.** Both successful reports and bug reports are valuable.

Please [open a GitHub issue](https://github.com/Jim1537/PinkieSysMon/issues/new) and, where practical, include:

- the Windows edition, version, and OS build;
- CPU, motherboard, memory, and GPU information;
- the display model and how it is connected over USB;
- the PinkieSysMon version and any enabled optional telemetry providers;
- what worked, what failed, and the steps needed to reproduce a problem, if any.

Please remove personal information, device serial numbers, credentials, and other sensitive details from logs or screenshots before sharing.

Community reports will help establish which configurations work in practice and highlight hardware-specific problems that have not surfaced on the original test setup.
