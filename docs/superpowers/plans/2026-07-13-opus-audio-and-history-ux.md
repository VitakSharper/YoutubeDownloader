# Opus Audio + History UX Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a native-Opus audio download option, a confirm-before-clear-all prompt, and a per-entry "Open in foobar2000" button.

**Architecture:** All logic lives in the shared `YoutubeDownloader.Core` project behind interfaces; the two UI apps (Avalonia = primary, WPF = original) wire it up in their own composition roots. Opus is a new `DownloadMode` whose download path stream-copies YouTube's native Opus into a `.opus` file (no re-encode). foobar2000 launching is a new platform service with a real Windows implementation in Avalonia and a no-op in Core (used by WPF).

**Tech Stack:** .NET 8, C#, CommunityToolkit.Mvvm (source-generated commands/properties), xUnit + Moq, YoutubeExplode, FFmpeg (via existing `FFmpegMediaConverter`), Avalonia 11, WPF.

## Global Constraints

- **Indentation:** 4 spaces (match existing files); nullable reference types enabled; implicit usings enabled.
- **Opus output:** stream-copy only (`ffmpeg -c:a copy`), never re-encode; file extension `.opus`.
- **foobar2000 real implementation:** Avalonia only. WPF registers the Core `NullFoobar2000Launcher`. The chip UI is Avalonia-only.
- **Clear-all confirmation:** reuse the existing `IConfirmationService.ConfirmAsync(message, title)`.
- **Command names:** CommunityToolkit strips the `Async` suffix — `ClearHistoryAsync` → `ClearHistoryCommand`. Keep XAML bindings matching generated names.
- **Registry dependency:** the Avalonia project's `net8.0` target needs the `Microsoft.Win32.Registry` NuGet package for App Paths lookup.
- **Test command:** `dotnet test YoutubeDownloader.Core.Tests` (add `--filter "FullyQualifiedName~<Name>"` for a single test). Build a UI app with `dotnet build YoutubeDownloader.Avalonia` / `dotnet build YoutubeDownloader.App`.
- **Out of scope:** version bump; re-encode/bitrate for Opus; WPF foobar UI; confirmation on single-entry Remove.

---

### Task 1: Opus remux FFmpeg argument

**Files:**
- Modify: `YoutubeDownloader.Core/Ffmpeg/FFmpegArguments.cs`
- Test: `YoutubeDownloader.Core.Tests/FFmpegArgumentsTests.cs`

**Interfaces:**
- Produces: `FFmpegArguments.ForRemuxOpus(string input, string output) : string`

- [ ] **Step 1: Write the failing test**

Add to `FFmpegArgumentsTests.cs`:

```csharp
    [Fact]
    public void ForRemuxOpus_CopiesAudioStreamWithoutReencode()
    {
        var args = FFmpegArguments.ForRemuxOpus(@"C:\in.webm", @"C:\out.opus");

        Assert.StartsWith("-y", args);
        Assert.Contains("-i \"C:\\in.webm\"", args);
        Assert.Contains("-vn", args);
        Assert.Contains("-c:a copy", args);
        Assert.DoesNotContain("libopus", args);
        Assert.EndsWith("\"C:\\out.opus\"", args);
    }
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test YoutubeDownloader.Core.Tests --filter "FullyQualifiedName~ForRemuxOpus_CopiesAudioStreamWithoutReencode"`
Expected: FAIL — compile error, `FFmpegArguments` has no `ForRemuxOpus`.

- [ ] **Step 3: Write minimal implementation**

Add to `FFmpegArguments.cs` (after `ForMuxMp4`):

```csharp
    public static string ForRemuxOpus(string input, string output)
        => $"-y -i \"{input}\" -vn -c:a copy \"{output}\"";
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test YoutubeDownloader.Core.Tests --filter "FullyQualifiedName~ForRemuxOpus_CopiesAudioStreamWithoutReencode"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add YoutubeDownloader.Core/Ffmpeg/FFmpegArguments.cs YoutubeDownloader.Core.Tests/FFmpegArgumentsTests.cs
git commit -m "feat(opus): add ForRemuxOpus stream-copy ffmpeg argument"
```

---

### Task 2: Best-Opus stream selector

**Files:**
- Modify: `YoutubeDownloader.Core/Helpers/StreamSelector.cs`
- Test: `YoutubeDownloader.Core.Tests/StreamSelectorTests.cs`

**Interfaces:**
- Consumes: `AudioStreamOption(string Codec, int BitrateKbps, string Container, IStreamInfo Source)` (existing).
- Produces: `StreamSelector.PickBestOpusAudio(IEnumerable<AudioStreamOption>) : AudioStreamOption?`

- [ ] **Step 1: Write the failing tests**

Add to `StreamSelectorTests.cs` (note the existing `Audio` helper defaults codec to `"aac"`; add an opus overload):

