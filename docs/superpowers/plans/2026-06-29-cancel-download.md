# Cancel In-Progress Download Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let the user cancel a running download; the Download button toggles to a Cancel button, and cancelling stops the work, clears progress, and deletes the partial output file.

**Architecture:** The download pipeline in `MainViewModel.DownloadAsync` already threads a `CancellationToken` through all four awaited calls (currently `CancellationToken.None`). We add a per-download `CancellationTokenSource`, a narrower `IsDownloading` flag (since `IsBusy` covers both Fetch and Download), a `CancelDownloadCommand`, and an `OperationCanceledException` handler that resets state and deletes the partial file. The UI toggles a single button between Download and Cancel via a `Style` `DataTrigger` on `IsDownloading`.

**Tech Stack:** .NET 8, WPF, CommunityToolkit.Mvvm (`[ObservableProperty]`, `[RelayCommand]`), xUnit + Moq.

## Global Constraints

- Target framework: `net8.0` (matches existing `YoutubeDownloader.Core.Tests`).
- MVVM via CommunityToolkit.Mvvm source generators — use `[ObservableProperty]` / `[RelayCommand]`, never hand-rolled `INotifyPropertyChanged`.
- C# style in this repo: file-scoped namespaces, nullable reference types enabled, fields prefixed `_`.
- Cancel status text is exactly `"Download cancelled."` (British spelling, matching the existing save-dialog-cancel message at `MainViewModel.cs:251`).
- Out of scope (do NOT implement): cancelling the Fetch-info step, pause/resume, multiple concurrent downloads.

---

### Task 1: ViewModel cancel capability

**Files:**
- Modify: `YoutubeDownloader.Core/ViewModels/MainViewModel.cs`
- Test: `YoutubeDownloader.Core.Tests/MainViewModelTests.cs`

**Interfaces:**
- Consumes (already present): `IFFmpegLocator.EnsureAsync(IProgress<double>?, CancellationToken)`, `IYouTubeService.DownloadStreamAsync(IStreamInfo, string, IProgress<double>?, CancellationToken)`, `IMediaConverter.ConvertToMp3Async(...)` / `MuxToMp4Async(...)` (each ends with a `CancellationToken`).
- Produces (later task / UI rely on): public generated property `bool IsDownloading { get; set; }` and public generated command `IRelayCommand CancelDownloadCommand` (from `[RelayCommand] private void CancelDownload()`).

This task implements the entire testable cancel logic. The XAML wiring is Task 2.

- [ ] **Step 1: Write the failing tests**

Add these tests to `YoutubeDownloader.Core.Tests/MainViewModelTests.cs` (inside the `MainViewModelTests` class). They reuse the existing `_youtube`/`_ffmpeg`/`_temp`/`_saveFile` mocks, the `CreateSut()` helper, and `SampleInfo()`.

```csharp
[Fact]
public void CancelDownload_CannotExecute_WhenNotDownloading()
{
    var vm = CreateSut();

    Assert.False(vm.IsDownloading);
    Assert.False(vm.CancelDownloadCommand.CanExecute(null));
}

[Fact]
public async Task Download_Success_LeavesIsDownloadingFalse()
{
    var info = SampleInfo();
    _youtube.Setup(s => s.GetVideoInfoAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(info);
    _ffmpeg.Setup(f => f.EnsureAsync(It.IsAny<IProgress<double>?>(), It.IsAny<CancellationToken>()))
           .ReturnsAsync(@"C:\ffmpeg\ffmpeg.exe");
    _temp.Setup(t => t.NewTempFile(It.IsAny<string>())).Returns(@"C:\temp\a.webm");
    _saveFile.Setup(s => s.PromptForSavePath(It.IsAny<string>(), "mp3", It.IsAny<string?>())).Returns(@"C:\out\song.mp3");

    var vm = CreateSut();
    vm.Url = "https://youtu.be/dQw4w9WgXcQ";
    await vm.FetchInfoCommand.ExecuteAsync(null);
    vm.SelectedMode = DownloadMode.AudioMp3;

    await vm.DownloadCommand.ExecuteAsync(null);

    Assert.False(vm.IsDownloading);
}

[Fact]
public async Task Download_Cancelled_SetsCancelledStatusResetsProgressNoHistoryAndDeletesPartialFile()
{
    // A real file on disk stands in for the partially-written output.
    var target = Path.Combine(Path.GetTempPath(), $"cancel-test-{Guid.NewGuid():N}.mp3");
    File.WriteAllText(target, "partial bytes");

    try
    {
        var info = SampleInfo();
        _youtube.Setup(s => s.GetVideoInfoAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(info);
        _ffmpeg.Setup(f => f.EnsureAsync(It.IsAny<IProgress<double>?>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync(@"C:\ffmpeg\ffmpeg.exe");
        _temp.Setup(t => t.NewTempFile(It.IsAny<string>())).Returns(@"C:\temp\a.webm");
        _saveFile.Setup(s => s.PromptForSavePath(It.IsAny<string>(), "mp3", It.IsAny<string?>())).Returns(target);
        // Block the audio download until the token is cancelled, then throw.
        _youtube.Setup(s => s.DownloadStreamAsync(It.IsAny<IStreamInfo>(), It.IsAny<string>(),
                    It.IsAny<IProgress<double>?>(), It.IsAny<CancellationToken>()))
                .Returns((IStreamInfo _, string _, IProgress<double>? _, CancellationToken ct) =>
                    Task.Delay(Timeout.Infinite, ct));

        var vm = CreateSut();
        vm.Url = "https://youtu.be/dQw4w9WgXcQ";
        await vm.FetchInfoCommand.ExecuteAsync(null);
        vm.SelectedMode = DownloadMode.AudioMp3;

        // IsDownloading is set before the first awaited call that actually yields
        // (EnsureAsync completes synchronously via Moq), so it is already true here.
        var download = vm.DownloadCommand.ExecuteAsync(null);
        Assert.True(vm.IsDownloading);
        Assert.True(vm.CancelDownloadCommand.CanExecute(null));

        vm.CancelDownloadCommand.Execute(null);
        await download;

        Assert.Equal("Download cancelled.", vm.StatusMessage);
        Assert.Equal(0, vm.Progress);
        Assert.Empty(vm.History);
        Assert.False(vm.IsDownloading);
        Assert.False(File.Exists(target));
    }
    finally
    {
        if (File.Exists(target)) File.Delete(target);
    }
}
```

