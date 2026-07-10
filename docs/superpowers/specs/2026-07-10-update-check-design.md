# Design — Manual "Check for updates" (Avalonia app)

**Date:** 2026-07-10
**Status:** Approved (design)
**Branch:** `feat/avalonia-update-check` (off `feat/avalonia-ui-port` @ 454620f)
**Author:** Victor + Claude

## Context & goal

The Avalonia app ships as a self-contained single-file `.exe` with no installer, so
there is no built-in way for a user to learn a newer build exists. Add a **manual**
"Check for updates" action: the user clicks a button, the app asks GitHub whether a
newer release exists, and — if so — downloads the new build and reveals it so the
user can replace the running `.exe`.

## Decisions (settled in brainstorming)

- **Distribution channel: GitHub Releases.** Each version is a GitHub Release with a
  SemVer tag (`v1.1.0`) and the zipped single-file exe attached as an asset. Chosen
  over committing zips to a git branch (no repo bloat; free release notes).
- **Apply strategy: download + reveal.** The app downloads the zip, extracts the new
  `.exe`, opens the containing folder in Explorer, and instructs the user to close the
  app and replace the old `.exe`. No in-place self-replace (a running single-file exe
  cannot overwrite itself without a fragile helper-process dance; explicitly out of scope).
- **Scope: Avalonia app only.** The WPF app (`YoutubeDownloader.App`) and the shared
  `MainViewModel` are **not** modified. New logic lives in `Core`; only the Avalonia app
  surfaces the UI and registers the new services.
- **Auth: none.** The repo `github.com/VitakSharper/YoutubeDownloader` is public, so the
  app uses the unauthenticated GitHub REST API — **no token is shipped in the binary.**
- **Versioning: SemVer** via `<Version>` in the Avalonia `.csproj` (baseline `1.0.0`).
- **Trigger: manual only** — a button. No automatic check on startup or in the background.

## Architecture & components

### New in `YoutubeDownloader.Core` (UI-agnostic, unit-testable)

**`Models/UpdateInfo.cs`** — result record:
```
CurrentVersion : Version
LatestVersion  : Version
IsUpdateAvailable : bool        // LatestVersion > CurrentVersion
DownloadUrl    : string?        // asset browser_download_url (null if none)
AssetName      : string?
ReleaseUrl     : string?        // html_url of the release (for "view notes")
ReleaseNotes   : string?        // release body (optional display)
```
Plus a small `UpdateOptions` (Owner, Repo, CurrentVersion) so the service is not coupled
to the running assembly and is trivially testable.

**`Services/IUpdateService.cs`**
```
Task<UpdateInfo> CheckAsync(CancellationToken ct);
Task<string> DownloadAndExtractAsync(UpdateInfo info, IProgress<double>? progress, CancellationToken ct);
    // returns the full path of the extracted .exe
```

**`Services/GitHubUpdateService.cs`** — implements `IUpdateService` using an injected
`HttpClient` + `UpdateOptions`:
- `CheckAsync`: `GET https://api.github.com/repos/{Owner}/{Repo}/releases/latest` with
  headers `User-Agent: LiudochkaYoutubeDownloader` (GitHub requires a UA) and
  `Accept: application/vnd.github+json`. Parse `tag_name`, `html_url`, `body`, and the
  first asset whose name ends in `.zip` (→ `browser_download_url`). Version parsing:
  strip a leading `v`/`V`, `Version.TryParse`; `IsUpdateAvailable = latest > current`.
- `DownloadAndExtractAsync`: download the asset zip to
  `%USERPROFILE%\Downloads\Liudochka-<latest>\` (fallback to temp if Downloads is
  missing), report progress via `IProgress<double>` (mirrors `IFFmpegBinaryDownloader`),
  `ZipFile.ExtractToDirectory(..., overwrite:true)`, return the extracted `.exe` path.

### Version-compare helper

`Helpers/SemVerComparer.cs` (or a static method) — pure function
`TryParseTag(string tag, out Version)` handling the `v` prefix and malformed input;
isolated so it is unit-tested directly.

### New in `Core` — `ViewModels/UpdateViewModel.cs` (CommunityToolkit MVVM)

A dedicated VM (NOT `MainViewModel`, to keep the shared VM and WPF untouched):
- `[ObservableProperty] string status` — user-facing message.
- `[ObservableProperty] bool isUpdateAvailable`
- `[ObservableProperty] bool isBusy` — disables buttons; gates commands via
  `CanExecute`.
- `[ObservableProperty] double progress`
- `[RelayCommand] CheckForUpdatesAsync()` → `IUpdateService.CheckAsync`; sets Status
  ("You're on the latest version (1.0.0)" / "Version 1.1.0 is available"),
  `IsUpdateAvailable`, keeps the resulting `UpdateInfo`.
- `[RelayCommand] DownloadUpdateAsync()` (enabled only when `IsUpdateAvailable`) →
  `DownloadAndExtractAsync` (progress) → `IFileRevealer.RevealInFolder(newExe)` →
  Status "Downloaded to … — close the app and replace the .exe."
- `[RelayCommand] ViewReleaseNotesAsync()` (optional) → `ILinkOpener.Open(ReleaseUrl)`.

Dependencies injected: `IUpdateService`, `IFileRevealer`, `ILinkOpener` (all already
exist in Core).

### Avalonia wiring only (no WPF, no `MainViewModel` change)

- `App.axaml.cs`: register `IUpdateService` → `GitHubUpdateService` (with a static
  `HttpClient` and `UpdateOptions` built from `Assembly.GetExecutingAssembly().GetName().Version`
  and the hard-coded owner/repo), and register `UpdateViewModel`.
- `MainWindow`: expose the `UpdateViewModel` (constructor-injected, held as a
  code-behind property, e.g. `#Root.UpdateVm`) and add a compact **"Check for updates"**
  link/button in the header near the always-on-top toggle. When `IsUpdateAvailable`,
  show a **Download** button + a small progress indicator. All bound to `UpdateViewModel`.
  No changes to existing `x:Name`s / `Classes` / bindings; reuse existing theme classes
  (`Button.chip`) for the new controls.
