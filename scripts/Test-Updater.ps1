[CmdletBinding()]
param(
    [switch]$VerboseOutput
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = Split-Path -Parent $PSScriptRoot
Set-Location -LiteralPath $repoRoot

$artifactsDir = Join-Path $repoRoot ".codex-artifacts\auto-update"
New-Item -ItemType Directory -Path $artifactsDir -Force | Out-Null

$testRunDir = Join-Path ([IO.Path]::GetTempPath()) ("TTSK-Updater-Tests-" + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $testRunDir -Force | Out-Null

$logFile = Join-Path $artifactsDir "test-results.log"
$summaryJsonFile = Join-Path $artifactsDir "test-summary.json"

if (Test-Path -LiteralPath $logFile) { Remove-Item -LiteralPath $logFile -Force }

$testResults = New-Object System.Collections.Generic.List[PSCustomObject]

function Log([string]$message, [string]$color = "White") {
    Write-Host $message -ForegroundColor $color
    [System.IO.File]::AppendAllText($logFile, "$message`r`n", [System.Text.Encoding]::UTF8)
}

function Run-Test([string]$groupName, [string]$testName, [scriptblock]$action) {
    Log "--------------------------------------------------------"
    Log "[TEST] [$groupName] $testName" "Cyan"
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    try {
        & $action
        $sw.Stop()
        Log "[PASS] $testName ($($sw.ElapsedMilliseconds) ms)" "Green"
        $testResults.Add([PSCustomObject]@{
            Group = $groupName
            Test = $testName
            Status = "PASS"
            DurationMs = $sw.ElapsedMilliseconds
            Error = ""
        })
    } catch {
        $sw.Stop()
        Log "[FAIL] $testName ($($sw.ElapsedMilliseconds) ms): $($_.Exception.Message)" "Red"
        Log "$($_.ScriptStackTrace)" "DarkRed"
        $testResults.Add([PSCustomObject]@{
            Group = $groupName
            Test = $testName
            Status = "FAIL"
            DurationMs = $sw.ElapsedMilliseconds
            Error = $_.Exception.Message
        })
    }
}

function Path-Combine([string]$p1, [string]$p2) {
    return [System.IO.Path]::Combine($p1, $p2)
}

function Assert-True([bool]$condition, [string]$message) {
    if (!$condition) { throw "Assert-True thất bại: $message" }
}

function Assert-Equal($expected, $actual, [string]$message) {
    if ($expected -ne $actual) { throw "Assert-Equal thất bại: $message. Kỳ vọng: '$expected', Thực tế: '$actual'" }
}

function Get-Sha256File([string]$path) {
    $sha = [System.Security.Cryptography.SHA256]::Create()
    $stream = [System.IO.File]::OpenRead($path)
    try {
        $bytes = $sha.ComputeHash($stream)
        return ([System.BitConverter]::ToString($bytes)).Replace("-", "").ToLowerInvariant()
    } finally {
        $stream.Dispose()
        $sha.Dispose()
    }
}

function Complete-TestPackage([string]$Staging, $Metadata) {
    foreach ($name in @('TTSK Dim Plates.exe','TTSK Dim Plates.exe.config')) {
        if (!$Metadata.managedFiles.Contains($name)) { $Metadata.managedFiles.Add($name) }
        if (!(Test-Path -LiteralPath (Join-Path $Staging $name))) { [IO.File]::WriteAllText((Join-Path $Staging $name), 'fixture runtime') }
    }
    $Metadata.createdUtc = [DateTime]::UtcNow.ToString('o')
    $Metadata.SaveToFile((Join-Path $Staging 'release.json'))
    $sums = [Collections.Generic.Dictionary[string,string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($name in $Metadata.managedFiles) { $sums[$name] = Get-Sha256File (Join-Path $Staging $name) }
    $sums['release.json'] = Get-Sha256File (Join-Path $Staging 'release.json')
    [TTSK_AutoDim_Plates.Updater.UpdateChecksumManifest]::WriteChecksums((Join-Path $Staging 'SHA256.csv'), $sums)
}

Log "========================================================" "Cyan"
Log "  KHOI CHAY BO KIEM THU AUTO UPDATE - TTSK DIM PLATES" "Cyan"
Log "========================================================" "Cyan"

# Nạp assembly từ build thử nghiệm
$builtExe = Join-Path $repoRoot ".codex-artifacts\test-build\TTSK Dim Plates.exe"
if ($true) {
    Log "Dang build Release x64 truoc khi test..." "Yellow"
    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    $msbuild = & $vswhere -latest -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\amd64\MSBuild.exe' | Select-Object -First 1
    if (!$msbuild) { $msbuild = & $vswhere -latest -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\MSBuild.exe' | Select-Object -First 1 }
    $proj = Join-Path $repoRoot "TTSK Dim Plates\TTSK Dim Plates\TTSK Dim Plates.csproj"
    $outDir = Join-Path $repoRoot ".codex-artifacts\test-build\"
    & $msbuild $proj /t:Rebuild /p:Configuration=Release /p:Platform=x64 ('/p:OutDir="' + $outDir.TrimEnd('\') + '\\"') /nologo /v:q /clp:ErrorsOnly
    if ($LASTEXITCODE -ne 0) { throw 'Fresh build failed; do not test an old EXE.' }
}

[System.Reflection.Assembly]::LoadFrom($builtExe) | Out-Null
[System.Reflection.Assembly]::LoadWithPartialName("System.Web.Extensions") | Out-Null
[System.Reflection.Assembly]::LoadWithPartialName("System.IO.Compression.FileSystem") | Out-Null

[TTSK_AutoDim_Plates.Updater.UpdateWorker]::SilentMode = $true

# Chạy lần lượt các tệp test
. (Join-Path $repoRoot "tests\Updater\Test-VersionAndManifest.ps1")
. (Join-Path $repoRoot "tests\Updater\Test-SecurityAndZipSlip.ps1")
. (Join-Path $repoRoot "tests\Updater\Test-LockAndMutualExclusion.ps1")
. (Join-Path $repoRoot "tests\Updater\Test-TransactionAndRollback.ps1")
. (Join-Path $repoRoot "tests\Updater\Test-ConfigPreservation.ps1")
. (Join-Path $repoRoot "tests\Updater\Test-PackagingAndWorkflow.ps1")
. (Join-Path $repoRoot "tests\Updater\Test-ReviewRegressions.ps1")
. (Join-Path $repoRoot "tests\Updater\Test-PublisherMock.ps1")

# Tổng kết
$total = $testResults.Count
$passed = @($testResults | Where-Object { $_.Status -eq "PASS" }).Count
$failed = @($testResults | Where-Object { $_.Status -eq "FAIL" }).Count

Log "========================================================" "Cyan"
Log "  KET QUA KIEM THU:" "Cyan"
Log "  Tong so test: $total" "Cyan"
Log "  PASS        : $passed" "Green"
Log "  FAIL        : $failed" $(if ($failed -gt 0) { "Red" } else { "Green" })
Log "========================================================" "Cyan"

$summaryObj = [PSCustomObject]@{
    TestRunDirectory = $testRunDir
    Total = $total
    Passed = $passed
    Failed = $failed
    Results = $testResults
}
$summaryObj | ConvertTo-Json -Depth 5 | Set-Content -Path $summaryJsonFile -Encoding UTF8

if ($failed -gt 0) {
    exit 1
} else {
    exit 0
}
