[CmdletBinding()]
param([string]$DriveFolderId = '1PKjvRjjE3yJQo1rbxN-O3XCE9O77fuNF')
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'GoogleDrive-ReleaseApi.ps1')
Assert-DriveId $DriveFolderId
$settings = Get-DriveUpdateSettings
if ($DriveFolderId -eq $settings['GoogleDriveFolderId']) {
    throw 'Hay dung thu muc source rieng, khong dung thu muc release cong khai.'
}
$null = Get-DriveAccessToken
try {
    $folder = Invoke-DriveApi -Uri "https://www.googleapis.com/drive/v3/files/${DriveFolderId}?fields=id,name,mimeType,trashed,capabilities(canAddChildren)"
} catch {
    throw 'Khong doc duoc thu muc luu code. Kiem tra mang va tai khoan Google da dang nhap co quyen truy cap thu muc. Luu source khong can latest.json hay chia se cong khai.'
}
if ($folder.trashed -or $folder.mimeType -ne 'application/vnd.google-apps.folder') {
    throw 'Noi luu code khong phai thu muc Drive hop le hoac da nam trong thung rac.'
}
if (!$folder.capabilities.canAddChildren) {
    throw 'Tai khoan Google da dang nhap chua co quyen them file vao thu muc luu code. Hay cap quyen Chinh sua cho tai khoan nay.'
}
Write-Host "[OK] Thu muc luu code: $($folder.name). Co quyen tai len; khong can chi muc latest.json."
