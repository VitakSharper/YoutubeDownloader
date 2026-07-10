@echo off
setlocal
cd /d "%~dp0"

set VERSION=1.0.0
echo Publishing + zipping Liudochka Youtube Downloader (Avalonia) v%VERSION% ...
echo.

dotnet publish "YoutubeDownloader.Avalonia\YoutubeDownloader.Avalonia.csproj" -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=none -p:DebugSymbols=false -o "dist"
if errorlevel 1 exit /b 1

set ZIP=dist\LiudochkaYoutubeDownloader.Avalonia-v%VERSION%.zip
powershell -NoProfile -Command "Compress-Archive -Force -Path 'dist\LiudochkaYoutubeDownloader.Avalonia.exe' -DestinationPath '%ZIP%'"
if errorlevel 1 exit /b 1

echo.
echo Created %ZIP%
echo.
echo To PUBLISH the GitHub release (creates a public release - run manually):
echo   gh release create v%VERSION% "%ZIP%" --title "v%VERSION%" --notes "What changed in this version"
echo.
pause
