[CmdletBinding()]
param(
    [ValidateSet('Sync', 'BuildOnly', 'RegressionOnly', 'LocalOnly', 'GitHubOnly')]
    [string]$Action,

    [string]$LocalRoot = 'C:\PinkieSysMon',

    [string]$CommitMessage
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2.0

if ([string]::IsNullOrWhiteSpace($Action)) {
    throw 'Specify an explicit -Action or use .\builder.ps1 for the interactive menu.'
}
if ($PSBoundParameters.ContainsKey('CommitMessage') -and $Action -ne 'GitHubOnly') {
    throw '-CommitMessage is only accepted for -Action GitHubOnly.'
}

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$runtimeProject = Join-Path $root 'src\PinkieSysMon\PinkieSysMon.csproj'
$editorProject = Join-Path $root 'src\PinkieSysMon.Editor\PinkieSysMon.Editor.csproj'
$launcherProject = Join-Path $root 'src\PinkieSysMon.Launcher\PinkieSysMon.Launcher.csproj'
$testsProject = Join-Path $root 'tests\PinkieSysMon.RegressionTests\PinkieSysMon.RegressionTests.csproj'
$artifactsRoot = Join-Path $root 'artifacts'
$publishOut = Join-Path $artifactsRoot 'publish'
$testsOut = Join-Path $artifactsRoot 'tests'
$launcherOut = Join-Path $artifactsRoot 'launcher'
$launcherExe = Join-Path $launcherOut 'Launcher.exe'
$releaseRoot = Join-Path $root 'release'
$releaseStageRoot = Join-Path $artifactsRoot 'portable-release'
$releaseStageAppRoot = Join-Path $releaseStageRoot 'PinkieSysMon'
$releaseTempZip = Join-Path $artifactsRoot 'portable-release.zip'
$buildPropsPath = Join-Path $root 'Directory.Build.props'
$signingTools = Join-Path $root 'tools\PinkieCodeSigning.ps1'
$buildStatePath = Join-Path $artifactsRoot 'build-state.json'
$regressionStatePath = Join-Path $artifactsRoot 'regression-state.json'

function Invoke-External {
    param(
        [Parameter(Mandatory = $true)]
        [string]$FilePath,

        [Parameter(ValueFromRemainingArguments = $true)]
        [string[]]$Arguments
    )

    # Windows PowerShell 5.1 does not reliably transcribe native console output.
    # Route both streams through Write-Host so the active transcript sees each
    # line, without buffering a potentially large build or regression log.
    # Do not echo arbitrary arguments (e.g. private Git commit messages).
    $label = $FilePath
    if ($FilePath -eq 'git' -and $Arguments.Count -ge 3 -and $Arguments[0] -eq '-C') {
        $label = "git $($Arguments[2])"
    } elseif ($FilePath -eq 'dotnet' -and $Arguments.Count -ge 1) {
        if ($Arguments[0] -like '*.dll') {
            $label = 'dotnet regression runner'
        } else {
            $label = "dotnet $($Arguments[0])"
        }
    }
    Write-Host "External command: $label"

    # Redirected native stderr under ErrorActionPreference=Stop may prematurely
    # abort Windows PowerShell 5.1 even when the process exits successfully.
    # Use the real native exit code to decide success or failure.
    $previousErrorActionPreference = $ErrorActionPreference
    try {
        $ErrorActionPreference = 'Continue'
        & $FilePath @Arguments 2>&1 | ForEach-Object {
            if ($_ -is [System.Management.Automation.ErrorRecord]) {
                Write-Host ([string]$_.Exception.Message) -ForegroundColor Red
            } else {
                Write-Host ([string]$_)
            }
        }
        $exitCode = $LASTEXITCODE
    } finally {
        $ErrorActionPreference = $previousErrorActionPreference
    }

    Write-Host "External exit code: $exitCode"
    if ($exitCode -ne 0) {
        throw "Command failed with exit code ${exitCode}: $label"
    }
}
function Remove-DevelopmentArtifacts {
    Write-Host ''
    Write-Host 'Cleaning development build artifacts...'

    Remove-Item -LiteralPath $artifactsRoot -Recurse -Force -ErrorAction SilentlyContinue

    # Backward-safety cleanup: remove legacy project-local output left by builds
    # made before centralized artifacts output was introduced.
    Get-ChildItem -LiteralPath $root -Directory -Recurse -Force -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -eq 'bin' -or $_.Name -eq 'obj' } |
        Sort-Object { $_.FullName.Length } -Descending |
        ForEach-Object {
            Remove-Item -LiteralPath $_.FullName -Recurse -Force -ErrorAction SilentlyContinue
        }
}

function Remove-UnsupportedPublishCultures {
    if (-not (Test-Path -LiteralPath $publishOut -PathType Container)) {
        return
    }

    # PinkieSysMon currently ships an English-only UI. SatelliteResourceLanguages
    # handles normal .NET/NuGet satellites, while self-contained Windows App SDK
    # payloads can still copy every framework culture. Prune only immediate child
    # directories whose names are valid cultures, preserving English resources.
    $preservedCultures = @('en', 'en-US')
    $removed = New-Object System.Collections.Generic.List[string]

    foreach ($directory in @(Get-ChildItem -LiteralPath $publishOut -Directory -Force)) {
        if ($directory.Name -in $preservedCultures) {
            continue
        }

        try {
            [void][System.Globalization.CultureInfo]::GetCultureInfo($directory.Name)
        } catch {
            continue
        }

        Remove-Item -LiteralPath $directory.FullName -Recurse -Force
        $removed.Add($directory.Name)
    }

    if ($removed.Count -eq 0) {
        Write-Host 'Publish language cleanup: no non-English culture directories found.'
    } else {
        Write-Host "Publish language cleanup: removed $($removed.Count) non-English culture directories."
    }
}

