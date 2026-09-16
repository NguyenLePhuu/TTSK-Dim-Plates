Run-Test 'Dual sources' 'Parse Drive manifest, enforce URL boundaries and compare numeric versions' {
    $manifest = @{schemaVersion=1;product='TTSK Dim Plates';version='1.0.10';notes='Drive notes';zipFileId='zip_123';sha256FileId='sha_123';zipSizeBytes=1024;sha256SizeBytes=96}
    $drive = [TTSK_AutoDim_Plates.Updater.UpdateSources]::ParseDriveManifest(($manifest | ConvertTo-Json), 'public_key')
    $github = [TTSK_AutoDim_Plates.Updater.GitHubReleaseInfo]::new()
    $github.Version = [TTSK_AutoDim_Plates.Updater.UpdateVersion]::Parse('1.0.9')
    Assert-Equal 'GoogleDrive' ([TTSK_AutoDim_Plates.Updater.UpdateSources]::SelectNewest($github,$drive).Source) '1.0.10 beats 1.0.9'
    $github.Version = $drive.Version
    Assert-Equal 'GitHub' ([TTSK_AutoDim_Plates.Updater.UpdateSources]::SelectNewest($github,$drive).Source) 'Equal versions prefer GitHub'
    Assert-True ([TTSK_AutoDim_Plates.Updater.UpdateSources]::IsDriveMediaUrl($drive.ZipDownloadUrl)) 'Generated media URL accepted'
    Assert-True (!$drive.ZipDownloadUrl.Contains('public_key') -and !$drive.ZipDownloadUrl.Contains('key=')) 'No API key in client URL'
    foreach ($url in @('https://drive.usercontent.google.com.evil.test/download?id=abc&export=download','http://drive.usercontent.google.com/download?id=abc&export=download','https://user@drive.usercontent.google.com/download?id=abc&export=download','https://drive.usercontent.google.com:444/download?id=abc&export=download','https://drive.usercontent.google.com/download?id=abc&export=download&key=secret','https://drive.usercontent.google.com/download?id=abc&export=download#fragment')) {
        Assert-True (![TTSK_AutoDim_Plates.Updater.UpdateSources]::IsDriveMediaUrl($url)) 'Reject unsafe public download URL'
    }
    foreach ($url in @('http://www.googleapis.com/drive/v3/files/abc?alt=media&key=x', 'https://www.googleapis.com.evil.test/drive/v3/files/abc?alt=media&key=x', 'https://www.googleapis.com:444/drive/v3/files/abc?alt=media&key=x', 'https://user@www.googleapis.com/drive/v3/files/abc?alt=media&key=x', 'https://www.googleapis.com/drive/v3/files/abc?alt=media&key=x&other=y', 'https://www.googleapis.com/drive/v3/files/abc?alt=media&key=x#fragment')) {
        Assert-True (![TTSK_AutoDim_Plates.Updater.UpdateSources]::IsDriveMediaUrl($url)) "Reject $url"
    }
    foreach ($field in @('version','product','zipFileId','zipSizeBytes','sha256SizeBytes')) {
        $bad = $manifest.Clone()
        $bad[$field] = switch ($field) { 'version' {'v1.0.10'} 'product' {'Other'} 'zipFileId' {'../evil'} 'zipSizeBytes' {209715201} 'sha256SizeBytes' {0} }
        $failed=$false
        try { $null = [TTSK_AutoDim_Plates.Updater.UpdateSources]::ParseDriveManifest(($bad | ConvertTo-Json), 'key') } catch { $failed=$true }
        Assert-True $failed "Reject malformed $field"
    }
}

