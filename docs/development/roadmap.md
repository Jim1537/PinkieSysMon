# Development Roadmap

Pinkie's System Monitor evolved through a sequence of small development passes. This page summarizes the **accepted historical milestones** and links to **GitHub Issues** for follow-up work.

**Historical review date: October 8, 2026.** Dates below describe documented engineering and acceptance, not necessarily releases or the creation dates of imported Issues. **Completed** applies only to the stated scope; source/regression acceptance is not a substitute for real-device testing. Individual task states and acceptance criteria belong to the [Issues tracker](https://github.com/Jim1537/PinkieSysMon/issues).

## Historical milestones

### September 28–30, 2026 — Editor and dashboard foundation

**Completed — PASS 1–6.**

The early passes established whole-pixel Editor geometry, persistent Layers tree scroll position, unified grid control, stable context-disabled properties, multi-selection editing, an interactive date/time formatting editor, and a broader global icon asset pipeline.

Canvas became a first-class configurable surface. Its size and orientation ceased to be hard-coded to a particular output device, while background and foreground images adopted the shared image-layer model. The accepted migration lineage reached **dashboard schema 18**.

**References:** [geometry (#15)](https://github.com/Jim1537/PinkieSysMon/issues/15), [Layers (#17)](https://github.com/Jim1537/PinkieSysMon/issues/17), [icons (#19)](https://github.com/Jim1537/PinkieSysMon/issues/19), [property applicability (#22)](https://github.com/Jim1537/PinkieSysMon/issues/22), [multi-selection (#23)](https://github.com/Jim1537/PinkieSysMon/issues/23), [Canvas (#21)](https://github.com/Jim1537/PinkieSysMon/issues/21).

### Late September–early October 2026 — Device-oriented output

**Partially completed — PASS 7 software and UI accepted; physical acceptance outstanding.**

Device enumeration and explicit target identity replaced implicit first-compatible-device selection. Named output targets, independent sessions, device-scoped IPC and commands, transaction-safe reconfiguration, and the device-oriented Screen/Output menu were implemented. An absent or ambiguous target must not be silently redirected to another device.

The **software/UI portion** closed under [BUG-012 (#26)](https://github.com/Jim1537/PinkieSysMon/issues/26). Four separate **physical acceptance scenarios remain tracked**, even though this software issue is closed: see [Current work](#current-work) and the [Trofeo integration reference](devices/trofeo-vision-9.16.md).

### September 30–October 1, 2026 — Windows integration and reliability

**Completed for the historically accepted PASS 8 / PASS 9A–9D scopes.**

The Windows shell adopted supported native title-bar mechanisms; the redundant Group menu was removed; notification delivery, local development signing and Editor themes were accepted on the original workstation after iterations.

Reliability work addressed stale inactive telemetry, rollback during configuration and output reconfiguration, controlled exception handling, and shutdown/disposal semantics. These were **specific historical software/workstation acceptance results**, not a claim that later blocked-USB or platform failures were eliminated.

**References:** [notifications (#16)](https://github.com/Jim1537/PinkieSysMon/issues/16), [Editor shell (#25)](https://github.com/Jim1537/PinkieSysMon/issues/25), [focus behavior (#27)](https://github.com/Jim1537/PinkieSysMon/issues/27), [Output-menu wording (#29)](https://github.com/Jim1537/PinkieSysMon/issues/29), [Stop/Unload behavior (#31)](https://github.com/Jim1537/PinkieSysMon/issues/31).

### October 1–4, 2026 — Rendering efficiency and canonical property model

**Completed for the October 4 accepted schema-19 baseline; hardware acceptance separate.**

Rendering passes introduced stateful visual caching and reduced per-frame allocations/snapshots. Later source and regression contracts include JPEG zero-copy transport and stateful renderer unification; these contracts alone do **not** prove any particular USB throughput or system stability.

The major persisted-data milestone was **dashboard schema 19**: canonical typed widgets and property/source contracts, explicit schema 18 → 19 migration, migration idempotence, and consistent Runtime/Editor semantics. The private production dashboard was migrated and accepted for this historical baseline.

**Historical acceptance recorded on October 4:** the user's Windows `build.ps1` compilation **PASS**, **153/153 regressions PASS**, and **Runtime/Visual PASS** for the accepted schema-19 production board. This records an **archived baseline**, **not** a new compile, regression run, or physical-device PASS for the current GitHub `main`.

**References:** [property-model normalization (#28)](https://github.com/Jim1537/PinkieSysMon/issues/28), [widget reference rebuild (#30)](https://github.com/Jim1537/PinkieSysMon/issues/30), [schema migration source](https://github.com/Jim1537/PinkieSysMon/blob/main/src/PinkieSysMon/DashboardSchemaMigration.cs).

### October 8, 2026 — Public documentation and issue tracking

**Completed for the documented publishing and tracker-migration scope.**

MkDocs Material documentation was published. Telemetry and widget reference material, hardware compatibility and device-integration notes were organized; general documentation became device-neutral. Historical closed defects and outstanding issues were migrated into GitHub Issues.

**References:** [MkDocs migration (PR #1)](https://github.com/Jim1537/PinkieSysMon/pull/1), [Supported Devices and integration notes (PR #12)](https://github.com/Jim1537/PinkieSysMon/pull/12), [device-neutral documentation (PR #13)](https://github.com/Jim1537/PinkieSysMon/pull/13), [sidebar organization (PR #14)](https://github.com/Jim1537/PinkieSysMon/pull/14).

## Current work

These are the workstreams that were outstanding at the October 8 review. **Follow their linked GitHub Issue states for current status; this page does not maintain a second issue tracker.** No completion dates are promised.

| Workstream | Source of current status |
| --- | --- |
| Runtime asset correctness | [BUG-015: deleted image reference after Reload (#32)](https://github.com/Jim1537/PinkieSysMon/issues/32) |
| Physical output acceptance (PASS 7) | [Unplug/replug (#33)](https://github.com/Jim1537/PinkieSysMon/issues/33), [suspend/resume (#34)](https://github.com/Jim1537/PinkieSysMon/issues/34), [configured device missing (#35)](https://github.com/Jim1537/PinkieSysMon/issues/35), [multiple-device identity (#36)](https://github.com/Jim1537/PinkieSysMon/issues/36) |
| USB worker lifetime | [Blocking WinUSB I/O and shutdown/reconfiguration (#37)](https://github.com/Jim1537/PinkieSysMon/issues/37) |
| Platform diagnostics | [USB/platform instability investigation (#38)](https://github.com/Jim1537/PinkieSysMon/issues/38); **software causality is not established** |

The historical [BUG-004 Property Editor redesign (#18)](https://github.com/Jim1537/PinkieSysMon/issues/18) was closed as **not planned / obsolete** on October 8 by the author's decision. It is not an outstanding roadmap milestone.

## Maintenance principle

This page owns **milestone history and direction**, not day-to-day status, bug reproduction steps, or acceptance checklists. Keep those details in [GitHub Issues](https://github.com/Jim1537/PinkieSysMon/issues), and refer to source, accepted test evidence, or pull requests when recording a new milestone. Never turn successful compilation or regressions into unverified physical-device acceptance. Private production dashboard assets remain outside this public history.
