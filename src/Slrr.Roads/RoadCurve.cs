using System.Numerics;

namespace Slrr.Roads;

/// <summary>A point on a road path: position, unit tangent, signed horizontal curvature (1/m, positive = the path turns
/// towards +X when heading +Z, i.e. clockwise seen from above; GUESSED to be "right" in the game's frame), the distance along the
/// path, and the lane's speed value (m/s, 0 when unknown; GUESSED meaning, see <see cref="TrcLane.SpeedLimit"/>).</summary>
public readonly record struct PathSample(Vector3 Position, Vector3 Tangent, float Curvature, float Distance, float SpeedLimit, int LaneId);

/// <summary>Something an AI driver can follow: a lane, a connector, a route or a <c>.spl</c> spline.</summary>
public interface IRoadPath
{
    float Length { get; }

    /// <summary>Sample at distance <paramref name="s"/>, clamped to 0..<see cref="Length"/>.</summary>
    PathSample Sample(float s);

    /// <summary>Closest point in the horizontal plane: distance along the path and signed lateral offset (positive = +X side
    /// when heading +Z). False for an empty path.</summary>
    bool Project(Vector3 p, out float s, out float lateral);
}

/// <summary>
/// A chain of <see cref="HermitePiece"/>s parameterised by distance, as the exe's curve list (0x57f1a0 builds it; length =
/// sum of piece lengths at +0x38). <see cref="PointAt"/> / <see cref="TangentAt"/> follow 0x57e960 / 0x57eb00 (MEASURED:
/// negative distances wrap by the total length, the piece is found by subtracting piece lengths, u = s / pieceLength, the
/// tangent is normalised). <see cref="Project"/> follows 0x57ef20's structure (per piece closest point in x/z, else the piece
/// ends; the smallest |distance| wins) with our own per-piece search (the exe's 0x57d0a0 was not traced: GUESSED).
/// </summary>
public sealed class RoadCurve : IRoadPath
{
    private readonly HermitePiece[] _pieces;
    private readonly float[] _start;

    public RoadCurve(IReadOnlyList<HermitePiece> pieces, float speedLimit = 0, int laneId = 0)
    {
        _pieces = [.. pieces];
        _start = new float[_pieces.Length];
        var acc = 0f;
        for (var i = 0; i < _pieces.Length; i++)
        {
            _start[i] = acc;
            acc += _pieces[i].Length;
        }

        Length = acc;
        SpeedLimit = speedLimit;
        LaneId = laneId;
    }

    public IReadOnlyList<HermitePiece> Pieces => _pieces;

    public float Length { get; }

    public float SpeedLimit { get; }

    public int LaneId { get; }

    public Vector3 Start => _pieces.Length == 0 ? Vector3.Zero : _pieces[0].P0;

    public Vector3 End => _pieces.Length == 0 ? Vector3.Zero : _pieces[^1].P1;

    /// <summary>Axis-aligned bounds of the control points and a sampled outline (8 steps per piece).</summary>
    public (Vector3 Min, Vector3 Max) Bounds()
    {
        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        foreach (var p in _pieces)
        {
            for (var k = 0; k <= 8; k++)
            {
                var q = p.Point(k / 8f);
                min = Vector3.Min(min, q);
                max = Vector3.Max(max, q);
            }
        }

        return (min, max);
    }

    /// <summary>The exe's distance → (piece, u) mapping (0x57e960), including its wrap of negative distances and the
    /// extrapolation of the last piece past the end.</summary>
    public int Locate(float s, out float u)
    {
        if (_pieces.Length == 0)
        {
            u = 0;
            return -1;
        }

        if (s < 0 && Length != 0)
        {
            s = s - (MathF.Truncate(s / Length) * Length) + Length;
        }

        var i = 0;
        while (i < _pieces.Length - 1 && s >= _pieces[i].Length)
        {
            s -= _pieces[i].Length;
            i++;
        }

        var len = _pieces[i].Length;
        u = len != 0 ? s / len : 0;
        return i;
    }

    public Vector3 PointAt(float s)
    {
        var i = Locate(s, out var u);
        return i < 0 ? Vector3.Zero : _pieces[i].Point(u);
    }

