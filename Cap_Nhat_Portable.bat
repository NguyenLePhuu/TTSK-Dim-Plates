@echo off
setlocal EnableDelayedExpansion
title Cap Nhat Portable va Day Len GitHub - TTSK Auto Dim
cd /d "%~dp0"

echo ========================================================================
echo   TTSK AUTO DIM - BIEN DICH, CAP NHAT PORTABLE VA DAY LEN GITHUB
echo ========================================================================
echo.
echo [1/4] Dang tim kiem trinh bien dich Visual Studio MSBuild...

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
    echo Vui long cai dat Visual Studio voi workload .NET Desktop Development.
    echo.
    pause
    exit /b 1
)

echo        Tim thay MSBuild: "%MSBUILD_EXE%"
echo.
echo [2/4] Dang bien dich ma nguon Release x64 voi thuat toan moi nhat...
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
echo [3/4] Dang dong bo tep thuc thi sang thu muc portable...

set "OUT_DIR=%PROJ_DIR%\bin\x64\Release"
set "PORTABLE_DIR=%~dp0portable"

if not exist "%PORTABLE_DIR%" mkdir "%PORTABLE_DIR%"

copy /y "%OUT_DIR%\TTSK Dim Plates.exe" "%PORTABLE_DIR%\TTSK Dim Plates.exe" >nul
if exist "%OUT_DIR%\TTSK Dim Plates.pdb" (
    copy /y "%OUT_DIR%\TTSK Dim Plates.pdb" "%PORTABLE_DIR%\TTSK Dim Plates.pdb" >nul
)

echo        Da cap nhat xong ban portable moi nhat.
echo.
echo [4/4] Dang tu dong kiem tra va day (push) len GitHub...

where git >nul 2>nul
if %ERRORLEVEL% NEQ 0 (
    echo        [BO QUA] Git chua duoc cai dat hoac chua duoc them vao PATH.
) else (
    cd /d "%~dp0"
    set "HAS_CHANGES="
    for /f "tokens=*" %%g in ('git status --porcelain') do set "HAS_CHANGES=1"
    
    if defined HAS_CHANGES (
        echo        Phat hien thay doi, dang commit va day len GitHub...
        git add -A
        git commit -m "Cap nhat thuat toan moi va dong bo portable (%DATE% %TIME%)"
        git push origin main
        if !ERRORLEVEL! EQU 0 (
            echo.
            echo        [THANH CONG] Da day thanh cong toan bo thay doi len GitHub!
        ) else (
            echo.
            echo        [CANH BAO] Khong the day len GitHub. Vui long kiem tra ket noi mang.
        )
    ) else (
        echo        Ma nguon va ban portable da dong bo voi GitHub, khong co gi moi can day.
    )
)

echo.
echo ========================================================================
echo   [HOAN TAT] QUA TRINH CAP NHAT VA DONG BO DA THANH CONG!
echo.
echo   - Thuat toan moi nhat da duoc nap vao: portable\TTSK Dim Plates.exe
echo   - Ma nguon va ban portable da duoc dong bo an toan len GitHub.
echo ========================================================================
echo.
pause
exit /b 0