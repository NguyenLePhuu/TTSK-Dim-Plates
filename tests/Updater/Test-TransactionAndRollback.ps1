# Test Transaction, Fault Injection, Backup and Rollback
Run-Test "Nhom 7 & 8: Transaction & Rollback" "Invalid package is rejected before mutation; original files unchanged" {
    $targetDir = Join-Path $testRunDir "tx_rollback_target"
    New-Item -ItemType Directory -Path $targetDir -Force | Out-Null

    $file1 = Join-Path $targetDir "file1.txt"
    $file2 = Join-Path $targetDir "file2.txt"
    $userCfg = Join-Path $targetDir "theme.cfg"

    [System.IO.File]::WriteAllText($file1, "Old content 1", [System.Text.Encoding]::UTF8)
    [System.IO.File]::WriteAllText($file2, "Old content 2", [System.Text.Encoding]::UTF8)
    [System.IO.File]::WriteAllText($userCfg, "Dark=1", [System.Text.Encoding]::UTF8)

    $hash1Before = Get-Sha256File $file1
    $hash2Before = Get-Sha256File $file2
    $cfgHashBefore = Get-Sha256File $userCfg

    $stagingDir = Join-Path $testRunDir "tx_rollback_staging"
    New-Item -ItemType Directory -Path $stagingDir -Force | Out-Null

    [System.IO.File]::WriteAllText((Join-Path $stagingDir "file1.txt"), "New content 1", [System.Text.Encoding]::UTF8)
    [System.IO.File]::WriteAllText((Join-Path $stagingDir "file2.txt"), "New content 2", [System.Text.Encoding]::UTF8)
    [System.IO.File]::WriteAllText((Join-Path $stagingDir "new_file3.txt"), "Newly added file", [System.Text.Encoding]::UTF8)

    $meta = New-Object TTSK_AutoDim_Plates.Updater.ReleaseMetadata
    $meta.schemaVersion = 1
    $meta.product = "TTSK Dim Plates"
    $meta.repository = "NguyenLePhuu/TTSK-Dim-Plates"
    $meta.version = "1.0.5"
    $meta.tag = "v1.0.5"
    $meta.commit = "a1b2c3d4e5f60718293041526374859607182930"
    $meta.managedFiles.Add("file1.txt")
    $meta.managedFiles.Add("file2.txt")
    $meta.managedFiles.Add("new_file3.txt")
    $meta.SaveToFile((Join-Path $stagingDir "release.json"))

    $checksums = New-Object 'System.Collections.Generic.Dictionary[string, string]' ([System.StringComparer]::OrdinalIgnoreCase)
    $checksums["file1.txt"] = Get-Sha256File (Join-Path $stagingDir "file1.txt")
    $checksums["file2.txt"] = Get-Sha256File (Join-Path $stagingDir "file2.txt")
    $checksums["new_file3.txt"] = "0000000000000000000000000000000000000000000000000000000000000000"
    $checksums["release.json"] = Get-Sha256File (Join-Path $stagingDir "release.json")
    [TTSK_AutoDim_Plates.Updater.UpdateChecksumManifest]::WriteChecksums((Join-Path $stagingDir "SHA256.csv"), $checksums)

    $tx = New-Object TTSK_AutoDim_Plates.Updater.UpdateTransaction($targetDir, $stagingDir, "fault_session_1", $meta)
    $threwExpected = $false
    try {
        $tx.Execute()
    } catch {
        $threwExpected = $true
    }
    Assert-True $threwExpected "Transaction phai nem loi khi hash verify khong khop"

    Assert-True (Test-Path -LiteralPath $file1) "file1.txt phai con ton tai"
    Assert-True (Test-Path -LiteralPath $file2) "file2.txt phai con ton tai"
    Assert-Equal $hash1Before (Get-Sha256File $file1) "file1.txt phai duoc rollback nguyen ven hash cu"
    Assert-Equal $hash2Before (Get-Sha256File $file2) "file2.txt phai duoc rollback nguyen ven hash cu"
    Assert-Equal $cfgHashBefore (Get-Sha256File $userCfg) "theme.cfg cua user tuyet doi khong bi anh huong"

    $addedTarget = Join-Path $targetDir "new_file3.txt"
    Assert-True (!(Test-Path -LiteralPath $addedTarget)) "File moi them phai bi xoa sach khi rollback"
}

Run-Test "Nhom 7 & 8: Transaction & Rollback" "Khoi phuc tu dong khi worker bi crash giua chung (Crash Recovery qua Journal)" {
    $crashTarget = Join-Path $testRunDir "crash_target"
    New-Item -ItemType Directory -Path $crashTarget -Force | Out-Null

    $origFile = Join-Path $crashTarget "app_core.dll"
    [System.IO.File]::WriteAllText($origFile, "Original core binary", [System.Text.Encoding]::UTF8)
    $origHash = Get-Sha256File $origFile

    # Tao thu muc backup cho crash session
    $pathHash = [TTSK_AutoDim_Plates.Updater.UpdateSecurity]::GetCanonicalPathHash($crashTarget)
    $crashSessionId = "crash_session_99"
    $backupDir = Path-Combine (Path-Combine (Path-Combine ([Environment]::GetFolderPath([Environment+SpecialFolder]::LocalApplicationData)) "TTSK Dim Plates\Updater") $pathHash) "backup\$crashSessionId"
    New-Item -ItemType Directory -Path $backupDir -Force | Out-Null
    [System.IO.File]::WriteAllText((Join-Path $backupDir "app_core.dll"), "Original core binary", [System.Text.Encoding]::UTF8)

    # Gia lap file target bi sua do dang thanh phien ban loi
    [System.IO.File]::WriteAllText($origFile, "Corrupted half-written binary", [System.Text.Encoding]::UTF8)

    # Tao file journal o trang thai Replacing
    $journalPath = [TTSK_AutoDim_Plates.Updater.UpdateJournal]::GetJournalPath($crashTarget)
    $journal = New-Object TTSK_AutoDim_Plates.Updater.UpdateJournal
    $journal.SessionGuid = $crashSessionId
    $journal.TargetDirectory = $crashTarget
    $journal.BackupDirectory = $backupDir
    $journal.TargetVersion = "1.0.9"
    $journal.State = [TTSK_AutoDim_Plates.Updater.UpdateTransactionState]::Replacing
    $journal.CreatedUtc = [DateTime]::UtcNow.ToString("o")

    $rec = New-Object TTSK_AutoDim_Plates.Updater.UpdateFileRecord
    $rec.RelativePath = "app_core.dll"
    $rec.ActionType = [TTSK_AutoDim_Plates.Updater.UpdateFileActionType]::Replace
    $rec.ExistedBefore = $true
    $rec.BackupHash = $origHash
    $journal.FileActions.Add($rec)
    $journal.SaveAtomic($journalPath)

    # Thuc hien Recovery
    $exitCode = [TTSK_AutoDim_Plates.Updater.UpdateWorker]::ExecuteRecovery($crashTarget)
    Assert-Equal 0 $exitCode "ExecuteRecovery phai thanh cong voi exit code 0"

    # Kiem tra file da duoc khoi phuc ve hash ban dau
    Assert-Equal $origHash (Get-Sha256File $origFile) "File target phai duoc khoi phuc nguyen ven tu backup sau crash"

    # Kiem tra trang thai journal da chuyen sang RolledBack
    Assert-True (!(Test-Path -LiteralPath $journalPath)) "Completed recovery journal must be cleared"
}
