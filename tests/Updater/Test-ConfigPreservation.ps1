# Test User Configuration Preservation & Happy Path E2E
Run-Test "Nhom 9: User Config" "Bao toan 3 file cfg va custom file ngoai manifest" {
    $targetDir = Join-Path $testRunDir "cfg_preservation_target"
    New-Item -ItemType Directory -Path $targetDir -Force | Out-Null

    $themePath = Join-Path $targetDir "theme.cfg"
    $shortcutPath = Join-Path $targetDir "shortcut.cfg"
    $autoSecPath = Join-Path $targetDir "auto_section.cfg"
    $customUserFile = Join-Path $targetDir "my_custom_notes.txt"

    [System.IO.File]::WriteAllText($themePath, "theme=dark`r`n", [System.Text.Encoding]::UTF8)
    [System.IO.File]::WriteAllText($shortcutPath, "key_binds_v2=custom`r`n", [System.Text.Encoding]::UTF8)
    [System.IO.File]::WriteAllText($autoSecPath, "enabled=1`r`n", [System.Text.Encoding]::UTF8)
    [System.IO.File]::WriteAllText($customUserFile, "User private notes`r`n", [System.Text.Encoding]::UTF8)

    $themeHash = Get-Sha256File $themePath
    $shortcutHash = Get-Sha256File $shortcutPath
    $autoSecHash = Get-Sha256File $autoSecPath
    $customHash = Get-Sha256File $customUserFile

    $stagingDir = Join-Path $testRunDir "cfg_staging"
    New-Item -ItemType Directory -Path $stagingDir -Force | Out-Null
    [System.IO.File]::WriteAllText((Join-Path $stagingDir "runtime.dll"), "New DLL content", [System.Text.Encoding]::UTF8)

    $meta = New-Object TTSK_AutoDim_Plates.Updater.ReleaseMetadata
    $meta.schemaVersion = 1
    $meta.product = "TTSK Dim Plates"
    $meta.repository = "NguyenLePhuu/TTSK-Dim-Plates"
    $meta.version = "1.0.8"
    $meta.tag = "v1.0.8"
    $meta.commit = "a1b2c3d4e5f60718293041526374859607182930"
    $meta.managedFiles.Add("runtime.dll")
    $meta.SaveToFile((Join-Path $stagingDir "release.json"))

    $sums = New-Object 'System.Collections.Generic.Dictionary[string, string]' ([System.StringComparer]::OrdinalIgnoreCase)
    $sums["runtime.dll"] = Get-Sha256File (Join-Path $stagingDir "runtime.dll")
    $sums["release.json"] = Get-Sha256File (Join-Path $stagingDir "release.json")
    [TTSK_AutoDim_Plates.Updater.UpdateChecksumManifest]::WriteChecksums((Join-Path $stagingDir "SHA256.csv"), $sums)

    Complete-TestPackage $stagingDir $meta
    $tx = New-Object TTSK_AutoDim_Plates.Updater.UpdateTransaction($targetDir, $stagingDir, "cfg_session", $meta)
    $tx.Execute()

    Assert-Equal $themeHash (Get-Sha256File $themePath) "theme.cfg khong duoc doi du chi 1 byte"
    Assert-Equal $shortcutHash (Get-Sha256File $shortcutPath) "shortcut.cfg khong duoc doi du chi 1 byte"
    Assert-Equal $autoSecHash (Get-Sha256File $autoSecPath) "auto_section.cfg khong duoc doi du chi 1 byte"
    Assert-Equal $customHash (Get-Sha256File $customUserFile) "my_custom_notes.txt khong duoc bi xoa hoac sua"
}

