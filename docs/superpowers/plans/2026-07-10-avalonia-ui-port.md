# Avalonia UI Port — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a new Avalonia UI front-end (`YoutubeDownloader.Avalonia`) that clones the existing WPF app with a custom modern dark UI, reusing `YoutubeDownloader.Core` unchanged except for a minimal async adaptation. The WPF app keeps working.

**Architecture:** New Avalonia "head" project references the existing UI-agnostic `Core` (which holds `MainViewModel` and all service interfaces). Two dialog interfaces become async (Avalonia pickers/dialogs are async-only); clipboard stays synchronous via an activation-refreshed cache. Avalonia/Windows service implementations, converters, a thumbnail loader, and AXAML views live in the new project. `TopLevel` is resolved lazily from the desktop app lifetime, so services need no window injection.

**Tech Stack:** .NET 8, Avalonia 11.3.12 (Fluent theme + Inter font, compiled bindings), CommunityToolkit.Mvvm (via Core), Microsoft.Extensions.DependencyInjection, YoutubeExplode + Xabe.FFmpeg (via Core), xUnit + Moq (tests).

## Global Constraints

- Target framework for the new project: **`net8.0`** (matches `Core` net8.0 and WPF net8.0-windows). Override the template's `net10.0`.
- **Windows only.** File-reveal uses `explorer.exe /select,`; link-open uses `Process.Start(UseShellExecute=true)`.
- **Do not modify or remove** `YoutubeDownloader.App` (WPF) beyond the two service-impl signature changes in Task 1. WPF must still build and run.
- **Do not change** `Core` business logic beyond the async adaptation of `IConfirmationService` and `ISaveFileService` in Task 1. `IClipboardService.GetText()` stays synchronous.
- Preserve branding: name **"Liudochka Youtube Downloader"**, reuse `app.ico`.
- New project `AssemblyName` = `LiudochkaYoutubeDownloader.Avalonia` (distinct from WPF's `LiudochkaYoutubeDownloader`) so both build side by side.
- Generated command names must stay stable: renaming `CancelDownload` → `CancelDownloadAsync` keeps the generated `CancelDownloadCommand` (CommunityToolkit strips the `Async` suffix). Do not rename the command.
- Git: do **not** commit unless the user explicitly says so (personal repo, no ADO work item). The "Commit" steps below are written for completeness but are **gated on explicit user approval** — skip them otherwise.

---

## File Structure

**Modified — `Core`:**
- `YoutubeDownloader.Core/Services/IConfirmationService.cs` — method → async.
- `YoutubeDownloader.Core/Services/ISaveFileService.cs` — method → async.
- `YoutubeDownloader.Core/ViewModels/MainViewModel.cs` — 2 call sites → await async members; `CancelDownload` → `async Task CancelDownloadAsync`.

**Modified — WPF (`YoutubeDownloader.App`):**
- `Services/MessageBoxConfirmationService.cs` — implement `ConfirmAsync` (wrap sync `MessageBox`).
- `Services/SaveFileService.cs` — implement `PromptForSavePathAsync` (wrap sync `SaveFileDialog`).

**Modified — tests:**
- `YoutubeDownloader.Core.Tests/MainViewModelTests.cs` — mock setups/verifies → async members; cancel tests await the async command.

**Created — `YoutubeDownloader.Avalonia/`:**
- `YoutubeDownloader.Avalonia.csproj`, `app.manifest`, `Program.cs`
- `App.axaml`, `App.axaml.cs`
- `Infrastructure/TopLevelResolver.cs`
- `Services/AvaloniaClipboardService.cs`, `AvaloniaSaveFileService.cs`, `AvaloniaConfirmationService.cs`, `ExplorerFileRevealer.cs`, `ProcessLinkOpener.cs`
- `Converters/NullToBoolConverter.cs`, `StringNotEmptyToBoolConverter.cs`, `DownloadModeDisplayConverter.cs`, `Mp3BitrateDisplayConverter.cs`
- `Imaging/AsyncImage.cs`
- `Views/MainWindow.axaml`, `MainWindow.axaml.cs`, `ConfirmDialog.axaml`, `ConfirmDialog.axaml.cs`
- `Assets/app.ico`, `Assets/cassette.png`

---

## Task 1: Core async adaptation (behavior-preserving refactor)

Make the two dialog interfaces async, update `MainViewModel`, keep WPF impls working, and keep the test suite green. This is a pure refactor: same behavior, async signatures. The solution won't build mid-task; the deliverable is a green build + green tests.

**Files:**
- Modify: `YoutubeDownloader.Core/Services/IConfirmationService.cs`
- Modify: `YoutubeDownloader.Core/Services/ISaveFileService.cs`
- Modify: `YoutubeDownloader.Core/ViewModels/MainViewModel.cs:257`, `:341-346`
- Modify: `YoutubeDownloader.App/Services/MessageBoxConfirmationService.cs`
- Modify: `YoutubeDownloader.App/Services/SaveFileService.cs`
- Test: `YoutubeDownloader.Core.Tests/MainViewModelTests.cs`

**Interfaces:**
- Produces:
  - `IConfirmationService.ConfirmAsync(string message, string title) : Task<bool>`
  - `ISaveFileService.PromptForSavePathAsync(string suggestedFileName, string extension, string? initialDirectory) : Task<string?>`
  - `MainViewModel.CancelDownloadCommand` (now an `IAsyncRelayCommand`; name unchanged)

- [ ] **Step 1: Update the test mocks and cancel tests to the async members**

In `YoutubeDownloader.Core.Tests/MainViewModelTests.cs`:

Replace the constructor confirm setup (line ~27):
```csharp
_confirm.Setup(c => c.ConfirmAsync(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(true);
```

Replace **every** `_saveFile.Setup(s => s.PromptForSavePath(...)).Returns(X)` with the async form. There are several (lines ~130, 151, 172, 195, 220, 399, 417, 583, 603, 638, 685). Each changes like:
```csharp
// before: _saveFile.Setup(s => s.PromptForSavePath(It.IsAny<string>(), "mp3", It.IsAny<string?>())).Returns(@"C:\out\song.mp3");
_saveFile.Setup(s => s.PromptForSavePathAsync(It.IsAny<string>(), "mp3", It.IsAny<string?>())).ReturnsAsync(@"C:\out\song.mp3");
```
For the null-return one (line ~151):
```csharp
_saveFile.Setup(s => s.PromptForSavePathAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>())).ReturnsAsync((string?)null);
```
For the `.Verify` on save path (line ~406):
```csharp
_saveFile.Verify(s => s.PromptForSavePathAsync(It.IsAny<string>(), "mp3", @"C:\Music"), Times.Once);
```

In the "declines" test (line ~645):
```csharp
_confirm.Setup(c => c.ConfirmAsync(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(false);
```

Replace both `_confirm.Verify(c => c.Confirm(...))` blocks (lines ~625, ~659) with:
```csharp
_confirm.Verify(c => c.ConfirmAsync(
    "Cancel the current download? Your progress will be lost.",
    "Cancel download?"), Times.Once);
```

In both cancel tests, change the cancel invocation from `vm.CancelDownloadCommand.Execute(null);` to:
```csharp
await vm.CancelDownloadCommand.ExecuteAsync(null);
```

- [ ] **Step 2: Run tests to confirm they fail to build (red)**

Run: `dotnet test YoutubeDownloader.Core.Tests --nologo`
Expected: FAIL — compile errors like `'IConfirmationService' does not contain a definition for 'ConfirmAsync'` / `'ISaveFileService' does not contain a definition for 'PromptForSavePathAsync'`.

- [ ] **Step 3: Change the interfaces to async**

`YoutubeDownloader.Core/Services/IConfirmationService.cs`:
```csharp
namespace YoutubeDownloader.Core.Services;

public interface IConfirmationService
{
    /// <summary>Ask the user to confirm an action. Returns true if confirmed.</summary>
    Task<bool> ConfirmAsync(string message, string title);
}
```

`YoutubeDownloader.Core/Services/ISaveFileService.cs`:
```csharp
namespace YoutubeDownloader.Core.Services;

public interface ISaveFileService
{
    /// <summary>
    /// Prompts for a save path, starting in <paramref name="initialDirectory"/> when it exists.
    /// Returns the chosen absolute path, or null if the user cancels.
    /// </summary>
    Task<string?> PromptForSavePathAsync(string suggestedFileName, string extension, string? initialDirectory);
}
```

- [ ] **Step 4: Update `MainViewModel` call sites**

`YoutubeDownloader.Core/ViewModels/MainViewModel.cs` — the save prompt (was line 257):
```csharp
        var target = await _saveFile.PromptForSavePathAsync(suggested, ext, _appSettings.LastSaveFolder);
```

Replace the whole `CancelDownload` method (was lines 341-346) with:
```csharp
    [RelayCommand(CanExecute = nameof(CanCancelDownload))]
    private async Task CancelDownloadAsync()
    {
        if (await _confirm.ConfirmAsync("Cancel the current download? Your progress will be lost.", "Cancel download?"))
            _downloadCts?.Cancel();
    }
```
(Leave `CanCancelDownload()` unchanged directly below it.)

- [ ] **Step 5: Update the two WPF service impls to satisfy the async interfaces**

`YoutubeDownloader.App/Services/MessageBoxConfirmationService.cs`:
```csharp
using System.Windows;
using YoutubeDownloader.Core.Services;

namespace YoutubeDownloader.App.Services;

public sealed class MessageBoxConfirmationService : IConfirmationService
{
    public Task<bool> ConfirmAsync(string message, string title) =>
        Task.FromResult(MessageBox.Show(message, title, MessageBoxButton.YesNo,
            MessageBoxImage.Warning, MessageBoxResult.No) == MessageBoxResult.Yes);
}
```

`YoutubeDownloader.App/Services/SaveFileService.cs`:
```csharp
using System.IO;
using Microsoft.Win32;
using YoutubeDownloader.Core.Services;

namespace YoutubeDownloader.App.Services;

public sealed class SaveFileService : ISaveFileService
{
    public Task<string?> PromptForSavePathAsync(string suggestedFileName, string extension, string? initialDirectory)
    {
        var dialog = new SaveFileDialog
        {
            FileName = suggestedFileName,
            DefaultExt = "." + extension,
            Filter = extension.Equals("mp3", StringComparison.OrdinalIgnoreCase)
                ? "MP3 audio (*.mp3)|*.mp3"
                : "MP4 video (*.mp4)|*.mp4",
            AddExtension = true,
            OverwritePrompt = true
        };

        if (!string.IsNullOrEmpty(initialDirectory) && Directory.Exists(initialDirectory))
            dialog.InitialDirectory = initialDirectory;

        return Task.FromResult(dialog.ShowDialog() == true ? dialog.FileName : null);
    }
}
```

- [ ] **Step 6: Run tests — expect green**

Run: `dotnet test YoutubeDownloader.Core.Tests --nologo`
Expected: PASS (all tests, same count as before).

- [ ] **Step 7: Build the WPF app to confirm it still compiles**

Run: `dotnet build YoutubeDownloader.App --nologo`
Expected: Build succeeded, 0 errors.

- [ ] **Step 8: Commit** *(only if the user approved committing)*

```bash
git add YoutubeDownloader.Core YoutubeDownloader.App YoutubeDownloader.Core.Tests
git commit -m "refactor: make confirmation and save-file services async for Avalonia support"
```

---

## Task 2: Scaffold the Avalonia project

Create the project from the installed template, retarget net8.0, reference Core, add to the solution, and confirm a blank window runs.

**Files:**
- Create: `YoutubeDownloader.Avalonia/` (via template), then edit `YoutubeDownloader.Avalonia.csproj`
- Modify: `YoutubeDownloader.sln`

**Interfaces:**
- Produces: a runnable Avalonia app `YoutubeDownloader.Avalonia` referencing `YoutubeDownloader.Core`.

- [ ] **Step 1: Scaffold from the Avalonia app template**

Run (from repo root `C:\Repos\YoutubeDownloader`):
```bash
dotnet new avalonia.app -o YoutubeDownloader.Avalonia
```
Expected: creates `YoutubeDownloader.Avalonia/` with `Program.cs`, `App.axaml(.cs)`, `MainWindow.axaml(.cs)`, `app.manifest`, `YoutubeDownloader.Avalonia.csproj`.

- [ ] **Step 2: Move the scaffolded `MainWindow` into a `Views/` folder**

Delete the scaffolded root `MainWindow.axaml` and `MainWindow.axaml.cs` (they'll be recreated under `Views/` in Task 4). Run:
```bash
rm YoutubeDownloader.Avalonia/MainWindow.axaml YoutubeDownloader.Avalonia/MainWindow.axaml.cs
```

- [ ] **Step 3: Rewrite the csproj (net8.0, Core reference, branding)**

Replace `YoutubeDownloader.Avalonia/YoutubeDownloader.Avalonia.csproj` with:
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net8.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <ApplicationManifest>app.manifest</ApplicationManifest>
    <AvaloniaUseCompiledBindingsByDefault>true</AvaloniaUseCompiledBindingsByDefault>
    <ApplicationIcon>Assets\app.ico</ApplicationIcon>
    <AssemblyName>LiudochkaYoutubeDownloader.Avalonia</AssemblyName>
    <AssemblyTitle>Liudochka Youtube Downloader</AssemblyTitle>
    <Product>Liudochka Youtube Downloader</Product>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Avalonia" Version="11.3.12" />
    <PackageReference Include="Avalonia.Desktop" Version="11.3.12" />
    <PackageReference Include="Avalonia.Themes.Fluent" Version="11.3.12" />
    <PackageReference Include="Avalonia.Fonts.Inter" Version="11.3.12" />
    <PackageReference Include="Avalonia.Diagnostics" Version="11.3.12">
      <IncludeAssets Condition="'$(Configuration)' != 'Debug'">None</IncludeAssets>
      <PrivateAssets Condition="'$(Configuration)' != 'Debug'">All</PrivateAssets>
    </PackageReference>
    <PackageReference Include="Microsoft.Extensions.DependencyInjection" Version="10.0.9" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\YoutubeDownloader.Core\YoutubeDownloader.Core.csproj" />
  </ItemGroup>

  <ItemGroup>
    <AvaloniaResource Include="Assets\**" />
  </ItemGroup>
</Project>
```

- [ ] **Step 4: Copy the branding assets**

Run:
```bash
mkdir -p YoutubeDownloader.Avalonia/Assets
cp YoutubeDownloader.App/app.ico YoutubeDownloader.Avalonia/Assets/app.ico
cp YoutubeDownloader.App/Assets/cassette.png YoutubeDownloader.Avalonia/Assets/cassette.png
```

- [ ] **Step 5: Add the project to the solution**

Run:
```bash
dotnet sln YoutubeDownloader.sln add YoutubeDownloader.Avalonia/YoutubeDownloader.Avalonia.csproj
```
Expected: `Project ... added to the solution.`

- [ ] **Step 6: Temporarily point the scaffolded App at a blank window to verify it runs**

The scaffolded `App.axaml.cs` references `MainWindow` which we deleted. Replace `YoutubeDownloader.Avalonia/App.axaml.cs` body's `OnFrameworkInitializationCompleted` with a temporary blank window so the project builds and runs now (it will be fully rewritten in Task 4):
```csharp
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace YoutubeDownloader.Avalonia;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.MainWindow = new Window { Title = "Liudochka Youtube Downloader", Width = 720, Height = 640 };

        base.OnFrameworkInitializationCompleted();
    }
}
```

- [ ] **Step 7: Build and run the blank shell**

Run: `dotnet build YoutubeDownloader.Avalonia --nologo`
Expected: Build succeeded.
Run: `dotnet run --project YoutubeDownloader.Avalonia`
Expected: an empty 720×640 window titled "Liudochka Youtube Downloader" opens. Close it.

- [ ] **Step 8: Commit** *(only if the user approved committing)*

```bash
git add YoutubeDownloader.Avalonia YoutubeDownloader.sln
git commit -m "chore: scaffold YoutubeDownloader.Avalonia project targeting net8.0"
```

---

## Task 3: Windows service implementations + confirm dialog

Implement the five service interfaces in Avalonia/Windows terms, plus the small confirmation dialog. `TopLevel` is resolved lazily from the app lifetime.

**Files:**
- Create: `YoutubeDownloader.Avalonia/Infrastructure/TopLevelResolver.cs`
- Create: `YoutubeDownloader.Avalonia/Services/AvaloniaClipboardService.cs`
- Create: `YoutubeDownloader.Avalonia/Services/AvaloniaSaveFileService.cs`
- Create: `YoutubeDownloader.Avalonia/Services/AvaloniaConfirmationService.cs`
- Create: `YoutubeDownloader.Avalonia/Services/ExplorerFileRevealer.cs`
- Create: `YoutubeDownloader.Avalonia/Services/ProcessLinkOpener.cs`
- Create: `YoutubeDownloader.Avalonia/Views/ConfirmDialog.axaml` + `.axaml.cs`

**Interfaces:**
- Consumes: `Core` service interfaces (`IClipboardService`, `ISaveFileService.PromptForSavePathAsync`, `IConfirmationService.ConfirmAsync`, `IFileRevealer`, `ILinkOpener`).
- Produces:
  - `TopLevelResolver.MainWindow : Window?`, `TopLevelResolver.TopLevel : TopLevel?`
  - `AvaloniaClipboardService` (impl `IClipboardService`) with extra `Task RefreshAsync()`.
  - `AvaloniaSaveFileService`, `AvaloniaConfirmationService`, `ExplorerFileRevealer`, `ProcessLinkOpener`.
  - `ConfirmDialog(string title, string message)` → `ShowDialog<bool>(owner)`.

- [ ] **Step 1: TopLevel resolver**

Create `Infrastructure/TopLevelResolver.cs`:
```csharp
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;

