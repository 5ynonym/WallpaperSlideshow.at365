@echo off
setlocal

set "SOURCE=%~dp0publish\WallpaperSlideshow.at365.exe"
set "TARGET_DIR=%~dp0..\..\00.ESSENTIAL\00.MainTools\WallpaperSlideshow.at365"
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