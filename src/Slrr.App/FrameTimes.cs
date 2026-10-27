using System.Globalization;

namespace Slrr.App;

/// <summary>Rolling and whole-run frame-time statistics (milliseconds).</summary>
public sealed class FrameTimes(int window = 120)
{
    private readonly Queue<double> _recent = new();
    private readonly List<double> _all = [];

    public int Count => _all.Count;

    public void Add(double ms)
    {
        if (!double.IsFinite(ms) || ms < 0)
        {
            return;
        }

        _recent.Enqueue(ms);
        while (_recent.Count > window)
        {
            _recent.Dequeue();
        }

        _all.Add(ms);
    }

    public double RecentMean => _recent.Count == 0 ? 0 : _recent.Average();

    public double RecentMax => _recent.Count == 0 ? 0 : _recent.Max();

    public double Fps => RecentMean > 0 ? 1000.0 / RecentMean : 0;

    public double Mean => _all.Count == 0 ? 0 : _all.Average();

    public double Max => _all.Count == 0 ? 0 : _all.Max();

    public double Percentile(double p)
    {
        if (_all.Count == 0)
        {
            return 0;
        }

        var s = _all.Order().ToList();
        var i = (int)Math.Clamp(Math.Ceiling(p / 100.0 * s.Count) - 1, 0, s.Count - 1);
        return s[i];
    }

    public string Summary(string name) => Count == 0 ? $"{name}: none" : string.Create(CultureInfo.InvariantCulture,
        $"{name}: n {Count}, mean {Mean:F2} ms, p50 {Percentile(50):F2}, p95 {Percentile(95):F2}, p99 {Percentile(99):F2}, max {Max:F2}");
}
