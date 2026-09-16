@echo off
chcp 65001 >nul
title Kiem Tra Cau Hinh Google Drive - TTSK
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0..\scripts\Test-DriveConfig.ps1"
echo.
pause
