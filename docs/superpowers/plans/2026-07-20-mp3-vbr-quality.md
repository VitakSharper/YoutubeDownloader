# MP3 VBR Quality (+ remove WPF app) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Remove the legacy WPF front-end so Avalonia is the sole app, then add a best-quality **VBR V0** option to the MP3 path and make it the default (keeping the fixed-bitrate presets).

**Architecture:** Two phases. Phase A deletes `YoutubeDownloader.App` (WPF) and consolidates the publish pipeline on the already-shipping Avalonia app. Phase B replaces the int-valued `Mp3Bitrate` enum with an `Mp3Quality` value type that models VBR *or* CBR, threads it through the FFmpeg arg builders / converter / ViewModel, and updates the Avalonia UI + tests.

**Tech Stack:** .NET 8, C#, xUnit + Moq, Avalonia 11 (compiled bindings), CommunityToolkit.Mvvm, FFmpeg (libmp3lame).

## Global Constraints

- **Target framework:** `net8.0` (Core, Core.Tests, Avalonia). No new dependencies.
- **C# indentation:** 4 spaces; match each file's existing style. Nullable + ImplicitUsings are enabled in Core.
- **VBR = LAME `-q:a`** (0 = best); **CBR = LAME `-b:a {n}k`**. Both use `-c:a libmp3lame`.
- **Default MP3 quality = `Mp3Quality.BestVbr`** (VBR V0, ~245 kbps).
- **Surviving projects:** `YoutubeDownloader.Core`, `YoutubeDownloader.Core.Tests`, `YoutubeDownloader.Avalonia`. Do not touch the Opus or Video paths.
- **Test command:** `dotnet test YoutubeDownloader.Core.Tests --nologo` (add `--filter "FullyQualifiedName~<Name>"` for one test). **Build the app:** `dotnet build YoutubeDownloader.Avalonia --nologo`. **Build all:** `dotnet build YoutubeDownloader.sln --nologo`.
- **Skill edits are BOM-free** (use Edit, never a BOM-applying Write) and verified by actually running the documented command before finishing.
- The GitHub updater (`GitHubUpdateService`) is name-agnostic (first `.zip` asset → first `.exe` inside), so the Avalonia assembly name is **left unchanged** (`LiudochkaYoutubeDownloader.Avalonia`).

---

## Phase A — Remove the WPF app

### Task 1: Delete `YoutubeDownloader.App` and consolidate publishing on Avalonia

Structural task (no unit tests). Deliverable: the solution builds with 3 projects, Core tests stay green, and the Avalonia publish still produces the single exe.

**Files:**
- Modify: `YoutubeDownloader.sln` (remove the WPF project entry + its config block)
- Delete: `YoutubeDownloader.App/` (entire directory)
- Delete: `publish.cmd`
- Modify: `.claude/skills/publish-app/SKILL.md`
- Modify: `README.md`

**Interfaces:**
- Consumes: nothing.
- Produces: a 3-project solution; `publish-app` skill now points at Avalonia.

- [ ] **Step 1: Remove the WPF project from the solution**

In `YoutubeDownloader.sln`, delete these two lines (the project declaration):

```
Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "YoutubeDownloader.App", "YoutubeDownloader.App\YoutubeDownloader.App.csproj", "{16AA5983-D4DA-444B-88AA-03084F5F0CA1}"
EndProject
```

And delete the entire `{16AA5983-D4DA-444B-88AA-03084F5F0CA1}` block inside `GlobalSection(ProjectConfigurationPlatforms)` (the 12 lines from `.Debug|Any CPU.ActiveCfg` through `.Release|x86.Build.0`).

- [ ] **Step 2: Delete the WPF project directory**

```bash
git rm -r --quiet --ignore-unmatch YoutubeDownloader.App
```

Then remove any leftover untracked build output:

Run (PowerShell): `Remove-Item -Recurse -Force YoutubeDownloader.App -ErrorAction SilentlyContinue`

- [ ] **Step 3: Delete the WPF publish script**

```bash
git rm --quiet publish.cmd
```

(The Avalonia app is published by `release.cmd`, which stays.)

- [ ] **Step 4: Verify the solution builds with 3 projects**

Run: `dotnet build YoutubeDownloader.sln --nologo`
Expected: PASS — builds `YoutubeDownloader.Core`, `YoutubeDownloader.Core.Tests`, `YoutubeDownloader.Avalonia`; no reference to `YoutubeDownloader.App`.

- [ ] **Step 5: Verify Core tests still pass**

Run: `dotnet test YoutubeDownloader.Core.Tests --nologo`
Expected: PASS (unchanged from before).

- [ ] **Step 6: Rewrite the `publish-app` skill for Avalonia**

Edit `.claude/skills/publish-app/SKILL.md` (BOM-free). Replace the body **below the frontmatter** (keep the `---` frontmatter block exactly as-is) with:

