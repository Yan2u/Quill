<#
.SYNOPSIS
    Publishes Quill as an unpackaged x64 Windows release (.NET 10 framework-dependent, Windows App SDK self-contained)
    and creates a distributable ZIP archive.
#>
[CmdletBinding()]
param(
    [string]$Version = "0.1.0",
    [string]$Configuration = "Release",
    [string]$OutputRoot = "publish"
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repoRoot "src/Quill/Quill.csproj"
$outBase = if ([System.IO.Path]::IsPathRooted($OutputRoot)) { $OutputRoot } else { Join-Path $repoRoot $OutputRoot }
$packageName = "Quill-$Version-win-x64"
$stageDir = Join-Path $outBase $packageName
$zipPath = Join-Path $outBase "$packageName.zip"

$cwd = $PWD.Path
& "D:\Visual Studio\18\insiders\Common7\Tools\Launch-VsDevShell.ps1" -Arch amd64 -HostArch amd64 | Out-Null
Set-Location $cwd

if (Test-Path $stageDir) {
    Remove-Item $stageDir -Recurse -Force
}
if (Test-Path $zipPath) {
    Remove-Item $zipPath -Force
}
New-Item -ItemType Directory -Force $outBase | Out-Null

dotnet publish $project `
    -c $Configuration `
    -r win-x64 `
    --self-contained false `
    -p:DebugType=none `
    -p:DebugSymbols=false `
    -p:Version=$Version `
    -o $stageDir

if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

Get-ChildItem $stageDir -Recurse -Filter *.pdb -File -ErrorAction SilentlyContinue | Remove-Item -Force

foreach ($doc in @("README.md", "README.zh-CN.md", "THIRD-PARTY-NOTICES.md")) {
    $src = Join-Path $repoRoot $doc
    if (-not (Test-Path $src)) {
        throw "Missing repository notice/documentation file: $src"
    }
    Copy-Item $src (Join-Path $stageDir $doc) -Force
}

$required = @(
    "Quill.exe",
    "Quill.pri",
    "App.xbf",
    "MainWindow.xbf",
    "Shell\ChromeResources.xbf",
    "Quill.Shell\Sidebar\FilesPanel.xbf",
    "Quill.Shell\Pages\OpenPage.xbf",
    "Quill.Shell\Pages\ExportPage.xbf",
    "quill_native.dll",
    "Assets\Quill.ico",
    "Assets\Quill-Markdown.ico",
    "Assets\Quill-256.png",
    "Assets\Quill-nav.svg",
    "Assets\KaTeX",
    "Microsoft.ui.xaml.dll"
)
foreach ($rel in $required) {
    $target = Join-Path $stageDir $rel
    if (-not (Test-Path $target)) {
        throw "Required release artifact missing from staged output: $rel"
    }
}

if (Test-Path (Join-Path $stageDir "coreclr.dll")) {
    throw "Unexpected coreclr.dll found in framework-dependent release output."
}

$verifyReport = Join-Path ([System.IO.Path]::GetTempPath()) ("quill-verify-{0}.txt" -f [System.Guid]::NewGuid().ToString("N"))
try {
    $proc = Start-Process -FilePath (Join-Path $stageDir "Quill.exe") -ArgumentList @("--verify-startup", "`"$verifyReport`"") -PassThru
    if (-not $proc.WaitForExit(30000)) {
        Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue
        throw "Published Quill.exe startup verification timed out after 30 seconds."
    }
    if ($proc.ExitCode -ne 0) {
        $detail = if (Test-Path $verifyReport) { Get-Content $verifyReport -Raw } else { "(no report written)" }
        throw "Published Quill.exe startup verification failed with exit code $($proc.ExitCode): $detail"
    }
    if (-not (Test-Path $verifyReport)) {
        throw "Published Quill.exe startup verification produced no report."
    }
    $verifyContent = Get-Content $verifyReport -Raw
    if ($verifyContent -notmatch "(?m)^OK\r?$" -or $verifyContent -notmatch "(?m)^foregroundUnchanged=True\r?$") {
        throw "Published Quill.exe startup verification failed:`n$verifyContent"
    }
    Write-Output "Startup verification: OK (foregroundUnchanged=True)"
}
finally {
    Remove-Item $verifyReport -Force -ErrorAction SilentlyContinue
}

Compress-Archive -Path (Join-Path $stageDir "*") -DestinationPath $zipPath -CompressionLevel Optimal

$files = Get-ChildItem $stageDir -Recurse -File
$totalBytes = ($files | Measure-Object -Property Length -Sum).Sum
$zipBytes = (Get-Item $zipPath).Length

Write-Output ("Staged directory: {0}" -f $stageDir)
Write-Output ("Staged files: {0}, size: {1:N2} MB" -f $files.Count, ($totalBytes / 1MB))
Write-Output ("Release archive: {0}, size: {1:N2} MB" -f $zipPath, ($zipBytes / 1MB))
