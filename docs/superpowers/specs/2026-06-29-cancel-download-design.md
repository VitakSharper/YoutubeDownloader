# Cancel an In-Progress Download — Design

**Date:** 2026-06-29
**Status:** Approved
**Component:** YoutubeDownloader WPF app — `MainViewModel` + `MainWindow.xaml`

## Goal

Let the user cancel a download while it is running. The Download button turns
into a Cancel button for the duration of the download; clicking it stops the
work, clears progress, and removes the partial output file.

## Background

The download pipeline already threads a `CancellationToken` through every
awaited call in `MainViewModel.DownloadAsync` — `IFFmpegLocator.EnsureAsync`,
both `IYouTubeService.DownloadStreamAsync` calls, and the
`ConvertToMp3Async` / `MuxToMp4Async` converter calls. Today each is passed
`CancellationToken.None`. This feature is therefore **wiring + UX**, not new
plumbing: supply a real token and add the UI to trigger cancellation.

`IsBusy` is set true during **both** Fetch and Download, so it cannot drive a
download-only toggle. A narrower `IsDownloading` flag is introduced for that.

## Scope

**In scope**
- Cancel a running download via the toggled button.
- On cancel: stop work, set status to "Download cancelled.", reset progress to 0,
  delete the partial output file at the save path, record no history entry.

**Out of scope (explicitly)**
- Cancelling the Fetch-info step.
- Pause / resume.
- Multiple concurrent downloads.

## Design

### ViewModel (`YoutubeDownloader.Core/ViewModels/MainViewModel.cs`)

**New state**
- `[ObservableProperty] private bool _isDownloading;`
  Annotated `[NotifyCanExecuteChangedFor(nameof(CancelDownloadCommand))]` so the
  cancel command's executability updates when the flag flips.
- `private CancellationTokenSource? _downloadCts;`
  Created per download, disposed and nulled in `finally`.

`IsBusy` is unchanged: it stays true during the download and continues to
disable Fetch and Download. `IsDownloading` is a narrower flag layered on top,
used only for the button toggle and to gate the cancel command.

**`DownloadAsync` changes**
- After the save-path prompt succeeds, inside the `try`:
  create `_downloadCts = new CancellationTokenSource()` and set
  `IsDownloading = true`.
- Replace the four `CancellationToken.None` arguments with `_downloadCts.Token`.
- Add a `catch (OperationCanceledException)` **before** the existing
  `catch (Exception ex)`:
  - `StatusMessage = "Download cancelled.";`
  - `Progress = 0;`
  - Delete the partial output file at `target` (best-effort `try/catch`,
    mirroring the existing temp-file cleanup).
  - Do **not** call `RecordHistory`.
- In `finally`:
  - `IsDownloading = false;`
  - Dispose `_downloadCts` and set it to `null`.
  - The existing temp-file cleanup loop stays as-is.

**New command**
```csharp
[RelayCommand(CanExecute = nameof(CanCancelDownload))]
private void CancelDownload() => _downloadCts?.Cancel();

private bool CanCancelDownload() => IsDownloading;
```

### UI (`YoutubeDownloader.App/MainWindow.xaml`, the Download button at ~line 311)

Keep a single button. A `Style` with a `DataTrigger` on `IsDownloading` swaps
its `Content` and `Command` while a download runs. **No new converter is
required.**

```xml
<Button Command="{Binding DownloadCommand}">
  <Button.Style>
    <Style TargetType="Button" BasedOn="{StaticResource ...existing base style if any}">
      <Setter Property="Content" Value="Download"/>
      <Style.Triggers>
        <DataTrigger Binding="{Binding IsDownloading}" Value="True">
          <Setter Property="Content" Value="Cancel"/>
          <Setter Property="Command" Value="{Binding CancelDownloadCommand}"/>
        </DataTrigger>
      </Style.Triggers>
    </Style>
  </Button.Style>
</Button>
```

Implementation note: the current button declares `Command` and `Content` as
inline attributes. Both move into the `Style` (default `Setter` + trigger
`Setter`) so the trigger can override them. Any existing styling / `BasedOn` and
other inline attributes on the button must be preserved.

## Data Flow

1. User clicks Download → `DownloadCommand` runs `DownloadAsync`.
2. Save path chosen → CTS created, `IsDownloading = true` → button shows
   "Cancel", now bound to `CancelDownloadCommand`.
3. User clicks Cancel → `_downloadCts.Cancel()` → the in-flight awaited call
   throws `OperationCanceledException`.
4. `catch (OperationCanceledException)` → status "Download cancelled.",
   progress 0, partial `target` deleted, no history entry.
5. `finally` → `IsDownloading = false` (button reverts to "Download"), CTS
   disposed, temp files cleaned.

## Error Handling

- `OperationCanceledException` is caught **before** the generic `Exception`
  handler so a cancel is never reported as a "Download failed" error.
- Partial-file deletion is best-effort: a missing or locked `target` must not
  surface an exception or disrupt the UI.
- Temp-file cleanup in `finally` is unchanged and runs on every path.

## Testing

- **ViewModel unit tests** (xUnit, in `YoutubeDownloader.Core.Tests`): use a fake `IYouTubeService` /
  `IMediaConverter` whose async method blocks until the passed token is
  cancelled. Start `DownloadAsync`, invoke `CancelDownloadCommand`, then assert:
  - `StatusMessage == "Download cancelled."`
  - `Progress == 0`
  - no new `History` entry was added
  - the target output file was deleted
  - `IsDownloading` is false after completion, and the CTS is disposed
  - `CanCancelDownload()` is true only while a download is in progress
- **XAML trigger**: verified manually (Download ↔ Cancel toggle).

## Files Touched

- `YoutubeDownloader.Core/ViewModels/MainViewModel.cs`
- `YoutubeDownloader.App/MainWindow.xaml` (Download button)
- `YoutubeDownloader.Core.Tests` (new cancel tests)
