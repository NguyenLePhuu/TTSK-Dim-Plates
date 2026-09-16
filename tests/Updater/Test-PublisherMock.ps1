Run-Test 'Publisher mock (no network)' 'Public rerun verified, conflicting draft untouched, old draft cannot become latest, upload failure stays draft' {
    $global:ttskMockCommit=(git rev-parse HEAD).Trim()
    $global:ttskMockPackage=Join-Path $testRunDir 'inventory_pkg'
    $global:ttskMockCalls=[Collections.Generic.List[string]]::new()
    $global:ttskMockMode='public'
    $global:ttskMockCreated=$false
    $publisherSandbox = Join-Path $testRunDir 'github-publisher'
    $publisherScripts = Join-Path $publisherSandbox 'scripts'
    $publisherConfig = Join-Path $publisherSandbox 'TTSK Dim Plates\TTSK Dim Plates'
    New-Item -ItemType Directory -Path $publisherScripts,$publisherConfig | Out-Null
    foreach ($name in @('Publish-GitHubPortableRelease.ps1','Resolve-PortableReleaseVersion.ps1','GoogleDrive-ReleaseApi.ps1')) {
        Copy-Item -LiteralPath (Join-Path $repoRoot "scripts\$name") -Destination $publisherScripts
    }
    [IO.File]::WriteAllText((Join-Path $publisherConfig 'App.config'), '<configuration><appSettings><add key="GoogleDriveManifestId" value="index"/><add key="GoogleDriveApiKey" value="fake_key"/></appSettings></configuration>')
    [IO.File]::WriteAllText((Join-Path $publisherSandbox 'release-version-prefix.txt'), '1.0')
    [IO.File]::WriteAllText((Join-Path $publisherSandbox 'release-request.json'), '{"notes":"fixture notes"}')
    function Invoke-RestMethod {
        param($Uri,$Method,$Headers,$TimeoutSec,$MaximumRedirection,$ErrorAction)
        if ($Uri -like 'https://api.github.com/*') { return @{tag_name='v1.0.49';body=''} }
        if ($global:ttskMockMode -eq 'driveoffline') { throw 'Drive offline' }
        $driveVersion = if ($global:ttskMockMode -eq 'driveconflict') {'1.0.50'} else {'1.0.49'}
        return [pscustomobject]@{schemaVersion=1;product='TTSK Dim Plates';version=$driveVersion;notes='';zipFileId='zip';sha256FileId='sha';zipSizeBytes=1024;sha256SizeBytes=96}
    }
    function gh {
        $a=@($args); $global:LASTEXITCODE=0
        $global:ttskMockCalls.Add(($a -join ' '))
        $assets=@(@{name='TTSK-Dim-Plates-Portable.zip';state='uploaded'},@{name='TTSK-Dim-Plates-Portable.zip.sha256';state='uploaded'})
        $body="<!-- ttsk-portable:$($global:ttskMockCommit):1.0.50 -->"
        if ($global:ttskMockMode -eq 'conflict') { $body='someone else draft' }
        $rel=@{tag_name='v1.0.50';draft=($global:ttskMockMode -ne 'public');prerelease=$false;target_commitish=$global:ttskMockCommit;body=$body;assets=$assets}
        if ($a[0] -eq 'api' -and $a[1] -match '/releases\?') {
            if ($global:ttskMockMode -ne 'uploadfail') { $rel | ConvertTo-Json -Compress -Depth 5 }
            if ($global:ttskMockMode -eq 'old') { @{tag_name='v1.0.99';draft=$false;prerelease=$false;target_commitish=$global:ttskMockCommit;body='';assets=@()} | ConvertTo-Json -Compress }
            return
        }
        if ($a[0] -eq 'api' -and $a[1] -match '/git/ref/tags/') { @{object=@{type='commit';sha=$global:ttskMockCommit}} | ConvertTo-Json -Compress; return }
        if ($a[0] -eq 'api' -and $a[1] -match '/git/matching-refs/') { '[]'; return }
        if ($a[0] -eq 'api' -and $a[1] -match '/releases/tags/') { $rel.assets=@(); $rel | ConvertTo-Json -Compress -Depth 5; return }
        if ($a[0] -eq 'release' -and $a[1] -eq 'create') { $global:ttskMockCreated=$true; return }
        if ($a[0] -eq 'release' -and $a[1] -eq 'upload') { $global:LASTEXITCODE=1; return }
        if ($a[0] -eq 'release' -and $a[1] -eq 'edit') { return }
        if ($a[0] -eq 'release' -and $a[1] -eq 'download') {
            $name=$a[[Array]::IndexOf($a,'--pattern')+1]; $dest=$a[[Array]::IndexOf($a,'--dir')+1]
            Copy-Item -LiteralPath (Join-Path $global:ttskMockPackage $name) -Destination $dest; return
        }
        throw ('Unexpected mocked gh invocation: ' + ($a -join ' '))
    }
    $publisher=Join-Path $publisherScripts 'Publish-GitHubPortableRelease.ps1'
    & $publisher -Version '1.0.50' -CommitSha $global:ttskMockCommit -PackageDirectory $global:ttskMockPackage
    Assert-True (!($global:ttskMockCalls | Where-Object { $_ -match '^release (create|edit|upload|delete)' })) 'Public no-op must not mutate release'
    foreach ($mode in @('conflict','old','uploadfail','driveconflict','driveoffline')) {
        $global:ttskMockMode=$mode; $global:ttskMockCalls.Clear(); $failed=$false
        try { & $publisher -Version '1.0.50' -CommitSha $global:ttskMockCommit -PackageDirectory $global:ttskMockPackage } catch { $failed=$true }
        Assert-True $failed "Scenario $mode must fail clearly"
        Assert-True (!($global:ttskMockCalls | Where-Object { $_ -match '^release (edit|delete)' })) "Scenario $mode must not publish/delete"
    }
    $global:ttskMockMode='ready'; $global:ttskMockCalls.Clear()
    & $publisher -Version '1.0.50' -CommitSha $global:ttskMockCommit -PackageDirectory $global:ttskMockPackage
    Assert-True ([bool]($global:ttskMockCalls | Where-Object { $_ -match '^release edit' })) 'Verified draft publishes when version is free on both sources'
}
