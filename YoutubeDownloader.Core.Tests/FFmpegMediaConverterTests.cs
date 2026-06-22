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
}