    /// <summary>Unit tangent (zero vector for a degenerate derivative), as 0x57cda0 returns it.</summary>
    public Vector3 TangentAt(float s)
    {
        var i = Locate(s, out var u);
        if (i < 0)
        {
            return Vector3.Zero;
        }

        var d = _pieces[i].Derivative(u);
        var l = d.Length();
        return l == 0 ? Vector3.Zero : d / l;
    }

    /// <summary>Signed curvature of the horizontal projection (1/m).</summary>
    public float CurvatureAt(float s)
    {
        var i = Locate(s, out var u);
        if (i < 0)
        {
            return 0;
        }

        var d = _pieces[i].Derivative(u);
        var dd = _pieces[i].SecondDerivative(u);
        var sp = (d.X * d.X) + (d.Z * d.Z);
        if (sp < 1e-12f)
        {
            return 0;
        }

        // dz/du·d²x/du² − dx/du·d²z/du²: positive when heading +Z and bending towards +X
        return ((d.Z * dd.X) - (d.X * dd.Z)) / (sp * MathF.Sqrt(sp));
    }

    public PathSample Sample(float s)
    {
        s = Math.Clamp(s, 0, Length);
        return new PathSample(PointAt(s), TangentAt(s), CurvatureAt(s), s, SpeedLimit, LaneId);
    }

    /// <summary>Closest point to (x, z) in the horizontal plane (y ignored, like 0x57ef20): distance along the curve and the
    /// signed lateral offset (positive = +X side when heading +Z).</summary>
    public bool Project(float x, float z, out float s, out float lateral)
    {
        s = 0;
        lateral = 0;
        if (_pieces.Length == 0)
        {
            return false;
        }

        var best = float.MaxValue;
        for (var i = 0; i < _pieces.Length; i++)
        {
            var u = ClosestU(_pieces[i], x, z);
            var q = _pieces[i].Point(u);
            var dx = x - q.X;
            var dz = z - q.Z;
            var d2 = (dx * dx) + (dz * dz);
            if (d2 < best)
            {
                best = d2;
                s = _start[i] + (u * _pieces[i].Length);
                var t = _pieces[i].Derivative(u);
                var tl = MathF.Sqrt((t.X * t.X) + (t.Z * t.Z));
                var side = tl > 0 ? ((dx * t.Z) - (dz * t.X)) / tl : 0;
                lateral = side >= 0 ? MathF.Sqrt(d2) : -MathF.Sqrt(d2);
            }
        }

        return true;
    }

    public bool Project(Vector3 p, out float s, out float lateral) => Project(p.X, p.Z, out s, out lateral);

    /// <summary>u in [0,1] minimising the horizontal distance: 24 samples, then golden-section refinement around the best.</summary>
    internal static float ClosestU(in HermitePiece p, float x, float z)
    {
        const int N = 24;
        var bestU = 0f;
        var best = float.MaxValue;
        for (var k = 0; k <= N; k++)
        {
            var u = k / (float)N;
            var d = D2(p, u, x, z);
            if (d < best)
            {
                best = d;
                bestU = u;
            }
        }

        var lo = MathF.Max(0, bestU - (1f / N));
        var hi = MathF.Min(1, bestU + (1f / N));
        const float G = 0.618034f;
        var a = hi - (G * (hi - lo));
        var b = lo + (G * (hi - lo));
        var fa = D2(p, a, x, z);
        var fb = D2(p, b, x, z);
        for (var it = 0; it < 40 && hi - lo > 1e-6f; it++)
        {
            if (fa < fb)
            {
                hi = b;
                b = a;
                fb = fa;
                a = hi - (G * (hi - lo));
                fa = D2(p, a, x, z);
            }
            else
            {
                lo = a;
                a = b;
                fa = fb;
                b = lo + (G * (hi - lo));
                fb = D2(p, b, x, z);
            }
        }

        var m = (lo + hi) / 2;
        return D2(p, m, x, z) < best ? m : bestU;
    }

    private static float D2(in HermitePiece p, float u, float x, float z)
    {
        var q = p.Point(u);
        var dx = x - q.X;
        var dz = z - q.Z;
        return (dx * dx) + (dz * dz);
    }
}
