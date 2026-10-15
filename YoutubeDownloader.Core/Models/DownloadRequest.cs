namespace YoutubeDownloader.Core.Models;

/// <param name="TargetPath">Final file path; FFmpeg writes its ".part" sibling and the pipeline commits it.</param>
/// <param name="Tags">Written into MP3/Opus files; ignored for MP4.</param>
public sealed record DownloadRequest(VideoInfo Info, OutputFormat Format, string TargetPath, MediaTags Tags, string FfmpegPath);
