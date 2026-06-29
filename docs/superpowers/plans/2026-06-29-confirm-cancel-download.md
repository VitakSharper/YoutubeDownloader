# Confirm Before Cancelling a Download — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** When the user clicks Cancel during a download, show a native Yes/No confirmation; Yes cancels, No (the default) lets the download continue.

**Architecture:** Introduce a mockable `IConfirmationService` in `Core/Services` (mirroring `ISaveFileService`) with a thin WPF `MessageBox` implementation in `App/Services`, registered in DI. `MainViewModel.CancelDownload()` consults it before calling `_downloadCts?.Cancel()`. The view-model branch is covered by xUnit/Moq tests; the WPF `MessageBox` wrapper is verified manually.

**Tech Stack:** .NET 8, WPF, CommunityToolkit.Mvvm (`[RelayCommand]`), xUnit + Moq.

## Global Constraints

- Target framework: `net8.0`.
- MVVM via CommunityToolkit.Mvvm source generators — `[RelayCommand]`, never hand-rolled commands.
- C# style in this repo: file-scoped namespaces, nullable reference types enabled, implicit usings, fields prefixed `_`, 4-space indentation.
- Confirmation copy is exactly: message `"Cancel the current download? Your progress will be lost."`, title `"Cancel download?"`.
- Native `MessageBox` only — no custom dialog window or button relabelling. **No** is the default button.
- The existing cancel status text `"Download cancelled."` is unchanged.
- Out of scope (do NOT implement): confirmation on any other action, wording configuration, localization.

---

### Task 1: Confirmation service + gated CancelDownload (view model)

**Files:**
- Create: `YoutubeDownloader.Core/Services/IConfirmationService.cs`
- Modify: `YoutubeDownloader.Core/ViewModels/MainViewModel.cs` (constructor + `CancelDownload`)
- Test: `YoutubeDownloader.Core.Tests/MainViewModelTests.cs` (fixture + 2 new tests)

**Interfaces:**
- Produces: `IConfirmationService.Confirm(string message, string title) -> bool` (true = confirmed). `MainViewModel` constructor gains a final `IConfirmationService confirm` parameter.
- Consumes (already present): the `_youtube`/`_ffmpeg`/`_temp`/`_saveFile` mocks, `CreateSut()`, and `SampleInfo()` in the test fixture; the per-download `CancellationTokenSource _downloadCts` in `MainViewModel`.

- [ ] **Step 1: Create the `IConfirmationService` interface**

Create `YoutubeDownloader.Core/Services/IConfirmationService.cs`:

```csharp
namespace YoutubeDownloader.Core.Services;

public interface IConfirmationService
{
    /// <summary>Ask the user to confirm an action. Returns true if confirmed.</summary>
    bool Confirm(string message, string title);
}
```

- [ ] **Step 2: Write the failing tests (and the fixture changes they need)**

In `YoutubeDownloader.Core.Tests/MainViewModelTests.cs`:

1. Add a mock field alongside the others (after `_clipboard`):

```csharp
    private readonly Mock<IConfirmationService> _confirm = new();
```

2. In the `MainViewModelTests()` constructor, default confirmation to **true** so the existing `Download_Cancelled_...` test (which expects cancellation) keeps passing:

```csharp
        _confirm.Setup(c => c.Confirm(It.IsAny<string>(), It.IsAny<string>())).Returns(true);
```

3. Update `CreateSut()` to pass the new dependency (it becomes the last constructor argument):

```csharp
    private MainViewModel CreateSut() =>
        new(_youtube.Object, _ffmpeg.Object, _converter.Object, _saveFile.Object, _temp.Object,
            _history.Object, _linkOpener.Object, _fileRevealer.Object, _settings.Object, _clipboard.Object,
            _confirm.Object);
```

4. Add these two tests inside the class. Both gate the download on a `TaskCompletionSource` so the test controls exactly when the in-flight download finishes:

