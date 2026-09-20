@echo off
setlocal
title Luu Tru Code - GitHub / Google Drive - TTSK
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0..\scripts\Backup-Source.ps1" %*
set "BACKUP_EXIT=%ERRORLEVEL%"
echo.
if not "%BACKUP_EXIT%"=="0" echo [THAT BAI] Chua hoan tat luu tru. Xem thong bao o tren.
set "NO_PAUSE="
for %%A in (%*) do if /I "%%~A"=="-NoPause" set "NO_PAUSE=1"
if not defined NO_PAUSE pause
exit /b %BACKUP_EXIT%
