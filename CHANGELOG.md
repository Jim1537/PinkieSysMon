# Changelog

Notable changes to PinkieSysMon, newest first.

## [Unreleased]

### Added
- **Shortcut widget:** Canonical Shell target/arguments, shared artwork, optional styled label and selective Desktop input regions; schema 26 ([#101](https://github.com/Jim1537/PinkieSysMon/issues/101)).

### Fixed
- **Shortcut Label:** An explicit Editor Target edit suggests a filename-based caption only when Label is empty, preserving nonempty captions and existing saved boards ([#101](https://github.com/Jim1537/PinkieSysMon/issues/101)).
- **Runtime tray:** Left-button double-click on the notification-area icon opens Control Center through its existing menu action ([#85](https://github.com/Jim1537/PinkieSysMon/issues/85)).

## [2.46.239] - 2026-10-17 (development build)

This [published development release](https://github.com/Jim1537/PinkieSysMon/releases/tag/2.46.239) records the major v2 snapshot since the 1.20.0 test candidate. Its release notes identify source commit [`dc6ba63`](https://github.com/Jim1537/PinkieSysMon/commit/dc6ba63b3152ee3f86ea5d372267a4f9938107ea). Later `main` changes belong under **Unreleased**.

### Added
- **Architecture — independent Outputs:** Run multiple native Desktop panels and supported physical USB displays with separately assigned dashboards, stable Output IDs, refresh intervals, enablement, and lifecycles. A single dashboard can be shared by multiple Outputs ([#58](https://github.com/Jim1537/PinkieSysMon/issues/58)).
- **Desktop panels:** Per-pixel transparency, saved position and stacking order, drag-to-move, click-through Lock, and independent enable/disable controls; a hardware display is no longer required ([#58](https://github.com/Jim1537/PinkieSysMon/issues/58)).
- **Telemetry and device ownership:** Deduplicated demand-driven telemetry across active dashboards, independent Output-specific status/performance metrics, passive device discovery, and explicit consent before controlling physical hardware ([#58](https://github.com/Jim1537/PinkieSysMon/issues/58)).
- **Runtime inactivity control:** Optional Windows user-idle pause suspends telemetry collection and Desktop/physical frame generation, then resumes on activity, honoring the applicable Windows display-idle limit ([#60](https://github.com/Jim1537/PinkieSysMon/issues/60)).
- **Portable Launcher:** A location-independent launcher for Control Center, Runtime, or both, with a shared About window and optional Desktop/Start Menu shortcuts ([#52](https://github.com/Jim1537/PinkieSysMon/issues/52), [#64](https://github.com/Jim1537/PinkieSysMon/issues/64), [#65](https://github.com/Jim1537/PinkieSysMon/issues/65)).
- **Editor — cross-dashboard clipboard:** Copy and paste configured widgets between dashboards and concurrent Editor instances, including explicit handling of dashboard-local image assets ([#70](https://github.com/Jim1537/PinkieSysMon/issues/70)).
- **Date/time widgets:** Calendar and clock components for Gauge, Bar, and Binary evaluation, including numeric comparisons in Binary ([#66](https://github.com/Jim1537/PinkieSysMon/issues/66), [#81](https://github.com/Jim1537/PinkieSysMon/issues/81)).
- **Developer workflow:** Interactive `builder.ps1` with independent pull, build, regression, local publish, and push actions, reusable compiled artifacts, and a per-session `builder.log` ([#83](https://github.com/Jim1537/PinkieSysMon/issues/83)).

### Changed
- **Editor interface:** Split Editor, Desktop, and Physical Devices settings into independent dialogs; moved Desktop and Devices into the main menu and Outputs to the toolbar. Output controls include contextual help and dashboard-aware Start/Reload actions ([#58](https://github.com/Jim1537/PinkieSysMon/issues/58), [#82](https://github.com/Jim1537/PinkieSysMon/issues/82)).
- **Runtime controls:** Reorganized the notification-area menu around Desktop and physical Outputs, with consistent Enabled controls, target-specific reloads, status, and action feedback ([#63](https://github.com/Jim1537/PinkieSysMon/issues/63)).
- **Desktop window placement:** Use independent non-topmost Windows panels with best-effort placement near the back of normal windows, rather than Explorer-owned wallpaper windows. Permanent positioning behind every other window or desktop icon is **not guaranteed** ([#78](https://github.com/Jim1537/PinkieSysMon/issues/78)).
- **Configuration and telemetry compatibility:** Replaced the former single-active-dashboard assumptions with per-Output configuration; retired display-specific `system.runtime.*` counters in favor of `system.output.<OutputId>.*`. Existing `system.runtime.version` remains application-wide ([#58](https://github.com/Jim1537/PinkieSysMon/issues/58)).
- **Build versioning and packaging:** Introduced automatic `vA.B.C` numbering based on the approved major version, closed Issue count, and main-branch commit count. Build, regression, and local publishing now use one shared executor without mandatory double compilation ([#83](https://github.com/Jim1537/PinkieSysMon/issues/83)).

### Removed
- Bundled root `.lnk` launchers, redundant Runtime per-Output Stop/Unload state, the legacy interactive build menu, and the superseded `pull-build.ps1` wrapper ([#52](https://github.com/Jim1537/PinkieSysMon/issues/52), [#63](https://github.com/Jim1537/PinkieSysMon/issues/63), [#83](https://github.com/Jim1537/PinkieSysMon/issues/83)).

### Fixed
- **Editor history:** Property-panel edits now participate in dashboard Undo/Redo ([#80](https://github.com/Jim1537/PinkieSysMon/issues/80)).
- **Desktop and Output reliability:** Addressed Win+D stacking recovery, taskbar visibility, first-frame telemetry refresh, and live Output-menu synchronization ([#78](https://github.com/Jim1537/PinkieSysMon/issues/78), [#69](https://github.com/Jim1537/PinkieSysMon/issues/69), [#72](https://github.com/Jim1537/PinkieSysMon/issues/72), [#73](https://github.com/Jim1537/PinkieSysMon/issues/73)).
- **Other fixes:** Resolved assorted Editor property/dropdown/font, Desktop Settings selection, widget image-mode, launcher lifecycle, image rollback, and Output shutdown issues ([#32](https://github.com/Jim1537/PinkieSysMon/issues/32), [#67](https://github.com/Jim1537/PinkieSysMon/issues/67), [#68](https://github.com/Jim1537/PinkieSysMon/issues/68), [#75](https://github.com/Jim1537/PinkieSysMon/issues/75), [#76](https://github.com/Jim1537/PinkieSysMon/issues/76)).

## [1.20.0] - 2026-10-10 (test candidate)

### Added
- Optional bounded USB diagnostic journal and expanded transport/lifecycle regression checks.
- Safe GitHub branch synchronization (`build.ps1 -Action Sync`).

### Changed
- Trofeo WinUSB transfers now use overlapped I/O with cancellation and completion draining.
- Output workers own transport lifetime; lifecycle operations no longer hold shared locks during USB I/O.
- GitHub publishing checks for newer remote commits before pushing.

**Validation:** Windows build and 167/167 deterministic regressions passed. Runtime, Editor, iCUE telemetry and basic physical Trofeo display were smoke-tested on a second workstation; USB lifecycle and sustained stability remain unverified. See [Compatibility & Testing](docs/compatibility.md).
