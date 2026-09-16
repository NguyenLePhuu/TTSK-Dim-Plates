[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

# Nạp thư viện API Drive
. (Join-Path $PSScriptRoot 'GoogleDrive-ReleaseApi.ps1')

Write-Host '========================================================' -ForegroundColor Cyan
Write-Host '  KIEM TRA CAU HINH GOOGLE DRIVE - TTSK DIM PLATES' -ForegroundColor Cyan
Write-Host '========================================================' -ForegroundColor Cyan
Write-Host ''

# 1. Kiểm tra App.config
Write-Host '[1/5] Kiem tra App.config...' -ForegroundColor Yellow
$settings = Get-DriveUpdateSettings
$folderId = $settings['GoogleDriveFolderId']
$manifestId = $settings['GoogleDriveManifestId']
$apiKey = $settings['GoogleDriveApiKey']

$ok = $true
if ([string]::IsNullOrWhiteSpace($folderId)) {
    Write-Host '  [LOI] GoogleDriveFolderId trong!' -ForegroundColor Red
    $ok = $false
} else {
    Write-Host "  [OK] GoogleDriveFolderId = $folderId" -ForegroundColor Green
}

if ([string]::IsNullOrWhiteSpace($manifestId)) {
    Write-Host '  [LOI] GoogleDriveManifestId trong!' -ForegroundColor Red
    $ok = $false
} else {
    Write-Host "  [OK] GoogleDriveManifestId = $manifestId" -ForegroundColor Green
}

if ([string]::IsNullOrWhiteSpace($apiKey)) {
    Write-Host '  [LOI] GoogleDriveApiKey trong! Chay Cai_Dat_Google_Drive.bat truoc.' -ForegroundColor Red
    $ok = $false
} else {
    # Chỉ hiện 8 ký tự đầu
    $masked = $apiKey.Substring(0, [Math]::Min(8, $apiKey.Length)) + '...'
    Write-Host "  [OK] GoogleDriveApiKey = $masked" -ForegroundColor Green
}

if (!$ok) {
    Write-Host ''
    Write-Host '[THAT BAI] Chua cau hinh du. Chay Cai_Dat_Google_Drive.bat de thiet lap.' -ForegroundColor Red
    exit 1
}

# 2. Kiểm tra đọc manifest công khai
Write-Host ''
Write-Host '[2/5] Thu doc manifest tu Google Drive (khong can dang nhap)...' -ForegroundColor Yellow
try {
    $manifest = Read-DriveManifest $manifestId $apiKey
    if ($manifest) {
        Write-Host "  [OK] Drive dang phat hanh phien ban: $($manifest.version)" -ForegroundColor Green
        Write-Host "  [OK] ZIP file ID: $($manifest.zipFileId)" -ForegroundColor Green
    } else {
        Write-Host '  [OK] Manifest da tao nhung chua co ban phat hanh nao (initialized=false). Binh thuong.' -ForegroundColor Yellow
    }
} catch {
    Write-Host "  [LOI] Khong doc duoc manifest: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host '  Kiem tra: Thu muc Drive da chia se cong khai? API key dung? Google Drive API da bat?' -ForegroundColor Yellow
    exit 1
}

# 3. Kiểm tra đăng nhập publisher
Write-Host ''
Write-Host '[3/5] Kiem tra dang nhap publisher (OAuth)...' -ForegroundColor Yellow
$credPath = Get-DriveCredentialPath
if (Test-Path -LiteralPath $credPath) {
    Write-Host "  [OK] Da luu dang nhap tai: $credPath" -ForegroundColor Green
} else {
    Write-Host '  [CANH BAO] Chua dang nhap. Chi can de NHAN cap nhat thi khong can dang nhap.' -ForegroundColor Yellow
    Write-Host '  De PHAT HANH len Drive, chay Cai_Dat_Google_Drive.bat.' -ForegroundColor Yellow
}

# 4. Kiểm tra lấy access token (nếu đã đăng nhập)
Write-Host ''
Write-Host '[4/5] Thu lam moi access token...' -ForegroundColor Yellow
if (Test-Path -LiteralPath $credPath) {
    try {
        $token = Get-DriveAccessToken
        if ($token) {
            Write-Host '  [OK] Access token hop le! San sang phat hanh.' -ForegroundColor Green
        }
    } catch {
        Write-Host "  [CANH BAO] Khong lam moi duoc token: $($_.Exception.Message)" -ForegroundColor Yellow
        Write-Host '  OAuth Testing mode het han sau 7 ngay. Chay lai Cai_Dat_Google_Drive.bat de dang nhap lai.' -ForegroundColor Yellow
    }
} else {
    Write-Host '  [BO QUA] Chua dang nhap, khong can token.' -ForegroundColor Yellow
}

# 5. Kiểm tra GitHub
Write-Host ''
Write-Host '[5/5] Kiem tra phien ban GitHub...' -ForegroundColor Yellow
try {
    $headers = @{ 'User-Agent' = 'TTSK-Drive-Test'; 'Cache-Control' = 'no-cache' }
    if ($env:GH_TOKEN) { $headers.Authorization = 'Bearer ' + $env:GH_TOKEN }
    $release = Invoke-RestMethod -Uri 'https://api.github.com/repos/NguyenLePhuu/TTSK-Dim-Plates/releases/latest' -Headers $headers -TimeoutSec 15
    Write-Host "  [OK] GitHub phien ban moi nhat: $($release.tag_name)" -ForegroundColor Green
} catch {
    $status = if ($_.Exception.PSObject.Properties['Response'] -and $_.Exception.Response) { [int]$_.Exception.Response.StatusCode } else { 0 }
    if ($status -eq 404) {
        Write-Host '  [OK] Chua co ban phat hanh nao tren GitHub. Binh thuong cho du an moi.' -ForegroundColor Yellow
    } else {
        Write-Host "  [CANH BAO] Khong kiem tra duoc GitHub: $($_.Exception.Message)" -ForegroundColor Yellow
    }
}

# Tổng kết
Write-Host ''
Write-Host '========================================================' -ForegroundColor Green
Write-Host '  KIEM TRA HOAN TAT' -ForegroundColor Green
Write-Host '========================================================' -ForegroundColor Green
Write-Host ''
Write-Host 'Ung dung TTSK se tu dong kiem tra cap nhat tu ca 2 nguon:' -ForegroundColor Cyan
Write-Host '  - GitHub Releases' -ForegroundColor White
Write-Host '  - Google Drive (neu da cau hinh)' -ForegroundColor White
Write-Host ''
Write-Host 'De PHAT HANH ban cap nhat, chay: Cap_Nhat_Portable.bat' -ForegroundColor Cyan
