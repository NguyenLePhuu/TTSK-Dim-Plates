@echo off
chcp 65001 >nul 2>&1
setlocal
title Tao Phim Tat TTSK Dim Plates
echo.
echo ============================================
echo   TAO SHORTCUT TTSK DIM PLATES TREN DESKTOP
echo ============================================
echo.
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\New-DesktopShortcut.ps1" %*
set "SHORTCUT_EXIT=%ERRORLEVEL%"
echo.
if not "%SHORTCUT_EXIT%"=="0" (
    echo [LOI] Chua tao duoc phim tat. Xem chi tiet o tren.
    echo.
)
set "NO_PAUSE="
for %%A in (%*) do if /I "%%~A"=="-NoPause" set "NO_PAUSE=1"
if not defined NO_PAUSE pause
exit /b %SHORTCUT_EXIT%
