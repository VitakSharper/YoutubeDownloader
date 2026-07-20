# MP3 VBR quality option — design

**Date:** 2026-07-20
**Status:** Approved (pending spec review)

## Summary

Add a **best-quality VBR** option to the MP3 audio path and make it the default,
while keeping the existing fixed-bitrate (CBR) presets. Today the MP3 path always
uses CBR (`-b:a {n}k`, default 192 kbps). LAME's highest-quality mode is **VBR V0**
(`-q:a 0`, ~245 kbps average, 220–260 varying, peaking to 320) — same perceived
quality as 320 CBR at ~30% smaller size. This becomes the new default.

### Honest caveat (kept, not hidden)

YouTube's audio source is already lossy Opus (~128–160 kbps). Re-encoding it to any
higher MP3 bitrate — CBR *or* VBR V0 — does not recover quality that isn't in the
source; it only grows the file. VBR V0 is the best MP3 you can make from that source,
and the app's existing **Opus** mode (stream-copy, no re-encode) remains the true
highest-fidelity path. The existing "upscale" warning is preserved and now fires on
the VBR average too.

## Scope

This work has two phases:

- **Phase A — remove the legacy WPF app** (`YoutubeDownloader.App`) and make the
  Avalonia app the sole, shipping front-end (see "Phase A" below).
- **Phase B — the MP3 VBR feature** itself, now Avalonia-only.

**In scope:**
- Remove the WPF project; repoint the publish pipeline to Avalonia.
- New `Mp3Quality` model replacing the `Mp3Bitrate` enum.
- VBR + CBR FFmpeg argument builders.
- Converter, ViewModel, the Avalonia UI, README, and tests updated accordingly.

**Out of scope:**
- Any change to the Opus or Video paths.
- Version bump (optional follow-up).

## Phase A — remove the WPF app, keep only Avalonia

`YoutubeDownloader.App` (WPF, `net8.0-windows`) is the *original* front-end. The
Avalonia app is already the actively-developed, shipping UI: it has its own release
pipeline (`release.cmd` → `dist\LiudochkaYoutubeDownloader.Avalonia.exe` + a versioned
zip that feeds the in-app updater). Nothing in `YoutubeDownloader.Core` or
`YoutubeDownloader.Avalonia` depends on the WPF project. Remove it:

- **Solution:** delete the `YoutubeDownloader.App` project declaration and its
  `ProjectConfigurationPlatforms` block from `YoutubeDownloader.sln`.
- **Files:** delete the `YoutubeDownloader.App/` directory.
- **`publish.cmd`:** this script builds *only* the WPF app; Avalonia is built by
  `release.cmd`. Delete `publish.cmd`.
- **`publish-app` skill** (`.claude/skills/publish-app/SKILL.md`): today it documents
  the WPF `publish.cmd`. Rewrite it to describe the Avalonia release flow (`release.cmd`
  → `dist\LiudochkaYoutubeDownloader.Avalonia.exe`). Edit BOM-free; verify the
  documented command actually runs before finishing.
- **Assembly name — keep as-is.** Avalonia stays `LiudochkaYoutubeDownloader.Avalonia`
  (deliverable `LiudochkaYoutubeDownloader.Avalonia.exe`). This is the established
  released artifact name; the updater is name-agnostic so a rename buys nothing and
  only risks release continuity. (Renaming to drop the now-legacy `.Avalonia` suffix
  is a possible cosmetic follow-up, not part of this work.)
- **README:** drop the dual-UI / WPF references (intro, badges, requirements,
  getting-started, building, project structure, built-with) so it describes the
  Avalonia app only.

`YoutubeDownloader.Core`, `YoutubeDownloader.Core.Tests`, and
`YoutubeDownloader.Avalonia` are the surviving projects.

## Design

### 1. Model: `Mp3Quality` (replaces `Mp3Bitrate`)

`YoutubeDownloader.Core/Models/Mp3Quality.cs` — a sealed class exposing 5 singleton
presets. Each preset knows how to describe itself and how to be encoded:

| Field | Meaning |
|---|---|
| `Label` | Dropdown text (e.g. `"Best (VBR V0)"`, `"320 kbps"`). |
| `HistoryLabel` | Descriptor used in history: `"MP3 · {HistoryLabel}"`. |
| `IsVbr` | `true` → LAME VBR (`-q:a`); `false` → CBR (`-b:a`). |
| `VbrQuality` | LAME VBR level (0 = best). Meaningful only when `IsVbr`. |
| `ApproxKbps` | Target kbps for CBR; approximate average for VBR. Drives the upscale warning. |

