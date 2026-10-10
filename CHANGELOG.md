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

**Status:** Runtime and physical Trofeo validation of this candidate remain pending.
