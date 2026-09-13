[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$Version,
      [Parameter(Mandatory=$true)][string]$CommitSha,
      [Parameter(Mandatory=$true)][string]$PackageDirectory)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repo = 'NguyenLePhuu/TTSK-Dim-Plates'
if ($Version -notmatch '^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$') { throw 'Invalid version.' }
$numericVersion = [version]$Version
if ($CommitSha -notmatch '^[a-f0-9]{40}$') { throw 'Invalid commit SHA.' }
$tag = "v$Version"
$zipName = 'TTSK-Dim-Plates-Portable.zip'
$shaName = "$zipName.sha256"
$zip = Join-Path $PackageDirectory $zipName
$sha = Join-Path $PackageDirectory $shaName
function Invoke-Gh([string[]]$Arguments) {
    $output = & gh @Arguments
    if ($LASTEXITCODE -ne 0) { throw "GitHub CLI failed: $($Arguments[0]) $($Arguments[1])" }
    return $output
}
function Assert-Checksum([string]$ZipPath, [string]$ShaPath) {
    $line = (Get-Content -LiteralPath $ShaPath -Raw).Trim()
    if ($line -notmatch '\A([0-9a-fA-F]{64})  TTSK-Dim-Plates-Portable\.zip\z') { throw 'Invalid asset checksum.' }
    if ((Get-FileHash -LiteralPath $ZipPath -Algorithm SHA256).Hash -ne $Matches[1]) { throw 'ZIP checksum mismatch.' }
}
function Get-ZipContents([string]$Path) {
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [IO.Compression.ZipFile]::OpenRead($Path)
    $result = [Collections.Generic.Dictionary[string,string]]::new([StringComparer]::OrdinalIgnoreCase)
    try {
        foreach ($entry in $archive.Entries) {
            if ($entry.FullName.EndsWith('/')) { continue }
            if ($result.ContainsKey($entry.FullName)) { throw 'Duplicate ZIP path.' }
            $inputStream = $entry.Open(); $hasher = [Security.Cryptography.SHA256]::Create()
            try { $result.Add($entry.FullName, ([BitConverter]::ToString($hasher.ComputeHash($inputStream))).Replace('-','')) }
            finally { $inputStream.Dispose(); $hasher.Dispose() }
        }
    } finally { $archive.Dispose() }
    return ,$result
}
Assert-Checksum $zip $sha
$localFiles = Get-ZipContents $zip
# Explicit API listing distinguishes a missing tag from network/authentication failure.
$releases = @(Invoke-Gh @('api', "repos/$repo/releases?per_page=100", '--paginate', '--jq', '.[] | @json') | ForEach-Object { $_ | ConvertFrom-Json })
$existing = @($releases | Where-Object { $_.tag_name -ceq $tag })
if ($existing.Count -gt 1) { throw 'Ambiguous release tag.' }
$marker = "<!-- ttsk-portable:${CommitSha}:$Version -->"
function Assert-TagCommit {
    $ref = Invoke-Gh @('api', "repos/$repo/git/ref/tags/$tag") | ConvertFrom-Json
    $object = $ref.object
    for ($depth=0; $object.type -eq 'tag' -and $depth -lt 8; $depth++) {
        $annotated = Invoke-Gh @('api', "repos/$repo/git/tags/$($object.sha)") | ConvertFrom-Json
        $object = $annotated.object
    }
    if ($object.type -ne 'commit' -or $object.sha -ne $CommitSha) { throw 'Tag resolves to a different commit.' }
}
if ($existing.Count -eq 1) {
    $release = $existing[0]
    if ($release.prerelease) { throw 'Existing release is a prerelease.' }
    if ($release.draft) {
        if ($release.target_commitish -ne $CommitSha -or !$release.body.Contains($marker)) { throw 'Draft is not owned by this commit/version; do not modify it.' }
    } else { Assert-TagCommit }
} else {
    # Check a pre-existing bare tag too; never allow gh to publish a tag pointing elsewhere.
    $refs = @(Invoke-Gh @('api', "repos/$repo/git/matching-refs/tags/$tag") | ConvertFrom-Json)
    if (@($refs | Where-Object { $_.ref -ceq "refs/tags/$tag" }).Count -gt 0) { Assert-TagCommit }
    $notes = Join-Path $PackageDirectory 'release-notes.txt'
    [IO.File]::WriteAllText($notes, $marker, [Text.UTF8Encoding]::new($false))
    Invoke-Gh @('release','create',$tag,'--repo',$repo,'--target',$CommitSha,'--title',"TTSK Dim Plates $tag",'--notes-file',$notes,'--generate-notes','--draft') | Out-Null
    # A draft may have no Git tag yet; lookup-by-tag can return 404 until publication.
    $drafts = @()
    for ($attempt = 0; $attempt -lt 6; $attempt++) {
        $drafts = @(Invoke-Gh @('api', "repos/$repo/releases?per_page=100&refresh=$([guid]::NewGuid().ToString('N'))", '-H', 'Cache-Control: no-cache', '--paginate', '--jq', '.[] | @json') | ForEach-Object { $_ | ConvertFrom-Json } | Where-Object { $_.tag_name -ceq $tag })
        if ($drafts.Count -eq 1) { break }
        Start-Sleep -Seconds 2
    }
    if ($drafts.Count -ne 1 -or !$drafts[0].draft -or $drafts[0].target_commitish -ne $CommitSha -or !$drafts[0].body.Contains($marker)) { throw 'Created draft could not be verified by release listing.' }
    $release = $drafts[0]
}
$verifyDir = Join-Path $PackageDirectory ('remote-verify-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $verifyDir | Out-Null
foreach ($assetName in @($zipName,$shaName)) {
    $matching = @($release.assets | Where-Object { $_.name -ceq $assetName })
    if ($matching.Count -eq 0) {
        if (!$release.draft) { throw "Public release missing $assetName; never overwrite public assets." }
        Invoke-Gh @('release','upload',$tag,(Join-Path $PackageDirectory $assetName),'--repo',$repo) | Out-Null
    } elseif ($matching.Count -ne 1 -or $matching[0].state -ne 'uploaded') { throw 'Invalid/duplicate asset state.' }
    Invoke-Gh @('release','download',$tag,'--repo',$repo,'--pattern',$assetName,'--dir',$verifyDir) | Out-Null
}
$remoteZip = Join-Path $verifyDir $zipName
Assert-Checksum $remoteZip (Join-Path $verifyDir $shaName)
$remoteFiles = Get-ZipContents $remoteZip
if ($remoteFiles.Count -ne $localFiles.Count) { throw 'Published runtime inventory differs.' }
foreach ($key in $localFiles.Keys) {
    if (!$remoteFiles.ContainsKey($key) -or $remoteFiles[$key] -ne $localFiles[$key]) { throw "Published content conflicts: $key" }
}
if (!$release.draft) { Write-Host 'Verified existing public release; no changes.'; return }
# Refresh immediately before publishing; an older rerun must not replace latest.
$stable = @(Invoke-Gh @('api', "repos/$repo/releases?per_page=100", '--paginate', '--jq', '.[] | @json') | ForEach-Object { $_ | ConvertFrom-Json } | Where-Object { !$_.draft -and !$_.prerelease })
foreach ($item in $stable) {
    if ($item.tag_name -match '^v(\d+\.\d+\.\d+)$' -and [version]$Matches[1] -ge $numericVersion) { throw 'A newer/equal stable release already exists. Keep this older draft unpublished.' }
}
Invoke-Gh @('release','edit',$tag,'--repo',$repo,'--draft=false','--prerelease=false','--latest') | Out-Null
Assert-TagCommit
Write-Host "Published and verified $tag at $CommitSha."
