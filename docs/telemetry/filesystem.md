# Filesystem

The built-in **Filesystem** provider exposes metrics for individual files and directories explicitly registered by the user. Directory size is the **recursive logical sum of file lengths**, not allocated disk space. Existing widgets consume these metrics through the ordinary Metric selector; there is no dedicated File/Folder widget.

## User setup

1. Open **Edit → Editor settings → Metric providers**. Filesystem is **disabled by default**. Switching it on displays an explicit disk-activity warning; choose **Enable** or **Cancel**. It does not ban traditional HDDs.
2. Select **Sources...**, then **Add...**. Give the source a readable alias, choose **File** or **Directory**, and enter or browse to its absolute path.
3. Select **Check and Save**. For a directory, the Editor scans the entire tree using the same limits and traversal as Runtime. The progress indicator remains responsive; **Cancel** dismisses the check, though one blocked native filesystem call may finish later. A failed or incomplete check cannot admit a source. Alias-only edits retain the existing ID without rescanning.
4. Confirm the Sources dialog and then **Editor Settings → OK** to save and apply the whole settings draft. Cancelling either dialog leaves the corresponding unsaved changes behind.
5. In a widget's **Metric** selector, choose **Filesystem → source alias → metric**. Use ordinary widget units, formats and Fallback properties.

!!! warning "Recursive scanning can be expensive"
    Directory scans may generate significant disk I/O, especially on HDDs and network shares. Scanning is opt-in and only scheduled for metrics demanded by an active Output. A configured but unused directory is not scanned periodically. Editing or adding a source is the separate explicit preflight exception.

!!! info "Editor preview"
    The Editor performs **only explicit preflight**, not recurring Filesystem scans. When Runtime is available, the Editor obtains already-published values for configured, enabled Filesystem sources through the [read-only current-value snapshot boundary](../development/architecture.md#editor-versus-runtime-state). Runtime scans only sources demanded by an active Output: opening or editing a dashboard in Editor alone does not initiate a scan. If Runtime is absent, the source is disabled/removed, no scan has completed or the published reading is unavailable, the widget displays its normal Fallback. Creation and modification timestamps retain their native date/time values and use ordinary widget formatting.

## Metrics and identity

Each configured source has an immutable, opaque 32-character ID. Aliases and paths are configuration, **not metric identity**. A rename leaves existing dashboard bindings unchanged. The source's kind and path are explicitly validated before addition or material edits.

| Metric | ID pattern | Value | Meaning |
| --- | --- | --- | --- |
| Size | `filesystem.<id>.size` | `DataSize`, bytes | File length; or recursive sum of descendant file lengths for a directory |
| Created | `filesystem.<id>.created` | `DateTime` | Creation timestamp of the configured file or directory **itself**, in local time |
| Modified | `filesystem.<id>.modified` | `DateTime` | Last-write timestamp of the configured file or directory **itself**, in local time |
| Files | `filesystem.<id>.files` | `Number`, unitless | **Directories only:** total number of files at all descendant depths |
| Directories | `filesystem.<id>.directories` | `Number`, unitless | **Directories only:** number of nested directories at all depths, excluding the source root |

Directory timestamps **do not** represent the newest or oldest descendant. Directory size is not a disk-volume capacity or free-space reading. File sources expose only the original three metrics; folder counts are available only for Directory sources.

The two counts are collected **during the same full traversal** as size, with no extra scan. They exclude skipped reparse points (symlinks and junctions). Empty directories return zero; failed or incomplete traversals make both counts unavailable, just like size.

Source records live in `config/app.json` under `Filesystem.Sources` (`Id`, `Alias`, `Path`, `Kind`). Provider enablement is `MetricProviders["filesystem"]`. Existing configuration files load with an empty Sources list and Filesystem disabled. Dashboard JSON and its schema are not changed. Removed or disabled sources retain existing widget metric ID references as **unavailable**; they are not silently rebound to different paths.

## Scanning and availability

A common scanner validates a source before admission in Editor and whenever Runtime refreshes it:

- **15 elapsed seconds** and **250,000 encountered entries** per source are hard cooperative budgets. Both must be met by a *complete* traversal, with no access or metadata errors. Exactly 250,000 complete entries may pass; any further entry fails.
- Every encountered file, subdirectory and skipped reparse point counts. Directory junctions, symlinks and other reparse-point targets are never traversed. A root that is itself a reparse point is rejected.
- A file needs metadata validation but no recursive walk. The scanner reads file attributes, lengths and timestamps, **not payload bytes**.
- Missing or inaccessible paths, type mismatches, overflowing sums, access-denied children, enumeration failures, cancellation and exceeded budgets yield **unavailable (`null`)**, never a valid-looking partial size or count.
- A single blocked native Windows filesystem call is not forcibly interruptible. The deadline and cancellation are checked between operations; neither guarantees an operating-system I/O timeout.

Runtime has one serialized background scanner for the entire Filesystem provider. Multiple active Outputs requesting the same source share its result, and only demanded sources are scanned. The ordinary synchronous `IMetricSource.Capture()` returns a memory snapshot without walking directories or waiting on a scan. Runtime schedules each demanded source for a new scan **five minutes after its previous scan completes**, independently of widget redraw and normal metric snapshot-publication cadence. Refreshing a second source never creates a second overlapping scanner.

Before the first complete result, the metric is `null`. The last complete result may remain visible during a subsequent refresh. A completed failure **invalidates** the prior result and allows normal widget Fallback; later successful scans restore availability. Disabling the provider or removing dashboard demand clears active readings. Source records are retained across transient disappearance, mounting and permission problems.

Logical sizes can count hardlinks more than once; compressed or sparse files may consume less allocated space. The scan is not an atomic filesystem snapshot if contents change while it runs. Avoid using these values for backup verification or disk-capacity accounting.

## Privacy and diagnostics

A configured path is saved in the user's local application configuration. **Do not commit** personal source paths or copies of production `app.json` to the public repository. Scanner incidents log bounded reason/status information through the existing application logs, with repeated failures throttled. See [Application logging](../development/architecture.md#application-logging) and the [public repository boundary](../PUBLIC-REPOSITORY.md).

See [Application Architecture](../development/architecture.md#runtime-data-path) for Runtime/Editor state ownership, active Output demand, capture scheduling and shared `MetricStore` publication.
