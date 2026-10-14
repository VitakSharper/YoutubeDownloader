---
name: publish-app
description: Use when the user says "publish", or asks to package/release/ship the YoutubeDownloader (Liudochka) desktop app as a distributable. Produces ONE self-contained Windows .exe via dotnet publish — no .NET install needed on the target machine.
---

# Publish — single self-contained executable

## Overview
For this repo, **"publish" means: build `YoutubeDownloader.Avalonia` as ONE self-contained Windows executable** that runs without a .NET install. The canonical flow is the repo's `release.cmd`, which publishes and zips the single-file exe into `dist\`. The deliverable is `dist\LiudochkaYoutubeDownloader.Avalonia.exe` (and a versioned `.zip` the in-app updater consumes).

## Command
Run from the repo root (`C:\Repos\YoutubeDownloader`):

```
.\release.cmd
```

`release.cmd` runs, in effect, this single-line publish (bump `<Version>` in the csproj first — the script reads the version from it):

```
dotnet publish YoutubeDownloader.Avalonia/YoutubeDownloader.Avalonia.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=none -p:DebugSymbols=false -o dist
```

## Why each flag
- `-r win-x64 --self-contained true` — bundle the .NET runtime so no install is needed on the target.
- `-p:PublishSingleFile=true` — emit a single `.exe`.
- `-p:IncludeNativeLibrariesForSelfExtract=true` — bundle Avalonia's native libs (Skia/HarfBuzz: `libSkiaSharp.dll`, `libHarfBuzzSharp.dll`, `av_libglesv2.dll`) into the exe so the publish folder is a single file rather than several.
- `-p:EnableCompressionInSingleFile=true` — compresses the bundle.
- `-p:DebugType=none -p:DebugSymbols=false` — no stray `.pdb` beside the exe.

## Output
```
dist\LiudochkaYoutubeDownloader.Avalonia.exe
dist\LiudochkaYoutubeDownloader.Avalonia-v<version>.zip
```

## After publishing — open the folder (always)
```
Start-Process explorer.exe -ArgumentList "C:\Repos\YoutubeDownloader\dist"
```

(Use `Start-Process` rather than bare `explorer.exe` — Explorer returns a non-zero exit code even on success, which otherwise reads as a failed command.)

## Verify it really is single-file
After publishing, `dist\` must contain the one `.exe` (plus the release `.zip`). If you also see `libSkiaSharp.dll` / `libHarfBuzzSharp.dll` / `av_libglesv2.dll` beside the exe, `IncludeNativeLibrariesForSelfExtract` was missing — clear the folder and rebuild. If you see libSkiaSharp.pdb / libHarfBuzzSharp.pdb beside the exe, the csproj target ExcludeNativePdbsFromPublish is missing (SkiaSharp 3, from Avalonia 12, ships native .pdb files that DebugType=none does not suppress).

## Gotchas
- **"Device or resource busy" / "in use" when cleaning `dist\`:** caused by a shell whose working directory is inside that folder (or the app still running). Never `cd` into `dist\` before deleting it — delete it from the repo root with `Remove-Item -Recurse -Force dist`.
- First launch of the single-file exe self-extracts the native libs to a temp dir — normal for single-file, happens once per version.
- To cut a GitHub release, after `release.cmd`: `gh release create v<version> "dist\LiudochkaYoutubeDownloader.Avalonia-v<version>.zip" --title "v<version>" --notes "…"`.
