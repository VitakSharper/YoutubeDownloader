# Native-Opus Audio Option — Design

**Date:** 2026-07-13
**Status:** Approved
**Component:** YoutubeDownloader Core (`DownloadMode`, `VideoInfo`, `StreamSelector`, `FFmpegArguments`, `IMediaConverter`/`FFmpegMediaConverter`, `MainViewModel`) + Avalonia UI (`DownloadModeDisplayConverter`, `MainWindow.axaml`)

## Goal

Add a third download format — **"Opus audio"** — alongside the existing "MP3 audio"
and "Video (MP4)". When chosen, the app downloads YouTube's **native Opus**
audio-only stream and saves it straight to a `.opus` file with **no re-encode**
(`ffmpeg -c:a copy`). The user chooses between the current MP3 path and the new
Opus path from the same format dropdown.

## Background

YouTube already serves its audio-only streams as **Opus** (in a WebM container).
The current audio path (`DownloadMode.AudioMp3`) takes that native Opus stream
and transcodes it *down* to MP3 via `libmp3lame` — a lossy→lossy step that
degrades quality and costs CPU time. Saving the native Opus with a stream-copy
remux is therefore **higher fidelity and near-instant**: there is no second
lossy pass and no bitrate to choose (the bitrate is whatever YouTube provides,
typically ~130–160 kbps VBR).

A remux is still required (not a plain file rename) because the downloaded
stream sits in a WebM container; `ffmpeg -c:a copy` re-wraps the same Opus bytes
into an Ogg-Opus `.opus` file without touching the audio.

This feature is **additive**: the MP3 (`libmp3lame`) and Video (mux w/ AAC)
paths are unchanged.

## Scope

**In scope**
- New `DownloadMode.AudioOpus` value.
- Select the highest-bitrate **Opus** audio-only stream, download it, remux
  (`-c:a copy`) to a `.opus` file.
- Avalonia UI: pretty label "Opus audio" and hide the bitrate box when Opus is
  selected.
- Clear, graceful handling when a video has no Opus stream.
- Unit tests mirroring existing xUnit/Moq style.

**Out of scope (explicitly)**
- Re-encoding to Opus / an Opus bitrate selector (stream-copy has no bitrate knob).
- Any change to the MP3 or Video paths.
- WPF (`YoutubeDownloader.App`) label + visibility polish — the new mode still
  appears there functionally via shared Core, but its dropdown shows the raw
  enum name (`AudioOpus`) and its bitrate box does not hide. (Per decision.)
- The video-quality combo's pre-existing "always visible" quirk — left as-is.

## Design

### Core (shared by both UIs)

**`Models/DownloadMode.cs`**
```csharp
public enum DownloadMode
{
    AudioMp3,
    AudioOpus,
    Video
}
```

**`Models/VideoInfo.cs`** — add a trailing nullable parameter with a default so
existing call sites keep compiling:
```csharp
public sealed record VideoInfo(
    string Title,
    string Author,
    TimeSpan Duration,
    string ThumbnailUrl,
    AudioStreamOption BestAudio,
    IReadOnlyList<VideoStreamOption> VideoOptions,
    AudioStreamOption? BestOpusAudio = null)
{
    public int SourceAudioKbps => BestAudio.BitrateKbps;
}
```
`BestOpusAudio` is the highest-bitrate audio stream whose codec is Opus, or
`null` when the video offers none.

**`Helpers/StreamSelector.cs`** — new selector that returns `null` rather than
throwing (MP3/Video still work without an Opus stream):
```csharp
public static AudioStreamOption? PickBestOpusAudio(IEnumerable<AudioStreamOption> audio)
    => audio
        .Where(a => a.Codec.Contains("opus", StringComparison.OrdinalIgnoreCase))
        .OrderByDescending(a => a.BitrateKbps)
        .FirstOrDefault();
```

**`Services/YoutubeExplodeService.cs`** — populate `BestOpusAudio` from the same
`audioOptions` list already built, and pass it into the `VideoInfo`
constructor.

**`Ffmpeg/FFmpegArguments.cs`**
```csharp
public static string ForRemuxOpus(string input, string output)
    => $"-y -i \"{input}\" -vn -c:a copy \"{output}\"";
```

**`Services/IMediaConverter.cs` + `Services/FFmpegMediaConverter.cs`** — add:
```csharp
Task RemuxToOpusAsync(string ffmpegPath, string input, string output,
    TimeSpan duration, IProgress<double>? progress, CancellationToken ct);
```
The `FFmpegMediaConverter` implementation delegates to the existing
`RunAsync(...)` with `FFmpegArguments.ForRemuxOpus(input, output)`, reusing the
current progress-parsing and process-tree cancellation plumbing. (A stream copy
completes almost instantly, so progress simply jumps to done.)

**`ViewModels/MainViewModel.cs`** — `DownloadAsync` becomes a 3-way decision on
`SelectedMode` instead of the current `isAudio` boolean:

