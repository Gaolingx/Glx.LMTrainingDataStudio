#Requires -Version 5.1
<#
.SYNOPSIS
    Fails when the Coverlet Cobertura report is below the configured line-coverage floors.
#>
param(
    [string]$ReportPath = "artifacts/coverage/coverage.cobertura.xml",
    [double]$MinimumTotal = 60,
    [double]$MinimumServices = 70
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path $ReportPath)) {
    throw "Coverage report not found: $ReportPath"
}

[xml]$report = Get-Content -Path $ReportPath -Raw
$coverage = $report.coverage
$valid = [double]$coverage.'lines-valid'
if ($valid -le 0) {
    throw "Coverage report contains no line data."
}

$total = [math]::Round(([double]$coverage.'lines-covered') * 100 / $valid, 2)
Write-Host "Total line coverage: $total% (minimum $MinimumTotal%)"
if ($total -lt $MinimumTotal) {
    throw "Total line coverage $total% is below the $MinimumTotal% floor."
}

$classes = @($coverage.packages.package.classes.class | Where-Object {
    $_.filename -like "Services*" -or $_.filename -like "Commands*"
})

if ($classes.Count -gt 0) {
    $lines = @($classes | ForEach-Object { $_.methods.method.lines.line } | Where-Object { $_ })
    $servicesValid = $lines.Count
    $servicesCovered = @($lines | Where-Object { [int]$_.hits -gt 0 }).Count
    if ($servicesValid -gt 0) {
        $services = [math]::Round($servicesCovered * 100 / $servicesValid, 2)
        Write-Host "Services/Commands line coverage: $services% (minimum $MinimumServices%)"
        if ($services -lt $MinimumServices) {
            throw "Services/Commands line coverage $services% is below the $MinimumServices% floor."
        }
    }
}
