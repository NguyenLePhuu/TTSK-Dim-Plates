param([Parameter(Mandatory)][int]$TeklaPid, [string]$ResumeSnapshot, [switch]$Ambiguous)
$ErrorActionPreference = 'Stop'
$repo = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$target = Get-Process -Id $TeklaPid
if ($target.ProcessName -ne 'TeklaStructures') { throw 'Wrong target process' }
$bin = Split-Path $target.Path -Parent
$output = Join-Path $repo ('.codex-artifacts/neighbor-live/' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
$null = New-Item -ItemType Directory -Path $output -Force
$exe = Join-Path $output 'LiveReview.exe'
$product = Join-Path $repo 'portable/TTSK Dim Plates.exe'
& "$env:WINDIR/Microsoft.NET/Framework64/v4.0.30319/csc.exe" /nologo /platform:x64 /target:exe "/out:$exe" "/r:$product" "/r:$bin/Tekla.Structures.dll" "/r:$bin/Tekla.Structures.Model.dll" "/r:$bin/Tekla.Structures.Drawing.dll" (Join-Path $PSScriptRoot 'LiveReview.cs')
if ($LASTEXITCODE) { throw 'Live probe build failed' }
Copy-Item "$product.config" "$exe.config" -Force
Copy-Item (Join-Path $repo 'portable/*.dll') $output -Force
Write-Output "Snapshots: $output"
$callArgs = @($product,$bin,$target.Id,$target.StartTime.ToUniversalTime().Ticks,$output)
if ($ResumeSnapshot) { $callArgs[4] = (Resolve-Path -LiteralPath $ResumeSnapshot).Path; $callArgs += 'resume' }
elseif ($Ambiguous) { $callArgs += 'ambiguous' }
& $exe @callArgs 2>&1 | Tee-Object -FilePath (Join-Path $output 'live.log')
if ($LASTEXITCODE) { throw 'Live test failed; inspect snapshots and current drawing before retrying' }
