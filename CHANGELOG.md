# Changelog

Notable changes to PinkieSysMon, newest first.

## [Unreleased]

## [1.20.0] - 2026-10-10 (test candidate)

### Added
- Optional bounded USB diagnostic journal and expanded transport/lifecycle regression checks.
- Safe GitHub branch synchronization (`build.ps1 -Action Sync`).

### Changed
- Trofeo WinUSB transfers now use overlapped I/O with cancellation and completion draining.
- Output workers own transport lifetime; lifecycle operations no longer hold shared locks during USB I/O.
- GitHub publishing checks for newer remote commits before pushing.

**Validation:** Windows build and 167/167 deterministic regressions passed. Runtime, Editor, iCUE telemetry and basic physical Trofeo display were smoke-tested on a second workstation; USB lifecycle and sustained stability remain unverified. See [Compatibility & Testing](docs/compatibility.md).
