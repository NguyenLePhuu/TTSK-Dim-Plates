param([string]$TeklaBinPath = 'C:\Program Files\Tekla Structures\2025.0\bin', [switch]$UiHandoff, [int]$StartPid = 0, [switch]$PreviewOnly, [switch]$NativePreview, [switch]$StartupWait, [switch]$WorkspaceRecovery)
$ErrorActionPreference = 'Stop'
$repo = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$output = Join-Path $repo '.codex-artifacts\session-tests'
New-Item -ItemType Directory -Path $output -Force | Out-Null
$exe = Join-Path $output $(if ($NativePreview) { 'SessionProbe.Native.exe' } else { 'SessionProbe.exe' })
$source = Join-Path $PSScriptRoot 'Program.cs'
$compilerOptions = @()
if ($NativePreview) {
    $manifest = Join-Path $output 'native-preview.manifest'
    @'
<?xml version="1.0" encoding="utf-8"?>
<assembly xmlns="urn:schemas-microsoft-com:asm.v1" manifestVersion="1.0">
  <assemblyIdentity version="1.0.0.0" name="TTSK.PickerPreview" />
  <compatibility xmlns="urn:schemas-microsoft-com:compatibility.v1"><application><supportedOS Id="{8e0f7a12-bfb3-4fe8-b9a5-48fd50a15a9a}" /></application></compatibility>
  <application xmlns="urn:schemas-microsoft-com:asm.v3"><windowsSettings><dpiAware xmlns="http://schemas.microsoft.com/SMI/2005/WindowsSettings">true</dpiAware></windowsSettings></application>
</assembly>
'@ | Set-Content -LiteralPath $manifest -Encoding utf8
    $compilerOptions += "/win32manifest:$manifest"
}
& "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /platform:x64 /target:exe "/out:$exe" @compilerOptions /r:System.Windows.Forms.dll /r:System.Drawing.dll "/r:$TeklaBinPath\Tekla.Structures.dll" "/r:$TeklaBinPath\Tekla.Structures.Model.dll" "/r:$TeklaBinPath\Tekla.Structures.Drawing.dll" $source
if ($LASTEXITCODE) { throw 'Probe build failed' }
$product = Join-Path $repo 'portable\TTSK Dim Plates.exe'
Copy-Item "$product.config" "$exe.config" -Force
if ($NativePreview) {
    [xml]$previewConfig = Get-Content -LiteralPath "$exe.config" -Raw
    $dpiSetting = $previewConfig.CreateElement('add')
    $dpiSetting.SetAttribute('key', 'EnableWindowsFormsHighDpiAutoResizing')
    $dpiSetting.SetAttribute('value', 'true')
    $null = $previewConfig.configuration.appSettings.AppendChild($dpiSetting)
    $previewConfig.Save("$exe.config")
}
Copy-Item (Join-Path $repo 'portable\*.dll') $output -Force
$env:TeklaBinPath = $TeklaBinPath
function Close-TestWorkspace {
    $deadline = [DateTime]::UtcNow.AddSeconds(15)
    do {
        $child = Get-Process 'TTSK Dim Plates' -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $product }
        if ($child -and $child.MainWindowTitle -like 'TTSK AutoDim*') { break }
        Start-Sleep -Milliseconds 200
    } while ([DateTime]::UtcNow -lt $deadline)
    if (!$child -or $child.MainWindowTitle -notlike 'TTSK AutoDim*') { throw 'Verified child did not show its project window' }
    Write-Output "PASS: child visible: $($child.MainWindowTitle)"
    $null = $child.CloseMainWindow()
    if (!$child.WaitForExit(10000)) { throw 'Test child did not close normally' }
}
if ($WorkspaceRecovery) {
    if (Get-Process 'TTSK Dim Plates' -ErrorAction SilentlyContinue) { throw 'Close TTSK before workspace recovery test' }
    Copy-Item (Join-Path $repo 'portable\Resources') $output -Recurse -Force
    & $exe $product audit-pure
    if ($LASTEXITCODE) { throw 'Workspace identity regressions failed' }
    & $exe $product readiness
    if ($LASTEXITCODE) { throw 'A ready Tekla model is required' }
    $target = Get-Process TeklaStructures | Where-Object { !$StartPid -or $_.Id -eq $StartPid } | Select-Object -First 1
    if (!$target) { throw 'Requested Tekla process is unavailable' }
    foreach ($mode in @('guard','watch-snapshots','same-pid')) {
        & $exe $product $target.Id $target.StartTime.ToUniversalTime().Ticks $mode
        if ($LASTEXITCODE) { throw "Workspace test failed: $mode" }
    }
    Close-TestWorkspace
    return
}
if ($StartupWait) {
    if (Get-Process 'TTSK Dim Plates' -ErrorAction SilentlyContinue) { throw 'Close TTSK before startup lifecycle test' }
    Copy-Item (Join-Path $repo 'portable\Resources') $output -Recurse -Force
    & $exe $product wait-cancel $output
    if ($LASTEXITCODE) { throw 'Wait/cancel test failed' }
    & $exe $product readiness
    if ($LASTEXITCODE -eq 3) {
        Write-Output 'SKIP: no ready Tekla API; wait/cancel passed, connection/handoff tests require a ready model.'
        return
    }
    if ($LASTEXITCODE) { throw 'Could not check live test readiness' }
    & $exe $product wait-abort $output
    if ($LASTEXITCODE) { throw 'Cancel during connect failed' }
    Start-Sleep -Milliseconds 300
    if (Get-Process 'TTSK Dim Plates' -ErrorAction SilentlyContinue) { throw 'Cancel leaked child process' }
    & $exe $product wait-retry $output
    if ($LASTEXITCODE) { throw 'Retry after child connection failure failed' }
    Close-TestWorkspace
    & $exe $product wait-connect $output
    if ($LASTEXITCODE) { throw 'Wait/connect test failed' }
    Close-TestWorkspace
    & $exe $product wait-ready $output
    if ($LASTEXITCODE) { throw 'Logo-only ready startup failed' }
    Close-TestWorkspace
    return
}
if ($PreviewOnly) {
    & $exe $product preview $output $(if ($NativePreview) { 'native' } else { 'bitmap' })
    if ($LASTEXITCODE) { throw 'Picker preview failed' }
    return
}
& $exe $product
if ($LASTEXITCODE) { throw 'Discovery requires at least two accessible Tekla sessions' }
foreach ($process in Get-Process TeklaStructures) {
    & $exe $product $process.Id $process.StartTime.ToUniversalTime().Ticks guard
    if ($LASTEXITCODE) { throw "Read-only preflight failed for PID $($process.Id)" }
}
if ($UiHandoff) {
    if (Get-Process 'TTSK Dim Plates' -ErrorAction SilentlyContinue) { throw 'Close TTSK before the isolated UI handoff test' }
    Copy-Item (Join-Path $repo 'portable\Resources') $output -Recurse -Force
    & $exe $product startup
    if ($LASTEXITCODE) { throw 'Fresh startup picker failed' }
    Close-TestWorkspace
    $first = Get-Process TeklaStructures | Where-Object { !$StartPid -or $_.Id -eq $StartPid } | Select-Object -First 1
    & $exe $product $first.Id $first.StartTime.ToUniversalTime().Ticks ui
    if ($LASTEXITCODE) { throw 'UI handoff parent failed' }
    $deadline = [DateTime]::UtcNow.AddSeconds(15)
    do {
        $child = Get-Process 'TTSK Dim Plates' -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $product }
        if ($child -and $child.MainWindowTitle -like 'TTSK AutoDim*') { break }
        Start-Sleep -Milliseconds 200
    } while ([DateTime]::UtcNow -lt $deadline)
    if (!$child -or $child.MainWindowTitle -notlike 'TTSK AutoDim*') { throw 'Verified child did not show its project window' }
    Write-Output "PASS: child visible: $($child.MainWindowTitle)"
    $null = $child.CloseMainWindow()
    if (!$child.WaitForExit(10000)) { throw 'Test child did not close normally' }
}
