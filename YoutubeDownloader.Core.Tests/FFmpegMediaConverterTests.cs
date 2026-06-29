using YoutubeDownloader.Core.Services;

namespace YoutubeDownloader.Core.Tests;

public class FFmpegMediaConverterTests
{
    [Fact]
    public async Task ConvertToMp3Async_WhenFfmpegPathInvalid_Throws()
    {
        var converter = new FFmpegMediaConverter();
        var missing = Path.Combine(Path.GetTempPath(), "definitely-not-ffmpeg-" + Guid.NewGuid().ToString("N") + ".exe");

        await Assert.ThrowsAnyAsync<Exception>(() =>
            converter.ConvertToMp3Async(missing, "in.webm", "out.mp3", 192, TimeSpan.FromSeconds(10), null, CancellationToken.None));
    }

    [Fact]
    public async Task RunAsync_WhenCancelled_KillsTheSpawnedProcess()
    {
        // The spawned process keeps "working" by rewriting a heartbeat file ~every 100ms, bumping its
        // last-write time. While alive the timestamp keeps moving; once killed it stops. It self-terminates
        // after ~30s so an unfixed (orphaning) run cannot leak a long-lived process.
        var heartbeat = Path.Combine(Path.GetTempPath(), "ffmpeg-cancel-heartbeat-" + Guid.NewGuid().ToString("N") + ".txt");
        var script = $"for ($i = 0; $i -lt 300; $i++) {{ Set-Content -LiteralPath '{heartbeat}' -Value $i; Start-Sleep -Milliseconds 100 }}";
        var arguments = $"-NoProfile -NonInteractive -Command \"{script}\"";

        using var cts = new CancellationTokenSource();
        var run = FFmpegMediaConverter.RunAsync("powershell.exe", arguments, TimeSpan.Zero, null, cts.Token);

        // Wait until the process is actually running and writing.
        await WaitUntilAsync(() => File.Exists(heartbeat), TimeSpan.FromSeconds(15));

        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);

        // Give the kill a moment to fully take effect, then confirm the process is no longer writing.
        await Task.Delay(TimeSpan.FromMilliseconds(750));
        var lastWriteAfterCancel = File.GetLastWriteTimeUtc(heartbeat);
        await Task.Delay(TimeSpan.FromSeconds(1));
        var lastWriteLater = File.GetLastWriteTimeUtc(heartbeat);

        Assert.Equal(lastWriteAfterCancel, lastWriteLater);

        try { File.Delete(heartbeat); } catch { /* best effort cleanup */ }
    }

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
                return;
            await Task.Delay(50);
        }

        throw new TimeoutException("Condition was not met within the timeout.");
    }
}
