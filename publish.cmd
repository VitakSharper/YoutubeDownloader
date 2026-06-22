@echo off
setlocal
cd /d "%~dp0"
echo Publishing Liudochka Youtube Downloader (self-contained single exe, win-x64)...
echo.
dotnet publish "YoutubeDownloader.App\YoutubeDownloader.App.csproj" -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=none -p:DebugSymbols=false -o "dist"
echo.
echo Done. Single exe is at:
echo   %~dp0dist\LiudochkaYoutubeDownloader.exe
echo.
pause
