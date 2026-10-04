param([int]$TeklaPid = 0, [switch]$Offline, [string]$TeklaBinPath)
$ErrorActionPreference = 'Stop'
$repo = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
if ($Offline) {
    if (!$TeklaBinPath) { throw 'Offline cần -TeklaBinPath đến bin đã dùng build sản phẩm.' }
    $bin = $TeklaBinPath
} else {
    $targets = @(Get-Process TeklaStructures | Where-Object { !$TeklaPid -or $_.Id -eq $TeklaPid })
    if ($targets.Count -ne 1) { throw 'Chọn đúng một Tekla bằng -TeklaPid.' }
    $target = $targets[0]
    $bin = Split-Path $target.Path -Parent
}
$output = Join-Path $repo '.codex-artifacts/neighbor-cleanup'
$null = New-Item -ItemType Directory -Path $output -Force
$exe = Join-Path $output 'NeighborProbe.exe'
$product = Join-Path $repo 'portable/TTSK Dim Plates.exe'
& "$env:WINDIR/Microsoft.NET/Framework64/v4.0.30319/csc.exe" /nologo /platform:x64 /target:exe "/out:$exe" "/r:$product" "/r:$bin/Tekla.Structures.dll" "/r:$bin/Tekla.Structures.Model.dll" "/r:$bin/Tekla.Structures.Drawing.dll" (Join-Path $PSScriptRoot 'Program.cs') (Join-Path $PSScriptRoot 'Regression.cs') (Join-Path $PSScriptRoot 'OverlapRegression.cs')
if ($LASTEXITCODE) { throw 'Probe build failed' }
Copy-Item "$product.config" "$exe.config" -Force
Copy-Item (Join-Path $repo 'portable/*.dll') $output -Force
if ($Offline) { & $exe $product offline unused $bin }
else { & $exe $product $target.Id $target.StartTime.ToUniversalTime().Ticks $bin }
if ($LASTEXITCODE) { throw 'Read-only audit failed' }
