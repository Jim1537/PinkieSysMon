# Pull the current branch safely, then build and run regressions.
# Run from VS Code with "Run PowerShell File"; no arguments are required.
# This script never commits, pushes, installs, or starts PinkieSysMon.

[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$buildScript = Join-Path $PSScriptRoot 'build.ps1'

if (-not (Test-Path -LiteralPath $buildScript -PathType Leaf)) {
    throw "Missing build.ps1 next to pull-build.ps1: $buildScript"
}

try {
    Write-Host ''
    Write-Host '=== 1/2: Sync source from GitHub (fast-forward only) ==='
    & $buildScript -Action Sync
    if (-not $?) {
        throw 'GitHub synchronization failed. Build was not started.'
    }

    $head = @(& git -C $PSScriptRoot rev-parse HEAD)
    if ($LASTEXITCODE -ne 0 -or $head.Count -ne 1) {
        throw 'Could not determine the synchronized Git commit. Build was not started.'
    }

    Write-Host ''
    Write-Host "Source commit: $($head[0])"
    Write-Host '=== 2/2: Build Runtime + Editor and run regressions ==='
    & $buildScript -Action End
    if (-not $?) {
        throw 'Build or regression checks failed.'
    }

    Write-Host ''
    Write-Host "PULL-BUILD: PASS ($($head[0]))" -ForegroundColor Green
} catch {
    Write-Host ''
    Write-Host 'PULL-BUILD: FAIL' -ForegroundColor Red
    throw
}
