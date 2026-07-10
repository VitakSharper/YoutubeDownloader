# Manual Update Check Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a manual "Check for updates" button to the Avalonia app that queries GitHub Releases, and — if a newer version exists — downloads the zipped single-file exe and reveals it in Explorer for the user to swap in.

**Architecture:** UI-agnostic logic (`IUpdateService`/`GitHubUpdateService`, `UpdateViewModel`, models, version helper) lives in `YoutubeDownloader.Core` and is unit-tested. Only the Avalonia app registers the services (DI) and surfaces the UI (a header button bound to a dedicated `UpdateViewModel`). The shared `MainViewModel` and the WPF app are not touched.

**Tech Stack:** .NET 8, C#, CommunityToolkit.Mvvm (source generators), `System.Text.Json`, `System.IO.Compression`, Avalonia 11.3, xUnit 2.5.3 + Moq.

## Global Constraints

- **Avalonia app only.** Do NOT modify `YoutubeDownloader.App` (WPF) or the shared `YoutubeDownloader.Core/ViewModels/MainViewModel.cs`.
- **No auth token in the binary.** Repo `github.com/VitakSharper/YoutubeDownloader` is public; use the unauthenticated GitHub REST API.
- **GitHub API requires a `User-Agent` header** on every request (both the API call and the asset download).
- New shared logic goes in `YoutubeDownloader.Core`; only the Avalonia app registers services and shows UI.
- **App version** comes from `<Version>` in `YoutubeDownloader.Avalonia.csproj`; baseline `1.0.0`.
- **Manual trigger only** — a button. No automatic/background/startup checks.
- **Creating a public GitHub release is outward-facing.** `release.cmd` prepares the zip and prints the `gh` command but must NOT publish; publishing happens only on explicit user approval.
- Tests use **xUnit + Moq** (no FluentAssertions — plain `Assert`). Indentation in this repo is **4 spaces** (not tabs).
- Owner/repo constants: `VitakSharper` / `YoutubeDownloader`.

---

### Task 1: Version helper + update models

**Files:**
- Create: `YoutubeDownloader.Core/Helpers/ReleaseVersion.cs`
- Create: `YoutubeDownloader.Core/Models/UpdateInfo.cs`
- Test: `YoutubeDownloader.Core.Tests/ReleaseVersionTests.cs`

**Interfaces:**
- Produces: `ReleaseVersion.TryParseTag(string? tag, out Version version) : bool`; records `UpdateInfo(Version CurrentVersion, Version? LatestVersion, bool IsUpdateAvailable, string? DownloadUrl, string? AssetName, string? ReleaseUrl, string? ReleaseNotes)` and `UpdateOptions(string Owner, string Repo, Version CurrentVersion, string? DownloadRoot = null)`.

- [ ] **Step 1: Write the failing test**

```csharp
using YoutubeDownloader.Core.Helpers;

namespace YoutubeDownloader.Core.Tests;

public class ReleaseVersionTests
{
    [Theory]
    [InlineData("v1.2.3", true, "1.2.3")]
    [InlineData("1.2.3", true, "1.2.3")]
    [InlineData("V2.0", true, "2.0")]
    [InlineData("v1.0.0.0", true, "1.0.0.0")]
    [InlineData("", false, null)]
    [InlineData("   ", false, null)]
    [InlineData("beta", false, null)]
    [InlineData(null, false, null)]
    public void TryParseTag_parses_expected(string? tag, bool ok, string? expected)
    {
        var result = ReleaseVersion.TryParseTag(tag, out var version);
        Assert.Equal(ok, result);
        if (ok)
            Assert.Equal(Version.Parse(expected!), version);
    }

    [Fact]
    public void Newer_tag_compares_greater_than_current()
    {
        Assert.True(ReleaseVersion.TryParseTag("v1.1.0", out var latest));
        Assert.True(latest > new Version(1, 0, 0));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test YoutubeDownloader.Core.Tests --filter "ReleaseVersionTests" --nologo`
Expected: FAIL — `ReleaseVersion` does not exist (compile error).

- [ ] **Step 3: Write the helper and models**

`YoutubeDownloader.Core/Helpers/ReleaseVersion.cs`:
```csharp
namespace YoutubeDownloader.Core.Helpers;

/// <summary>Parsing of SemVer-ish GitHub release tags ("v1.2.3" / "1.2.3").</summary>
public static class ReleaseVersion
{
    /// <summary>Parses a release tag, tolerating a leading 'v'/'V'. Returns false if unparseable.</summary>
    public static bool TryParseTag(string? tag, out Version version)
    {
        version = new Version(0, 0);
        if (string.IsNullOrWhiteSpace(tag))
            return false;

        var trimmed = tag.Trim();
        if (trimmed[0] is 'v' or 'V')
            trimmed = trimmed[1..];

        return Version.TryParse(trimmed, out version!);
    }
}
```