namespace YoutubeDownloader.Avalonia.Infrastructure;

/// <summary>Resolves the single main window / its TopLevel lazily from the desktop lifetime.</summary>
internal static class TopLevelResolver
{
    public static Window? MainWindow =>
        (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;

    public static TopLevel? TopLevel => MainWindow is { } w ? Avalonia.Controls.TopLevel.GetTopLevel(w) : null;
}
```

- [ ] **Step 2: Clipboard service (cached, activation-refreshed)**

Create `Services/AvaloniaClipboardService.cs`:
```csharp
using YoutubeDownloader.Avalonia.Infrastructure;
using YoutubeDownloader.Core.Services;

namespace YoutubeDownloader.Avalonia.Services;

/// <summary>
/// Avalonia clipboard is async-only; <see cref="IClipboardService.GetText"/> must be synchronous.
/// So we cache the clipboard text (refreshed on window activation) and return the snapshot.
/// </summary>
public sealed class AvaloniaClipboardService : IClipboardService
{
    private volatile string? _cached;

    public string? GetText() => _cached;

    /// <summary>Read the OS clipboard and update the cached snapshot. Call on window activation.</summary>
    public async Task RefreshAsync()
    {
        try
        {
            var clipboard = TopLevelResolver.TopLevel?.Clipboard;
            _cached = clipboard is null ? null : await clipboard.GetTextAsync();
        }
        catch
        {
            _cached = null; // clipboard can be momentarily locked; treat as empty
        }
    }
}
```

- [ ] **Step 3: Save-file service (async picker)**

Create `Services/AvaloniaSaveFileService.cs`:
```csharp
using System.IO;
using Avalonia.Platform.Storage;
using YoutubeDownloader.Avalonia.Infrastructure;
using YoutubeDownloader.Core.Services;

namespace YoutubeDownloader.Avalonia.Services;

public sealed class AvaloniaSaveFileService : ISaveFileService
{
    public async Task<string?> PromptForSavePathAsync(string suggestedFileName, string extension, string? initialDirectory)
    {
        var topLevel = TopLevelResolver.TopLevel;
        if (topLevel is null)
            return null;

        var isMp3 = extension.Equals("mp3", StringComparison.OrdinalIgnoreCase);
        var options = new FilePickerSaveOptions
        {
            SuggestedFileName = suggestedFileName,
            DefaultExtension = extension,
            ShowOverwritePrompt = true,
            FileTypeChoices = new[]
            {
                isMp3
                    ? new FilePickerFileType("MP3 audio") { Patterns = new[] { "*.mp3" } }
                    : new FilePickerFileType("MP4 video") { Patterns = new[] { "*.mp4" } }
            }
        };

        if (!string.IsNullOrEmpty(initialDirectory) && Directory.Exists(initialDirectory))
            options.SuggestedStartLocation = await topLevel.StorageProvider.TryGetFolderFromPathAsync(initialDirectory);

        var file = await topLevel.StorageProvider.SaveFilePickerAsync(options);
        return file?.TryGetLocalPath();
    }
}
```

- [ ] **Step 4: Confirmation dialog view**

Create `Views/ConfirmDialog.axaml`:
```xml
<Window xmlns="https://github.com/avaloniaui"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        x:Class="YoutubeDownloader.Avalonia.Views.ConfirmDialog"
        Width="380" SizeToContent="Height"
        CanResize="False" ShowInTaskbar="False"
        WindowStartupLocation="CenterOwner"
        Background="#1A1726">
  <StackPanel Margin="22" Spacing="18">
    <TextBlock x:Name="TitleText" FontSize="15" FontWeight="SemiBold" Foreground="#F5F3FF" />
    <TextBlock x:Name="MessageText" FontSize="13" Foreground="#B8B0D0" TextWrapping="Wrap" />
    <StackPanel Orientation="Horizontal" HorizontalAlignment="Right" Spacing="10">
      <Button x:Name="NoButton" Content="No" Padding="18,7" />
      <Button x:Name="YesButton" Content="Yes" Padding="18,7"
              Background="#A855F7" Foreground="White" />
    </StackPanel>
  </StackPanel>
</Window>
```

Create `Views/ConfirmDialog.axaml.cs`:
```csharp
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace YoutubeDownloader.Avalonia.Views;

public partial class ConfirmDialog : Window
{
    public ConfirmDialog() // parameterless ctor for the XAML loader / designer
    {
        AvaloniaXamlLoader.Load(this);
        YesButton.Click += (_, _) => Close(true);
        NoButton.Click += (_, _) => Close(false);
    }