```csharp
    private static AudioStreamOption Opus(int kbps)
        => new("opus", kbps, "webm", Source: null!);

    [Fact]
    public void PickBestOpusAudio_ReturnsHighestBitrateOpus_IgnoringOtherCodecs()
    {
        var best = StreamSelector.PickBestOpusAudio(new[] { Audio(320), Opus(96), Opus(160), Audio(256) });
        Assert.NotNull(best);
        Assert.Equal(160, best!.BitrateKbps);
        Assert.Equal("opus", best.Codec);
    }

    [Fact]
    public void PickBestOpusAudio_ReturnsNull_WhenNoOpusStream()
    {
        var best = StreamSelector.PickBestOpusAudio(new[] { Audio(128), Audio(256) });
        Assert.Null(best);
    }
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test YoutubeDownloader.Core.Tests --filter "FullyQualifiedName~PickBestOpusAudio"`
Expected: FAIL — compile error, `StreamSelector` has no `PickBestOpusAudio`.

- [ ] **Step 3: Write minimal implementation**

Add to `StreamSelector.cs`:

```csharp
    public static AudioStreamOption? PickBestOpusAudio(IEnumerable<AudioStreamOption> audio)
        => audio
            .Where(a => a.Codec.Contains("opus", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(a => a.BitrateKbps)
            .FirstOrDefault();
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test YoutubeDownloader.Core.Tests --filter "FullyQualifiedName~PickBestOpusAudio"`
Expected: PASS (both).

- [ ] **Step 5: Commit**

```bash
git add YoutubeDownloader.Core/Helpers/StreamSelector.cs YoutubeDownloader.Core.Tests/StreamSelectorTests.cs
git commit -m "feat(opus): add PickBestOpusAudio stream selector"
```

---

### Task 3: Opus model + converter plumbing

**Files:**
- Modify: `YoutubeDownloader.Core/Models/DownloadMode.cs`
- Modify: `YoutubeDownloader.Core/Models/VideoInfo.cs`
- Modify: `YoutubeDownloader.Core/Services/YoutubeExplodeService.cs`
- Modify: `YoutubeDownloader.Core/Services/IMediaConverter.cs`
- Modify: `YoutubeDownloader.Core/Services/FFmpegMediaConverter.cs`
- Test: `YoutubeDownloader.Core.Tests/FFmpegMediaConverterTests.cs`

**Interfaces:**
- Consumes: `FFmpegArguments.ForRemuxOpus` (Task 1), `StreamSelector.PickBestOpusAudio` (Task 2).
- Produces:
  - `DownloadMode.AudioOpus` enum value.
  - `VideoInfo.BestOpusAudio : AudioStreamOption?` (trailing ctor param, default `null`).
  - `IMediaConverter.RemuxToOpusAsync(string ffmpegPath, string input, string output, TimeSpan duration, IProgress<double>? progress, CancellationToken ct) : Task`

- [ ] **Step 1: Write the failing test**

Add to `FFmpegMediaConverterTests.cs` (mirrors the existing invalid-path test):

```csharp
    [Fact]
    public async Task RemuxToOpusAsync_WhenFfmpegPathInvalid_Throws()
    {
        var converter = new FFmpegMediaConverter();
        var missing = Path.Combine(Path.GetTempPath(), "definitely-not-ffmpeg-" + Guid.NewGuid().ToString("N") + ".exe");

        await Assert.ThrowsAnyAsync<Exception>(() =>
            converter.RemuxToOpusAsync(missing, "in.webm", "out.opus", TimeSpan.FromSeconds(10), null, CancellationToken.None));
    }
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test YoutubeDownloader.Core.Tests --filter "FullyQualifiedName~RemuxToOpusAsync_WhenFfmpegPathInvalid_Throws"`
Expected: FAIL — compile error, `IMediaConverter`/`FFmpegMediaConverter` has no `RemuxToOpusAsync`.

- [ ] **Step 3a: Add the enum value**

Replace `Models/DownloadMode.cs` body:

```csharp
public enum DownloadMode
{
    AudioMp3,
    AudioOpus,
    Video
}
```

- [ ] **Step 3b: Add `BestOpusAudio` to `VideoInfo`**

Replace `Models/VideoInfo.cs`:

```csharp
namespace YoutubeDownloader.Core.Models;

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

- [ ] **Step 3c: Populate `BestOpusAudio` in `YoutubeExplodeService`**

In `Services/YoutubeExplodeService.cs`, after `var bestAudio = StreamSelector.PickBestAudio(audioOptions);` add:

```csharp
        var bestOpus = StreamSelector.PickBestOpusAudio(audioOptions);
```

and change the `return new VideoInfo(...)` to pass it as the last argument:

```csharp
        return new VideoInfo(
            Title: video.Title,
            Author: video.Author.ChannelTitle,
            Duration: video.Duration ?? TimeSpan.Zero,
            ThumbnailUrl: thumb,
            BestAudio: bestAudio,
            VideoOptions: videoOptions,
            BestOpusAudio: bestOpus);
```

- [ ] **Step 3d: Add `RemuxToOpusAsync` to the interface**

In `Services/IMediaConverter.cs`, add after `MuxToMp4Async`:

```csharp
    Task RemuxToOpusAsync(string ffmpegPath, string input, string output,
        TimeSpan duration, IProgress<double>? progress, CancellationToken ct);
```

- [ ] **Step 3e: Implement it in `FFmpegMediaConverter`**

In `Services/FFmpegMediaConverter.cs`, add after `MuxToMp4Async`:

```csharp
    public Task RemuxToOpusAsync(string ffmpegPath, string input, string output,
        TimeSpan duration, IProgress<double>? progress, CancellationToken ct)
        => RunAsync(ffmpegPath, FFmpegArguments.ForRemuxOpus(input, output), duration, progress, ct);
