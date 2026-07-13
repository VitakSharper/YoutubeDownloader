# History UX Improvements — Design

**Date:** 2026-07-13
**Status:** Approved
**Component:** YoutubeDownloader Core (`MainViewModel`, new `IFoobar2000Launcher`) + Avalonia UI (`MainWindow.axaml`, new `Foobar2000Launcher`, DI) + WPF DI (null launcher registration)

## Goal

Two small, independent improvements to the History tab:

1. **Confirm before clearing history** — "Clear all" asks a yes/no question first,
   instead of wiping history immediately.
2. **Open in foobar2000** — a per-entry button that opens the downloaded file in
   [foobar2000](https://www.foobar2000.org/). If foobar2000 is not installed, the
   button is visible but disabled, with a tooltip explaining why.

These are separate from the Opus-audio feature (see
`2026-07-13-opus-audio-option-design.md`) and from each other; they are grouped
here only because both touch the History tab.

## Background

- `MainViewModel.ClearHistory()` (line ~426) currently clears and saves with no
  prompt. An `IConfirmationService.ConfirmAsync(message, title)` already exists
  and is used by Cancel-download, so the confirm pattern is established and reused.
- History entries already render per-entry chip buttons (**Use / Open / Folder /
  ✕**) in `MainWindow.axaml` (~line 201). `Folder` is shown only when the entry
  has a `FilePath` (`NotEmpty` converter). The new foobar2000 chip follows the
  same visibility rule and sits between `Folder` and `✕`.
- Launching external programs already goes through thin platform services
  (`ILinkOpener`, `IFileRevealer`) with per-app Windows implementations. foobar2000
  gets the same treatment: a Core interface + an Avalonia Windows implementation.

## Scope

**In scope**
- Yes/no confirmation on **Clear all** (not on single-entry Remove).
- Per-entry "▶ foobar" chip, visible when the entry has a saved file.
- Detect foobar2000; enabled when found, disabled + tooltip when not.
- Avalonia UI only (matching the Opus decision). WPF gets a no-op launcher
  registration so the shared ViewModel still constructs, but shows no button.
- Unit tests for both behaviors.

**Out of scope (explicitly)**
- Confirmation on single-entry Remove (`✕`).
- WPF foobar2000 UI (button/detection) — inert there.
- Choosing a player other than foobar2000; enqueue-vs-play options; playlists.

## Design

### Feature 1 — Confirm before Clear all

**`ViewModels/MainViewModel.cs`** — make the command async and gate on a confirm,
mirroring `CancelDownload`:
```csharp
[RelayCommand]
private async Task ClearHistoryAsync()
{
    if (History.Count == 0)
        return;
    if (!await _confirm.ConfirmAsync(
            "Clear all download history? This can't be undone.", "Clear history?"))
        return;
    History.Clear();
    _history.Save(History);
}
```
CommunityToolkit generates the command as **`ClearHistoryCommand`** (the `Async`
suffix is stripped), so the existing Avalonia binding
`Command="{Binding ClearHistoryCommand}"` (MainWindow.axaml:165) is unchanged.
No XAML edit needed for this feature.

### Feature 2 — Open in foobar2000

**`Services/IFoobar2000Launcher.cs`** (new, in Core):
```csharp
namespace YoutubeDownloader.Core.Services;

/// <summary>Detects and launches foobar2000 to play a downloaded file.</summary>
public interface IFoobar2000Launcher
{
    /// <summary>True when a foobar2000 executable was located on this machine.</summary>
    bool IsInstalled { get; }

    /// <summary>Open the given file in foobar2000. No-op when not installed.</summary>
    void Open(string filePath);
}
```

**`Services/NullFoobar2000Launcher.cs`** (new, in Core) — null-object so the
shared ViewModel constructs on platforms/apps without a real implementation
(used by WPF):
```csharp
public sealed class NullFoobar2000Launcher : IFoobar2000Launcher
{
    public bool IsInstalled => false;
    public void Open(string filePath) { }
}
```

**`YoutubeDownloader.Avalonia/Services/Foobar2000Launcher.cs`** (new, Windows) —
resolves the exe path once and caches it; `IsInstalled` reflects whether it was
found:
- Detection order (first existing path wins):
  1. `App Paths\foobar2000.exe` default value under
     `HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\foobar2000.exe`,
     then the same under `HKLM`, checking both `RegistryView.Registry64` and
     `Registry32` (foobar2000 v2 is x64; v1 is x86 under WOW6432Node).
  2. Fallback probes: `%ProgramFiles%\foobar2000\foobar2000.exe`,
     `%ProgramFiles(x86)%\foobar2000\foobar2000.exe`.
- `Open(filePath)`:
  ```csharp
  Process.Start(new ProcessStartInfo(_exePath!, $"\"{filePath}\"") { UseShellExecute = true });
  ```
  Passing the media file as an argument makes foobar2000 open and play it.

Registry access needs `Microsoft.Win32.Registry`, which is **not** in the
Avalonia project's portable `net8.0` reference set — add the
`Microsoft.Win32.Registry` PackageReference to
`YoutubeDownloader.Avalonia.csproj`.

**`ViewModels/MainViewModel.cs`** — inject the launcher and expose state + command:
```csharp
private readonly IFoobar2000Launcher _foobar;
// …added as the last constructor parameter; assigned in the ctor…

public bool IsFoobarInstalled => _foobar.IsInstalled;
public string FoobarTooltip =>
    IsFoobarInstalled ? "Open in foobar2000" : "foobar2000 is not installed";

[RelayCommand]
private void OpenInFoobar(HistoryEntry? entry)
{
    if (entry is null || string.IsNullOrEmpty(entry.FilePath) || !_foobar.IsInstalled)
        return;
    try { _foobar.Open(entry.FilePath); }
    catch (Exception ex) { StatusMessage = $"Couldn't open foobar2000: {ex.Message}"; }
}
```
Adding a constructor parameter changes all three call sites: Avalonia DI, WPF DI,
and the test `CreateSut` helper (below).

**DI registrations**
- `YoutubeDownloader.Avalonia/App.axaml.cs`:
  `services.AddSingleton<IFoobar2000Launcher, Foobar2000Launcher>();`
- `YoutubeDownloader.App/App.xaml.cs`:
  `services.AddSingleton<IFoobar2000Launcher, NullFoobar2000Launcher>();`

**`Views/MainWindow.axaml`** — new chip between Folder and ✕ (~line 211),
matching the existing chip style. Visible when the entry has a file; enabled only
when foobar2000 is installed; tooltip explains the disabled state:
```xml
<Button Classes="chip" Content="▶ foobar"
        IsVisible="{Binding FilePath, Converter={StaticResource NotEmpty}}"
        IsEnabled="{Binding #Root.((vm:MainViewModel)DataContext).IsFoobarInstalled}"
        ToolTip.Tip="{Binding #Root.((vm:MainViewModel)DataContext).FoobarTooltip}"
        Command="{Binding #Root.((vm:MainViewModel)DataContext).OpenInFoobarCommand}"
        CommandParameter="{Binding}" />
```
(Avalonia shows `ToolTip.Tip` on disabled controls, so the "not installed" hint
works while the button is greyed out.)

## Data Flow

**Clear all:** click → `ClearHistoryCommand` → if empty, return → confirm dialog →
on Yes, clear + save; on No, nothing changes.

**Open in foobar2000:** app start → `Foobar2000Launcher` resolves the exe path
once → each history entry with a file shows the chip → enabled iff
`IsFoobarInstalled` → click → `_foobar.Open(entry.FilePath)` launches foobar2000
with the file; failure sets a status message.

## Error Handling

- Clear-all confirm: declining leaves history untouched; empty history returns
  before prompting.
- foobar2000 launch: wrapped in try/catch → a spawn failure sets `StatusMessage`
  rather than crashing (mirrors `OpenInBrowser`).
- Detection is defensive: any registry read that throws or returns nothing falls
  through to the Program Files probes; if nothing is found, `IsInstalled` is
  false and the button stays disabled.

## Testing

- **`MainViewModelTests`**
  - Add `Mock<IFoobar2000Launcher>` to the fixture and `CreateSut`.
  - **Clear all:** with entries and confirm → `true`, history is cleared and
    `_history.Save` called; with confirm → `false`, history is untouched and not
    saved; empty history → confirm never asked.
  - **Open in foobar2000:** installed → `_foobar.Open(entry.FilePath)` called once;
    not installed, or entry has no `FilePath`, or null entry → `Open` never called.
  - `IsFoobarInstalled` / `FoobarTooltip` reflect the mock's `IsInstalled`.
- **Manual (Avalonia):** with foobar2000 installed, the chip is enabled and opens
  a downloaded file; uninstalled (or simulated), the chip is greyed with the
  tooltip; "Clear all" shows the yes/no dialog and only clears on Yes.

## Files Touched

- `YoutubeDownloader.Core/ViewModels/MainViewModel.cs`
- `YoutubeDownloader.Core/Services/IFoobar2000Launcher.cs` (new)
- `YoutubeDownloader.Core/Services/NullFoobar2000Launcher.cs` (new)
- `YoutubeDownloader.Avalonia/Services/Foobar2000Launcher.cs` (new)
- `YoutubeDownloader.Avalonia/YoutubeDownloader.Avalonia.csproj` (Registry package)
- `YoutubeDownloader.Avalonia/App.axaml.cs` (DI)
- `YoutubeDownloader.Avalonia/Views/MainWindow.axaml` (foobar chip)
- `YoutubeDownloader.App/App.xaml.cs` (null launcher DI)
- `YoutubeDownloader.Core.Tests` (clear-confirm + foobar tests)
