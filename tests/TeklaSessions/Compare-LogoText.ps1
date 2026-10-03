$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$repo = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$files = @('logo-original-190x156.png','logo-imagegen-bold.png','logo-imagegen-thin.png')
$labels = @('GỐC — chữ mỏng, ảnh nhỏ','IMAGEGEN TRƯỚC — chữ đậm','ĐÃ CHỈNH — chữ mỏng, ảnh lớn')
$canvas = New-Object Drawing.Bitmap(1120, 655)
$g = [Drawing.Graphics]::FromImage($canvas)
$font = New-Object Drawing.Font('Segoe UI', 14, [Drawing.FontStyle]::Bold)
try {
    $g.Clear([Drawing.Color]::FromArgb(245,247,251))
    $g.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.PixelOffsetMode = [Drawing.Drawing2D.PixelOffsetMode]::Half
    for ($i=0; $i -lt $files.Count; $i++) {
        $image = [Drawing.Bitmap]::FromFile((Join-Path $repo ('docs/assets/' + $files[$i])))
        try {
            $top = [int][Math]::Floor($image.Height * .82)
            $g.DrawString($labels[$i], $font, [Drawing.Brushes]::DarkSlateGray, 28, 14 + $i*215)
            $displayHeight = [int][Math]::Round(1064.0 * ($image.Height-$top) / $image.Width)
            $g.DrawImage($image, [Drawing.Rectangle]::new(28, 48 + $i*215, 1064, $displayHeight),
                0, $top, $image.Width, $image.Height-$top, [Drawing.GraphicsUnit]::Pixel)
        } finally { $image.Dispose() }
    }
    $path = Join-Path $repo 'docs/assets/logo-text-comparison.png'
    $canvas.Save($path, [Drawing.Imaging.ImageFormat]::Png)
    Write-Output $path
} finally { $font.Dispose(); $g.Dispose(); $canvas.Dispose() }
