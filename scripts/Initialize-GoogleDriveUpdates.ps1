[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$FolderId,
      [Parameter(Mandatory=$true)][string]$ApiKey,
      [string]$ManifestId)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'GoogleDrive-ReleaseApi.ps1')
Assert-DriveId $FolderId
$settings = Get-DriveUpdateSettings
if (!$ManifestId) { $ManifestId = $settings['GoogleDriveManifestId'] }
if ($settings['GoogleDriveManifestId'] -and $ManifestId -ne $settings['GoogleDriveManifestId']) { throw 'Khong thay ID chi muc da cau hinh bang ID khac.' }
$temp = Join-Path ([IO.Path]::GetTempPath()) ('ttsk-drive-init-' + [guid]::NewGuid().ToString('N') + '.json')
try {
    [IO.File]::WriteAllText($temp, '{"schemaVersion":1,"product":"TTSK Dim Plates","initialized":false}', [Text.UTF8Encoding]::new($false))
    $id = $ManifestId
    if (!$id) { $id = Send-DriveFile -Path $temp -FolderId $FolderId -Name 'TTSK-update-latest.json' }
    Assert-DriveId $id
    # Require inherited public read permission on a dedicated release folder. Do not change sharing silently.
    $null = Read-DriveManifest $id $ApiKey
    $path = Join-Path $PSScriptRoot '..\TTSK Dim Plates\TTSK Dim Plates\App.config'
    [xml]$xml = Get-Content -LiteralPath $path -Raw
    foreach ($pair in @(@('GoogleDriveManifestId',$id), @('GoogleDriveApiKey',$ApiKey), @('GoogleDriveFolderId',$FolderId))) {
        $node = $xml.SelectSingleNode("/configuration/appSettings/add[@key='$($pair[0])']")
        if (!$node) { $node = $xml.CreateElement('add'); $node.SetAttribute('key', $pair[0]); $null = $xml.configuration.appSettings.AppendChild($node) }
        $node.SetAttribute('value', $pair[1])
    }
    $xml.Save($path)
    Write-Host "Da cau hinh Google Drive. Manifest ID: $id. Hay build/phat hanh portable de phan phoi cau hinh nay."
} finally { if (Test-Path -LiteralPath $temp) { Remove-Item -LiteralPath $temp -Force } }
