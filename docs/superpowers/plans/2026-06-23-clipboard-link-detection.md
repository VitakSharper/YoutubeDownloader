# Clipboard Link Detection Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** When the app window gains focus, detect a YouTube link on the clipboard and offer a one-click "fill and fetch" via a dismissible suggestion banner.

**Architecture:** Clipboard access sits behind a new `IClipboardService` in Core (mirroring the existing `ILinkOpener`/`IFileRevealer` pattern), implemented by `WpfClipboardService` in the App. Detection state and the `CheckClipboard` / `UseDetectedLink` / `DismissDetectedLink` commands live on `MainViewModel`, so all detection logic is unit-testable. `MainWindow` triggers the check on its `Activated` event.

**Tech Stack:** C# / .NET 8, WPF, CommunityToolkit.Mvvm (`[ObservableProperty]`, `[RelayCommand]`), Microsoft.Extensions.DependencyInjection; tests in xUnit + Moq.

## Global Constraints

- Target frameworks: `YoutubeDownloader.Core` and `YoutubeDownloader.Core.Tests` are `net8.0`; `YoutubeDownloader.App` is `net8.0-windows`.
- `MainViewModel` lives in Core and must NOT reference WPF/`System.Windows`. All platform calls go through interfaces in `YoutubeDownloader.Core.Services`.
- Use CommunityToolkit.Mvvm source generators: `[ObservableProperty]` on a `private` backing field; `[RelayCommand]` on a method. An async method `FooAsync` generates `FooCommand`.
- Link identity is compared by **YouTube video id** via `YouTubeUrlValidator.GetVideoId(string?)` (returns `null` for non-YouTube text), so `youtu.be/X` and `youtube.com/watch?v=X` are treated as the same link.
- Tests use the existing `MainViewModelTests` conventions: `Mock<T>` fields + a `CreateSut()` factory.

---

## File Structure

- `YoutubeDownloader.Core/Services/IClipboardService.cs` *(new)* — clipboard text abstraction.
- `YoutubeDownloader.Core/ViewModels/MainViewModel.cs` *(modify)* — inject `IClipboardService`; add detection state + 3 commands.
- `YoutubeDownloader.Core.Tests/MainViewModelTests.cs` *(modify)* — add `Mock<IClipboardService>`, update `CreateSut()`, add detection tests.
- `YoutubeDownloader.App/Services/WpfClipboardService.cs` *(new)* — WPF clipboard implementation.
- `YoutubeDownloader.App/App.xaml.cs` *(modify)* — register `IClipboardService`.
- `YoutubeDownloader.App/MainWindow.xaml.cs` *(modify)* — call `CheckClipboard` on `Activated`.
- `YoutubeDownloader.App/MainWindow.xaml` *(modify)* — add the suggestion banner above the URL row.

---

## Task 1: Introduce `IClipboardService` and inject into `MainViewModel`

Adds the interface and a no-op constructor dependency. No new behavior yet; the gate is that the whole solution still compiles and every existing test stays green after the constructor signature changes.

**Files:**
- Create: `YoutubeDownloader.Core/Services/IClipboardService.cs`
- Modify: `YoutubeDownloader.Core/ViewModels/MainViewModel.cs` (constructor + fields, around lines 15-48)
- Modify: `YoutubeDownloader.Core.Tests/MainViewModelTests.cs` (fields + `CreateSut`, lines 11-29)

**Interfaces:**
- Produces: `interface IClipboardService { string? GetText(); }` in namespace `YoutubeDownloader.Core.Services`.
- Produces: `MainViewModel` constructor gains a trailing `IClipboardService clipboard` parameter; stored as `private readonly IClipboardService _clipboard;`.

- [ ] **Step 1: Create the interface**

Create `YoutubeDownloader.Core/Services/IClipboardService.cs`:

```csharp
namespace YoutubeDownloader.Core.Services;

/// <summary>Reads the current text contents of the system clipboard.</summary>
public interface IClipboardService
{
    /// <summary>The clipboard's text, or <c>null</c> if it is empty, non-text, or unavailable.</summary>
    string? GetText();
}
```

- [ ] **Step 2: Add the dependency to `MainViewModel`**

In `YoutubeDownloader.Core/ViewModels/MainViewModel.cs`, add a field next to the other service fields (after `private readonly ISettingsStore _settings;`, line ~23):

```csharp
    private readonly IClipboardService _clipboard;
```

