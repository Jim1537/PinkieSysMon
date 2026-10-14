# Pinkie's System Monitor

![Pinkie's System Monitor banner](images/banner_001.png)

<p align="center">
  <a href="https://github.com/Jim1537/PinkieSysMon/releases/latest"><img src="images/download-windows.svg" alt="Download Pinkie's System Monitor for Windows" width="272" height="54"></a>
  <a href="https://ko-fi.com/Z5X220SWVK"><img src="images/support-kofi.svg" alt="Support Pinkie's System Monitor on Ko-fi" width="272" height="54"></a><br>
  <strong>Windows x64 · Portable ZIP · No separate .NET Runtime required</strong><br>
  <a href="https://github.com/Jim1537/PinkieSysMon/releases">All releases</a> ·
  <a href="https://github.com/Jim1537/PinkieSysMon">Source on GitHub</a>
</p>

**Pinkie's System Monitor** is a fully local Windows application for building telemetry dashboards and displaying them on independent Windows Desktop panels or [supported USB devices](supported-devices.md). It includes a background **Runtime** and a separate **Dashboard Editor**.

For tested Windows configurations see [Compatibility & Testing](compatibility.md); for known defects and outstanding work see [GitHub Issues](https://github.com/Jim1537/PinkieSysMon/issues).

## Key Features

- Native dashboard rendering with visual layout editing, layers, groups, and Undo/Redo.
- Flexible text, gauges, bars, images, and state-based widgets, including animated assets.
- Customizable colors, images, thresholds, units, and text formatting.
- Independent Desktop and physical-device Outputs with per-Output dashboard assignments and tray controls.
- System telemetry plus optional external sensor providers.
- Portable, self-contained Windows x64 distribution without cloud services.

## Telemetry Providers

- **[System](telemetry/system.md)** — built-in Windows, network, power, media, and Runtime metrics; no external monitoring program required.
- **[Libre Hardware Monitor](telemetry/libre-hardware-monitor.md)** — optional local HTTP hardware-sensor source; Libre Hardware Monitor runs separately.
- **[iCUE Sensor Logging](telemetry/icue-sensor-logging.md)** — optional read-only metrics from Corsair iCUE Sensor Logging files.

## Available Widgets

- **[Text / Value](widgets/text-value.md)** — formatted text or live metrics.
- **[Binary](widgets/binary.md)** — two-state values with configurable text, icons, or images.
- **[Gauge](widgets/gauge.md)** — radial numeric indicator with ranges and thresholds.
- **[Bar](widgets/bar.md)** — linear numeric indicator with fill or image progress.
- **[Image](widgets/image.md)** — static or animated images and icons.
- **[Power](widgets/power.md)** — battery and UPS states.
- **[Media System](widgets/media-system.md)** — audio endpoint type and availability.
- **[Media Player](widgets/media-player.md)** — media playback state.

The [Properties by Tab](widgets/properties-by-tab.md) and [Property Dictionary](widgets/properties.md) describe Editor settings and their supported values.

## Dashboard Editor

The visual Editor supports widget placement, resizing, grouping, contextual property editing, metric selection, and live previews. Dashboards are saved separately from the application binaries.

In **Edit → Settings**, hover over the question-mark icon beside a parameter for a short explanation of its effect and dependencies. General, Desktop and Device settings include these hints; the same help is available over their controls.

<p align="center">
  <a href="images/PinkeySysMon_editor.png">
    <img src="images/PinkeySysMon_editor.png" alt="PinkieSysMon Dashboard Editor" width="50%">
  </a>
</p>

## Outputs and Runtime

Use the [Desktop guide](outputs/desktop.md) to create, configure, move, stack and manage independent Windows panels.

Use the [Physical Devices guide](outputs/physical_devices.md) for supported hardware discovery, adoption, Device Settings and output management.

Runtime loads each active Output's assigned saved dashboard and collects the metrics it needs. Physical Outputs receive JPEG frames over USB; Desktop Outputs render through native layered Windows panels with per-pixel alpha support. The tray provides independent Output status and Start/Reload/Stop controls; the Editor can apply dashboard choices to individual Outputs. Saving a dashboard in the Editor does not silently replace the Runtime's loaded document; apply or reload it explicitly.

## System Requirements

- 64-bit Windows; Windows 11 x64 is the primary tested platform. Compatible Windows 10 configurations depend on the .NET 10 Windows runtime requirements.
- No extra display hardware for Desktop-only Outputs. A [supported device](supported-devices.md) is required only for direct USB output.
- Optional Libre Hardware Monitor (local Remote Web Server) or Corsair iCUE (Sensor Logging) for their respective metrics.

Published Runtime and Editor builds are self-contained and require no separate .NET Runtime installation.

PinkieSysMon was developed through [AI-assisted vibe coding](development/vibe-coding.md). It is an independent fan-made project, not affiliated with or endorsed by Hasbro. Source code is released under the [MIT License](https://github.com/Jim1537/PinkieSysMon/blob/main/LICENSE).
