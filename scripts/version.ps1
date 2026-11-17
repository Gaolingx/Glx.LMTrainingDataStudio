function Get-ProjectVersion {
    param([Parameter(Mandatory = $true)][string]$Root)

    $props = Join-Path $Root "Directory.Build.props"
    if (Test-Path $props) {
        try {
            [xml]$xml = Get-Content -Path $props -Raw
            $version = $xml.SelectSingleNode("//Version")
            if ($version -and $version.InnerText.Trim()) {
                return $version.InnerText.Trim()
            }
        }
        catch {
            # Fall through to the default when the props file cannot be parsed.
        }
    }

    return "1.0.0"
}
