# Interactive PinkieSysMon development/build/publish workflow.
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2.0

$root = $PSScriptRoot
$buildScript = Join-Path $root 'build.ps1'
$logPath = Join-Path $root 'builder.log'

if (-not (Test-Path -LiteralPath $buildScript -PathType Leaf)) {
    throw "Required authoritative build script missing: $buildScript"
}

$logging = $false
try {
    # Fail closed if full-session logging cannot start.
    Start-Transcript -LiteralPath $logPath -Force -ErrorAction Stop | Out-Null
    $logging = $true
    Write-Host 'PinkieSysMon Builder'
    Write-Host "Started: $((Get-Date).ToString('o'))"
    Write-Host "Source root: $root"
    Write-Host "PowerShell version: $($PSVersionTable.PSVersion)"
    Write-Host "Session log: $logPath"

    while ($true) {
        Write-Host ''
        Write-Host '=== PinkieSysMon Builder ==='
        Write-Host '1 - pull'
        Write-Host '2 - build'
        Write-Host '3 - regression checks'
        Write-Host '4 - local publish'
        Write-Host '5 - push'
        Write-Host '0 - exit'
        $choice = Read-Host 'Select action'

        if ($choice -eq '0') {
            Write-Host "Finished: $((Get-Date).ToString('o'))"
            break
        }

        $action = switch ($choice) {
            '1' { 'Sync' }
            '2' { 'BuildOnly' }
            '3' { 'RegressionOnly' }
            '4' { 'LocalOnly' }
            '5' { 'GitHubOnly' }
            default { $null }
        }
        if ($null -eq $action) {
            Write-Host 'Invalid selection. Choose 0 through 5.' -ForegroundColor Yellow
            continue
        }
        $labels = @{
            '1' = 'PULL'
            '2' = 'BUILD'
            '3' = 'REGRESSION CHECKS'
            '4' = 'LOCAL PUBLISH'
            '5' = 'PUSH'
        }
        $label = $labels[$choice]
        $started = Get-Date
        Write-Host ''
        Write-Host ("=== {0}: START ({1}) ===" -f $label, $started.ToString('o'))

        try {
            if (Get-Command git -ErrorAction SilentlyContinue) {
                $branch = @(& git -C $root branch --show-current)
                $head = @(& git -C $root rev-parse HEAD)
                if ($LASTEXITCODE -eq 0 -and $head.Count -eq 1) {
                    Write-Host "Git branch: $($branch -join '')"
                    Write-Host "Git HEAD: $($head[0])"
                }
                $changes = @(& git -C $root status --short)
                if ($LASTEXITCODE -eq 0) {
                    Write-Host "Git status entries: $($changes.Count)"
                    foreach ($change in $changes) { Write-Host "  $change" }
                }
            }
            & $buildScript -Action $action
            if (-not $?) { throw "$label returned an unsuccessful status." }
            Write-Host ("=== {0}: PASS ({1}s) ===" -f $label, [int]((Get-Date) - $started).TotalSeconds) -ForegroundColor Green
        } catch {
            Write-Host ("=== {0}: FAIL ({1}s) ===" -f $label, [int]((Get-Date) - $started).TotalSeconds) -ForegroundColor Red
            Write-Host $_.Exception.Message -ForegroundColor Red
            if (-not [string]::IsNullOrWhiteSpace($_.ScriptStackTrace)) {
                Write-Host $_.ScriptStackTrace -ForegroundColor DarkRed
            }
        }
        Write-Host "Back to menu. Log: $logPath"
    }
} finally {
    if ($logging) { Stop-Transcript | Out-Null }
}
