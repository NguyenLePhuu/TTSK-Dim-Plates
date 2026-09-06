@echo off
setlocal EnableDelayedExpansion
title Cap Nhat Portable - TTSK Auto Dim
cd /d "%~dp0"

echo ========================================================================
echo        TTSK AUTO DIM - TRINH TU DONG BIEN DICH VA CAP NHAT PORTABLE
echo ========================================================================
echo.
echo [1/3] Dang tim kiem trinh bien dich Visual Studio MSBuild...

set "MSBUILD_EXE="
set "VSWHERE=%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe"

if exist "%VSWHERE%" (
    for /f "usebackq tokens=*" %%i in (`"%VSWHERE%" -latest -requires Microsoft.Component.MSBuild -find "MSBuild\**\Bin\amd64\MSBuild.exe" 2^>nul`) do (
        if exist "%%i" set "MSBUILD_EXE=%%i"
    )
    if not defined MSBUILD_EXE (
        for /f "usebackq tokens=*" %%i in (`"%VSWHERE%" -latest -requires Microsoft.Component.MSBuild -find "MSBuild\**\Bin\MSBuild.exe" 2^>nul`) do (
            if exist "%%i" set "MSBUILD_EXE=%%i"
        )
    )
)

if not defined MSBUILD_EXE (
    if exist "C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\amd64\MSBuild.exe" set "MSBUILD_EXE=C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\amd64\MSBuild.exe"
    if exist "C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\amd64\MSBuild.exe" set "MSBUILD_EXE=C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\amd64\MSBuild.exe"
    if exist "C:\Program Files\Microsoft Visual Studio\2022\Professional\MSBuild\Current\Bin\amd64\MSBuild.exe" set "MSBUILD_EXE=C:\Program Files\Microsoft Visual Studio\2022\Professional\MSBuild\Current\Bin\amd64\MSBuild.exe"
    if exist "C:\Program Files\Microsoft Visual Studio\2022\Enterprise\MSBuild\Current\Bin\amd64\MSBuild.exe" set "MSBUILD_EXE=C:\Program Files\Microsoft Visual Studio\2022\Enterprise\MSBuild\Current\Bin\amd64\MSBuild.exe"
)

if not defined MSBUILD_EXE (
    echo [LOI] Khong tim thay MSBuild.exe tren may tinh!
    echo Vui long cai dat Visual Studio voi workload '.NET Desktop Development'.
    echo.
    pause
    exit /b 1
)

echo        Tim thay MSBuild: "%MSBUILD_EXE%"
echo.
echo [2/3] Dang bien dich ma nguon Release x64 voi thuat toan moi nhat...
echo.

set "PROJ_DIR=%~dp0TTSK Dim Plates\TTSK Dim Plates"
set "CSPROJ=%PROJ_DIR%\TTSK Dim Plates.csproj"

"%MSBUILD_EXE%" "%CSPROJ%" /p:Configuration=Release /p:Platform=x64 /v:m /nologo

if %ERRORLEVEL% NEQ 0 (
    echo.
    echo ========================================================================
    echo   [THAT BAI] BIEN DICH CO LOI!
    echo   Vui long kiem tra lai ma nguon C# truoc khi cap nhat.
    echo   Ban portable cu van duoc giu nguyen an toan.
    echo ========================================================================
    echo.
    pause
    exit /b %ERRORLEVEL%
)

echo.
echo [3/3] Dang dong bo tep thuc thi sang thu muc portable...

set "OUT_DIR=%PROJ_DIR%\bin\x64\Release"
set "PORTABLE_DIR=%~dp0portable"

if not exist "%PORTABLE_DIR%" mkdir "%PORTABLE_DIR%"

copy /y "%OUT_DIR%\TTSK Dim Plates.exe" "%PORTABLE_DIR%\TTSK Dim Plates.exe" >nul
if exist "%OUT_DIR%\TTSK Dim Plates.pdb" (
    copy /y "%OUT_DIR%\TTSK Dim Plates.pdb" "%PORTABLE_DIR%\TTSK Dim Plates.pdb" >nul
)

echo.
echo ========================================================================
echo   [THANH CONG] DA BIEN DICH VA CAP NHAT PORTABLE HOAN TAT!
echo.
echo   - Toan bo thuat toan moi nhat da duoc nap vao: portable\TTSK Dim Plates.exe
echo   - Ban co the mo ung dung truc tiep de kiem tra va su dung ngay.
echo ========================================================================
echo.
pause
exit /b 0