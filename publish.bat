@echo off
setlocal

pushd "%~dp0"
dotnet publish "WallpaperSlideshow.at365.slnx" %*
set "exitCode=%ERRORLEVEL%"
popd

exit /b %exitCode%
