using System.Numerics;

namespace Slrr.AI.Roads;

/// <summary>
/// A graphless <see cref="IRoadSource"/> over a fixed set of paths: race routes (loops, shipped <c>.spl</c> splines) and
/// traffic lanes with optional successor links. Used for synthetic test roads and until the Roads area's map graph exists.
/// <see cref="Route"/> only routes along a single path (the one both points are closest to), in its direction.
/// </summary>
public sealed class PathSetRoadSource : IRoadSource
{
    private readonly List<IRoadPath> _routes = [];
    private readonly List<IRoadPath> _lanes = [];
    private readonly Dictionary<IRoadPath, List<IRoadPath>> _next = new(ReferenceEqualityComparer.Instance);

    public IReadOnlyList<IRoadPath> Routes => _routes;

    public IReadOnlyList<IRoadPath> Lanes => _lanes;

    /// <summary>Maximum distance of the route end points from a path for <see cref="Route"/> (metres).</summary>
    public float RouteSnap { get; set; } = 40f;

    /// <summary>Checkpoint spacing for <see cref="IRoadSource.Checkpoints"/> (GUESSED).</summary>
    public float CheckpointSpacing { get; set; } = 150f;

    public PathSetRoadSource AddRoute(IRoadPath path)
    {
        _routes.Add(path);
        return this;
    }

    public PathSetRoadSource AddLane(IRoadPath lane, params IRoadPath[] next)
    {
        _lanes.Add(lane);
        if (next.Length > 0)
        {
            _next[lane] = [.. next];
        }

        return this;
    }

    public void Link(IRoadPath from, IRoadPath to)
    {
        if (!_next.TryGetValue(from, out var l))
        {
            _next[from] = l = [];
        }

        l.Add(to);
    }

    public IReadOnlyList<IRoadPath> Next(IRoadPath lane) => _next.TryGetValue(lane, out var l) ? l : [];

    public RoadPoint? Nearest(Vector3 p, float maxDistance)
    {
        RoadPoint? best = null;
        foreach (var path in _routes.Concat(_lanes))
        {
            var rp = Measure(path, p, path.Project(p));
            if (rp.Distance <= maxDistance && (best is null || rp.Distance < best.Value.Distance))
            {
                best = rp;
            }
        }

        return best;
    }

    public static RoadPoint Measure(IRoadPath path, Vector3 p, float s)
    {
        var smp = path.Sample(s);
        var d = p - smp.Position;
        var lat = Vector3.Dot(d, RoadMath.LeftOf(smp.Tangent));
        return new RoadPoint(path, smp, RoadMath.Horizontal(d), lat);
    }

    public IRoadPath? Route(Vector3 from, Vector3 to)
    {
        IRoadPath? best = null;
        float bestScore = float.MaxValue, s0 = 0, s1 = 0;
        foreach (var path in _routes.Concat(_lanes))
        {
            var a = Measure(path, from, path.Project(from));
            var b = Measure(path, to, path.Project(to));
            if (a.Distance > RouteSnap || b.Distance > RouteSnap)
            {
                continue;
            }

            var score = a.Distance + b.Distance;
            if (score < bestScore)
            {
                (best, bestScore, s0, s1) = (path, score, a.Sample.S, b.Sample.S);
            }
        }

        if (best is null)
        {
            return null;
        }

        var len = s1 - s0;
        if (best.Closed && len <= 0)
        {
            len += best.Length;
        }

        return len <= 0 ? null : new SubPath(best, s0, len);
    }

    public IReadOnlyList<Checkpoint> Checkpoints(IRoadPath route) => RaceLayout.Checkpoints(route, CheckpointSpacing);
}
