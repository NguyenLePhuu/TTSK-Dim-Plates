@echo off
setlocal
title Cap Nhat Portable va GitHub - TTSK Auto Dim
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\Update-Portable.ps1" %*
set "UPDATE_EXIT=%ERRORLEVEL%"
echo.
if not "%UPDATE_EXIT%"=="0" echo [THAT BAI] Chua dong bo GitHub. Xem log o tren.
set "NO_PAUSE="
for %%A in (%*) do if /I "%%~A"=="-NoPause" set "NO_PAUSE=1"
if not defined NO_PAUSE pause
exit /b %UPDATE_EXIT%
