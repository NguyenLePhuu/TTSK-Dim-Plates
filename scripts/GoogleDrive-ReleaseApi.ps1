# Shared by setup, version resolution and publisher. Never write OAuth tokens to disk/logs.
Set-StrictMode -Version Latest
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
$script:TtskDriveToken = $null
$script:TtskDriveTokenExpiry = [DateTime]::MinValue

function Get-DriveCredentialPath {
    return Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'TTSK Dim Plates\Publisher\google-oauth.xml'
}

function Save-DriveCredential([string]$ClientId, [string]$ClientSecret, [string]$RefreshToken, [string]$Path = (Get-DriveCredentialPath)) {
    if (!$ClientId -or !$ClientSecret -or !$RefreshToken) { throw 'OAuth credentials incomplete.' }
    $dir = Split-Path -Parent $Path
    New-Item -ItemType Directory -Path $dir -Force | Out-Null
    $temporary = $Path + '.' + [guid]::NewGuid().ToString('N') + '.tmp'
    try {
        @{ ClientId=$ClientId; ClientSecret=(ConvertTo-SecureString $ClientSecret -AsPlainText -Force);
           RefreshToken=(ConvertTo-SecureString $RefreshToken -AsPlainText -Force) } | Export-Clixml -LiteralPath $temporary
        Move-Item -LiteralPath $temporary -Destination $Path -Force
    } finally { if (Test-Path -LiteralPath $temporary) { Remove-Item -LiteralPath $temporary -Force } }
}

function Get-DriveAccessToken([string]$CredentialPath = (Get-DriveCredentialPath)) {
    if ($env:TTSK_GOOGLE_DRIVE_ACCESS_TOKEN) { return $env:TTSK_GOOGLE_DRIVE_ACCESS_TOKEN }
    if ($script:TtskDriveToken -and [DateTime]::UtcNow -lt $script:TtskDriveTokenExpiry) { return $script:TtskDriveToken }
    if (!(Test-Path -LiteralPath $CredentialPath)) { throw 'Chua dang nhap Google cho BAT. Chay scripts/Connect-GoogleDrivePublisher.ps1 voi OAuth Desktop client JSON. Khong gui token vao chat.' }
    try {
        $saved = Import-Clixml -LiteralPath $CredentialPath
        $secret = [Net.NetworkCredential]::new('', $saved.ClientSecret).Password
        $refresh = [Net.NetworkCredential]::new('', $saved.RefreshToken).Password
        $token = Invoke-RestMethod -Uri 'https://oauth2.googleapis.com/token' -Method POST -ContentType 'application/x-www-form-urlencoded' -Body @{
            client_id=$saved.ClientId; client_secret=$secret; refresh_token=$refresh; grant_type='refresh_token'
        } -TimeoutSec 30 -ErrorAction Stop
        if (!$token.access_token -or $token.expires_in -le 0) { throw 'Missing token.' }
        $script:TtskDriveToken = [string]$token.access_token
        $script:TtskDriveTokenExpiry = [DateTime]::UtcNow.AddSeconds([Math]::Max(0, [int]$token.expires_in - 60))
        return $script:TtskDriveToken
    } catch { throw 'Khong lam moi duoc dang nhap Google. Chay lai Connect-GoogleDrivePublisher.ps1 (hoac kiem tra Internet). Credentials chi dung duoc voi tai khoan Windows da luu.' }
}

function Get-DriveUpdateSettings {
    $path = Join-Path $PSScriptRoot '..\TTSK Dim Plates\TTSK Dim Plates\App.config'
    [xml]$xml = Get-Content -LiteralPath $path -Raw
    $values = @{}
    foreach ($item in $xml.SelectNodes('/configuration/appSettings/add')) { $values[$item.key] = [string]$item.value }
    return $values
}

function Assert-DriveId([string]$Id) {
    if ($Id -notmatch '\A[A-Za-z0-9_-]+\z') { throw 'Google Drive File/Folder ID khong hop le.' }
}

function Get-DriveMediaUrl([string]$Id, [string]$ApiKey) {
    Assert-DriveId $Id
    return "https://drive.usercontent.google.com/download?id=${Id}&export=download"
}

