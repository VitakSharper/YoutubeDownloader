# Clipboard YouTube-link Detection — Design

**Date:** 2026-06-23
**Status:** Approved (pending implementation plan)

## Summary

When the app window gains focus, check the Windows clipboard for a YouTube
link. If one is found, show a dismissible suggestion banner above the URL box:

> 📋 Found a YouTube link on your clipboard.  **[ Use it ]**  **✕**

- **Use it** — fills the URL box with the detected link, hides the banner, and
  immediately runs Fetch so the video info loads in one click.
- **✕** — hides the banner and suppresses *that same link* from reappearing.

This removes the manual paste step in the common flow of copying a link in the
browser and switching back to the app.

## Goals / Non-goals

**Goals**
- Detect a YouTube link on the clipboard when the window gains focus (and on
  first show / startup).
- Surface it non-destructively — never overwrite what the user has typed.
- One-click "use" that fills and fetches.
- Keep detection logic in `Core` and unit-testable.

**Non-goals**
- No continuous/background clipboard listening (no Win32 clipboard hook).
- No auto-fill without explicit user action.
- No playlist/multi-link handling (single video link only).

## Approach

Chosen: **clipboard access behind an interface in Core**, mirroring the
existing `ILinkOpener` → `ProcessLinkOpener` and `IFileRevealer` →
`ExplorerFileRevealer` pattern. Detection state and commands live on
`MainViewModel`; the WPF window triggers the check on its `Activated` event.

Rejected:
- *All in `MainWindow.xaml.cs` code-behind* — breaks the Core/App separation
  and makes detection untestable.
- *Continuous Win32 clipboard listener* — extra interop and edge cases;
  unnecessary given the focus-triggered behavior chosen.

## Components

### Core (`YoutubeDownloader.Core`, net8.0)

**`IClipboardService`** *(new, `Services/`)*

```csharp
public interface IClipboardService
{
    /// <summary>Current clipboard text, or null if empty / non-text / unavailable.</summary>
    string? GetText();
}
```

**`MainViewModel`** *(extended)*

New constructor dependency: `IClipboardService _clipboard`.

State:

```csharp
[ObservableProperty] private string? _detectedClipboardUrl;  // non-null ⇒ banner visible
private string? _dismissedVideoId;                           // last link used/dismissed
```

Commands:

```csharp
[RelayCommand] private void CheckClipboard();
[RelayCommand] private Task UseDetectedLinkAsync();
[RelayCommand] private void DismissDetectedLink();
```

`CheckClipboard` sets `DetectedClipboardUrl` to the raw clipboard text **only
if all** of the following hold, otherwise sets it to `null`:

1. `YouTubeUrlValidator.GetVideoId(text)` is non-null (valid YouTube link).
2. That video id ≠ `GetVideoId(Url)` (not the same video already in the box).
3. That video id ≠ `_dismissedVideoId`.
4. `!IsBusy` (no fetch/download in progress).

