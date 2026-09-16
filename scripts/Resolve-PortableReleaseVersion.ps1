[CmdletBinding()]
param([string]$MinimumVersion, [string]$CommitSha, [string]$RequestedVersion,
      [switch]$Reserve, [string]$ReservationPath)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'GoogleDrive-ReleaseApi.ps1')
function ConvertTo-ReleaseVersion([string]$Text) {
    if ($Text -cnotmatch '\A(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\z') { throw 'Invalid release version.' }
    return [version]$Text
}

function Read-GitHubVersionData([string]$Path) {
    $headers = @{ 'User-Agent' = 'TTSK-Portable-Publisher'; 'Cache-Control' = 'no-cache' }
    if ($env:GH_TOKEN) { $headers.Authorization = 'Bearer ' + $env:GH_TOKEN }
    try { return Invoke-RestMethod -Uri "https://api.github.com/repos/NguyenLePhuu/TTSK-Dim-Plates/$Path" -Headers $headers -TimeoutSec 30 }
    catch {
        if ($_.Exception.PSObject.Properties['Response'] -and $_.Exception.Response -and [int]$_.Exception.Response.StatusCode -eq 404) { return $null }
        throw 'Khong doc duoc trang thai GitHub. Dung phat hanh de tranh trung/lui version.'
    }
}

$hasRequestedVersion = $PSBoundParameters.ContainsKey('RequestedVersion')
if ($hasRequestedVersion) { $null = ConvertTo-ReleaseVersion $RequestedVersion }
if ($Reserve -and ($hasRequestedVersion -or $CommitSha)) { throw 'Cannot reserve a workflow version twice.' }
if (!$ReservationPath) { $ReservationPath = Join-Path $PSScriptRoot '..\.codex-artifacts\portable-update\version-reservation.json' }
$reservationLock = $null
try {
    if ($Reserve) {
        New-Item -ItemType Directory -Path (Split-Path -Parent $ReservationPath) -Force | Out-Null
        try { $reservationLock = [IO.File]::Open(($ReservationPath + '.lock'), 'OpenOrCreate', 'ReadWrite', 'None') }
        catch { throw 'Mot tien trinh khac dang cap so phien ban. Thu lai sau.' }
    }
    $prefix = (Get-Content -LiteralPath (Join-Path $PSScriptRoot '..\update\release-version-prefix.txt') -Raw).Trim()
    if ($prefix -notmatch '\A(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\z') { throw 'Invalid version prefix.' }
    $highest = ConvertTo-ReleaseVersion "$prefix.0"
    $github = Read-GitHubVersionData 'releases/latest'
    $githubVersion = $null
    if ($github) {
        if ($github.tag_name -cnotmatch '\Av((0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*))\z') { throw 'Invalid GitHub stable tag.' }
        $githubVersion = ConvertTo-ReleaseVersion $Matches[1]
        # A rerun verifies the exact immutable release; it does not consume a new number.
        if ($CommitSha -and $github.body -and $github.body.Contains("<!-- ttsk-portable:${CommitSha}:$githubVersion -->") -and
            (!$RequestedVersion -or $RequestedVersion -ceq $githubVersion.ToString(3))) { return $githubVersion.ToString(3) }
        if ($githubVersion -gt $highest) { $highest = $githubVersion }
    }
    $settings = Get-DriveUpdateSettings
    $drive = $null
    if ($settings['GoogleDriveManifestId']) {
        # An unreadable configured source must not be treated as version zero by a publisher.
        $drive = Read-DriveManifest $settings['GoogleDriveManifestId'] $settings['GoogleDriveApiKey']
        if ($drive -and [version]$drive.version -gt $highest) { $highest = [version]$drive.version }
    }
    if ($hasRequestedVersion) {
        $requested = ConvertTo-ReleaseVersion $RequestedVersion
        if ($githubVersion -and $requested -le $githubVersion) { throw 'GitHub version already exists or this queued release is stale.' }
        if ($drive -and $requested -eq [version]$drive.version) { throw 'Version already published on Google Drive. Allocate a new release number.' }
        # A later Drive request may finish first. Preserve this earlier request's reserved number.
        return $requested.ToString(3)
    }
    # Include requests queued on GitHub, not just releases that Actions finished publishing.
    $remoteRequest = Read-GitHubVersionData 'contents/update/release-request.json?ref=main'
    if (!$remoteRequest) { $remoteRequest = Read-GitHubVersionData 'contents/release-request.json?ref=main' }
    if ($remoteRequest) {
        if ($remoteRequest.encoding -ne 'base64') { throw 'Invalid remote release request encoding.' }
        $request = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($remoteRequest.content)) | ConvertFrom-Json
        if ($request.PSObject.Properties['version']) {
            $queued = ConvertTo-ReleaseVersion $request.version
            if ($queued -gt $highest) { $highest = $queued }
        }
    }
    # Also cover a local push/upload which failed, or Actions which has not started yet.
    foreach ($path in @($ReservationPath, (Join-Path $PSScriptRoot '..\update\release-request.json'))) {
        if (!(Test-Path -LiteralPath $path)) { continue }
        $saved = Get-Content -LiteralPath $path -Raw | ConvertFrom-Json
        if ($saved.PSObject.Properties['version']) {
            $reserved = ConvertTo-ReleaseVersion $saved.version
            if ($reserved -gt $highest) { $highest = $reserved }
        } elseif ($path -eq $ReservationPath) { throw 'Damaged version reservation. Do not silently reuse a number.' }
    }
    if ($highest.Build -eq [int]::MaxValue) { throw 'Patch version exhausted; increase the release prefix.' }
    $next = [version]::new($highest.Major, $highest.Minor, ($highest.Build + 1))
    if ($MinimumVersion) {
        $minimum = ConvertTo-ReleaseVersion $MinimumVersion
        if ($minimum -gt $next) { $next = $minimum }
    }
    if ($Reserve) {
        $temporary = $ReservationPath + '.' + [guid]::NewGuid().ToString('N') + '.tmp'
        try {
            [IO.File]::WriteAllText($temporary, (@{version=$next.ToString(3);reservedUtc=[DateTime]::UtcNow.ToString('o')} | ConvertTo-Json), [Text.UTF8Encoding]::new($false))
            Move-Item -LiteralPath $temporary -Destination $ReservationPath -Force
        } finally { if (Test-Path -LiteralPath $temporary) { Remove-Item -LiteralPath $temporary -Force } }
    }
    return $next.ToString(3)
} finally { if ($reservationLock) { $reservationLock.Dispose() } }