function Get-PinkieBuildIdentity {
    Assert-GitRepositoryReady
    $branch = Get-CurrentGitBranch
    if ($branch -ne 'main') {
        throw "Automatic versioning requires main, not $branch."
    }

    $shallow = @(& git -C $root rev-parse --is-shallow-repository)
    if ($LASTEXITCODE -ne 0 -or $shallow.Count -ne 1 -or ([string]$shallow[0]).Trim() -ne 'false') {
        throw 'Automatic versioning requires complete, non-shallow main history.'
    }

    $commitOutput = @(& git -C $root rev-list --count HEAD)
    if ($LASTEXITCODE -ne 0 -or $commitOutput.Count -ne 1) {
        throw 'Could not count main commits.'
    }
    $commits = 0
    if (-not [int]::TryParse(([string]$commitOutput[0]).Trim(), [ref]$commits) -or $commits -lt 1) {
        throw 'Invalid main commit count.'
    }

    try {
        [xml]$props = Get-Content -LiteralPath $buildPropsPath -Raw
    } catch {
        throw "Cannot read Directory.Build.props: $($_.Exception.Message)"
    }
    $major = 0
    if (-not [int]::TryParse(([string]$props.Project.PropertyGroup.PinkieMajorVersion), [ref]$major) -or $major -lt 1) {
        throw 'PinkieMajorVersion is not a positive integer.'
    }

    # GitHub search excludes pull requests and supplies the full total_count.
    $uri = 'https://api.github.com/search/issues?q=repo%3AJim1537%2FPinkieSysMon%20is%3Aissue%20is%3Aclosed&per_page=1'
    try {
        $response = Invoke-RestMethod -Uri $uri -Method Get -TimeoutSec 20 -Headers @{
            Accept = 'application/vnd.github+json'
            'User-Agent' = 'PinkieSysMon-build'
        } -ErrorAction Stop
    } catch {
        throw "Closed GitHub Issue count could not be verified; build aborted: $($_.Exception.Message)"
    }
    $closed = 0
    if ($null -eq $response -or $response.incomplete_results -ne $false -or
        -not [int]::TryParse(([string]$response.total_count), [ref]$closed) -or $closed -lt 0) {
        throw 'GitHub returned an incomplete or invalid closed-Issue count.'
    }
    if ($major -gt 65535 -or $closed -gt 65535 -or $commits -gt 65535) {
        throw 'A version component exceeds the supported Windows file/assembly version range.'
    }

    [PSCustomObject]@{
        Version = "$major.$closed.$commits"
        Major = $major
        ClosedIssues = $closed
        Commits = $commits
    }
}

function Assert-PinkiePublishedVersion {
    param([Parameter(Mandatory = $true)][string]$ExpectedVersion)
    $expected = "$ExpectedVersion.0"
    foreach ($file in @('PinkieSysMon.exe', 'PinkieSysMon.dll',
                       'PinkieSysMon.Editor.exe', 'PinkieSysMon.Editor.dll')) {
        $path = Join-Path $publishOut $file
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
            throw "Missing versioned binary: $path"
        }
        $info = (Get-Item -LiteralPath $path).VersionInfo
        $actual = "$($info.FileMajorPart).$($info.FileMinorPart).$($info.FileBuildPart).$($info.FilePrivatePart)"
        if ($actual -ne $expected) {
            throw "FileVersion mismatch in ${file}: expected $expected, observed $actual"
        }
    }
}

function Get-PortableReleaseIdentity {
    if ($null -eq $buildIdentity) {
        throw 'No resolved build identity is available for release packaging.'
    }
    [PSCustomObject]@{
        Version = $buildIdentity.Version
        Build = $buildIdentity.Commits
        FileName = "PinkieSysMon_$($buildIdentity.Version).zip"
    }
}

function Write-PortableReleaseConfig {
    param(
        [Parameter(Mandatory = $true)]
        [string]$DestinationPath
    )

    $publicConfigPath = Join-Path $root 'config\app.json'
    try {
        $config = Get-Content -LiteralPath $publicConfigPath -Raw | ConvertFrom-Json
    } catch {
        throw "Release config source is not valid JSON: $publicConfigPath - $($_.Exception.Message)"
    }

    if ($null -eq $config.MetricProviders -or
        $null -eq $config.Outputs -or $null -eq $config.Media) {
        throw 'Release config source is missing required public configuration sections.'
    }

    $config.MetricProviders.system = $true
    $config.MetricProviders.lhm = $false
    $config.MetricProviders.icue = $false

    foreach ($target in @($config.Outputs.Targets)) {
        if ($null -eq $target) { continue }

        # ConvertFrom-Json omits absent optional fields. Desktop-only outputs
        # have no DeviceId; do not manufacture a USB identity field for them.
        $deviceIdProperty = $target.PSObject.Properties['DeviceId']
        if ($null -ne $deviceIdProperty) {
            $deviceIdProperty.Value = ''
        }
    }

    $config.Media.EndpointTypeOverrides = [PSCustomObject]@{}

    $parent = Split-Path -Parent $DestinationPath
    New-Item -ItemType Directory -Path $parent -Force | Out-Null
    $json = $config | ConvertTo-Json -Depth 20
    Set-Content -LiteralPath $DestinationPath -Value $json -Encoding UTF8
}

function Assert-PortableReleaseStaging {
    param(
        [Parameter(Mandatory = $true)]
        [string]$AppRoot
    )

    $requiredFiles = @(
        'bin\PinkieSysMon.exe',
        'bin\PinkieSysMon.dll',
        'bin\PinkieSysMon.Editor.exe',
        'bin\PinkieSysMon.Editor.dll',
        'config\app.json',
        'LICENSE',
        'THIRD-PARTY-NOTICES.md',
        'Launcher.exe'
    )

    foreach ($relative in $requiredFiles) {
        $path = Join-Path $AppRoot $relative
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
            throw "Portable release staging is incomplete. Missing: $path"
        }
    }

    foreach ($relative in @('assets\fonts\Roboto', 'assets\icons\Lucide', 'dashboards')) {
        $path = Join-Path $AppRoot $relative
        if (-not (Test-Path -LiteralPath $path -PathType Container)) {
            throw "Portable release staging is incomplete. Missing directory: $path"
        }
    }

    $assetFontRoot = Join-Path $AppRoot 'assets\fonts'
    $unexpectedFonts = @(Get-ChildItem -LiteralPath $assetFontRoot -Directory -Force | Where-Object Name -ne 'Roboto')
    if ($unexpectedFonts.Count -ne 0) {
        throw "Portable release contains unexpected font assets: $($unexpectedFonts.Name -join ', ')"
    }

    $assetIconRoot = Join-Path $AppRoot 'assets\icons'
    $unexpectedIcons = @(Get-ChildItem -LiteralPath $assetIconRoot -Directory -Force | Where-Object Name -ne 'Lucide')
    if ($unexpectedIcons.Count -ne 0) {
        throw "Portable release contains unexpected icon assets: $($unexpectedIcons.Name -join ', ')"
    }

    $dashboardRoot = Join-Path $AppRoot 'dashboards'
    $unexpectedDashboards = @(Get-ChildItem -LiteralPath $dashboardRoot -Force)
    if ($unexpectedDashboards.Count -ne 0) {
        throw "Portable release must contain no saved dashboards or user files: $($unexpectedDashboards.Name -join ', ')"
    }

    $configPath = Join-Path $AppRoot 'config\app.json'
    $config = Get-Content -LiteralPath $configPath -Raw | ConvertFrom-Json
    if ($null -ne $config.PSObject.Properties['Dashboard']) {
        throw 'Portable release config must not contain the retired global Dashboard setting.'
    }
    if (@($config.Outputs.Targets).Count -ne 1 -or
        [string]$config.Outputs.Targets[0].Kind -ne 'desktop' -or
        [string]$config.Outputs.Targets[0].Dashboard -ne 'desktop_default' -or
        $config.Outputs.Targets[0].Desktop.BoardEnabled -ne $false) {
        throw 'Portable release must start with a disabled Desktop Output and no prepackaged board.'
    }
    if ($config.MetricProviders.system -ne $true -or
        $config.MetricProviders.lhm -ne $false -or
        $config.MetricProviders.icue -ne $false) {
        throw 'Portable release config must enable only the System metric provider.'
    }
    foreach ($target in @($config.Outputs.Targets)) {
        if ($null -eq $target) { continue }

        $deviceIdProperty = $target.PSObject.Properties['DeviceId']
        if ($null -ne $deviceIdProperty -and
            -not [string]::IsNullOrWhiteSpace([string]$deviceIdProperty.Value)) {
            throw 'Portable release config must not contain a device-specific DeviceId.'
        }
    }
}