```

- [ ] **Step 4: Run test + full build to verify green**

Run: `dotnet test YoutubeDownloader.Core.Tests --filter "FullyQualifiedName~RemuxToOpusAsync_WhenFfmpegPathInvalid_Throws"`
Expected: PASS.
Run: `dotnet build YoutubeDownloader.sln`
Expected: Build succeeded (VideoInfo's default arg keeps all call sites compiling).

- [ ] **Step 5: Commit**

```bash
git add YoutubeDownloader.Core/Models/DownloadMode.cs YoutubeDownloader.Core/Models/VideoInfo.cs YoutubeDownloader.Core/Services/YoutubeExplodeService.cs YoutubeDownloader.Core/Services/IMediaConverter.cs YoutubeDownloader.Core/Services/FFmpegMediaConverter.cs YoutubeDownloader.Core.Tests/FFmpegMediaConverterTests.cs
git commit -m "feat(opus): add AudioOpus mode, BestOpusAudio, and RemuxToOpusAsync"
```

---

### Task 4: MainViewModel Opus download branch + ShowBitrate

**Files:**
- Modify: `YoutubeDownloader.Core/ViewModels/MainViewModel.cs`
- Test: `YoutubeDownloader.Core.Tests/MainViewModelTests.cs`

**Interfaces:**
- Consumes: `DownloadMode.AudioOpus`, `VideoInfo.BestOpusAudio`, `IMediaConverter.RemuxToOpusAsync` (Task 3).
- Produces: `MainViewModel.ShowBitrate : bool` (true unless mode is `AudioOpus`); Opus download branch in `DownloadAsync`.

- [ ] **Step 1: Update the `SampleInfo` test helper to include an Opus stream**

In `MainViewModelTests.cs`, replace the `SampleInfo` helper:

```csharp
    private static VideoInfo SampleInfo(int sourceAudioKbps = 128, bool withOpus = true) => new(
        Title: "Test Video",
        Author: "Tester",
        Duration: TimeSpan.FromMinutes(3),
        ThumbnailUrl: "",
        BestAudio: new AudioStreamOption("opus", sourceAudioKbps, "webm", Source: null!),
        VideoOptions: new[] { new VideoStreamOption("720p", 720, "mp4", Source: null!) },
        BestOpusAudio: withOpus ? new AudioStreamOption("opus", 160, "webm", Source: null!) : null);
```

- [ ] **Step 2: Write the failing tests**

Add to `MainViewModelTests.cs`:

```csharp
    [Fact]
    public void ShowBitrate_HiddenOnlyForOpusMode()
    {
        var vm = CreateSut();

        vm.SelectedMode = DownloadMode.AudioMp3;
        Assert.True(vm.ShowBitrate);
        vm.SelectedMode = DownloadMode.Video;
        Assert.True(vm.ShowBitrate);
        vm.SelectedMode = DownloadMode.AudioOpus;
        Assert.False(vm.ShowBitrate);
    }

    [Fact]
    public async Task Download_OpusMode_RemuxesNativeStreamAndRecordsOpusFormat()
    {
        var info = SampleInfo();
        _youtube.Setup(s => s.GetVideoInfoAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(info);
        _ffmpeg.Setup(f => f.EnsureAsync(It.IsAny<IProgress<double>?>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync(@"C:\ffmpeg\ffmpeg.exe");
        _temp.Setup(t => t.NewTempFile(It.IsAny<string>())).Returns(@"C:\temp\a.webm");
        _saveFile.Setup(s => s.PromptForSavePathAsync(It.IsAny<string>(), "opus", It.IsAny<string?>())).ReturnsAsync(@"C:\out\song.opus");

        var vm = CreateSut();
        vm.Url = "https://youtu.be/dQw4w9WgXcQ";
        await vm.FetchInfoCommand.ExecuteAsync(null);
        vm.SelectedMode = DownloadMode.AudioOpus;

        await vm.DownloadCommand.ExecuteAsync(null);

        _converter.Verify(c => c.RemuxToOpusAsync(@"C:\ffmpeg\ffmpeg.exe", @"C:\temp\a.webm", @"C:\out\song.opus",
            info.Duration, It.IsAny<IProgress<double>?>(), It.IsAny<CancellationToken>()), Times.Once);
        _converter.Verify(c => c.ConvertToMp3Async(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<int>(), It.IsAny<TimeSpan>(), It.IsAny<IProgress<double>?>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.Equal("Opus · 160 kbps", Assert.Single(vm.History).Format);
    }

    [Fact]
    public async Task Download_OpusMode_NoOpusStream_ErrorsWithoutPrompting()
    {
        var info = SampleInfo(withOpus: false);
        _youtube.Setup(s => s.GetVideoInfoAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(info);

        var vm = CreateSut();
        vm.Url = "https://youtu.be/dQw4w9WgXcQ";
        await vm.FetchInfoCommand.ExecuteAsync(null);
        vm.SelectedMode = DownloadMode.AudioOpus;

        await vm.DownloadCommand.ExecuteAsync(null);

        Assert.Contains("Opus", vm.StatusMessage);
        _saveFile.Verify(s => s.PromptForSavePathAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>()), Times.Never);
        Assert.Empty(vm.History);
    }
```

- [ ] **Step 3: Run tests to verify they fail**

Run: `dotnet test YoutubeDownloader.Core.Tests --filter "FullyQualifiedName~Opus|FullyQualifiedName~ShowBitrate"`
Expected: FAIL — `ShowBitrate` undefined; Opus download not implemented.

- [ ] **Step 4a: Add the `ShowBitrate` property + notify**

In `MainViewModel.cs`, the `_selectedMode` observable property currently reads:

```csharp
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UpscaleWarning))]
    private DownloadMode _selectedMode = DownloadMode.AudioMp3;
```

Add a second notify attribute:

```csharp
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UpscaleWarning))]
    [NotifyPropertyChangedFor(nameof(ShowBitrate))]
    private DownloadMode _selectedMode = DownloadMode.AudioMp3;
