# Test Security, Path Traversal, and Zip Slip Prevention
Run-Test "Nhom 4: Security" "Chong Zip Slip, ky tu cam, ten thiet bi DOS va luong phu ADS" {
    Assert-True (![TTSK_AutoDim_Plates.Updater.UpdateSecurity]::IsSafeRelativePath("../evil.dll")) "Path chua .. phai bi tu choi"
    Assert-True (![TTSK_AutoDim_Plates.Updater.UpdateSecurity]::IsSafeRelativePath("sub/../../evil.dll")) "Path chua ../.. phai bi tu choi"
    Assert-True (![TTSK_AutoDim_Plates.Updater.UpdateSecurity]::IsSafeRelativePath("C:\evil.dll")) "Path rooted phai bi tu choi"
    Assert-True (![TTSK_AutoDim_Plates.Updater.UpdateSecurity]::IsSafeRelativePath("/evil.dll")) "Leading slash phai bi tu choi"
    Assert-True (![TTSK_AutoDim_Plates.Updater.UpdateSecurity]::IsSafeRelativePath("file.dll:hidden_stream")) "ADS dau : phai bi tu choi"
    Assert-True (![TTSK_AutoDim_Plates.Updater.UpdateSecurity]::IsSafeRelativePath("CON")) "CON device name phai bi tu choi"
    Assert-True (![TTSK_AutoDim_Plates.Updater.UpdateSecurity]::IsSafeRelativePath("NUL.txt")) "NUL device name phai bi tu choi"
    Assert-True (![TTSK_AutoDim_Plates.Updater.UpdateSecurity]::IsSafeRelativePath("COM1")) "COM1 device name phai bi tu choi"
    Assert-True (![TTSK_AutoDim_Plates.Updater.UpdateSecurity]::IsSafeRelativePath("folder. /test")) "Trailing dot alias phai bi tu choi"
    Assert-True (![TTSK_AutoDim_Plates.Updater.UpdateSecurity]::IsSafeRelativePath("folder /test")) "Trailing space alias phai bi tu choi"

    Assert-True ([TTSK_AutoDim_Plates.Updater.UpdateSecurity]::IsSafeRelativePath("Data/JapaneseDictionary.tsv")) "Path an toan phai pass"
    Assert-True ([TTSK_AutoDim_Plates.Updater.UpdateSecurity]::IsSafeRelativePath("Resources/Slot01_dark.png")) "Path an toan phai pass"

    $baseDir = Join-Path $testRunDir "safe_base"
    New-Item -ItemType Directory -Path $baseDir -Force | Out-Null

    $threwZipSlip = $false
    try {
        [TTSK_AutoDim_Plates.Updater.UpdateSecurity]::GetSafeFullPath($baseDir, "../outside.dll")
    } catch {
        $threwZipSlip = $true
    }
    Assert-True $threwZipSlip "GetSafeFullPath phai nem ngoai le khi co Zip Slip"
}

Run-Test "Nhom 4: Security" "Bao ve tuyet doi file cau hinh nguoi dung (theme, shortcut, auto_section)" {
    Assert-True ([TTSK_AutoDim_Plates.Updater.UpdateSecurity]::IsProtectedUserFile("theme.cfg")) "theme.cfg phai duoc bao ve"
    Assert-True ([TTSK_AutoDim_Plates.Updater.UpdateSecurity]::IsProtectedUserFile("shortcut.cfg")) "shortcut.cfg phai duoc bao ve"
    Assert-True ([TTSK_AutoDim_Plates.Updater.UpdateSecurity]::IsProtectedUserFile("auto_section.cfg")) "auto_section.cfg phai duoc bao ve"
    Assert-True ([TTSK_AutoDim_Plates.Updater.UpdateSecurity]::IsProtectedUserFile("logs/DPMPrinter_ADMIN.log")) "File trong logs/ phai duoc bao ve"
    Assert-True ([TTSK_AutoDim_Plates.Updater.UpdateSecurity]::IsProtectedUserFile("app.user.config")) "user.config phai duoc bao ve"
    Assert-True ([TTSK_AutoDim_Plates.Updater.UpdateSecurity]::IsProtectedUserFile("Start TTSK Dim Plates.bat")) "Launcher bat phai duoc bao ve"

    Assert-True (![TTSK_AutoDim_Plates.Updater.UpdateSecurity]::IsProtectedUserFile("TTSK Dim Plates.exe")) "EXE runtime khong phai user file"
    Assert-True (![TTSK_AutoDim_Plates.Updater.UpdateSecurity]::IsProtectedUserFile("Data/JapaneseDictionary.tsv")) "Dictionary khong phai user file"
}