function Prepare-PortableReleasePackage {
    Assert-PublishPackage

    $identity = Get-PortableReleaseIdentity
    Remove-Item -LiteralPath $releaseStageRoot -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $releaseTempZip -Force -ErrorAction SilentlyContinue

    $succeeded = $false
    try {
        New-Item -ItemType Directory -Path $releaseStageAppRoot -Force | Out-Null

        Copy-DirectoryContent -Source $publishOut -Destination (Join-Path $releaseStageAppRoot 'bin')
        Copy-DirectoryContent -Source (Join-Path $root 'assets\fonts\Roboto') -Destination (Join-Path $releaseStageAppRoot 'assets\fonts\Roboto')
        Copy-DirectoryContent -Source (Join-Path $root 'assets\icons\Lucide') -Destination (Join-Path $releaseStageAppRoot 'assets\icons\Lucide')
        New-Item -ItemType Directory -Path (Join-Path $releaseStageAppRoot 'dashboards') -Force | Out-Null
        Write-PortableReleaseConfig -DestinationPath (Join-Path $releaseStageAppRoot 'config\app.json')
        Copy-Item -LiteralPath (Join-Path $root 'LICENSE') -Destination (Join-Path $releaseStageAppRoot 'LICENSE') -Force
        Copy-Item -LiteralPath (Join-Path $root 'THIRD-PARTY-NOTICES.md') -Destination (Join-Path $releaseStageAppRoot 'THIRD-PARTY-NOTICES.md') -Force

        Copy-Item -LiteralPath $launcherExe -Destination (Join-Path $releaseStageAppRoot 'Launcher.exe') -Force

        Assert-PortableReleaseStaging -AppRoot $releaseStageAppRoot

        Add-Type -AssemblyName System.IO.Compression.FileSystem
        [System.IO.Compression.ZipFile]::CreateFromDirectory(
            $releaseStageRoot,
            $releaseTempZip,
            [System.IO.Compression.CompressionLevel]::Optimal,
            $false)

        if (-not (Test-Path -LiteralPath $releaseTempZip -PathType Leaf)) {
            throw "Portable release ZIP was not created: $releaseTempZip"
        }

        $archive = [System.IO.Compression.ZipFile]::OpenRead($releaseTempZip)
        try {
            $entries = @($archive.Entries | ForEach-Object { $_.FullName -replace '\\', '/' })
            foreach ($required in @(
                'PinkieSysMon/bin/PinkieSysMon.exe',
                'PinkieSysMon/bin/PinkieSysMon.Editor.exe',
                'PinkieSysMon/config/app.json',
                'PinkieSysMon/LICENSE',
                'PinkieSysMon/THIRD-PARTY-NOTICES.md',
                'PinkieSysMon/Launcher.exe'
            )) {
                if ($required -notin $entries) {
                    throw "Portable release ZIP integrity check failed. Missing entry: $required"
                }
            }

            $forbiddenEntryPatterns = @(
                '^PinkieSysMon/assets/fonts/(?!Roboto(?:/|$))',
                '^PinkieSysMon/assets/icons/(?!Lucide(?:/|$))',
                '^PinkieSysMon/dashboards/[^/]',
                '^PinkieSysMon/config/(?!app\.json$)',
                '^PinkieSysMon/(?:External|src|tests|tools|logs)(?:/|$)'
            )
            foreach ($entry in $entries) {
                foreach ($pattern in $forbiddenEntryPatterns) {
                    if ($entry -match $pattern) {
                        throw "Portable release ZIP contains a forbidden entry: $entry"
                    }
                }
            }
        } finally {
            $archive.Dispose()
        }

        $succeeded = $true
        [PSCustomObject]@{
            FileName = $identity.FileName
            TempZipPath = $releaseTempZip
            FinalZipPath = (Join-Path $releaseRoot $identity.FileName)
            Version = $identity.Version
            Build = $identity.Build
        }
    } finally {
        Remove-Item -LiteralPath $releaseStageRoot -Recurse -Force -ErrorAction SilentlyContinue
        if (-not $succeeded) {
            Remove-Item -LiteralPath $releaseTempZip -Force -ErrorAction SilentlyContinue
        }
    }
}

function Commit-PortableReleasePackage {
    param(
        [Parameter(Mandatory = $true)]
        $PreparedPackage
    )

    if (-not (Test-Path -LiteralPath $PreparedPackage.TempZipPath -PathType Leaf)) {
        throw "Prepared portable release ZIP is missing: $($PreparedPackage.TempZipPath)"
    }

    New-Item -ItemType Directory -Path $releaseRoot -Force | Out-Null

    # Same version/build is intentionally replaced rather than accumulated.
    if (Test-Path -LiteralPath $PreparedPackage.FinalZipPath -PathType Leaf) {
        Remove-Item -LiteralPath $PreparedPackage.FinalZipPath -Force
    }

    Move-Item -LiteralPath $PreparedPackage.TempZipPath -Destination $PreparedPackage.FinalZipPath -Force

    if (-not (Test-Path -LiteralPath $PreparedPackage.FinalZipPath -PathType Leaf)) {
        throw "Portable release ZIP was not committed: $($PreparedPackage.FinalZipPath)"
    }

    Write-Host ''
    Write-Host 'PORTABLE RELEASE: PASS'
    Write-Host "Release ZIP: $($PreparedPackage.FinalZipPath)"
    Write-Host 'Release startup: disabled Desktop placeholder; dashboards generated after Settings OK'
    Write-Host 'Release assets: Roboto + Lucide only'
}

function Assert-PublishPackage {
    if (-not (Test-Path -LiteralPath $launcherExe -PathType Leaf)) {
        throw "Published Launcher is missing: $launcherExe"
    }
    $required = @(
        'PinkieSysMon.exe',
        'PinkieSysMon.dll',
        'PinkieSysMon.Editor.exe',
        'PinkieSysMon.Editor.dll'
    )

    foreach ($name in $required) {
        $path = Join-Path $publishOut $name
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
            throw "Published application is incomplete. Missing: $path"
        }
    }
}