`YoutubeDownloader.Core/Models/UpdateInfo.cs`:
```csharp
namespace YoutubeDownloader.Core.Models;

/// <summary>Result of an update check.</summary>
public sealed record UpdateInfo(
    Version CurrentVersion,
    Version? LatestVersion,
    bool IsUpdateAvailable,
    string? DownloadUrl,
    string? AssetName,
    string? ReleaseUrl,
    string? ReleaseNotes);

/// <summary>Configuration for <c>GitHubUpdateService</c>. <paramref name="DownloadRoot"/> is for tests; null resolves to the user's Downloads folder.</summary>
public sealed record UpdateOptions(
    string Owner,
    string Repo,
    Version CurrentVersion,
    string? DownloadRoot = null);
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test YoutubeDownloader.Core.Tests --filter "ReleaseVersionTests" --nologo`
Expected: PASS (all theory cases + the fact).

- [ ] **Step 5: Commit**

```bash
git add YoutubeDownloader.Core/Helpers/ReleaseVersion.cs YoutubeDownloader.Core/Models/UpdateInfo.cs YoutubeDownloader.Core.Tests/ReleaseVersionTests.cs
git commit -m "feat(update): version-tag parsing + UpdateInfo/UpdateOptions models"
```

---

### Task 2: GitHubUpdateService.CheckAsync (query + parse latest release)

**Files:**
- Create: `YoutubeDownloader.Core/Services/IUpdateService.cs`
- Create: `YoutubeDownloader.Core/Services/GitHubUpdateService.cs`
- Create: `YoutubeDownloader.Core.Tests/TestHttp.cs` (shared test doubles)
- Test: `YoutubeDownloader.Core.Tests/GitHubUpdateServiceCheckTests.cs`

**Interfaces:**
- Consumes: `ReleaseVersion.TryParseTag`, `UpdateInfo`, `UpdateOptions` (Task 1).
- Produces: `IUpdateService.CheckAsync(CancellationToken) : Task<UpdateInfo>` and `IUpdateService.DownloadAndExtractAsync(UpdateInfo, IProgress<double>?, CancellationToken) : Task<string>` (the latter implemented in Task 3); `GitHubUpdateService(HttpClient, UpdateOptions)` ctor. Test doubles `StubHttpMessageHandler` and `ThrowingHttpMessageHandler`.

- [ ] **Step 1: Write the shared test doubles**

`YoutubeDownloader.Core.Tests/TestHttp.cs`:
```csharp
using System.Net;

namespace YoutubeDownloader.Core.Tests;

/// <summary>Returns a canned response computed from the request.</summary>
internal sealed class StubHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

    public StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) => _responder = responder;

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        => Task.FromResult(_responder(request));

    public static HttpClient Client(Func<HttpRequestMessage, HttpResponseMessage> responder)
        => new(new StubHttpMessageHandler(responder));

    public static HttpResponseMessage Json(HttpStatusCode code, string body)
        => new(code) { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") };
}

/// <summary>Always faults the send (simulates a network failure).</summary>
internal sealed class ThrowingHttpMessageHandler : HttpMessageHandler
{
    private readonly Exception _ex;

    public ThrowingHttpMessageHandler(Exception ex) => _ex = ex;

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        => Task.FromException<HttpResponseMessage>(_ex);
}
```

- [ ] **Step 2: Write the failing test**