Run-Test "Nhom 9: User Config" "Obsolete runtime duoc xoa neu khop hash cu, giu lai neu user da sua, legacy khong xoa" {
    $targetDir = Join-Path $testRunDir "obsolete_target"
    New-Item -ItemType Directory -Path $targetDir -Force | Out-Null

    # Tao 2 file cu thuoc manifest cu:
    # 1. file_unmodified.dll (chua bi sua)
    # 2. file_modified.dll (user da sua)
    $unmodFile = Join-Path $targetDir "file_unmodified.dll"
    $modFile = Join-Path $targetDir "file_modified.dll"
    $legacyFile = Join-Path $targetDir "untracked_user_tool.exe"

    [System.IO.File]::WriteAllText($unmodFile, "Original Unmodified DLL", [System.Text.Encoding]::UTF8)
    [System.IO.File]::WriteAllText($modFile, "User Modified Content", [System.Text.Encoding]::UTF8)
    [System.IO.File]::WriteAllText($legacyFile, "Untracked tool", [System.Text.Encoding]::UTF8)

    # Manifest cu: ghi nhan hash goc cua file_unmodified va file_modified
    $oldMeta = New-Object TTSK_AutoDim_Plates.Updater.ReleaseMetadata
    $oldMeta.schemaVersion = 1
    $oldMeta.product = "TTSK Dim Plates"
    $oldMeta.repository = "NguyenLePhuu/TTSK-Dim-Plates"
    $oldMeta.version = "1.0.1"
    $oldMeta.tag = "v1.0.1"
    $oldMeta.commit = "a1b2c3d4e5f60718293041526374859607182930"
    $oldMeta.managedFiles.Add("file_unmodified.dll")
    $oldMeta.managedFiles.Add("file_modified.dll")
    $oldMeta.SaveToFile((Join-Path $targetDir "release.json"))

    $oldSums = New-Object 'System.Collections.Generic.Dictionary[string, string]' ([System.StringComparer]::OrdinalIgnoreCase)
    $oldSums["file_unmodified.dll"] = Get-Sha256File $unmodFile
    $oldSums["file_modified.dll"] = "1111111111111111111111111111111111111111111111111111111111111111" # Hash goc khac voi hash user hien tai
    $oldSums["release.json"] = Get-Sha256File (Join-Path $targetDir "release.json")
    [TTSK_AutoDim_Plates.Updater.UpdateChecksumManifest]::WriteChecksums((Join-Path $targetDir "SHA256.csv"), $oldSums)

    # Staging moi: Loai bo ca 2 file khoi manifest moi (chi con new_runtime.dll)
    $stagingDir = Join-Path $testRunDir "obsolete_staging"
    New-Item -ItemType Directory -Path $stagingDir -Force | Out-Null
    [System.IO.File]::WriteAllText((Join-Path $stagingDir "new_runtime.dll"), "New 1.0.2 runtime", [System.Text.Encoding]::UTF8)

    $newMeta = New-Object TTSK_AutoDim_Plates.Updater.ReleaseMetadata
    $newMeta.schemaVersion = 1
    $newMeta.product = "TTSK Dim Plates"
    $newMeta.repository = "NguyenLePhuu/TTSK-Dim-Plates"
    $newMeta.version = "1.0.2"
    $newMeta.tag = "v1.0.2"
    $newMeta.commit = "b1b2c3d4e5f60718293041526374859607182930"
    $newMeta.managedFiles.Add("new_runtime.dll")
    $newMeta.SaveToFile((Join-Path $stagingDir "release.json"))

    $newSums = New-Object 'System.Collections.Generic.Dictionary[string, string]' ([System.StringComparer]::OrdinalIgnoreCase)
    $newSums["new_runtime.dll"] = Get-Sha256File (Join-Path $stagingDir "new_runtime.dll")
    $newSums["release.json"] = Get-Sha256File (Join-Path $stagingDir "release.json")
    [TTSK_AutoDim_Plates.Updater.UpdateChecksumManifest]::WriteChecksums((Join-Path $stagingDir "SHA256.csv"), $newSums)

    # Thuc hien cap nhat
    Complete-TestPackage $stagingDir $newMeta
    $tx = New-Object TTSK_AutoDim_Plates.Updater.UpdateTransaction($targetDir, $stagingDir, "obsolete_session", $newMeta)
    $tx.Execute()

    # Kiem tra ket qua:
    # 1. file_unmodified.dll (chua bi sua, dung hash cu) -> phai bi XOA khoi target
    Assert-True (!(Test-Path -LiteralPath $unmodFile)) "file_unmodified.dll cu khop hash phai bi xoa khoi target"

    # 2. file_modified.dll (da bi user sua, khac hash cu) -> phai duoc GIU LAI
    Assert-True (Test-Path -LiteralPath $modFile) "file_modified.dll da bi user sua phai duoc giu lai, khong duoc xoa"

    # 3. untracked_user_tool.exe (file khong nam trong manifest cu/moi) -> phai duoc GIU LAI
    Assert-True (Test-Path -LiteralPath $legacyFile) "File la untracked khong thuoc manifest tuyet doi khong bi xoa"
}

Run-Test "Transaction integration (not network/restart E2E)" "Ap dung ban cap nhat vao fixture target va verify success marker" {
    $e2eTarget = Join-Path $testRunDir "e2e_target"
    New-Item -ItemType Directory -Path $e2eTarget -Force | Out-Null

    $portableSrc = Join-Path $repoRoot "portable"
    Copy-Item -Path "$portableSrc\*" -Destination $e2eTarget -Recurse -Force

    $pkgOutDir = Join-Path $testRunDir "e2e_package"
    $headSha = (git rev-parse HEAD).Trim()
    $pkgScript = Join-Path $repoRoot "scripts\Build-GitHubReleasePackage.ps1"
    & $pkgScript -Version "1.0.88" -Tag "v1.0.88" -CommitSha $headSha -OutDir $pkgOutDir -PortableDir "portable"

    $zipPath = Join-Path $pkgOutDir "TTSK-Dim-Plates-Portable.zip"
    $e2eStaging = Join-Path $testRunDir "e2e_staging"
    $metaOut = $null
    [TTSK_AutoDim_Plates.Updater.UpdateDownloader]::ExtractAndValidatePackage($zipPath, $e2eStaging, [ref]$metaOut)

    $tx = New-Object TTSK_AutoDim_Plates.Updater.UpdateTransaction($e2eTarget, $e2eStaging, "e2e_session", $metaOut)
    $tx.Execute()

    $verAfter = [TTSK_AutoDim_Plates.Updater.UpdateManager]::ResolveCurrentVersion($e2eTarget)
    Assert-Equal "1.0.88" ($verAfter.ToString()) "Target fixture phai co version 1.0.88 sau khi cap nhat"

    $mgr = New-Object TTSK_AutoDim_Plates.Updater.UpdateManager($e2eTarget)
    $markerVer = $mgr.CheckSuccessMarker()
    Assert-Equal "1.0.88" $markerVer "Success marker phai tra ve phien ban 1.0.88"
    Assert-True ($null -eq $mgr.CheckSuccessMarker()) "Lan doc thu hai success marker phai la null vi da tieu thu"
}
