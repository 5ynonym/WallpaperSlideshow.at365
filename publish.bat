@echo off
setlocal

pushd "%~dp0"
dotnet publish "WallpaperSlideshow.at365.csproj" -c Release -r win-x64 --self-contained false %*
set "exitCode=%ERRORLEVEL%"
popd

exit /b %exitCode%