Run-Test 'Dual sources' 'Both providers queried; independent failures, total failure and cancellation' {
    $fixtureAssembly = Join-Path $repoRoot '.codex-artifacts\test-build\TTSK Dim Plates.exe'
    $fixtureSource = Join-Path $testRunDir 'DualSourceFixture.cs'
    $fixtureDll = Join-Path $testRunDir 'DualSourceFixture.dll'
    $code = @'
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using TTSK_AutoDim_Plates.Updater;
public static class DualSourceFixture {
    public static void Run() {
        for (int scenario = 0; scenario < 4; scenario++) {
            int calls = 0, warnings = 0;
            int mode = scenario;
            Func<Task<GitHubReleaseInfo>> github = () => {
                calls++;
                if (mode == 1 || mode == 3) throw new IOException("offline");
                return Task.FromResult(new GitHubReleaseInfo { Version = new UpdateVersion(1,0,9) });
            };
            Func<Task<GitHubReleaseInfo>> drive = () => {
                calls++;
                if (mode == 2 || mode == 3) throw new IOException("offline");
                return Task.FromResult(new GitHubReleaseInfo { Source = "GoogleDrive", Version = new UpdateVersion(1,0,10) });
            };
            try {
                var result = UpdateSources.GetLatestAsync(github, drive, s => warnings++, CancellationToken.None).GetAwaiter().GetResult();
                if (mode == 3 || result.Source != (mode == 2 ? "GitHub" : "GoogleDrive")) throw new Exception("Wrong selection");
            } catch (IOException) { if (mode != 3) throw; }
            if (calls != 2 || warnings != (mode == 0 ? 0 : mode == 3 ? 2 : 1)) throw new Exception("Providers/warnings mismatch");
        }
        var cts = new CancellationTokenSource(); cts.Cancel();
        bool canceled = false;
        try { UpdateSources.GetLatestAsync(() => Task.FromResult<GitHubReleaseInfo>(null), () => Task.FromResult<GitHubReleaseInfo>(null), s => {}, cts.Token).GetAwaiter().GetResult(); }
        catch (OperationCanceledException) { canceled = true; }
        if (!canceled) throw new Exception("Cancellation was swallowed");
    }
}


'@
    [IO.File]::WriteAllText($fixtureSource, $code)
    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    $vsRoot = & $vswhere -latest -property installationPath
    $csc = Join-Path $vsRoot 'MSBuild\Current\Bin\Roslyn\csc.exe'
    & $csc /nologo /target:library "/out:$fixtureDll" "/r:$fixtureAssembly" /r:System.Core.dll $fixtureSource
    if ($LASTEXITCODE -ne 0) { throw 'Dual source fixture compilation failed.' }
    [Reflection.Assembly]::LoadFrom($fixtureDll) | Out-Null
    [DualSourceFixture]::Run()
}