- [ ] **Step 2: Run the new tests to verify they fail**

Run: `dotnet test YoutubeDownloader.Core.Tests --filter "FullyQualifiedName~MainViewModelTests" --no-restore -v n`
Expected: FAIL — compile error / missing members `IsDownloading` and `CancelDownloadCommand` (they don't exist yet).

- [ ] **Step 3: Add the `IsDownloading` property and the CTS field**

In `YoutubeDownloader.Core/ViewModels/MainViewModel.cs`, add the observable property next to the other `[ObservableProperty]` declarations (e.g. right after the `_isBusy` block at lines 91-94):

```csharp
[ObservableProperty]
[NotifyCanExecuteChangedFor(nameof(CancelDownloadCommand))]
private bool _isDownloading;
```

Then add the cancellation-token-source field. Put it immediately above the `[RelayCommand(CanExecute = nameof(CanDownload))]` `DownloadAsync` method:

```csharp
private CancellationTokenSource? _downloadCts;
```

- [ ] **Step 4: Wire the token and the IsDownloading lifecycle into `DownloadAsync`**

In `DownloadAsync`, change the `try` block opener (currently lines 259-265) from:

```csharp
        try
        {
            IsBusy = true;
            Progress = 0;
            StatusMessage = "Preparing FFmpeg…";
            var ffmpegPath = await _ffmpeg.EnsureAsync(
                new Progress<double>(p => Progress = p * 0.1), CancellationToken.None);
```

to:

```csharp
        try
        {
            _downloadCts = new CancellationTokenSource();
            var token = _downloadCts.Token;
            IsBusy = true;
            IsDownloading = true;
            Progress = 0;
            StatusMessage = "Preparing FFmpeg…";
            var ffmpegPath = await _ffmpeg.EnsureAsync(
                new Progress<double>(p => Progress = p * 0.1), token);
```

Then replace the remaining three `CancellationToken.None` arguments in this method with `token`:
- the audio `DownloadStreamAsync` call (line 273)
- the `ConvertToMp3Async` call (line 277)
- both video-path `DownloadStreamAsync` calls (lines 291, 293) and the `MuxToMp4Async` call (line 297)

After this step there are **zero** `CancellationToken.None` occurrences left inside `DownloadAsync` (the `FetchInfoAsync` one at line 173 stays — it is out of scope).

- [ ] **Step 5: Add the cancel handler and update `finally`**

In `DownloadAsync`, insert a `catch (OperationCanceledException)` block **before** the existing `catch (Exception ex)` (currently lines 304-307), and extend `finally` (currently lines 308-316):

```csharp
        catch (OperationCanceledException)
        {
            StatusMessage = "Download cancelled.";
            Progress = 0;
            try { if (File.Exists(target)) File.Delete(target); }
            catch { /* best-effort: partial output may be locked or already gone */ }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Download failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
            IsDownloading = false;
            _downloadCts?.Dispose();
            _downloadCts = null;
            foreach (var f in tempFiles)
            {
                try { if (File.Exists(f)) File.Delete(f); }
                catch { /* best-effort temp cleanup */ }
            }
        }
```

(`target` is the save path declared at line 248, in scope for the catch. `System.IO` is already imported at line 2.)

- [ ] **Step 6: Add the `CancelDownload` command**

Add the command alongside the other `[RelayCommand]` methods (e.g. directly after `DownloadAsync`):

```csharp
    [RelayCommand(CanExecute = nameof(CanCancelDownload))]
    private void CancelDownload() => _downloadCts?.Cancel();

    private bool CanCancelDownload() => IsDownloading;
```

- [ ] **Step 7: Run the tests to verify they pass**

Run: `dotnet test YoutubeDownloader.Core.Tests --filter "FullyQualifiedName~MainViewModelTests" --no-restore -v n`
Expected: PASS — all `MainViewModelTests` (existing + the 3 new ones). The existing download tests still pass because their mocks match on `It.IsAny<CancellationToken>()`.

- [ ] **Step 8: Commit**

```bash
git add YoutubeDownloader.Core/ViewModels/MainViewModel.cs YoutubeDownloader.Core.Tests/MainViewModelTests.cs
git commit -m "feat: cancel an in-progress download in the view model"
```

---

### Task 2: Toggle the Download button to Cancel in the UI

**Files:**
- Modify: `YoutubeDownloader.App/MainWindow.xaml:311-312`

**Interfaces:**
- Consumes: `IsDownloading` (bool) and `CancelDownloadCommand` from Task 1; existing `DownloadCommand`; existing `{StaticResource AccentButton}` button style.
- Produces: nothing consumed by later tasks (terminal UI change).

No unit test — WPF `Style`/`DataTrigger` behavior is verified by building and running the app.

- [ ] **Step 1: Replace the Download button with a toggling single button**

In `YoutubeDownloader.App/MainWindow.xaml`, replace the current button (lines 311-312):

```xml
                            <Button Grid.Column="1" Content="Download" Style="{StaticResource AccentButton}"
                                    Padding="26,10" Command="{Binding DownloadCommand}" />
```

with:

```xml
                            <Button Grid.Column="1" Padding="26,10">
                                <Button.Style>
                                    <Style TargetType="Button" BasedOn="{StaticResource AccentButton}">
                                        <Setter Property="Content" Value="Download" />
                                        <Setter Property="Command" Value="{Binding DownloadCommand}" />
                                        <Style.Triggers>
                                            <DataTrigger Binding="{Binding IsDownloading}" Value="True">
                                                <Setter Property="Content" Value="Cancel" />
                                                <Setter Property="Command" Value="{Binding CancelDownloadCommand}" />
                                            </DataTrigger>
                                        </Style.Triggers>
                                    </Style>
                                </Button.Style>
                            </Button>
```

Why `Content` and `Command` move off the element and into `Setter`s: WPF
dependency-property precedence ranks a local (inline-attribute) value **above** a
`DataTrigger` setter. If `Command="{Binding DownloadCommand}"` stayed a local
attribute, the trigger could not swap it to `CancelDownloadCommand` and the
"Cancel" button would still start a download. `Grid.Column` and `Padding` are not
touched by the trigger, so they stay as local attributes.

- [ ] **Step 2: Build the app to verify the XAML compiles**

Run: `dotnet build YoutubeDownloader.App --no-restore`
Expected: Build succeeded, 0 errors. (A XAML binding/resource error would fail the build.)

- [ ] **Step 3: Manually verify the toggle**

Run: `dotnet run --project YoutubeDownloader.App`
Then:
1. Paste a YouTube URL and click **Fetch** — the button reads **Download**.
2. Click **Download** — while it runs the button reads **Cancel** and is enabled.
3. Click **Cancel** — status shows "Download cancelled.", the progress bar returns to 0, the button reverts to **Download**, and no entry is added to History. The partial file at the chosen save path is gone.

- [ ] **Step 4: Commit**

```bash
git add YoutubeDownloader.App/MainWindow.xaml
git commit -m "feat: toggle Download button to Cancel while downloading"
```

---

## Self-Review

**1. Spec coverage:**
- `IsDownloading` flag + `_downloadCts` → Task 1, Step 3. ✓
- Token wired into all 4 awaited calls → Task 1, Step 4. ✓
- `OperationCanceledException` caught before generic; status "Download cancelled.", Progress 0, delete partial `target`, no history → Task 1, Step 5 + tests in Step 1. ✓
- `finally` resets `IsDownloading`, disposes CTS → Task 1, Step 5. ✓
- `CancelDownloadCommand` with `CanExecute = IsDownloading` → Task 1, Step 6. ✓
- `IsBusy` unchanged (still true during download) → Task 1 keeps `IsBusy = true/false`. ✓
- Single-button `DataTrigger` toggle, no new converter, `BasedOn` AccentButton preserved → Task 2. ✓
- Tests in `YoutubeDownloader.Core.Tests` with a fake/mock that blocks until cancelled → Task 1, Step 1. ✓
- Out-of-scope items (Fetch cancel, pause/resume, multi-download) → not implemented. ✓

**2. Placeholder scan:** No TBD/TODO/"handle edge cases"/"similar to". All code blocks are concrete. ✓

**3. Type consistency:** `IsDownloading` (bool), `CancelDownloadCommand` (generated from `CancelDownload`), `CanCancelDownload()`, `_downloadCts`, `token` used identically across Task 1 steps and referenced correctly in Task 2. Status string `"Download cancelled."` identical in test (Step 1) and implementation (Step 5). ✓
