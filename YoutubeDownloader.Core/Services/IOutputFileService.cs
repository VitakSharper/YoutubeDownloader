namespace YoutubeDownloader.Core.Services;

/// <summary>Promotes a finished part file to the user's chosen path, or throws it away.</summary>
public interface IOutputFileService
{
    /// <summary>Replaces <paramref name="target"/> with <paramref name="partPath"/> (same-folder rename; overwrites).</summary>
    void Commit(string partPath, string target);

    /// <summary>Best-effort delete of an abandoned part file. Never throws.</summary>
    void Discard(string partPath);
}