Append `IClipboardService clipboard` as the final constructor parameter:

```csharp
    public MainViewModel(IYouTubeService youtube, IFFmpegLocator ffmpeg, IMediaConverter converter,
        ISaveFileService saveFile, ITempFileService temp, IHistoryStore history, ILinkOpener linkOpener,
        IFileRevealer fileRevealer, ISettingsStore settings, IClipboardService clipboard)
    {
```

Assign it inside the constructor body (next to `_settings = settings;`):

```csharp
        _clipboard = clipboard;
```

- [ ] **Step 3: Update the test SUT factory**

In `YoutubeDownloader.Core.Tests/MainViewModelTests.cs`, add the mock field with the others (after line 19):

```csharp
    private readonly Mock<IClipboardService> _clipboard = new();
```

Update `CreateSut()` (lines 27-29) to pass it last:

```csharp
    private MainViewModel CreateSut() =>
        new(_youtube.Object, _ffmpeg.Object, _converter.Object, _saveFile.Object, _temp.Object,
            _history.Object, _linkOpener.Object, _fileRevealer.Object, _settings.Object, _clipboard.Object);
```

- [ ] **Step 4: Build and run the full test suite — everything still passes**

Run: `dotnet test YoutubeDownloader.Core.Tests/YoutubeDownloader.Core.Tests.csproj`
Expected: PASS — all existing tests green (the constructor change compiles; `_clipboard.Object.GetText()` is never called yet).

- [ ] **Step 5: Commit**

```bash
git add YoutubeDownloader.Core/Services/IClipboardService.cs YoutubeDownloader.Core/ViewModels/MainViewModel.cs YoutubeDownloader.Core.Tests/MainViewModelTests.cs
git commit -m "feat: add IClipboardService and inject into MainViewModel"
```

---

## Task 2: `CheckClipboard` detection logic (TDD)

Adds the `DetectedClipboardUrl` state and the `CheckClipboard` command that decides when a suggestion appears.

**Files:**
- Modify: `YoutubeDownloader.Core/ViewModels/MainViewModel.cs`
- Test: `YoutubeDownloader.Core.Tests/MainViewModelTests.cs`

**Interfaces:**
- Consumes: `IClipboardService.GetText()`, `YouTubeUrlValidator.GetVideoId(string?)`, the existing `Url` and `IsBusy` properties.
- Produces: `string? DetectedClipboardUrl` observable property (non-null/non-empty ⇒ banner visible); `CheckClipboardCommand` (generated from `void CheckClipboard()`); a private `string? _dismissedVideoId` field used by Tasks 3 and 4.

- [ ] **Step 1: Write the failing tests**

Add to `YoutubeDownloader.Core.Tests/MainViewModelTests.cs`:

```csharp
    [Fact]
    public void CheckClipboard_ValidNewLink_SetsSuggestion()
    {
        _clipboard.Setup(c => c.GetText()).Returns("https://youtu.be/dQw4w9WgXcQ");
        var vm = CreateSut();

        vm.CheckClipboardCommand.Execute(null);

        Assert.Equal("https://youtu.be/dQw4w9WgXcQ", vm.DetectedClipboardUrl);
    }

    [Fact]
    public void CheckClipboard_NonYouTubeText_NoSuggestion()
    {
        _clipboard.Setup(c => c.GetText()).Returns("just some text");
        var vm = CreateSut();

        vm.CheckClipboardCommand.Execute(null);

        Assert.Null(vm.DetectedClipboardUrl);
    }

    [Fact]
    public void CheckClipboard_EmptyClipboard_NoSuggestion()
    {
        _clipboard.Setup(c => c.GetText()).Returns((string?)null);
        var vm = CreateSut();

        vm.CheckClipboardCommand.Execute(null);

        Assert.Null(vm.DetectedClipboardUrl);
    }

    [Fact]
    public void CheckClipboard_LinkSameVideoAsUrlBox_NoSuggestion()
    {
        _clipboard.Setup(c => c.GetText()).Returns("https://youtu.be/dQw4w9WgXcQ");
        var vm = CreateSut();
        vm.Url = "https://www.youtube.com/watch?v=dQw4w9WgXcQ"; // same video, different URL form

        vm.CheckClipboardCommand.Execute(null);

        Assert.Null(vm.DetectedClipboardUrl);
    }

    [Fact]
    public void CheckClipboard_WhileBusy_NoSuggestion()
    {
        _clipboard.Setup(c => c.GetText()).Returns("https://youtu.be/dQw4w9WgXcQ");
        var vm = CreateSut();
        typeof(MainViewModel).GetProperty(nameof(MainViewModel.IsBusy))!.SetValue(vm, true);

        vm.CheckClipboardCommand.Execute(null);

        Assert.Null(vm.DetectedClipboardUrl);
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test YoutubeDownloader.Core.Tests/YoutubeDownloader.Core.Tests.csproj --filter "FullyQualifiedName~CheckClipboard"`
Expected: FAIL to compile — `DetectedClipboardUrl` and `CheckClipboardCommand` do not exist yet.

