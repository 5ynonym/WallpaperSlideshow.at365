@echo off
setlocal DisableDelayedExpansion

set "SOURCE=%~dp0publish\WallpaperSlideshow.at365.exe"
set "TARGET_DIR="
if not "%~1"=="" (
    set "TARGET_DIR=%~f1"
    goto target_ready
)
if exist "%~dp0deploy.local.txt" set /p "TARGET_DIR="<"%~dp0deploy.local.txt"
if not defined TARGET_DIR (
    echo Specify a destination argument or put its absolute path in deploy.local.txt.
    exit /b 1
)

:target_ready
set "TARGET=%TARGET_DIR%\WallpaperSlideshow.at365.exe"

if not exist "%SOURCE%" (
    echo Source executable not found.
    exit /b 1
)

if not exist "%TARGET_DIR%\" (
    echo Target directory not found.
    exit /b 1
)

copy /Y "%SOURCE%" "%TARGET%" >nul
if errorlevel 1 (
    echo Copy failed.
    exit /b 1
)

echo Deployment completed.
exit /b 0
