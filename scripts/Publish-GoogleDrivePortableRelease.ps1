[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$Version,
      [Parameter(Mandatory=$true)][string]$PackageDirectory,
      [Parameter(Mandatory=$true)][string]$ReleaseNotes)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'GoogleDrive-ReleaseApi.ps1')
if ($Version -cnotmatch '\A(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\z') { throw 'Invalid version.' }
$null = [version]$Version
if ([string]::IsNullOrWhiteSpace($ReleaseNotes)) { throw 'Release notes are required.' }
$settings = Get-DriveUpdateSettings
$folder = $settings['GoogleDriveFolderId']; $index = $settings['GoogleDriveManifestId']; $key = $settings['GoogleDriveApiKey']
Assert-DriveId $folder
Assert-DriveId $index
$before = Read-DriveManifest $index $key
$beforeJson = $before | ConvertTo-Json -Compress
if ($before -and [version]$before.version -ge [version]$Version) { throw 'Drive da co phien ban bang/moi hon. Khong ghi de ban da phat hanh.' }
$null = & (Join-Path $PSScriptRoot 'Resolve-PortableReleaseVersion.ps1') -RequestedVersion $Version
$zipName = 'TTSK-Dim-Plates-Portable.zip'
$zip = Join-Path $PackageDirectory $zipName
$sha = "$zip.sha256"
$line = (Get-Content -LiteralPath $sha -Raw).Trim()
if ($line -cnotmatch '\A([0-9a-fA-F]{64})  TTSK-Dim-Plates-Portable\.zip\z') { throw 'Invalid ZIP checksum file.' }
$expected = $Matches[1]
if ((Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash -ne $expected) { throw 'ZIP checksum mismatch.' }
if ((Get-Item -LiteralPath $zip).Length -gt 209715200) { throw 'ZIP exceeds updater size limit.' }
# New asset IDs per release: old releases remain available while latest is published last.
$zipId = Send-DriveFile -Path $zip -FolderId $folder -Name "v$Version-$zipName"
$shaId = Send-DriveFile -Path $sha -FolderId $folder -Name "v$Version-$zipName.sha256"
$verify = Join-Path $PackageDirectory ('drive-verify-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $verify | Out-Null
$remoteZip = Join-Path $verify $zipName
$remoteSha = Join-Path $verify "$zipName.sha256"
$null = Invoke-DriveApi -Uri (Get-DriveMediaUrl $zipId $key) -Public -OutFile $remoteZip
$null = Invoke-DriveApi -Uri (Get-DriveMediaUrl $shaId $key) -Public -OutFile $remoteSha
if ((Get-FileHash -LiteralPath $remoteZip -Algorithm SHA256).Hash -ne $expected -or
    (Get-FileHash -LiteralPath $remoteSha -Algorithm SHA256).Hash -ne (Get-FileHash -LiteralPath $sha -Algorithm SHA256).Hash) { throw 'Public Drive assets do not match package; latest index unchanged.' }
$manifest = [ordered]@{ schemaVersion=1; product='TTSK Dim Plates'; version=$Version; notes=$ReleaseNotes;
    zipFileId=$zipId; sha256FileId=$shaId; zipSizeBytes=(Get-Item -LiteralPath $zip).Length; sha256SizeBytes=(Get-Item -LiteralPath $sha).Length }
$manifestPath = Join-Path $verify 'latest.json'
[IO.File]::WriteAllText($manifestPath, ($manifest | ConvertTo-Json), [Text.UTF8Encoding]::new($false))
$now = Read-DriveManifest $index $key
if (($now | ConvertTo-Json -Compress) -cne $beforeJson) { throw 'Drive latest changed during upload. Stop; run only one publisher at a time.' }
# Recheck GitHub immediately before exposing this release too.
$null = & (Join-Path $PSScriptRoot 'Resolve-PortableReleaseVersion.ps1') -RequestedVersion $Version
$null = Send-DriveFile -Path $manifestPath -FileId $index
$published = Read-DriveManifest $index $key
if (!$published -or $published.version -cne $Version -or $published.zipFileId -cne $zipId -or $published.sha256FileId -cne $shaId) { throw 'Cannot verify published Drive index. Inspect latest before retrying.' }
Write-Host "[THANH CONG] Google Drive v${Version}: da tai lai ZIP/SHA256 cong khai va xac minh chi muc."
