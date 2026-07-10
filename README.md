# 🎀 Liudochka Youtube Downloader

A small, friendly Windows desktop app for grabbing **audio and video from any YouTube link** — pick a format, pick a quality, and download. Built on .NET 8 with a frosted-glass cassette theme. Ships as an **Avalonia** app (the actively developed UI, with in-app updates) plus the original **WPF** build; both share the same core.

![.NET](https://img.shields.io/badge/.NET-8.0-512BD4)
![Platform](https://img.shields.io/badge/platform-Windows-0078D6)
![UI](https://img.shields.io/badge/UI-Avalonia%20%2B%20WPF-2C3E50)

![Liudochka Youtube Downloader](docs/screenshot.png)

## Features

- **Download audio or video** — save as **MP3** (selectable bitrate) or **video** (selectable quality).
- **Fetch first** — paste a link and see the title, author, and thumbnail before committing to a download.
- **Smart quality hints** — warns you when the requested quality would upscale beyond what the source offers.
- **Live progress** — a gradient progress bar tracks download + conversion.
- **Clipboard detection** — spots a YouTube link on your clipboard and offers to use it.
- **History** — every download is remembered, **grouped by date** and **searchable** by title or link. From any entry you can:
  - **Use** — drop the link back into the download box
  - **Open** — open the video in your browser
  - **Folder** — reveal the saved file in Explorer
  - **Remove** a single entry, or **Clear all**
- **Check for updates** *(Avalonia)* — a header button checks GitHub Releases and downloads the newest build for you to swap in.
- **Remembers your last save folder** so you're not re-navigating every time.
- **Always-on-top toggle** (on by default, persisted across restarts).
- **No manual FFmpeg setup** — the required FFmpeg binaries are downloaded automatically on first use.

## Requirements

- **Windows** — both UIs target Windows (WPF is `net8.0-windows`; the Avalonia build ships as a Windows x64 single file).
- **[.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)** — only needed to build/run from source. A published release is self-contained and needs no .NET install.

## Getting started (from source)

```sh
git clone https://github.com/VitakSharper/YoutubeDownloader.git
cd YoutubeDownloader

# Run the Avalonia app (primary UI)
dotnet run --project YoutubeDownloader.Avalonia

# …or the original WPF app
dotnet run --project YoutubeDownloader.App

# Run the tests
dotnet test
```

FFmpeg is fetched automatically into `%LocalAppData%\YoutubeDownloader\ffmpeg` the first time it's needed.

## Building a standalone release

A single self-contained `.exe` (no .NET runtime required on the target machine) can be produced with the included scripts:

```sh
release.cmd    # Avalonia build  -> dist\LiudochkaYoutubeDownloader.Avalonia.exe
publish.cmd    # WPF build       -> dist\LiudochkaYoutubeDownloader.exe
```

Each runs a Release publish for `win-x64` (single file, compressed) into `dist\`.

## In-app updates (Avalonia)

The Avalonia app has a manual **Check for updates** button in the header that queries this repo's GitHub Releases. To cut a release:

1. Bump `<Version>` in `YoutubeDownloader.Avalonia.csproj` (and `VERSION` in `release.cmd`).
2. Run `release.cmd` — it publishes the single-file exe and zips it to `dist\`.
3. Publish the release (public):
   `gh release create v<version> "dist\LiudochkaYoutubeDownloader.Avalonia-v<version>.zip" --title "v<version>" --notes "…"`

Users on an older build click **Check for updates** → **Download**; the app fetches the zip, extracts the new exe to their Downloads folder, and opens it in Explorer to swap in.

## Where your data lives

Everything is stored under `%LocalAppData%\YoutubeDownloader`:

| Path | Contents |
|------|----------|
| `ffmpeg\` | Auto-downloaded FFmpeg binaries |
| `history.json` | Your download history |
| `settings.json` | App settings (always-on-top, last save folder) |

Deleting that folder resets the app to a clean state.

## Project structure

```
YoutubeDownloader.sln
├─ YoutubeDownloader.Core/        Platform-agnostic logic (net8.0)
│  ├─ Services/                   YouTube access, FFmpeg, history, settings, updates, …
│  ├─ ViewModels/                 MVVM view models (CommunityToolkit.Mvvm)
│  ├─ Models/ · Helpers/ · Ffmpeg/
├─ YoutubeDownloader.Avalonia/    Avalonia UI (net8.0) — primary, in-app updates
├─ YoutubeDownloader.App/         WPF UI (net8.0-windows) — original
├─ YoutubeDownloader.Core.Tests/  Unit tests (xUnit + Moq)
├─ release.cmd                    Avalonia single-file release build
└─ publish.cmd                    WPF single-file release build
```

The UI is kept thin: all logic lives in `Core` behind interfaces and is wired up with `Microsoft.Extensions.DependencyInjection` in each app's composition root, which keeps the core testable and shared across both UIs.

## Built with

- [Avalonia](https://avaloniaui.net/) — cross-platform .NET UI (primary app)
- [YoutubeExplode](https://github.com/Tyrrrz/YoutubeExplode) — YouTube metadata and stream access
- [Xabe.FFmpeg](https://github.com/tomaszzmuda/Xabe.FFmpeg) — media conversion + automatic FFmpeg download
- [CommunityToolkit.Mvvm](https://learn.microsoft.com/dotnet/communitytoolkit/mvvm/) — MVVM helpers
- WPF on .NET 8 — original UI

## Disclaimer

This is a personal hobby project and is **not affiliated with YouTube or Google**. Please download only content you have the right to, and respect YouTube's Terms of Service and applicable copyright law.
