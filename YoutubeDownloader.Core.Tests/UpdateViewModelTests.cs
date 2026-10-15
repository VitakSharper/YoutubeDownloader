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

    [Fact]
    public async Task Check_available_without_download_url_reports_no_file()
    {
        var (vm, svc, _) = Make();
        svc.Setup(s => s.CheckAsync(It.IsAny<CancellationToken>()))
           .ReturnsAsync(new UpdateInfo(new Version(1, 0, 0), new Version(1, 1, 0), true, null, null, null, null));

        await vm.CheckForUpdatesCommand.ExecuteAsync(null);

        Assert.False(vm.IsUpdateAvailable);
        Assert.Contains("no downloadable file", vm.Status, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Download_sets_IsDownloadingUpdate_while_running_and_clears_after()
    {
        var (vm, svc, _) = Make();
        var tcs = new TaskCompletionSource<string>();
        svc.Setup(s => s.CheckAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Available);
        svc.Setup(s => s.DownloadAndExtractAsync(It.IsAny<UpdateInfo>(), It.IsAny<IProgress<double>?>(), It.IsAny<CancellationToken>()))
           .Returns(tcs.Task);

        await vm.CheckForUpdatesCommand.ExecuteAsync(null);
        var run = vm.DownloadUpdateCommand.ExecuteAsync(null);

        Assert.True(vm.IsDownloadingUpdate);

        tcs.SetResult(@"C:\Downloads\Liudochka-1.1.0\app.exe");
        await run;

        Assert.False(vm.IsDownloadingUpdate);
    }

    [Fact]
    public async Task Download_failure_clears_IsDownloadingUpdate()
    {
        var (vm, svc, _) = Make();
        svc.Setup(s => s.CheckAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Available);
        svc.Setup(s => s.DownloadAndExtractAsync(It.IsAny<UpdateInfo>(), It.IsAny<IProgress<double>?>(), It.IsAny<CancellationToken>()))
           .ThrowsAsync(new HttpRequestException("x"));

        await vm.CheckForUpdatesCommand.ExecuteAsync(null);
        await vm.DownloadUpdateCommand.ExecuteAsync(null);

        Assert.False(vm.IsDownloadingUpdate);
        Assert.Contains("Couldn't reach GitHub", vm.Status);
    }

    [Fact]
    public async Task Check_does_not_set_IsDownloadingUpdate()
    {
        var (vm, svc, _) = Make();
        var tcs = new TaskCompletionSource<UpdateInfo>();
        svc.Setup(s => s.CheckAsync(It.IsAny<CancellationToken>())).Returns(tcs.Task);

        var run = vm.CheckForUpdatesCommand.ExecuteAsync(null);

        Assert.True(vm.IsBusy);
        Assert.False(vm.IsDownloadingUpdate);

        tcs.SetResult(Available);
        await run;

        Assert.False(vm.IsDownloadingUpdate);
    }

    [Fact]
    public async Task Download_resets_progress_to_zero_at_start()
    {
        var (vm, svc, _) = Make();
        var tcs = new TaskCompletionSource<string>();
        svc.Setup(s => s.CheckAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Available);
        svc.Setup(s => s.DownloadAndExtractAsync(It.IsAny<UpdateInfo>(), It.IsAny<IProgress<double>?>(), It.IsAny<CancellationToken>()))
           .Returns(tcs.Task);

        await vm.CheckForUpdatesCommand.ExecuteAsync(null);
        vm.Progress = 0.7;
        var run = vm.DownloadUpdateCommand.ExecuteAsync(null);

        Assert.Equal(0, vm.Progress);

        tcs.SetResult(@"C:\Downloads\Liudochka-1.1.0\app.exe");
        await run;
    }
}