````markdown
# Publish — single self-contained executable

## Overview
For this repo, **"publish" means: build `YoutubeDownloader.Avalonia` as ONE self-contained Windows executable** that runs without a .NET install. The canonical flow is the repo's `release.cmd`, which publishes and zips the single-file exe into `dist\`. The deliverable is `dist\LiudochkaYoutubeDownloader.Avalonia.exe` (and a versioned `.zip` the in-app updater consumes).

## Command
Run from the repo root (`C:\Repos\YoutubeDownloader`):

```
.\release.cmd
```

`release.cmd` runs, in effect, this single-line publish (bump `set VERSION=` in the script + `<Version>` in the csproj first):

```
dotnet publish YoutubeDownloader.Avalonia/YoutubeDownloader.Avalonia.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=none -p:DebugSymbols=false -o dist
```

## Why each flag
- `-r win-x64 --self-contained true` — bundle the .NET runtime so no install is needed on the target.
- `-p:PublishSingleFile=true` — emit a single `.exe`.
- `-p:IncludeNativeLibrariesForSelfExtract=true` — bundle Avalonia's native libs (Skia/HarfBuzz: `libSkiaSharp.dll`, `libHarfBuzzSharp.dll`, `av_libglesv2.dll`) into the exe so the publish folder is a single file rather than several.
- `-p:EnableCompressionInSingleFile=true` — compresses the bundle.
- `-p:DebugType=none -p:DebugSymbols=false` — no stray `.pdb` beside the exe.

## Output
```
dist\LiudochkaYoutubeDownloader.Avalonia.exe
dist\LiudochkaYoutubeDownloader.Avalonia-v<version>.zip
```

## After publishing — open the folder (always)
```
Start-Process explorer.exe -ArgumentList "C:\Repos\YoutubeDownloader\dist"
```

(Use `Start-Process` rather than bare `explorer.exe` — Explorer returns a non-zero exit code even on success, which otherwise reads as a failed command.)

