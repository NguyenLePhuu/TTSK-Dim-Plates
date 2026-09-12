# Test Mutex, UpdateLock and Multi-Instance Handling
Run-Test "Nhom 6: Lock & Mutual Exclusion" "UpdateLock khoa target duy nhat, cho phep nhieu target khac nhau chay song song" {
    $targetA = Join-Path $testRunDir "target_A"
    $targetB = Join-Path $testRunDir "target_B"
    New-Item -ItemType Directory -Path $targetA -Force | Out-Null
    New-Item -ItemType Directory -Path $targetB -Force | Out-Null

    $lockA1 = New-Object TTSK_AutoDim_Plates.Updater.UpdateLock($targetA)
    $acquiredA1 = $lockA1.TryAcquireUpdateLock(1000)
    Assert-True $acquiredA1 "Lock target A lan dau phai thanh cong"

    $lockA2 = New-Object TTSK_AutoDim_Plates.Updater.UpdateLock($targetA)
    $acquiredA2 = $lockA2.TryAcquireUpdateLock(500)
    Assert-True (!$acquiredA2) "Instance thu hai tren target A phai bi chan"
    $lockA2.Dispose()

    $lockB = New-Object TTSK_AutoDim_Plates.Updater.UpdateLock($targetB)
    $acquiredB = $lockB.TryAcquireUpdateLock(1000)
    Assert-True $acquiredB "Target B doc lap phai acquire thanh cong du target A dang bi khoa"
    $lockB.Dispose()

    $lockA1.Dispose()

    $lockA3 = New-Object TTSK_AutoDim_Plates.Updater.UpdateLock($targetA)
    $acquiredA3 = $lockA3.TryAcquireUpdateLock(1000)
    Assert-True $acquiredA3 "Target A phai acquire lai duoc sau khi lock cu giai phong"
    $lockA3.Dispose()
}

Run-Test "Nhom 5: Tekla Isolation" "Worker chay khong can Tekla va co exit code chuan xac" {
    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = $builtExe
    $psi.Arguments = "--apply-update --silent --target `"C:\NonExistent_Path_12345`" --staging `"C:\NonExistent_Staging`""
    $psi.UseShellExecute = $false
    $psi.RedirectStandardError = $true
    $psi.RedirectStandardOutput = $true
    $psi.EnvironmentVariables.Remove("TeklaBinPath")

    $proc = [System.Diagnostics.Process]::Start($psi)
    $proc.WaitForExit(10000)

    Assert-Equal 1 $proc.ExitCode "Worker phai tra ve exit code 1 khi tham so sai ma khong crash Tekla"
}

Run-Test "Nhom 6: Lock & Mutual Exclusion" "Kiem tra ca 5 busy flags tren MainForm tu choi update khi ban" {
    $asm = [System.AppDomain]::CurrentDomain.GetAssemblies() | Where-Object { $_.GetName().Name -eq "TTSK Dim Plates" } | Select-Object -First 1
    $mainFormType = $asm.GetType("TTSK_AutoDim_Plates.MainForm")
    Assert-True ($null -ne $mainFormType) "MainForm type phai tim thay tu assembly TTSK Dim Plates"
    # Khoi tao uninitialized object cua MainForm ma khong goi constructor (tranh Tekla runtime dependencies)
    $form = [System.Runtime.Serialization.FormatterServices]::GetUninitializedObject($mainFormType)

    # Khi tat ca 5 co deu false -> CanApplyUpdateNow phai tra ve true
    $bindingFlags = [System.Reflection.BindingFlags]::Instance -bor [System.Reflection.BindingFlags]::NonPublic -bor [System.Reflection.BindingFlags]::Public
    $methodCanApply = $mainFormType.GetMethod("CanApplyUpdateNow", $bindingFlags)

    Assert-True ($methodCanApply.Invoke($form, $null)) "Tat ca co tat -> CanApplyUpdateNow phai la true"

    $busyFlags = @("_isBatchRunning", "_pdfCommandRunning", "_gridVisibilityMacroRunning", "_fitAndCleanupRunning", "_snapshotExportRunning")
    foreach ($flagName in $busyFlags) {
        $field = $mainFormType.GetField($flagName, $bindingFlags)
        Assert-True ($null -ne $field) "Field $flagName phai ton tai tren MainForm"

        # Bat co
        $field.SetValue($form, $true)
        $canApply = $methodCanApply.Invoke($form, $null)
        Assert-True (!$canApply) "Khi co $flagName = true, CanApplyUpdateNow phai tra ve false"

        # Tat co lai
        $field.SetValue($form, $false)
    }

    # Sau khi tat tat ca co -> tro lai true
    Assert-True ($methodCanApply.Invoke($form, $null)) "Sau khi tat tat ca co -> CanApplyUpdateNow phai tra ve true"
}

