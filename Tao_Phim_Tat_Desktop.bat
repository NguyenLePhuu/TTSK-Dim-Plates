@echo off
title Tao Phim Tat TTSK Dim Plates Ra Desktop
cd /d "%~dp0"
echo ======================================================================
echo   DANG TAO PHIM TAT TTSK DIM PLATES RA MAN HINH DESKTOP...
echo ======================================================================

powershell -NoProfile -ExecutionPolicy Bypass -Command "$fso = New-Object -ComObject Scripting.FileSystemObject; $ws = New-Object -ComObject WScript.Shell; $curr = (Get-Item -LiteralPath '.').FullName; $exe = Join-Path $curr 'portable\TTSK Dim Plates.exe'; if (Test-Path -LiteralPath $exe) { $shortExe = $fso.GetFile($exe).ShortPath; $shortDir = $fso.GetFolder((Join-Path $curr 'portable')).ShortPath; $desktop = [Environment]::GetFolderPath('Desktop'); $lnk = Join-Path $desktop 'TTSK Dim Plates.lnk'; $s = $ws.CreateShortcut($lnk); $s.TargetPath = $shortExe; $s.WorkingDirectory = $shortDir; $s.IconLocation = $shortExe + ',0'; $s.Description = 'Khoi dong TTSK Auto Dimension cho Tekla Structures'; $s.Save(); Write-Host 'SUCCESS' } else { Write-Host 'EXE_NOT_FOUND' }"

if %ERRORLEVEL% EQU 0 (
    echo.
    echo ======================================================================
    echo  [THANH CONG] Da tao phim tat 'TTSK Dim Plates' tren Man hinh Desktop!
    echo  Ban co the ra Desktop va click dup vao bieu tuong logo de khoi dong.
    echo ======================================================================
    echo.
) else (
    echo.
    echo [LOI] Khong the tu dong tao phim tat tren Desktop.
    echo.
)

timeout /t 3 >nul
exit