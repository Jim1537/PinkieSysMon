# Legacy noninteractive convenience workflow: pull, build, run regressions.
# Run from VS Code with "Run PowerShell File"; no arguments are required.
# This script never commits, pushes, installs, or starts PinkieSysMon.
# Generated output is retained and can be installed later from builder.ps1.

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
    & $buildScript -Action BuildOnly
    if (-not $?) {
        throw 'Build failed; regression checks were not started.'
    }
    & $buildScript -Action RegressionOnly
    if (-not $?) {
        throw 'Regression checks failed.'
    }

    Write-Host ''
    Write-Host "PULL-BUILD: PASS ($($head[0]))" -ForegroundColor Green
} catch {
    Write-Host ''
    Write-Host 'PULL-BUILD: FAIL' -ForegroundColor Red
    throw
}
