[CmdletBinding()]
param(
    [string]$BuildDirectory,
    [switch]$AllowUnsigned
)

# Package a previously rebuilt Release x64 output. Never commit, upload, or
# change antivirus settings. Signing, when available, precedes this script.
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repo = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repo 'TTSK Dim Plates\TTSK Dim Plates'
if (-not $BuildDirectory) { $BuildDirectory = Join-Path $project 'bin\x64\Release' }
$build = (Resolve-Path -LiteralPath $BuildDirectory).Path
$exeName = 'TTSK Dim Plates.exe'
$exe = Join-Path $build $exeName
if (-not (Test-Path -LiteralPath $exe -PathType Leaf)) { throw "Missing build: $exe" }
$signature = Get-AuthenticodeSignature -LiteralPath $exe
if ($signature.Status -ne 'Valid') {
    if (-not $AllowUnsigned -or $signature.Status -ne 'NotSigned') {
        throw "EXE signature is $($signature.Status). Sign with a trusted Authenticode certificate and timestamp first. -AllowUnsigned permits only an unsigned diagnostic package."
    }
    Write-Warning 'Unsigned diagnostic package: cloud acceptance is not verified.'
}

# Only named project dependencies and explicitly copied content are eligible.
# No recursive copy of portable, source history, scripts, local state, or logs.
[xml]$definition = Get-Content -LiteralPath (Join-Path $project 'TTSK Dim Plates.csproj') -Raw
$ns = New-Object System.Xml.XmlNamespaceManager($definition.NameTable)
$ns.AddNamespace('m', $definition.DocumentElement.NamespaceURI)
$files = New-Object 'System.Collections.Generic.List[string]'
$files.Add($exeName)
$files.Add($exeName + '.config')
foreach ($reference in $definition.SelectNodes('//m:Reference[m:HintPath]', $ns)) {
    $privateNode = $reference.SelectSingleNode('m:Private', $ns)
    if ($privateNode -and $privateNode.InnerText -eq 'False') { continue }
    $hint = $reference.SelectSingleNode('m:HintPath', $ns).InnerText
    $files.Add([IO.Path]::GetFileName($hint))
}
foreach ($content in $definition.SelectNodes('//m:Content[m:CopyToOutputDirectory="PreserveNewest" or m:CopyToOutputDirectory="Always"]', $ns)) {
    $files.Add($content.GetAttribute('Include'))
}
$relativeFiles = @($files | Sort-Object -Unique)
foreach ($relative in $relativeFiles) {
    if ([IO.Path]::IsPathRooted($relative) -or $relative -match '(^|[\\/])\.\.([\\/]|$)') { throw "Unsafe package path: $relative" }
    if ($relative -match '\.(bat|cmd|ps1|lnk|pdb|log|cfg)$') { throw "Development/local state is not package content: $relative" }
    if (-not (Test-Path -LiteralPath (Join-Path $build $relative) -PathType Leaf)) { throw "Missing runtime file: $relative" }
}

$label = if ($signature.Status -eq 'Valid') { 'SIGNED' } else { 'UNSIGNED-DIAGNOSTIC' }
$id = 'TTSK-Dim-Plates-' + $label + '-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [Guid]::NewGuid().ToString('N').Substring(0,8)
$output = Join-Path $repo ('.codex-artifacts\cloud-release\' + $id)
$stage = Join-Path $output 'TTSK Dim Plates'
New-Item -ItemType Directory -Path $stage | Out-Null
foreach ($relative in $relativeFiles) {
    $destination = Join-Path $stage $relative
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $destination) | Out-Null
    Copy-Item -LiteralPath (Join-Path $build $relative) -Destination $destination
    if ((Get-FileHash -LiteralPath $destination).Hash -ne (Get-FileHash -LiteralPath (Join-Path $build $relative)).Hash) { throw "Copy hash mismatch: $relative" }
}
@'
TTSK Auto Dimension - Tekla Structures 2025 SP7 / .NET Framework 4.8 / Windows x64
Extract the complete folder, then open TTSK Dim Plates.exe directly.
Keep all DLL, config, Data, Resources and macro files beside the application.
Tekla Structures must be installed and licensed, with the required model open.
This package omits personal settings; application defaults apply on first use.
A local antivirus scan does not establish Google Drive or OneDrive acceptance.
Do not disable antivirus to run this package. Report suspected false positives
to the service that blocked the exact file, with its SHA-256 and detection name.
'@ | Set-Content -LiteralPath (Join-Path $stage 'README.txt') -Encoding UTF8
$manifest = @(Get-ChildItem -LiteralPath $stage -Recurse -File | Sort-Object FullName | ForEach-Object {
    [pscustomobject]@{ Path = $_.FullName.Substring($stage.Length + 1); SHA256 = (Get-FileHash -LiteralPath $_.FullName).Hash }
})
$manifest | Export-Csv -LiteralPath (Join-Path $stage 'SHA256.csv') -NoTypeInformation -Encoding UTF8

$defender = Get-ChildItem -Path "$env:ProgramData\Microsoft\Windows Defender\Platform\*\MpCmdRun.exe" -ErrorAction SilentlyContinue |
    Sort-Object { [version]($_.Directory.Name -replace '-.*$', '') } -Descending | Select-Object -First 1
if (-not $defender) { throw 'Defender scanner unavailable. Package is not validated.' }
function Assert-CleanScan([string]$Path, [string]$Log) {
    # Custom scan only; no automatic remediation of user files.
    $result = & $defender.FullName -Scan -ScanType 3 -File $Path -DisableRemediation 2>&1
    $scanExit = $LASTEXITCODE
    $result | Set-Content -LiteralPath $Log -Encoding UTF8
    if ($scanExit -ne 0) { throw "Defender scan failed or found a threat (exit $scanExit). See $Log" }
}
Assert-CleanScan $stage (Join-Path $output 'defender-folder.txt')
$zip = Join-Path $output ($id + '.zip')
Compress-Archive -LiteralPath $stage -DestinationPath $zip -CompressionLevel Optimal
if ((Get-Item -LiteralPath $zip).Length -ge 100000000) { throw 'ZIP exceeds the conservative 100 MB cloud scan size limit.' }
Assert-CleanScan $zip (Join-Path $output 'defender-zip.txt')
$zipHash = (Get-FileHash -LiteralPath $zip).Hash
"$zipHash  $([IO.Path]::GetFileName($zip))" | Set-Content -LiteralPath ($zip + '.sha256') -Encoding ASCII
[pscustomobject]@{
    Created = (Get-Date).ToString('o')
    Zip = $zip
    ZipBytes = (Get-Item -LiteralPath $zip).Length
    SHA256 = $zipHash
    RuntimeFiles = $relativeFiles.Count
    Signature = [string]$signature.Status
    DefenderFolderExitCode = 0
    DefenderZipExitCode = 0
    AntivirusSignatureVersion = (Get-MpComputerStatus).AntivirusSignatureVersion
    GoogleDriveVerified = $false
    OneDriveVerified = $false
} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $output 'validation.json') -Encoding UTF8
Write-Output $zip
