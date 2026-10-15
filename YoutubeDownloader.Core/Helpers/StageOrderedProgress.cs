using YoutubeDownloader.Core.Models;

namespace YoutubeDownloader.Core.Helpers;

/// <summary>
/// Forwards pipeline reports but drops any from a stage earlier than one already forwarded: a Progress&lt;T&gt;
/// post from a finished download can arrive after the next stage started, and must not flip the status back.
/// </summary>
internal sealed class StageOrderedProgress : IProgress<PipelineProgress>
{
    private readonly IProgress<PipelineProgress> _inner;
    private readonly object _gate = new();
    private PipelineStage? _current;

    public StageOrderedProgress(IProgress<PipelineProgress> inner) => _inner = inner;

    public void Report(PipelineProgress value)
    {
        lock (_gate)
        {
            if (_current is { } current && value.Stage < current)
                return;
            _current = value.Stage;
        }
        _inner.Report(value);
    }
}