function Stop-PinkieProcesses {
    $names = @('PinkieSysMon', 'PinkieSysMon.Editor', 'PinkieSysMon.Launcher')
    $launcherPath = Join-Path $LocalRoot 'Launcher.exe'
    $running = @()

    foreach ($name in $names) {
        $running += @(Get-Process -Name $name -ErrorAction SilentlyContinue)
    }

    # Launcher.exe is a generic process name: stop it only when its image
    # belongs to this managed installation, never another vendor's launcher.
    $running += @(Get-Process -Name 'Launcher' -ErrorAction SilentlyContinue |
        Where-Object { $_.Path -eq $launcherPath })
    $running = @($running | Sort-Object Id -Unique)
    if ($running.Count -eq 0) {
        Write-Host 'Running PinkieSysMon processes: none'
        return
    }

    Write-Host 'Stopping running PinkieSysMon processes...'
    foreach ($process in $running) {
        Write-Host "  $($process.ProcessName) (PID $($process.Id))"
        Stop-Process -Id $process.Id -Force -ErrorAction Stop
    }

    $deadline = (Get-Date).AddSeconds(10)
    do {
        $alive = @()
        foreach ($name in $names) {
            $alive += @(Get-Process -Name $name -ErrorAction SilentlyContinue)
        }
        $alive += @(Get-Process -Name 'Launcher' -ErrorAction SilentlyContinue |
            Where-Object { $_.Path -eq $launcherPath })
        if ($alive.Count -eq 0) { break }
        Start-Sleep -Milliseconds 200
    } while ((Get-Date) -lt $deadline)

    if ($alive.Count -ne 0) {
        throw 'PinkieSysMon processes are still running. Local publish aborted.'
    }
}

function Copy-DirectoryContent {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Source,

        [Parameter(Mandatory = $true)]
        [string]$Destination
    )

    if (-not (Test-Path -LiteralPath $Source -PathType Container)) {
        throw "Deployment source directory is missing: $Source"
    }

    New-Item -ItemType Directory -Path $Destination -Force | Out-Null
    Get-ChildItem -LiteralPath $Source -Force | ForEach-Object {
        Copy-Item -LiteralPath $_.FullName -Destination $Destination -Recurse -Force
    }
}

function Publish-Local {
    if (-not (Test-Path -LiteralPath $LocalRoot -PathType Container)) {
        throw "Local installation root does not exist: $LocalRoot"
    }

    Assert-PublishPackage
    Stop-PinkieProcesses

    $transactionId = [Guid]::NewGuid().ToString('N')
    $stagingRoot = Join-Path $LocalRoot ".publish-staging-$transactionId"
    $backupRoot = Join-Path $LocalRoot ".publish-backup-$transactionId"

    $managed = @(
        [PSCustomObject]@{
            Relative = 'bin'
            Source = $publishOut
        },
        [PSCustomObject]@{
            Relative = 'Launcher.exe'
            Source = $launcherExe
        },
        [PSCustomObject]@{
            Relative = 'assets\fonts\Roboto'
            Source = (Join-Path $root 'assets\fonts\Roboto')
        },
        [PSCustomObject]@{
            Relative = 'assets\icons\Lucide'
            Source = (Join-Path $root 'assets\icons\Lucide')
        }
    )

    try {
        Write-Host ''
        Write-Host "Preparing local deployment staging under $LocalRoot ..."
        foreach ($item in $managed) {
            $staged = Join-Path $stagingRoot $item.Relative
            if (Test-Path -LiteralPath $item.Source -PathType Leaf) {
                New-Item -ItemType Directory -Path (Split-Path -Parent $staged) -Force | Out-Null
                Copy-Item -LiteralPath $item.Source -Destination $staged -Force
            } else {
                Copy-DirectoryContent -Source $item.Source -Destination $staged
            }
        }

        # Validate staging before touching the live managed paths.
        foreach ($name in @('PinkieSysMon.exe', 'PinkieSysMon.dll', 'PinkieSysMon.Editor.exe', 'PinkieSysMon.Editor.dll')) {
            $path = Join-Path (Join-Path $stagingRoot 'bin') $name
            if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
                throw "Local deployment staging is incomplete. Missing: $path"
            }
        }

        if (-not (Test-Path -LiteralPath (Join-Path $stagingRoot 'Launcher.exe') -PathType Leaf)) {
            throw 'Local deployment staging is incomplete. Missing root Launcher.'
        }

        New-Item -ItemType Directory -Path $backupRoot -Force | Out-Null

        Write-Host 'Replacing managed local product paths...'
        $activated = New-Object System.Collections.Generic.List[string]
        $backedUp = New-Object System.Collections.Generic.List[string]
        $backedUpObsoleteRootEntries = New-Object System.Collections.Generic.List[string]

        try {
            foreach ($item in $managed) {
                $relative = $item.Relative
                $live = Join-Path $LocalRoot $relative
                $backup = Join-Path $backupRoot $relative
                $staged = Join-Path $stagingRoot $relative

                if (Test-Path -LiteralPath $live) {
                    $backupParent = Split-Path -Parent $backup
                    New-Item -ItemType Directory -Path $backupParent -Force | Out-Null
                    Move-Item -LiteralPath $live -Destination $backup -Force
                    $backedUp.Add($relative)
                }

                $liveParent = Split-Path -Parent $live
                New-Item -ItemType Directory -Path $liveParent -Force | Out-Null
                Move-Item -LiteralPath $staged -Destination $live -Force
                $activated.Add($relative)
            }

            # app.json is machine-specific in an established installation. Never
            # overwrite it. Only seed the public default if a new install lacks one.
            $liveAppConfig = Join-Path $LocalRoot 'config\app.json'
            if (-not (Test-Path -LiteralPath $liveAppConfig -PathType Leaf)) {
                $configParent = Split-Path -Parent $liveAppConfig
                New-Item -ItemType Directory -Path $configParent -Force | Out-Null
                Copy-Item -LiteralPath (Join-Path $root 'config\app.json') -Destination $liveAppConfig -Force
                Write-Host 'Seeded missing config\app.json from public defaults.'
            } else {
                Write-Host 'Preserved existing config\app.json.'
            }

            # Retire obsolete managed root entries, including the previous Launcher
            # filename. Desktop/Start Menu links are untouched; rollback restores these.
            foreach ($name in @("Pinkie's System Monitor.lnk", 'PinkieSysMon Dashboard Editor.lnk', 'PinkieSysMon.Launcher.exe')) {
                $existingShortcut = Join-Path $LocalRoot $name
                if (Test-Path -LiteralPath $existingShortcut -PathType Leaf) {
                    Move-Item -LiteralPath $existingShortcut -Destination (Join-Path $backupRoot $name) -Force
                    $backedUpObsoleteRootEntries.Add($name)
                }
            }

            foreach ($name in @('PinkieSysMon.exe', 'PinkieSysMon.dll', 'PinkieSysMon.Editor.exe', 'PinkieSysMon.Editor.dll')) {
                $path = Join-Path (Join-Path $LocalRoot 'bin') $name
                if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
                    throw "Local deployment verification failed. Missing: $path"
                }
            }

            if (-not (Test-Path -LiteralPath (Join-Path $LocalRoot 'Launcher.exe') -PathType Leaf)) {
                throw 'Local deployment verification failed. Root Launcher is missing.'
            }
        } catch {
            Write-Warning 'Local publish failed after deployment began. Rolling managed paths back.'

            foreach ($name in @($backedUpObsoleteRootEntries)) {
                $backup = Join-Path $backupRoot $name
                $live = Join-Path $LocalRoot $name
                if (Test-Path -LiteralPath $backup -PathType Leaf) {
                    Move-Item -LiteralPath $backup -Destination $live -Force
                }
            }

            foreach ($relative in @($activated) | Sort-Object Length -Descending) {
                $live = Join-Path $LocalRoot $relative
                Remove-Item -LiteralPath $live -Recurse -Force -ErrorAction SilentlyContinue
            }

            foreach ($relative in @($backedUp) | Sort-Object Length) {
                $backup = Join-Path $backupRoot $relative
                $live = Join-Path $LocalRoot $relative
                if (Test-Path -LiteralPath $backup) {
                    $liveParent = Split-Path -Parent $live
                    New-Item -ItemType Directory -Path $liveParent -Force | Out-Null
                    Move-Item -LiteralPath $backup -Destination $live -Force
                }
            }

            throw
        }

        Write-Host ''
        Write-Host 'LOCAL PUBLISH: PASS'
        Write-Host "Updated: $LocalRoot\bin"
        Write-Host 'Updated public assets: Roboto + Lucide'
        Write-Host 'Preserved all existing saved dashboards (none deployed or replaced).'
        Write-Host 'Installed root Launcher.exe; retired obsolete root shortcuts and previous Launcher executable name.'
        Write-Host 'Desktop / Start Menu shortcuts: not created'
        Write-Host 'Preserved: config, PinkiePie_Vertical, private/local assets, logs'
        Write-Host 'Application was left stopped.'
    } finally {
        Remove-Item -LiteralPath $stagingRoot -Recurse -Force -ErrorAction SilentlyContinue
        Remove-Item -LiteralPath $backupRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
}

