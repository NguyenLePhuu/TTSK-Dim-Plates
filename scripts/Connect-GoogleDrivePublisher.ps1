[CmdletBinding()]
param([string]$ClientJson)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'GoogleDrive-ReleaseApi.ps1')
Add-Type -AssemblyName System.Web
if ($ClientJson) {
    $configuration = Get-Content -LiteralPath $ClientJson -Raw | ConvertFrom-Json
    if (!$configuration.PSObject.Properties['installed']) { throw 'Can OAuth client loai Desktop app (installed), khong phai Web app.' }
    $client = $configuration.installed
} else {
    try {
        $saved = Import-Clixml -LiteralPath (Get-DriveCredentialPath)
        $client = [pscustomobject]@{client_id=$saved.ClientId;client_secret=[Net.NetworkCredential]::new('', $saved.ClientSecret).Password}
    } catch { throw 'Khong doc duoc OAuth client da luu. Chay lai voi -ClientJson va duong dan OAuth Desktop client JSON.' }
}
if (!$client.client_id -or !$client.client_secret) { throw 'OAuth client JSON thieu client_id/client_secret.' }
function New-OAuthRandom {
    $bytes = New-Object byte[] 32
    $rng = [Security.Cryptography.RandomNumberGenerator]::Create()
    try { $rng.GetBytes($bytes) } finally { $rng.Dispose() }
    return [Convert]::ToBase64String($bytes).TrimEnd('=').Replace('+','-').Replace('/','_')
}
$state = New-OAuthRandom
$verifier = New-OAuthRandom
$sha = [Security.Cryptography.SHA256]::Create()
try { $challenge = [Convert]::ToBase64String($sha.ComputeHash([Text.Encoding]::ASCII.GetBytes($verifier))).TrimEnd('=').Replace('+','-').Replace('/','_') }
finally { $sha.Dispose() }
# Bind before opening authorization, to an ephemeral loopback-only port; no URL ACL/admin rights needed.
$listener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 0)
$listener.Start()
try {
    $redirect = "http://127.0.0.1:$($listener.LocalEndpoint.Port)/"
    $parameters = [ordered]@{client_id=$client.client_id;redirect_uri=$redirect;response_type='code';
        scope='https://www.googleapis.com/auth/drive';access_type='offline';prompt='consent';
        state=$state;code_challenge=$challenge;code_challenge_method='S256'}
    $query = ($parameters.GetEnumerator() | ForEach-Object { $_.Key + '=' + [Uri]::EscapeDataString($_.Value) }) -join '&'
    $authUrl = 'https://accounts.google.com/o/oauth2/v2/auth?' + $query
    Write-Host 'Dang mo trinh duyet dang nhap Google. Chon tai khoan co quyen ghi thu muc can luu va xem quyen tren trang Google.'
    Write-Host '----------------------------------------------------------------------------------' -ForegroundColor Cyan
    Write-Host 'MEO: Neu trinh duyet bao "This site can''t be reached" hoac "Connection refused":' -ForegroundColor Yellow
    Write-Host 'Ban chi can COPY toan bo duong link tren thanh dia chi cua trinh duyet' -ForegroundColor Yellow
    Write-Host '(dang: http://127.0.0.1:.../?state=...&code=...) roi DAN (Paste) vao day va Enter!' -ForegroundColor Yellow
    Write-Host '----------------------------------------------------------------------------------' -ForegroundColor Cyan
    Start-Process $authUrl
    $deadline = [DateTime]::UtcNow.AddMinutes(10)
    $code = $null
    while (!$code -and [DateTime]::UtcNow -lt $deadline) {
        # Kiem tra neu nguoi dung dan URL hoac ma vao cua so Console
        try {
            if ([Console]::KeyAvailable) {
                $line = [Console]::ReadLine()
                $manualUri = $null
                if ([Uri]::TryCreate($line.Trim(),[UriKind]::Absolute,[ref]$manualUri) -and
                    $manualUri.GetLeftPart([UriPartial]::Path) -ceq $redirect) {
                    $manualQuery = [Web.HttpUtility]::ParseQueryString($manualUri.Query)
                    if ($manualQuery.Get('state') -cne $state -or !$manualQuery.Get('code')) {
                        Write-Host 'Link khong thuoc phien dang nhap nay. Hay dung trang Google vua mo.' -ForegroundColor Yellow
                        continue
                    }
                    $code = $manualQuery.Get('code')
                    Write-Host 'Da nhan ma tu duong link ban dan vao! Dang xac thuc...' -ForegroundColor Green
                    break
                }
            }
        } catch { }

        if (!$listener.Pending()) { Start-Sleep -Milliseconds 200; continue }
        $connection = $listener.AcceptTcpClient()
        try {
            $connection.ReceiveTimeout=5000; $connection.SendTimeout=5000
            $stream = $connection.GetStream()
            $reader = [IO.StreamReader]::new($stream, [Text.Encoding]::ASCII, $false, 1024, $true)
            try {
                # Bound incoming HTTP headers; authorization responses fit comfortably within 16 KiB.
                $request = New-Object Text.StringBuilder
                while ($request.Length -lt 16384) {
                    $character = $reader.Read()
                    if ($character -lt 0) { break }
                    $null = $request.Append([char]$character)
                    if ($request.Length -ge 4 -and $request.ToString($request.Length - 4,4) -eq "`r`n`r`n") { break }
                }
                $first = ($request.ToString() -split "`r`n")[0]
                $match = $first -match '\AGET (/\?[^ ]+) HTTP/1\.[01]\z'
                if ($match) {
                    $callback = [Web.HttpUtility]::ParseQueryString(([Uri]($redirect.TrimEnd('/') + $Matches[1])).Query)
                    $cbState = if ($null -ne $callback) { $callback.Get('state') } else { $null }
                    if ($cbState -ceq $state) {
                        $body = 'Google response received. Return to TTSK publisher; this tab can be closed.'
                        $bytes = [Text.Encoding]::ASCII.GetBytes("HTTP/1.1 200 OK`r`nContent-Type: text/plain`r`nCache-Control: no-store`r`nConnection: close`r`nContent-Length: $($body.Length)`r`n`r`n$body")
                        $stream.Write($bytes, 0, $bytes.Length)
                        if ($callback.Get('error')) { throw 'Google authorization was declined.' }
                        $code = $callback.Get('code')
                    } else {
                        $body = 'Invalid state.'
                        $bytes = [Text.Encoding]::ASCII.GetBytes("HTTP/1.1 400 Bad Request`r`nContent-Type: text/plain`r`nConnection: close`r`nContent-Length: $($body.Length)`r`n`r`n$body")
                        $stream.Write($bytes, 0, $bytes.Length)
                    }
                } else {
                    # Bỏ qua request rác của trình duyệt (ví dụ: favicon.ico) và tiếp tục chờ callback
                    $bytes = [Text.Encoding]::ASCII.GetBytes("HTTP/1.1 404 Not Found`r`nConnection: close`r`nContent-Length: 0`r`n`r`n")
                    $stream.Write($bytes, 0, $bytes.Length)
                }
            } finally { $reader.Dispose() }
        } finally { $connection.Dispose() }
    }
    if (!$code) { throw 'Het thoi gian dang nhap Google (10 phut). Chay lai de thu lai.' }
    try {
        $token = Invoke-RestMethod -Uri 'https://oauth2.googleapis.com/token' -Method POST -ContentType 'application/x-www-form-urlencoded' -Body @{
            code=$code;client_id=$client.client_id;client_secret=$client.client_secret;redirect_uri=$redirect;
            grant_type='authorization_code';code_verifier=$verifier
        } -TimeoutSec 30 -ErrorAction Stop
        if (!$token.PSObject.Properties['refresh_token'] -or !$token.refresh_token) { throw 'No refresh token.' }
    } catch { throw 'Khong nhan duoc OAuth refresh token. Kiem tra client Desktop, consent va test user; chay lai dang nhap.' }
    Save-DriveCredential $client.client_id $client.client_secret $token.refresh_token
    Write-Host 'Da luu dang nhap Google ma hoa bang Windows DPAPI. BAT se tu lam moi access token; khong dong goi credentials vao portable.'
} finally { $listener.Stop() }
