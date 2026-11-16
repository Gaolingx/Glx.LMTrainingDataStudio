param(
    [string]$Output = "LMTrainingDataStudio2/LMTrainingDataStudio2/Assets/app-icon.png"
)

Add-Type -AssemblyName System.Drawing

$size = 256
$bitmap = New-Object System.Drawing.Bitmap($size, $size)
$graphics = [System.Drawing.Graphics]::FromImage($bitmap)
$graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias

$rect = New-Object System.Drawing.Rectangle(0, 0, $size, $size)
$background = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
    $rect,
    [System.Drawing.Color]::FromArgb(255, 28, 45, 110),
    [System.Drawing.Color]::FromArgb(255, 112, 48, 190),
    35.0)
$graphics.FillRectangle($background, $rect)

$gridPen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(34, 255, 255, 255), 1)
for ($i = 16; $i -lt $size; $i += 24) {
    $graphics.DrawLine($gridPen, $i, 0, $i, $size)
    $graphics.DrawLine($gridPen, 0, $i, $size, $i)
}

$edgePen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(235, 120, 220, 255), 7)
$edgePen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
$edgePen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round

$nodes = @(
    @(70, 128),
    @(128, 66),
    @(186, 128),
    @(128, 190)
)

foreach ($node in $nodes) {
    $graphics.DrawLine($edgePen, 128, 128, $node[0], $node[1])
}

$glowBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(55, 255, 255, 255))
$nodeBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 255, 255, 255))
$innerBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 56, 189, 248))
$centerBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 255, 255, 255))

foreach ($node in $nodes) {
    $graphics.FillEllipse($glowBrush, $node[0] - 26, $node[1] - 26, 52, 52)
    $graphics.FillEllipse($nodeBrush, $node[0] - 15, $node[1] - 15, 30, 30)
    $graphics.FillEllipse($innerBrush, $node[0] - 8, $node[1] - 8, 16, 16)
}

$graphics.FillEllipse($glowBrush, 82, 82, 92, 92)
$graphics.FillEllipse($centerBrush, 101, 101, 54, 54)
$graphics.FillEllipse($innerBrush, 114, 114, 28, 28)

$graphics.Dispose()
$background.Dispose()
$gridPen.Dispose()
$edgePen.Dispose()
$glowBrush.Dispose()
$nodeBrush.Dispose()
$innerBrush.Dispose()
$centerBrush.Dispose()

$directory = Split-Path -Parent $Output
if ($directory -and !(Test-Path $directory)) {
    New-Item -ItemType Directory -Path $directory | Out-Null
}

$bitmap.Save($Output, [System.Drawing.Imaging.ImageFormat]::Png)
$bitmap.Dispose()
Write-Host "Wrote $Output"