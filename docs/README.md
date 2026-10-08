# Pinkie's System Monitor

![banner_001.png](images/banner_001.png)

**Project:** Pinkie's System Monitor  
**Key:** `PinkieSysMon`  
**Date:** `2026-10-08`  
**Revision:** `2`  
**Repository:** [GitHub](https://github.com/Jim1537/PinkieSysMon)

**Pinkie's System Monitor** is a native Windows application for creating and displaying customizable hardware dashboards with system telemetry. It is designed for setups where a small telemetry display is installed inside or next to a PC and you want a lightweight, fully local solution with its own editor and direct control of the device.

PinkieSysMon consists of two main components:

- **PinkieSysMon Runtime** — a background application that collects telemetry, renders the active dashboard, and manages output to the device;
- **PinkieSysMon Dashboard Editor** — a visual editor for creating and configuring dashboards.

The project is designed for fully local operation.

## Key Features

- fully native dashboard rendering on Windows;
- a dedicated visual dashboard editor;
- free placement, scaling, and rotation of elements;
- layers and element groups;
- multi-selection, drag-and-drop, Undo/Redo, and grid-based positioning;
- background and foreground image layers;
- support for static and animated images;
- numeric and text formatting;
- unit conversion;
- ranges, thresholds, and state-based visual highlighting;
- customizable fonts, colors, opacity, outlines, and other presentation settings;
- dashboard switching without rebuilding the application;
- Runtime operation from the system tray;
- fully local operation with no cloud dependency;
- self-contained x64 deployment — no separate .NET Runtime installation is required for published builds.


## Telemetry Providers

PinkieSysMon combines multiple data sources into a unified metrics system.

- **[System](telemetry/system.md)** — the built-in Windows provider. It collects operating system, network, power, audio/media, and Runtime telemetry directly through Windows and the application itself, without requiring third-party monitoring software.
- **[Libre Hardware Monitor](telemetry/libre-hardware-monitor.md)** — an optional provider for extended hardware telemetry. PinkieSysMon reads sensors exposed by an already-running Libre Hardware Monitor instance through its local HTTP API.
- **[iCUE Sensor Logging](telemetry/icue-sensor-logging.md)** — an optional provider for Corsair hardware telemetry. PinkieSysMon reads Corsair iCUE Sensor Logging files in read-only mode and does not interfere with hardware control performed by iCUE.

## Available Widgets

- **[Text / Value](widgets/text-value.md)** — displays a dynamic numeric or text value. Supports formatting, units, prefix/suffix text, fallback text, and customizable text presentation.
- **[Binary](widgets/binary.md)** — displays a value with two logical states. Each state can have its own presentation, such as text, an icon, or an image.
- **[Gauge](widgets/gauge.md)** — displays a numeric value as a gauge. Intended for temperatures, load, clock speeds, battery level, and other values with a defined range.
- **[Bar](widgets/bar.md)** — displays a value as a linear bar within a defined range. Useful for progress-style indicators, resource usage, levels, and other quantitative values.
- **[Image](widgets/image.md)** — a standalone graphical element for static or animated images and decorative dashboard content.
- **[Power](widgets/power.md)** — a specialized representation of battery or UPS state, including AC power, battery operation, charging, low or critical charge, and other power states.
- **[Media System](widgets/media-system.md)** — displays the type and availability of the current audio device, such as speakers, headphones, microphones, display audio, S/PDIF, and other media endpoints.
- **[Media Player](widgets/media-player.md)** — represents media playback state: Playing, Paused, Stopped, or Unavailable. It can be combined with media metrics to build current-playback elements.

For widget configuration details, see [Widget Properties — Editor Tabs](widgets/properties-by-tab.md) and [Widget Property Dictionary](widgets/properties.md).

## Dashboard Editor

**PinkieSysMon Dashboard Editor** is designed for creating dashboards without manually editing their internal files.

The editor allows you to:

- add and remove widgets;
- move and resize them directly on the canvas;
- rotate elements;
- select multiple elements at once;
- apply shared changes to a group of selected elements;
- organize elements into nested groups and layers;
- change Z-order;
- use a grid for precise positioning;
- use Undo and Redo;
- configure properties through contextual property panels;
- select available telemetry metrics;
- work with images and a shared icon library;
- use System, Light, and Dark interface themes;
- save dashboards and apply changes to Runtime.

A dashboard is a self-contained layout and presentation configuration. You can create multiple dashboards for different purposes or visual styles and switch between them as needed.

## Runtime

**PinkieSysMon Runtime** is designed for continuous background operation.

After startup, Runtime:

- loads the selected dashboard;
- determines which telemetry metrics it needs;
- retrieves current values from available sources;
- renders the dashboard;
- sends the rendered frame to the Trofeo;
- monitors output status;
- allows dashboard switching and access to the Editor from the system tray.

Configuration files, dashboards, and user assets are stored separately from the application binaries, so updating the program does not require rebuilding or recreating your dashboard.

## Supported Device

At this stage, direct hardware output is implemented for:

- **Thermalright Trofeo Vision 9.16**
- native resolution: **1920 × 480**
- connection: **USB / WinUSB**

The Trofeo is used as a specialized frame device rather than as an additional Windows display.

Support for other displays requires a dedicated output transport and should not be assumed simply because another device has a similar resolution or USB connection.

## System Requirements

- 64-bit Windows;
- Windows 11 x64 is recommended as the primary supported desktop platform; Windows 10 is supported only in configurations compatible with the current .NET 10 runtime;
- a compatible **Thermalright Trofeo Vision 9.16** for direct hardware output.

The following software is optional and is only required for the corresponding telemetry source:

- **Libre Hardware Monitor**, running with its local Remote Web Server enabled, for extended hardware telemetry;
- **Corsair iCUE**, with Sensor Logging enabled, for Corsair hardware telemetry.

Published Runtime and Editor builds are self-contained x64 applications.

## Documentation

- [Building, deployment, and development](development/building.md)
- [System provider](telemetry/system.md)
- [Libre Hardware Monitor](telemetry/libre-hardware-monitor.md)
- [iCUE Sensor Logging](telemetry/icue-sensor-logging.md)
- [Widget reference](widgets/text-value.md)
- [Widget property dictionary](widgets/properties.md)


PinkieSysMon is an independent fan-made project and is not affiliated with or endorsed by Hasbro. The project source code is distributed under the [MIT License](https://github.com/Jim1537/PinkieSysMon/blob/main/LICENSE).
