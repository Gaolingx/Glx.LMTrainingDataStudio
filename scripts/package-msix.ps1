#Requires -Version 5.1
<#
.SYNOPSIS
    Builds a self-contained win-x64 publish output and packages it as an MSIX.

.DESCRIPTION
    Requires the Windows SDK (makeappx.exe). The script generates the MSIX logo
    assets from the application icon, copies AppxManifest.xml, and produces
    artifacts\msix\LLMTrainingDataStudio.msix.

    Signing is optional; pass -CertificatePath and -CertificatePassword to sign
    with signtool, otherwise the MSIX is unsigned (Developer Mode can install it).
#>
param(
    [string]$Configuration = "Release",
    [string]$Version = "1.0.0.0",
    [string]$CertificatePath,
    [string]$CertificatePassword
)

$ErrorActionPreference = "Stop"

$root = Resolve-Path (Join-Path $PSScriptRoot "..")
$solution = Join-Path $root "LMTrainingDataStudio2\LMTrainingDataStudio2.slnx"
$project = Join-Path $root "LMTrainingDataStudio2\LMTrainingDataStudio2\LMTrainingDataStudio2.csproj"
$icon = Join-Path $root "LMTrainingDataStudio2\LMTrainingDataStudio2\Assets\app-icon.png"
$publish = Join-Path $root "artifacts\msix\AppX"
$outputDirectory = Join-Path $root "artifacts\msix"
$msixPath = Join-Path $outputDirectory "LLMTrainingDataStudio.msix"
$manifest = Join-Path $root "packaging\msix\AppxManifest.xml"

Write-Host "Publishing $Configuration win-x64..." -ForegroundColor Cyan
if (Test-Path $publish) { Remove-Item $publish -Recurse -Force }
New-Item -ItemType Directory -Path $publish -Force | Out-Null

dotnet publish $project -c $Configuration -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:Version=$Version -o $publish
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed." }

Copy-Item $manifest (Join-Path $publish "AppxManifest.xml") -Force

Write-Host "Generating MSIX assets..." -ForegroundColor Cyan
Add-Type -AssemblyName System.Drawing
$assets = Join-Path $publish "Assets"
New-Item -ItemType Directory -Path $assets -Force | Out-Null
$source = [System.Drawing.Image]::FromFile($icon)

function Save-SquareAsset([int]$size, [string]$path) {
    $bitmap = New-Object System.Drawing.Bitmap $size, $size
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $graphics.Clear([System.Drawing.Color]::Transparent)
    $graphics.DrawImage($source, 0, 0, $size, $size)
    $bitmap.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $graphics.Dispose()
    $bitmap.Dispose()
}

try {
    Save-SquareAsset 44 (Join-Path $assets "Square44x44Logo.png")
    Save-SquareAsset 150 (Join-Path $assets "Square150x150Logo.png")
    Save-SquareAsset 50 (Join-Path $assets "StoreLogo.png")

    $wide = New-Object System.Drawing.Bitmap 310, 150
    $wideGraphics = [System.Drawing.Graphics]::FromImage($wide)
    $wideGraphics.Clear([System.Drawing.Color]::Transparent)
    $wideGraphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $wideGraphics.DrawImage($source, 80, 0, 150, 150)
    $wide.Save((Join-Path $assets "Wide310x150Logo.png"), [System.Drawing.Imaging.ImageFormat]::Png)
    $wideGraphics.Dispose()
    $wide.Dispose()
}
finally {
    $source.Dispose()
}

Write-Host "Locating makeappx.exe..." -ForegroundColor Cyan
$makeappx = (Get-Command makeappx.exe -ErrorAction SilentlyContinue | Select-Object -First 1).Source
if (-not $makeappx) {
    $sdkRoot = "${env:ProgramFiles(x86)}\Windows Kits\10\bin"
    if (Test-Path $sdkRoot) {
        $makeappx = Get-ChildItem $sdkRoot -Recurse -Filter makeappx.exe |
            Where-Object { $_.FullName -match '\\x64\\' } |
            Sort-Object FullName -Descending |
            Select-Object -First 1 -ExpandProperty FullName
    }
}

if (-not $makeappx) {
    throw "makeappx.exe was not found. Install the Windows SDK or add makeappx to PATH."
}

New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
if (Test-Path $msixPath) { Remove-Item $msixPath -Force }

Write-Host "Packing MSIX..." -ForegroundColor Cyan
& $makeappx pack /d $publish /p $msixPath /o
if ($LASTEXITCODE -ne 0) { throw "makeappx failed." }

if ($CertificatePath) {
    $signtool = (Get-Command signtool.exe -ErrorAction SilentlyContinue | Select-Object -First 1).Source
    if (-not $signtool) { throw "signtool.exe was not found; cannot sign the package." }

    Write-Host "Signing MSIX..." -ForegroundColor Cyan
    & $signtool sign /fd SHA256 /f $CertificatePath /p $CertificatePassword $msixPath
    if ($LASTEXITCODE -ne 0) { throw "signtool failed." }
}

Write-Host "MSIX created: $msixPath" -ForegroundColor Green