- [ ] **Step 3: Implement the detection logic**

In `YoutubeDownloader.Core/ViewModels/MainViewModel.cs`, add the state near the other `[ObservableProperty]` fields (e.g. after `_url`, around line 87):

```csharp
    /// <summary>A YouTube link detected on the clipboard, surfaced as a suggestion. Null hides the banner.</summary>
    [ObservableProperty]
    private string? _detectedClipboardUrl;

    /// <summary>Video id of the last suggestion the user used or dismissed, so it is not re-offered.</summary>
    private string? _dismissedVideoId;
```

Add the command alongside the other `[RelayCommand]` methods (e.g. after `FetchInfoAsync`, around line 178):

```csharp
    /// <summary>
    /// Inspect the clipboard and surface a YouTube link as a suggestion when it is new.
    /// Called when the window gains focus.
    /// </summary>
    [RelayCommand]
    private void CheckClipboard()
    {
        if (IsBusy)
        {
            DetectedClipboardUrl = null;
            return;
        }

        var text = _clipboard.GetText();
        var videoId = YouTubeUrlValidator.GetVideoId(text);

        if (videoId is null
            || videoId == YouTubeUrlValidator.GetVideoId(Url)
            || videoId == _dismissedVideoId)
        {
            DetectedClipboardUrl = null;
            return;
        }

        DetectedClipboardUrl = text;
    }
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test YoutubeDownloader.Core.Tests/YoutubeDownloader.Core.Tests.csproj --filter "FullyQualifiedName~CheckClipboard"`
Expected: PASS — all 5 new tests green.

- [ ] **Step 5: Commit**

```bash
git add YoutubeDownloader.Core/ViewModels/MainViewModel.cs YoutubeDownloader.Core.Tests/MainViewModelTests.cs
git commit -m "feat: detect new YouTube link on clipboard via CheckClipboard"
```

---

## Task 3: `DismissDetectedLink` and same-link suppression (TDD)

Adds the dismiss command and verifies a dismissed link does not nag again, while a different link does.

**Files:**
- Modify: `YoutubeDownloader.Core/ViewModels/MainViewModel.cs`
- Test: `YoutubeDownloader.Core.Tests/MainViewModelTests.cs`

**Interfaces:**
- Consumes: `DetectedClipboardUrl`, `_dismissedVideoId`, `YouTubeUrlValidator.GetVideoId`.
- Produces: `DismissDetectedLinkCommand` (generated from `void DismissDetectedLink()`).

- [ ] **Step 1: Write the failing tests**

Add to `YoutubeDownloader.Core.Tests/MainViewModelTests.cs`:

```csharp
    [Fact]
    public void DismissDetectedLink_HidesSuggestion()
    {
        _clipboard.Setup(c => c.GetText()).Returns("https://youtu.be/dQw4w9WgXcQ");
        var vm = CreateSut();
        vm.CheckClipboardCommand.Execute(null);
        Assert.NotNull(vm.DetectedClipboardUrl);

        vm.DismissDetectedLinkCommand.Execute(null);

        Assert.Null(vm.DetectedClipboardUrl);
    }

    [Fact]
    public void CheckClipboard_AfterDismiss_SameLinkStaysSuppressed()
    {
        _clipboard.Setup(c => c.GetText()).Returns("https://youtu.be/dQw4w9WgXcQ");
        var vm = CreateSut();
        vm.CheckClipboardCommand.Execute(null);
        vm.DismissDetectedLinkCommand.Execute(null);

        vm.CheckClipboardCommand.Execute(null); // same link still on clipboard

        Assert.Null(vm.DetectedClipboardUrl);
    }

    [Fact]
    public void CheckClipboard_AfterDismiss_DifferentLinkReappears()
    {
        _clipboard.Setup(c => c.GetText()).Returns("https://youtu.be/dQw4w9WgXcQ");
        var vm = CreateSut();
        vm.CheckClipboardCommand.Execute(null);
        vm.DismissDetectedLinkCommand.Execute(null);

        _clipboard.Setup(c => c.GetText()).Returns("https://youtu.be/oHg5SJYRHA0");
        vm.CheckClipboardCommand.Execute(null);

        Assert.Equal("https://youtu.be/oHg5SJYRHA0", vm.DetectedClipboardUrl);
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test YoutubeDownloader.Core.Tests/YoutubeDownloader.Core.Tests.csproj --filter "FullyQualifiedName~Dismiss"`
Expected: FAIL to compile — `DismissDetectedLinkCommand` does not exist yet.