    public ConfirmDialog(string title, string message) : this()
    {
        Title = title;
        TitleText.Text = title;
        MessageText.Text = message;
    }

    private void InitializeComponent() { } // referenced controls are wired via x:Name after Load
}
```
Note: with `x:Name`, the fields (`TitleText`, `MessageText`, `YesButton`, `NoButton`) are generated by Avalonia's compiler; `AvaloniaXamlLoader.Load(this)` populates them. Remove the empty `InitializeComponent` if the generator complains — the `Load` call is sufficient.

- [ ] **Step 5: Confirmation service (async modal)**

Create `Services/AvaloniaConfirmationService.cs`:
```csharp
using YoutubeDownloader.Avalonia.Infrastructure;
using YoutubeDownloader.Avalonia.Views;
using YoutubeDownloader.Core.Services;

namespace YoutubeDownloader.Avalonia.Services;

public sealed class AvaloniaConfirmationService : IConfirmationService
{
    public async Task<bool> ConfirmAsync(string message, string title)
    {
        var owner = TopLevelResolver.MainWindow;
        if (owner is null)
            return false;

        var dialog = new ConfirmDialog(title, message);
        return await dialog.ShowDialog<bool>(owner);
    }
}
```

- [ ] **Step 6: File revealer + link opener (copy the WPF Windows logic — no WPF deps)**

Create `Services/ExplorerFileRevealer.cs`:
```csharp
using System.Diagnostics;
using System.IO;
using YoutubeDownloader.Core.Services;

namespace YoutubeDownloader.Avalonia.Services;

/// <summary>Opens Windows Explorer, selecting the file when it still exists.</summary>
public sealed class ExplorerFileRevealer : IFileRevealer
{
    public void RevealInFolder(string filePath)
    {
        if (File.Exists(filePath))
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{filePath}\"")
            {
                UseShellExecute = true
            });
            return;
        }

        var dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
            Process.Start(new ProcessStartInfo(dir) { UseShellExecute = true });
    }
}
```

Create `Services/ProcessLinkOpener.cs`:
```csharp
using System.Diagnostics;
using YoutubeDownloader.Core.Services;

