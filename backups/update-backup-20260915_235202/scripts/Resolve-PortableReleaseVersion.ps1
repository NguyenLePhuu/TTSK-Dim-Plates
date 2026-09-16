[CmdletBinding()]
param([string]$MinimumVersion, [string]$CommitSha)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'GoogleDrive-ReleaseApi.ps1')
$prefix = (Get-Content -LiteralPath (Join-Path $PSScriptRoot '..\release-version-prefix.txt') -Raw).Trim()
if ($prefix -notmatch '\A(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\z') { throw 'Invalid version prefix.' }
$highest = [version]"$prefix.0"
try {
    $headers = @{ 'User-Agent' = 'TTSK-Portable-Publisher'; 'Cache-Control' = 'no-cache' }
    if ($env:GH_TOKEN) { $headers.Authorization = 'Bearer ' + $env:GH_TOKEN }
    $releases = Invoke-RestMethod -Uri 'https://api.github.com/repos/NguyenLePhuu/TTSK-Dim-Plates/releases/latest' -Headers $headers -TimeoutSec 30
    if ($releases.tag_name -cnotmatch '\Av((0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*))\z') { throw 'Invalid GitHub stable tag.' }
    $githubVersion = [version]$Matches[1]
    # A rerun for an already published commit verifies the same immutable release.
    if ($CommitSha -and $releases.body -and $releases.body.Contains("<!-- ttsk-portable:${CommitSha}:$githubVersion -->")) { return $githubVersion.ToString(3) }
    if ($githubVersion -gt $highest) { $highest = $githubVersion }
} catch {
    # Only 404 means no release yet. Authentication/network failures must not allocate an older version.
    if (!$_.Exception.PSObject.Properties['Response'] -or !$_.Exception.Response -or [int]$_.Exception.Response.StatusCode -ne 404) { throw 'Khong doc duoc phien ban GitHub. Dung phat hanh de tranh trung/lui version.' }
}
$settings = Get-DriveUpdateSettings
if ($settings['GoogleDriveManifestId'] -or $settings['GoogleDriveApiKey']) {
    $drive = Read-DriveManifest $settings['GoogleDriveManifestId'] $settings['GoogleDriveApiKey']
    if ($drive -and [version]$drive.version -gt $highest) { $highest = [version]$drive.version }
}
$next = [version]::new($highest.Major, $highest.Minor, ([int]$highest.Build + 1))
if ($MinimumVersion -and [version]$MinimumVersion -gt $next) { $next = [version]$MinimumVersion }
return $next.ToString(3)