- [ ] **Step 3: Implement the dismiss command**

In `YoutubeDownloader.Core/ViewModels/MainViewModel.cs`, add after `CheckClipboard`:

```csharp
    /// <summary>Dismiss the current suggestion and suppress this same link from reappearing.</summary>
    [RelayCommand]
    private void DismissDetectedLink()
    {
        _dismissedVideoId = YouTubeUrlValidator.GetVideoId(DetectedClipboardUrl);
        DetectedClipboardUrl = null;
    }
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test YoutubeDownloader.Core.Tests/YoutubeDownloader.Core.Tests.csproj --filter "FullyQualifiedName~Dismiss OR FullyQualifiedName~CheckClipboard"`
Expected: PASS — dismiss + suppression tests and the earlier detection tests all green.

- [ ] **Step 5: Commit**

```bash
git add YoutubeDownloader.Core/ViewModels/MainViewModel.cs YoutubeDownloader.Core.Tests/MainViewModelTests.cs
git commit -m "feat: dismiss clipboard suggestion and suppress the same link"
```

---

## Task 4: `UseDetectedLink` fill-and-fetch (TDD)

Adds the "Use it" command: fill the URL box, hide the banner, suppress that link, and run Fetch.

**Files:**
- Modify: `YoutubeDownloader.Core/ViewModels/MainViewModel.cs`
- Test: `YoutubeDownloader.Core.Tests/MainViewModelTests.cs`

**Interfaces:**
- Consumes: `DetectedClipboardUrl`, `_dismissedVideoId`, `Url`, the existing `FetchInfoCommand` (an `IAsyncRelayCommand` with `CanExecute`/`ExecuteAsync`).
- Produces: `UseDetectedLinkCommand` (generated from `Task UseDetectedLinkAsync()`).

- [ ] **Step 1: Write the failing tests**

Add to `YoutubeDownloader.Core.Tests/MainViewModelTests.cs`:

```csharp
    [Fact]
    public async Task UseDetectedLink_SetsUrlClearsBannerAndFetches()
    {
        var info = SampleInfo();
        _youtube.Setup(s => s.GetVideoInfoAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(info);
        _clipboard.Setup(c => c.GetText()).Returns("https://youtu.be/dQw4w9WgXcQ");
        var vm = CreateSut();
        vm.CheckClipboardCommand.Execute(null);

        await vm.UseDetectedLinkCommand.ExecuteAsync(null);

        Assert.Equal("https://youtu.be/dQw4w9WgXcQ", vm.Url);
        Assert.Null(vm.DetectedClipboardUrl);
        Assert.Same(info, vm.VideoInfo);
        _youtube.Verify(s => s.GetVideoInfoAsync("https://youtu.be/dQw4w9WgXcQ", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UseDetectedLink_NoSuggestion_DoesNothing()
    {
        var vm = CreateSut(); // DetectedClipboardUrl is null

        await vm.UseDetectedLinkCommand.ExecuteAsync(null);

        Assert.Equal("", vm.Url);
        _youtube.Verify(s => s.GetVideoInfoAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task UseDetectedLink_ThenCheckClipboard_DoesNotReappear()
    {
        _youtube.Setup(s => s.GetVideoInfoAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(SampleInfo());
        _clipboard.Setup(c => c.GetText()).Returns("https://youtu.be/dQw4w9WgXcQ");
        var vm = CreateSut();
        vm.CheckClipboardCommand.Execute(null);
        await vm.UseDetectedLinkCommand.ExecuteAsync(null);

        vm.CheckClipboardCommand.Execute(null); // same link still on clipboard

        Assert.Null(vm.DetectedClipboardUrl);
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test YoutubeDownloader.Core.Tests/YoutubeDownloader.Core.Tests.csproj --filter "FullyQualifiedName~UseDetectedLink"`
Expected: FAIL to compile — `UseDetectedLinkCommand` does not exist yet.

