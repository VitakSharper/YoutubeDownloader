# Confirm Before Cancelling a Download — Design

**Date:** 2026-06-29
**Status:** Approved
**Component:** YoutubeDownloader WPF app — `MainViewModel` + new `IConfirmationService`

## Goal

When the user clicks **Cancel** during a download, ask for confirmation before
actually stopping. A native Yes/No prompt appears; **Yes** cancels the download,
**No** (the default) leaves it running untouched. This prevents losing download
progress to an accidental click.

## Background

The cancel feature (merged @ `6c57893`) wired a `CancellationTokenSource` into
`MainViewModel.DownloadAsync` and added `CancelDownload()`, which today calls
`_downloadCts?.Cancel()` immediately — a single click aborts the download with no
confirmation.

The app already uses a clean service-per-concern pattern: a focused interface in
`YoutubeDownloader.Core/Services` with a thin WPF implementation in
`YoutubeDownloader.App/Services`, injected into `MainViewModel` via DI and mocked
in `MainViewModel` unit tests. `ISaveFileService` (a WPF `SaveFileDialog` behind
`string? PromptForSavePath(...)`) is the direct precedent for a UI prompt. This
feature follows that pattern so the confirmation is mockable in tests.

## Scope

**In scope**
- A confirmation prompt when the user triggers cancel on an in-progress download.
- Native `MessageBox`, Yes/No, **No** as default button; Yes cancels, No continues.
- A reusable `IConfirmationService` abstraction + WPF implementation.

**Out of scope (explicitly)**
- Custom dialog window / button restyling (chose native; native buttons cannot be
  relabelled, so the labels are "Yes"/"No", not "Yes"/"Continue").
- Wording configuration or localization.
- Confirmation on any other action (fetch, clear history, etc.).

## Design

### New service (`YoutubeDownloader.Core/Services/IConfirmationService.cs`)

```csharp
namespace YoutubeDownloader.Core.Services;

public interface IConfirmationService
{
    /// <summary>Ask the user to confirm an action. Returns true if confirmed.</summary>
    bool Confirm(string message, string title);
}
```

### WPF implementation (`YoutubeDownloader.App/Services/MessageBoxConfirmationService.cs`)

```csharp
using System.Windows;
using YoutubeDownloader.Core.Services;

namespace YoutubeDownloader.App.Services;

public sealed class MessageBoxConfirmationService : IConfirmationService
{
    public bool Confirm(string message, string title) =>
        MessageBox.Show(message, title, MessageBoxButton.YesNo,
            MessageBoxImage.Warning, MessageBoxResult.No) == MessageBoxResult.Yes;
}
```

`MessageBoxResult.No` as the `defaultResult` makes No the focused button, so
pressing Enter or Esc keeps the download running.

### DI registration (`YoutubeDownloader.App/App.xaml.cs`)

Add alongside the other singletons (before `MainViewModel` is registered):

```csharp
services.AddSingleton<IConfirmationService, MessageBoxConfirmationService>();
```

### ViewModel (`YoutubeDownloader.Core/ViewModels/MainViewModel.cs`)

- Add `private readonly IConfirmationService _confirm;` and a constructor
  parameter `IConfirmationService confirm` (assigned in the ctor body). It is
  added to the existing constructor signature.
- Change `CancelDownload()`:

```csharp
[RelayCommand(CanExecute = nameof(CanCancelDownload))]
private void CancelDownload()
{
    if (_confirm.Confirm("Cancel the current download? Your progress will be lost.",
            "Cancel download?"))
        _downloadCts?.Cancel();
}
```

`CanCancelDownload()` (returns `IsDownloading`) is unchanged. `_downloadCts?.Cancel()`
remains a safe no-op if the download finished while the prompt was open
(`_downloadCts` is set to null in `DownloadAsync`'s `finally`).

## Data Flow

1. Download running → button shows "Cancel", bound to `CancelDownloadCommand`.
2. User clicks Cancel → `CancelDownload()` calls `_confirm.Confirm(...)`.
3. **Yes** → `_downloadCts.Cancel()` → in-flight awaited call throws
   `OperationCanceledException` → existing cancel handling (status "Download
   cancelled.", progress 0, partial file deleted, no history entry).
4. **No** → nothing happens; the download continues. (`MessageBox.Show` pumps the
   UI message loop, so download progress keeps updating while the prompt is open.)

## Error Handling

- The confirmation itself cannot throw in normal use; no extra handling needed.
- If the download completes while the prompt is open, a subsequent Yes is a no-op
  (null `_downloadCts`), so no `ObjectDisposedException` or crash.
- All existing cancel-path error handling in `DownloadAsync` is unchanged.

## Testing

- **ViewModel unit tests** (xUnit + Moq, in `YoutubeDownloader.Core.Tests`),
  reusing the existing fake `IYouTubeService` / `IMediaConverter` that block until
  the passed token is cancelled, plus a mocked `IConfirmationService`:
  - **Confirm returns true:** start `DownloadAsync`, invoke
    `CancelDownloadCommand`, assert the download ends cancelled
    (`StatusMessage == "Download cancelled."`, `Progress == 0`, no history entry).
  - **Confirm returns false:** start `DownloadAsync`, invoke
    `CancelDownloadCommand`, assert the token was **not** cancelled and the
    download runs to completion (status reports "Done!", history entry recorded).
  - Assert `IConfirmationService.Confirm` is invoked when Cancel is clicked.
- Only the shared `CreateSut()` factory changes to pass the new mock; the test
  fixture sets `Confirm(...)` to return **true by default** so the existing
  `Download_Cancelled_...` test (which expects cancellation) keeps passing. The
  "confirm returns false" test overrides that default locally.
- **MessageBox**: the thin WPF implementation is verified manually.

## Files Touched

- `YoutubeDownloader.Core/Services/IConfirmationService.cs` (new)
- `YoutubeDownloader.App/Services/MessageBoxConfirmationService.cs` (new)
- `YoutubeDownloader.App/App.xaml.cs` (DI registration)
- `YoutubeDownloader.Core/ViewModels/MainViewModel.cs` (ctor + `CancelDownload`)
- `YoutubeDownloader.Core.Tests` (new confirm tests; existing cancel tests updated)
