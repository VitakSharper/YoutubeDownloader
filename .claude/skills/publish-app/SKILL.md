---
name: publish-app
description: Use when the user says "publish", or asks to package/release/ship the YoutubeDownloader (Liudochka) desktop app as a distributable. Produces ONE self-contained Windows .exe via dotnet publish — no .NET install needed on the target machine.
---

# Publish — single self-contained executable

## Overview
For this repo, **"publish" means: build `YoutubeDownloader.App` as ONE self-contained Windows executable** that runs without a .NET install. The deliverable is a single file: `LiudochkaYoutubeDownloader.exe`.

## Command
Run from the repo root (`C:\Repos\YoutubeDownloader`), single line:

```
dotnet publish YoutubeDownloader.App/YoutubeDownloader.App.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=none -p:DebugSymbols=false
```

## Why each flag
- `-r win-x64 --self-contained true` — bundle the .NET runtime so no install is needed on the target.
- `-p:PublishSingleFile=true` — emit a single `.exe`.
- `-p:IncludeNativeLibrariesForSelfExtract=true` — **required for WPF.** Bundles the native libs (`wpfgfx_cor3.dll`, `PresentationNative_cor3.dll`, `D3DCompiler_47_cor3.dll`, `PenImc_cor3.dll`, `vcruntime140_cor3.dll`) into the exe. Without it the publish folder ends up with **6 files instead of 1**.
- `-p:EnableCompressionInSingleFile=true` — compresses the bundle (final exe ≈ 69 MB).
- `-p:DebugType=none -p:DebugSymbols=false` — no stray `.pdb` left beside the exe.

## Output
```
YoutubeDownloader.App\bin\Release\net8.0-windows\win-x64\publish\LiudochkaYoutubeDownloader.exe
```

## After publishing — open the folder (always)
Once the build succeeds, open the publish folder in File Explorer so the user can grab the exe:

```
Start-Process explorer.exe -ArgumentList "C:\Repos\YoutubeDownloader\YoutubeDownloader.App\bin\Release\net8.0-windows\win-x64\publish"
```

(Use `Start-Process` rather than bare `explorer.exe` — Explorer returns a non-zero exit code even on success, which otherwise reads as a failed command.)

## Verify it really is single-file
After publishing, the `publish` folder must contain **exactly one file** (the `.exe`). If you also see `*_cor3.dll` files, `IncludeNativeLibrariesForSelfExtract` was missing — clear the folder and rebuild.

## Gotchas
- **"Device or resource busy" / "in use" when cleaning the publish folder:** caused by a shell whose working directory is inside that folder (or the app still running). Never `cd` into the publish folder before deleting it — delete it from the repo root, or use a fresh shell / `Remove-Item -Recurse -Force` from PowerShell.
- First launch of the single-file exe self-extracts the native libs to a temp dir — this is normal for single-file WPF and happens only once per version.
