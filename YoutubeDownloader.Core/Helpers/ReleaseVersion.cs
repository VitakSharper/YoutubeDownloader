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
