using System.Reflection;
using Xabe.FFmpeg.Downloader;

namespace YoutubeDownloader.Core.Tests;

/// <summary>
/// Xabe's packages are compiled against assemblies they don't declare as NuGet dependencies
/// (System.Text.Json 9). If one can't load, the first-run FFmpeg download crashes at runtime.
/// </summary>
public class XabeDependencyTests
{
    [Fact]
    public void XabeAssemblies_AllReferencedAssembliesLoad()
    {
        var xabe = new[] { typeof(FFmpegDownloader).Assembly, typeof(Xabe.FFmpeg.FFmpeg).Assembly };

        foreach (var assembly in xabe)
            foreach (var reference in assembly.GetReferencedAssemblies())
                Assembly.Load(reference);
    }
}