- [ ] **Step 3: Implement the command**

In `YoutubeDownloader.Core/ViewModels/MainViewModel.cs`, add after `DismissDetectedLink`:

```csharp
    /// <summary>Accept the suggested link: fill the box, suppress re-offering it, then fetch its info.</summary>
    [RelayCommand]
    private async Task UseDetectedLinkAsync()
    {
        var link = DetectedClipboardUrl;
        if (string.IsNullOrWhiteSpace(link))
            return;

        _dismissedVideoId = YouTubeUrlValidator.GetVideoId(link);
        DetectedClipboardUrl = null;
        Url = link;

        if (FetchInfoCommand.CanExecute(null))
            await FetchInfoCommand.ExecuteAsync(null);
    }
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test YoutubeDownloader.Core.Tests/YoutubeDownloader.Core.Tests.csproj`
Expected: PASS — the full suite (all prior tests plus the three `UseDetectedLink` tests) green.

- [ ] **Step 5: Commit**

```bash
git add YoutubeDownloader.Core/ViewModels/MainViewModel.cs YoutubeDownloader.Core.Tests/MainViewModelTests.cs
git commit -m "feat: use detected clipboard link to fill and fetch"
```

---

## Task 5: Wire up WPF clipboard, window hook, and banner UI

Implements the platform pieces and the visible banner, then verifies end-to-end in the running app. No unit tests (WPF clipboard + UI); gate is a clean build + the documented manual check.

**Files:**
- Create: `YoutubeDownloader.App/Services/WpfClipboardService.cs`
- Modify: `YoutubeDownloader.App/App.xaml.cs` (DI registration, near lines 23-34)
- Modify: `YoutubeDownloader.App/MainWindow.xaml.cs` (constructor)
- Modify: `YoutubeDownloader.App/MainWindow.xaml` (URL row, lines 223-234)

**Interfaces:**
- Consumes: `IClipboardService` (Task 1), `CheckClipboardCommand` (Task 2), `UseDetectedLinkCommand` (Task 4), `DismissDetectedLinkCommand` (Task 3), `DetectedClipboardUrl` (Task 2), and the existing `NonEmptyToVis` converter + `ChipButton` style in `MainWindow.xaml`.

- [ ] **Step 1: Implement the WPF clipboard service**

Create `YoutubeDownloader.App/Services/WpfClipboardService.cs`:

```csharp
using System.Windows;
using YoutubeDownloader.Core.Services;

namespace YoutubeDownloader.App.Services;

/// <summary>Reads text from the Windows clipboard. Returns null when empty, non-text, or locked.</summary>
public sealed class WpfClipboardService : IClipboardService
{
    public string? GetText()
    {
        try
        {
            return Clipboard.ContainsText() ? Clipboard.GetText() : null;
        }
        catch
        {
            // The clipboard can be momentarily locked by another process; treat as empty.
            return null;
        }
    }
}
```

- [ ] **Step 2: Register the service in DI**

In `YoutubeDownloader.App/App.xaml.cs`, add alongside the other `AddSingleton` registrations (e.g. after the `IFileRevealer` line, ~line 34):

```csharp
        services.AddSingleton<IClipboardService, WpfClipboardService>();
```

(`using YoutubeDownloader.App.Services;` and `using YoutubeDownloader.Core.Services;` are already present.)

- [ ] **Step 3: Trigger the check when the window gains focus**

Replace the body of `YoutubeDownloader.App/MainWindow.xaml.cs` with:

```csharp
using System.Windows;
using YoutubeDownloader.Core.ViewModels;

namespace YoutubeDownloader.App;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        // Fires on first show (covers startup) and on every refocus.
        Activated += (_, _) => (DataContext as MainViewModel)?.CheckClipboardCommand.Execute(null);
    }
}
```

- [ ] **Step 4: Add the suggestion banner above the URL box**

In `YoutubeDownloader.App/MainWindow.xaml`, replace the existing URL-row block (lines 223-234):

