# Building, Deployment, and Development

Build, regression, local deployment, and portable packaging instructions. For application features, see the [Overview](../README.md).

## Requirements

- 64-bit Windows.
- Windows 11 x64 is the primary supported desktop platform; compatible Windows 10 configurations are also supported by the current .NET 10 target.
- .NET SDK **10.0.401** to build from source.
- A [supported output device](../supported-devices.md) when testing direct hardware output; it is not required merely to build the application.
- Optional: Libre Hardware Monitor; its [provider setup](../telemetry/libre-hardware-monitor.md#user-setup) owns endpoint and security details.
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

The authoritative public/private source boundary and exclusion inventory are maintained in [Public Repository Boundary](../PUBLIC-REPOSITORY.md). The repository layout above describes the public source baseline, not the author's local production installation.

## Automatic version numbering

Every build uses `vA.B.C`. `A` is the major explicitly chosen by the owner (currently `2`, in `Directory.Build.props`); `B` is the live total of closed GitHub Issues in this repository (excluding pull requests); `C` is the number of Git commits reachable from `main` HEAD, including documentation commits and merges. Before compilation, `build.ps1` queries the GitHub Issues search total and executes `git rev-list --count HEAD`. Builds from non-`main` or shallow history, or without a reliable live issue count, stop instead of manufacturing a version.

The resulting `Version` and `InformationalVersion` are `A.B.C`; Windows `AssemblyVersion` and `FileVersion` must have four components, so they are `A.B.C.0`. The final zero is a Windows metadata field, **not** the build number. The same version is injected into Runtime, Editor and regression build, and published EXE/DLL file metadata are validated before signing. The portable archive is named `PinkieSysMon_A.B.C.zip` without a trailing `-0`. Direct project compilation without version resolution fails; the supported entry point is `build.ps1`.

B can change on Issue closure/reopening even without a new commit, and reopening can decrease it. Each binary keeps the count observed at the beginning of its build; historic Issue milestone titles do not define the live version. A major change requires separate owner approval. Network access to GitHub is required to verify B; no stale fallback is allowed.

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


### GitHub synchronization

To synchronize the current local branch with its corresponding branch on
`origin`, run:

```powershell
.\build.ps1 -Action Sync
```

Synchronization requires a clean Git working tree, including no untracked
files. The command fetches the latest remote references and updates the
current branch only when a fast-forward is possible.

- If the local branch is current, no changes are made.
- If the local branch is ahead, local commits are preserved.
- If the remote branch is ahead, the local branch is fast-forwarded.
- If the branches have diverged, synchronization stops without merging or rebasing.
- If the corresponding remote branch does not exist, the local branch is left unchanged.

The GitHub publish action also checks the current remote branch before
committing or pushing. If newer remote commits exist, publication is blocked
until synchronization has been resolved explicitly.

Run the build and regression checks again after synchronizing updated source.


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

!!! info "Publish local: replaced vs. preserved"
    - **Replaced:** `Publish local` replaces only developer-managed application material: binaries, the public Roboto/Lucide assets, and the public `Default` dashboard.
    - **Included:** Root shortcuts `Pinkie's System Monitor.lnk` and `PinkieSysMon Dashboard Editor.lnk`. Portable relocation of these shortcuts is currently broken ([Issue #52](https://github.com/Jim1537/PinkieSysMon/issues/52)); launch `bin\PinkieSysMon.exe` or `bin\PinkieSysMon.Editor.exe` directly. No Desktop or Start Menu shortcuts are created.
    - **Preserved:** Existing machine-specific configuration, private/local assets, dashboards, and logs.
    - **After deployment:** The application is left stopped so runtime/device verification remains an explicit step.

The same **Publish local** action also prepares a clean redistributable portable ZIP under `release/`. The archive contains a top-level `PinkieSysMon/` folder with the self-contained binaries, the two portable root shortcuts, the project/third-party license notices, only the public Roboto and Lucide assets, only the public `Default` dashboard, and a machine-neutral `config/app.json` configured for `Default` with only the `system` metric provider enabled. The ZIP filename is `PinkieSysMon_A.B.C.zip`; rerunning the same resolved version replaces that ZIP.

Libre Hardware Monitor is an independent external application and is not bundled into the portable PinkieSysMon installation.

## Libre Hardware Monitor setup

LHM is an optional runtime telemetry integration, not a build requirement and not part of the portable package. The authoritative HTTP endpoint, default port, authentication limitations, setup instructions, and troubleshooting are in the [Libre Hardware Monitor provider guide](../telemetry/libre-hardware-monitor.md#user-setup).

## Dashboard compatibility

See [Application Architecture](architecture.md#persistence-and-schema-boundary) for the shared document/schema boundary.

## License

PinkieSysMon project source code is released under the **MIT License**. See [`LICENSE`](https://github.com/Jim1537/PinkieSysMon/blob/main/LICENSE).

Bundled third-party assets remain under their own licenses. See [`THIRD-PARTY-NOTICES.md`](https://github.com/Jim1537/PinkieSysMon/blob/main/THIRD-PARTY-NOTICES.md) and the license files stored with those assets.