Run-Test 'Drive publisher (no network)' 'Public download verified before index commit; failures leave old index intact' {
    $sandbox = Join-Path $testRunDir 'drive-publisher'
    $scriptDir = Join-Path $sandbox 'scripts'
    $configDir = Join-Path $sandbox 'TTSK Dim Plates\TTSK Dim Plates'
    New-Item -ItemType Directory -Path $scriptDir,$configDir,(Join-Path $sandbox 'update') | Out-Null
    foreach ($name in @('GoogleDrive-ReleaseApi.ps1','Publish-GoogleDrivePortableRelease.ps1','Resolve-PortableReleaseVersion.ps1')) {
        Copy-Item -LiteralPath (Join-Path $repoRoot "scripts\$name") -Destination $scriptDir
    }
    [IO.File]::WriteAllText((Join-Path $configDir 'App.config'), '<configuration><appSettings><add key="GoogleDriveFolderId" value="folder"/><add key="GoogleDriveManifestId" value="index"/><add key="GoogleDriveApiKey" value="fake_key"/></appSettings></configuration>')
    [IO.File]::WriteAllText((Join-Path $sandbox 'update/release-version-prefix.txt'), '1.0')
    $oldToken = $env:TTSK_GOOGLE_DRIVE_ACCESS_TOKEN
    $env:TTSK_GOOGLE_DRIVE_ACCESS_TOKEN = 'fake_test_token'
    function Invoke-WebRequest {
        param($Uri,$Method,$Body,$Headers,$TimeoutSec,$MaximumRedirection,$ContentType,$ErrorAction,$OutFile,[switch]$UseBasicParsing)
        if ($Uri -like 'https://drive.usercontent.google.com/*') {
            $value = Invoke-RestMethod -Uri $Uri -Method $Method -Headers $Headers -OutFile $OutFile
            return @{ Content = ($value | ConvertTo-Json -Compress) }
        }
        if ($Method -eq 'PATCH') { $id='index' } else { $global:driveMockCounter++; $id='asset_' + $global:driveMockCounter }
        return @{Headers=@{Location="https://www.googleapis.com/upload/drive/v3/files/${id}?session=test"}}
    }
    function Invoke-RestMethod {
        param($Uri,$Method,$Body,$InFile,$OutFile,$Headers,$TimeoutSec,$MaximumRedirection,$ContentType,$ErrorAction)
        if ($Uri -like 'https://api.github.com/*/contents/*') { return @{encoding='base64';content=[Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes('{"notes":"legacy request"}'))} }
        if ($Uri -like 'https://api.github.com/*') { return @{tag_name='v1.0.9';body=''} }
        if ($Method -eq 'GET') { Assert-True (!$Headers.ContainsKey('Authorization')) 'Public Drive reads must not require user login' }
        $id=([Uri]$Uri).Segments[-1]
        if ($Uri -match '[?]id=([^&]+)') { $id=$Matches[1] }
        if ($Method -eq 'PUT') {
            if ($id -eq 'index') { $global:driveIndexWrites++; $global:driveIndex = Get-Content -LiteralPath $InFile -Raw | ConvertFrom-Json }
            else { $global:driveAssets[$id]=$InFile }
            return @{id=$id}
        }
        if ($id -eq 'index') {
            $global:driveIndexReads++
            if ($global:driveMockMode -eq 'changed' -and $global:driveIndexReads -gt 1) { $global:driveIndex.version='1.0.60' }
            return $global:driveIndex
        }
        if ($global:driveMockMode -eq 'denied') { throw '403 public read denied' }
        Copy-Item -LiteralPath $global:driveAssets[$id] -Destination $OutFile
        if ($global:driveMockMode -eq 'corrupt') { [IO.File]::AppendAllText($OutFile, 'corrupt') }
    }
    try {
        foreach ($mode in @('ok','corrupt','denied','newer','changed')) {
            $global:driveMockMode=$mode; $global:driveMockCounter=0; $global:driveIndexWrites=0; $global:driveIndexReads=0; $global:driveAssets=@{}
            $global:driveIndex=[pscustomobject]@{schemaVersion=1;product='TTSK Dim Plates';version='1.0.10';notes='old';zipFileId='oldzip';sha256FileId='oldsha';zipSizeBytes=1024;sha256SizeBytes=96}
            if ($mode -eq 'newer') { $global:driveIndex.version='1.0.99' }
            $failed=$false
            try { & (Join-Path $scriptDir 'Publish-GoogleDrivePortableRelease.ps1') -Version '1.0.50' -PackageDirectory (Join-Path $testRunDir 'inventory_pkg') -ReleaseNotes 'test' } catch { $failed=$true; if ($mode -eq 'ok') { throw } }
            Assert-Equal ($mode -ne 'ok') $failed "Expected result for $mode"
            Assert-Equal $(if ($mode -eq 'ok') {1} else {0}) $global:driveIndexWrites "Index writes for $mode"
            if ($mode -eq 'ok') {
                Assert-Equal '1.0.50' $global:driveIndex.version 'Published index version'
                $resolved = & (Join-Path $scriptDir 'Resolve-PortableReleaseVersion.ps1')
                Assert-Equal '1.0.51' $resolved 'GitHub next version must exceed newer Drive release'
            }
        }
    } finally { $env:TTSK_GOOGLE_DRIVE_ACCESS_TOKEN=$oldToken }
}