- Extension map: `AudioMp3 → "mp3"`, `AudioOpus → "opus"`, `Video → "mp4"`.
- **Opus guard, before the save prompt:**
  ```csharp
  if (SelectedMode == DownloadMode.AudioOpus && info.BestOpusAudio is null)
  {
      StatusMessage = "This video has no Opus audio stream to save.";
      return;
  }
  ```
- Opus branch (parallels the MP3 branch's progress weighting):
  ```csharp
  StatusMessage = "Downloading audio…";
  var tempAudio = _temp.NewTempFile(info.BestOpusAudio!.Container);
  tempFiles.Add(tempAudio);
  await _youtube.DownloadStreamAsync(info.BestOpusAudio.Source, tempAudio,
      new Progress<double>(p => Progress = 0.1 + p * 0.6), token);

  StatusMessage = "Saving Opus audio…";
  await _converter.RemuxToOpusAsync(ffmpegPath, tempAudio, target,
      info.Duration, new Progress<double>(p => Progress = 0.7 + p * 0.3), token);
  formatLabel = $"Opus · {info.BestOpusAudio.BitrateKbps} kbps";
  ```
- `UpscaleWarning` stays gated to `AudioMp3` — no bitrate warning for Opus.

### Avalonia UI only

**`Converters/DownloadModeDisplayConverter.cs`** — add the arm:
```csharp
DownloadMode.AudioOpus => "Opus audio",
```

**`ViewModels/MainViewModel.cs`** — computed visibility flag, notified when the
mode changes:
```csharp
public bool ShowBitrate => SelectedMode != DownloadMode.AudioOpus;
```
Add `[NotifyPropertyChangedFor(nameof(ShowBitrate))]` to the existing
`SelectedMode` observable property.

**`Views/MainWindow.axaml`** — bind the "MP3 BITRATE" label + its `ComboBox`
`IsVisible="{Binding ShowBitrate}"`. This hides the bitrate box **only** for
Opus; MP3 and Video behavior are byte-for-byte unchanged (Video mode continues
to show the box, matching today).

## Data Flow (Opus mode)

1. User fetches a video → `YoutubeExplodeService` sets `VideoInfo.BestOpusAudio`
   (or `null`).
2. User selects "Opus audio" → Avalonia hides the bitrate box (`ShowBitrate`).
3. User clicks Download → `DownloadAsync`:
   - If `BestOpusAudio is null` → status "This video has no Opus audio stream to
     save." and return (no save prompt).
   - Else prompt for a `.opus` save path.
4. Download `BestOpusAudio.Source` to a temp WebM file.
5. `RemuxToOpusAsync` copies the Opus stream into the target `.opus`.
6. History records `"Opus · {kbps} kbps"`; status "Done!".

## Error Handling

- **No Opus stream:** guarded up front with a clear status message and early
  return — never a generic failure, never a save prompt.
- **Cancellation:** the Opus branch threads the same `token` as MP3/Video, so
  the existing `catch (OperationCanceledException)` (status "Download
  cancelled.", progress reset, partial-file cleanup) applies unchanged.
- **FFmpeg failure:** surfaced by the existing generic handler in
  `DownloadAsync`; temp-file cleanup in `finally` runs on every path.

## Testing

- **`FFmpegArgumentsTests`** — `ForRemuxOpus` contains `-c:a copy`, `-vn`,
  quoted input/output paths, starts with `-y`, ends with the `.opus` output.
- **`StreamSelectorTests`** — `PickBestOpusAudio` picks the highest-bitrate
  opus stream, ignores aac/mp4a streams, and returns `null` when no opus stream
  is present.
- **`MainViewModelTests`** — with a `VideoInfo` carrying a `BestOpusAudio`:
  selecting `AudioOpus` calls `RemuxToOpusAsync` (and **not** `ConvertToMp3Async`
  / `MuxToMp4Async`) and records an "Opus" history label; with
  `BestOpusAudio == null`, download sets the "no Opus audio stream" status,
  returns early, and never prompts for a save path.
- **Manual (Avalonia):** verify the dropdown shows "Opus audio", the bitrate box
  hides when Opus is selected, and a real download produces a playable `.opus`.

## Files Touched

- `YoutubeDownloader.Core/Models/DownloadMode.cs`
- `YoutubeDownloader.Core/Models/VideoInfo.cs`
- `YoutubeDownloader.Core/Helpers/StreamSelector.cs`
- `YoutubeDownloader.Core/Services/YoutubeExplodeService.cs`
- `YoutubeDownloader.Core/Ffmpeg/FFmpegArguments.cs`
- `YoutubeDownloader.Core/Services/IMediaConverter.cs`
- `YoutubeDownloader.Core/Services/FFmpegMediaConverter.cs`
- `YoutubeDownloader.Core/ViewModels/MainViewModel.cs`
- `YoutubeDownloader.Avalonia/Converters/DownloadModeDisplayConverter.cs`
- `YoutubeDownloader.Avalonia/Views/MainWindow.axaml`
- `YoutubeDownloader.Core.Tests` (new Opus tests)