`UseDetectedLinkAsync`:
- If `DetectedClipboardUrl` is null, return.
- `Url = DetectedClipboardUrl;`
- `_dismissedVideoId = GetVideoId(DetectedClipboardUrl);` (so it won't re-show).
- `DetectedClipboardUrl = null;`
- If `FetchInfoCommand.CanExecute(null)`, `await FetchInfoCommand.ExecuteAsync(null);`

`DismissDetectedLink`:
- `_dismissedVideoId = GetVideoId(DetectedClipboardUrl);`
- `DetectedClipboardUrl = null;`

Comparison is by **video id** so `youtu.be/X` and `youtube.com/watch?v=X`
are treated as the same link.

### App (`YoutubeDownloader.App`, WPF)

**`WpfClipboardService : IClipboardService`** *(new, `Services/`)*

```csharp
public sealed class WpfClipboardService : IClipboardService
{
    public string? GetText()
    {
        try { return Clipboard.ContainsText() ? Clipboard.GetText() : null; }
        catch { return null; } // clipboard may be locked by another process
    }
}
```

**`App.xaml.cs`** — register the service:

```csharp
services.AddSingleton<IClipboardService, WpfClipboardService>();
```

**`MainWindow.xaml.cs`** — trigger the check on activation (fires on first show,
covering startup, and on every refocus):

```csharp
Activated += (_, _) => (DataContext as MainViewModel)?.CheckClipboardCommand.Execute(null);
```

**`MainWindow.xaml`** — add the banner above the URL row in the Download tab.
A new `RowDefinition Height="Auto"` is inserted at the top of the Download
grid and the existing rows shift down by one. The banner reuses the existing
`ChipButton` style and the existing null/empty→visibility converter:

```xml
<Border Grid.Row="0" Margin="0,0,0,12" CornerRadius="10" Padding="12,9"
        Background="#33A855F7" BorderBrush="#5CC4B5FD" BorderThickness="1"
        Visibility="{Binding DetectedClipboardUrl, Converter={StaticResource NonEmptyToVis}}">
    <Grid>
        <Grid.ColumnDefinitions>
            <ColumnDefinition Width="*" />
            <ColumnDefinition Width="Auto" />
            <ColumnDefinition Width="Auto" />
        </Grid.ColumnDefinitions>
        <TextBlock Grid.Column="0" VerticalAlignment="Center" Foreground="#EDE9FE"
                   FontSize="12.5" TextWrapping="Wrap"
                   Text="📋 Found a YouTube link on your clipboard." />
        <Button Grid.Column="1" Content="Use it" Style="{StaticResource ChipButton}"
                Command="{Binding UseDetectedLinkCommand}" />
        <Button Grid.Column="2" Content="✕" Style="{StaticResource ChipButton}"
                ToolTip="Dismiss" Command="{Binding DismissDetectedLinkCommand}" />
    </Grid>
</Border>
```

`NonEmptyToVis` is used (rather than `NullToVis`) because `DetectedClipboardUrl`
is a string; an empty string and null both correctly hide the banner.

## Data flow

```
copy link in browser ──▶ switch to app ──▶ Window.Activated
        │
        ▼
   CheckClipboard ──(valid, new, not dismissed, not busy)──▶ DetectedClipboardUrl set
        │                                                          │
        │                                                          ▼
        │                                                   banner appears
        ▼
  ┌─ Use it ──▶ Url = link; remember id; hide banner; Fetch ──▶ video info loads
  └─ ✕      ──▶ remember id; hide banner
```

A different copied link re-triggers the banner on the next focus; the same link
(used or dismissed) stays suppressed.

## Error handling & threading

- Clipboard reads are guarded; a locked clipboard or non-text content yields
  `null` → no banner, no exception.
- The clipboard is accessed only on the UI thread (the `Activated` handler runs
  on the dispatcher thread; `System.Windows.Clipboard` requires STA).
- No network call happens until the user clicks **Use it**; Fetch retains its
  existing try/catch error reporting via `StatusMessage`.

## Testing

`MainViewModel` unit tests (in `YoutubeDownloader.Core.Tests`) using a
`FakeClipboardService` whose text is settable, and the existing fakes/mocks for
the other view-model dependencies:

| Scenario | Expectation |
|----------|-------------|
| Valid new link on clipboard, idle, empty box | `CheckClipboard` sets `DetectedClipboardUrl` |
| Empty or non-YouTube clipboard text | `DetectedClipboardUrl` stays null |
| Clipboard link == video already in `Url` box | suppressed (null) |
| `IsBusy` is true | suppressed (null) |
| Dismiss, then same link still on clipboard | `CheckClipboard` does not re-show |
| Dismiss link A, then link B copied | banner shows for B |
| `UseDetectedLink` | sets `Url`, clears banner, triggers a fetch (fake YouTube service) |

## Files touched

- `YoutubeDownloader.Core/Services/IClipboardService.cs` *(new)*
- `YoutubeDownloader.Core/ViewModels/MainViewModel.cs` *(edit)*
- `YoutubeDownloader.App/Services/WpfClipboardService.cs` *(new)*
- `YoutubeDownloader.App/App.xaml.cs` *(edit — DI registration)*
- `YoutubeDownloader.App/MainWindow.xaml.cs` *(edit — Activated hook)*
- `YoutubeDownloader.App/MainWindow.xaml` *(edit — banner + row shift)*
- `YoutubeDownloader.Core.Tests/...` *(new/extended VM tests + FakeClipboardService)*
