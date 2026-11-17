#Requires -Version 5.1
<#
.SYNOPSIS
    Builds the win-x64 publish output and compiles the Inno Setup installer.

.DESCRIPTION
    Requires Inno Setup 6 (ISCC.exe). Produces
    artifacts\installer\LMTrainingDataStudio-Setup-<version>.exe.

    The installer registers .recipe file associations and adds the app to the
    "Open with" list for .json files, and launches a single application instance
    that forwards later file opens to the running window.
#>
param(
    [string]$Configuration = "Release",
    [switch]$SkipPublish
)

$ErrorActionPreference = "Stop"

$root = Resolve-Path (Join-Path $PSScriptRoot "..")
$publishScript = Join-Path $PSScriptRoot "publish-win-x64.ps1"
$iss = Join-Path $root "packaging\inno\LMTrainingDataStudio.iss"

if (-not $SkipPublish) {
    Write-Host "Publishing win-x64..." -ForegroundColor Cyan
    & powershell -ExecutionPolicy Bypass -File $publishScript -Configuration $Configuration
    if ($LASTEXITCODE -ne 0) { throw "publish script failed." }
}

Write-Host "Locating ISCC.exe..." -ForegroundColor Cyan
$iscc = (Get-Command ISCC.exe -ErrorAction SilentlyContinue | Select-Object -First 1).Source
if (-not $iscc) {
    $candidates = @(
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
        "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
    )
    $iscc = $candidates | Where-Object { Test-Path $_ } | Select-Object -First 1
}

if (-not $iscc) {
    throw "ISCC.exe was not found. Install Inno Setup 6 from https://jrsoftware.org/isinfo.php."
}

Write-Host "Compiling installer..." -ForegroundColor Cyan
& $iscc $iss
if ($LASTEXITCODE -ne 0) { throw "ISCC failed." }

Write-Host "Installer created under artifacts\installer." -ForegroundColor Green