function Assert-GitRepositoryReady {
    if (-not (Get-Command git -ErrorAction SilentlyContinue)) {
        throw 'git.exe is not installed or is not in PATH.'
    }

    $gitDir = Join-Path $root '.git'
    if (-not (Test-Path -LiteralPath $gitDir -PathType Container)) {
        throw 'Git operation is not ready: this developer tree has not been initialized as a Git repository.'
    }

    $remote = @(& git -C $root remote get-url origin 2>$null)
    $remoteExitCode = $LASTEXITCODE
    if ($remoteExitCode -ne 0 -or [string]::IsNullOrWhiteSpace(($remote -join '').Trim())) {
        throw 'Git operation is not ready: Git remote "origin" is not configured.'
    }
}

function Get-CurrentGitBranch {
    $branchOutput = @(& git -C $root branch --show-current)
    $branchExitCode = $LASTEXITCODE
    if ($branchExitCode -ne 0) {
        throw 'Unable to determine the current Git branch.'
    }

    $branch = if ($branchOutput.Count -eq 0) { '' } else { [string]$branchOutput[0] }
    $branch = $branch.Trim()
    if ([string]::IsNullOrWhiteSpace($branch)) {
        throw 'Git operation is blocked: Git is in detached HEAD state.'
    }

    return $branch
}

function Get-OriginBranchHead {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Branch
    )

    $remoteOutput = @(& git -C $root ls-remote --heads origin "refs/heads/$Branch")
    $remoteExitCode = $LASTEXITCODE
    if ($remoteExitCode -ne 0) {
        throw "Unable to query origin/$Branch."
    }

    if ($remoteOutput.Count -eq 0 -or [string]::IsNullOrWhiteSpace([string]$remoteOutput[0])) {
        return $null
    }

    $parts = ([string]$remoteOutput[0]).Trim() -split '\s+'
    if ($parts.Count -lt 2 -or [string]::IsNullOrWhiteSpace($parts[0])) {
        throw "Unable to parse origin/$Branch head."
    }

    return $parts[0]
}

function Get-GitBranchDivergence {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Branch
    )

    $countOutput = @(& git -C $root rev-list --left-right --count "HEAD...origin/$Branch")
    $countExitCode = $LASTEXITCODE
    if ($countExitCode -ne 0) {
        throw "Unable to compare HEAD with origin/$Branch."
    }

    $countText = ($countOutput -join ' ').Trim()
    $parts = $countText -split '\s+'
    if ($parts.Count -ne 2) {
        throw "Unexpected Git divergence result for origin/${Branch}: $countText"
    }

    $localAhead = 0
    $remoteAhead = 0
    if (-not [int]::TryParse($parts[0], [ref]$localAhead) -or
        -not [int]::TryParse($parts[1], [ref]$remoteAhead)) {
        throw "Unable to parse Git divergence result for origin/${Branch}: $countText"
    }

    return [PSCustomObject]@{
        LocalAhead = $localAhead
        RemoteAhead = $remoteAhead
    }
}

function Sync-GitHub {
    Assert-GitRepositoryReady

    $branch = Get-CurrentGitBranch

    $status = @(& git -C $root status --porcelain --untracked-files=normal)
    $statusExitCode = $LASTEXITCODE
    if ($statusExitCode -ne 0) {
        throw 'Unable to inspect the Git working tree before synchronization.'
    }

    if ($status.Count -ne 0) {
        throw 'GitHub sync blocked: local source changes are present. Commit, stash, or discard them before synchronization.'
    }

    $remoteHead = Get-OriginBranchHead -Branch $branch
    if ($null -eq $remoteHead) {
        Write-Host ''
        Write-Host 'GITHUB SYNC: PASS'
        Write-Host "Remote branch origin/$branch does not exist; local source was left unchanged."
        return
    }

    Write-Host ''
    Write-Host "Fetching origin/$branch..."
    Invoke-External git '-C' $root 'fetch' '--prune' 'origin'

    $divergence = Get-GitBranchDivergence -Branch $branch

    if ($divergence.LocalAhead -gt 0 -and $divergence.RemoteAhead -gt 0) {
        throw "GitHub sync blocked: local $branch and origin/$branch have diverged ($($divergence.LocalAhead) local-only commit(s), $($divergence.RemoteAhead) remote-only commit(s)). Resolve explicitly; no automatic merge or rebase was performed."
    }

    if ($divergence.RemoteAhead -gt 0) {
        Invoke-External git '-C' $root 'merge' '--ff-only' "origin/$branch"
        Write-Host ''
        Write-Host 'GITHUB SYNC: PASS'
        Write-Host "Fast-forwarded $branch by $($divergence.RemoteAhead) commit(s) from origin/$branch."
        Write-Host 'Run .\builder.ps1: select 2 (build), then 3 (regression checks), before publishing.'
        return
    }

    Write-Host ''
    Write-Host 'GITHUB SYNC: PASS'
    if ($divergence.LocalAhead -gt 0) {
        Write-Host "$branch is $($divergence.LocalAhead) commit(s) ahead of origin/$branch; nothing was pulled."
    } else {
        Write-Host "$branch is already synchronized with origin/$branch."
    }
}

