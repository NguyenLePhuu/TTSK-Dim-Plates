[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$ClientJson)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'GoogleDrive-ReleaseApi.ps1')
Add-Type -AssemblyName System.Web
$configuration = Get-Content -LiteralPath $ClientJson -Raw | ConvertFrom-Json
if (!$configuration.PSObject.Properties['installed']) { throw 'Can OAuth client loai Desktop app (installed), khong phai Web app.' }
$client = $configuration.installed
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
    Write-Host 'Dang mo trinh duyet dang nhap Google. Chon tai khoan co quyen ghi thu muc RELEASE va xem quyen tren trang Google.'
    # This is an interactive login the user must see and complete in the system browser.
    Start-Process ('https://accounts.google.com/o/oauth2/v2/auth?' + $query)
    $deadline = [DateTime]::UtcNow.AddMinutes(10)
    $code = $null
    while (!$code -and [DateTime]::UtcNow -lt $deadline) {
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
                $valid = $first -match '\AGET (/\?[^ ]+) HTTP/1\.[01]\z'
                $callback = if ($valid) { [Web.HttpUtility]::ParseQueryString(([Uri]($redirect.TrimEnd('/') + $Matches[1])).Query) } else { $null }
                $valid = $valid -and $callback['state'] -ceq $state
                $body = if ($valid) { 'Google response received. Return to TTSK publisher; this tab can be closed.' } else { 'Invalid callback.' }
                $status = if ($valid) { '200 OK' } else { '400 Bad Request' }
                $bytes = [Text.Encoding]::ASCII.GetBytes("HTTP/1.1 $status`r`nContent-Type: text/plain`r`nCache-Control: no-store`r`nConnection: close`r`nContent-Length: $($body.Length)`r`n`r`n$body")
                $stream.Write($bytes,0,$bytes.Length)
                if ($valid) {
                    if ($callback['error']) { throw 'Google authorization was declined.' }
                    $code = $callback['code']
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
