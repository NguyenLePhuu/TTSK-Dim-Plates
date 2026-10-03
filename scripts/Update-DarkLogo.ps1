# Chỉ đổi RGB vùng chữ, giữ nguyên alpha/hình chữ và mọi pixel biểu tượng. Không dùng AI tái vẽ.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$repo = Split-Path $PSScriptRoot -Parent
$sourcePath = Join-Path $repo 'TTSK Dim Plates\TTSK Dim Plates\Resources\logo.png'
$targetPath = Join-Path $repo 'TTSK Dim Plates\TTSK Dim Plates\Resources\logo_dark.png'
$source = [Drawing.Bitmap]::FromFile($sourcePath)
$dark = $source.Clone([Drawing.Rectangle]::new(0,0,$source.Width,$source.Height), [Drawing.Imaging.PixelFormat]::Format32bppArgb)
try {
    # Lấy trắng điển hình trong các nét trắng của biểu tượng; không lấy nền trong suốt.
    $white = New-Object 'System.Collections.Generic.List[int]'
    for ($y=0; $y -lt [int]($source.Height*.70); $y+=4) {
        for ($x=0; $x -lt $source.Width; $x+=4) {
            $p=$source.GetPixel($x,$y)
            if ($p.A -ge 240 -and $p.R -ge 245 -and $p.G -ge 245 -and $p.B -ge 245) { $white.Add([int][Math]::Round(($p.R+$p.G+$p.B)/3.0)) }
        }
    }
    if (!$white.Count) { throw 'Không tìm được nét trắng chuẩn của logo.' }
    $white.Sort(); $shade=$white[[int]($white.Count/2)]
    $textTop=[int][Math]::Floor($source.Height*.82)
    # Ranh giới phải nằm trong khoảng trống giữa biểu tượng và chữ.
    for ($y=$textTop-8; $y -le $textTop+8; $y++) {
        for ($x=0; $x -lt $source.Width; $x++) {
            if ($source.GetPixel($x,$y).A -gt 16) { throw 'Bố cục logo đã đổi: ranh giới chạm nội dung.' }
        }
    }
    $changed=0
    for ($y=$textTop; $y -lt $source.Height; $y++) {
        for ($x=0; $x -lt $source.Width; $x++) {
            $p=$source.GetPixel($x,$y)
            if ($p.A -ne 0) { $dark.SetPixel($x,$y,[Drawing.Color]::FromArgb($p.A,$shade,$shade,$shade)); $changed++ }
        }
    }
    $dark.Save($targetPath,[Drawing.Imaging.ImageFormat]::Png)
    # Kiểm lại toàn bộ ảnh đã lưu, không chỉ bitmap đang ở RAM.
    $saved=[Drawing.Bitmap]::FromFile($targetPath)
    try {
        for ($y=0; $y -lt $source.Height; $y++) {
            for ($x=0; $x -lt $source.Width; $x++) {
                $a=$source.GetPixel($x,$y); $b=$saved.GetPixel($x,$y)
                if ($a.A -ne $b.A) { throw 'Alpha/hình chữ bị thay đổi.' }
                if (($y -lt $textTop -or $a.A -eq 0) -and $a.ToArgb() -ne $b.ToArgb()) { throw 'Pixel ngoài chữ bị thay đổi.' }
                if ($y -ge $textTop -and $a.A -gt 0 -and ($b.R -ne $shade -or $b.G -ne $shade -or $b.B -ne $shade)) { throw 'Màu chữ sai.' }
            }
        }
    } finally { $saved.Dispose() }
    Write-Output ('PASS: {0} pixel chữ -> #{1:X2}{1:X2}{1:X2}; toàn bộ alpha và pixel ngoài chữ giữ nguyên.' -f $changed,$shade)
} finally { $source.Dispose(); $dark.Dispose() }
