@echo off
chcp 65001 >nul
setlocal enabledelayedexpansion
title Cai Dat Google Drive cho TTSK - Tu Dong
color 0B

echo ========================================================
echo   CAI DAT GOOGLE DRIVE CHO TTSK DIM PLATES
echo ========================================================
echo.

set "DEFAULT_JSON=C:\Users\phu17\Downloads\client_secret_192927333400-uvvl2ddrb1cmec6vp6qmvhro3fmhtjmr.apps.googleusercontent.com.json"
set "CLIENT_JSON="

if exist "%DEFAULT_JSON%" (
    set "CLIENT_JSON=%DEFAULT_JSON%"
    echo [OK] Da tim thay file OAuth JSON:
    echo   !CLIENT_JSON!
    echo.
) else (
    for %%F in ("%USERPROFILE%\Downloads\client_secret_*.json") do (
        set "CLIENT_JSON=%%~fF"
    )
    if defined CLIENT_JSON (
        echo [OK] Da tim thay file OAuth JSON:
        echo   !CLIENT_JSON!
        echo.
    )
)

if not defined CLIENT_JSON (
    echo Nhap duong dan file OAuth JSON tai ve tu Google Cloud:
    set /p CLIENT_JSON="Duong dan: "
    if not exist "!CLIENT_JSON!" (
        echo [LOI] Khong tim thay file JSON.
        pause
        exit /b 1
    )
)

echo Dang bat dau dang nhap Google...
echo.
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0..\scripts\Connect-GoogleDrivePublisher.ps1" -ClientJson "!CLIENT_JSON!"
if not "%ERRORLEVEL%"=="0" (
    echo.
    echo [LOI] Dang nhap Google that bai.
    pause
    exit /b 1
)

echo.
echo [OK] Dang nhap Google thanh cong!
echo.
echo Dang khoi tao chi muc Google Drive...
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0..\scripts\Initialize-GoogleDriveUpdates.ps1" -FolderId "1sTL3OSwqUFrCuXqr0fhjzsiQZQtxQqsh"
if not "%ERRORLEVEL%"=="0" (
    echo.
    echo [CANH BAO] Kiem tra chi muc va quyen doc cong khai cua thu muc Drive.
    pause
    exit /b 1
)

echo.
echo ========================================================
echo   [THANH CONG] CAU HINH GOOGLE DRIVE HOAN TAT 100%!
echo ========================================================
echo.
pause
exit /b 0