```csharp
    [Fact]
    public async Task CancelDownload_WhenUserConfirms_CancelsAndAsksForConfirmation()
    {
        var info = SampleInfo();
        _youtube.Setup(s => s.GetVideoInfoAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(info);
        _ffmpeg.Setup(f => f.EnsureAsync(It.IsAny<IProgress<double>?>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync(@"C:\ffmpeg\ffmpeg.exe");
        _temp.Setup(t => t.NewTempFile(It.IsAny<string>())).Returns(@"C:\temp\a.webm");
        _saveFile.Setup(s => s.PromptForSavePath(It.IsAny<string>(), "mp3", It.IsAny<string?>())).Returns(@"C:\out\song.mp3");

        // The audio download blocks until the token is cancelled.
        var gate = new TaskCompletionSource();
        _youtube.Setup(s => s.DownloadStreamAsync(It.IsAny<IStreamInfo>(), It.IsAny<string>(),
                    It.IsAny<IProgress<double>?>(), It.IsAny<CancellationToken>()))
                .Returns((IStreamInfo _, string _, IProgress<double>? _, CancellationToken ct) => gate.Task.WaitAsync(ct));

        _confirm.Setup(c => c.Confirm(It.IsAny<string>(), It.IsAny<string>())).Returns(true);

        var vm = CreateSut();
        vm.Url = "https://youtu.be/dQw4w9WgXcQ";
        await vm.FetchInfoCommand.ExecuteAsync(null);
        vm.SelectedMode = DownloadMode.AudioMp3;

        var download = vm.DownloadCommand.ExecuteAsync(null);
        Assert.True(vm.IsDownloading);

        vm.CancelDownloadCommand.Execute(null);
        await download;

        Assert.Equal("Download cancelled.", vm.StatusMessage);
        Assert.False(vm.IsDownloading);
        _confirm.Verify(c => c.Confirm(It.IsAny<string>(), It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task CancelDownload_WhenUserDeclines_LetsDownloadContinue()
    {
        var info = SampleInfo();
        _youtube.Setup(s => s.GetVideoInfoAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(info);
        _ffmpeg.Setup(f => f.EnsureAsync(It.IsAny<IProgress<double>?>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync(@"C:\ffmpeg\ffmpeg.exe");
        _temp.Setup(t => t.NewTempFile(It.IsAny<string>())).Returns(@"C:\temp\a.webm");
        _saveFile.Setup(s => s.PromptForSavePath(It.IsAny<string>(), "mp3", It.IsAny<string?>())).Returns(@"C:\out\song.mp3");

        var gate = new TaskCompletionSource();
        _youtube.Setup(s => s.DownloadStreamAsync(It.IsAny<IStreamInfo>(), It.IsAny<string>(),
                    It.IsAny<IProgress<double>?>(), It.IsAny<CancellationToken>()))
                .Returns((IStreamInfo _, string _, IProgress<double>? _, CancellationToken ct) => gate.Task.WaitAsync(ct));

        _confirm.Setup(c => c.Confirm(It.IsAny<string>(), It.IsAny<string>())).Returns(false);

        var vm = CreateSut();
        vm.Url = "https://youtu.be/dQw4w9WgXcQ";
        await vm.FetchInfoCommand.ExecuteAsync(null);
        vm.SelectedMode = DownloadMode.AudioMp3;

        var download = vm.DownloadCommand.ExecuteAsync(null);
        Assert.True(vm.IsDownloading);

        // User declines the cancel: the download must keep running.
        vm.CancelDownloadCommand.Execute(null);
        Assert.NotEqual("Download cancelled.", vm.StatusMessage);
        Assert.True(vm.IsDownloading);
        _confirm.Verify(c => c.Confirm(It.IsAny<string>(), It.IsAny<string>()), Times.Once);

        // Let the (still-running) download finish normally.
        gate.SetResult();
        await download;

        Assert.Equal(@"C:\out\song.mp3", Assert.Single(vm.History).FilePath);
        Assert.False(vm.IsDownloading);
    }
```

- [ ] **Step 3: Run the tests to verify they fail**

Run: `dotnet test YoutubeDownloader.Core.Tests/YoutubeDownloader.Core.Tests.csproj --filter "FullyQualifiedName~CancelDownload_WhenUser" -v minimal`
Expected: **build FAILS** — `MainViewModel`'s constructor does not accept the 11th argument `_confirm.Object` (the parameter does not exist yet). This is the RED state.

- [ ] **Step 4: Implement the constructor dependency and gate `CancelDownload`**

In `YoutubeDownloader.Core/ViewModels/MainViewModel.cs`:

1. Add the field with the other readonly service fields (after `_clipboard`):

```csharp
    private readonly IConfirmationService _confirm;
```

2. Add `IConfirmationService confirm` as the final constructor parameter and assign it. The constructor signature and the new assignment:

```csharp
    public MainViewModel(IYouTubeService youtube, IFFmpegLocator ffmpeg, IMediaConverter converter,
        ISaveFileService saveFile, ITempFileService temp, IHistoryStore history, ILinkOpener linkOpener,
        IFileRevealer fileRevealer, ISettingsStore settings, IClipboardService clipboard,
        IConfirmationService confirm)
    {
```

and, in the constructor body next to `_clipboard = clipboard;`:

```csharp
        _confirm = confirm;
```

3. Replace `CancelDownload` (currently `private void CancelDownload() => _downloadCts?.Cancel();`) with the gated version:

```csharp
    [RelayCommand(CanExecute = nameof(CanCancelDownload))]
    private void CancelDownload()
    {
        if (_confirm.Confirm("Cancel the current download? Your progress will be lost.", "Cancel download?"))
            _downloadCts?.Cancel();
    }
```

`CanCancelDownload()` is unchanged.

- [ ] **Step 5: Run the full test project to verify GREEN**

Run: `dotnet test YoutubeDownloader.Core.Tests/YoutubeDownloader.Core.Tests.csproj -v minimal`
Expected: **PASS**, all tests (the two new ones plus the existing suite, including `Download_Cancelled_...` which now cancels via the defaulted `Confirm → true`). Output pristine — no warnings.

- [ ] **Step 6: Commit**

```bash
git add YoutubeDownloader.Core/Services/IConfirmationService.cs YoutubeDownloader.Core/ViewModels/MainViewModel.cs YoutubeDownloader.Core.Tests/MainViewModelTests.cs
git commit -m "feat: confirm before cancelling a download (view model)"
```

---

### Task 2: WPF MessageBox implementation + DI registration

**Files:**
- Create: `YoutubeDownloader.App/Services/MessageBoxConfirmationService.cs`
- Modify: `YoutubeDownloader.App/App.xaml.cs` (DI registration)

**Interfaces:**
- Consumes: `IConfirmationService` (from Task 1).
- Produces: a concrete `MessageBoxConfirmationService` registered so `MainViewModel` resolves at runtime.

No unit test: `MessageBox` cannot be exercised headlessly. This task is verified by a clean build and the manual check in Step 4.

- [ ] **Step 1: Create the WPF implementation**

Create `YoutubeDownloader.App/Services/MessageBoxConfirmationService.cs`:

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

- [ ] **Step 2: Register it in DI**

In `YoutubeDownloader.App/App.xaml.cs`, add this line with the other `AddSingleton` calls, immediately before `services.AddSingleton<MainViewModel>();`:

```csharp
        services.AddSingleton<IConfirmationService, MessageBoxConfirmationService>();
```

- [ ] **Step 3: Build the whole solution**

Run: `dotnet build YoutubeDownloader.sln --nologo`
Expected: **Build succeeded. 0 Warning(s). 0 Error(s).** (DI now satisfies `MainViewModel`'s new `IConfirmationService` parameter.)

- [ ] **Step 4: Manual verification**

Run the app (`dotnet run --project YoutubeDownloader.App` or launch the built exe). Fetch a video, start a download, and click **Cancel**:
- A native dialog titled "Cancel download?" appears with **Yes** / **No**, focus on **No**.
- **No** (or Esc/Enter) → dialog closes, download keeps going.
- Click Cancel again, choose **Yes** → download stops, status shows "Download cancelled.", button reverts to "Download".

- [ ] **Step 5: Commit**

```bash
git add YoutubeDownloader.App/Services/MessageBoxConfirmationService.cs YoutubeDownloader.App/App.xaml.cs
git commit -m "feat: wire native MessageBox confirmation for cancel + DI"
```

---

## Done When

- Both new view-model tests pass and the full `YoutubeDownloader.Core.Tests` suite is green with pristine output.
- `dotnet build YoutubeDownloader.sln` succeeds with 0 warnings / 0 errors.
- Manual check confirms the Yes/No dialog gates cancellation as described.
