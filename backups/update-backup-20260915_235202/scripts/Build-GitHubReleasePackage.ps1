[CmdletBinding()]
param(
    [string]$PortableDir = "portable",
    [string]$CsprojPath = "TTSK Dim Plates\TTSK Dim Plates\TTSK Dim Plates.csproj",
    [string]$OutDir = ".codex-artifacts\release-package",
    [Parameter(Mandatory = $true)]
    [string]$Version,
    [Parameter(Mandatory = $true)]
    [string]$Tag,
    [Parameter(Mandatory = $true)]
    [string]$CommitSha,
    [string]$CreatedUtc
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

# Chuyển về thư mục gốc của repository
$repoRoot = Split-Path -Parent $PSScriptRoot
Set-Location -LiteralPath $repoRoot

function Path-Combine([string]$p1, [string]$p2) {
    return [System.IO.Path]::GetFullPath([System.IO.Path]::Combine($p1, $p2))
}

function Get-Sha256([string]$filePath) {
    $sha = [System.Security.Cryptography.SHA256]::Create()
    $stream = [System.IO.File]::OpenRead($filePath)
    try {
        $bytes = $sha.ComputeHash($stream)
        return ([System.BitConverter]::ToString($bytes)).Replace("-", "").ToLowerInvariant()
    } finally {
        $stream.Dispose()
        $sha.Dispose()
    }
}

Write-Host "========================================================" -ForegroundColor Cyan
Write-Host "  DONG GOI GITHUB RELEASE ASSETS - TTSK DIM PLATES" -ForegroundColor Cyan
Write-Host "========================================================" -ForegroundColor Cyan

# 1. Kiem tra tinh hop le cua tham so dau vao
$versionTrimmed = $Version.Trim()
if ($versionTrimmed -notmatch '^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$') { throw 'Invalid numeric version.' }
if ($versionTrimmed.StartsWith("v", [StringComparison]::OrdinalIgnoreCase)) {
    $versionTrimmed = $versionTrimmed.Substring(1)
}

$vParts = $versionTrimmed.Split('.')
if ($vParts.Length -ne 3) {
    throw "Phien ban khong hop le: '$Version'. Yeu cau dung 3 thanh phan so hoc (Major.Minor.Patch)."
}
foreach ($p in $vParts) {
    [int]$num = 0
    if (![int]::TryParse($p, [ref]$num) -or $num -lt 0) {
        throw "Thanh phan phien ban '$p' khong phai la so nguyen khong am."
    }
}
$normalizedVersion = $versionTrimmed

if ($Tag -cne "v$normalizedVersion") {
    throw "Tag '$Tag' khong khop voi phien ban '$normalizedVersion'."
}

if ($CommitSha.Length -ne 40 -or $CommitSha -notmatch '^[0-9a-fA-F]{40}$') {
    throw "CommitSha phai la chuoi 40 ky tu hex: '$CommitSha'."
}
$commitHex = $CommitSha.ToLowerInvariant()
if (!$CreatedUtc) {
    $CreatedUtc = & git show -s --format=%cI $commitHex
    if ($LASTEXITCODE -ne 0) { throw 'Cannot resolve release commit timestamp.' }
}
$releaseTime = [DateTimeOffset]::Parse($CreatedUtc).ToUniversalTime()
$CreatedUtc = $releaseTime.ToString('yyyy-MM-ddTHH:mm:ssZ')

$absPortable = Path-Combine $repoRoot $PortableDir
if (!(Test-Path -LiteralPath $absPortable -PathType Container)) {
    throw "Khong tim thay thu muc portable tai: $absPortable"
}

$absCsproj = Path-Combine $repoRoot $CsprojPath
if (!(Test-Path -LiteralPath $absCsproj -PathType Leaf)) {
    throw "Khong tim thay file csproj tai: $absCsproj"
}


# 2. Trich xuat danh sach runtime cho phep tu csproj
Write-Host "[1/6] Trich xuat danh sach file runtime tu csproj..." -ForegroundColor Yellow
[xml]$definition = Get-Content -LiteralPath $absCsproj -Raw
$ns = New-Object Xml.XmlNamespaceManager($definition.NameTable)
$ns.AddNamespace('m', $definition.DocumentElement.NamespaceURI)

$runtimeFiles = New-Object System.Collections.Generic.List[string]
$runtimeFiles.Add("TTSK Dim Plates.exe")
$runtimeFiles.Add("TTSK Dim Plates.exe.config")

foreach ($reference in $definition.SelectNodes('//m:Reference[m:HintPath]', $ns)) {
    if ($reference.HasAttribute('Condition') -or $reference.ParentNode.HasAttribute('Condition')) { throw 'Conditional runtime Reference is unsupported.' }
    $privateNode = $reference.SelectSingleNode('m:Private', $ns)
    if ($privateNode -and $privateNode.InnerText -eq 'False') { continue }
    $hintPath = $reference.SelectSingleNode('m:HintPath', $ns).InnerText
    if ($hintPath -match 'TeklaBinPath') { throw 'Tekla product binary cannot be packaged.' }
    $runtimeFiles.Add([System.IO.Path]::GetFileName($hintPath))
}

foreach ($content in $definition.SelectNodes('//m:Content[m:CopyToOutputDirectory="PreserveNewest" or m:CopyToOutputDirectory="Always"]', $ns)) {
    if ($content.HasAttribute('Condition') -or $content.ParentNode.HasAttribute('Condition') -or $content.SelectSingleNode('m:Link|m:TargetPath', $ns)) { throw 'Unsupported runtime content mapping.' }
    $includePath = $content.GetAttribute('Include').Replace('\', '/')
    $runtimeFiles.Add($includePath)
}

$uniqueFiles = @($runtimeFiles | Sort-Object -Unique)
Write-Host "Phat hien $($uniqueFiles.Count) file runtime theo allow-list csproj." -ForegroundColor Green

# 3. Kiem tra tinh hop le va an toan cua tung file runtime
Write-Host "[2/6] Kiem tra su ton tai va an toan cua cac file runtime..." -ForegroundColor Yellow
foreach ($rel in $uniqueFiles) {
    $allowed = $rel -in @('TTSK Dim Plates.exe','TTSK Dim Plates.exe.config','Phu_Macro_GridVisibility.cs') -or $rel -match '^[^/\\]+\.dll$' -or $rel -match '^Data/[^:]+\.tsv$' -or $rel -match '^Resources/[^:]+\.png$'
    if (!$allowed -or $rel -match '^(Tekla\.Structures.*|DPMPrinter|DotNetKit)\.dll$') { throw "Unsupported runtime policy: $rel" }
    if ([System.IO.Path]::IsPathRooted($rel) -or $rel -match '(^|[\\/])\.\.?([\\/]|$)' -or $rel -match '\.(cfg|log|pdb|bat|cmd|ps1|lnk)$' -or $rel -match '[:,*?<>|]' -or $rel -match '(^|/)(\.git|\.vs|bin|obj|backup|logs|\.codex-[^/]*)(/|$)' -or $rel -match '[. ](/|$)') {
        throw "Duong dan runtime khong hop le hoac bi cam: '$rel'"
    }
    $sourceFile = Path-Combine $absPortable ($rel.Replace('/', [System.IO.Path]::DirectorySeparatorChar))
    if (!(Test-Path -LiteralPath $sourceFile -PathType Leaf)) {
        throw "Thieu file runtime tren dia: '$rel' tai '$sourceFile'"
    }
    $checkPath = $sourceFile
    while ($checkPath) {
        if ((Get-Item -LiteralPath $checkPath).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw "Reparse source: $checkPath" }
        $checkPath = Split-Path -Parent $checkPath
    }
}

$assembly = [Reflection.AssemblyName]::GetAssemblyName((Join-Path $absPortable 'TTSK Dim Plates.exe'))
$pe = [IO.BinaryReader]::new([IO.File]::OpenRead((Join-Path $absPortable 'TTSK Dim Plates.exe')))
try {
    $pe.BaseStream.Position = 0x3c; $peOffset = $pe.ReadInt32()
    $pe.BaseStream.Position = $peOffset
    if ($pe.ReadUInt32() -ne 0x4550 -or $pe.ReadUInt16() -ne 0x8664) { throw 'Portable EXE must be managed AMD64.' }
} finally { $pe.Dispose() }

# Kiem tra exe.config co target .NET 4.8
$configPath = Path-Combine $absPortable "TTSK Dim Plates.exe.config"
$configXml = Get-Content -LiteralPath $configPath -Raw
if ($configXml -notmatch 'supportedRuntime.*\.NETFramework,Version=v4\.8') {
    throw "File TTSK Dim Plates.exe.config khong target dung .NET Framework 4.8."
}

# 4. Chuan bi staging dong goi
Write-Host "[3/6] Chuan bi thu muc staging dong goi..." -ForegroundColor Yellow
$absOutDir = Path-Combine $repoRoot $OutDir
if (Test-Path -LiteralPath $absOutDir) {
    if (@(Get-ChildItem -LiteralPath $absOutDir -Force).Count -gt 0) { throw "Output must be fresh or empty: $absOutDir" }
}
if ($absOutDir -eq $repoRoot -or $absOutDir.StartsWith($absPortable + '\', [StringComparison]::OrdinalIgnoreCase) -or $absOutDir -eq $absPortable) { throw 'Unsafe package output.' }
New-Item -ItemType Directory -Path $absOutDir -Force | Out-Null

$sessionTemp = Path-Combine $absOutDir "staging_temp"
$rootInsideZip = Path-Combine $sessionTemp "TTSK Dim Plates"
New-Item -ItemType Directory -Path $rootInsideZip -Force | Out-Null

$fileChecksums = [System.Collections.Generic.Dictionary[string, string]]::new([System.StringComparer]::OrdinalIgnoreCase)
$managedFilesList = New-Object System.Collections.Generic.List[string]

foreach ($rel in $uniqueFiles) {
    $normRel = $rel.Replace('\', '/')
    $managedFilesList.Add($normRel)

    $src = Path-Combine $absPortable ($rel.Replace('/', [System.IO.Path]::DirectorySeparatorChar))
    $dst = Path-Combine $rootInsideZip ($rel.Replace('/', [System.IO.Path]::DirectorySeparatorChar))
    $dstDir = [System.IO.Path]::GetDirectoryName($dst)
    if (![System.IO.Directory]::Exists($dstDir)) {
        [System.IO.Directory]::CreateDirectory($dstDir) | Out-Null
    }

    [System.IO.File]::Copy($src, $dst, $true)
    $srcHash = Get-Sha256 $src
    $dstHash = Get-Sha256 $dst
    if ($srcHash -ne $dstHash) {
        throw "Loi toan ven khi copy file: '$rel'"
    }
    $fileChecksums[$normRel] = $dstHash
}

# 5. Tao release.json va SHA256.csv
Write-Host "[4/6] Sinh metadata release.json va SHA256.csv..." -ForegroundColor Yellow
$releaseMetadata = [PSCustomObject]@{
    schemaVersion = 1
    product = "TTSK Dim Plates"
    repository = "NguyenLePhuu/TTSK-Dim-Plates"
    version = $normalizedVersion
    tag = $Tag
    commit = $commitHex
    createdUtc = $CreatedUtc
    managedFiles = $managedFilesList
}

$jsonPath = Path-Combine $rootInsideZip "release.json"
$jsonContent = $releaseMetadata | ConvertTo-Json -Depth 5
[System.IO.File]::WriteAllText($jsonPath, $jsonContent, [System.Text.Encoding]::UTF8)
$fileChecksums["release.json"] = Get-Sha256 $jsonPath

$csvPath = Path-Combine $rootInsideZip "SHA256.csv"
$csvLines = New-Object System.Collections.Generic.List[string]
$csvLines.Add("Path,SHA256")
$sortedKeys = @($fileChecksums.Keys | Sort-Object)
foreach ($k in $sortedKeys) {
    $csvLines.Add("$k,$($fileChecksums[$k])")
}
[System.IO.File]::WriteAllLines($csvPath, $csvLines, (New-Object System.Text.UTF8Encoding($false)))
foreach ($item in Get-ChildItem -LiteralPath $rootInsideZip -File -Recurse) { $item.LastWriteTimeUtc = $releaseTime.UtcDateTime }

# 6. Tao file ZIP va file SHA256 ngoai
Write-Host "[5/6] Nen file ZIP va tao file checksum SHA256..." -ForegroundColor Yellow
$zipFileName = "TTSK-Dim-Plates-Portable.zip"
$zipPath = Path-Combine $absOutDir $zipFileName
$sha256FileName = "TTSK-Dim-Plates-Portable.zip.sha256"
$sha256Path = Path-Combine $absOutDir $sha256FileName

if ([System.IO.File]::Exists($zipPath)) { [System.IO.File]::Delete($zipPath) }
if ([System.IO.File]::Exists($sha256Path)) { [System.IO.File]::Delete($sha256Path) }

# Su dung System.IO.Compression.ZipFile de tao zip voi root duy nhat 'TTSK Dim Plates/'
[System.Reflection.Assembly]::LoadWithPartialName("System.IO.Compression.FileSystem") | Out-Null
[System.IO.Compression.ZipFile]::CreateFromDirectory($sessionTemp, $zipPath, [System.IO.Compression.CompressionLevel]::Optimal, $false)

$zipSha256 = Get-Sha256 $zipPath
$sha256Content = "$zipSha256  $zipFileName`r`n"
[System.IO.File]::WriteAllText($sha256Path, $sha256Content, [System.Text.Encoding]::ASCII)

# 7. Xac thuc lai toan dien goi ZIP vua dong goi
Write-Host "[6/6] Kiem tra toan ven doc lap goi ZIP vua sinh..." -ForegroundColor Yellow
$verifyDir = Path-Combine $absOutDir "verify_temp"
if (Test-Path -LiteralPath $verifyDir) { Remove-Item -LiteralPath $verifyDir -Recurse -Force }
[System.IO.Compression.ZipFile]::ExtractToDirectory($zipPath, $verifyDir)

$extractedRoot = Path-Combine $verifyDir "TTSK Dim Plates"
if (!(Test-Path -LiteralPath $extractedRoot -PathType Container)) {
    throw "Goi ZIP khong chua thu muc goc duy nhat 'TTSK Dim Plates/'"
}

# Kiem tra khong co thu muc goc nao khac ngoai 'TTSK Dim Plates'
$rootItems = @(Get-ChildItem -LiteralPath $verifyDir)
if ($rootItems.Count -ne 1 -or $rootItems[0].Name -ne "TTSK Dim Plates") {
    throw "Goi ZIP chua them thu muc/file ngoai root duy nhat 'TTSK Dim Plates/'"
}

# Doi chieu tung file trong verify voi SHA256.csv
$verifyCsvPath = Path-Combine $extractedRoot "SHA256.csv"
if (!(Test-Path -LiteralPath $verifyCsvPath)) {
    throw "Thieu SHA256.csv trong ban giai nen kiem tra."
}
$csvContent = Get-Content -LiteralPath $verifyCsvPath
for ($i = 1; $i -lt $csvContent.Count; $i++) {
    $line = $csvContent[$i].Trim()
    if (!$line) { continue }
    $parts = $line.Split(',')
    $p = $parts[0]
    $expectedHash = $parts[1].ToLowerInvariant()
    $diskFile = Path-Combine $extractedRoot ($p.Replace('/', [System.IO.Path]::DirectorySeparatorChar))
    if (!(Test-Path -LiteralPath $diskFile)) {
        throw "File trong CSV khong ton tai sau khi giai nen: '$p'"
    }
    $actualDiskHash = Get-Sha256 $diskFile
    if ($actualDiskHash -ne $expectedHash) {
        throw "Hash khong khop sau khi giai nen cho: '$p'"
    }
}

# Don sach cac thu muc staging tam
Remove-Item -LiteralPath $sessionTemp -Recurse -Force
Remove-Item -LiteralPath $verifyDir -Recurse -Force

Write-Host "========================================================" -ForegroundColor Green
Write-Host "  DONG GOI THANH CONG HOAN HAO!" -ForegroundColor Green
Write-Host "  Phien ban : $normalizedVersion ($Tag)" -ForegroundColor Green
Write-Host "  Commit    : $commitHex" -ForegroundColor Green
Write-Host "  ZIP file  : $zipPath ($([math]::Round((Get-Item $zipPath).Length / 1MB, 2)) MB)" -ForegroundColor Green
Write-Host "  SHA256    : $zipSha256" -ForegroundColor Green
Write-Host "========================================================" -ForegroundColor Green