namespace YoutubeDownloader.Avalonia.Services;

/// <summary>Opens a URL with the OS default handler (the user's browser).</summary>
public sealed class ProcessLinkOpener : ILinkOpener
{
    public void Open(string url)
        => Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
}
```

- [ ] **Step 7: Build to verify the services compile**

Run: `dotnet build YoutubeDownloader.Avalonia --nologo`
Expected: Build succeeded (the temporary blank-window App from Task 2 is still in place; these services aren't wired yet).

- [ ] **Step 8: Commit** *(only if the user approved committing)*

```bash
git add YoutubeDownloader.Avalonia/Infrastructure YoutubeDownloader.Avalonia/Services YoutubeDownloader.Avalonia/Views
git commit -m "feat(avalonia): add Windows service implementations and confirm dialog"
```

---

## Task 4: DI bootstrap + MainWindow shell wired to MainViewModel

Wire the DI container (mirroring the WPF `App.xaml.cs`), create the real `MainWindow` (empty tab shell for now), bind `Topmost`, and refresh the clipboard on activation. Deliverable: the app launches, resolves `MainViewModel` without error, and the always-on-top toggle works.

**Files:**
- Modify: `YoutubeDownloader.Avalonia/App.axaml` (theme + resources — minimal here, expanded in Task 5/6)
- Modify: `YoutubeDownloader.Avalonia/App.axaml.cs` (DI)
- Create: `YoutubeDownloader.Avalonia/Views/MainWindow.axaml` + `.axaml.cs` (shell)

**Interfaces:**
- Consumes: `MainViewModel` (Core), all Avalonia services from Task 3.
- Produces: `MainWindow(MainViewModel vm, AvaloniaClipboardService clipboard)`; a configured `ServiceProvider` in `App`.

- [ ] **Step 1: App.axaml — dark Fluent theme + Inter**

Replace `YoutubeDownloader.Avalonia/App.axaml` with:
```xml
<Application xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             x:Class="YoutubeDownloader.Avalonia.App"
             RequestedThemeVariant="Dark">
  <Application.Styles>
    <FluentTheme />
  </Application.Styles>
</Application>
```

- [ ] **Step 2: App.axaml.cs — DI container mirroring WPF**

Replace `YoutubeDownloader.Avalonia/App.axaml.cs` with:
```csharp
using System;
using System.IO;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using YoutubeDownloader.Avalonia.Services;
using YoutubeDownloader.Avalonia.Views;
using YoutubeDownloader.Core.Services;
using YoutubeDownloader.Core.ViewModels;

namespace YoutubeDownloader.Avalonia;

public partial class App : Application
{
    private ServiceProvider? _provider;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        var baseDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "YoutubeDownloader");
        var ffmpegDir = Path.Combine(baseDir, "ffmpeg");

        var services = new ServiceCollection();
        services.AddSingleton<IYouTubeService, YoutubeExplodeService>();
        services.AddSingleton<IFFmpegBinaryDownloader, XabeFFmpegBinaryDownloader>();
        services.AddSingleton<IFFmpegLocator>(sp =>
            new FFmpegLocator(sp.GetRequiredService<IFFmpegBinaryDownloader>(), ffmpegDir));
        services.AddSingleton<IMediaConverter, FFmpegMediaConverter>();
        services.AddSingleton<ITempFileService, TempFileService>();
        services.AddSingleton<ISaveFileService, AvaloniaSaveFileService>();
        services.AddSingleton<IHistoryStore>(_ => new JsonHistoryStore(baseDir));
        services.AddSingleton<ISettingsStore>(_ => new JsonSettingsStore(baseDir));
        services.AddSingleton<ILinkOpener, ProcessLinkOpener>();
        services.AddSingleton<IFileRevealer, ExplorerFileRevealer>();
        services.AddSingleton<AvaloniaClipboardService>();
        services.AddSingleton<IClipboardService>(sp => sp.GetRequiredService<AvaloniaClipboardService>());
        services.AddSingleton<IConfirmationService, AvaloniaConfirmationService>();
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<MainWindow>();

        _provider = services.BuildServiceProvider();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = _provider.GetRequiredService<MainWindow>();
            desktop.Exit += (_, _) => _provider?.Dispose();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
```

- [ ] **Step 3: MainWindow shell (empty tabs) + code-behind**

Create `Views/MainWindow.axaml`:
```xml
<Window xmlns="https://github.com/avaloniaui"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:vm="clr-namespace:YoutubeDownloader.Core.ViewModels;assembly=YoutubeDownloader.Core"
        x:Class="YoutubeDownloader.Avalonia.Views.MainWindow"
        x:Name="Root"
        x:DataType="vm:MainViewModel"
        Title="Liudochka Youtube Downloader"
        Width="720" Height="640" MinWidth="580" MinHeight="560"
        WindowStartupLocation="CenterScreen"
        Topmost="{Binding AlwaysOnTop}"
        Icon="/Assets/app.ico">
  <TabControl SelectedIndex="{Binding SelectedTabIndex}">
    <TabItem Header="Download">
      <TextBlock Margin="20" Text="Download tab — built in Task 6" />
    </TabItem>
    <TabItem Header="History">
      <TextBlock Margin="20" Text="History tab — built in Task 7" />
    </TabItem>
  </TabControl>
</Window>
```

Create `Views/MainWindow.axaml.cs`:
```csharp
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using YoutubeDownloader.Avalonia.Services;
using YoutubeDownloader.Core.ViewModels;

namespace YoutubeDownloader.Avalonia.Views;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;
    private readonly AvaloniaClipboardService _clipboard;

    public MainWindow() // designer ctor
    {
        AvaloniaXamlLoader.Load(this);
        _vm = null!;
        _clipboard = null!;
    }

    public MainWindow(MainViewModel vm, AvaloniaClipboardService clipboard) : this()
    {
        _vm = vm;
        _clipboard = clipboard;
        DataContext = vm;

        // Fires on first show (covers startup) and on every refocus.
        // Refresh the clipboard cache first, then let the VM inspect it.
        Activated += async (_, _) =>
        {
            await _clipboard.RefreshAsync();
            _vm.CheckClipboardCommand.Execute(null);
        };
    }
}
```

- [ ] **Step 4: Build and run — verify the VM resolves and binds**

Run: `dotnet build YoutubeDownloader.Avalonia --nologo`
Expected: Build succeeded.
Run: `dotnet run --project YoutubeDownloader.Avalonia`
Expected: window opens with two tabs ("Download", "History") showing placeholder text; the window stays on top of others (AlwaysOnTop defaults to true); no exceptions in the console. Switch tabs to confirm `SelectedTabIndex` binds. Close it.

- [ ] **Step 5: Commit** *(only if the user approved committing)*

```bash
git add YoutubeDownloader.Avalonia/App.axaml YoutubeDownloader.Avalonia/App.axaml.cs YoutubeDownloader.Avalonia/Views/MainWindow.axaml YoutubeDownloader.Avalonia/Views/MainWindow.axaml.cs
git commit -m "feat(avalonia): wire DI container and MainWindow shell to MainViewModel"
```

---

## Task 5: Converters, display formatters, and thumbnail loader

Add the value converters and the async image attached-property the views need. These are view-layer only (no Core change).

**Files:**
- Create: `YoutubeDownloader.Avalonia/Converters/NullToBoolConverter.cs`
- Create: `YoutubeDownloader.Avalonia/Converters/StringNotEmptyToBoolConverter.cs`
- Create: `YoutubeDownloader.Avalonia/Converters/DownloadModeDisplayConverter.cs`
- Create: `YoutubeDownloader.Avalonia/Converters/Mp3BitrateDisplayConverter.cs`
- Create: `YoutubeDownloader.Avalonia/Imaging/AsyncImage.cs`

**Interfaces:**
- Produces (used as `StaticResource` keys in Task 6/7): `NotNull`, `NotEmpty`, `ModeDisplay`, `BitrateDisplay`; attached property `imaging:AsyncImage.SourceUrl`.

- [ ] **Step 1: NullToBool (true when value is not null)**

Create `Converters/NullToBoolConverter.cs`:
```csharp
using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace YoutubeDownloader.Avalonia.Converters;

