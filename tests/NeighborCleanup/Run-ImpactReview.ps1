param([switch]$Live, [int]$TeklaPid = 0, [string]$TeklaBinPath = 'C:\Program Files\Tekla Structures\2025.0\bin')
$ErrorActionPreference = 'Stop'
$repo = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$output = Join-Path $repo '.codex-artifacts/neighbor-impact'
$null = New-Item -ItemType Directory -Path $output -Force
if ($Live) {
    $targets = @(Get-Process TeklaStructures | Where-Object { !$TeklaPid -or $_.Id -eq $TeklaPid })
    if ($targets.Count -ne 1) { throw 'Chọn một phiên Tekla bằng -TeklaPid.' }
    $target = $targets[0]
    $TeklaBinPath = Split-Path $target.Path -Parent
}
# Bản trước hai sửa Slot 08; chỉ sinh bản đổi namespace trong cache, không khôi phục working tree.
$baselineRef = '80b831e2ee1f804497069715087bce33ed6c777f'
$legacy = (& git -C $repo show "${baselineRef}:TTSK Dim Plates/TTSK Dim Plates/PHU_Slot08_NeighborCleanup.cs") -join "`n"
if ($LASTEXITCODE) { throw 'Không đọc được baseline Git.' }
$legacy = "using Tekla.Technology.Akit.UserScript;`n" + $legacy.Replace('namespace Tekla.Technology.Akit.UserScript', 'namespace NeighborLegacy')
$legacyPath = Join-Path $output 'LegacyNeighbor.cs'
[IO.File]::WriteAllText($legacyPath,$legacy,[Text.UTF8Encoding]::new($true))
$exe = Join-Path $output 'ImpactReview.exe'
$product = Join-Path $repo 'portable/TTSK Dim Plates.exe'
& "$env:WINDIR/Microsoft.NET/Framework64/v4.0.30319/csc.exe" /nologo /platform:x64 /target:exe "/out:$exe" "/r:$product" "/r:$TeklaBinPath/Tekla.Structures.dll" "/r:$TeklaBinPath/Tekla.Structures.Model.dll" "/r:$TeklaBinPath/Tekla.Structures.Drawing.dll" $legacyPath (Join-Path $PSScriptRoot 'ImpactReview.cs')
if ($LASTEXITCODE) { throw 'Differential probe build failed' }
Copy-Item "$product.config" "$exe.config" -Force
Copy-Item (Join-Path $repo 'portable/*.dll') $output -Force
if ($Live) { & $exe $product $TeklaBinPath $target.Id $target.StartTime.ToUniversalTime().Ticks }
else { & $exe $product $TeklaBinPath offline }
if ($LASTEXITCODE) { throw 'Impact review failed' }
