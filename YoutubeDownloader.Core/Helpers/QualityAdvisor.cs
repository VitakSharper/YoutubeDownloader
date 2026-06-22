namespace YoutubeDownloader.Core.Helpers;

public static class QualityAdvisor
{
    public static bool WouldUpscale(int sourceKbps, int targetKbps)
        => sourceKbps > 0 && targetKbps > sourceKbps;

    public static string Describe(string codec, int kbps) => $"{codec}, {kbps} kbps";
}