```

Add the property near `UpscaleWarning`:

```csharp
    /// <summary>Bitrate only applies to MP3; hidden for the copy-only Opus mode.</summary>
    public bool ShowBitrate => SelectedMode != DownloadMode.AudioOpus;
```

- [ ] **Step 4b: Rewrite the mode-dependent part of `DownloadAsync`**

Replace the opening of `DownloadAsync` — from `var info = VideoInfo!;` through the `var target = ...` prompt — with (adds the guard and the 3-way extension):

```csharp
        var info = VideoInfo!;

        if (SelectedMode == DownloadMode.AudioOpus && info.BestOpusAudio is null)
        {
            StatusMessage = "This video has no Opus audio stream to save.";
            return;
        }

        var ext = SelectedMode switch
        {
            DownloadMode.AudioMp3 => "mp3",
            DownloadMode.AudioOpus => "opus",
            _ => "mp4"
        };
        var suggested = $"{FileNameSanitizer.Sanitize(info.Title)}.{ext}";

        var target = await _saveFile.PromptForSavePathAsync(suggested, ext, _appSettings.LastSaveFolder);
        if (target is null)
        {
            StatusMessage = "Download cancelled.";
            return;
        }
```

Then replace the `if (isAudio) { ... } else { ... }` block with this 3-way structure (the MP3 and Video arms are unchanged from today; the Opus arm is new):

```csharp
            if (SelectedMode == DownloadMode.AudioMp3)
            {
                StatusMessage = "Downloading audio…";
                var tempAudio = _temp.NewTempFile(info.BestAudio.Container);
                tempFiles.Add(tempAudio);
                await _youtube.DownloadStreamAsync(info.BestAudio.Source, tempAudio,
                    new Progress<double>(p => Progress = 0.1 + p * 0.6), token);

                StatusMessage = $"Converting to MP3 ({(int)SelectedBitrate} kbps)…";
                await _converter.ConvertToMp3Async(ffmpegPath, tempAudio, target, (int)SelectedBitrate,
                    info.Duration, new Progress<double>(p => Progress = 0.7 + p * 0.3), token);
                formatLabel = $"MP3 · {(int)SelectedBitrate} kbps";
            }
            else if (SelectedMode == DownloadMode.AudioOpus)
            {
                StatusMessage = "Downloading audio…";
                var tempAudio = _temp.NewTempFile(info.BestOpusAudio!.Container);
                tempFiles.Add(tempAudio);
                await _youtube.DownloadStreamAsync(info.BestOpusAudio.Source, tempAudio,
                    new Progress<double>(p => Progress = 0.1 + p * 0.6), token);

                StatusMessage = "Saving Opus audio…";
                await _converter.RemuxToOpusAsync(ffmpegPath, tempAudio, target,
                    info.Duration, new Progress<double>(p => Progress = 0.7 + p * 0.3), token);
                formatLabel = $"Opus · {info.BestOpusAudio.BitrateKbps} kbps";
            }
            else
            {
                var quality = SelectedVideoQuality ?? info.VideoOptions.First();
                formatLabel = $"MP4 · {quality.QualityLabel}";
                StatusMessage = $"Downloading video ({quality.QualityLabel})…";
                var tempVideo = _temp.NewTempFile(quality.Container);
                tempFiles.Add(tempVideo);
                var tempAudio = _temp.NewTempFile(info.BestAudio.Container);
                tempFiles.Add(tempAudio);

                await _youtube.DownloadStreamAsync(quality.Source, tempVideo,
                    new Progress<double>(p => Progress = 0.1 + p * 0.4), token);
                await _youtube.DownloadStreamAsync(info.BestAudio.Source, tempAudio,
                    new Progress<double>(p => Progress = 0.5 + p * 0.2), token);

                StatusMessage = "Merging video + audio…";
                await _converter.MuxToMp4Async(ffmpegPath, tempVideo, tempAudio, target,
                    info.Duration, new Progress<double>(p => Progress = 0.7 + p * 0.3), token);
            }
```

(The `var isAudio = ...` line is now gone — its two former uses, the extension and the branch, are both replaced above.)

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test YoutubeDownloader.Core.Tests`
Expected: PASS (new Opus/ShowBitrate tests + all existing tests, including the untouched MP3/Video/UpscaleWarning tests).

- [ ] **Step 6: Commit**

