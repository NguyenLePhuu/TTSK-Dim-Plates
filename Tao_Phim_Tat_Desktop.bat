@echo off
title Tao Phim Tat TTSK Dim Plates Ra Desktop
echo Dang tao phim tat TTSK Dim Plates ra Desktop...

powershell -NoProfile -ExecutionPolicy Bypass -Command "$ws = New-Object -ComObject WScript.Shell; $desktop = [Environment]::GetFolderPath('Desktop'); $s = $ws.CreateShortcut($desktop + '\TTSK Dim Plates.lnk'); $s.TargetPath = '%~dp0portable\TTSK Dim Plates.exe'; $s.WorkingDirectory = '%~dp0portable'; $s.IconLocation = '%~dp0portable\TTSK Dim Plates.exe,0'; $s.Description = 'Khoi dong TTSK Auto Dimension cho Tekla Structures'; $s.Save()"

if %ERRORLEVEL% EQU 0 (
    echo.
    echo ======================================================================
    echo  [THANH CONG] Da tao phim tat 'TTSK Dim Plates' tren Man hinh Desktop!
    echo  Ban co the khoi dong phan mem truc tiep tu Desktop.
    echo ======================================================================
    echo.
) else (
    echo.
    echo [LOI] Khong the tu dong tao phim tat tren Desktop.
    echo.
)

pause
exit
