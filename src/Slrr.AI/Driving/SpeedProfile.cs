using Slrr.AI.Roads;

namespace Slrr.AI.Driving;

/// <summary>
/// Highest speed at each point of a path that the car can still corner and brake for (GUESSED planner, standard
/// "forward/backward pass" profile): v = min(limit, top, sqrt(aLat / |κ|)), then a backward pass so the car can brake
/// from v(s) to v(s + ds) at <c>aBrake</c>. Closed paths get two backward passes (wrap-around).
/// </summary>
public sealed class SpeedProfile
{
    /// <summary>Largest curvature the planner believes (1/m).</summary>
    public const double MaxCurvature = 1.0 / 8;

    private readonly double[] _v;
    private readonly double _ds;
    private readonly bool _closed;
    private readonly float _length;

    public SpeedProfile(IRoadPath path, double lateralAccel, double brakeDecel, double topSpeed, bool stopAtEnd, double spacing = 2.0, float laneOffset = 0f)
    {
        _closed = path.Closed;
        _length = path.Length;
        var n = Math.Max(2, (int)Math.Ceiling(path.Length / spacing));
        _ds = path.Length / n;
        _v = new double[n + 1];
        var kappa = new double[n + 1];
        for (var i = 0; i <= n; i++)
        {
            var smp = path.Sample((float)(i * _ds));
            var k = (double)smp.Curvature;
            // a left lane offset shortens the radius of a left turn: kappa' = kappa / (1 - kappa * offset)
            var denom = 1 - (k * laneOffset);
            // capped at 1/8 m: sharper kinks are joins of spline pieces, not hairpins a car could follow (GUESSED)
            kappa[i] = Math.Min(MaxCurvature, Math.Abs(denom > 0.2 ? k / denom : k * 5));
            _v[i] = Math.Min(topSpeed, smp.SpeedLimit);
        }

        for (var i = 0; i <= n; i++)
        {
            // worst curvature within ±2 samples (the tyres need a moment to build the force)
            var k = 0.0;
            for (var j = -2; j <= 2; j++)
            {
                var idx = i + j;
                idx = _closed ? ((idx % n) + n) % n : Math.Clamp(idx, 0, n);
                k = Math.Max(k, kappa[idx]);
            }

            if (k > 1e-6)
            {
                _v[i] = Math.Min(_v[i], Math.Sqrt(lateralAccel / k));
            }
        }

        if (stopAtEnd && !_closed)
        {
            _v[n] = 0;
        }

        var passes = _closed ? 2 : 1;
        for (var p = 0; p < passes; p++)
        {
            for (var i = n - 1; i >= 0; i--)
            {
                var next = _closed && i == n - 1 ? _v[0] : _v[i + 1];
                _v[i] = Math.Min(_v[i], Math.Sqrt((next * next) + (2 * brakeDecel * _ds)));
            }

            if (_closed)
            {
                _v[n] = _v[0];
            }
        }
    }

    /// <summary>Planned speed at arc length <paramref name="s"/> (beyond an open path's end: its last value).</summary>
    public double At(double s)
    {
        if (_closed)
        {
            s %= _length;
            if (s < 0)
            {
                s += _length;
            }
        }

        var f = s / _ds;
        if (f <= 0)
        {
            return _v[0];
        }

        var i = (int)f;
        if (i >= _v.Length - 1)
        {
            return _v[^1];
        }

        var u = f - i;
        return _v[i] + ((_v[i + 1] - _v[i]) * u);
    }

    /// <summary>Lowest planned speed (report).</summary>
    public double Min => _v.Min();
}
