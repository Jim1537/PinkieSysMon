# Building, Deployment, and Development

This page preserves the build, packaging, deployment, compatibility, and licensing instructions previously maintained in the root repository README. For the product overview and complete documentation, see the [project overview](../README.md).

## Requirements

- 64-bit Windows.
- Windows 11 x64 is the primary supported desktop platform; compatible Windows 10 configurations are also supported by the current .NET 10 target.
- .NET SDK **10.0.401** to build from source.
- A [supported output device](../supported-devices.md) when testing direct hardware output; it is not required merely to build the application.
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

PinkieSysMon project source code is released under the **MIT License**. See [`LICENSE`](https://github.com/Jim1537/PinkieSysMon/blob/main/LICENSE).

Bundled third-party assets remain under their own licenses. See [`THIRD-PARTY-NOTICES.md`](https://github.com/Jim1537/PinkieSysMon/blob/main/THIRD-PARTY-NOTICES.md) and the license files stored with those assets.