/// <summary>Returns true when the bound value is non-null (for IsVisible bindings).</summary>
public sealed class NullToBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value is not null;
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
```

- [ ] **Step 2: StringNotEmptyToBool**

Create `Converters/StringNotEmptyToBoolConverter.cs`:
```csharp
using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace YoutubeDownloader.Avalonia.Converters;

/// <summary>Returns true when the bound string is non-empty (for IsVisible bindings).</summary>
public sealed class StringNotEmptyToBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => !string.IsNullOrEmpty(value as string);
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
```

- [ ] **Step 3: DownloadMode display**

Create `Converters/DownloadModeDisplayConverter.cs`:
```csharp
using System;
using System.Globalization;
using Avalonia.Data.Converters;
using YoutubeDownloader.Core.Models;

namespace YoutubeDownloader.Avalonia.Converters;

/// <summary>Pretty label for the format combo: AudioMp3 -> "MP3 audio", Video -> "Video (MP4)".</summary>
public sealed class DownloadModeDisplayConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        DownloadMode.AudioMp3 => "MP3 audio",
        DownloadMode.Video => "Video (MP4)",
        _ => value?.ToString() ?? ""
    };
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
```

- [ ] **Step 4: Mp3Bitrate display**

Create `Converters/Mp3BitrateDisplayConverter.cs`:
```csharp
using System;
using System.Globalization;
using Avalonia.Data.Converters;
using YoutubeDownloader.Core.Models;

namespace YoutubeDownloader.Avalonia.Converters;

/// <summary>Pretty label for the bitrate combo: Kbps192 -> "192 kbps".</summary>
public sealed class Mp3BitrateDisplayConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is Mp3Bitrate b ? $"{(int)b} kbps" : value?.ToString() ?? "";
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
```

- [ ] **Step 5: Async image attached property**

Create `Imaging/AsyncImage.cs`:
```csharp
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net.Http;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;

namespace YoutubeDownloader.Avalonia.Imaging;

/// <summary>
/// Attached property that async-loads an http(s) image URL into an <see cref="Image"/>.
/// Usage: &lt;Image imaging:AsyncImage.SourceUrl="{Binding VideoInfo.ThumbnailUrl}" /&gt;
/// Fails silently (leaves the image empty) on any error.
/// </summary>
public static class AsyncImage
{
    private static readonly HttpClient Http = new();
    private static readonly ConcurrentDictionary<string, Bitmap> Cache = new();

    public static readonly AttachedProperty<string?> SourceUrlProperty =
        AvaloniaProperty.RegisterAttached<Image, string?>("SourceUrl", typeof(AsyncImage));

    public static void SetSourceUrl(Image element, string? value) => element.SetValue(SourceUrlProperty, value);
    public static string? GetSourceUrl(Image element) => element.GetValue(SourceUrlProperty);

    static AsyncImage() => SourceUrlProperty.Changed.AddClassHandler<Image>(OnChanged);

    private static async void OnChanged(Image image, AvaloniaPropertyChangedEventArgs e)
    {
        var url = e.NewValue as string;
        image.Source = null;
        if (string.IsNullOrWhiteSpace(url))
            return;

        try
        {
            if (Cache.TryGetValue(url, out var cached))
            {
                image.Source = cached;
                return;
            }

            var bytes = await Http.GetByteArrayAsync(url);
            using var ms = new MemoryStream(bytes);
            var bitmap = new Bitmap(ms);
            Cache[url] = bitmap;

            // The Avalonia sync context returns us to the UI thread; guard against a stale URL.
            if (GetSourceUrl(image) == url)
                image.Source = bitmap;
        }
        catch
        {
            // ignore — leave the image empty
        }
    }
}
```

- [ ] **Step 6: Build to verify converters + loader compile**

Run: `dotnet build YoutubeDownloader.Avalonia --nologo`
Expected: Build succeeded.

- [ ] **Step 7: Commit** *(only if the user approved committing)*

```bash
git add YoutubeDownloader.Avalonia/Converters YoutubeDownloader.Avalonia/Imaging
git commit -m "feat(avalonia): add value converters and async thumbnail loader"
```

---

## Task 6: Download tab — full UI + theme

Build the styled dark theme and the complete Download tab. Deliverable: fetch a real video (thumbnail + info + options appear) and download it as MP3 and MP4.

> **Design note:** The AXAML below is a complete, coherent dark theme (near-black layered surfaces, one violet→magenta accent, Inter type). It builds and meets the "nice UI" bar as written. The implementer MAY invoke the `frontend-design:frontend-design` skill to refine the tokens (exact colors/spacing/radii) before finalizing — but must keep every binding, `x:Name`, and `Classes` hook intact.

**Files:**
- Modify: `YoutubeDownloader.Avalonia/App.axaml` (add resources + control styles)
- Modify: `YoutubeDownloader.Avalonia/Views/MainWindow.axaml` (header + tabs + Download tab)

**Interfaces:**
- Consumes: `MainViewModel` bindable surface — `Url`, `IsBusy`, `IsDownloading`, `StatusMessage`, `Progress`, `VideoInfo`, `Modes`/`SelectedMode`, `Bitrates`/`SelectedBitrate`, `UpscaleWarning`, `VideoInfo.VideoOptions`/`SelectedVideoQuality`, `DetectedClipboardUrl`, `AlwaysOnTop`, `SelectedTabIndex`; commands `FetchInfoCommand`, `DownloadCommand`, `CancelDownloadCommand`, `UseDetectedLinkCommand`, `DismissDetectedLinkCommand`. Converters `NotNull`, `NotEmpty`, `ModeDisplay`, `BitrateDisplay`; `imaging:AsyncImage.SourceUrl`.

- [ ] **Step 1: App.axaml — resources + styles (theme)**

Replace `YoutubeDownloader.Avalonia/App.axaml` with:
```xml
<Application xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:conv="clr-namespace:YoutubeDownloader.Avalonia.Converters"
             x:Class="YoutubeDownloader.Avalonia.App"
             RequestedThemeVariant="Dark">

  <Application.Resources>
    <conv:NullToBoolConverter x:Key="NotNull" />
    <conv:StringNotEmptyToBoolConverter x:Key="NotEmpty" />
    <conv:DownloadModeDisplayConverter x:Key="ModeDisplay" />
    <conv:Mp3BitrateDisplayConverter x:Key="BitrateDisplay" />

    <SolidColorBrush x:Key="AccentBrush" Color="#A855F7" />
    <SolidColorBrush x:Key="CardBrush" Color="#1A1726" />
    <SolidColorBrush x:Key="CardBorderBrush" Color="#2A2438" />
    <SolidColorBrush x:Key="TextPrimaryBrush" Color="#F5F3FF" />
    <SolidColorBrush x:Key="TextSecondaryBrush" Color="#B8B0D0" />
    <SolidColorBrush x:Key="TextMutedBrush" Color="#8B82A8" />
    <SolidColorBrush x:Key="LabelBrush" Color="#C4B5FD" />

    <LinearGradientBrush x:Key="AccentGradient" StartPoint="0%,0%" EndPoint="100%,100%">
      <GradientStop Color="#8B5CF6" Offset="0" />
      <GradientStop Color="#EC4899" Offset="1" />
    </LinearGradientBrush>

    <LinearGradientBrush x:Key="WindowBackground" StartPoint="0%,0%" EndPoint="100%,100%">
      <GradientStop Color="#141020" Offset="0" />
      <GradientStop Color="#0C0A12" Offset="1" />
    </LinearGradientBrush>
  </Application.Resources>

  <Application.Styles>
    <FluentTheme />

    <!-- Section label -->
    <Style Selector="TextBlock.label">
      <Setter Property="Foreground" Value="{StaticResource LabelBrush}" />
      <Setter Property="FontWeight" Value="SemiBold" />
      <Setter Property="FontSize" Value="11.5" />
      <Setter Property="Margin" Value="2,0,0,5" />
    </Style>

    <!-- Card surface -->
    <Style Selector="Border.card">
      <Setter Property="Background" Value="{StaticResource CardBrush}" />
      <Setter Property="BorderBrush" Value="{StaticResource CardBorderBrush}" />
      <Setter Property="BorderThickness" Value="1" />
      <Setter Property="CornerRadius" Value="12" />
      <Setter Property="Padding" Value="14,12" />
    </Style>

    <!-- Accent (primary) button -->
    <Style Selector="Button.accent">
      <Setter Property="Background" Value="{StaticResource AccentGradient}" />
      <Setter Property="Foreground" Value="White" />
      <Setter Property="FontWeight" Value="SemiBold" />
      <Setter Property="FontSize" Value="13" />
      <Setter Property="Padding" Value="18,9" />
      <Setter Property="CornerRadius" Value="10" />
      <Setter Property="Cursor" Value="Hand" />
    </Style>
    <Style Selector="Button.accent:pointerover /template/ ContentPresenter">
      <Setter Property="Background" Value="{StaticResource AccentGradient}" />
      <Setter Property="Opacity" Value="0.9" />
    </Style>

    <!-- Chip (ghost) button -->
    <Style Selector="Button.chip">
      <Setter Property="Background" Value="#221D30" />
      <Setter Property="BorderBrush" Value="#33FFFFFF" />
      <Setter Property="BorderThickness" Value="1" />
      <Setter Property="Foreground" Value="{StaticResource TextPrimaryBrush}" />
      <Setter Property="FontSize" Value="11.5" />
      <Setter Property="Padding" Value="11,5" />
      <Setter Property="CornerRadius" Value="7" />
      <Setter Property="Cursor" Value="Hand" />
    </Style>

    <!-- Rounded text box -->
    <Style Selector="TextBox.glass">
      <Setter Property="CornerRadius" Value="9" />
      <Setter Property="FontSize" Value="13" />
      <Setter Property="Padding" Value="12,9" />
      <Setter Property="Background" Value="#221D30" />
      <Setter Property="BorderBrush" Value="{StaticResource CardBorderBrush}" />
    </Style>

    <!-- Pill tab headers -->
    <Style Selector="TabItem">
      <Setter Property="FontSize" Value="13" />
      <Setter Property="FontWeight" Value="SemiBold" />
      <Setter Property="Foreground" Value="{StaticResource TextSecondaryBrush}" />
      <Setter Property="Padding" Value="18,7" />
      <Setter Property="Margin" Value="0,0,8,0" />
      <Setter Property="MinHeight" Value="0" />
      <Setter Property="Template">
        <ControlTemplate>
          <Border x:Name="PART_Pill" Background="#1FFFFFFF" CornerRadius="9" Padding="{TemplateBinding Padding}">
            <ContentPresenter Content="{TemplateBinding Header}"
                              HorizontalAlignment="Center" VerticalAlignment="Center" />
          </Border>
        </ControlTemplate>
      </Setter>
    </Style>
    <Style Selector="TabItem:selected">
      <Setter Property="Foreground" Value="White" />
    </Style>
    <Style Selector="TabItem:selected /template/ Border#PART_Pill">
      <Setter Property="Background" Value="{StaticResource AccentGradient}" />
    </Style>

    <!-- Accent progress bar -->
    <Style Selector="ProgressBar.accent">
      <Setter Property="Foreground" Value="{StaticResource AccentBrush}" />
      <Setter Property="Background" Value="#22FFFFFF" />
      <Setter Property="MinHeight" Value="8" />
      <Setter Property="CornerRadius" Value="4" />
    </Style>
  </Application.Styles>