```bash
git add YoutubeDownloader.Core/ViewModels/MainViewModel.cs YoutubeDownloader.Core.Tests/MainViewModelTests.cs
git commit -m "feat(opus): download+remux native Opus and hide bitrate for Opus mode"
```

---

### Task 5: Avalonia UI for Opus (label + bitrate visibility)

**Files:**
- Modify: `YoutubeDownloader.Avalonia/Converters/DownloadModeDisplayConverter.cs`
- Modify: `YoutubeDownloader.Avalonia/Views/MainWindow.axaml`

**Interfaces:**
- Consumes: `DownloadMode.AudioOpus`, `MainViewModel.ShowBitrate` (Task 4).

- [ ] **Step 1: Add the display label**

In `DownloadModeDisplayConverter.cs`, add the `AudioOpus` arm to the switch:

```csharp
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        DownloadMode.AudioMp3 => "MP3 audio",
        DownloadMode.AudioOpus => "Opus audio",
        DownloadMode.Video => "Video (MP4)",
        _ => value?.ToString() ?? ""
    };
```

- [ ] **Step 2: Hide the bitrate box for Opus**

In `Views/MainWindow.axaml`, the "MP3 BITRATE" label + combo are the `TextBlock`/`ComboBox` at ~lines 114–122. Add `IsVisible="{Binding ShowBitrate}"` to both:

```xml
            <TextBlock Classes="label" Text="MP3 BITRATE" IsVisible="{Binding ShowBitrate}" />
            <ComboBox Width="260" HorizontalAlignment="Left" Margin="0,0,0,6"
                      IsVisible="{Binding ShowBitrate}"
                      ItemsSource="{Binding Bitrates}" SelectedItem="{Binding SelectedBitrate}">
              <ComboBox.ItemTemplate>
                <DataTemplate x:DataType="models:Mp3Bitrate">
                  <TextBlock Text="{Binding Converter={StaticResource BitrateDisplay}}" />
                </DataTemplate>
              </ComboBox.ItemTemplate>
            </ComboBox>
```

- [ ] **Step 3: Build to verify XAML compiles**

Run: `dotnet build YoutubeDownloader.Avalonia`
Expected: Build succeeded.

- [ ] **Step 4: Manual smoke check**

Run: `dotnet run --project YoutubeDownloader.Avalonia`, fetch any video, open the FORMAT dropdown. Expected: three options — "MP3 audio", "Opus audio", "Video (MP4)". Selecting "Opus audio" hides the MP3 BITRATE control; MP3/Video show it.

- [ ] **Step 5: Commit**

```bash
git add YoutubeDownloader.Avalonia/Converters/DownloadModeDisplayConverter.cs YoutubeDownloader.Avalonia/Views/MainWindow.axaml
git commit -m "feat(opus): surface Opus audio in Avalonia dropdown and hide bitrate box"
```

---

### Task 6: Confirm before Clear all

**Files:**
- Modify: `YoutubeDownloader.Core/ViewModels/MainViewModel.cs`
- Test: `YoutubeDownloader.Core.Tests/MainViewModelTests.cs`

**Interfaces:**
- Consumes: existing `IConfirmationService.ConfirmAsync(message, title)`.
- Produces: `ClearHistoryAsync()` → generated `ClearHistoryCommand` (name unchanged).

- [ ] **Step 1: Replace the existing clear test with confirm-aware tests**

In `MainViewModelTests.cs`, delete the existing `ClearHistory_EmptiesAndSaves` test (lines ~289–299) and add:

```csharp
    [Fact]
    public async Task ClearHistory_WhenConfirmed_EmptiesAndSaves()
    {
        _history.Setup(h => h.Load()).Returns(new List<HistoryEntry> { new() { Url = "u1" } });
        _confirm.Setup(c => c.ConfirmAsync(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(true);
        var vm = CreateSut();

        await vm.ClearHistoryCommand.ExecuteAsync(null);

        Assert.Empty(vm.History);
        _history.Verify(h => h.Save(It.IsAny<IEnumerable<HistoryEntry>>()), Times.Once);
    }

    [Fact]
    public async Task ClearHistory_WhenDeclined_KeepsHistory()
    {
        _history.Setup(h => h.Load()).Returns(new List<HistoryEntry> { new() { Url = "u1" } });
        _confirm.Setup(c => c.ConfirmAsync(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(false);
        var vm = CreateSut();

        await vm.ClearHistoryCommand.ExecuteAsync(null);

        Assert.Single(vm.History);
        _history.Verify(h => h.Save(It.IsAny<IEnumerable<HistoryEntry>>()), Times.Never);
    }

    [Fact]
    public async Task ClearHistory_WhenEmpty_DoesNotPrompt()
    {
        var vm = CreateSut();

        await vm.ClearHistoryCommand.ExecuteAsync(null);

        _confirm.Verify(c => c.ConfirmAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test YoutubeDownloader.Core.Tests --filter "FullyQualifiedName~ClearHistory"`
Expected: FAIL — `ClearHistoryCommand` is not an async command yet (no `ExecuteAsync`), and declined-case still clears.

- [ ] **Step 3: Make `ClearHistory` async with confirmation**

In `MainViewModel.cs`, replace the existing command:

