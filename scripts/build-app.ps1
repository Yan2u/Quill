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

$cwd = $PWD.Path
& "D:\Visual Studio\18\insiders\Common7\Tools\Launch-VsDevShell.ps1" -Arch amd64 -HostArch amd64 | Out-Null
Set-Location $cwd

dotnet build $project -c $Configuration -nologo -clp:"ErrorsOnly;Summary"
exit $LASTEXITCODE