```xml
                        <!-- URL row -->
                        <Grid Grid.Row="0">
                            <Grid.ColumnDefinitions>
                                <ColumnDefinition Width="*" />
                                <ColumnDefinition Width="Auto" />
                            </Grid.ColumnDefinitions>
                            <TextBox Grid.Column="0" Margin="0,0,10,0" Style="{StaticResource GlassTextBox}"
                                     Text="{Binding Url, UpdateSourceTrigger=PropertyChanged}"
                                     IsEnabled="{Binding IsBusy, Converter={StaticResource InverseBool}}" />
                            <Button Grid.Column="1" Content="Fetch" Style="{StaticResource AccentButton}"
                                    Command="{Binding FetchInfoCommand}" />
                        </Grid>
```

with this (a `StackPanel` wrapping a banner + the original URL grid, so no other rows need renumbering):

```xml
                        <!-- URL row, with clipboard-suggestion banner above it -->
                        <StackPanel Grid.Row="0">
                            <Border Margin="0,0,0,12" CornerRadius="10" Padding="12,9"
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
                            <Grid>
                                <Grid.ColumnDefinitions>
                                    <ColumnDefinition Width="*" />
                                    <ColumnDefinition Width="Auto" />
                                </Grid.ColumnDefinitions>
                                <TextBox Grid.Column="0" Margin="0,0,10,0" Style="{StaticResource GlassTextBox}"
                                         Text="{Binding Url, UpdateSourceTrigger=PropertyChanged}"
                                         IsEnabled="{Binding IsBusy, Converter={StaticResource InverseBool}}" />
                                <Button Grid.Column="1" Content="Fetch" Style="{StaticResource AccentButton}"
                                        Command="{Binding FetchInfoCommand}" />
                            </Grid>
                        </StackPanel>
```

- [ ] **Step 5: Build the whole solution**

Run: `dotnet build YoutubeDownloader.sln -c Debug`
Expected: PASS — 0 errors.

- [ ] **Step 6: Manual end-to-end verification**

Run: `dotnet run --project YoutubeDownloader.App` (launch in your own interactive session if the agent shell can't keep the window visible).

Verify:
1. Copy a YouTube link (e.g. `https://youtu.be/dQw4w9WgXcQ`) in the browser, then click back into the app → the banner appears: *"📋 Found a YouTube link on your clipboard."*
2. Click **Use it** → the link fills the URL box and the video info loads (Fetch runs automatically).
3. Copy the same link again, refocus → no banner (suppressed). Copy a *different* YouTube link, refocus → banner reappears.
4. With a banner showing, click **✕** → it disappears and does not return for that same link.
5. Copy non-YouTube text, refocus → no banner.

- [ ] **Step 7: Commit**

```bash
git add YoutubeDownloader.App/Services/WpfClipboardService.cs YoutubeDownloader.App/App.xaml.cs YoutubeDownloader.App/MainWindow.xaml.cs YoutubeDownloader.App/MainWindow.xaml
git commit -m "feat: clipboard link suggestion banner in the app UI"
```

---

## Self-Review

**Spec coverage:**
- Detect on focus + startup → Task 5 Step 3 (`Activated`, fires on first show). ✓
- Banner non-destructive, validity/new/dismissed/not-busy gating → Task 2. ✓
- Use it = fill + fetch → Task 4. ✓
- ✕ = dismiss + suppress same link (by video id) → Task 3. ✓
- Clipboard behind `IClipboardService` in Core; `WpfClipboardService` in App; DI registration → Tasks 1 & 5. ✓
- Error handling (clipboard locked/non-text → null) → Task 5 Step 1. ✓
- UI thread only (Activated handler) → Task 5 Step 3. ✓
- Tests: valid-new, invalid/empty, same-as-box, busy, dismiss-suppress, different-reappears, use-it → Tasks 2-4. ✓

**Deviations from the spec (intentional refinements):**
- Tests use `Mock<IClipboardService>` (Moq) rather than a hand-written `FakeClipboardService`, matching the existing `MainViewModelTests` convention.
- The banner is added by wrapping the URL row in a `StackPanel` instead of inserting a new grid row and renumbering five children — less churn, same layout.

**Placeholder scan:** No TBD/TODO; every code step shows complete code. ✓

**Type consistency:** `DetectedClipboardUrl`, `_dismissedVideoId`, `CheckClipboardCommand`, `DismissDetectedLinkCommand`, `UseDetectedLinkCommand`, and `IClipboardService.GetText()` are used identically across tasks and the XAML bindings. ✓
