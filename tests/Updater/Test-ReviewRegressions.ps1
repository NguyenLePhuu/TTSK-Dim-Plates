Run-Test 'Review regressions' 'Reject path normalization bypass, aliases and invalid versions' {
    foreach ($p in @('/evil.dll','test.dll ','NUL.foo.dll','x//y.dll','x:ads','x,evil.dll')) {
        Assert-True (![TTSK_AutoDim_Plates.Updater.UpdateSecurity]::IsSafeRelativePath($p)) "Reject $p"
    }
    foreach ($v in @('1.+2.3','01.2.3','1.02.3','1.2.03')) {
        $parsed = $null
        Assert-True (![TTSK_AutoDim_Plates.Updater.UpdateVersion]::TryParse($v,[ref]$parsed)) "Reject $v"
    }
}

Run-Test 'Review regressions' 'Exact selected tag and asset URL required' {
    $failed = $false
    try { [TTSK_AutoDim_Plates.Updater.GitHubReleaseService]::ValidateExactAssetUrl('https://github.com/NguyenLePhuu/TTSK-Dim-Plates/releases/download/v1.0.2/TTSK-Dim-Plates-Portable.zip','v1.0.3','TTSK-Dim-Plates-Portable.zip') }
    catch { $failed = $true }
    Assert-True $failed 'Wrong tag cannot execute a different release'
}

Run-Test 'Review regressions' 'Mid-transaction failure really restores changed runtime and preserves unrelated temp file' {
    $target = Join-Path $testRunDir 'midfailure-target'
    $stage = Join-Path $testRunDir 'midfailure-stage'
    New-Item -ItemType Directory -Path $target,$stage | Out-Null
    [IO.File]::WriteAllText((Join-Path $target 'a.dll'), 'original')
    $oldHash = Get-Sha256File (Join-Path $target 'a.dll')
    [IO.File]::WriteAllText((Join-Path $target 'custom.new_update'), 'user file')
    [IO.File]::WriteAllText((Join-Path $stage 'a.dll'), 'updated')
    [IO.File]::WriteAllText((Join-Path $stage 'b.dll'), 'new')
    # Existing directory collides with the SECOND runtime file; the first replacement must be rolled back.
    New-Item -ItemType Directory -Path (Join-Path $target 'b.dll') | Out-Null
    $meta = New-Object TTSK_AutoDim_Plates.Updater.ReleaseMetadata
    $meta.version='1.0.90'; $meta.tag='v1.0.90'; $meta.commit=('a' * 40)
    $meta.managedFiles.Add('a.dll'); $meta.managedFiles.Add('b.dll')
    Complete-TestPackage $stage $meta
    $tx = New-Object TTSK_AutoDim_Plates.Updater.UpdateTransaction($target,$stage,'midfailure',$meta)
    $failed=$false
    try { $tx.Execute() } catch { $failed=$true }
    Assert-True $failed 'Mutation must fail at file/directory collision'
    Assert-Equal $oldHash (Get-Sha256File (Join-Path $target 'a.dll')) 'Earlier replaced file restored'
    Assert-Equal 'user file' ([IO.File]::ReadAllText((Join-Path $target 'custom.new_update'))) 'No wildcard deletion of user files'
    $journal = [TTSK_AutoDim_Plates.Updater.UpdateJournal]::LoadFromFile([TTSK_AutoDim_Plates.Updater.UpdateJournal]::GetJournalPath($target))
    Assert-Equal ([TTSK_AutoDim_Plates.Updater.UpdateTransactionState]::RolledBack) $journal.State 'Verified rollback'
    Assert-True (@($journal.FileActions | Where-Object MutationStarted).Count -gt 0) 'Failure actually reached mutation'
}

Run-Test 'Review regressions' 'Missing backup must fail recovery and retain journal, never claim success' {
    $target=Join-Path $testRunDir 'missingbackup-target'; New-Item -ItemType Directory -Path $target | Out-Null
    [IO.File]::WriteAllText((Join-Path $target 'a.dll'),'broken')
    $journalPath=[TTSK_AutoDim_Plates.Updater.UpdateJournal]::GetJournalPath($target)
    $j=New-Object TTSK_AutoDim_Plates.Updater.UpdateJournal
    $j.TargetDirectory=$target; $j.SessionGuid='missingbackup'; $j.BackupDirectory=Join-Path (Split-Path $journalPath) 'backup\missingbackup'
    $j.State=[TTSK_AutoDim_Plates.Updater.UpdateTransactionState]::Replacing
    $r=New-Object TTSK_AutoDim_Plates.Updater.UpdateFileRecord
    $r.RelativePath='a.dll'; $r.ExistedBefore=$true; $r.BackupHash=('a'*64); $j.FileActions.Add($r); $j.SaveAtomic($journalPath)
    Assert-Equal 1 ([TTSK_AutoDim_Plates.Updater.UpdateWorker]::ExecuteRecovery($target)) 'Recovery failure exit code'
    Assert-True (Test-Path $journalPath) 'Journal retained'
}

