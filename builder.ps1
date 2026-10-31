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

function Convert-PinkieRegressionTranscript {
    param([Parameter(Mandatory = $true)][string]$Path)

    # The live PowerShell 5.1 transcript splits Write-Host -NoNewline
    # into character-per-line records. Preserve live dots and reconstruct
    # only recognizable, complete regression rows after Stop-Transcript.
    $lines = [System.IO.File]::ReadAllLines($Path)
    $output = New-Object 'System.Collections.Generic.List[string]'
    $converted = 0
    $index = 0

    while ($index -lt $lines.Length) {
        if ($lines[$index] -ne 'External command: dotnet regression runner') {
            $output.Add($lines[$index])
            $index++
            continue
        }

        $end = $index + 1
        while ($end -lt $lines.Length -and $lines[$end] -notmatch '^External exit code:') {
            $end++
        }

        $cursor = $index + 1
        $rows = New-Object 'System.Collections.Generic.List[string]'
        $passed = 0
        $total = 0
        while (($cursor + 69) -le $end) {
            # Runner contract: two spaces, 34-column label, " [",
            # 24 markers, "] ", 3-column passed count, "/" and total.
            $prefix = New-Object System.Text.StringBuilder
            $valid = $true
            for ($j = 0; $j -lt 68; $j++) {
                $part = $lines[$cursor + $j]
                if ($part.Length -gt 1) {
                    $valid = $false
                    break
                }
                if ($part.Length -eq 0) { [void]$prefix.Append(' ') }
                else { [void]$prefix.Append($part) }
            }
            if (-not $valid -or $prefix.ToString() -cnotmatch '^  .{34} \[[.! ]{24}\] [ 0-9]{3}/$') {
                break
            }

            $cursor += 68
            $digits = New-Object System.Text.StringBuilder
            while ($cursor -lt $end -and $lines[$cursor] -cmatch '^\d$') {
                [void]$digits.Append($lines[$cursor])
                $cursor++
            }
            if ($digits.Length -eq 0 -or $cursor -ge $end -or $lines[$cursor] -ne '') {
                break
            }

            $row = $prefix.ToString() + $digits.ToString()
            $counts = [regex]::Match($row, '(\d+)/(\d+)$')
            if (-not $counts.Success) { break }
            $passed += [int]$counts.Groups[1].Value
            $total += [int]$counts.Groups[2].Value
            $rows.Add($row)
            $cursor++ # Original line break after the row.
        }

        $summaryText = New-Object System.Text.StringBuilder
        if ($rows.Count -gt 0) {
            for ($j = 0; $j -lt 128 -and $cursor -lt $end; $j++) {
                $part = $lines[$cursor]
                if ($part.Length -gt 1) { break }
                if ($part.Length -eq 0) { [void]$summaryText.Append(' ') }
                else { [void]$summaryText.Append($part) }
                $cursor++
                if ($part -eq '.') { break }
            }
        }
        $summary = $summaryText.ToString()
        $counts = [regex]::Match($summary, '^Regression checks: (\d+)/(\d+) passed\.$')
        if ($rows.Count -gt 0 -and $counts.Success -and
            $passed -eq [int]$counts.Groups[1].Value -and
            $total -eq [int]$counts.Groups[2].Value) {
            $output.Add($lines[$index])
            foreach ($row in $rows) { $output.Add($row) }
            $output.Add($summary)
            for ($j = $cursor; $j -lt $end; $j++) { $output.Add($lines[$j]) }
            $converted++
        } else {
            # Fail closed: retain incomplete output and all diagnostics verbatim.
            for ($j = $index; $j -lt $end; $j++) { $output.Add($lines[$j]) }
        }
        $index = $end
    }

    if ($converted -eq 0) { return }

    $temporary = Join-Path (Split-Path -Parent $Path) ([System.IO.Path]::GetRandomFileName())
    $backup = "$temporary.backup"
    $replaced = $false
    try {
        $utf8WithBom = New-Object System.Text.UTF8Encoding($true)
        [System.IO.File]::WriteAllLines($temporary, [string[]]$output.ToArray(), $utf8WithBom)
        # Use a real backup path: Windows PowerShell 5.1 marshals null-like
        # string arguments unpredictably for this .NET Framework method.
        [System.IO.File]::Replace($temporary, $Path, $backup)
        $replaced = $true
    } finally {
        if (Test-Path -LiteralPath $temporary -PathType Leaf) {
            Remove-Item -LiteralPath $temporary -Force
        }
        # A failed replacement must retain any original transcript backup.
        if ($replaced -and (Test-Path -LiteralPath $backup -PathType Leaf)) {
            Remove-Item -LiteralPath $backup -Force
        }
    }
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
            $pendingChanges = 0
            if (Get-Command git -ErrorAction SilentlyContinue) {
                $branch = @(& git -C $root branch --show-current)
                $head = @(& git -C $root rev-parse HEAD)
                if ($LASTEXITCODE -eq 0 -and $head.Count -eq 1) {
                    Write-Host "Git branch: $($branch -join '')"
                    Write-Host "Git HEAD: $($head[0])"
                }
                $changes = @(& git -C $root status --short)
                if ($LASTEXITCODE -eq 0) {
                    $pendingChanges = $changes.Count
                    Write-Host "Git status entries: $($changes.Count)"
                    foreach ($change in $changes) { Write-Host "  $change" }
                }
            }
            $parameters = @{ Action = $action }
            if ($action -eq 'GitHubOnly' -and $pendingChanges -gt 0) {
                # Commit-message input is owned by this interactive wrapper.
                $defaultMessage = 'PinkieSysMon update ' + (Get-Date -Format 'yyyy-MM-dd HH:mm')
                $message = Read-Host "Commit message [$defaultMessage]"
                if ([string]::IsNullOrWhiteSpace($message)) {
                    $message = $defaultMessage
                }
                $parameters.CommitMessage = $message
            }
            & $buildScript @parameters
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
    if ($logging) {
        Stop-Transcript | Out-Null
        Convert-PinkieRegressionTranscript -Path $logPath
    }
}