```csharp
    [RelayCommand]
    private void ClearHistory()
    {
        if (History.Count == 0)
            return;
        History.Clear();
        _history.Save(History);
    }
```

with:

```csharp
    [RelayCommand]
    private async Task ClearHistoryAsync()
    {
        if (History.Count == 0)
            return;
        if (!await _confirm.ConfirmAsync("Clear all download history? This can't be undone.", "Clear history?"))
            return;
        History.Clear();
        _history.Save(History);
    }
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test YoutubeDownloader.Core.Tests --filter "FullyQualifiedName~ClearHistory"`
Expected: PASS (all three). The Avalonia `Command="{Binding ClearHistoryCommand}"` binding is unchanged.

- [ ] **Step 5: Commit**

```bash
git add YoutubeDownloader.Core/ViewModels/MainViewModel.cs YoutubeDownloader.Core.Tests/MainViewModelTests.cs
git commit -m "feat(history): confirm before clearing all history"
```

---

### Task 7: foobar2000 launcher interface + ViewModel wiring + DI

**Files:**
- Create: `YoutubeDownloader.Core/Services/IFoobar2000Launcher.cs`
- Create: `YoutubeDownloader.Core/Services/NullFoobar2000Launcher.cs`
- Modify: `YoutubeDownloader.Core/ViewModels/MainViewModel.cs`
- Modify: `YoutubeDownloader.Avalonia/App.axaml.cs`
- Modify: `YoutubeDownloader.App/App.xaml.cs`
- Test: `YoutubeDownloader.Core.Tests/MainViewModelTests.cs`

**Interfaces:**
- Produces:
  - `IFoobar2000Launcher { bool IsInstalled { get; } void Open(string filePath); }`
  - `NullFoobar2000Launcher : IFoobar2000Launcher` (IsInstalled=false, Open no-op).
  - `MainViewModel` new last ctor param `IFoobar2000Launcher foobar`; properties `IsFoobarInstalled : bool`, `FoobarTooltip : string`; command `OpenInFoobarCommand` from `OpenInFoobar(HistoryEntry?)`.

- [ ] **Step 1: Create the interface and null implementation**

Create `YoutubeDownloader.Core/Services/IFoobar2000Launcher.cs`:

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

Create `YoutubeDownloader.Core/Services/NullFoobar2000Launcher.cs`:

```csharp
namespace YoutubeDownloader.Core.Services;

/// <summary>Inert launcher for platforms/apps without foobar2000 support (e.g. the WPF build).</summary>
public sealed class NullFoobar2000Launcher : IFoobar2000Launcher
{
    public bool IsInstalled => false;
    public void Open(string filePath) { }
}
```

- [ ] **Step 2: Add the mock to the test fixture and write failing tests**

In `MainViewModelTests.cs`:

Add the field alongside the other mocks:

```csharp
    private readonly Mock<IFoobar2000Launcher> _foobar = new();
```

Update `CreateSut` to pass it as the last constructor argument:

```csharp
    private MainViewModel CreateSut() =>
        new(_youtube.Object, _ffmpeg.Object, _converter.Object, _saveFile.Object, _temp.Object,
            _history.Object, _linkOpener.Object, _fileRevealer.Object, _settings.Object, _clipboard.Object,
            _confirm.Object, _foobar.Object);
```

Add the tests:

```csharp
    [Fact]
    public void OpenInFoobar_WhenInstalledWithFile_LaunchesIt()
    {
        _foobar.SetupGet(f => f.IsInstalled).Returns(true);
        var vm = CreateSut();

        vm.OpenInFoobarCommand.Execute(new HistoryEntry { FilePath = @"C:\out\song.opus" });

        _foobar.Verify(f => f.Open(@"C:\out\song.opus"), Times.Once);
    }

    [Fact]
    public void OpenInFoobar_WhenNotInstalled_DoesNotLaunch()
    {
        _foobar.SetupGet(f => f.IsInstalled).Returns(false);
        var vm = CreateSut();

        vm.OpenInFoobarCommand.Execute(new HistoryEntry { FilePath = @"C:\out\song.opus" });

        _foobar.Verify(f => f.Open(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public void OpenInFoobar_NullEntryOrNoFilePath_DoesNotLaunch()
    {
        _foobar.SetupGet(f => f.IsInstalled).Returns(true);
        var vm = CreateSut();

        vm.OpenInFoobarCommand.Execute(null);
        vm.OpenInFoobarCommand.Execute(new HistoryEntry { FilePath = "" });

        _foobar.Verify(f => f.Open(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public void FoobarTooltip_ReflectsInstalledState()
    {
        _foobar.SetupGet(f => f.IsInstalled).Returns(false);
        Assert.Equal("foobar2000 is not installed", CreateSut().FoobarTooltip);

        _foobar.SetupGet(f => f.IsInstalled).Returns(true);
        Assert.Equal("Open in foobar2000", CreateSut().FoobarTooltip);
    }
```

- [ ] **Step 3: Run tests to verify they fail**

Run: `dotnet test YoutubeDownloader.Core.Tests --filter "FullyQualifiedName~Foobar"`
Expected: FAIL — compile error, `MainViewModel` ctor takes 11 args (no `IFoobar2000Launcher`), no `OpenInFoobarCommand`/`FoobarTooltip`.

