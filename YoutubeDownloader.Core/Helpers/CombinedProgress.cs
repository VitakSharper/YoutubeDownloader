namespace YoutubeDownloader.Core.Helpers;

/// <summary>
/// Combines two 0..1 progress sources into one 0..1 value with fixed weights.
/// Thread-safe; the combined value never decreases.
/// </summary>
public sealed class CombinedProgress
{
    private readonly object _gate = new();
    private readonly double _firstWeight;
    private double _first;
    private double _second;
    private double _value;

    public CombinedProgress(double firstWeight)
    {
        if (firstWeight is < 0.0 or > 1.0)
            throw new ArgumentOutOfRangeException(nameof(firstWeight), firstWeight, "Weight must be between 0 and 1.");
        _firstWeight = firstWeight;
    }

    /// <summary>Report the first source's fraction; returns the combined value.</summary>
    public double ReportFirst(double fraction)
    {
        lock (_gate)
        {
            _first = Math.Max(_first, Math.Clamp(fraction, 0.0, 1.0));
            return Recompute();
        }
    }

    /// <summary>Report the second source's fraction; returns the combined value.</summary>
    public double ReportSecond(double fraction)
    {
        lock (_gate)
        {
            _second = Math.Max(_second, Math.Clamp(fraction, 0.0, 1.0));
            return Recompute();
        }
    }

    private double Recompute()
    {
        _value = Math.Max(_value, (_first * _firstWeight) + (_second * (1.0 - _firstWeight)));
        return _value;
    }
}