function Invoke-DriveApi {
    param([string]$Uri, [string]$Method = 'GET', [string]$Body, [string]$InFile,
          [string]$OutFile, [string]$ContentType = 'application/json', [switch]$Public, [switch]$Raw)
    $parsed = [Uri]$Uri
    $publicDownload = $Public -and $Method -eq 'GET' -and $parsed.Host -eq 'drive.usercontent.google.com' -and $parsed.AbsolutePath -eq '/download' -and $parsed.Query -cmatch '\A\?id=[A-Za-z0-9_-]+&export=download\z'
    if ($parsed.Scheme -ne 'https' -or (!$publicDownload -and $parsed.Host -ne 'www.googleapis.com') -or !$parsed.IsDefaultPort -or $parsed.UserInfo -or $parsed.Fragment) { throw 'Unsafe Drive API endpoint.' }
    $arguments = @{ Uri = $Uri; Method = $Method; ErrorAction = 'Stop'; TimeoutSec = 300; MaximumRedirection = 0; Headers = @{ 'Cache-Control' = 'no-cache' } }
    if (!$Public) {
        $arguments.Headers.Authorization = 'Bearer ' + (Get-DriveAccessToken)
    }
    if ($PSBoundParameters.ContainsKey('Body')) { $arguments.Body = [Text.Encoding]::UTF8.GetBytes($Body); $arguments.ContentType = $ContentType }
    if ($InFile) { $arguments.InFile = $InFile; $arguments.ContentType = $ContentType }
    if ($OutFile) { $arguments.OutFile = $OutFile }
    try {
        if ($publicDownload) {
            $response = Invoke-WebRequest @arguments -UseBasicParsing
            if ($OutFile) { return }
            if ($response.Content -is [byte[]]) { return [Text.Encoding]::UTF8.GetString($response.Content) }
            return [string]$response.Content
        }
        if ($Raw) { return Invoke-WebRequest @arguments -UseBasicParsing }
        return Invoke-RestMethod @arguments
    } catch {
        # Do not include the request URI (API key) or authentication headers in transcript.
        throw 'Google Drive that bai. Kiem tra Internet, quyen chia se cong khai, gioi han tai file va dang nhap publisher.'
    }
}

function Send-DriveFile([string]$Path, [string]$FolderId, [string]$Name, [string]$FileId) {
    $mimeType = if ($Path.EndsWith('.json', [StringComparison]::OrdinalIgnoreCase)) { 'application/json' }
        elseif ($Path.EndsWith('.sha256', [StringComparison]::OrdinalIgnoreCase)) { 'text/plain' }
        else { 'application/zip' }
    if ($FileId) {
        Assert-DriveId $FileId
        $uri = "https://www.googleapis.com/upload/drive/v3/files/${FileId}?uploadType=resumable"
        $method = 'PATCH'
        $body = @{ mimeType = $mimeType } | ConvertTo-Json -Compress
    } else {
        Assert-DriveId $FolderId
        $uri = 'https://www.googleapis.com/upload/drive/v3/files?uploadType=resumable'
        $method = 'POST'
        $body = @{ name = $Name; parents = @($FolderId); mimeType = $mimeType } | ConvertTo-Json -Compress
    }
    $session = Invoke-DriveApi -Uri $uri -Method $method -Body $body -Raw
    $location = [string](@($session.Headers.Location)[0])
    if (!$location) { throw 'Drive upload session has no Location.' }
    $uploaded = Invoke-DriveApi -Uri $location -Method PUT -InFile $Path -ContentType $mimeType
    Assert-DriveId $uploaded.id
    return [string]$uploaded.id
}

function Assert-DriveManifest($Manifest) {
    foreach ($field in @('schemaVersion','product','version','notes','zipFileId','sha256FileId','zipSizeBytes','sha256SizeBytes')) {
        if (!$Manifest.PSObject.Properties[$field]) { throw "Drive manifest thieu $field." }
    }
    if ($Manifest.schemaVersion -ne 1 -or $Manifest.product -cne 'TTSK Dim Plates' -or
        $Manifest.version -cnotmatch '\A(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\z') { throw 'Drive manifest khong hop le.' }
    $null = [version]$Manifest.version
    Assert-DriveId $Manifest.zipFileId
    Assert-DriveId $Manifest.sha256FileId
    if ($Manifest.zipFileId -eq $Manifest.sha256FileId -or $Manifest.zipSizeBytes -le 0 -or $Manifest.zipSizeBytes -gt 209715200 -or $Manifest.sha256SizeBytes -le 0 -or $Manifest.sha256SizeBytes -gt 4096) { throw 'Drive asset size/ID khong hop le.' }
}

function Read-DriveManifest([string]$Id, [string]$ApiKey) {
    $manifest = Invoke-DriveApi -Uri (Get-DriveMediaUrl $Id $ApiKey) -Public
    if ($manifest -is [string]) { $manifest = $manifest | ConvertFrom-Json }
    # Initial empty index, created by setup, is not a release.
    if ($manifest.PSObject.Properties['initialized'] -and $manifest.initialized -eq $false -and $manifest.product -ceq 'TTSK Dim Plates') { return $null }
    Assert-DriveManifest $manifest
    return $manifest
}
