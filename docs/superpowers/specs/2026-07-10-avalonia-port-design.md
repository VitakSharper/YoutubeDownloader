# Design — Avalonia UI port of Liudochka Youtube Downloader

**Date:** 2026-07-10
**Status:** Approved for planning
**Author:** Victor + Claude

## Goal

Clone the existing WPF app (`YoutubeDownloader.App`) to a new **Avalonia UI** front-end with a custom, modern UI, reusing the existing UI-agnostic `YoutubeDownloader.Core` project. The WPF app stays intact.

## Decisions (locked)

| Topic | Decision |
|---|---|
| Structure | **New `YoutubeDownloader.Avalonia` project** alongside `.App`; both reference `Core`. WPF is not removed. |
| Platforms | **Windows only** (Avalonia app targets `net8.0`; simplest service impls). |
| UI direction | **Custom modern**, dark-first, my design (driven by the frontend-design skill). |
| Thumbnail | **Add** the video thumbnail to the info card (async-loaded from `VideoInfo.ThumbnailUrl`). |
| Branding | **Keep** the "Liudochka Youtube Downloader" name + app icon. |
| Git | Spec is written but **not committed** (personal repo, no ADO work item; user's git policy gates commits on a work item). |

## Current architecture (baseline)

- `YoutubeDownloader.Core` (`net8.0`) — UI-agnostic. Contains `MainViewModel` (CommunityToolkit.Mvvm, 433 lines), all service **interfaces** + platform-neutral implementations (`YoutubeExplodeService`, FFmpeg locator/converter, Xabe downloader, `JsonHistoryStore`, `JsonSettingsStore`, `TempFileService`), models, helpers. Uses `YoutubeExplode` + `Xabe.FFmpeg.Downloader`.
- `YoutubeDownloader.App` (`net8.0-windows`, WPF) — `App.xaml(.cs)` builds a `Microsoft.Extensions.DependencyInjection` container, `MainWindow.xaml(.cs)`, 3 `Visibility` converters, and 5 Windows service impls (`WpfClipboardService`, `SaveFileService`, `MessageBoxConfirmationService`, `ExplorerFileRevealer`, `ProcessLinkOpener`).
- `YoutubeDownloader.Core.Tests` (xUnit + Moq) — includes `MainViewModelTests` mocking the service interfaces.

## Approach

Add a new Avalonia "head" that reuses `Core`. Because `Core` already holds the ViewModel and every service is behind an interface, the port is: rebuild the view in AXAML, provide Avalonia/Windows implementations of the UI-facing service interfaces, replace the WPF converters, and wire DI — mirroring the WPF `App.xaml.cs` container.

*Alternative rejected:* convert `.App` in place — rejected because WPF must keep working.

## The async adaptation (only material `Core` change)

Three services do UI interaction. WPF's APIs are synchronous; Avalonia's equivalents are async-only.

- **`IClipboardService.GetText()` — stays synchronous.** A clipboard read is a passive "what is on the clipboard now" snapshot, so it can be cached. The Avalonia service reads `TopLevel.Clipboard.GetTextAsync()` on `Window.Activated` and caches the result; `GetText()` returns the cached snapshot. The Avalonia `MainWindow.Activated` handler `await`s the cache refresh **before** executing `CheckClipboardCommand`, preserving the WPF trigger semantics. **No `Core` change.**
- **`IConfirmationService.Confirm()` → `Task<bool> ConfirmAsync(string message, string title)`.** A confirmation must return a live user decision and cannot be cached.
- **`ISaveFileService.PromptForSavePath()` → `Task<string?> PromptForSavePathAsync(string suggestedFileName, string extension, string? initialDirectory)`.** A save picker must return a live path and cannot be cached.

### Blast radius (mechanical)

- `MainViewModel`:
  - Line ~257: `await _saveFile.PromptForSavePathAsync(suggested, ext, _appSettings.LastSaveFolder)` (already inside `async Task DownloadAsync`).
  - `CancelDownload` becomes `[RelayCommand(CanExecute = nameof(CanCancelDownload))] private async Task CancelDownloadAsync()` awaiting `ConfirmAsync`. **Generated command name stays `CancelDownloadCommand`** (CommunityToolkit strips the `Async` suffix), so existing WPF XAML bindings (`CancelDownloadCommand`) are unaffected.
- WPF impls: `SaveFileService.PromptForSavePathAsync` and `MessageBoxConfirmationService.ConfirmAsync` wrap the existing synchronous dialog result in `Task.FromResult(...)`. WPF keeps working unchanged in behavior.
- `Core.Tests` (`MainViewModelTests`): the `_confirm.Setup/Verify(c => c.Confirm(...))` and `_saveFile.Setup(s => s.PromptForSavePath(...))` lines change to the async members returning `Task.FromResult(...)` (~15 lines). Clipboard (`GetText`) setups are untouched.

Everything else in `Core` is reused as-is.

## New project: `YoutubeDownloader.Avalonia`

**Target:** `net8.0`, `OutputType=WinExe`. **Packages:** `Avalonia`, `Avalonia.Desktop`, `Avalonia.Themes.Fluent`, `Avalonia.Fonts.Inter`, `Microsoft.Extensions.DependencyInjection`, `Avalonia.Diagnostics` (Debug only). References `YoutubeDownloader.Core`. Keeps `AssemblyName=LiudochkaYoutubeDownloader.Avalonia` (distinct output so both apps can build), `app.ico`.

**Files / units (each has one clear purpose):**

- `Program.cs` — `BuildAvaloniaApp().StartWithClassicDesktopLifetime(args)`.
- `App.axaml` — Fluent theme, Inter font, app-wide styles/resources (accent brushes, control styles).
- `App.axaml.cs` — builds the DI container (mirrors WPF `App.xaml.cs`: same `baseDir`/`ffmpegDir` under `LocalApplicationData\YoutubeDownloader`, same singletons for `Core` services) but registers Avalonia service impls; on framework init, resolves `MainWindow` + `MainViewModel`, sets `DataContext`, assigns `desktop.MainWindow`.
- `Views/MainWindow.axaml(.cs)` — the UI (below). Code-behind: wire `Activated` → `await clipboard.RefreshAsync(); vm.CheckClipboardCommand.Execute(null)`; bind `Topmost` to `AlwaysOnTop`.
- `Views/ConfirmDialog.axaml(.cs)` — small owner-modal Yes/No window used by the confirmation service.
- `Services/AvaloniaClipboardService.cs` — `IClipboardService`; caches `TopLevel.Clipboard` text; `RefreshAsync()` + sync `GetText()`.
- `Services/AvaloniaSaveFileService.cs` — `ISaveFileService.PromptForSavePathAsync` via `IStorageProvider.SaveFilePickerAsync` (suggested name, extension filter, start folder).
- `Services/AvaloniaConfirmationService.cs` — `IConfirmationService.ConfirmAsync` via `ConfirmDialog.ShowDialog<bool>(owner)`.
- `Services/ExplorerFileRevealer.cs` — `IFileRevealer`; `explorer.exe /select,"<path>"`.
- `Services/ProcessLinkOpener.cs` — `ILinkOpener`; `Process.Start(new ProcessStartInfo(url){ UseShellExecute = true })`.
- `Converters/` — only if needed after moving to `IsVisible`/`!` bindings: a `NullToBool` / `StringNotEmptyToBool` `IValueConverter` returning `bool`. Prefer compiled bindings (`x:DataType`) + built-in `!`.
- `Imaging/ImageUrlLoader.cs` — attached property `ImageUrl` (string) that async-loads a `Bitmap` from an http(s) URL (HttpClient → stream → `Bitmap`), with a simple in-memory cache; used to bind the info-card thumbnail from `VideoInfo.ThumbnailUrl`. Fails silently to no image.

## UI specification (custom modern, dark-first)

Driven by the frontend-design skill during implementation; exact tokens finalized there, then verified by running the app.

- **Window:** ~720×640, `MinWidth 580 / MinHeight 560`, centered, resizable. Deep near-black background with subtly layered surfaces (base → card → hairline borders), not the old full rainbow gradient.
- **Accent:** one refined violet→magenta accent (nods to Liudochka identity), used only for the primary action button, progress fill, active tab, and focus rings.
- **Type:** Inter; tabular figures for the progress percentage; clear label/caption hierarchy.
- **Header (shared):** app icon + "**Liudochka** Youtube Downloader" wordmark + tagline; "Always on top" toggle bound to `AlwaysOnTop`.
- **Tabs:** segmented control bound to `SelectedTabIndex` — **Download** and **History**.
- **Download tab (feature parity):**
  - Clipboard-detected-link banner (visible when `DetectedClipboardUrl` non-empty) with **Use it** / dismiss.
  - URL field (`Url`, update on keystroke, disabled while `IsBusy`) + **Fetch** button (`FetchInfoCommand`).
  - Video info card (visible when `VideoInfo != null`): **thumbnail** (async from `ThumbnailUrl`) + title + author.
  - Options (visible when `VideoInfo != null`): **Format** (`Modes`/`SelectedMode`), **MP3 bitrate** (`Bitrates`/`SelectedBitrate`), **upscale warning** (visible when `UpscaleWarning != null`), **Video quality** (`VideoInfo.VideoOptions`/`SelectedVideoQuality`, `DisplayMemberPath=QualityLabel`).
  - Accent progress bar (`Progress`, 0..1) + status text (`StatusMessage`).
  - Primary button that morphs **Download** (`DownloadCommand`) ↔ **Cancel** (`CancelDownloadCommand`) driven by `IsDownloading` (Avalonia style selector on a `Classes` toggle).
- **History tab (feature parity):**
  - Search field (`SearchText`, update on keystroke) with placeholder + **Clear all** (`ClearHistoryCommand`).
  - Empty/no-results state (`HistoryPlaceholder`).
  - Grouped list (`HistoryGroups` → `Header` + `Entries`); each entry card shows title, format chip, time, url, and hover-revealed actions **Use** (`UseEntryCommand`), **Open** (`OpenInBrowserCommand`), **Folder** (`OpenFolderCommand`, visible when `FilePath` non-empty), **Remove** (`RemoveEntryCommand`). Item commands bound to the window `DataContext` (Avalonia: `$parent[Window].DataContext` / compiled-binding to root VM).

## Testing / verification

- `Core.Tests` must stay green after the async adaptation (`dotnet test`).
- Solution builds clean (add the Avalonia project to the `.sln`).
- **Manual end-to-end (Windows):** launch the Avalonia app; paste a real YouTube URL → Fetch (thumbnail + info appear); download once as **mp3** and once as **mp4** (FFmpeg auto-download on first run, progress advances, file saved); verify the **History** tab records entries and that Use/Open/Folder/Remove and Clear all work; verify the clipboard banner appears when a YouTube link is on the clipboard and the window is activated; verify Always-on-top.

## Out of scope

- Cross-platform (macOS/Linux) service impls — Windows only for now (structure leaves room to add later).
- Any change to `Core` business logic beyond the async adaptation of the two dialog interfaces.
- Removing or modifying the WPF app.