</Application>
```

- [ ] **Step 2: MainWindow.axaml — header + tabs + full Download tab**

Replace `YoutubeDownloader.Avalonia/Views/MainWindow.axaml` with:
```xml
<Window xmlns="https://github.com/avaloniaui"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:vm="clr-namespace:YoutubeDownloader.Core.ViewModels;assembly=YoutubeDownloader.Core"
        xmlns:models="clr-namespace:YoutubeDownloader.Core.Models;assembly=YoutubeDownloader.Core"
        xmlns:imaging="clr-namespace:YoutubeDownloader.Avalonia.Imaging"
        x:Class="YoutubeDownloader.Avalonia.Views.MainWindow"
        x:Name="Root"
        x:DataType="vm:MainViewModel"
        Title="Liudochka Youtube Downloader"
        Width="720" Height="640" MinWidth="580" MinHeight="560"
        WindowStartupLocation="CenterScreen"
        Topmost="{Binding AlwaysOnTop}"
        Background="{StaticResource WindowBackground}"
        Icon="/Assets/app.ico">

  <Grid Margin="18" RowDefinitions="Auto,*">

    <!-- Header -->
    <Grid Grid.Row="0" Margin="4,2,4,16" ColumnDefinitions="Auto,*,Auto">
      <Image Grid.Column="0" Source="/Assets/app.ico" Width="44" Height="44" />
      <StackPanel Grid.Column="1" VerticalAlignment="Center" Margin="14,0,0,0" Spacing="2">
        <TextBlock FontSize="21">
          <Run Text="Liudochka" FontWeight="Bold" Foreground="#F0ABFC" />
          <Run Text=" Youtube Downloader" FontWeight="SemiBold" Foreground="{StaticResource TextPrimaryBrush}" />
        </TextBlock>
        <TextBlock Text="Grab audio &amp; video from any YouTube link"
                   FontSize="11.5" Foreground="{StaticResource TextSecondaryBrush}" />
      </StackPanel>
      <CheckBox Grid.Column="2" Content="Always on top" IsChecked="{Binding AlwaysOnTop}"
                VerticalAlignment="Center" FontSize="11.5" Foreground="{StaticResource TextSecondaryBrush}" />
    </Grid>

    <!-- Tabs -->
    <TabControl Grid.Row="1" SelectedIndex="{Binding SelectedTabIndex}" Padding="0">

      <!-- ===== DOWNLOAD ===== -->
      <TabItem Header="Download">
        <Grid Margin="0,16,0,0" RowDefinitions="Auto,Auto,*,Auto,Auto">

          <!-- URL row + clipboard banner -->
          <StackPanel Grid.Row="0" Spacing="12">
            <Border Classes="card" Background="#2A1E3E" BorderBrush="#4C3A6E"
                    IsVisible="{Binding DetectedClipboardUrl, Converter={StaticResource NotEmpty}}">
              <Grid ColumnDefinitions="*,Auto,Auto">
                <TextBlock Grid.Column="0" VerticalAlignment="Center" FontSize="12.5" TextWrapping="Wrap"
                           Foreground="#EDE9FE" Text="📋 Found a YouTube link on your clipboard." />
                <Button Grid.Column="1" Classes="chip" Content="Use it"
                        Command="{Binding UseDetectedLinkCommand}" Margin="8,0,0,0" />
                <Button Grid.Column="2" Classes="chip" Content="✕"
                        Command="{Binding DismissDetectedLinkCommand}" Margin="6,0,0,0" />
              </Grid>
            </Border>

            <Grid ColumnDefinitions="*,Auto">
              <TextBox Grid.Column="0" Classes="glass" Margin="0,0,10,0"
                       Watermark="Paste a YouTube URL…"
                       Text="{Binding Url}" IsEnabled="{Binding !IsBusy}" />
              <Button Grid.Column="1" Classes="accent" Content="Fetch"
                      Command="{Binding FetchInfoCommand}" />
            </Grid>
          </StackPanel>

          <!-- Video info card (title + author + thumbnail) -->
          <Border Grid.Row="1" Classes="card" Margin="0,16,0,0"
                  IsVisible="{Binding VideoInfo, Converter={StaticResource NotNull}}">
            <Grid ColumnDefinitions="Auto,*">
              <Border Grid.Column="0" Width="120" Height="68" CornerRadius="8" ClipToBounds="True"
                      Background="#221D30" Margin="0,0,14,0">
                <Image imaging:AsyncImage.SourceUrl="{Binding VideoInfo.ThumbnailUrl}" Stretch="UniformToFill" />
              </Border>
              <StackPanel Grid.Column="1" VerticalAlignment="Center" Spacing="3">
                <TextBlock Text="{Binding VideoInfo.Title}" Foreground="{StaticResource TextPrimaryBrush}"
                           FontSize="15" FontWeight="Bold" TextWrapping="Wrap" MaxLines="2" TextTrimming="CharacterEllipsis" />
                <TextBlock Text="{Binding VideoInfo.Author}" Foreground="{StaticResource TextSecondaryBrush}" FontSize="12" />
              </StackPanel>
            </Grid>
          </Border>

          <!-- Options -->
          <StackPanel Grid.Row="2" Margin="0,18,0,0" Spacing="0"
                      IsVisible="{Binding VideoInfo, Converter={StaticResource NotNull}}">
            <TextBlock Classes="label" Text="FORMAT" />
            <ComboBox Width="260" HorizontalAlignment="Left" Margin="0,0,0,14"
                      ItemsSource="{Binding Modes}" SelectedItem="{Binding SelectedMode}">
              <ComboBox.ItemTemplate>
                <DataTemplate x:DataType="models:DownloadMode">
                  <TextBlock Text="{Binding Converter={StaticResource ModeDisplay}}" />
                </DataTemplate>
              </ComboBox.ItemTemplate>
            </ComboBox>

            <TextBlock Classes="label" Text="MP3 BITRATE" />
            <ComboBox Width="260" HorizontalAlignment="Left" Margin="0,0,0,6"
                      ItemsSource="{Binding Bitrates}" SelectedItem="{Binding SelectedBitrate}">
              <ComboBox.ItemTemplate>
                <DataTemplate x:DataType="models:Mp3Bitrate">
                  <TextBlock Text="{Binding Converter={StaticResource BitrateDisplay}}" />
                </DataTemplate>
              </ComboBox.ItemTemplate>
            </ComboBox>

            <Border CornerRadius="8" Background="#3A2A0C" Padding="10,7" Margin="0,2,0,14"
                    IsVisible="{Binding UpscaleWarning, Converter={StaticResource NotNull}}">
              <TextBlock Text="{Binding UpscaleWarning}" Foreground="#FCD34D" FontSize="12" TextWrapping="Wrap" />
            </Border>

            <TextBlock Classes="label" Text="VIDEO QUALITY" />
            <ComboBox Width="260" HorizontalAlignment="Left"
                      ItemsSource="{Binding VideoInfo.VideoOptions}" SelectedItem="{Binding SelectedVideoQuality}">
              <ComboBox.ItemTemplate>
                <DataTemplate x:DataType="models:VideoStreamOption">
                  <TextBlock Text="{Binding QualityLabel}" />
                </DataTemplate>
              </ComboBox.ItemTemplate>
            </ComboBox>
          </StackPanel>

          <!-- Progress -->
          <ProgressBar Grid.Row="3" Classes="accent" Margin="0,18,0,10"
                       Minimum="0" Maximum="1" Value="{Binding Progress}" />

          <!-- Status + action -->
          <Grid Grid.Row="4" ColumnDefinitions="*,Auto">
            <TextBlock Grid.Column="0" Text="{Binding StatusMessage}" Foreground="{StaticResource TextSecondaryBrush}"
                       FontSize="12" VerticalAlignment="Center" TextWrapping="Wrap" Margin="0,0,12,0" />
            <Button Grid.Column="1" Classes="accent" Content="Download" Padding="26,10"
                    Command="{Binding DownloadCommand}" IsVisible="{Binding !IsDownloading}" />
            <Button Grid.Column="1" Classes="accent" Content="Cancel" Padding="26,10"
                    Command="{Binding CancelDownloadCommand}" IsVisible="{Binding IsDownloading}" />
          </Grid>
        </Grid>
      </TabItem>

      <!-- ===== HISTORY (placeholder — built in Task 7) ===== -->
      <TabItem Header="History">
        <TextBlock Margin="20" Text="History tab — built in Task 7" />
      </TabItem>
    </TabControl>
  </Grid>
