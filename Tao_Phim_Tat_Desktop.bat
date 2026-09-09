@echo off
title Tao Phim Tat TTSK Dim Plates
cd /d "%~dp0"
echo ======================================================================
echo   DANG TAO PHIM TAT CO LOGO CHO MAY TINH NAY...
echo ======================================================================

powershell -NoProfile -Command "$ErrorActionPreference = 'Stop'; $fso = New-Object -ComObject Scripting.FileSystemObject; $ws = New-Object -ComObject WScript.Shell; $curr = (Get-Item -LiteralPath '.').FullName; $exe = Join-Path $curr 'portable\TTSK Dim Plates.exe'; if (Test-Path -LiteralPath $exe) { $shortExe = $fso.GetFile($exe).ShortPath; $shortDir = $fso.GetFolder((Join-Path $curr 'portable')).ShortPath; $shortRoot = $fso.GetFolder($curr).ShortPath; $desktop = [Environment]::GetFolderPath('Desktop'); $sDesk = $ws.CreateShortcut((Join-Path $desktop 'TTSK Dim Plates.lnk')); $sDesk.TargetPath = $shortExe; $sDesk.WorkingDirectory = $shortDir; $sDesk.IconLocation = $shortExe + ',0'; $sDesk.Description = 'TTSK Auto Dimension cho Tekla Structures'; $sDesk.Save(); $sRoot = $ws.CreateShortcut((Join-Path $shortRoot 'TTSK Dim Plates.lnk')); $sRoot.TargetPath = $shortExe; $sRoot.WorkingDirectory = $shortDir; $sRoot.IconLocation = $shortExe + ',0'; $sRoot.Description = 'TTSK Auto Dimension cho Tekla Structures'; $sRoot.Save(); Write-Host 'SUCCESS' } else { Write-Error 'EXE_NOT_FOUND'; exit 1 }"

if %ERRORLEVEL% EQU 0 (
    echo.
    echo ======================================================================
    echo  [THANH CONG] Da tao phim tat co LOGO thanh cong:
    echo    1. Tren Man hinh Desktop cua may nay
    echo    2. Ngay tai thu muc goc nay (TTSK Dim Plates.lnk)
    echo ======================================================================
    echo.
) else (
    echo.
    echo [LOI] Khong the tao phim tat tren may nay.
    echo.
)

timeout /t 3 >nul
exit