@echo off
title Khoi dong TTSK Dim Plates
:: Chuyen thu muc lam viec vao portable de dam bao day du DLL va tai nguyen
cd /d "%~dp0portable"

if not exist "TTSK Dim Plates.exe" (
    echo ======================================================================
    echo [LOI] Khong tim thay file TTSK Dim Plates.exe trong thu muc portable!
    echo Vui long kiem tra lai bo cai dat hoac lien he admin.
    echo ======================================================================
    pause
    exit /b 1
)

:: Khoi dong ung dung chinh voi moi truong portable day du
start "" "TTSK Dim Plates.exe"
exit