</Window>
```

- [ ] **Step 3: Build**

Run: `dotnet build YoutubeDownloader.Avalonia --nologo`
Expected: Build succeeded (watch for XAML compiled-binding errors; fix any `x:DataType` mismatch before proceeding).

- [ ] **Step 4: Run and verify fetch + MP3 download (manual E2E)**

Run: `dotnet run --project YoutubeDownloader.Avalonia`
Then:
1. Paste a real short YouTube URL (e.g. `https://youtu.be/dQw4w9WgXcQ`) → click **Fetch**.
   Expected: status shows "Loaded: …"; the info card appears with **thumbnail**, title, author; Format/Bitrate/Quality combos appear with pretty labels ("MP3 audio", "192 kbps", "720p", …).
2. Leave Format = "MP3 audio" → click **Download** → choose a save path.
   Expected: FFmpeg downloads on first run (progress moves through preparing → downloading → converting), button shows **Cancel** during the run, then status "Done! Saved to …" and a real playable .mp3 exists at the chosen path.

- [ ] **Step 5: Verify MP4 download + cancel (manual E2E)**

1. Change Format to "Video (MP4)", pick a quality → **Download**.
   Expected: downloads video+audio, merges, saves a playable .mp4.
2. Start another download and click **Cancel** mid-run → confirm dialog appears → **Yes**.
   Expected: status "Download cancelled.", progress resets, no partial file left at the target.

- [ ] **Step 6: Commit** *(only if the user approved committing)*

```bash
git add YoutubeDownloader.Avalonia/App.axaml YoutubeDownloader.Avalonia/Views/MainWindow.axaml
git commit -m "feat(avalonia): styled dark theme and full Download tab"
```

---

## Task 7: History tab — full UI

Replace the History placeholder with the grouped, searchable list and row actions.

**Files:**
- Modify: `YoutubeDownloader.Avalonia/Views/MainWindow.axaml` (History `TabItem` content)

**Interfaces:**
- Consumes: `SearchText`, `HistoryGroups` (`models:HistoryGroup` → `Header`, `Entries` of `models:HistoryEntry` → `Title`, `Format`, `TimeDisplay`, `Url`, `FilePath`), `HistoryPlaceholder`; commands on the root VM: `ClearHistoryCommand`, `UseEntryCommand`, `OpenInBrowserCommand`, `OpenFolderCommand`, `RemoveEntryCommand`. Item-level commands reach the VM via `#Root` with a cast.

- [ ] **Step 1: Replace the History `TabItem` content**