## Verify it really is single-file
After publishing, `dist\` must contain the one `.exe` (plus the release `.zip`). If you also see `libSkiaSharp.dll` / `libHarfBuzzSharp.dll` / `av_libglesv2.dll` beside the exe, `IncludeNativeLibrariesForSelfExtract` was missing — clear the folder and rebuild.

## Gotchas
- **"Device or resource busy" / "in use" when cleaning `dist\`:** caused by a shell whose working directory is inside that folder (or the app still running). Never `cd` into `dist\` before deleting it — delete it from the repo root with `Remove-Item -Recurse -Force dist`.
- First launch of the single-file exe self-extracts the native libs to a temp dir — normal for single-file, happens once per version.
- To cut a GitHub release, after `release.cmd`: `gh release create v<version> "dist\LiudochkaYoutubeDownloader.Avalonia-v<version>.zip" --title "v<version>" --notes "…"`.
````

- [ ] **Step 7: Verify the rewritten skill's command actually works**

Run: `.\release.cmd` (from the repo root)
Expected: PASS — creates `dist\LiudochkaYoutubeDownloader.Avalonia.exe` and `dist\LiudochkaYoutubeDownloader.Avalonia-v1.2.0.zip`. Confirm no `libSkiaSharp.dll` sits beside the exe (single-file worked). If the run fails, fix the skill/command before committing — do not commit an unverified skill.

- [ ] **Step 8: Update the README to describe the Avalonia app only**

In `README.md`, apply these edits:

Line 3 — replace:
```
Built on .NET 8 with a frosted-glass cassette theme. Ships as an **Avalonia** app (the actively developed UI, with in-app updates) plus the original **WPF** build; both share the same core.
```
with:
```
Built on .NET 8 with a frosted-glass cassette theme. Ships as an **Avalonia** app with in-app updates.
```

Line 7 — replace `![UI](https://img.shields.io/badge/UI-Avalonia%20%2B%20WPF-2C3E50)` with `![UI](https://img.shields.io/badge/UI-Avalonia-2C3E50)`.

Line 14 — replace `- **Open in foobar2000** *(Avalonia)* — from` with `- **Open in foobar2000** — from` (drop the now-redundant `*(Avalonia)*` qualifier).

Line 25 — replace `- **Check for updates** *(Avalonia)* — a header` with `- **Check for updates** — a header`.

Line 32 — replace:
```
- **Windows** — both UIs target Windows (WPF is `net8.0-windows`; the Avalonia build ships as a Windows x64 single file).
```
with:
```
- **Windows** — the Avalonia build ships as a Windows x64 single file.
```

Lines 41–45 — replace the run block:
```
# Run the Avalonia app (primary UI)
dotnet run --project YoutubeDownloader.Avalonia

# …or the original WPF app
dotnet run --project YoutubeDownloader.App
```
with:
```
# Run the app
dotnet run --project YoutubeDownloader.Avalonia
```

Lines 57–60 — replace the building block:
```
release.cmd    # Avalonia build  -> dist\LiudochkaYoutubeDownloader.Avalonia.exe
publish.cmd    # WPF build       -> dist\LiudochkaYoutubeDownloader.exe
```
with:
```
release.cmd    # -> dist\LiudochkaYoutubeDownloader.Avalonia.exe
```

Lines 95–99 — replace the project-structure lines:
```
├─ YoutubeDownloader.Avalonia/    Avalonia UI (net8.0) — primary, in-app updates
├─ YoutubeDownloader.App/         WPF UI (net8.0-windows) — original
├─ YoutubeDownloader.Core.Tests/  Unit tests (xUnit + Moq)
├─ release.cmd                    Avalonia single-file release build
└─ publish.cmd                    WPF single-file release build
```
with:
```
├─ YoutubeDownloader.Avalonia/    Avalonia UI (net8.0) — in-app updates
├─ YoutubeDownloader.Core.Tests/  Unit tests (xUnit + Moq)
└─ release.cmd                    Single-file release build
```

Line 102 — replace `wired up with \`Microsoft.Extensions.DependencyInjection\` in each app's composition root, which keeps the core testable and shared across both UIs.` with `wired up with \`Microsoft.Extensions.DependencyInjection\` in the app's composition root, which keeps the core testable.`

Line 110 — delete the list item `- WPF on .NET 8 — original UI`.

- [ ] **Step 9: Final verification for Phase A**

Run: `dotnet build YoutubeDownloader.sln --nologo && dotnet test YoutubeDownloader.Core.Tests --nologo`
Expected: PASS. `grep -ri "YoutubeDownloader.App" README.md publish.cmd` → no matches (files gone / references removed).

- [ ] **Step 10: Commit**

```bash
git add -A
git commit -m "chore: remove legacy WPF app, Avalonia is the sole front-end"
```

---

## Phase B — MP3 VBR quality

### Task 2: `Mp3Quality` model

Additive — coexists with `Mp3Bitrate` (deleted in Task 4), so the whole solution stays green.

**Files:**
- Create: `YoutubeDownloader.Core/Models/Mp3Quality.cs`
- Test: `YoutubeDownloader.Core.Tests/Mp3QualityTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: `Mp3Quality` with `string Label`, `string HistoryLabel`, `bool IsVbr`, `int VbrQuality`, `int ApproxKbps`; singletons `BestVbr`, `Cbr320`, `Cbr256`, `Cbr192`, `Cbr128`; `static IReadOnlyList<Mp3Quality> All`; `ToString() => Label`.

- [ ] **Step 1: Write the failing test**

Create `YoutubeDownloader.Core.Tests/Mp3QualityTests.cs`:

```csharp
using YoutubeDownloader.Core.Models;

namespace YoutubeDownloader.Core.Tests;

public class Mp3QualityTests
{
    [Fact]
    public void All_ListsBestVbrFirstThenCbrDescending()
    {
        Assert.Equal(
            new[] { Mp3Quality.BestVbr, Mp3Quality.Cbr320, Mp3Quality.Cbr256, Mp3Quality.Cbr192, Mp3Quality.Cbr128 },
            Mp3Quality.All);
    }

    [Fact]
    public void BestVbr_IsVbrLevelZeroApprox245()
    {
        Assert.True(Mp3Quality.BestVbr.IsVbr);
        Assert.Equal(0, Mp3Quality.BestVbr.VbrQuality);
        Assert.Equal(245, Mp3Quality.BestVbr.ApproxKbps);
        Assert.Equal("Best (VBR V0)", Mp3Quality.BestVbr.Label);
        Assert.Equal("VBR V0 (~245 kbps)", Mp3Quality.BestVbr.HistoryLabel);
    }

    [Fact]
    public void Cbr320_IsConstant320()
    {
        Assert.False(Mp3Quality.Cbr320.IsVbr);
        Assert.Equal(320, Mp3Quality.Cbr320.ApproxKbps);
        Assert.Equal("320 kbps", Mp3Quality.Cbr320.Label);
        Assert.Equal("320 kbps", Mp3Quality.Cbr320.HistoryLabel);
    }

    [Fact]
    public void ToString_ReturnsLabel()
    {
        Assert.Equal("Best (VBR V0)", Mp3Quality.BestVbr.ToString());
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test YoutubeDownloader.Core.Tests --filter "FullyQualifiedName~Mp3QualityTests" --nologo`
Expected: FAIL — `Mp3Quality` does not exist (compile error).

- [ ] **Step 3: Create the model**

Create `YoutubeDownloader.Core/Models/Mp3Quality.cs`:

```csharp
namespace YoutubeDownloader.Core.Models;

/// <summary>
/// An MP3 encoding preset — either LAME VBR (a quality level) or CBR (a fixed kbps).
/// </summary>
public sealed class Mp3Quality
{
    private Mp3Quality(string label, string historyLabel, bool isVbr, int vbrQuality, int approxKbps)
    {
        Label = label;
        HistoryLabel = historyLabel;
        IsVbr = isVbr;
        VbrQuality = vbrQuality;
        ApproxKbps = approxKbps;
    }

    /// <summary>Dropdown text, e.g. "Best (VBR V0)" or "320 kbps".</summary>
    public string Label { get; }

    /// <summary>Descriptor used in history: "MP3 · {HistoryLabel}".</summary>
    public string HistoryLabel { get; }

    /// <summary>True → LAME VBR (-q:a); false → CBR (-b:a).</summary>
    public bool IsVbr { get; }

    /// <summary>LAME VBR quality level (0 = best). Meaningful only when <see cref="IsVbr"/>.</summary>
    public int VbrQuality { get; }

    /// <summary>Target kbps for CBR; the approximate average for VBR. Drives the upscale warning.</summary>
    public int ApproxKbps { get; }

    public static readonly Mp3Quality BestVbr = new("Best (VBR V0)", "VBR V0 (~245 kbps)", isVbr: true, vbrQuality: 0, approxKbps: 245);
    public static readonly Mp3Quality Cbr320 = new("320 kbps", "320 kbps", isVbr: false, vbrQuality: 0, approxKbps: 320);
    public static readonly Mp3Quality Cbr256 = new("256 kbps", "256 kbps", isVbr: false, vbrQuality: 0, approxKbps: 256);
    public static readonly Mp3Quality Cbr192 = new("192 kbps", "192 kbps", isVbr: false, vbrQuality: 0, approxKbps: 192);
    public static readonly Mp3Quality Cbr128 = new("128 kbps", "128 kbps", isVbr: false, vbrQuality: 0, approxKbps: 128);

    /// <summary>All presets in dropdown order (best first).</summary>
    public static readonly IReadOnlyList<Mp3Quality> All = new[] { BestVbr, Cbr320, Cbr256, Cbr192, Cbr128 };

    public override string ToString() => Label;
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test YoutubeDownloader.Core.Tests --filter "FullyQualifiedName~Mp3QualityTests" --nologo`
Expected: PASS (4 tests).

- [ ] **Step 5: Commit**

```bash
git add YoutubeDownloader.Core/Models/Mp3Quality.cs YoutubeDownloader.Core.Tests/Mp3QualityTests.cs
git commit -m "feat(mp3): add Mp3Quality model with VBR V0 + CBR presets"
```

---

### Task 3: FFmpeg VBR/CBR argument builders

Splits `ForMp3` into explicit `ForMp3Cbr` + `ForMp3Vbr`. The converter's *public* signature stays `int bitrateKbps` here (only its internal call changes), so `MainViewModel` and its tests are untouched and the solution stays green.

**Files:**
- Modify: `YoutubeDownloader.Core/Ffmpeg/FFmpegArguments.cs`
- Modify: `YoutubeDownloader.Core/Services/FFmpegMediaConverter.cs:10` (internal call only)
- Test: `YoutubeDownloader.Core.Tests/FFmpegArgumentsTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: `FFmpegArguments.ForMp3Cbr(string input, string output, int bitrateKbps)` → `... -c:a libmp3lame -b:a {n}k ...`; `FFmpegArguments.ForMp3Vbr(string input, string output, int vbrQuality)` → `... -c:a libmp3lame -q:a {q} ...`. `ForMp3` no longer exists.

- [ ] **Step 1: Update the failing tests**

In `YoutubeDownloader.Core.Tests/FFmpegArgumentsTests.cs`, replace the first test (`ForMp3_BuildsLameArgsWithBitrateAndQuotedPaths`) with these two:

```csharp
    [Fact]
    public void ForMp3Cbr_BuildsLameCbrArgsWithBitrateAndQuotedPaths()
    {
        var args = FFmpegArguments.ForMp3Cbr(@"C:\in.webm", @"C:\out.mp3", 192);

        Assert.Contains("-i \"C:\\in.webm\"", args);
        Assert.Contains("-vn", args);
        Assert.Contains("-c:a libmp3lame", args);
        Assert.Contains("-b:a 192k", args);
        Assert.Contains("\"C:\\out.mp3\"", args);
        Assert.StartsWith("-y", args);
    }

    [Fact]
    public void ForMp3Vbr_BuildsLameVbrArgsWithQualityLevel()
    {
        var args = FFmpegArguments.ForMp3Vbr(@"C:\in.webm", @"C:\out.mp3", 0);

        Assert.Contains("-i \"C:\\in.webm\"", args);
        Assert.Contains("-vn", args);
        Assert.Contains("-c:a libmp3lame", args);
        Assert.Contains("-q:a 0", args);
        Assert.DoesNotContain("-b:a", args);
        Assert.Contains("\"C:\\out.mp3\"", args);
        Assert.StartsWith("-y", args);
    }
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test YoutubeDownloader.Core.Tests --filter "FullyQualifiedName~FFmpegArgumentsTests" --nologo`
Expected: FAIL — `ForMp3Cbr` / `ForMp3Vbr` do not exist (compile error).

- [ ] **Step 3: Add the two builders**

In `YoutubeDownloader.Core/Ffmpeg/FFmpegArguments.cs`, replace the `ForMp3` method:

```csharp
    public static string ForMp3(string input, string output, int bitrateKbps)
        => $"-y -i \"{input}\" -vn -c:a libmp3lame -b:a {bitrateKbps}k \"{output}\"";
```

with:

```csharp
    public static string ForMp3Cbr(string input, string output, int bitrateKbps)
        => $"-y -i \"{input}\" -vn -c:a libmp3lame -b:a {bitrateKbps}k \"{output}\"";

    public static string ForMp3Vbr(string input, string output, int vbrQuality)
        => $"-y -i \"{input}\" -vn -c:a libmp3lame -q:a {vbrQuality} \"{output}\"";
```

- [ ] **Step 4: Fix the converter's internal call**

In `YoutubeDownloader.Core/Services/FFmpegMediaConverter.cs`, line 10, change `FFmpegArguments.ForMp3(` to `FFmpegArguments.ForMp3Cbr(`:

```csharp
    public Task ConvertToMp3Async(string ffmpegPath, string input, string output, int bitrateKbps,
        TimeSpan duration, IProgress<double>? progress, CancellationToken ct)
        => RunAsync(ffmpegPath, FFmpegArguments.ForMp3Cbr(input, output, bitrateKbps), duration, progress, ct);
```

- [ ] **Step 5: Run all Core tests**

Run: `dotnet test YoutubeDownloader.Core.Tests --nologo`
Expected: PASS (all tests, including the two new FFmpeg arg tests).

- [ ] **Step 6: Commit**

```bash
git add YoutubeDownloader.Core/Ffmpeg/FFmpegArguments.cs YoutubeDownloader.Core/Services/FFmpegMediaConverter.cs YoutubeDownloader.Core.Tests/FFmpegArgumentsTests.cs
git commit -m "feat(mp3): add CBR/VBR ffmpeg arg builders"
```

---

### Task 4: Switch converter + ViewModel + UI to `Mp3Quality`; delete the enum

Atomic type change across Core, tests, and the Avalonia UI. Deleting `Mp3Bitrate` breaks the Avalonia project until its XAML is updated, so **all steps land in one commit** (the committed state builds & passes). Core-test checkpoint is mid-task (Step 8); full build + manual smoke before the commit.

**Files:**
- Modify: `YoutubeDownloader.Core/Services/IMediaConverter.cs:5`
- Modify: `YoutubeDownloader.Core/Services/FFmpegMediaConverter.cs:8-10`
- Modify: `YoutubeDownloader.Core/ViewModels/MainViewModel.cs` (lines ~118, 121-123, 155, 158-167, 259, 305-308)
- Delete: `YoutubeDownloader.Core/Models/Mp3Bitrate.cs`
- Modify: `YoutubeDownloader.Core.Tests/FFmpegMediaConverterTests.cs:14`
- Modify: `YoutubeDownloader.Core.Tests/MainViewModelTests.cs` (enum refs + verifies + new tests)
- Modify: `YoutubeDownloader.Avalonia/Views/MainWindow.axaml:114-120`
- Modify: `YoutubeDownloader.Avalonia/App.axaml:11`
- Delete: `YoutubeDownloader.Avalonia/Converters/Mp3BitrateDisplayConverter.cs`
- Modify: `README.md:13`

**Interfaces:**
- Consumes: `Mp3Quality` (Task 2); `FFmpegArguments.ForMp3Cbr` / `ForMp3Vbr` (Task 3).
- Produces: `IMediaConverter.ConvertToMp3Async(string ffmpegPath, string input, string output, Mp3Quality quality, TimeSpan duration, IProgress<double>? progress, CancellationToken ct)`; `MainViewModel.SelectedQuality : Mp3Quality` (default `BestVbr`), `MainViewModel.Qualities : IReadOnlyList<Mp3Quality>`, `MainViewModel.ShowQuality : bool`.

- [ ] **Step 1: Update `FFmpegMediaConverterTests` to the new signature**

In `YoutubeDownloader.Core.Tests/FFmpegMediaConverterTests.cs`, add `using YoutubeDownloader.Core.Models;` at the top, and change the call on line 14 from `..., "out.mp3", 192, ...` to `..., "out.mp3", Mp3Quality.Cbr192, ...`:

```csharp
        await Assert.ThrowsAnyAsync<Exception>(() =>
            converter.ConvertToMp3Async(missing, "in.webm", "out.mp3", Mp3Quality.Cbr192, TimeSpan.FromSeconds(10), null, CancellationToken.None));
```

- [ ] **Step 2: Update `MainViewModelTests` — enum references**

In `YoutubeDownloader.Core.Tests/MainViewModelTests.cs`, replace each `Mp3Bitrate.KbpsNNN` / `vm.SelectedBitrate` as follows:
- Line 98: `vm.SelectedBitrate = Mp3Bitrate.Kbps320;` → `vm.SelectedQuality = Mp3Quality.Cbr320;`
- Line 101: `vm.SelectedBitrate = Mp3Bitrate.Kbps128;` → `vm.SelectedQuality = Mp3Quality.Cbr128;`
- Line 111: `vm.SelectedBitrate = Mp3Bitrate.Kbps320;` → `vm.SelectedQuality = Mp3Quality.Cbr320;`
- Line 138: `vm.SelectedBitrate = Mp3Bitrate.Kbps256;` → `vm.SelectedQuality = Mp3Quality.Cbr256;`
- Line 202: `vm.SelectedBitrate = Mp3Bitrate.Kbps256;` → `vm.SelectedQuality = Mp3Quality.Cbr256;`

- [ ] **Step 3: Update `MainViewModelTests` — converter verifies**

In the same file, change the `ConvertToMp3Async` verify/setup argument-matchers:
- Line 143 (in `Download_AudioMode_ConvertsWithSelectedBitrate`): `..., @"C:\out\song.mp3", 256,` → `..., @"C:\out\song.mp3", Mp3Quality.Cbr256,`
- Line 224 (setup in `Download_Failure_DoesNotRecordHistory`): `It.IsAny<int>(), It.IsAny<TimeSpan>(),` → `It.IsAny<Mp3Quality>(), It.IsAny<TimeSpan>(),`
- Line 687 (verify in `Download_ModeChangedMidFlight_...`): `It.IsAny<string>(), It.IsAny<int>(),` → `It.IsAny<string>(), It.IsAny<Mp3Quality>(),`
- Line 725 (verify in `Download_OpusMode_...`): `It.IsAny<string>(), It.IsAny<int>(),` → `It.IsAny<string>(), It.IsAny<Mp3Quality>(),`

- [ ] **Step 4: Update `MainViewModelTests` — rename the ShowBitrate test + add two new tests**

Rename `ShowBitrate_HiddenOnlyForOpusMode` (line 693) to `ShowQuality_HiddenOnlyForOpusMode` and replace the three `vm.ShowBitrate` references with `vm.ShowQuality`:

```csharp
    [Fact]
    public void ShowQuality_HiddenOnlyForOpusMode()
    {
        var vm = CreateSut();

        vm.SelectedMode = DownloadMode.AudioMp3;
        Assert.True(vm.ShowQuality);
        vm.SelectedMode = DownloadMode.Video;
        Assert.True(vm.ShowQuality);
        vm.SelectedMode = DownloadMode.AudioOpus;
        Assert.False(vm.ShowQuality);
    }
```

Then add these two tests (e.g. right after it):

```csharp
    [Fact]
    public void SelectedQuality_DefaultsToBestVbr()
    {
        Assert.Equal(Mp3Quality.BestVbr, CreateSut().SelectedQuality);
    }

    [Fact]
    public async Task Download_DefaultVbr_ConvertsWithVbrAndRecordsVbrLabel()
    {
        var info = SampleInfo();
        _youtube.Setup(s => s.GetVideoInfoAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(info);
        _ffmpeg.Setup(f => f.EnsureAsync(It.IsAny<IProgress<double>?>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync(@"C:\ffmpeg\ffmpeg.exe");
        _temp.Setup(t => t.NewTempFile(It.IsAny<string>())).Returns(@"C:\temp\a.webm");
        _saveFile.Setup(s => s.PromptForSavePathAsync(It.IsAny<string>(), "mp3", It.IsAny<string?>())).ReturnsAsync(@"C:\out\song.mp3");

        var vm = CreateSut();
        vm.Url = "https://youtu.be/dQw4w9WgXcQ";
        await vm.FetchInfoCommand.ExecuteAsync(null);
        // SelectedQuality defaults to BestVbr — do not set it.

        await vm.DownloadCommand.ExecuteAsync(null);

        _converter.Verify(c => c.ConvertToMp3Async(
            @"C:\ffmpeg\ffmpeg.exe", @"C:\temp\a.webm", @"C:\out\song.mp3", Mp3Quality.BestVbr,
            info.Duration, It.IsAny<IProgress<double>?>(), It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal("MP3 · VBR V0 (~245 kbps)", Assert.Single(vm.History).Format);
    }
```

- [ ] **Step 5: Change the converter interface + implementation**

`YoutubeDownloader.Core/Services/IMediaConverter.cs` — add `using YoutubeDownloader.Core.Models;` at the top and change the first method's `int bitrateKbps` to `Mp3Quality quality`:

```csharp
using YoutubeDownloader.Core.Models;

namespace YoutubeDownloader.Core.Services;

public interface IMediaConverter
{
    Task ConvertToMp3Async(string ffmpegPath, string input, string output, Mp3Quality quality,
        TimeSpan duration, IProgress<double>? progress, CancellationToken ct);

    Task MuxToMp4Async(string ffmpegPath, string videoInput, string audioInput, string output,
        TimeSpan duration, IProgress<double>? progress, CancellationToken ct);

    Task RemuxToOpusAsync(string ffmpegPath, string input, string output,
        TimeSpan duration, IProgress<double>? progress, CancellationToken ct);
}
```

`YoutubeDownloader.Core/Services/FFmpegMediaConverter.cs` — add `using YoutubeDownloader.Core.Models;` and replace the `ConvertToMp3Async` method (lines 8-10) with:

```csharp
    public Task ConvertToMp3Async(string ffmpegPath, string input, string output, Mp3Quality quality,
        TimeSpan duration, IProgress<double>? progress, CancellationToken ct)
        => RunAsync(ffmpegPath,
            quality.IsVbr
                ? FFmpegArguments.ForMp3Vbr(input, output, quality.VbrQuality)
                : FFmpegArguments.ForMp3Cbr(input, output, quality.ApproxKbps),
            duration, progress, ct);
```

- [ ] **Step 6: Update `MainViewModel`**

In `YoutubeDownloader.Core/ViewModels/MainViewModel.cs`:

(a) Line 118 — in the `_selectedMode` attribute list, change `[NotifyPropertyChangedFor(nameof(ShowBitrate))]` to `[NotifyPropertyChangedFor(nameof(ShowQuality))]`.

(b) Lines 121-123 — replace:
```csharp
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UpscaleWarning))]
    private Mp3Bitrate _selectedBitrate = Mp3Bitrate.Kbps192;
```
with:
```csharp
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UpscaleWarning))]
    private Mp3Quality _selectedQuality = Mp3Quality.BestVbr;
```

(c) Line 155 — replace:
```csharp
    public IReadOnlyList<Mp3Bitrate> Bitrates { get; } = Enum.GetValues<Mp3Bitrate>();
```
with:
```csharp
    public IReadOnlyList<Mp3Quality> Qualities { get; } = Mp3Quality.All;
```

(d) Lines 158-159 — replace:
```csharp
    /// <summary>Bitrate only applies to MP3; hidden for the copy-only Opus mode.</summary>
    public bool ShowBitrate => SelectedMode != DownloadMode.AudioOpus;
```
with:
```csharp
    /// <summary>Quality only applies to MP3; hidden for the copy-only Opus mode.</summary>
    public bool ShowQuality => SelectedMode != DownloadMode.AudioOpus;
```

(e) Lines 161-167 — replace the `UpscaleWarning` property with:
```csharp
    public string? UpscaleWarning =>
        VideoInfo is not null
        && SelectedMode == DownloadMode.AudioMp3
        && QualityAdvisor.WouldUpscale(VideoInfo.SourceAudioKbps, SelectedQuality.ApproxKbps)
            ? $"Selected {SelectedQuality.Label} is higher than the source audio " +
              $"({VideoInfo.SourceAudioKbps} kbps); this won't add real quality."
            : null;
```

(f) Line 259 — right after `var mode = SelectedMode;`, add:
```csharp
        var quality = SelectedQuality;
```

(g) Lines 305-308 — replace the MP3-branch body:
```csharp
                StatusMessage = $"Converting to MP3 ({(int)SelectedBitrate} kbps)…";
                await _converter.ConvertToMp3Async(ffmpegPath, tempAudio, target, (int)SelectedBitrate,
                    info.Duration, new Progress<double>(p => Progress = 0.7 + p * 0.3), token);
                formatLabel = $"MP3 · {(int)SelectedBitrate} kbps";
```
with:
```csharp
                StatusMessage = $"Converting to MP3 ({quality.Label})…";
                await _converter.ConvertToMp3Async(ffmpegPath, tempAudio, target, quality,
                    info.Duration, new Progress<double>(p => Progress = 0.7 + p * 0.3), token);
                formatLabel = $"MP3 · {quality.HistoryLabel}";
```

- [ ] **Step 7: Delete the `Mp3Bitrate` enum**

```bash
git rm --quiet YoutubeDownloader.Core/Models/Mp3Bitrate.cs
```

- [ ] **Step 8: Core-test checkpoint**

Run: `dotnet test YoutubeDownloader.Core.Tests --nologo`
Expected: PASS — all tests, including `Mp3QualityTests`, `SelectedQuality_DefaultsToBestVbr`, `Download_DefaultVbr_ConvertsWithVbrAndRecordsVbrLabel`, and the retyped existing tests. (The Avalonia project will NOT build yet — that's Step 9-11.)

- [ ] **Step 9: Update the Avalonia XAML**

In `YoutubeDownloader.Avalonia/Views/MainWindow.axaml`, replace lines 114-120:

```xml
            <TextBlock Classes="label" Text="MP3 BITRATE" IsVisible="{Binding ShowBitrate}" />
            <ComboBox Width="260" HorizontalAlignment="Left" Margin="0,0,0,6"
                      IsVisible="{Binding ShowBitrate}"
                      ItemsSource="{Binding Bitrates}" SelectedItem="{Binding SelectedBitrate}">
              <ComboBox.ItemTemplate>
                <DataTemplate x:DataType="models:Mp3Bitrate">
                  <TextBlock Text="{Binding Converter={StaticResource BitrateDisplay}}" />
```

with:

```xml
            <TextBlock Classes="label" Text="MP3 QUALITY" IsVisible="{Binding ShowQuality}" />
            <ComboBox Width="260" HorizontalAlignment="Left" Margin="0,0,0,6"
                      IsVisible="{Binding ShowQuality}"
                      ItemsSource="{Binding Qualities}" SelectedItem="{Binding SelectedQuality}">
              <ComboBox.ItemTemplate>
                <DataTemplate x:DataType="models:Mp3Quality">
                  <TextBlock Text="{Binding Label}" />
```

- [ ] **Step 10: Remove the redundant converter**

In `YoutubeDownloader.Avalonia/App.axaml`, delete line 11:
```xml
    <conv:Mp3BitrateDisplayConverter x:Key="BitrateDisplay" />
```

Then delete the converter class:
```bash
git rm --quiet YoutubeDownloader.Avalonia/Converters/Mp3BitrateDisplayConverter.cs
```

- [ ] **Step 11: Update the README audio bullet**

In `README.md`, line 13, replace:
```
- **Download audio or video** — save audio as **MP3** (selectable bitrate) or **Opus** (native, no re-encode), or **video** (selectable quality).
```
with:
```
- **Download audio or video** — save audio as **MP3** (best-quality VBR by default, or a fixed bitrate) or **Opus** (native, no re-encode), or **video** (selectable quality).
```

- [ ] **Step 12: Full build + tests**

Run: `dotnet build YoutubeDownloader.sln --nologo && dotnet test YoutubeDownloader.Core.Tests --nologo`
Expected: PASS — solution builds (Avalonia included) and all tests pass. Confirm no lingering references: `grep -rn "Mp3Bitrate\|SelectedBitrate\|ShowBitrate\|BitrateDisplay" YoutubeDownloader.Core YoutubeDownloader.Avalonia YoutubeDownloader.Core.Tests` → no matches.

- [ ] **Step 13: Manual smoke test**

Run: `dotnet run --project YoutubeDownloader.Avalonia` (launch in an interactive session so the window stays visible). Fetch a real video, open the **FORMAT**/quality area:
- The label reads **MP3 QUALITY**; the dropdown shows **Best (VBR V0)**, 320, 256, 192, 128 with **Best (VBR V0)** preselected.
- Selecting **Opus** hides the quality control; MP3/Video show it.
- Download with **Best (VBR V0)** → the saved `.mp3` plays; history shows `MP3 · VBR V0 (~245 kbps)`.
- (Optional) verify the encoding is VBR: `ffprobe` the output, or check the file is smaller than a 320-CBR download of the same video.

- [ ] **Step 14: Commit**

```bash
git add -A
git commit -m "feat(mp3): default to best-quality VBR V0, keep CBR presets"
```

---

## Self-Review

**Spec coverage:**
- Phase A (remove WPF, delete publish.cmd, rewrite publish-app skill, README, keep assembly name) → Task 1. ✓
- `Mp3Quality` model → Task 2. ✓
- VBR/CBR arg builders → Task 3. ✓
- Converter signature → Task 4 (Step 5). ✓
- ViewModel rename/default/snapshot/branch/warning → Task 4 (Step 6). ✓
- Delete `Mp3Bitrate` → Task 4 (Step 7). ✓
- Avalonia UI + delete converter → Task 4 (Steps 9-10). ✓
- README audio bullet → Task 4 (Step 11). ✓
- Tests (arg CBR+VBR, converter, VM retype, default, VBR download) → Tasks 2, 3, 4. ✓
- Unaffected `QualityAdvisorTests` / `JsonHistoryStoreTests` — correctly left alone. ✓

**Placeholder scan:** none — every code/edit step shows exact content.

**Type consistency:** `Mp3Quality` (Label, HistoryLabel, IsVbr, VbrQuality, ApproxKbps, All, BestVbr/Cbr320/256/192/128) used identically in Tasks 2→4. `ForMp3Cbr(input, output, bitrateKbps)` / `ForMp3Vbr(input, output, vbrQuality)` consistent Task 3→4. `ConvertToMp3Async(..., Mp3Quality quality, ...)`, `SelectedQuality`, `Qualities`, `ShowQuality` consistent across Task 4 and its tests.