Presets (dropdown order, best first) — `default = BestVbr`:

| Const | Label | HistoryLabel | IsVbr | VbrQuality | ApproxKbps |
|---|---|---|---|---|---|
| `BestVbr` | `Best (VBR V0)` | `VBR V0 (~245 kbps)` | true | 0 | 245 |
| `Cbr320` | `320 kbps` | `320 kbps` | false | — | 320 |
| `Cbr256` | `256 kbps` | `256 kbps` | false | — | 256 |
| `Cbr192` | `192 kbps` | `192 kbps` | false | — | 192 |
| `Cbr128` | `128 kbps` | `128 kbps` | false | — | 128 |

Also exposes `static IReadOnlyList<Mp3Quality> All` (the 5 above, in order) and
overrides `ToString() => Label` (a harmless safety net for any untemplated display).
Presets are singletons, so `SelectedQuality` matches a list item by reference — no
equality overrides needed.

### 2. FFmpeg arguments

`YoutubeDownloader.Core/Ffmpeg/FFmpegArguments.cs` — replace `ForMp3` with two
explicit builders (dumb string builders, easy to unit-test with primitives):

```csharp
public static string ForMp3Cbr(string input, string output, int bitrateKbps)
    => $"-y -i \"{input}\" -vn -c:a libmp3lame -b:a {bitrateKbps}k \"{output}\"";

public static string ForMp3Vbr(string input, string output, int vbrQuality)
    => $"-y -i \"{input}\" -vn -c:a libmp3lame -q:a {vbrQuality} \"{output}\"";
```

`ForMuxMp4` and `ForRemuxOpus` are unchanged.

### 3. Converter

`IMediaConverter.ConvertToMp3Async`'s `int bitrateKbps` parameter becomes
`Mp3Quality quality`. `FFmpegMediaConverter` selects the builder:

```csharp
public Task ConvertToMp3Async(string ffmpegPath, string input, string output, Mp3Quality quality,
    TimeSpan duration, IProgress<double>? progress, CancellationToken ct)
    => RunAsync(ffmpegPath,
        quality.IsVbr
            ? FFmpegArguments.ForMp3Vbr(input, output, quality.VbrQuality)
            : FFmpegArguments.ForMp3Cbr(input, output, quality.ApproxKbps),
        duration, progress, ct);
```

### 4. ViewModel (`MainViewModel`)

- Rename `SelectedBitrate → SelectedQuality` (type `Mp3Quality`, default `Mp3Quality.BestVbr`),
  `Bitrates → Qualities` (= `Mp3Quality.All`), `ShowBitrate → ShowQuality`.
- `[NotifyPropertyChangedFor(nameof(UpscaleWarning))]` stays on the renamed field.
- **Snapshot `SelectedQuality` at download start**, alongside the existing
  `var mode = SelectedMode;` snapshot, to avoid a mid-flight race:
  ```csharp
  var quality = SelectedQuality;
  ```
- MP3 branch:
  ```csharp
  StatusMessage = $"Converting to MP3 ({quality.Label})…";
  await _converter.ConvertToMp3Async(ffmpegPath, tempAudio, target, quality,
      info.Duration, new Progress<double>(p => Progress = 0.7 + p * 0.3), token);
  formatLabel = $"MP3 · {quality.HistoryLabel}";
  ```
- Upscale warning:
  ```csharp
  public string? UpscaleWarning =>
      VideoInfo is not null
      && SelectedMode == DownloadMode.AudioMp3
      && QualityAdvisor.WouldUpscale(VideoInfo.SourceAudioKbps, SelectedQuality.ApproxKbps)
          ? $"Selected {SelectedQuality.Label} is higher than the source audio " +
            $"({VideoInfo.SourceAudioKbps} kbps); this won't add real quality."
          : null;
  ```

### 5. UI (Avalonia — the sole app after Phase A)

`Views/MainWindow.axaml`:
- Combo `ItemsSource="{Binding Qualities}"`, `SelectedItem="{Binding SelectedQuality}"`,
  `IsVisible="{Binding ShowQuality}"` (both the label and the combo).