`YoutubeDownloader.Core.Tests/GitHubUpdateServiceCheckTests.cs`:
```csharp
using System.Net;
using YoutubeDownloader.Core.Models;
using YoutubeDownloader.Core.Services;

namespace YoutubeDownloader.Core.Tests;

public class GitHubUpdateServiceCheckTests
{
    private const string ReleaseJson = """
    {
      "tag_name": "v1.2.0",
      "html_url": "https://github.com/VitakSharper/YoutubeDownloader/releases/tag/v1.2.0",
      "body": "Release notes",
      "assets": [
        { "name": "LiudochkaYoutubeDownloader.Avalonia-v1.2.0.zip", "browser_download_url": "https://example.test/app.zip" }
      ]
    }
    """;

    private static GitHubUpdateService Service(HttpClient client, string current = "1.0.0")
        => new(client, new UpdateOptions("VitakSharper", "YoutubeDownloader", Version.Parse(current)));

    [Fact]
    public async Task Newer_release_is_available_with_asset()
    {
        var client = StubHttpMessageHandler.Client(_ => StubHttpMessageHandler.Json(HttpStatusCode.OK, ReleaseJson));
        var info = await Service(client, "1.0.0").CheckAsync();

        Assert.True(info.IsUpdateAvailable);
        Assert.Equal(new Version(1, 2, 0), info.LatestVersion);
        Assert.Equal("https://example.test/app.zip", info.DownloadUrl);
        Assert.Equal("LiudochkaYoutubeDownloader.Avalonia-v1.2.0.zip", info.AssetName);
        Assert.Equal("Release notes", info.ReleaseNotes);
    }

    [Fact]
    public async Task Same_version_is_not_available()
    {
        var client = StubHttpMessageHandler.Client(_ => StubHttpMessageHandler.Json(HttpStatusCode.OK, ReleaseJson));
        var info = await Service(client, "1.2.0").CheckAsync();
        Assert.False(info.IsUpdateAvailable);
    }

    [Fact]
    public async Task No_release_returns_null_latest()
    {
        var client = StubHttpMessageHandler.Client(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        var info = await Service(client).CheckAsync();
        Assert.Null(info.LatestVersion);
        Assert.False(info.IsUpdateAvailable);
    }

    [Fact]
    public async Task Sends_user_agent_header()
    {
        HttpRequestMessage? seen = null;
        var client = StubHttpMessageHandler.Client(req =>
        {
            seen = req;
            return StubHttpMessageHandler.Json(HttpStatusCode.OK, ReleaseJson);
        });
        await Service(client).CheckAsync();
        Assert.NotNull(seen!.Headers.UserAgent);
        Assert.NotEmpty(seen.Headers.UserAgent);
    }

    [Fact]
    public async Task Rate_limit_throws_HttpRequestException_with_status()
    {
        var client = StubHttpMessageHandler.Client(_ => new HttpResponseMessage(HttpStatusCode.Forbidden));
        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => Service(client).CheckAsync());
        Assert.Equal(HttpStatusCode.Forbidden, ex.StatusCode);
    }
}
```

- [ ] **Step 3: Run test to verify it fails**

Run: `dotnet test YoutubeDownloader.Core.Tests --filter "GitHubUpdateServiceCheckTests" --nologo`
Expected: FAIL — `IUpdateService` / `GitHubUpdateService` do not exist.

- [ ] **Step 4: Write the interface and CheckAsync implementation**

`YoutubeDownloader.Core/Services/IUpdateService.cs`:
```csharp
using YoutubeDownloader.Core.Models;

namespace YoutubeDownloader.Core.Services;

public interface IUpdateService
{
    /// <summary>Queries the latest release; never throws for "no release" (returns LatestVersion == null).</summary>
    Task<UpdateInfo> CheckAsync(CancellationToken ct = default);

    /// <summary>Downloads + extracts the update zip; returns the extracted .exe path.</summary>
    Task<string> DownloadAndExtractAsync(UpdateInfo info, IProgress<double>? progress, CancellationToken ct = default);
}
```

`YoutubeDownloader.Core/Services/GitHubUpdateService.cs`:
```csharp
using System.Net;
using System.Text.Json;
using YoutubeDownloader.Core.Helpers;
using YoutubeDownloader.Core.Models;

namespace YoutubeDownloader.Core.Services;

public sealed class GitHubUpdateService : IUpdateService
{
    private const string UserAgent = "LiudochkaYoutubeDownloader";

    private readonly HttpClient _http;
    private readonly UpdateOptions _options;

    public GitHubUpdateService(HttpClient http, UpdateOptions options)
    {
        _http = http;
        _options = options;
    }

    public async Task<UpdateInfo> CheckAsync(CancellationToken ct = default)
    {
        var url = $"https://api.github.com/repos/{_options.Owner}/{_options.Repo}/releases/latest";
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.UserAgent.ParseAdd(UserAgent);
        req.Headers.Accept.ParseAdd("application/vnd.github+json");

        using var res = await _http.SendAsync(req, ct);
        if (res.StatusCode == HttpStatusCode.NotFound)
            return new UpdateInfo(_options.CurrentVersion, null, false, null, null, null, null);

        res.EnsureSuccessStatusCode();
        var json = await res.Content.ReadAsStringAsync(ct);
        return Parse(json);
    }

    private UpdateInfo Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var tag = root.TryGetProperty("tag_name", out var t) ? t.GetString() : null;
        var releaseUrl = root.TryGetProperty("html_url", out var h) ? h.GetString() : null;
        var notes = root.TryGetProperty("body", out var b) ? b.GetString() : null;

        string? downloadUrl = null, assetName = null;
        if (root.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
        {
            foreach (var a in assets.EnumerateArray())
            {
                var name = a.TryGetProperty("name", out var n) ? n.GetString() : null;
                if (name is not null && name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                {
                    assetName = name;
                    downloadUrl = a.TryGetProperty("browser_download_url", out var d) ? d.GetString() : null;
                    break;
                }
            }
        }

        Version? latest = ReleaseVersion.TryParseTag(tag, out var v) ? v : null;
        var available = latest is not null && latest > _options.CurrentVersion;
        return new UpdateInfo(_options.CurrentVersion, latest, available, downloadUrl, assetName, releaseUrl, notes);
    }

    public Task<string> DownloadAndExtractAsync(UpdateInfo info, IProgress<double>? progress, CancellationToken ct = default)
        => throw new NotImplementedException(); // Task 3
}
```

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test YoutubeDownloader.Core.Tests --filter "GitHubUpdateServiceCheckTests" --nologo`
Expected: PASS (5 tests).

- [ ] **Step 6: Commit**

```bash
git add YoutubeDownloader.Core/Services/IUpdateService.cs YoutubeDownloader.Core/Services/GitHubUpdateService.cs YoutubeDownloader.Core.Tests/TestHttp.cs YoutubeDownloader.Core.Tests/GitHubUpdateServiceCheckTests.cs
git commit -m "feat(update): GitHubUpdateService.CheckAsync queries + parses latest release"
```

---

### Task 3: GitHubUpdateService.DownloadAndExtractAsync

**Files:**
- Modify: `YoutubeDownloader.Core/Services/GitHubUpdateService.cs` (replace the `NotImplementedException` body + add a private download helper)
- Test: `YoutubeDownloader.Core.Tests/GitHubUpdateServiceDownloadTests.cs`

**Interfaces:**
- Consumes: `UpdateInfo` (DownloadUrl, AssetName, LatestVersion), `UpdateOptions.DownloadRoot`.
- Produces: implemented `DownloadAndExtractAsync` returning the extracted `.exe` full path.

- [ ] **Step 1: Write the failing test**

`YoutubeDownloader.Core.Tests/GitHubUpdateServiceDownloadTests.cs`:
```csharp
using System.IO.Compression;
using System.Net;
using System.Text;
using YoutubeDownloader.Core.Models;
using YoutubeDownloader.Core.Services;

