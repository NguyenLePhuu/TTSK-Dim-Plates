# Test Version and Manifest Validation
Run-Test "Nhom 1: Version" "Parse hop le va so sanh numeric (1.0.9 < 1.0.10)" {
    $v1 = $null
    $res1 = [TTSK_AutoDim_Plates.Updater.UpdateVersion]::TryParse("1.0.9", [ref]$v1)
    Assert-True $res1 "1.0.9 phai parse thanh cong"
    Assert-Equal 1 $v1.Major "Major phai la 1"
    Assert-Equal 0 $v1.Minor "Minor phai la 0"
    Assert-Equal 9 $v1.Patch "Patch phai la 9"

    $v2 = $null
    $res2 = [TTSK_AutoDim_Plates.Updater.UpdateVersion]::TryParse("v1.0.10", [ref]$v2)
    Assert-True $res2 "v1.0.10 phai parse thanh cong"
    Assert-Equal 10 $v2.Patch "Patch phai la 10"

    Assert-True ($v1.CompareTo($v2) -lt 0) "1.0.9 phai nho hon 1.0.10 theo so hoc"
    Assert-True ($v2.CompareTo($v1) -gt 0) "1.0.10 phai lon hon 1.0.9"
    Assert-True ($v1.Equals($v1)) "v1 phai bang chinh no"
    Assert-Equal "1.0.9" ($v1.ToString()) "ToString phai ra 1.0.9"
    Assert-Equal "v1.0.9" ($v1.ToDisplayString()) "ToDisplayString phai ra v1.0.9"
}

Run-Test "Nhom 1: Version" "Tu choi version khong hop le (4 phan, chu, so am)" {
    $dummy = $null
    Assert-True (![TTSK_AutoDim_Plates.Updater.UpdateVersion]::TryParse("1.0", [ref]$dummy)) "1.0 thieu patch"
    Assert-True (![TTSK_AutoDim_Plates.Updater.UpdateVersion]::TryParse("1.0.0.0", [ref]$dummy)) "1.0.0.0 co 4 phan"
    Assert-True (![TTSK_AutoDim_Plates.Updater.UpdateVersion]::TryParse("v1.0.alpha", [ref]$dummy)) "chua chu"
    Assert-True (![TTSK_AutoDim_Plates.Updater.UpdateVersion]::TryParse("-1.0.0", [ref]$dummy)) "so am"
    Assert-True (![TTSK_AutoDim_Plates.Updater.UpdateVersion]::TryParse("", [ref]$dummy)) "chuoi rong"
}

Run-Test "Nhom 1: Version" "Xac thuc ReleaseMetadata schema va tu choi metadata hong" {
    $validMeta = New-Object TTSK_AutoDim_Plates.Updater.ReleaseMetadata
    $validMeta.schemaVersion = 1
    $validMeta.product = "TTSK Dim Plates"
    $validMeta.repository = "NguyenLePhuu/TTSK-Dim-Plates"
    $validMeta.version = "1.0.15"
    $validMeta.tag = "v1.0.15"
    $validMeta.commit = "a1b2c3d4e5f60718293041526374859607182930"
    $validMeta.managedFiles.Add("TTSK Dim Plates.exe")
    $validMeta.managedFiles.Add("Data/JapaneseDictionary.tsv")

    $err = $null
    Assert-True ($validMeta.Validate([ref]$err)) "Metadata hop le phai pass"

    $invalidSchema = New-Object TTSK_AutoDim_Plates.Updater.ReleaseMetadata
    $invalidSchema.schemaVersion = 2
    Assert-True (!($invalidSchema.Validate([ref]$err))) "Schema version 2 phai bi tu choi"

    $badFiles = New-Object TTSK_AutoDim_Plates.Updater.ReleaseMetadata
    $badFiles.schemaVersion = 1
    $badFiles.product = "TTSK Dim Plates"
    $badFiles.repository = "NguyenLePhuu/TTSK-Dim-Plates"
    $badFiles.version = "1.0.1"
    $badFiles.tag = "v1.0.1"
    $badFiles.commit = "a1b2c3d4e5f60718293041526374859607182930"
    $badFiles.managedFiles.Add("release.json")
    Assert-True (!($badFiles.Validate([ref]$err))) "managedFiles chua release.json phai bi tu choi"
}

Run-Test "Nhom 1: Version" "Fallback phien ban hien tai: legacy khong co release.json ra 1.0.0" {
    $tempDir = Join-Path $testRunDir "legacy_version_test"
    New-Item -ItemType Directory -Path $tempDir -Force | Out-Null
    $resolved = [TTSK_AutoDim_Plates.Updater.UpdateManager]::ResolveCurrentVersion($tempDir)
    Assert-Equal 1 $resolved.Major "Legacy phai ra Major 1"
    Assert-Equal 0 $resolved.Minor "Legacy phai ra Minor 0"
    Assert-Equal 0 $resolved.Patch "Legacy phai ra Patch 0"
}

Run-Test "Nhom 2: Network & Cache" "Xu ly corrupt cache va JSON loi khong lam crash ung dung" {
    $tempTarget = Join-Path $testRunDir "corrupt_cache_target"
    New-Item -ItemType Directory -Path $tempTarget -Force | Out-Null

    $mgr = New-Object TTSK_AutoDim_Plates.Updater.UpdateManager($tempTarget)
    $pathHash = [TTSK_AutoDim_Plates.Updater.UpdateSecurity]::GetCanonicalPathHash($tempTarget)
    $stateCacheFile = Path-Combine (Path-Combine (Path-Combine ([Environment]::GetFolderPath([Environment+SpecialFolder]::LocalApplicationData)) "TTSK Dim Plates\Updater") $pathHash) "state.json"

    # Ghi file cache bi hong (rac du lieu)
    [System.IO.File]::WriteAllText($stateCacheFile, "{ Corrupted Invalid JSON content ... @#$%", [System.Text.Encoding]::UTF8)

    # LoadCachedState phai tra ve null ma khong crash ung dung
    $loadedCache = $mgr.LoadCachedState()
    Assert-True ($null -eq $loadedCache) "Corrupt cache file phai tra ve null an toan"

    # ParseReleaseJson voi chuoi rac
    $badRel = [TTSK_AutoDim_Plates.Updater.GitHubReleaseService]::ParseReleaseJson("<html>404 Not Found</html>")
    Assert-True ($null -eq $badRel) "JSON loi tu HTML error page phai tra ve null"
}
