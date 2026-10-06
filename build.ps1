[CmdletBinding()]
param(
    [ValidateSet('Prompt', 'End', 'Local', 'GitHub')]
    [string]$Action = 'Prompt',

    [string]$LocalRoot = 'C:\PinkieSysMon'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2.0

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$runtimeProject = Join-Path $root 'src\PinkieSysMon\PinkieSysMon.csproj'
$editorProject = Join-Path $root 'src\PinkieSysMon.Editor\PinkieSysMon.Editor.csproj'
$testsProject = Join-Path $root 'tests\PinkieSysMon.RegressionTests\PinkieSysMon.RegressionTests.csproj'
$artifactsRoot = Join-Path $root 'artifacts'
$publishOut = Join-Path $artifactsRoot 'publish'
$testsOut = Join-Path $artifactsRoot 'tests'
$releaseRoot = Join-Path $root 'release'
$releaseStageRoot = Join-Path $artifactsRoot 'portable-release'
$releaseStageAppRoot = Join-Path $releaseStageRoot 'PinkieSysMon'
$releaseTempZip = Join-Path $artifactsRoot 'portable-release.zip'
$buildPropsPath = Join-Path $root 'Directory.Build.props'
$signingTools = Join-Path $root 'tools\PinkieCodeSigning.ps1'

$portableShortcutDefinitions = @(
    [PSCustomObject]@{
        Name = "Pinkie's System Monitor.lnk"
        Target = 'PinkieSysMon.exe'
        Description = "Pinkie's System Monitor"
    },
    [PSCustomObject]@{
        Name = 'PinkieSysMon Dashboard Editor.lnk'
        Target = 'PinkieSysMon.Editor.exe'
        Description = 'PinkieSysMon Dashboard Editor'
    }
)

function Invoke-External {
    param(
        [Parameter(Mandatory = $true)]
        [string]$FilePath,

        [Parameter(ValueFromRemainingArguments = $true)]
        [string[]]$Arguments
    )

    & $FilePath @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "Command failed with exit code ${LASTEXITCODE}: $FilePath $($Arguments -join ' ')"
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

function Initialize-PortableShellLinkInterop {
    if ('PinkieSysMon.Build.PortableShellLink' -as [type]) {
        return
    }

    Add-Type -Language CSharp -TypeDefinition @'
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace PinkieSysMon.Build
{
    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("000214F9-0000-0000-C000-000000000046")]
    internal interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile, int cch, IntPtr pfd, uint fFlags);
        void GetIDList(out IntPtr ppidl);
        void SetIDList(IntPtr pidl);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszName, int cch);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszDir, int cch);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszArgs, int cch);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
        void GetHotkey(out short pwHotkey);
        void SetHotkey(short wHotkey);
        void GetShowCmd(out int piShowCmd);
        void SetShowCmd(int iShowCmd);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszIconPath, int cch, out int piIcon);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, uint dwReserved);
        void Resolve(IntPtr hwnd, uint fFlags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
    }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("0000010B-0000-0000-C000-000000000046")]
    internal interface IPersistFile
    {
        void GetClassID(out Guid pClassID);
        [PreserveSig] int IsDirty();
        void Load([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, uint dwMode);
        void Save([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, bool fRemember);
        void SaveCompleted([MarshalAs(UnmanagedType.LPWStr)] string pszFileName);
        void GetCurFile([MarshalAs(UnmanagedType.LPWStr)] out string ppszFileName);
    }

    [ComImport]
    [Guid("00021401-0000-0000-C000-000000000046")]
    [ClassInterface(ClassInterfaceType.None)]
    internal class ShellLink
    {
    }

    public static class PortableShellLink
    {
        public static void Create(string shortcutPath, string targetPath, string description)
        {
            if (string.IsNullOrWhiteSpace(shortcutPath))
                throw new ArgumentException("Shortcut path is required.", "shortcutPath");
            if (string.IsNullOrWhiteSpace(targetPath))
                throw new ArgumentException("Target path is required.", "targetPath");

            string shortcutFullPath = Path.GetFullPath(shortcutPath);
            string targetFullPath = Path.GetFullPath(targetPath);
            string shortcutDirectory = Path.GetDirectoryName(shortcutFullPath);
            if (string.IsNullOrEmpty(shortcutDirectory))
                throw new InvalidOperationException("Shortcut path does not have a parent directory.");

            Directory.CreateDirectory(shortcutDirectory);

            object comObject = new ShellLink();
            try
            {
                IShellLinkW shellLink = (IShellLinkW)comObject;
                IPersistFile persistFile = (IPersistFile)comObject;

                // Keep a normal absolute fallback, then ask Windows Shell to record
                // the relationship relative to the .lnk file for portable moves.
                shellLink.SetPath(targetFullPath);
                shellLink.SetDescription(description ?? string.Empty);
                shellLink.SetRelativePath(shortcutFullPath, 0);
                persistFile.Save(shortcutFullPath, true);
            }
            finally
            {
                if (Marshal.IsComObject(comObject))
                    Marshal.FinalReleaseComObject(comObject);
            }
        }
    }
}
'@
}

