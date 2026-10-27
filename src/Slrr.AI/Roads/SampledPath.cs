using System.Numerics;

namespace Slrr.AI.Roads;

/// <summary>A control point of a <see cref="SampledPath"/>.</summary>
/// <param name="Tangent">Direction at the point (Hermite); null = Catmull-Rom tangent from the neighbours.</param>
/// <param name="HalfWidth">Drivable half width from this point to the next.</param>
/// <param name="SpeedLimit">Speed limit (m/s) from this point to the next (<see cref="float.PositiveInfinity"/> = none).</param>
public readonly record struct PathControlPoint(Vector3 Position, Vector3? Tangent = null, float HalfWidth = 5f, float SpeedLimit = float.PositiveInfinity);

/// <summary>
/// A centreline built from control points (cubic Hermite / Catmull-Rom, GUESSED interpolation: the exe's spline segment
/// routine 0x57de20 takes two (position, direction) pairs per segment, the tangent scale is not traced; chord length is
/// used here), resampled to a uniform arc-length table so <see cref="Sample"/> is O(1). Deterministic.
/// </summary>
public sealed class SampledPath : IRoadPath
{
    private readonly Vector3[] _pos;
    private readonly Vector3[] _tan;
    private readonly float[] _curv;
    private readonly float[] _half;
    private readonly float[] _limit;
    private readonly float _ds;
    private readonly int _n; // number of intervals

    public SampledPath(IReadOnlyList<PathControlPoint> points, bool closed, string name = "", float spacing = 1f, int subdivisions = 16, float tangentScale = 1f)
    {
        if (points.Count < 2)
        {
            throw new ArgumentException("a path needs at least two points", nameof(points));
        }

        Name = name;
        Closed = closed;
        var segs = closed ? points.Count : points.Count - 1;

        // ---- dense polyline
        var dense = new List<(Vector3 P, float Half, float Limit)>(segs * subdivisions + 1);
        for (var i = 0; i < segs; i++)
        {
            var a = points[i];
            var b = points[(i + 1) % points.Count];
            var chord = Vector3.Distance(a.Position, b.Position);
            var ta = TangentAt(points, i, closed) * chord * tangentScale;
            var tb = TangentAt(points, (i + 1) % points.Count, closed) * chord * tangentScale;
            for (var k = 0; k < subdivisions; k++)
            {
                var u = k / (float)subdivisions;
                dense.Add((Hermite(a.Position, ta, b.Position, tb, u), a.HalfWidth, a.SpeedLimit));
            }
        }

        var last = closed ? points[0] : points[^1];
        dense.Add((last.Position, closed ? points[^1].HalfWidth : last.HalfWidth, closed ? points[^1].SpeedLimit : last.SpeedLimit));

        var cum = new double[dense.Count];
        for (var i = 1; i < dense.Count; i++)
        {
            cum[i] = cum[i - 1] + Vector3.Distance(dense[i - 1].P, dense[i].P);
        }

        var length = cum[^1];
        _n = Math.Max(2, (int)Math.Round(length / spacing));
        _ds = (float)(length / _n);
        Length = (float)length;

        // ---- uniform resample
        var count = _n + 1;
        _pos = new Vector3[count];
        _half = new float[count];
        _limit = new float[count];
        var j = 0;
        for (var i = 0; i < count; i++)
        {
            var s = Math.Min(length, i * (double)_ds);
            while (j < dense.Count - 2 && cum[j + 1] < s)
            {
                j++;
            }

            var span = cum[j + 1] - cum[j];
            var u = span > 1e-9 ? (float)((s - cum[j]) / span) : 0f;
            _pos[i] = Vector3.Lerp(dense[j].P, dense[j + 1].P, u);
            _half[i] = dense[j].Half;
            _limit[i] = dense[j].Limit;
        }

        if (closed)
        {
            _pos[_n] = _pos[0];
        }

        // ---- tangents (central differences) and signed curvature
        _tan = new Vector3[count];
        for (var i = 0; i < count; i++)
        {
            var prev = Index(i - 1);
            var next = Index(i + 1);
            var d = _pos[next] - _pos[prev];
            _tan[i] = d.LengthSquared() > 1e-12f ? Vector3.Normalize(d) : (i > 0 ? _tan[i - 1] : new Vector3(0, 0, -1));
        }

        var raw = new float[count];
        for (var i = 0; i < count; i++)
        {
            var t0 = _tan[Index(i - 1)];
            var t1 = _tan[Index(i + 1)];
            var span = (closed || (i > 0 && i < _n) ? 2 : 1) * _ds;
            var ang = MathF.Atan2(Vector3.Dot(RoadMath.LeftOf(t0), t1), (t0.X * t1.X) + (t0.Z * t1.Z));
            raw[i] = ang / span;
        }

        _curv = new float[count];
        const int w = 2;
        for (var i = 0; i < count; i++)
        {
            float sum = 0;
            var m = 0;
            for (var k = -w; k <= w; k++)
            {
                var idx = i + k;
                if (!closed && (idx < 0 || idx > _n))
                {
                    continue;
                }

                sum += raw[Index(idx)];
                m++;
            }

            _curv[i] = sum / m;
        }
    }

    public float Length { get; }

    public bool Closed { get; }

    public string Name { get; }

    /// <summary>Sample spacing (metres).</summary>
    public float Spacing => _ds;

