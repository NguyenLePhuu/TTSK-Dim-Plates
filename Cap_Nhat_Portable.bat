@echo off
setlocal
title Cap Nhat Portable va GitHub - TTSK Auto Dim
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\Update-Portable.ps1" %*
set "UPDATE_EXIT=%ERRORLEVEL%"
echo.
if not "%UPDATE_EXIT%"=="0" echo [THAT BAI] Chua dong bo GitHub. Xem log o tren.
if /I not "%~1"=="-NoPause" pause
exit /b %UPDATE_EXIT%