- Item template: `x:DataType="models:Mp3Quality"`, `<TextBlock Text="{Binding Label}" />`.
  (Avalonia uses compiled bindings — `AvaloniaUseCompiledBindingsByDefault=true` — so
  the `x:DataType` and `Label` binding are compile-checked.)
- Relabel `"MP3 BITRATE"` → `"MP3 QUALITY"`.
- **Delete** `Converters/Mp3BitrateDisplayConverter.cs` and its `App.axaml` resource
  (`<conv:Mp3BitrateDisplayConverter x:Key="BitrateDisplay" />`) — now redundant.

### 6. README

Update the audio bullet: MP3 now defaults to best-quality VBR with fixed-bitrate
presets still available.

## Testing

TDD — write/adjust tests first where practical.

**`FFmpegArgumentsTests`:**
- Rename the MP3 test to `ForMp3Cbr_...`, asserting `-c:a libmp3lame` + `-b:a 192k`.
- **Add** `ForMp3Vbr_BuildsLameVbrArgs`: `ForMp3Vbr(in, out, 0)` contains
  `-c:a libmp3lame` + `-q:a 0`, quoted paths, and **does not** contain `-b:a`.

**`FFmpegMediaConverterTests`:**
- `ConvertToMp3Async(..., Mp3Quality.Cbr192, ...)` instead of `192`.

**`MainViewModelTests`:**
- Replace every `Mp3Bitrate.KbpsNNN` with `Mp3Quality.CbrNNN`; `It.IsAny<int>()` on
  the convert verify → `It.IsAny<Mp3Quality>()`.
- `Download_AudioMode_ConvertsWithSelectedBitrate`: set `Cbr256`, verify the call
  receives `Mp3Quality.Cbr256`.
- `Download_AudioMode_RecordsHistoryEntry`: `Cbr256` → still `"MP3 · 256 kbps"`.
- Rename `ShowBitrate_HiddenOnlyForOpusMode` → `ShowQuality_...` using `vm.ShowQuality`.
- **Add** `SelectedQuality_DefaultsToBestVbr`.
- **Add** `Download_DefaultVbr_ConvertsWithVbrAndRecordsVbrLabel`: default quality →
  `ConvertToMp3Async` receives `Mp3Quality.BestVbr`; history `"MP3 · VBR V0 (~245 kbps)"`.

**Unaffected:** `QualityAdvisorTests` (still int-based), `JsonHistoryStoreTests`
(format strings are opaque round-trip data).

## Files touched

**Phase A (remove WPF, promote Avalonia):**
- `YoutubeDownloader.sln` — remove the WPF project declaration + config block.
- `YoutubeDownloader.App/` — **deleted** (whole directory).
- `publish.cmd` — **deleted** (WPF-only; Avalonia uses `release.cmd`).
- `.claude/skills/publish-app/SKILL.md` — rewrite for the Avalonia `release.cmd` flow (BOM-free, via Edit; verify before finishing).
- `README.md` — drop dual-UI/WPF references → Avalonia-only.
- *(Avalonia `<AssemblyName>` left unchanged — no rename.)*

**Phase B (MP3 VBR feature):**
- `YoutubeDownloader.Core/Models/Mp3Quality.cs` — **new** (replaces `Mp3Bitrate.cs`).
- `YoutubeDownloader.Core/Models/Mp3Bitrate.cs` — **deleted**.
- `YoutubeDownloader.Core/Ffmpeg/FFmpegArguments.cs`
- `YoutubeDownloader.Core/Services/IMediaConverter.cs`
- `YoutubeDownloader.Core/Services/FFmpegMediaConverter.cs`
- `YoutubeDownloader.Core/ViewModels/MainViewModel.cs`
- `YoutubeDownloader.Avalonia/Views/MainWindow.axaml`
- `YoutubeDownloader.Avalonia/App.axaml`
- `YoutubeDownloader.Avalonia/Converters/Mp3BitrateDisplayConverter.cs` — **deleted**.
- `YoutubeDownloader.Core.Tests/FFmpegArgumentsTests.cs`
- `YoutubeDownloader.Core.Tests/FFmpegMediaConverterTests.cs`
- `YoutubeDownloader.Core.Tests/MainViewModelTests.cs`
- `README.md` (audio bullet)