- [ ] **Step 4a: Wire the launcher into `MainViewModel`**

In `MainViewModel.cs`, add the field with the others:

```csharp
    private readonly IFoobar2000Launcher _foobar;
```

Add `IFoobar2000Launcher foobar` as the **last** constructor parameter, and assign it in the body:

```csharp
        _foobar = foobar;
```

Add the properties and command near the other history commands:

```csharp
    public bool IsFoobarInstalled => _foobar.IsInstalled;

    public string FoobarTooltip =>
        IsFoobarInstalled ? "Open in foobar2000" : "foobar2000 is not installed";

    /// <summary>Play a downloaded file in foobar2000 (no-op unless installed).</summary>
    [RelayCommand]
    private void OpenInFoobar(HistoryEntry? entry)
    {
        if (entry is null || string.IsNullOrEmpty(entry.FilePath) || !_foobar.IsInstalled)
            return;
        try
        {
            _foobar.Open(entry.FilePath);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Couldn't open foobar2000: {ex.Message}";
        }
    }
```

- [ ] **Step 4b: Register a launcher in both composition roots**

In `YoutubeDownloader.Avalonia/App.axaml.cs`, next to the other `AddSingleton` calls (e.g. after the `IConfirmationService` registration), add:

```csharp
        services.AddSingleton<IFoobar2000Launcher, NullFoobar2000Launcher>();
```

In `YoutubeDownloader.App/App.xaml.cs`, next to the other `AddSingleton` calls, add:

```csharp
        services.AddSingleton<IFoobar2000Launcher, NullFoobar2000Launcher>();
```

(The Avalonia registration is swapped to the real launcher in Task 8.)

- [ ] **Step 5: Run tests + build to verify green**

Run: `dotnet test YoutubeDownloader.Core.Tests`
Expected: PASS (foobar tests + all prior tests).
Run: `dotnet build YoutubeDownloader.sln`
Expected: Build succeeded (both apps register a launcher).

- [ ] **Step 6: Commit**

```bash
git add YoutubeDownloader.Core/Services/IFoobar2000Launcher.cs YoutubeDownloader.Core/Services/NullFoobar2000Launcher.cs YoutubeDownloader.Core/ViewModels/MainViewModel.cs YoutubeDownloader.Avalonia/App.axaml.cs YoutubeDownloader.App/App.xaml.cs YoutubeDownloader.Core.Tests/MainViewModelTests.cs
git commit -m "feat(foobar): add launcher interface, ViewModel command, and DI wiring"
```

---

### Task 8: Avalonia real foobar2000 launcher + chip button

**Files:**
- Create: `YoutubeDownloader.Avalonia/Services/Foobar2000Launcher.cs`
- Modify: `YoutubeDownloader.Avalonia/YoutubeDownloader.Avalonia.csproj`
- Modify: `YoutubeDownloader.Avalonia/App.axaml.cs`
- Modify: `YoutubeDownloader.Avalonia/Views/MainWindow.axaml`

**Interfaces:**
- Consumes: `IFoobar2000Launcher` (Task 7); `MainViewModel.IsFoobarInstalled` / `FoobarTooltip` / `OpenInFoobarCommand` (Task 7).

- [ ] **Step 1: Add the Registry package**

In `YoutubeDownloader.Avalonia.csproj`, inside the `<ItemGroup>` with the other `PackageReference`s, add:

```xml
    <PackageReference Include="Microsoft.Win32.Registry" Version="5.0.0" />
```

Run: `dotnet restore YoutubeDownloader.Avalonia`
Expected: Restore succeeded.

- [ ] **Step 2: Implement the Windows launcher**

Create `YoutubeDownloader.Avalonia/Services/Foobar2000Launcher.cs`:

```csharp
using System;
using System.Diagnostics;
using System.IO;
using Microsoft.Win32;
using YoutubeDownloader.Core.Services;

namespace YoutubeDownloader.Avalonia.Services;

/// <summary>Locates foobar2000 (registry App Paths, then Program Files) and launches it with a file.</summary>
public sealed class Foobar2000Launcher : IFoobar2000Launcher
{
    private const string AppPathsKey =
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\foobar2000.exe";

    private readonly string? _exePath;

    public Foobar2000Launcher() => _exePath = Resolve();

    public bool IsInstalled => _exePath is not null;

    public void Open(string filePath)
    {
        if (_exePath is null)
            return;
        Process.Start(new ProcessStartInfo(_exePath, $"\"{filePath}\"") { UseShellExecute = true });
    }

    private static string? Resolve()
    {
        foreach (var hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(hive, view);
                using var key = baseKey.OpenSubKey(AppPathsKey);
                if (key?.GetValue(null) is string path && File.Exists(path))
                    return path;
            }
            catch
            {
                // Ignore unreadable/absent registry views and fall through to disk probing.
            }
        }

        foreach (var env in new[] { "ProgramFiles", "ProgramFiles(x86)" })
        {
            var root = Environment.GetEnvironmentVariable(env);
            if (string.IsNullOrEmpty(root))
                continue;
            var candidate = Path.Combine(root, "foobar2000", "foobar2000.exe");
            if (File.Exists(candidate))
                return candidate;
        }

        return null;
    }
}
```

- [ ] **Step 3: Swap the Avalonia DI registration to the real launcher**

