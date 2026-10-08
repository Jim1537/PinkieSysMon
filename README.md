# Pinkie's System Monitor

**Documentation:** [Online documentation](https://jim1537.github.io/PinkieSysMon/) · [Markdown sources](docs/index.md)

PinkieSysMon is a native Windows hardware-dashboard application for small telemetry displays installed in or near a PC. It runs fully locally, renders dashboards with SkiaSharp, collects telemetry from Windows and optional external providers, and can send rendered frames directly to supported USB display hardware.

PinkieSysMon is an independent fan-made project and is not affiliated with or endorsed by Hasbro.

The project contains two applications:

- **PinkieSysMon Runtime** — background telemetry, dashboard rendering, system-tray control, and output-device management.
- **PinkieSysMon Dashboard Editor** — visual dashboard authoring with layers, groups, multi-selection, drag/resize/rotate editing, contextual properties, and live telemetry preview.

## Current capabilities

PinkieSysMon supports configurable dashboards with Value, Binary, Gauge, Bar, Image, Power, Media System, and Media Player widgets. Dashboard presentation supports fonts, colors, units, formatting, thresholds and state-based presentation, static and animated images, background/foreground image layers, grouping, and dashboard switching.

Telemetry is exposed through a unified metric model:

- **System** — Windows-native OS, uptime, integrated-GPU, network, power, audio/media, and runtime metrics.
- **Libre Hardware Monitor** — optional extended hardware telemetry from an already-running Libre Hardware Monitor instance through its local HTTP API. PinkieSysMon does not embed LHM or compete for privileged hardware access.
- **Corsair iCUE Sensor Logging** — optional read-only consumption of iCUE sensor-log data; PinkieSysMon does not take over iCUE hardware control.

## Supported output device

Direct hardware output is currently implemented for **Thermalright Trofeo Vision 9.16** at its native **1920 × 480** resolution over USB/WinUSB.

Other displays require their own output transport and should not be assumed compatible solely because they have a similar resolution or USB connection.

## Requirements

- 64-bit Windows.
- Windows 11 x64 is the primary supported desktop platform; compatible Windows 10 configurations are also supported by the current .NET 10 target.
- .NET SDK **10.0.401** to build from source.
- Thermalright Trofeo Vision 9.16 for direct hardware output.
- Optional: Libre Hardware Monitor with its local web server enabled on port `8085` for LHM telemetry.
- Optional: Corsair iCUE with Sensor Logging enabled for Corsair telemetry.

Published Runtime and Editor builds are self-contained x64 applications and do not require a separately installed .NET Runtime. PinkieSysMon currently ships an English-only UI; non-English satellite-resource directories are removed from the self-contained publish payload.

## Repository layout

```text
src/
  PinkieSysMon/                  Runtime and shared dashboard/rendering code
  PinkieSysMon.Editor/           Dashboard Editor

tests/
  PinkieSysMon.RegressionTests/  Deterministic regression suite

assets/
  fonts/Roboto/                  Redistributable Roboto font files
  icons/Lucide/                  Redistributable Lucide SVG icon set

src/PinkieSysMon/PinkieSysMon.ico  Project application icon

dashboards/Default/              Public default dashboard
config/app.json                  Public, machine-neutral defaults
tools/                           Local code-signing helpers
build.ps1                        Build, regression, local deploy, Git publish workflow
```

Build output is generated under `artifacts/` and is not tracked. Portable release ZIPs are generated under `release/`; that directory is also intentionally gitignored.

The public repository intentionally excludes workstation-specific configuration, logs, the real private production dashboard, and additional private/local asset packs that are not part of the public project baseline.

## Build and regression

From PowerShell in the repository root:

```powershell
.\build.ps1
```

The script:

1. cleans generated development artifacts;
2. publishes Runtime and Editor as self-contained `win-x64` applications;
3. applies local Authenticode signing when a configured signing certificate is available;
4. builds the regression checks;
5. runs the deterministic regression suite;
6. after a successful build, offers to end, publish to the local portable installation, or publish the Git repository.

The generated application package is staged under `artifacts\publish` during the build. Choosing **End** removes generated artifacts before exit.

### Local code signing

Local signing is optional. To create the project-local trusted development signer on Windows:

```powershell
.\tools\Initialize-PinkieSysMonSigning.ps1
```

The private key stays in the current user's Windows certificate store and is created as non-exportable. No private key or certificate bundle is stored in this repository.

## Local portable deployment

The default local deployment root used by `build.ps1` is:

```text
C:\PinkieSysMon
```

`Publish local` replaces only developer-managed application material: binaries, the public Roboto/Lucide assets, and the public `Default` dashboard. It also creates two portable launch shortcuts in the installation root: `Pinkie's System Monitor.lnk` and `PinkieSysMon Dashboard Editor.lnk`. Their executable relationship is recorded relative to the installation root so the portable tree can move together. The shortcuts use the icons embedded in the target executables. PinkieSysMon does **not** create Desktop or Start Menu shortcuts.

Existing machine-specific configuration, private/local assets, dashboards, and logs are preserved. The application is left stopped after deployment so runtime/device verification remains an explicit step.

The same **Publish local** action also prepares a clean redistributable portable ZIP under `release/`. The archive contains a top-level `PinkieSysMon/` folder with the self-contained binaries, the two portable root shortcuts, the project/third-party license notices, only the public Roboto and Lucide assets, only the public `Default` dashboard, and a machine-neutral `config/app.json` configured for `Default` with only the `system` metric provider enabled. The ZIP filename is `PinkieSysMon_<Version>-<build>.zip`, where `<build>` is the fourth component of `FileVersion`; rerunning the same version/build replaces that ZIP.

Libre Hardware Monitor is an independent external application and is not bundled into the portable PinkieSysMon installation.

## Libre Hardware Monitor setup

PinkieSysMon reads LHM through the local HTTP API of an already-running Libre Hardware Monitor instance. The current provider expects the standard local port `8085`; HTTP authentication is not supported by the current implementation. LHM remains the owner of hardware-sensor access.

## Dashboard compatibility

The persisted dashboard schema is versioned and migration-aware. Runtime and Editor use the same canonical dashboard model and rendering semantics. Persisted schema changes require an intentional migration and regression coverage rather than silent reinterpretation.

## License

PinkieSysMon project source code is released under the **MIT License**. See [`LICENSE`](LICENSE).

Bundled third-party assets remain under their own licenses. See [`THIRD-PARTY-NOTICES.md`](THIRD-PARTY-NOTICES.md) and the license files stored with those assets.
