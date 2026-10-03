$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$repo = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$originalPath = Join-Path $repo 'docs\assets\logo-original-190x156.png'
$selectedPath = Join-Path $repo 'TTSK Dim Plates\TTSK Dim Plates\Resources\logo.png'
$outputPath = Join-Path $repo 'docs\assets\logo-comparison.png'
$original = [Drawing.Bitmap]::FromFile($originalPath)
$selected = [Drawing.Bitmap]::FromFile($selectedPath)
$canvas = New-Object Drawing.Bitmap(1060, 520)
$graphics = [Drawing.Graphics]::FromImage($canvas)
$heading = New-Object Drawing.Font('Segoe UI', 17, [Drawing.FontStyle]::Bold)
$caption = New-Object Drawing.Font('Segoe UI', 11)
$ink = New-Object Drawing.SolidBrush([Drawing.Color]::FromArgb(25,36,55))
$muted = New-Object Drawing.SolidBrush([Drawing.Color]::FromArgb(95,110,130))
try {
    $graphics.Clear([Drawing.Color]::FromArgb(245,247,251))
    $graphics.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $graphics.PixelOffsetMode = [Drawing.Drawing2D.PixelOffsetMode]::Half
    $graphics.DrawString('ẢNH GỐC', $heading, $ink, 35, 16)
    $graphics.DrawString('IMAGEGEN ĐÃ CHỌN', $heading, $ink, 575, 16)
    $graphics.DrawString("$($original.Width) × $($original.Height) px", $caption, $muted, 35, 47)
    $graphics.DrawString("$($selected.Width) × $($selected.Height) px", $caption, $muted, 575, 47)
    $graphics.DrawImage($original, [Drawing.Rectangle]::new(35, 85, 450, [int][Math]::Round(450.0 * $original.Height / $original.Width)))
    $graphics.DrawImage($selected, [Drawing.Rectangle]::new(575, 85, 450, [int][Math]::Round(450.0 * $selected.Height / $selected.Width)))
    $graphics.DrawString('Cùng chiều rộng hiển thị; giữ nguyên tỉ lệ từng ảnh.', $caption, $muted, 35, 485)
    $canvas.Save($outputPath, [Drawing.Imaging.ImageFormat]::Png)
    Write-Output $outputPath
}
finally {
    $graphics.Dispose(); $canvas.Dispose(); $original.Dispose(); $selected.Dispose()
    $heading.Dispose(); $caption.Dispose(); $ink.Dispose(); $muted.Dispose()
}