function Assert-GitHubRemoteCurrentForPublish {
    $branch = Get-CurrentGitBranch
    $remoteHead = Get-OriginBranchHead -Branch $branch

    if ($null -eq $remoteHead) {
        return $branch
    }

    Write-Host ''
    Write-Host "Checking origin/$branch for newer commits..."
    Invoke-External git '-C' $root 'fetch' '--prune' 'origin'

    $divergence = Get-GitBranchDivergence -Branch $branch
    if ($divergence.RemoteAhead -gt 0) {
        if ($divergence.LocalAhead -gt 0) {
            throw "GitHub publish blocked: local $branch and origin/$branch have diverged. Use .\builder.ps1 option 1 (pull) after resolving local changes, then rebuild before publishing."
        }

        throw "GitHub publish blocked: origin/$branch is $($divergence.RemoteAhead) commit(s) ahead. Use .\builder.ps1 option 1 (pull), then rebuild before publishing."
    }

    return $branch
}

function Assert-GitHubPublishReady {
    Assert-GitRepositoryReady

    foreach ($required in @('docs/README.md', 'LICENSE', 'THIRD-PARTY-NOTICES.md')) {
        if (-not (Test-Path -LiteralPath (Join-Path $root $required) -PathType Leaf)) {
            throw "GitHub publish is blocked until the public repository document exists: $required"
        }
    }

    # Public defaults must not accidentally capture workstation-specific identity.
    $publicConfigPath = Join-Path $root 'config\app.json'
    try {
        $publicConfig = Get-Content -LiteralPath $publicConfigPath -Raw | ConvertFrom-Json
    } catch {
        throw "GitHub publish blocked: config\app.json is not valid JSON: $($_.Exception.Message)"
    }

    foreach ($target in @($publicConfig.Outputs.Targets)) {
        if ($null -eq $target) { continue }

        $deviceIdProperty = $target.PSObject.Properties['DeviceId']
        if ($null -ne $deviceIdProperty -and
            -not [string]::IsNullOrWhiteSpace([string]$deviceIdProperty.Value)) {
            throw 'GitHub publish blocked: public config contains a device-specific DeviceId.'
        }
    }

    $endpointTypeOverrides = $publicConfig.Media.EndpointTypeOverrides
    if ($null -ne $endpointTypeOverrides -and
        @($endpointTypeOverrides.PSObject.Properties).Count -ne 0) {
        throw 'GitHub publish blocked: public config contains machine-specific media endpoint overrides.'
    }

}

function Publish-GitHub {
    param([AllowEmptyString()][string]$CommitMessage)

    Assert-GitHubPublishReady
    $branch = Assert-GitHubRemoteCurrentForPublish

    Write-Host ''
    Write-Host 'Preparing Git commit...'
    Invoke-External git '-C' $root 'add' '-A'

    $staged = @(& git -C $root diff --cached --name-only)
    if ($LASTEXITCODE -ne 0) {
        throw 'Unable to enumerate staged Git changes.'
    }

    $forbiddenPatterns = @(
        '^External/',
        '^logs/',
        '^dashboards/PinkiePie_Vertical(?:/|_|$)',
        '^assets/fonts/Equestria/',
        '^assets/icons/(?:Bootstrap|Phosphor_duotone|Tabler|MLP app icons|MLP folder icons|PinkieSysMon)/',
        '(^|/)(?:bin|obj)/',
        '^artifacts/',
        '^release/'
    )

    foreach ($name in $staged) {
        $normalized = $name -replace '\\', '/'
        foreach ($pattern in $forbiddenPatterns) {
            if ($normalized -match $pattern) {
                throw "GitHub publish blocked: forbidden/private/generated path is staged: $name"
            }
        }

        if ($normalized -match '\.(?:pfx|p12|p8|key|pem|snk)$') {
            throw "GitHub publish blocked: sensitive key/certificate file is staged: $name"
        }
    }

    # Route staged-diff errors through the native diagnostic capture as well.
    try {
        Invoke-External git '-C' $root 'diff' '--cached' '--check'
    } catch {
        throw "GitHub publish blocked: git diff --cached --check reported errors. $($_.Exception.Message)"
    }

    if ($staged.Count -eq 0) {
        Write-Host 'No source changes to commit.'
    } else {
        Write-Host ''
        Write-Host 'Staged changes:'
        $staged | ForEach-Object { Write-Host "  $_" }

        # Interactive commit-message input belongs to builder.ps1.
        $message = if ([string]::IsNullOrWhiteSpace($CommitMessage)) {
            'PinkieSysMon update ' + (Get-Date -Format 'yyyy-MM-dd HH:mm')
        } else { $CommitMessage }
        Invoke-External git '-C' $root 'commit' '-m' $message
    }

    # Always push through the project's authoritative remote and refresh upstream tracking.
    # This is valid for both the first publish and subsequent publishes, and avoids
    # platform-specific failure modes when probing a branch that has no upstream yet.
    Invoke-External git '-C' $root 'push' '-u' 'origin' $branch

    Write-Host ''
    Write-Host 'GITHUB PUBLISH: PASS'
}


