# Changelog

Notable changes to PinkieSysMon, newest first.

## [Unreleased]

### Added
- Portable root single-file Launcher with a shared Editor About window, project documentation link, independent Control Center/Runtime/Both actions, and opt-in idempotent Desktop/Start Menu shortcut management ([Issue #52](https://github.com/Jim1537/PinkieSysMon/issues/52); native portable relocation and Windows build not yet verified for this change).
- Independent, native Windows Desktop dashboard panels alongside explicitly enabled physical Trofeo outputs. Each Output has its own dashboard assignment, refresh interval, provider enablement, and lifecycle.
- Per-pixel desktop transparency, drag-and-persisted placement, full-window Lock click-through, ordering, and multiple independently controlled panels.
- General, Desktop and detected Device settings with context help, output-aware Editor Start/Reload, Runtime tray controls and command feedback.
- Independent `system.output.<OutputId>.*` metrics, shared demand-driven telemetry, passive physical-device discovery and opt-in hardware ownership.

### Changed
- Retired global display-specific `system.runtime.*` frame/USB counters without aliases; `system.runtime.version` remains application-wide. See [System telemetry](docs/telemetry/system.md#runtime-and-output-performance-metrics).
- Portable release staging replaces the two root `.lnk` entry points with a single versioned Launcher EXE that follows the existing optional signing policy; three user shortcuts are created only on request. Existing private configuration and dashboard preservation is unchanged.
- Windows build numbering and release packaging follow the verified [automatic version contract](docs/development/building.md#automatic-version-numbering).

### Validation and known limitations
- Owner-reported Windows build/regressions **242/242 PASS**, local publish and portable ZIP creation at source `c76fdabcb55592cefeb36a16f0a4de5c4a45daa5` (build **v2.19.158**). Earlier owner-observed Desktop, physical-device and resource checks have their individual scopes; the new Launcher source has **not** yet been compiled or tested on Windows. See [Issue #58](https://github.com/Jim1537/PinkieSysMon/issues/58) for dated acceptance and excluded hardware-risk scenarios.
- Historically, the included portable root `.lnk` shortcuts failed after relocation; the new Launcher replacement still needs owner Windows relocation validation before closing [Issue #52](https://github.com/Jim1537/PinkieSysMon/issues/52). Other deferred interface polish is tracked in [Issue #61](https://github.com/Jim1537/PinkieSysMon/issues/61).

## [1.20.0] - 2026-10-10 (test candidate)

### Added
- Optional bounded USB diagnostic journal and expanded transport/lifecycle regression checks.
- Safe GitHub branch synchronization (`build.ps1 -Action Sync`).

### Changed
- Trofeo WinUSB transfers now use overlapped I/O with cancellation and completion draining.
- Output workers own transport lifetime; lifecycle operations no longer hold shared locks during USB I/O.
- GitHub publishing checks for newer remote commits before pushing.

**Validation:** Windows build and 167/167 deterministic regressions passed. Runtime, Editor, iCUE telemetry and basic physical Trofeo display were smoke-tested on a second workstation; USB lifecycle and sustained stability remain unverified. See [Compatibility & Testing](docs/compatibility.md).
