# Accessibility

PinkieSysMon is a Windows application for creating and displaying visual telemetry dashboards. This statement describes the project's accessibility status, limitations, and reporting channel. It does not certify compliance with an accessibility standard.

## Our approach

We welcome specific reports of accessibility barriers and assess possible improvements according to their impact, technical feasibility, and available project resources. No formal accessibility audit, WCAG conformance level, or guaranteed response or remediation deadline has been established.

## Supported environments

- **Windows 11 x64** is the primary tested application platform. Windows 10 x64 compatibility depends on the supported .NET/Windows environment; see [Compatibility & Testing](docs/compatibility.md).
- PinkieSysMon displays dashboards in native Windows Desktop panels or on [supported USB devices](docs/supported-devices.md). Device display accessibility may have hardware-dependent limits.
- The application interface is currently available in **English only**.

## Available customization

Dashboard authors can configure visual properties such as text size, colors, and layout; the Editor also offers interface themes. Desktop panel positions can be adjusted in Settings rather than only by dragging. These are [documented features](docs/README.md), **not** evidence that assistive technologies are fully supported.

## Known limitations and unverified areas

- Dashboard output is primarily visual. An accessible, nonvisual equivalent for every widget and hardware-display output has not been established.
- End-to-end keyboard-only operation, screen-reader compatibility (including Windows Narrator, NVDA, and JAWS), focus behavior, Windows high-contrast/text scaling, color contrast, and reduced-motion behavior have **not been comprehensively assessed**. Some workflows may present barriers.
- Accessibility of custom dashboard images and animations, and of optional physical displays, has not been independently verified.

## Report an accessibility barrier

Please [open a GitHub Issue](https://github.com/Jim1537/PinkieSysMon/issues/new/choose) using **Bug report** or a blank Issue. Explain what you were trying to do, what prevented you from doing it, and what behavior you expected. When available, include reproducible steps, the PinkieSysMon and Windows versions, and any assistive technology used. Screenshots or recordings are optional.

Do not share credentials, private configurations, device serial numbers, or unsanitized logs. Reports can be assessed and tracked, but filing one does not establish a promised fix or timeline.