In `YoutubeDownloader.Avalonia/App.axaml.cs`, change the line added in Task 7 from:

```csharp
        services.AddSingleton<IFoobar2000Launcher, NullFoobar2000Launcher>();
```

to:

```csharp
        services.AddSingleton<IFoobar2000Launcher, Foobar2000Launcher>();
```

(Add `using YoutubeDownloader.Avalonia.Services;` if it is not already present.)

- [ ] **Step 4: Add the chip button to each history entry**

In `Views/MainWindow.axaml`, in the per-entry button `StackPanel` (~lines 201–215), insert the foobar chip between the "Folder" button and the "✕" button:

```xml
                                <Button Classes="chip" Content="▶ foobar"
                                        IsVisible="{Binding FilePath, Converter={StaticResource NotEmpty}}"
                                        IsEnabled="{Binding #Root.((vm:MainViewModel)DataContext).IsFoobarInstalled}"
                                        ToolTip.Tip="{Binding #Root.((vm:MainViewModel)DataContext).FoobarTooltip}"
                                        Command="{Binding #Root.((vm:MainViewModel)DataContext).OpenInFoobarCommand}"
                                        CommandParameter="{Binding}" />
```

- [ ] **Step 5: Build to verify it compiles**

Run: `dotnet build YoutubeDownloader.Avalonia`
Expected: Build succeeded.

- [ ] **Step 6: Manual smoke check**

Run: `dotnet run --project YoutubeDownloader.Avalonia`. In History, entries with a saved file show a "▶ foobar" chip. With foobar2000 installed it is enabled and opens the file; without it, it is greyed out and hovering shows "foobar2000 is not installed".

- [ ] **Step 7: Commit**

```bash
git add YoutubeDownloader.Avalonia/Services/Foobar2000Launcher.cs YoutubeDownloader.Avalonia/YoutubeDownloader.Avalonia.csproj YoutubeDownloader.Avalonia/App.axaml.cs YoutubeDownloader.Avalonia/Views/MainWindow.axaml
git commit -m "feat(foobar): detect+launch foobar2000 and add per-entry chip (Avalonia)"
```

---

### Task 9: Docs + full verification

**Files:**
- Modify: `README.md`

- [ ] **Step 1: Update the README Features list**

In `README.md`, in the Features section, update the first bullet and add a foobar bullet:

```markdown
- **Download audio or video** — save audio as **MP3** (selectable bitrate) or **Opus** (native, no re-encode), or **video** (selectable quality).
```

```markdown
- **Open in foobar2000** *(Avalonia)* — from any history entry with a saved file, open it in foobar2000 (button is disabled with a tooltip when foobar2000 isn't installed).
```

Also add "confirms before **Clear all**" to the History bullet's sub-list intro line (append to the History bullet):

```markdown
  - **Clear all** — now asks for confirmation first.
```

- [ ] **Step 2: Run the full test suite**

Run: `dotnet test`
Expected: All tests pass (Core.Tests). No failures.

- [ ] **Step 3: Build both apps**

Run: `dotnet build YoutubeDownloader.sln`
Expected: Build succeeded, no warnings introduced by these changes.

- [ ] **Step 4: Commit**

```bash
git add README.md
git commit -m "docs: note Opus audio, foobar2000 open, and clear-all confirmation"
```

---

## Self-Review

**Spec coverage (Opus spec):**
- DownloadMode.AudioOpus → Task 3. VideoInfo.BestOpusAudio → Task 3. StreamSelector.PickBestOpusAudio → Task 2. YoutubeExplodeService populate → Task 3. FFmpegArguments.ForRemuxOpus → Task 1. IMediaConverter/FFmpegMediaConverter.RemuxToOpusAsync → Task 3. MainViewModel 3-way + guard + label → Task 4. UpscaleWarning unchanged (gated to AudioMp3) → verified in Task 4 (existing tests still pass). Avalonia label + bitrate visibility → Task 5. Tests → Tasks 1, 2, 4. ✓

**Spec coverage (History UX spec):**
- ClearHistory confirm → Task 6. IFoobar2000Launcher + NullFoobar2000Launcher → Task 7. Foobar2000Launcher (registry + Program Files) → Task 8. Registry package → Task 8. MainViewModel inject/command/props → Task 7. DI (Avalonia real, WPF null) → Tasks 7+8. MainWindow chip → Task 8. Tests → Tasks 6, 7. ✓

**Placeholder scan:** No TBD/TODO; every code step shows complete code; every test step shows the assertions; commands have expected output. ✓

**Type consistency:** `RemuxToOpusAsync` signature identical in interface (Task 3d), impl (Task 3e), and verify (Task 4). `ForRemuxOpus(input, output)` identical in Tasks 1 and 3e. `PickBestOpusAudio` returns `AudioStreamOption?` in Task 2 and consumed nullable in Task 3c. `IFoobar2000Launcher` members (`IsInstalled`, `Open`) identical across Tasks 7 and 8. `OpenInFoobarCommand` / `FoobarTooltip` / `IsFoobarInstalled` names identical in Task 7 (ViewModel + tests) and Task 8 (XAML). `ClearHistoryAsync` → `ClearHistoryCommand` matches the unchanged Avalonia binding. ✓