    /// <summary>Polyline through <paramref name="points"/> with a constant half width (Catmull-Rom).</summary>
    public static SampledPath Through(IEnumerable<Vector3> points, bool closed, float halfWidth = 5f, string name = "") =>
        new(points.Select(p => new PathControlPoint(p, null, halfWidth)).ToList(), closed, name);

    private int Index(int i)
    {
        if (Closed)
        {
            i %= _n;
            return i < 0 ? i + _n : i;
        }

        return Math.Clamp(i, 0, _n);
    }

    public PathSample Sample(float s)
    {
        if (Closed)
        {
            s %= Length;
            if (s < 0)
            {
                s += Length;
            }
        }
        else if (s < 0)
        {
            var a = Sample(0);
            return a with { Position = a.Position + (a.Tangent * s), S = s };
        }
        else if (s > Length)
        {
            var b = SampleAt(_n, 0f, Length);
            return b with { Position = b.Position + (b.Tangent * (s - Length)), S = s };
        }

        var f = s / _ds;
        var i = Math.Min((int)f, _n - 1);
        return SampleAt(i, f - i, s);
    }

    private PathSample SampleAt(int i, float u, float s)
    {
        var k = Math.Min(i + 1, _n);
        var t = Vector3.Lerp(_tan[i], _tan[k], u);
        t = t.LengthSquared() > 1e-12f ? Vector3.Normalize(t) : _tan[i];
        return new PathSample(Vector3.Lerp(_pos[i], _pos[k], u), t, _curv[i] + ((_curv[k] - _curv[i]) * u), _half[i], _limit[i], s);
    }

    public float Project(Vector3 p, float hint = float.NaN, float window = float.PositiveInfinity)
    {
        int lo, hi;
        if (float.IsNaN(hint) || float.IsInfinity(window) || window * 2 >= Length)
        {
            lo = 0;
            hi = _n - 1;
        }
        else
        {
            lo = (int)MathF.Floor((hint - window) / _ds);
            hi = (int)MathF.Ceiling((hint + window) / _ds);
            if (!Closed)
            {
                lo = Math.Clamp(lo, 0, _n - 1);
                hi = Math.Clamp(hi, 0, _n - 1);
            }
        }

        var best = float.MaxValue;
        var bestS = 0f;
        for (var r = lo; r <= hi; r++)
        {
            var i = Index(r);
            if (i >= _n)
            {
                continue;
            }

            var a = _pos[i];
            var b = _pos[i + 1];
            float abx = b.X - a.X, abz = b.Z - a.Z;
            float apx = p.X - a.X, apz = p.Z - a.Z;
            var len2 = (abx * abx) + (abz * abz);
            var u = len2 > 1e-12f ? Math.Clamp(((apx * abx) + (apz * abz)) / len2, 0f, 1f) : 0f;
            float dx = apx - (abx * u), dz = apz - (abz * u);
            var d2 = (dx * dx) + (dz * dz);
            if (d2 < best)
            {
                best = d2;
                bestS = (i + u) * _ds;
            }
        }

        return bestS;
    }

    private static Vector3 TangentAt(IReadOnlyList<PathControlPoint> pts, int i, bool closed)
    {
        if (pts[i].Tangent is { } t && t.LengthSquared() > 1e-12f)
        {
            return Vector3.Normalize(t);
        }

        var n = pts.Count;
        var prev = closed ? pts[(i - 1 + n) % n].Position : pts[Math.Max(0, i - 1)].Position;
        var next = closed ? pts[(i + 1) % n].Position : pts[Math.Min(n - 1, i + 1)].Position;
        var d = next - prev;
        return d.LengthSquared() > 1e-12f ? Vector3.Normalize(d) : new Vector3(0, 0, -1);
    }

    private static Vector3 Hermite(Vector3 p0, Vector3 m0, Vector3 p1, Vector3 m1, float u)
    {
        var u2 = u * u;
        var u3 = u2 * u;
        return (p0 * ((2 * u3) - (3 * u2) + 1)) + (m0 * (u3 - (2 * u2) + u)) + (p1 * ((-2 * u3) + (3 * u2))) + (m1 * (u3 - u2));
    }
}

/// <summary>A view of part of another path: from <see cref="Start"/> for <see cref="Length"/> metres (wraps on closed parents).</summary>
public sealed class SubPath(IRoadPath parent, float start, float length, string? name = null) : IRoadPath
{
    public IRoadPath Parent => parent;

    public float Start => start;

    public float Length => length;

    public bool Closed => false;

    public string Name => name ?? $"{parent.Name}[{start:F0}+{length:F0}]";

    public PathSample Sample(float s)
    {
        if (s < 0 || s > length)
        {
            var end = s < 0 ? 0f : length;
            var e = parent.Sample(start + end);
            return e with { Position = e.Position + (e.Tangent * (s - end)), S = s };
        }

        return parent.Sample(start + s) with { S = s };
    }

    public float Project(Vector3 p, float hint = float.NaN, float window = float.PositiveInfinity)
    {
        var mid = float.IsNaN(hint) ? length / 2 : hint;
        var win = float.IsNaN(hint) ? (length / 2) + 1 : window;
        var s = parent.Project(p, start + mid, Math.Min(win, (length / 2) + 1));
        var rel = s - start;
        if (parent.Closed)
        {
            rel %= parent.Length;
            if (rel < 0)
            {
                rel += parent.Length;
            }

            if (rel > length + ((parent.Length - length) / 2))
            {
                rel -= parent.Length;
            }
        }

        return Math.Clamp(rel, 0, length);
    }
}