function New-PortableShellLink {
    param(
        [Parameter(Mandatory = $true)]
        [string]$ShortcutPath,

        [Parameter(Mandatory = $true)]
        [string]$TargetPath,

        [Parameter(Mandatory = $true)]
        [string]$Description
    )

    if (-not (Test-Path -LiteralPath $TargetPath -PathType Leaf)) {
        throw "Shortcut target does not exist: $TargetPath"
    }

    Initialize-PortableShellLinkInterop

    [PinkieSysMon.Build.PortableShellLink]::Create(
        [System.IO.Path]::GetFullPath($ShortcutPath),
        [System.IO.Path]::GetFullPath($TargetPath),
        $Description)

    if (-not (Test-Path -LiteralPath $ShortcutPath -PathType Leaf)) {
        throw "Shortcut was not created: $ShortcutPath"
    }
}

function Get-PortableReleaseIdentity {
    if (-not (Test-Path -LiteralPath $buildPropsPath -PathType Leaf)) {
        throw "Build metadata is missing: $buildPropsPath"
    }

    try {
        [xml]$props = Get-Content -LiteralPath $buildPropsPath -Raw
    } catch {
        throw "Unable to read build metadata from Directory.Build.props: $($_.Exception.Message)"
    }

    $versionText = [string]$props.Project.PropertyGroup.Version
    $fileVersionText = [string]$props.Project.PropertyGroup.FileVersion
    if ([string]::IsNullOrWhiteSpace($versionText) -or [string]::IsNullOrWhiteSpace($fileVersionText)) {
        throw 'Directory.Build.props must define both Version and FileVersion for release naming.'
    }

    try {
        $parsedFileVersion = [Version]::Parse($fileVersionText)
    } catch {
        throw "FileVersion is not a valid numeric version: $fileVersionText"
    }

    if ($parsedFileVersion.Revision -lt 0) {
        throw "FileVersion must contain a fourth numeric component used as the release build number: $fileVersionText"
    }

    $expectedPrefix = "$versionText."
    if (-not $fileVersionText.StartsWith($expectedPrefix, [StringComparison]::Ordinal)) {
        throw "Release version metadata is inconsistent. Version=$versionText, FileVersion=$fileVersionText"
    }

    $buildNumber = $parsedFileVersion.Revision
    [PSCustomObject]@{
        Version = $versionText
        Build = $buildNumber
        FileName = "PinkieSysMon_${versionText}-${buildNumber}.zip"
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

    if ($null -eq $config.Dashboard -or $null -eq $config.MetricProviders -or
        $null -eq $config.Outputs -or $null -eq $config.Media) {
        throw 'Release config source is missing required public configuration sections.'
    }

    $config.Dashboard.Active = 'Default'
    $config.MetricProviders.system = $true
    $config.MetricProviders.lhm = $false
    $config.MetricProviders.icue = $false

    foreach ($target in @($config.Outputs.Targets)) {
        if ($null -ne $target) {
            $target.DeviceId = ''
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
        "Pinkie's System Monitor.lnk",
        'PinkieSysMon Dashboard Editor.lnk'
    )

    foreach ($relative in $requiredFiles) {
        $path = Join-Path $AppRoot $relative
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
            throw "Portable release staging is incomplete. Missing: $path"
        }
    }

    foreach ($relative in @('assets\fonts\Roboto', 'assets\icons\Lucide', 'dashboards\Default')) {
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
    $unexpectedDashboards = @(Get-ChildItem -LiteralPath $dashboardRoot -Directory -Force | Where-Object Name -ne 'Default')
    if ($unexpectedDashboards.Count -ne 0) {
        throw "Portable release contains unexpected dashboards: $($unexpectedDashboards.Name -join ', ')"
    }

    $configPath = Join-Path $AppRoot 'config\app.json'
    $config = Get-Content -LiteralPath $configPath -Raw | ConvertFrom-Json
    if ([string]$config.Dashboard.Active -ne 'Default') {
        throw 'Portable release config must select the Default dashboard.'
    }
    if ($config.MetricProviders.system -ne $true -or
        $config.MetricProviders.lhm -ne $false -or
        $config.MetricProviders.icue -ne $false) {
        throw 'Portable release config must enable only the System metric provider.'
    }
    foreach ($target in @($config.Outputs.Targets)) {
        if ($null -ne $target -and -not [string]::IsNullOrWhiteSpace([string]$target.DeviceId)) {
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
        Copy-DirectoryContent -Source (Join-Path $root 'dashboards\Default') -Destination (Join-Path $releaseStageAppRoot 'dashboards\Default')
        Write-PortableReleaseConfig -DestinationPath (Join-Path $releaseStageAppRoot 'config\app.json')
        Copy-Item -LiteralPath (Join-Path $root 'LICENSE') -Destination (Join-Path $releaseStageAppRoot 'LICENSE') -Force
        Copy-Item -LiteralPath (Join-Path $root 'THIRD-PARTY-NOTICES.md') -Destination (Join-Path $releaseStageAppRoot 'THIRD-PARTY-NOTICES.md') -Force

        foreach ($definition in $portableShortcutDefinitions) {
            New-PortableShellLink `
                -ShortcutPath (Join-Path $releaseStageAppRoot $definition.Name) `
                -TargetPath (Join-Path (Join-Path $releaseStageAppRoot 'bin') $definition.Target) `
                -Description $definition.Description
        }

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
                "PinkieSysMon/Pinkie's System Monitor.lnk",
                'PinkieSysMon/PinkieSysMon Dashboard Editor.lnk'
            )) {
                if ($required -notin $entries) {
                    throw "Portable release ZIP integrity check failed. Missing entry: $required"
                }
            }

            $forbiddenEntryPatterns = @(
                '^PinkieSysMon/assets/fonts/(?!Roboto(?:/|$))',
                '^PinkieSysMon/assets/icons/(?!Lucide(?:/|$))',
                '^PinkieSysMon/dashboards/(?!Default(?:/|$))',
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
    Write-Host 'Release defaults: dashboard=Default; providers=System only'
    Write-Host 'Release assets: Roboto + Lucide only'
}

function Assert-PublishPackage {
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
    $names = @('PinkieSysMon', 'PinkieSysMon.Editor')
    $running = @()

    foreach ($name in $names) {
        $running += @(Get-Process -Name $name -ErrorAction SilentlyContinue)
    }

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
            Relative = 'assets\fonts\Roboto'
            Source = (Join-Path $root 'assets\fonts\Roboto')
        },
        [PSCustomObject]@{
            Relative = 'assets\icons\Lucide'
            Source = (Join-Path $root 'assets\icons\Lucide')
        },
        [PSCustomObject]@{
            Relative = 'dashboards\Default'
            Source = (Join-Path $root 'dashboards\Default')
        }
    )

    try {
        Write-Host ''
        Write-Host "Preparing local deployment staging under $LocalRoot ..."
        foreach ($item in $managed) {
            $staged = Join-Path $stagingRoot $item.Relative
            Copy-DirectoryContent -Source $item.Source -Destination $staged
        }

        # Validate staging before touching the live managed paths.
        foreach ($name in @('PinkieSysMon.exe', 'PinkieSysMon.dll', 'PinkieSysMon.Editor.exe', 'PinkieSysMon.Editor.dll')) {
            $path = Join-Path (Join-Path $stagingRoot 'bin') $name
            if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
                throw "Local deployment staging is incomplete. Missing: $path"
            }
        }

        New-Item -ItemType Directory -Path $backupRoot -Force | Out-Null

        Write-Host 'Replacing managed local product paths...'
        $activated = New-Object System.Collections.Generic.List[string]
        $backedUp = New-Object System.Collections.Generic.List[string]
        $activatedShortcuts = New-Object System.Collections.Generic.List[string]
        $backedUpShortcuts = New-Object System.Collections.Generic.List[string]

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

            # Root launch shortcuts are developer-managed portable entry points.
            # They deliberately stay inside the installation root; Publish local
            # never creates Desktop or Start Menu shortcuts.
            foreach ($definition in $portableShortcutDefinitions) {
                $shortcutPath = Join-Path $LocalRoot $definition.Name
                $shortcutBackup = Join-Path $backupRoot $definition.Name

                if (Test-Path -LiteralPath $shortcutPath -PathType Leaf) {
                    Move-Item -LiteralPath $shortcutPath -Destination $shortcutBackup -Force
                    $backedUpShortcuts.Add($definition.Name)
                }

                $targetPath = Join-Path (Join-Path $LocalRoot 'bin') $definition.Target
                New-PortableShellLink `
                    -ShortcutPath $shortcutPath `
                    -TargetPath $targetPath `
                    -Description $definition.Description
                $activatedShortcuts.Add($definition.Name)
            }

            foreach ($name in @('PinkieSysMon.exe', 'PinkieSysMon.dll', 'PinkieSysMon.Editor.exe', 'PinkieSysMon.Editor.dll')) {
                $path = Join-Path (Join-Path $LocalRoot 'bin') $name
                if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
                    throw "Local deployment verification failed. Missing: $path"
                }
            }

            foreach ($definition in $portableShortcutDefinitions) {
                $shortcutPath = Join-Path $LocalRoot $definition.Name
                if (-not (Test-Path -LiteralPath $shortcutPath -PathType Leaf)) {
                    throw "Local deployment verification failed. Missing shortcut: $shortcutPath"
                }
            }
        } catch {
            Write-Warning 'Local publish failed after deployment began. Rolling managed paths back.'

            foreach ($name in @($activatedShortcuts)) {
                Remove-Item -LiteralPath (Join-Path $LocalRoot $name) -Force -ErrorAction SilentlyContinue
            }

            foreach ($name in @($backedUpShortcuts)) {
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
        Write-Host 'Updated public dashboard: Default'
        Write-Host "Updated root launch shortcuts: $($portableShortcutDefinitions.Name -join ', ')"
        Write-Host 'Desktop / Start Menu shortcuts: not created'
        Write-Host 'Preserved: config, PinkiePie_Vertical, private/local assets, logs'
        Write-Host 'Application was left stopped.'
    } finally {
        Remove-Item -LiteralPath $stagingRoot -Recurse -Force -ErrorAction SilentlyContinue
        Remove-Item -LiteralPath $backupRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
}

function Assert-GitHubPublishReady {
    if (-not (Get-Command git -ErrorAction SilentlyContinue)) {
        throw 'git.exe is not installed or is not in PATH.'
    }

    $gitDir = Join-Path $root '.git'
    if (-not (Test-Path -LiteralPath $gitDir -PathType Container)) {
        throw 'GitHub publish is not ready: this developer tree has not been initialized as a Git repository.'
    }

    foreach ($required in @('README.md', 'LICENSE', 'THIRD-PARTY-NOTICES.md')) {
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
        if ($null -ne $target -and -not [string]::IsNullOrWhiteSpace([string]$target.DeviceId)) {
            throw 'GitHub publish blocked: public config contains a device-specific DeviceId.'
        }
    }

    $endpointTypeOverrides = $publicConfig.Media.EndpointTypeOverrides
    if ($null -ne $endpointTypeOverrides -and
        @($endpointTypeOverrides.PSObject.Properties).Count -ne 0) {
        throw 'GitHub publish blocked: public config contains machine-specific media endpoint overrides.'
    }

    $remote = (& git -C $root remote get-url origin 2>$null)
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace(($remote | Out-String))) {
        throw 'GitHub publish is not ready: Git remote "origin" is not configured.'
    }
}

function Publish-GitHub {
    Assert-GitHubPublishReady

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

    & git -C $root diff --cached --check
    if ($LASTEXITCODE -ne 0) {
        throw 'GitHub publish blocked: git diff --cached --check reported errors.'
    }

    if ($staged.Count -eq 0) {
        Write-Host 'No source changes to commit.'
    } else {
        Write-Host ''
        Write-Host 'Staged changes:'
        $staged | ForEach-Object { Write-Host "  $_" }

        $defaultMessage = 'PinkieSysMon update ' + (Get-Date -Format 'yyyy-MM-dd HH:mm')
        $message = Read-Host "Commit message [$defaultMessage]"
        if ([string]::IsNullOrWhiteSpace($message)) {
            $message = $defaultMessage
        }

        Invoke-External git '-C' $root 'commit' '-m' $message
    }

    $branchRaw = (& git -C $root branch --show-current | Select-Object -First 1)
    $branch = if ($null -eq $branchRaw) { '' } else { $branchRaw.Trim() }
    if ([string]::IsNullOrWhiteSpace($branch)) {
        throw 'GitHub publish blocked: Git is in detached HEAD state.'
    }

    # Always push through the project's authoritative remote and refresh upstream tracking.
    # This is valid for both the first publish and subsequent publishes, and avoids
    # platform-specific failure modes when probing a branch that has no upstream yet.
    Invoke-External git '-C' $root 'push' '-u' 'origin' $branch

    Write-Host ''
    Write-Host 'GITHUB PUBLISH: PASS'
}

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw '.NET 10 SDK is not installed or dotnet.exe is not in PATH.'
}

if (-not (Test-Path -LiteralPath $signingTools -PathType Leaf)) {
    throw "Code-signing helper is missing: $signingTools"
}
. $signingTools

foreach ($project in @($runtimeProject, $editorProject, $testsProject)) {
    if (-not (Test-Path -LiteralPath $project -PathType Leaf)) {
        throw "Required project is missing: $project"
    }
}

$version = & dotnet --version
Write-Host ".NET SDK: $version"
Write-Host "Developer root: $root"

# Every successful build starts from a deterministic clean generated state.
Remove-DevelopmentArtifacts
New-Item -ItemType Directory -Path $publishOut -Force | Out-Null
New-Item -ItemType Directory -Path $testsOut -Force | Out-Null

try {
    Write-Host ''
    Write-Host 'Publishing PinkieSysMon runtime...'
    Invoke-External dotnet 'publish' $runtimeProject '-c' 'Release' '-r' 'win-x64' '--self-contained' 'true' '-o' $publishOut

    Write-Host ''
    Write-Host 'Publishing PinkieSysMon Dashboard Editor...'
    Invoke-External dotnet 'publish' $editorProject '-c' 'Release' '-r' 'win-x64' '--self-contained' 'true' '-o' $publishOut

    Remove-UnsupportedPublishCultures
    Assert-PublishPackage

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
            (Join-Path $publishOut 'PinkieSysMon.Editor.dll')
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
    Write-Host 'Building regression checks...'
    Invoke-External dotnet 'build' $testsProject '-c' 'Release' '-o' $testsOut

    $testsDll = Join-Path $testsOut 'PinkieSysMon.RegressionTests.dll'
    if (-not (Test-Path -LiteralPath $testsDll -PathType Leaf)) {
        throw "Regression test assembly was not produced: $testsDll"
    }

    # Regression checks load the exact published Runtime/Editor assemblies and
    # app-local native dependencies beside the test host.
    Get-ChildItem -LiteralPath $publishOut -Force | ForEach-Object {
        Copy-Item -LiteralPath $_.FullName -Destination $testsOut -Recurse -Force
    }

    Write-Host ''
    Write-Host 'Running regression checks...'
    Invoke-External dotnet $testsDll

    Assert-PublishPackage

    Write-Host ''
    Write-Host 'BUILD/REGRESSION: PASS'
    Write-Host "Published package: $publishOut"

    if ($Action -eq 'Prompt') {
        Write-Host ''
        Write-Host '[1] End - clean generated artifacts and exit'
        Write-Host '[2] Publish local - update C:\PinkieSysMon and build portable release ZIP, leave app stopped'
        Write-Host '[3] Publish GitHub - clean artifacts, audit, commit and push'

        do {
            $choice = Read-Host 'Choose 1, 2 or 3'
        } until ($choice -in @('1', '2', '3'))

        switch ($choice) {
            '1' { $Action = 'End' }
            '2' { $Action = 'Local' }
            '3' { $Action = 'GitHub' }
        }
    }

    switch ($Action) {
        'End' {
            Remove-DevelopmentArtifacts
            Write-Host ''
            Write-Host 'END: generated artifacts cleaned; source tree unchanged.'
        }
        'Local' {
            $preparedRelease = Prepare-PortableReleasePackage
            Publish-Local
            Commit-PortableReleasePackage -PreparedPackage $preparedRelease
            Remove-DevelopmentArtifacts
        }
        'GitHub' {
            # Git must see only source/public material, never generated build output.
            Remove-DevelopmentArtifacts
            Publish-GitHub
        }
        default {
            throw "Unsupported action: $Action"
        }
    }
} catch {
    Write-Host ''
    Write-Host 'BUILD/PUBLISH: FAIL' -ForegroundColor Red
    Write-Host $_.Exception.Message -ForegroundColor Red
    Write-Host "Generated diagnostic artifacts were preserved under: $artifactsRoot"
    throw
}
