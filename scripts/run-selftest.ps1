<#
.SYNOPSIS
    Runs the in-process self-tests of Quill.Playground and prints the report.

.DESCRIPTION
    Launches Quill.Playground.exe with --selftest <report>, waits for the app to exit,
    then prints the report. Exits with 1 when any check failed or the report is missing.
#>
[CmdletBinding()]
param(
    [string]$Configuration = "Debug",
    [int]$TimeoutSeconds = 180,
    [string]$Only,
    [switch]$NoBuild
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot

if (-not $NoBuild) {
    & (Join-Path $PSScriptRoot "build-playground.ps1") -Configuration $Configuration
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

$exe = Join-Path $repoRoot "src/Quill.Playground/bin/x64/$Configuration/net10.0-windows10.0.19041.0/Quill.Playground.exe"
$tempDir = Join-Path $repoRoot ".codex/temp"
New-Item -ItemType Directory -Force $tempDir | Out-Null
$report = Join-Path $tempDir "selftest-report.txt"
Remove-Item $report -ErrorAction SilentlyContinue

$arguments = @("--selftest", "`"$report`"")
if ($Only) { $arguments += @("--only", $Only) }
$process = Start-Process -FilePath $exe -ArgumentList $arguments -PassThru
if (-not $process.WaitForExit($TimeoutSeconds * 1000)) {
    Stop-Process -Id $process.Id -Force
    Write-Error "Self-test timed out after $TimeoutSeconds seconds."
    exit 1
}

if (-not (Test-Path $report)) {
    Write-Error "Self-test report was not written (exit code $($process.ExitCode))."
    exit 1
}

Get-Content $report
$summary = Select-String -Path $report -Pattern "Summary: \d+ passed, (\d+) failed" | Select-Object -First 1
if ($null -eq $summary -or [int]$summary.Matches[0].Groups[1].Value -gt 0) { exit 1 }
exit 0