Run-Test 'Drive credentials (no network)' 'Refresh credentials encrypted for Windows user; access token reused before expiry' {
    . (Join-Path $repoRoot 'scripts\GoogleDrive-ReleaseApi.ps1')
    $credentialFile = Join-Path $testRunDir 'oauth-fixture\google-oauth.xml'
    Save-DriveCredential -ClientId 'test-client' -ClientSecret 'fixture-client-secret' -RefreshToken 'fixture-refresh-token' -Path $credentialFile
    $stored = Get-Content -LiteralPath $credentialFile -Raw
    Assert-True (!$stored.Contains('fixture-client-secret') -and !$stored.Contains('fixture-refresh-token')) 'Secrets not stored in plaintext'
    $oldToken = $env:TTSK_GOOGLE_DRIVE_ACCESS_TOKEN
    $env:TTSK_GOOGLE_DRIVE_ACCESS_TOKEN = $null
    $global:driveRefreshCalls=0
    function Invoke-RestMethod {
        param($Uri,$Method,$ContentType,$Body,$TimeoutSec,$ErrorAction)
        Assert-Equal 'https://oauth2.googleapis.com/token' $Uri 'Refresh only targets Google OAuth endpoint'
        Assert-Equal 'fixture-refresh-token' $Body.refresh_token 'DPAPI decrypted refresh token'
        Assert-Equal 'fixture-client-secret' $Body.client_secret 'DPAPI decrypted client secret'
        $global:driveRefreshCalls++
        return @{access_token='fixture-access-token';expires_in=3600}
    }
    try {
        Assert-Equal 'fixture-access-token' (Get-DriveAccessToken -CredentialPath $credentialFile) 'First refresh'
        Assert-Equal 'fixture-access-token' (Get-DriveAccessToken -CredentialPath $credentialFile) 'Cached token'
        Assert-Equal 1 $global:driveRefreshCalls 'Only one token request'
    } finally { $env:TTSK_GOOGLE_DRIVE_ACCESS_TOKEN=$oldToken }
}

Run-Test 'Update UI' 'Download errors stay inline; retry works without provider popups in both themes' {
    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    $vsRoot = & $vswhere -latest -property installationPath
    $csc = Join-Path $vsRoot 'MSBuild\Current\Bin\Roslyn\csc.exe'
    $assembly = Join-Path $repoRoot '.codex-artifacts\test-build\TTSK Dim Plates.exe'
    $fixture = Join-Path $repoRoot '.codex-artifacts\test-build\UpdateDialogFixture.exe'
    & $csc /nologo /target:exe /platform:x64 "/out:$fixture" "/r:$assembly" /r:System.Core.dll /r:System.Windows.Forms.dll /r:System.Drawing.dll (Join-Path $repoRoot 'tests\Updater\UpdateDialogFixture.cs')
    if ($LASTEXITCODE -ne 0) { throw 'Update dialog fixture compilation failed.' }
    $imageDir = Join-Path $repoRoot '.codex-artifacts\updater-ui-review'
    $start = New-Object Diagnostics.ProcessStartInfo
    $start.FileName = $fixture
    $start.Arguments = '"' + $imageDir + '"'
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    $process = [Diagnostics.Process]::Start($start)
    try {
        if (!$process.WaitForExit(15000)) { $process.Kill(); throw 'Dialog test blocked, possibly by an unexpected modal popup.' }
        $output = $process.StandardOutput.ReadToEnd() + $process.StandardError.ReadToEnd()
        Assert-Equal 0 $process.ExitCode $output
    } finally { $process.Dispose() }
}

Run-Test 'Dual sources' 'Recheck cannot silently replace selected version with an older release' {
    $current = [TTSK_AutoDim_Plates.Updater.UpdateVersion]::Parse('1.0.1')
    $selected = [TTSK_AutoDim_Plates.Updater.GitHubReleaseInfo]::new()
    $selected.Source = 'GoogleDrive'
    $selected.Version = [TTSK_AutoDim_Plates.Updater.UpdateVersion]::Parse('1.0.10')
    $latest = [TTSK_AutoDim_Plates.Updater.GitHubReleaseInfo]::new()
    foreach ($version in @('1.0.9','1.0.1')) {
        $latest.Version = [TTSK_AutoDim_Plates.Updater.UpdateVersion]::Parse($version)
        $failed = $false
        try { [TTSK_AutoDim_Plates.Updater.UpdateSources]::ValidateDownloadSelection($selected,$latest,$current) } catch { $failed = $true }
        Assert-True $failed "Do not silently select $version after offering 1.0.10"
        Assert-Equal '1.0.10' $selected.Version.ToString() 'Keep the selected version unchanged'
    }
    foreach ($version in @('1.0.10','1.0.11')) {
        $latest.Version = [TTSK_AutoDim_Plates.Updater.UpdateVersion]::Parse($version)
        [TTSK_AutoDim_Plates.Updater.UpdateSources]::ValidateDownloadSelection($selected,$latest,$current)
    }
    $failed = $false
    try { [TTSK_AutoDim_Plates.Updater.UpdateSources]::ValidateDownloadSelection($selected,$null,$current) } catch { $failed = $true }
    Assert-True $failed 'No available release must not use stale download URLs'
}
