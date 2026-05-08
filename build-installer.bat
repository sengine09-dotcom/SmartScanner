@echo off
setlocal

echo ============================================================
echo  Smart Scanner - Build Installer
echo ============================================================
echo.

:: ---------- 1. Publish the application ----------
echo [1/2] Publishing application (self-contained, win-x86)...
dotnet publish SmartScanner\SmartScanner.csproj ^
    -c Release ^
    -r win-x86 ^
    --self-contained true ^
    -p:PublishSingleFile=false ^
    -p:PublishReadyToRun=true ^
    -o publish

if %ERRORLEVEL% neq 0 (
    echo ERROR: dotnet publish failed.
    pause
    exit /b 1
)
echo    Done.
echo.

:: ---------- 2. Compile installer with Inno Setup ----------
echo [2/2] Compiling installer with Inno Setup...

:: Try common Inno Setup install locations
set ISCC=
if exist "%LOCALAPPDATA%\Programs\Inno Setup 6\ISCC.exe" set "ISCC=%LOCALAPPDATA%\Programs\Inno Setup 6\ISCC.exe"
if exist "C:\Program Files (x86)\Inno Setup 6\ISCC.exe"  set "ISCC=C:\Program Files (x86)\Inno Setup 6\ISCC.exe"
if exist "C:\Program Files\Inno Setup 6\ISCC.exe"        set "ISCC=C:\Program Files\Inno Setup 6\ISCC.exe"

if "%ISCC%"=="" (
    echo ERROR: Inno Setup not found.
    echo Please install Inno Setup 6 from https://jrsoftware.org/isinfo.php
    echo Then run this script again.
    pause
    exit /b 1
)

"%ISCC%" installer.iss

if %ERRORLEVEL% neq 0 (
    echo ERROR: Inno Setup compilation failed.
    pause
    exit /b 1
)

echo.
echo ============================================================
echo  Installer created in:  installer_output\
echo ============================================================
echo.
pause
