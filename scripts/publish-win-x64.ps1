param(
    [string]$Runtime = "win-x64",
    [string]$Configuration = "Release",
    [string]$Output = "artifacts/publish/win-x64",
    [string]$Version
)

$ErrorActionPreference = "Stop"
$root = Resolve-Path (Join-Path $PSScriptRoot "..")
. (Join-Path $PSScriptRoot "version.ps1")
if (-not $Version) {
    $Version = Get-ProjectVersion -Root $root
}

$project = "LMTrainingDataStudio2/LMTrainingDataStudio2/LMTrainingDataStudio2.csproj"

dotnet publish $project `
    -c $Configuration `
    -r $Runtime `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -p:Version=$Version `
    -o $Output

$zip = "$Output-$Version.zip"
if (Test-Path $zip) {
    Remove-Item $zip -Force
}

Compress-Archive -Path "$Output/*" -DestinationPath $zip
Write-Host "Published $Runtime bundle to $zip"