namespace YoutubeDownloader.Core.Tests;

public class GitHubUpdateServiceDownloadTests
{
    private static byte[] BuildZip(string entryName, byte[] content)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = zip.CreateEntry(entryName);
            using var s = entry.Open();
            s.Write(content, 0, content.Length);
        }
        return ms.ToArray();
    }

    [Fact]
    public async Task Downloads_extracts_and_returns_exe_path()
    {
        var payload = Encoding.UTF8.GetBytes("dummy-exe-bytes");
        var zipBytes = BuildZip("LiudochkaYoutubeDownloader.Avalonia.exe", payload);

        var client = StubHttpMessageHandler.Client(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(zipBytes)
        });

        var tempRoot = Path.Combine(Path.GetTempPath(), "yt-update-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var options = new UpdateOptions("o", "r", new Version(1, 0, 0), tempRoot);
            var service = new GitHubUpdateService(client, options);
            var info = new UpdateInfo(new Version(1, 0, 0), new Version(1, 2, 0), true,
                "https://example.test/app.zip", "app.zip", null, null);

            var progressValues = new List<double>();
            var exePath = await service.DownloadAndExtractAsync(info, new Progress<double>(progressValues.Add));

            Assert.True(File.Exists(exePath));
            Assert.EndsWith(".exe", exePath);
            Assert.Equal(payload, await File.ReadAllBytesAsync(exePath));
            Assert.Contains(tempRoot, exePath);
        }
        finally
        {
            if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public async Task Missing_asset_throws()
    {
        var client = StubHttpMessageHandler.Client(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var service = new GitHubUpdateService(client, new UpdateOptions("o", "r", new Version(1, 0, 0)));
        var info = new UpdateInfo(new Version(1, 0, 0), new Version(1, 2, 0), true, null, null, null, null);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.DownloadAndExtractAsync(info, null));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test YoutubeDownloader.Core.Tests --filter "GitHubUpdateServiceDownloadTests" --nologo`
Expected: FAIL — `DownloadAndExtractAsync` throws `NotImplementedException`.

- [ ] **Step 3: Implement DownloadAndExtractAsync**

In `GitHubUpdateService.cs`, add `using System.IO.Compression;` at the top, then replace the placeholder method with:
```csharp
    public async Task<string> DownloadAndExtractAsync(UpdateInfo info, IProgress<double>? progress, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(info.DownloadUrl) || string.IsNullOrEmpty(info.AssetName))
            throw new InvalidOperationException("The latest release has no downloadable file.");

        var versionText = info.LatestVersion?.ToString() ?? "latest";
        var root = _options.DownloadRoot;
        if (string.IsNullOrEmpty(root))
        {
            root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
            if (!Directory.Exists(root))
                root = Path.GetTempPath();
        }

        var targetDir = Path.Combine(root, $"Liudochka-{versionText}");
        Directory.CreateDirectory(targetDir);

        var zipPath = Path.Combine(targetDir, info.AssetName);
        await DownloadFileAsync(info.DownloadUrl, zipPath, progress, ct);

        ZipFile.ExtractToDirectory(zipPath, targetDir, overwriteFiles: true);

        var exe = Directory.EnumerateFiles(targetDir, "*.exe", SearchOption.AllDirectories).FirstOrDefault();
        return exe ?? targetDir;
    }

    private async Task DownloadFileAsync(string url, string destPath, IProgress<double>? progress, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.UserAgent.ParseAdd(UserAgent);

        using var res = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        res.EnsureSuccessStatusCode();

        var total = res.Content.Headers.ContentLength ?? -1L;
        await using var src = await res.Content.ReadAsStreamAsync(ct);
        await using var dst = File.Create(destPath);

        var buffer = new byte[81920];
        long readTotal = 0;
        int read;
        while ((read = await src.ReadAsync(buffer, ct)) > 0)
        {
            await dst.WriteAsync(buffer.AsMemory(0, read), ct);
            readTotal += read;
            if (total > 0)
                progress?.Report((double)readTotal / total);
        }

        progress?.Report(1.0);
    }
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test YoutubeDownloader.Core.Tests --filter "GitHubUpdateServiceDownloadTests" --nologo`
Expected: PASS (2 tests).

- [ ] **Step 5: Commit**

```bash
git add YoutubeDownloader.Core/Services/GitHubUpdateService.cs YoutubeDownloader.Core.Tests/GitHubUpdateServiceDownloadTests.cs
git commit -m "feat(update): download + extract the release zip, return new exe path"
```

---

### Task 4: UpdateViewModel

**Files:**
- Create: `YoutubeDownloader.Core/ViewModels/UpdateViewModel.cs`
- Test: `YoutubeDownloader.Core.Tests/UpdateViewModelTests.cs`

**Interfaces:**
- Consumes: `IUpdateService` (Task 2/3), `IFileRevealer` (existing Core service), `UpdateInfo`.
- Produces: `UpdateViewModel(IUpdateService, IFileRevealer)`; observable props `Status`, `IsUpdateAvailable`, `IsBusy`, `Progress`; generated commands `CheckForUpdatesCommand`, `DownloadUpdateCommand`.

- [ ] **Step 1: Write the failing test**

`YoutubeDownloader.Core.Tests/UpdateViewModelTests.cs`:
```csharp
using System.Net;
using Moq;
using YoutubeDownloader.Core.Models;
using YoutubeDownloader.Core.Services;
using YoutubeDownloader.Core.ViewModels;

namespace YoutubeDownloader.Core.Tests;

public class UpdateViewModelTests
{
    private static UpdateInfo Available =>
        new(new Version(1, 0, 0), new Version(1, 1, 0), true, "https://x/app.zip", "app.zip", "https://x/rel", "notes");

    private static (UpdateViewModel vm, Mock<IUpdateService> svc, Mock<IFileRevealer> rev) Make()
    {
        var svc = new Mock<IUpdateService>();
        var rev = new Mock<IFileRevealer>();
        return (new UpdateViewModel(svc.Object, rev.Object), svc, rev);
    }

    [Fact]
    public async Task Check_available_sets_flag_and_status()
    {
        var (vm, svc, _) = Make();
        svc.Setup(s => s.CheckAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Available);

        await vm.CheckForUpdatesCommand.ExecuteAsync(null);

        Assert.True(vm.IsUpdateAvailable);
        Assert.Contains("1.1.0", vm.Status);
        Assert.False(vm.IsBusy);
    }

    [Fact]
    public async Task Check_uptodate_reports_latest()
    {
        var (vm, svc, _) = Make();
        svc.Setup(s => s.CheckAsync(It.IsAny<CancellationToken>()))
           .ReturnsAsync(new UpdateInfo(new Version(1, 1, 0), new Version(1, 1, 0), false, null, null, null, null));

        await vm.CheckForUpdatesCommand.ExecuteAsync(null);

        Assert.False(vm.IsUpdateAvailable);
        Assert.Contains("latest", vm.Status, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Check_no_release_reports_none()
    {
        var (vm, svc, _) = Make();
        svc.Setup(s => s.CheckAsync(It.IsAny<CancellationToken>()))
           .ReturnsAsync(new UpdateInfo(new Version(1, 0, 0), null, false, null, null, null, null));

        await vm.CheckForUpdatesCommand.ExecuteAsync(null);

        Assert.False(vm.IsUpdateAvailable);
        Assert.Contains("No releases", vm.Status, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Check_rate_limited_reports_friendly()
    {
        var (vm, svc, _) = Make();
        svc.Setup(s => s.CheckAsync(It.IsAny<CancellationToken>()))
           .ThrowsAsync(new HttpRequestException("nope", null, HttpStatusCode.Forbidden));

        await vm.CheckForUpdatesCommand.ExecuteAsync(null);

        Assert.Contains("rate-limited", vm.Status, StringComparison.OrdinalIgnoreCase);
        Assert.False(vm.IsBusy);
    }

    [Fact]
    public async Task Download_reveals_extracted_exe()
    {
        var (vm, svc, rev) = Make();
        svc.Setup(s => s.CheckAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Available);
        svc.Setup(s => s.DownloadAndExtractAsync(It.IsAny<UpdateInfo>(), It.IsAny<IProgress<double>?>(), It.IsAny<CancellationToken>()))
           .ReturnsAsync(@"C:\Downloads\Liudochka-1.1.0\app.exe");

        await vm.CheckForUpdatesCommand.ExecuteAsync(null);
        await vm.DownloadUpdateCommand.ExecuteAsync(null);

        rev.Verify(r => r.RevealInFolder(@"C:\Downloads\Liudochka-1.1.0\app.exe"), Times.Once);
        Assert.Contains("replace", vm.Status, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Download_without_check_does_nothing()
    {
        var (vm, svc, rev) = Make();
        await vm.DownloadUpdateCommand.ExecuteAsync(null);
        svc.Verify(s => s.DownloadAndExtractAsync(It.IsAny<UpdateInfo>(), It.IsAny<IProgress<double>?>(), It.IsAny<CancellationToken>()), Times.Never);
        rev.Verify(r => r.RevealInFolder(It.IsAny<string>()), Times.Never);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test YoutubeDownloader.Core.Tests --filter "UpdateViewModelTests" --nologo`
Expected: FAIL — `UpdateViewModel` does not exist.

- [ ] **Step 3: Implement UpdateViewModel**

`YoutubeDownloader.Core/ViewModels/UpdateViewModel.cs`:
```csharp
using System.Net;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using YoutubeDownloader.Core.Models;
using YoutubeDownloader.Core.Services;

namespace YoutubeDownloader.Core.ViewModels;

public partial class UpdateViewModel : ObservableObject
{
    private readonly IUpdateService _updates;
    private readonly IFileRevealer _revealer;
    private UpdateInfo? _latest;

    public UpdateViewModel(IUpdateService updates, IFileRevealer revealer)
    {
        _updates = updates;
        _revealer = revealer;
    }

    [ObservableProperty] private string _status = "";
    [ObservableProperty] private bool _isUpdateAvailable;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private double _progress;

    [RelayCommand]
    private async Task CheckForUpdatesAsync()
    {
        if (IsBusy)
            return;

        IsBusy = true;
        IsUpdateAvailable = false;
        Status = "Checking for updates…";
        try
        {
            var info = await _updates.CheckAsync();
            _latest = info;

            if (info.LatestVersion is null)
                Status = "No releases have been published yet.";
            else if (info.IsUpdateAvailable && !string.IsNullOrEmpty(info.DownloadUrl))
            {
                IsUpdateAvailable = true;
                Status = $"Version {info.LatestVersion} is available (you have {info.CurrentVersion}).";
            }
            else if (info.IsUpdateAvailable)
                Status = $"Version {info.LatestVersion} is available, but the release has no downloadable file.";
            else
                Status = $"You're on the latest version ({info.CurrentVersion}).";
        }
        catch (Exception ex)
        {
            Status = FriendlyError(ex);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task DownloadUpdateAsync()
    {
        if (IsBusy || _latest is null || !IsUpdateAvailable)
            return;

        IsBusy = true;
        Progress = 0;
        Status = "Downloading…";
        try
        {
            var progress = new Progress<double>(p => Progress = p);
            var exe = await _updates.DownloadAndExtractAsync(_latest, progress);
            _revealer.RevealInFolder(exe);
            Status = "Downloaded. Close the app, then replace the old .exe with the new one.";
        }
        catch (Exception ex)
        {
            Status = FriendlyError(ex);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static string FriendlyError(Exception ex) => ex switch
    {
        HttpRequestException { StatusCode: HttpStatusCode.Forbidden } => "Update check is rate-limited — try again in a few minutes.",
        HttpRequestException => "Couldn't reach GitHub. Check your connection.",
        _ => "Update failed — please try again.",
    };
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test YoutubeDownloader.Core.Tests --filter "UpdateViewModelTests" --nologo`
Expected: PASS (6 tests).

- [ ] **Step 5: Commit**

```bash
git add YoutubeDownloader.Core/ViewModels/UpdateViewModel.cs YoutubeDownloader.Core.Tests/UpdateViewModelTests.cs
git commit -m "feat(update): UpdateViewModel (check/download commands + friendly status)"
```

---

### Task 5: Avalonia wiring — version, DI, window injection, header UI

**Files:**
- Modify: `YoutubeDownloader.Avalonia/YoutubeDownloader.Avalonia.csproj` (add `<Version>`)
- Modify: `YoutubeDownloader.Avalonia/App.axaml.cs` (register `IUpdateService` + `UpdateViewModel`)
- Modify: `YoutubeDownloader.Avalonia/Views/MainWindow.axaml.cs` (inject + expose `Update`)
- Modify: `YoutubeDownloader.Avalonia/Views/MainWindow.axaml` (header UI)

**Interfaces:**
- Consumes: `IUpdateService`, `GitHubUpdateService`, `UpdateViewModel`, `UpdateOptions` (Tasks 2–4).
- Produces: `MainWindow.Update : UpdateViewModel` property bound in XAML via `#Root.Update`.

No unit test (DI/UI composition); verified by build + startup smoke.

- [ ] **Step 1: Add the version to the csproj**

In `YoutubeDownloader.Avalonia.csproj`, inside the first `<PropertyGroup>`, add after `<Product>`:
```xml
    <Version>1.0.0</Version>
```

- [ ] **Step 2: Register the services in App.axaml.cs**

Add usings at the top of `App.axaml.cs`:
```csharp
using System.Net.Http;
using YoutubeDownloader.Core.Models;
```
Then, immediately before `services.AddSingleton<MainViewModel>();`, insert:
```csharp
        var currentVersion = typeof(App).Assembly.GetName().Version ?? new Version(1, 0, 0);
        var updateHttp = new HttpClient();
        services.AddSingleton<IUpdateService>(_ =>
            new GitHubUpdateService(updateHttp, new UpdateOptions("VitakSharper", "YoutubeDownloader", currentVersion)));
        services.AddSingleton<UpdateViewModel>();
```

- [ ] **Step 3: Inject + expose the VM in MainWindow.axaml.cs**

Replace the DI constructor and add the `Update` property. The class becomes:
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
        Update = null!;
    }

    public MainWindow(MainViewModel vm, AvaloniaClipboardService clipboard, UpdateViewModel update) : this()
    {
        _vm = vm;
        _clipboard = clipboard;
        Update = update;
        DataContext = vm;

        // Fires on first show (covers startup) and on every refocus.
        // Refresh the clipboard cache first, then let the VM inspect it.
        Activated += async (_, _) =>
        {
            await _clipboard.RefreshAsync();
            _vm.CheckClipboardCommand.Execute(null);
        };
    }

    /// <summary>Update-check VM, surfaced for the header controls (bound via #Root.Update).</summary>
    public UpdateViewModel Update { get; }
}
```

- [ ] **Step 4: Add the header UI in MainWindow.axaml**

Replace the header `CheckBox` element (currently `<CheckBox Grid.Column="2" Content="Always on top" .../>`) with this `StackPanel`:
```xml
      <StackPanel Grid.Column="2" VerticalAlignment="Center" HorizontalAlignment="Right" Spacing="6">
        <CheckBox Content="Always on top" IsChecked="{Binding AlwaysOnTop}"
                  HorizontalAlignment="Right" FontSize="11.5" Foreground="{StaticResource TextSecondaryBrush}" />
        <StackPanel DataContext="{Binding #Root.Update}" x:DataType="vm:UpdateViewModel"
                    HorizontalAlignment="Right" Spacing="4">
          <StackPanel Orientation="Horizontal" Spacing="6" HorizontalAlignment="Right">
            <Button Classes="chip" Content="Check for updates"
                    Command="{Binding CheckForUpdatesCommand}" IsEnabled="{Binding !IsBusy}" />
            <Button Classes="chip" Content="Download"
                    Command="{Binding DownloadUpdateCommand}"
                    IsVisible="{Binding IsUpdateAvailable}" IsEnabled="{Binding !IsBusy}" />
          </StackPanel>
          <TextBlock Text="{Binding Status}" FontSize="10.5" TextWrapping="Wrap"
                     MaxWidth="240" HorizontalAlignment="Right" TextAlignment="Right"
                     Foreground="{StaticResource TextMutedBrush}"
                     IsVisible="{Binding Status, Converter={StaticResource NotEmpty}}" />
        </StackPanel>
      </StackPanel>
```
(The `vm:` namespace is already declared on the `<Window>` root; `NotEmpty` and `TextMutedBrush` are existing resources. Do not change any other `x:Name`, `Classes`, or binding.)

- [ ] **Step 5: Build the whole solution**

Run: `dotnet build YoutubeDownloader.sln --nologo`
Expected: Build succeeded, 0 errors, 0 warnings (Core, WPF App, Avalonia, Tests).

- [ ] **Step 6: Startup smoke (controller)**

Run: `dotnet run --project YoutubeDownloader.Avalonia` briefly (launch, confirm the window renders with the new "Check for updates" button in the header, then close).
Expected: app launches; no XAML/binding exceptions in output.

- [ ] **Step 7: Commit**

```bash
git add YoutubeDownloader.Avalonia/YoutubeDownloader.Avalonia.csproj YoutubeDownloader.Avalonia/App.axaml.cs YoutubeDownloader.Avalonia/Views/MainWindow.axaml.cs YoutubeDownloader.Avalonia/Views/MainWindow.axaml
git commit -m "feat(avalonia): wire update-check service + header Check-for-updates UI"
```

---

### Task 6: Release tooling (`release.cmd`)

**Files:**
- Create: `release.cmd` (repo root)
- Modify: `README.md` (add a short "Releasing / updates" section)

**Interfaces:**
- Consumes: the Avalonia publish flags (mirrors `publish.cmd`), `<Version>` from the csproj.
- Produces: `dist\LiudochkaYoutubeDownloader.Avalonia-v<ver>.zip` + the documented `gh release create` command.

No unit test. Verified by running the publish+zip half (the `gh` step is NOT executed by the script).

- [ ] **Step 1: Create release.cmd**

`release.cmd`:
```bat
@echo off
setlocal
cd /d "%~dp0"

set VERSION=1.0.0
echo Publishing + zipping Liudochka Youtube Downloader (Avalonia) v%VERSION% ...
echo.

dotnet publish "YoutubeDownloader.Avalonia\YoutubeDownloader.Avalonia.csproj" -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=none -p:DebugSymbols=false -o "dist"
if errorlevel 1 exit /b 1

set ZIP=dist\LiudochkaYoutubeDownloader.Avalonia-v%VERSION%.zip
powershell -NoProfile -Command "Compress-Archive -Force -Path 'dist\LiudochkaYoutubeDownloader.Avalonia.exe' -DestinationPath '%ZIP%'"
if errorlevel 1 exit /b 1

echo.
echo Created %ZIP%
echo.
echo To PUBLISH the GitHub release (creates a public release - run manually):
echo   gh release create v%VERSION% "%ZIP%" --title "v%VERSION%" --notes "What changed in this version"
echo.
pause
```

- [ ] **Step 2: Document it in README.md**

Append to `README.md`:
```markdown
## Releasing / in-app updates

The Avalonia app has a manual **Check for updates** button (header) that queries GitHub
Releases. To cut a release:

1. Bump `<Version>` in `YoutubeDownloader.Avalonia.csproj` and in `release.cmd`.
2. Run `release.cmd` — it publishes the single-file exe and zips it to `dist\`.
3. Publish the release (this is public):
   `gh release create v<version> "dist\LiudochkaYoutubeDownloader.Avalonia-v<version>.zip" --title "v<version>" --notes "..."`

Users on an older build click **Check for updates** → **Download**; the app fetches the
zip, extracts the new exe to their Downloads folder, and opens it in Explorer to swap in.
```

- [ ] **Step 3: Verify the publish+zip half runs**

Run: `release.cmd` (or its `dotnet publish` + `Compress-Archive` lines).
Expected: `dist\LiudochkaYoutubeDownloader.Avalonia-v1.0.0.zip` exists and contains the exe. Do NOT run the `gh release create` line.

- [ ] **Step 4: Commit**

```bash
git add release.cmd README.md
git commit -m "chore(update): release.cmd to publish + zip the Avalonia single-file exe"
```

---

### Task 7: Final verification + polish

**Files:** none required (polish only if issues found).

- [ ] **Step 1: Full solution build**

Run: `dotnet build YoutubeDownloader.sln --nologo`
Expected: 0 errors, 0 warnings.

- [ ] **Step 2: Full test run**

Run: `dotnet test YoutubeDownloader.sln --nologo`
Expected: all pass — 88 pre-existing + the new update tests (ReleaseVersion, CheckAsync ×5, Download ×2, UpdateViewModel ×6).

- [ ] **Step 3: Avalonia Release build**

Run: `dotnet build YoutubeDownloader.Avalonia -c Release --nologo`
Expected: 0 errors, 0 warnings.

- [ ] **Step 4: Manual smoke (user, cannot run headlessly)**

1. Launch the Avalonia app → click **Check for updates**. With no release published yet, expect status "No releases have been published yet."
2. (After a `v1.1.0` release is published against a `1.0.0` build) re-check → "Version 1.1.0 is available"; **Download** appears; click it → Explorer opens on the extracted exe; status tells you to replace the exe.
3. Confirm the WPF app (`YoutubeDownloader.App`) still launches + downloads (it must be unaffected).

- [ ] **Step 5: Commit (only if polish edits were made)**

```bash
git add -A
git commit -m "chore(update): final verification and polish"
```
