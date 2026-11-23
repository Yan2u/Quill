<#
.SYNOPSIS
    Builds the Quill application (Debug, x64) inside the Visual Studio developer environment.
#>
[CmdletBinding()]
param(
    [string]$Configuration = "Debug"
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repoRoot "src/Quill/Quill.csproj"

$vsInstallPath = "D:\Visual Studio\18\insiders"
$devShellModule = Join-Path $vsInstallPath "Common7\Tools\Microsoft.VisualStudio.DevShell.dll"
if (Test-Path $devShellModule) {
    Import-Module $devShellModule -ErrorAction Stop
    Enter-VsDevShell -VsInstallPath $vsInstallPath -Arch amd64 -HostArch amd64 -SkipAutomaticLocation | Out-Null
} else {
    $cwd = $PWD.Path
    & (Join-Path $vsInstallPath "Common7\Tools\Launch-VsDevShell.ps1") -Arch amd64 -HostArch amd64 | Out-Null
    Set-Location $cwd
}

dotnet build $project -c $Configuration -nologo -clp:"ErrorsOnly;Summary"
exit $LASTEXITCODE
