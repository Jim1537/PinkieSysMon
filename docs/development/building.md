# Building, Deployment, and Development

Build, regression, local deployment, and portable packaging instructions. For application features, see the [Overview](../README.md).

## Requirements

- 64-bit Windows.
- Windows 11 x64 is the primary supported desktop platform; compatible Windows 10 configurations are also supported by the current .NET 10 target.
- .NET SDK **10.0.401** to build from source.
- A [supported output device](../supported-devices.md) when testing direct hardware output; it is not required merely to build the application.
- Optional: Libre Hardware Monitor; its [provider setup](../telemetry/libre-hardware-monitor.md#user-setup) owns endpoint and security details.
- Optional: Corsair iCUE with Sensor Logging enabled for Corsair telemetry.

Published Runtime, Editor, and Launcher builds are self-contained x64 applications and do not require a separately installed .NET Runtime. PinkieSysMon currently ships an English-only UI; non-English satellite-resource directories are removed from the self-contained publish payload.

## Repository layout

```text
src/
  PinkieSysMon/                  Runtime and shared dashboard/rendering code
  PinkieSysMon.Editor/           Dashboard Editor
  PinkieSysMon.Launcher/         Standalone root launcher and canonical shared About window

tests/
  PinkieSysMon.RegressionTests/  Deterministic regression suite

assets/
  fonts/Roboto/                  Redistributable Roboto font files
  icons/Lucide/                  Redistributable Lucide SVG icon set

src/PinkieSysMon/PinkieSysMon.ico  Project application icon

dashboards/Default/              Public default dashboard
config/app.json                  Public, machine-neutral defaults
tools/                           Local code-signing helpers
builder.ps1                      Interactive menu and session diagnostic log
build.ps1                        Noninteractive compilation, regression, deployment and Git executor
pull-build.ps1                   Noninteractive pull/build/regression shortcut
```

Build output is generated under `artifacts/` and is not tracked. Portable release ZIPs are generated under `release/`; that directory is also intentionally gitignored.

The authoritative public/private source boundary and exclusion inventory are maintained in [Public Repository Boundary](../PUBLIC-REPOSITORY.md). The repository layout above describes the public source baseline, not the author's local production installation.

## Automatic version numbering

Every build uses `vA.B.C`. `A` is the major explicitly chosen by the owner (currently `2`, in `Directory.Build.props`); `B` is the live total of closed GitHub Issues in this repository (excluding pull requests); `C` is the number of Git commits reachable from `main` HEAD, including documentation commits and merges. Before compilation, `build.ps1` queries the GitHub Issues search total and executes `git rev-list --count HEAD`. Builds from non-`main` or shallow history, or without a reliable live issue count, stop instead of manufacturing a version.

The resulting `Version` and `InformationalVersion` are `A.B.C`; Windows `AssemblyVersion` and `FileVersion` must have four components, so they are `A.B.C.0`. The final zero is a Windows metadata field, **not** the build number. The same version is injected into Runtime, Editor and regression build, and published EXE/DLL file metadata are validated before signing. The portable archive is named `PinkieSysMon_A.B.C.zip` without a trailing `-0`. Direct project compilation without version resolution fails; the developer-facing entry point is `builder.ps1`, which invokes the noninteractive `build.ps1` executor.

B can change on Issue closure/reopening even without a new commit, and reopening can decrease it. Each binary keeps the count observed at the beginning of its build; historic Issue milestone titles do not define the live version. A major change requires separate owner approval. Network access to GitHub is required to verify B; no stale fallback is allowed.

## Build and regression

From PowerShell in the repository root, run:

```powershell
.\builder.ps1
```

The interactive menu appears **before** any work begins. All actions return to the menu after completion or failure:

| Selection | Action | Behavior |
| --- | --- | --- |
| **1 — pull** | Fetch + fast-forward | Updates local Git source safely, without compilation or installation. |
| **2 — build** | Compile + stage | Resolves the live `vA.B.C`, publishes Runtime/Editor/Launcher, builds the regression runner, and retains the compiled files under `artifacts/`. Tests are **not executed** in this step. |
| **3 — regression checks** | Run tests | Executes already-built tests against the exact published binaries without compiling; records a successful acceptance for the current build. |
| **4 — local publish** | Install + package | Requires a build with matching successful regressions. Updates `C:\PinkieSysMon` and prepares a portable release ZIP using existing artifacts, without recompilation. |
| **5 — push** | Commit + push | Audits staged/public material, commits local changes, and pushes to GitHub; does not compile. |
| **0 — exit** | Finish | Ends the session without deleting reusable build artifacts. |

Each launch overwrites the root-level, ignored `builder.log` and records the interactive session using PowerShell transcription: operation boundaries, time and duration, branch and commit, Git status, build version and its Issue/commit counts, signing results, regressions, and failures. Native commands invoked through `build.ps1` (including `dotnet publish`, `dotnet build`, the regression runner, and Git write/network operations) stream standard output and standard error through the PowerShell host so Windows PowerShell 5.1 can capture their diagnostic lines, followed by the actual exit code. Git read-only probes report their interpreted results rather than dumping raw output. The diagnostic log may contain local paths and other workstation information; inspect it before sharing. Commands do not deliberately echo arbitrary arguments or credentials.

The build stores ignored `artifacts/build-state.json`; a successful regression run writes `artifacts/regression-state.json`. These records bind the current source HEAD and SHA-256 fingerprints of non-ignored build/release inputs to the compiled package. Changes to source inputs or packaged binaries, missing artifacts, or regressions that did not pass **for the same build** block local deployment. Rebuilding resets acceptance. The version assigned to an existing build is retained even if the live closed-Issue count later changes.

`build.ps1` requires an explicit `-Action` and contains no interactive prompts or menus. Its supported actions are `Sync`, `BuildOnly`, `RegressionOnly`, `LocalOnly`, and `GitHubOnly`; the optional `-CommitMessage` supplies the message for a GitHub commit. `builder.ps1` owns user prompts. The retained noninteractive `.\pull-build.ps1` wrapper uses the same build executor, preserving outputs for later local publication.

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

For the typical **pull → build → regression checks → local publish** sequence, choose menu options **1, 2, 3, 4** in `.\builder.ps1`. The single compilation is reused for checks and local publishing. Menu option **5 — push** is separate: a commit changes Git HEAD, so any previously staged build must be rebuilt before another local publish. The legacy `.\pull-build.ps1` shortcut performs pull, build, and regressions without installing, committing, or pushing.


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
    - **Included:** A versioned standalone `Launcher.exe` in the installation root, with a built-in icon and a shared About/Launcher window. It determines the Runtime and Editor executable paths from its current installation root, rather than embedding an installation-specific target. Legacy root `.lnk` files and the previous `PinkieSysMon.Launcher.exe` filename are retired on a subsequent local deployment. The Launcher only creates the three user-specific Desktop or Start Menu shortcuts when explicitly requested through **Advanced**; `Publish local` does not create them.
    - **Advanced shortcut actions:** each location receives Launcher, Control Center (current Editor), and Runtime shortcuts pointing to the *current absolute Launcher location* with command arguments. These user-created shortcuts must be refreshed through Advanced after the installation folder moves. Existing matching shortcuts are left untouched; different targets/settings require replacement confirmation. [Issue #52](https://github.com/Jim1537/PinkieSysMon/issues/52) tracks relocated-installation validation; do not treat Windows compile alone as its closure.
    - **Preserved:** Existing machine-specific configuration, private/local assets, dashboards, and logs.
    - **After deployment:** The application is left stopped so runtime/device verification remains an explicit step.

The same **Publish local** action also prepares a clean redistributable portable ZIP under `release/`. The archive contains a top-level `PinkieSysMon/` folder with the portable single-file `Launcher.exe` at its root, self-contained Runtime and Editor binaries in `bin/`, the project/third-party license notices, only the public Roboto and Lucide assets, only the public `Default` dashboard, and a machine-neutral `config/app.json` configured for `Default` with only the `system` metric provider enabled. The Launcher is published as a separate self-contained single-file WinForms application, so it increases ZIP size; it has no dependency on machine-installed .NET. The same shared `ProjectInfoWindow.cs` source is compiled into both Launcher and Editor, while the About mode omits only the three process-launch buttons. The ZIP filename is `PinkieSysMon_A.B.C.zip`; rerunning the same resolved version replaces that ZIP.

Libre Hardware Monitor is an independent external application and is not bundled into the portable PinkieSysMon installation.

## Libre Hardware Monitor setup

LHM is an optional runtime telemetry integration, not a build requirement and not part of the portable package. The authoritative HTTP endpoint, default port, authentication limitations, setup instructions, and troubleshooting are in the [Libre Hardware Monitor provider guide](../telemetry/libre-hardware-monitor.md#user-setup).

## Dashboard compatibility

See [Application Architecture](architecture.md#persistence-and-schema-boundary) for the shared document/schema boundary.

## License

PinkieSysMon project source code is released under the **MIT License**. See [`LICENSE`](https://github.com/Jim1537/PinkieSysMon/blob/main/LICENSE).

Bundled third-party assets remain under their own licenses. See [`THIRD-PARTY-NOTICES.md`](https://github.com/Jim1537/PinkieSysMon/blob/main/THIRD-PARTY-NOTICES.md) and the license files stored with those assets.
