namespace Filociraptor.Diagnostics;

// the numbers this project exists to produce, so they are collected all the time rather than behind a build flag.
internal sealed class PerfCounters
{
    public const int SampleCount = 240;

    private readonly double[] _frameMilliseconds = new double[SampleCount];
    private readonly double _tickToMilliseconds = 1000d / Stopwatch.Frequency;
    private int _sampleIndex;
    private int _sampleTotal;
    private long _frameStart;

    public double ScanMilliseconds { get; set; }
    public double SortMilliseconds { get; set; }
    public double FirstRowsMilliseconds { get; set; }
    public int ItemCount { get; set; }
    public long BufferBytes { get; set; }
    public long AllocatedBytesAtScanStart { get; set; }
    public long ScanAllocatedBytes { get; set; }
    public ReadOnlySpan<double> Samples => _frameMilliseconds;
    public int SampleIndex => _sampleIndex;

    public void BeginFrame() => _frameStart = Stopwatch.GetTimestamp();

    public void EndFrame()
    {
        var elapsed = (Stopwatch.GetTimestamp() - _frameStart) * _tickToMilliseconds;
        _frameMilliseconds[_sampleIndex] = elapsed;
        _sampleIndex = (_sampleIndex + 1) % SampleCount;
        if (_sampleTotal < SampleCount)
        {
            _sampleTotal++;
        }
    }

    public double LastFrameMilliseconds => _frameMilliseconds[(_sampleIndex - 1 + SampleCount) % SampleCount];

    public double AverageFrameMilliseconds
    {
        get
        {
            if (_sampleTotal == 0)
                return 0;

            var total = 0d;
            for (var i = 0; i < _sampleTotal; i++)
            {
                total += _frameMilliseconds[i];
            }
            return total / _sampleTotal;
        }
    }

    public double MaxFrameMilliseconds
    {
        get
        {
            var max = 0d;
            for (var i = 0; i < _sampleTotal; i++)
            {
                if (_frameMilliseconds[i] > max)
                {
                    max = _frameMilliseconds[i];
                }
            }
            return max;
        }
    }
}
