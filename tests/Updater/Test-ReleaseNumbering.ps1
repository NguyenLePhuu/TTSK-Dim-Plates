Run-Test 'Release numbering' 'Alternate GitHub/Drive with queued Actions, failures and local reservations' {
    $sandbox = Join-Path $testRunDir 'version-numbering'
    $scriptDir = Join-Path $sandbox 'scripts'
    $configDir = Join-Path $sandbox 'TTSK Dim Plates\TTSK Dim Plates'
    New-Item -ItemType Directory -Path $scriptDir,$configDir | Out-Null
    foreach ($name in @('GoogleDrive-ReleaseApi.ps1','Resolve-PortableReleaseVersion.ps1')) {
        Copy-Item -LiteralPath (Join-Path $repoRoot "scripts\$name") -Destination $scriptDir
    }
    [IO.File]::WriteAllText((Join-Path $configDir 'App.config'), '<configuration><appSettings><add key="GoogleDriveManifestId" value="index"/><add key="GoogleDriveApiKey" value="fake_key"/></appSettings></configuration>')
    [IO.File]::WriteAllText((Join-Path $sandbox 'release-version-prefix.txt'), '1.0')
    $resolver = Join-Path $scriptDir 'Resolve-PortableReleaseVersion.ps1'
    $ledger = Join-Path $sandbox 'version-reservation.json'
    $global:numberGithub='1.0.9'; $global:numberDrive='1.0.10'; $global:numberQueued=$null
    $global:numberBody=''; $global:numberOffline=$false; $global:numberDriveOffline=$false
    function Invoke-RestMethod {
        param($Uri,$Method,$Headers,$TimeoutSec,$MaximumRedirection,$ErrorAction)
        if ($global:numberOffline) { throw 'Simulated network failure' }
        if ($Uri -like '*/releases/latest') { return @{tag_name=('v'+$global:numberGithub);body=$global:numberBody} }
        if ($Uri -like '*/contents/*') {
            $json = if ($global:numberQueued) { @{version=$global:numberQueued} | ConvertTo-Json } else { '{"notes":"legacy"}' }
            return @{encoding='base64';content=[Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($json))}
        }
        if ($global:numberDriveOffline) { throw 'Drive read failed' }
        return [pscustomobject]@{schemaVersion=1;product='TTSK Dim Plates';version=$global:numberDrive;notes='';zipFileId='zip';sha256FileId='sha';zipSizeBytes=1024;sha256SizeBytes=96}
    }
    Assert-Equal '1.0.11' (& $resolver -Reserve -ReservationPath $ledger) 'First request on GitHub reserves next number'
    # GitHub upload is still queued: public latest has not changed.
    $global:numberQueued='1.0.11'
    Assert-Equal '1.0.12' (& $resolver -Reserve -ReservationPath $ledger) 'Immediate next request on Drive gets a different number'
    $global:numberDrive='1.0.12'
    Assert-Equal '1.0.11' (& $resolver -RequestedVersion '1.0.11' -ReservationPath $ledger) 'Delayed Actions keeps its original reservation'
    $global:numberGithub='1.0.11'
    Assert-Equal '1.0.13' (& $resolver -Reserve -ReservationPath $ledger) 'Third request exceeds both public versions'
    $global:numberOffline=$true
    $failed=$false
    try { $null = & $resolver -Reserve -ReservationPath $ledger } catch { $failed=$true }
    Assert-True $failed 'Do not allocate if a source cannot be read'
    Assert-Equal '1.0.13' ((Get-Content -LiteralPath $ledger -Raw | ConvertFrom-Json).version) 'Failure does not corrupt reservation'
    $global:numberOffline=$false
    Assert-Equal '1.0.14' (& $resolver -Reserve -ReservationPath $ledger) 'An allocated number is not reused after failed upload'
    $global:numberDriveOffline=$true
    $failed=$false
    try { $null = & $resolver -Reserve -ReservationPath $ledger } catch { $failed=$true }
    Assert-True $failed 'Do not guess a number from GitHub alone if configured Drive fails'
    Assert-Equal '1.0.14' ((Get-Content -LiteralPath $ledger -Raw | ConvertFrom-Json).version) 'Partial source failure preserves reservation'
    $global:numberDriveOffline=$false
    $lock=[IO.File]::Open(($ledger+'.lock'),'OpenOrCreate','ReadWrite','None')
    try {
        $failed=$false
        try { $null = & $resolver -Reserve -ReservationPath $ledger } catch { $failed=$true }
        Assert-True $failed 'Concurrent allocator cannot enter the same reservation'
    } finally { $lock.Dispose() }
    $global:numberQueued='1.0.20'
    Assert-Equal '1.0.21' (& $resolver -ReservationPath (Join-Path $sandbox 'fresh-machine.json')) 'A fresh checkout sees queued remote requests'
    foreach ($version in @('1.0.10','1.0.11','1.0.12','')) {
        $failed=$false
        try { $null = & $resolver -RequestedVersion $version -ReservationPath $ledger } catch { $failed=$true }
        Assert-True $failed "Block stale/duplicate requested version $version"
    }
    $commit='a'*40
    $global:numberBody="<!-- ttsk-portable:${commit}:1.0.11 -->"
    Assert-Equal '1.0.11' (& $resolver -RequestedVersion '1.0.11' -CommitSha $commit -ReservationPath $ledger) 'Rerun of same published commit reuses immutable release'
    [IO.File]::WriteAllText($ledger, '{"version":"invalid"}')
    $failed=$false
    try { $null = & $resolver -Reserve -ReservationPath $ledger } catch { $failed=$true }
    Assert-True $failed 'Damaged high-water mark fails closed'
}
