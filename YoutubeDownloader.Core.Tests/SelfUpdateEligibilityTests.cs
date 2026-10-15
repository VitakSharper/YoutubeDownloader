using YoutubeDownloader.Core.Helpers;

namespace YoutubeDownloader.Core.Tests;

public class SelfUpdateEligibilityTests : IDisposable
{
    private readonly string _dir =
        Path.Combine(Path.GetTempPath(), "ydl-eligibility-tests", Guid.NewGuid().ToString("N"));

    private string ExePath => Path.Combine(_dir, "LiudochkaYoutubeDownloader.Avalonia.exe");

    private static bool Writable(string _) => true;

    [Fact]
    public void Null_process_path_is_not_eligible()
    {
        Assert.False(SelfUpdateEligibility.IsEligible(null, false, File.Exists, Writable));
    }

    [Fact]
    public void Debugger_attached_is_not_eligible()
    {
        Directory.CreateDirectory(_dir);

        Assert.False(SelfUpdateEligibility.IsEligible(ExePath, true, File.Exists, Writable));
    }

    [Fact]
    public void Dll_beside_exe_is_not_eligible()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, SelfUpdateEligibility.AppDllName), "stub");

        Assert.False(SelfUpdateEligibility.IsEligible(ExePath, false, File.Exists, Writable));
    }

    [Fact]
    public void Writable_single_file_is_eligible()
    {
        Directory.CreateDirectory(_dir);
        string? probed = null;

        var eligible = SelfUpdateEligibility.IsEligible(ExePath, false, File.Exists, dir =>
        {
            probed = dir;
            return true;
        });

        Assert.True(eligible);
        Assert.Equal(_dir, probed);
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }
}