Run-Test 'Review regressions' 'Package extra executable is rejected despite valid listed hashes' {
    $stage=Join-Path $testRunDir 'extra-stage'; New-Item -ItemType Directory -Path $stage | Out-Null
    $meta=New-Object TTSK_AutoDim_Plates.Updater.ReleaseMetadata
    $meta.version='1.0.99'; $meta.tag='v1.0.99'; $meta.commit=('a'*40)
    Complete-TestPackage $stage $meta
    [IO.File]::WriteAllText((Join-Path $stage 'unexpected.exe'),'extra')
    $errorText=$null
    Assert-True (![TTSK_AutoDim_Plates.Updater.UpdateChecksumManifest]::ValidateDirectory($stage,$meta,[ref]$errorText,$true)) 'Reject extra file'
}

Run-Test 'Review regressions' 'Corrupt journal is a recovery blocker, not no-journal success' {
    $target=Join-Path $testRunDir 'corruptjournal-target'; New-Item -ItemType Directory -Path $target | Out-Null
    $path=[TTSK_AutoDim_Plates.Updater.UpdateJournal]::GetJournalPath($target)
    [IO.File]::WriteAllText($path,'{ broken')
    Assert-Equal 1 ([TTSK_AutoDim_Plates.Updater.UpdateWorker]::ExecuteRecovery($target)) 'Corrupt journal blocks startup'
    Assert-True (Test-Path $path) 'Evidence retained'
}

Run-Test 'Process integration' 'Unmodified production entry/worker: READY, clean parent exit, apply, release lock, restart without Tekla' {
    $fixture=Join-Path $testRunDir 'process-fixture'; $target=Join-Path $fixture 'target'; $stage=Join-Path $fixture 'stage'
    New-Item -ItemType Directory -Path $target,$stage | Out-Null
    $vswhere=Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    $vsRoot=& $vswhere -latest -property installationPath
    $compiler=Join-Path $vsRoot 'MSBuild\Current\Bin\Roslyn\csc.exe'
    $app=Join-Path $repoRoot 'TTSK Dim Plates\TTSK Dim Plates'
    $sources=@((Join-Path $app 'Program.cs'),(Join-Path $repoRoot 'tests\Updater\WorkerProcessFixture.cs'))
    $sources += (Join-Path $app 'Properties\Settings.Designer.cs')
    $sources+=@(Get-ChildItem (Join-Path $app 'Updater') -Filter '*.cs' | ForEach-Object FullName)
    $targetExe=Join-Path $target 'TTSK Dim Plates.exe'
    & $compiler /nologo /target:exe /platform:x64 /langversion:7.3 "/out:$targetExe" /r:System.Configuration.dll /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Net.Http.dll /r:System.Web.Extensions.dll /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll @sources
    if ($LASTEXITCODE -ne 0) { throw 'Process fixture compilation failed' }
    Copy-Item -LiteralPath $targetExe -Destination $stage
    $meta=New-Object TTSK_AutoDim_Plates.Updater.ReleaseMetadata
    $meta.version='1.0.91'; $meta.tag='v1.0.91'; $meta.commit=('a'*40)
    [IO.File]::WriteAllText((Join-Path $stage 'TTSK Dim Plates.exe.config'), '<configuration><startup><supportedRuntime version="v4.0" sku=".NETFramework,Version=v4.8" /></startup></configuration>')
    Copy-Item -LiteralPath (Join-Path $stage 'TTSK Dim Plates.exe.config') -Destination $target
    Complete-TestPackage $stage $meta
    $psi=New-Object Diagnostics.ProcessStartInfo
    $psi.FileName=$targetExe; $psi.WorkingDirectory=$target; $psi.UseShellExecute=$false; $psi.CreateNoWindow=$true
    $psi.Arguments='--fixture-parent "' + $stage + '"'
    $process=[Diagnostics.Process]::Start($psi)
    try { Assert-True ($process.WaitForExit(20000)) 'Parent completed handshake'; Assert-Equal 0 $process.ExitCode 'Parent exit success' }
    finally { $process.Dispose() }
    $marker=Join-Path $target 'fixture-restarted.txt'
    $watch=[Diagnostics.Stopwatch]::StartNew()
    while (!(Test-Path -LiteralPath $marker) -and $watch.ElapsedMilliseconds -lt 15000) { Start-Sleep -Milliseconds 100 }
    Assert-True (Test-Path -LiteralPath $marker) 'Worker restarted target after releasing lock'
    Assert-Equal '1.0.91' ([IO.File]::ReadAllText($marker)) 'Restarted executable sees new version'
    $settingMarker=Join-Path $target 'fixture-user-setting.txt'
    while (!(Test-Path $settingMarker) -and $watch.ElapsedMilliseconds -lt 15000) { Start-Sleep -Milliseconds 50 }
    Assert-Equal '175' ([IO.File]::ReadAllText($settingMarker)) 'Real Properties.Settings ManualScaleDenominator preserved across restart'
    Assert-True (!(Test-Path ([TTSK_AutoDim_Plates.Updater.UpdateJournal]::GetJournalPath($target)))) 'Committed journal cleared'
}