# Reuse is allowed only while HEAD, nonignored build/release inputs and all
# published binaries remain identical to the recorded build.
function Get-PinkieSourceDigest {
    Assert-GitRepositoryReady
    $head = @(& git -C $root rev-parse HEAD)
    if ($LASTEXITCODE -ne 0 -or $head.Count -ne 1) { throw 'Cannot identify source commit.' }
    $files = @(& git -C $root -c core.quotepath=false ls-files --cached --others --exclude-standard)
    if ($LASTEXITCODE -ne 0) { throw 'Cannot inventory source files.' }
    $rows = @("HEAD=$($head[0])")
    foreach ($entry in @($files | Sort-Object -Unique)) {
        $name = ([string]$entry).Replace('\', '/')
        if ($name -notmatch '^(src/|tests/|tools/|assets/fonts/Roboto/|assets/icons/Lucide/|config/app[.]json$|Directory[.]Build[.]props$|build[.]ps1$|builder[.]ps1$|global[.]json$|NuGet[.]Config$|[^/]+[.](?:sln|slnx|props|targets)$|LICENSE$|THIRD-PARTY-NOTICES[.]md$)') { continue }
        $path = Join-Path $root $entry
        if (Test-Path -LiteralPath $path -PathType Leaf) {
            $rows += ('{0}:{1}' -f $name, (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash)
        } else {
            $rows += ('{0}:MISSING' -f $name)
        }
    }
    return (Get-PinkieStringDigest -Content ($rows -join [Environment]::NewLine))
}

function Get-PinkieStringDigest {
    param([Parameter(Mandatory = $true)][string]$Content)
    $sha = [System.Security.Cryptography.SHA256]::Create()
    try {
        return ([BitConverter]::ToString($sha.ComputeHash([System.Text.Encoding]::UTF8.GetBytes($Content)))).Replace('-', '').ToLowerInvariant()
    } finally { $sha.Dispose() }
}

function Get-PinkieOutputDigest {
    Assert-PublishPackage
    $testDll = Join-Path $testsOut 'PinkieSysMon.RegressionTests.dll'
    $needed = @($launcherExe, $testDll)
    $rows = @()
    $files = @(Get-ChildItem -LiteralPath $publishOut -Recurse -File -Force | Sort-Object FullName)
    if ($files.Count -eq 0) { throw 'Compiled publish folder is empty.' }
    foreach ($file in $files) {
        $relative = $file.FullName.Substring($publishOut.Length).TrimStart('\', '/')
        $rows += ('publish/{0}:{1}' -f $relative, (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash)
    }
    foreach ($path in $needed) {
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Compiled file is missing: $path" }
        $rows += ('{0}:{1}' -f (Split-Path -Leaf $path), (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash)
    }
    return (Get-PinkieStringDigest -Content ($rows -join [Environment]::NewLine))
}

function Save-PinkieBuildState {
    param($Identity, [string]$InitialSourceDigest)
    if ((Get-PinkieSourceDigest) -ne $InitialSourceDigest) {
        throw 'Source changed during compilation; rebuild required.'
    }
    $record = [PSCustomObject]@{
        Version = $Identity.Version
        Commits = $Identity.Commits
        ClosedIssues = $Identity.ClosedIssues
        SourceDigest = $InitialSourceDigest
        OutputDigest = (Get-PinkieOutputDigest)
    }
    $record | ConvertTo-Json | Set-Content -LiteralPath $buildStatePath -Encoding UTF8
    Write-Host "Reusable build recorded: v$($record.Version); source=$($record.SourceDigest); output=$($record.OutputDigest)"
    return $record
}

function Assert-PinkieReuse {
    if (-not (Test-Path -LiteralPath $buildStatePath -PathType Leaf)) {
        throw 'No previous reusable build; select 2 - build.'
    }
    try { $record = Get-Content -LiteralPath $buildStatePath -Raw | ConvertFrom-Json }
    catch { throw "Cannot read build state: $($_.Exception.Message)" }
    if ([string]::IsNullOrWhiteSpace([string]$record.Version) -or
        [string]::IsNullOrWhiteSpace([string]$record.SourceDigest) -or
        [string]::IsNullOrWhiteSpace([string]$record.OutputDigest)) {
        throw 'Incomplete build state; rebuild required.'
    }
    if ((Get-PinkieSourceDigest) -ne [string]$record.SourceDigest) {
        throw 'Git HEAD or build/release input changed; rebuild required.'
    }
    Assert-PinkiePublishedVersion -ExpectedVersion ([string]$record.Version)
    $v = (Get-Item -LiteralPath $launcherExe).VersionInfo
    $observed = "$($v.FileMajorPart).$($v.FileMinorPart).$($v.FileBuildPart).$($v.FilePrivatePart)"
    if ($observed -ne "$($record.Version).0") { throw "Launcher version mismatch: $observed" }
    if ((Get-PinkieOutputDigest) -ne [string]$record.OutputDigest) {
        throw 'Published binaries changed or are missing; rebuild required.'
    }
    Write-Host "Reusable build valid: v$($record.Version); source=$($record.SourceDigest); output=$($record.OutputDigest)"
    return $record
}

function Invoke-PinkieExistingRegressions {
    $dll = Join-Path $testsOut 'PinkieSysMon.RegressionTests.dll'
    if (-not (Test-Path -LiteralPath $dll -PathType Leaf)) { throw "Missing regression runner: $dll" }
    Get-ChildItem -LiteralPath $publishOut -Force | ForEach-Object {
        Copy-Item -LiteralPath $_.FullName -Destination $testsOut -Recurse -Force
    }
    Write-Host ''
    Write-Host 'Running regression checks (no compilation)...'
    Invoke-External dotnet $dll
    Assert-PublishPackage
}

function Save-PinkieRegressionState {
    param($Record)
    $verified = Assert-PinkieReuse
    if ([string]$verified.OutputDigest -ne [string]$Record.OutputDigest -or
        [string]$verified.SourceDigest -ne [string]$Record.SourceDigest) {
        throw 'Build changed during regression run; acceptance refused.'
    }
    [PSCustomObject]@{
        Version = $verified.Version
        SourceDigest = $verified.SourceDigest
        OutputDigest = $verified.OutputDigest
    } | ConvertTo-Json | Set-Content -LiteralPath $regressionStatePath -Encoding UTF8
    Write-Host "Regression acceptance recorded for v$($verified.Version)."
}

function Assert-PinkieRegressionState {
    param($Record)
    if (-not (Test-Path -LiteralPath $regressionStatePath -PathType Leaf)) {
        throw 'Local publish blocked: select 3 - regression checks first.'
    }
    try { $checked = Get-Content -LiteralPath $regressionStatePath -Raw | ConvertFrom-Json }
    catch { throw "Regression state invalid: $($_.Exception.Message)" }
    if ([string]$checked.Version -ne [string]$Record.Version -or
        [string]$checked.SourceDigest -ne [string]$Record.SourceDigest -or
        [string]$checked.OutputDigest -ne [string]$Record.OutputDigest) {
        throw 'Local publish blocked: regression pass is from a different build.'
    }
    Write-Host "Regression gate: PASS for v$($Record.Version)."
}

if ($Action -eq 'Sync') {
    try {
        Sync-GitHub
    } catch {
        Write-Host ''
        Write-Host 'GITHUB SYNC: FAIL' -ForegroundColor Red
        Write-Host $_.Exception.Message -ForegroundColor Red
        throw
    }

    return
}

if ($Action -eq 'GitHubOnly') {
    try { Publish-GitHub -CommitMessage $CommitMessage }
    catch {
        Write-Host 'GITHUB PUSH: FAIL' -ForegroundColor Red
        Write-Host $_.Exception.Message -ForegroundColor Red
        throw
    }
    return
}

if ($Action -in @('RegressionOnly', 'LocalOnly')) {
    try {
        if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
            throw '.NET SDK not available.'
        }
        $reuse = Assert-PinkieReuse
        $buildIdentity = [PSCustomObject]@{
            Version = [string]$reuse.Version
            Commits = [int]$reuse.Commits
        }
        if ($Action -eq 'RegressionOnly') {
            Remove-Item -LiteralPath $regressionStatePath -Force -ErrorAction SilentlyContinue
            Invoke-PinkieExistingRegressions
            Save-PinkieRegressionState -Record $reuse
        } else {
            Assert-PinkieRegressionState -Record $reuse
            $preparedRelease = Prepare-PortableReleasePackage
            Publish-Local
            Commit-PortableReleasePackage -PreparedPackage $preparedRelease
            Write-Host 'Reusable artifacts retained after local publish.'
        }
    } catch {
        Write-Host "$($Action.ToUpperInvariant()): FAIL" -ForegroundColor Red
        Write-Host $_.Exception.Message -ForegroundColor Red
        throw
    }
    return
}

if ($Action -ne 'BuildOnly') {
    throw "Unsupported build action: $Action"
}

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw '.NET 10 SDK is not installed or dotnet.exe is not in PATH.'
}

if (-not (Test-Path -LiteralPath $signingTools -PathType Leaf)) {
    throw "Code-signing helper is missing: $signingTools"
}
. $signingTools

foreach ($project in @($runtimeProject, $editorProject, $launcherProject, $testsProject)) {
    if (-not (Test-Path -LiteralPath $project -PathType Leaf)) {
        throw "Required project is missing: $project"
    }
}

$buildIdentity = Get-PinkieBuildIdentity
$initialSourceDigest = Get-PinkieSourceDigest
$resolvedVersionArgument = "-p:PinkieResolvedVersion=$($buildIdentity.Version)"
$version = & dotnet --version
Write-Host ".NET SDK: $version"
Write-Host "Developer root: $root"
Write-Host "Build version: v$($buildIdentity.Version) (major=$($buildIdentity.Major); closed issues=$($buildIdentity.ClosedIssues); main commits=$($buildIdentity.Commits))"

# Every successful build starts from a deterministic clean generated state.
Remove-DevelopmentArtifacts
New-Item -ItemType Directory -Path $publishOut -Force | Out-Null
New-Item -ItemType Directory -Path $testsOut -Force | Out-Null

try {
    Write-Host ''
    Write-Host 'Publishing PinkieSysMon runtime...'
    Invoke-External dotnet 'publish' $runtimeProject '-c' 'Release' '-r' 'win-x64' '--self-contained' 'true' '-o' $publishOut $resolvedVersionArgument

    Write-Host ''
    Write-Host 'Publishing PinkieSysMon Dashboard Editor...'
    Invoke-External dotnet 'publish' $editorProject '-c' 'Release' '-r' 'win-x64' '--self-contained' 'true' '-o' $publishOut $resolvedVersionArgument

    Write-Host ''
    Write-Host 'Publishing single-file portable Launcher...'
    Invoke-External dotnet 'publish' $launcherProject '-c' 'Release' '-r' 'win-x64' '--self-contained' 'true' '-o' $launcherOut $resolvedVersionArgument

    Remove-UnsupportedPublishCultures
    Assert-PublishPackage
    Assert-PinkiePublishedVersion -ExpectedVersion $buildIdentity.Version
    $launcherVersion = (Get-Item -LiteralPath $launcherExe).VersionInfo
    $actualLauncherVersion = "$($launcherVersion.FileMajorPart).$($launcherVersion.FileMinorPart).$($launcherVersion.FileBuildPart).$($launcherVersion.FilePrivatePart)"
    if ($actualLauncherVersion -ne "$($buildIdentity.Version).0") {
        throw "Launcher FileVersion mismatch: expected $($buildIdentity.Version).0, got $actualLauncherVersion"
    }

    Write-Host ''
    Write-Host 'Applying Windows application identity / Authenticode signing...'
    $signingThumbprint = $env:PINKIESYSMON_SIGNING_THUMBPRINT
    $signingCertificate = Get-PinkieCodeSigningCertificate -Thumbprint $signingThumbprint
    $requireSigning = $env:PINKIESYSMON_REQUIRE_SIGNING -eq '1'
    $timestampServer = $env:PINKIESYSMON_TIMESTAMP_URL

    if ($null -eq $signingCertificate) {
        $message = 'Code signing is not configured. Run .\tools\Initialize-PinkieSysMonSigning.ps1 once to create and trust the local PinkieSysMon signer.'
        if ($requireSigning) {
            throw $message
        }
        Write-Warning $message
        Write-Host 'Signing: SKIPPED'
    } else {
        $firstPartyArtifacts = @(
            (Join-Path $publishOut 'PinkieSysMon.exe'),
            (Join-Path $publishOut 'PinkieSysMon.dll'),
            (Join-Path $publishOut 'PinkieSysMon.Editor.exe'),
            (Join-Path $publishOut 'PinkieSysMon.Editor.dll'),
            $launcherExe
        )

        try {
            Invoke-PinkieArtifactSigning `
                -Path $firstPartyArtifacts `
                -Certificate $signingCertificate `
                -TimestampServer $timestampServer

            Write-Host 'Signing: VALID'
            Write-Host "Signer:  $($signingCertificate.Subject)"
            Write-Host "Thumbprint: $($signingCertificate.Thumbprint)"
            if (-not [string]::IsNullOrWhiteSpace($timestampServer)) {
                Write-Host "Timestamp: $timestampServer"
            } else {
                Write-Host 'Timestamp: none (local/offline signing mode)'
            }
        } catch {
            if ($requireSigning) {
                throw
            }

            $exception = $_.Exception
            $accessDenied = ($exception -is [System.UnauthorizedAccessException]) -or
                ($exception.InnerException -is [System.UnauthorizedAccessException]) -or
                ($_.CategoryInfo.Reason -eq 'UnauthorizedAccessException')

            if (-not $accessDenied) {
                throw
            }

            Write-Warning 'Authenticode signing was denied by the host. Continuing with unsigned binaries because PINKIESYSMON_REQUIRE_SIGNING is not 1.'
            Write-Host 'Signing: SKIPPED'
            Write-Host 'Reason: host denied Authenticode signing (access denied)'
        }
    }

    Write-Host ''
    Write-Host 'Building regression runner...'
    Invoke-External dotnet 'build' $testsProject '-c' 'Release' '-o' $testsOut $resolvedVersionArgument
    Save-PinkieBuildState -Identity $buildIdentity -InitialSourceDigest $initialSourceDigest | Out-Null
    Write-Host ''
    Write-Host 'BUILD: PASS'
    Write-Host "Published package: $publishOut"
    Write-Host 'REGRESSION: NOT RUN (select 3 - regression checks).'
} catch {
    Write-Host ''
    Write-Host 'BUILD/PUBLISH: FAIL' -ForegroundColor Red
    Write-Host $_.Exception.Message -ForegroundColor Red
    Write-Host "Generated diagnostic artifacts were preserved under: $artifactsRoot"
    throw
}