In `YoutubeDownloader.Avalonia/Views/MainWindow.axaml`, replace the History `TabItem` (the placeholder block) with:
```xml
      <!-- ===== HISTORY ===== -->
      <TabItem Header="History">
        <Grid Margin="0,16,0,0" RowDefinitions="Auto,*">

          <!-- Search + clear -->
          <Grid Grid.Row="0" Margin="0,0,0,12" ColumnDefinitions="*,Auto">
            <TextBox Grid.Column="0" Classes="glass" Margin="0,0,10,0"
                     Watermark="Search by title or link…" Text="{Binding SearchText}" />
            <Button Grid.Column="1" Classes="chip" Content="Clear all" VerticalAlignment="Center"
                    Command="{Binding ClearHistoryCommand}" />
          </Grid>

          <!-- Empty / no-results state -->
          <TextBlock Grid.Row="1" Text="{Binding HistoryPlaceholder}"
                     Foreground="{StaticResource TextMutedBrush}" FontSize="13" TextWrapping="Wrap"
                     MaxWidth="360" TextAlignment="Center"
                     HorizontalAlignment="Center" VerticalAlignment="Center"
                     IsVisible="{Binding HistoryPlaceholder, Converter={StaticResource NotNull}}" />

          <!-- Grouped list -->
          <ScrollViewer Grid.Row="1" VerticalScrollBarVisibility="Auto">
            <ItemsControl ItemsSource="{Binding HistoryGroups}">
              <ItemsControl.ItemTemplate>
                <DataTemplate x:DataType="models:HistoryGroup">
                  <StackPanel Margin="0,0,0,4">
                    <TextBlock Text="{Binding Header}" Foreground="{StaticResource LabelBrush}"
                               FontWeight="SemiBold" FontSize="11.5" Margin="2,6,0,7" />
                    <ItemsControl ItemsSource="{Binding Entries}">
                      <ItemsControl.ItemTemplate>
                        <DataTemplate x:DataType="models:HistoryEntry">
                          <Border Classes="card" Margin="0,0,0,9">
                            <Grid ColumnDefinitions="*,Auto">
                              <StackPanel Grid.Column="0" Margin="0,0,10,0" Spacing="5">
                                <TextBlock Text="{Binding Title}" Foreground="{StaticResource TextPrimaryBrush}"
                                           FontWeight="SemiBold" FontSize="13" TextTrimming="CharacterEllipsis" />
                                <StackPanel Orientation="Horizontal" Spacing="8">
                                  <Border CornerRadius="5" Background="#3DA855F7" Padding="7,2" VerticalAlignment="Center">
                                    <TextBlock Text="{Binding Format}" Foreground="#EDE9FE" FontSize="10.5" />
                                  </Border>
                                  <TextBlock Text="{Binding TimeDisplay}" Foreground="{StaticResource TextMutedBrush}"
                                             FontSize="10.5" VerticalAlignment="Center" />
                                </StackPanel>
                                <TextBlock Text="{Binding Url}" Foreground="{StaticResource TextMutedBrush}"
                                           FontSize="10.5" TextTrimming="CharacterEllipsis" />
                              </StackPanel>
                              <StackPanel Grid.Column="1" Orientation="Horizontal" VerticalAlignment="Center" Spacing="6">
                                <Button Classes="chip" Content="Use"
                                        Command="{Binding #Root.((vm:MainViewModel)DataContext).UseEntryCommand}"
                                        CommandParameter="{Binding}" />
                                <Button Classes="chip" Content="Open"
                                        Command="{Binding #Root.((vm:MainViewModel)DataContext).OpenInBrowserCommand}"
                                        CommandParameter="{Binding}" />
                                <Button Classes="chip" Content="Folder"
                                        IsVisible="{Binding FilePath, Converter={StaticResource NotEmpty}}"
                                        Command="{Binding #Root.((vm:MainViewModel)DataContext).OpenFolderCommand}"
                                        CommandParameter="{Binding}" />
                                <Button Classes="chip" Content="✕"
                                        Command="{Binding #Root.((vm:MainViewModel)DataContext).RemoveEntryCommand}"
                                        CommandParameter="{Binding}" />
                              </StackPanel>
                            </Grid>
                          </Border>
                        </DataTemplate>
                      </ItemsControl.ItemTemplate>
                    </ItemsControl>
                  </StackPanel>
                </DataTemplate>
              </ItemsControl.ItemTemplate>
            </ItemsControl>
          </ScrollViewer>
        </Grid>
      </TabItem>
```

- [ ] **Step 2: Build**

Run: `dotnet build YoutubeDownloader.Avalonia --nologo`
Expected: Build succeeded. If a compiled-binding error mentions the `#Root.((vm:MainViewModel)DataContext)` commands, confirm the `Window` has `x:Name="Root"` and the `vm` namespace is declared (both set in Task 4/6).

- [ ] **Step 3: Run and verify History (manual E2E)**

Run: `dotnet run --project YoutubeDownloader.Avalonia`
With at least one prior download present:
1. Open **History** — entries appear grouped by date (e.g. "Today"), each with title, a format chip, time, and the URL.
2. **Search** — type part of a title/URL; the list filters; a non-matching term shows the "No history matches …" placeholder.
3. **Use** on an entry → switches to the Download tab with the URL filled.
4. **Open** → opens the link in the default browser.
5. **Folder** (visible only when a file path exists) → opens Explorer with the file selected.
6. **✕** → removes that entry.
7. **Clear all** → empties history; the "No downloads yet…" placeholder shows.

- [ ] **Step 4: Commit** *(only if the user approved committing)*

```bash
git add YoutubeDownloader.Avalonia/Views/MainWindow.axaml
git commit -m "feat(avalonia): full History tab with grouped list and row actions"
```

---

## Task 8: Final verification + polish pass

Confirm the whole solution is green and the Avalonia app is release-clean; optionally invoke frontend-design for a final visual refinement.

**Files:** none required (polish edits only if issues found).

- [ ] **Step 1: Full solution build**

Run: `dotnet build YoutubeDownloader.sln --nologo`
Expected: Build succeeded, 0 errors (Core, App/WPF, Avalonia, Tests all build).

- [ ] **Step 2: Full test run**

Run: `dotnet test YoutubeDownloader.sln --nologo`
Expected: all tests pass, same count as before Task 1 (async refactor preserved behavior).

- [ ] **Step 3: Release build of the Avalonia app**

Run: `dotnet build YoutubeDownloader.Avalonia -c Release --nologo`
Expected: Build succeeded; `Avalonia.Diagnostics` excluded in Release (per csproj condition), no warnings about it.

- [ ] **Step 4: Optional visual refinement**

If desired, invoke `frontend-design:frontend-design` to refine the theme tokens in `App.axaml` (colors/spacing/radii/hover states) and re-run Task 6 Step 4 to confirm nothing broke. Keep all bindings, `x:Name`s, and `Classes` intact.

- [ ] **Step 5: Final manual smoke (both apps)**

1. `dotnet run --project YoutubeDownloader.Avalonia` — quick fetch + MP3 download + history check.
2. `dotnet run --project YoutubeDownloader.App` — confirm the WPF app still launches and downloads (regression check for the Task 1 changes).

- [ ] **Step 6: Commit** *(only if the user approved committing)*

```bash
git add -A
git commit -m "chore(avalonia): final verification and polish"
```

---

## Self-Review

**Spec coverage:**
- New `YoutubeDownloader.Avalonia` project, WPF kept → Task 2. ✓
- Windows-only, net8.0 → Global Constraints + Task 2 Step 3. ✓
- Async adaptation (Confirm + SaveFile async; clipboard cached sync) → Task 1 + Task 3 Steps 2/3/5. ✓
- WPF impls kept working; tests updated → Task 1 Steps 5/6/7. ✓
- DI mirroring WPF; Topmost; activation clipboard refresh → Task 4. ✓
- Converters, inverse-bool via `!`, display formatters, thumbnail loader → Task 5 + used in Task 6. ✓
- Custom modern dark UI, one accent, Inter, feature parity (banner, URL+Fetch, info+thumbnail, options, upscale warning, progress, Download↔Cancel, history search/groups/actions/empty/clear, always-on-top, branding) → Tasks 6 & 7. ✓
- Verification: Core.Tests green, solution builds, manual E2E mp3+mp4+history → Tasks 6/7/8. ✓
- Out of scope (cross-platform, Core logic changes, WPF removal) → respected. ✓

**Placeholder scan:** No TBD/TODO. The "History tab — built in Task 7" text in Tasks 4/6 is intentional interim UI, replaced in Task 7. Every code step contains complete code.

**Type consistency:** `ConfirmAsync`/`PromptForSavePathAsync` signatures match across interface, VM call sites, WPF impls, Avalonia impls, and test mocks. `AvaloniaClipboardService.RefreshAsync()` defined in Task 3, consumed in Task 4. Converter keys (`NotNull`, `NotEmpty`, `ModeDisplay`, `BitrateDisplay`) defined in Task 6 App.axaml, used in Tasks 6/7. `imaging:AsyncImage.SourceUrl` defined in Task 5, used in Task 6. `#Root` name + `vm:`/`models:` namespaces defined in Task 4/6, used in Task 7. `CancelDownloadCommand` name preserved.