- `.csproj`: add `<Version>1.0.0</Version>`.

## Data flow

```
[Check for updates] click
   → UpdateViewModel.CheckForUpdatesAsync()
   → GitHubUpdateService.CheckAsync()  → GET releases/latest → UpdateInfo
   → Status set; if newer, IsUpdateAvailable = true, [Download] appears

[Download] click
   → UpdateViewModel.DownloadUpdateAsync()
   → GitHubUpdateService.DownloadAndExtractAsync(info, progress)
        → download zip → %Downloads%\Liudochka-<ver>\  → extract → new .exe path
   → IFileRevealer.RevealInFolder(newExe)   (opens Explorer, selects the file)
   → Status: "Downloaded — close the app and replace the old .exe."
```

## Error handling (all surfaced in `Status`; buttons disabled while `IsBusy`)

| Condition | Message |
|---|---|
| Network / DNS failure | "Couldn't reach GitHub. Check your connection." |
| HTTP 403 (rate limit) | "Update check is rate-limited — try again in a few minutes." |
| HTTP 404 (no releases) | "No releases have been published yet." |
| `latest <= current` | "You're on the latest version (X.Y.Z)." |
| Release has no `.zip` asset | "The latest release has no downloadable file." |
| Download / extract failure | "Download failed — please try again." |

Commands catch exceptions and set a friendly `Status`; they never throw to the UI. A
`CancellationToken` is threaded through both service methods (cancel-friendly for future
use; a hard cancel button is out of scope for v1).

## Versioning & release process ("the zip lives on GitHub")

- App version = `<Version>` in the Avalonia `.csproj`.
- A `release.cmd` at repo root (mirrors `publish.cmd`): publishes the single-file exe,
  zips it as `LiudochkaYoutubeDownloader.Avalonia-v<ver>.zip`, and documents the
  `gh release create v<ver> <zip> --title ... --notes ...` command.
- **⚠️ Outward-facing:** actually creating a public GitHub release is done ONLY on
  explicit user approval. The feature is complete without a release existing — but a
  release must exist for the "update available" path to fire against real data.

**Bootstrapping reality:** the first build that contains this button is the first that
can self-check. From that release onward, the app detects newer releases. To exercise
the "update available" path, publish two releases where the latest tag > the running
app's version (e.g. run a `1.0.0` build, publish `v1.1.0`).

## Testing strategy

**xUnit in `YoutubeDownloader.Core.Tests`:**
- `SemVerComparer` / tag parsing: `1.0.0` vs `1.1.0`, equal, older, `v`-prefix,
  malformed/empty → expected `IsUpdateAvailable` and parse success/failure.
- Release-JSON → `UpdateInfo`: feed sample GitHub `releases/latest` JSON (with/without a
  `.zip` asset) and assert parsed fields.
- `CheckAsync` via a stub `HttpMessageHandler` (canned 200 JSON, 404, 403, thrown
  `HttpRequestException`) → asserts correct `UpdateInfo` / friendly outcomes.
- `DownloadAndExtractAsync` extraction: point at a locally-built temp zip (stub handler
  serving the bytes) → asserts the `.exe` is extracted and the returned path exists.

**Manual smoke (Windows, can't run headlessly):** click Check with no release (404
message), publish a higher release and re-check (Download appears), Download →
Explorer opens on the extracted exe, replace + relaunch works.

## Out of scope (v1)

- In-place self-replace / auto-restart.
- Automatic/background update checks or a "remind me later" scheduler.
- Delta/patch updates, signature verification, channels (stable/beta).
- WPF app parity (logic lives in Core, so it can be added later with only DI + XAML).
