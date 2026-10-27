using System.Numerics;

namespace Slrr.Roads;

/// <summary>A crossing of the road graph.</summary>
public sealed class RoadNode
{
    internal RoadNode(TrcNode n)
    {
        Source = n;
    }

    public TrcNode Source { get; }

    public int Id => Source.Id;

    public Vector3 Position => Source.Position;

    /// <summary>Turn paths through this crossing (curve + the lanes they join).</summary>
    public List<RoadConnector> Connectors { get; } = [];

    /// <summary>Largest distance from <see cref="Position"/> to a connector end (GUESSED stand-in for the exe's node field
    /// +0x34 that 0x583840 compares with 0.7 × distance; that field is not in the file).</summary>
    public float Radius { get; internal set; }
}

/// <summary>A turn path from the end of <see cref="From"/> to the start of <see cref="To"/> through <see cref="Node"/>.</summary>
public sealed record RoadConnector(RoadNode Node, Lane From, Lane To, RoadCurve Curve, TrcConnector Source);

/// <summary>A road: reference curve, its crossings and lanes.</summary>
public sealed class Road
{
    internal Road(TrcRoad r, RoadNode? start, RoadNode? end)
    {
        Source = r;
        StartNode = start;
        EndNode = end;
        Curve = new RoadCurve(r.Pieces);
    }

    public TrcRoad Source { get; }

    public int Id => Source.Id;

    public RoadNode? StartNode { get; }

    public RoadNode? EndNode { get; }

    /// <summary>The road's reference curve (what <c>alignToRoad</c> snaps to, MEASURED).</summary>
    public RoadCurve Curve { get; }

    public List<Lane> Lanes { get; } = [];
}

/// <summary>A driving lane: centreline curve in its driving direction, speed value, neighbours, crossings at both ends.</summary>
public sealed class Lane
{
    internal Lane(TrcLane l)
    {
        Source = l;
        Curve = new RoadCurve(l.Pieces, l.SpeedLimit, l.Id);
    }

    public TrcLane Source { get; }

    public int Id => Source.Id;

    public Road Road { get; internal set; } = null!;

    public RoadCurve Curve { get; }

    public float SpeedLimit => Source.SpeedLimit;

    /// <summary>True when the lane runs against its road's curve direction.</summary>
    public bool Reverse => Source.Reverse != 0;

    public RoadNode? StartNode { get; internal set; }

    public RoadNode? EndNode { get; internal set; }

    public Lane? Left { get; internal set; }

    public Lane? Right { get; internal set; }

    /// <summary>Turn paths leaving the end of this lane.</summary>
    public List<RoadConnector> Exits { get; } = [];
}

/// <summary>A lane hit by <see cref="IRoadGraph.NearestLane"/>.</summary>
public readonly record struct LaneHit(Lane Lane, float Distance, float Lateral, Vector3 Position, float Range);

/// <summary>
/// Road-graph queries for the map natives (<c>GroundRef.alignToRoad / getNearestCross / getStartDirection / getRouteLength /
/// findRoute</c>) and AI drivers. Which behaviour is MEASURED (exe disassembly) and which is GUESSED is stated per member.
/// </summary>
public interface IRoadGraph
{
    IReadOnlyList<RoadNode> Nodes { get; }

    IReadOnlyList<Road> Roads { get; }

    IReadOnlyList<Lane> Lanes { get; }

    Lane? LaneById(int id);

    /// <summary>Nearest lane to <paramref name="p"/>: lanes whose x range is within 20 m of p.x, the closest curve point in x/z,
    /// dropped when its height differs from p.y by 5 m or more, smallest 3-D distance wins. MEASURED (0x581bf0, constants
    /// 20.0 at 0x5f0c4c and 5.0 at 0x5f45c8); the per-piece closest-point search is ours.</summary>
    LaneHit? NearestLane(Vector3 p);

    /// <summary><c>alignToRoad</c>: the nearest lane's ROAD curve point closest to p (x/z), and the road's unit tangent there,
    /// reversed when the point lies past half the road's length (so it points to the farther end). MEASURED (0x583a00).</summary>
    (Vector3 Position, Vector3 Direction)? AlignToRoad(Vector3 p);

    /// <summary><c>getNearestCross(p, d)</c>: the crossing whose 3-D distance to p is closest to d (d = 0: nearest crossing);
    /// with <paramref name="sameLevel"/> only crossings within 5 m of p.y. MEASURED (0x581a00; the native passes false).</summary>
    RoadNode? NearestCross(Vector3 p, float distance, bool sameLevel = false);

    /// <summary>Shortest drivable lane route from the road nearest <paramref name="from"/> to the road nearest
    /// <paramref name="to"/> (lanes + turn paths, length = cost). Every lane of the start road may be the first one (either
    /// direction of a two-way road) unless <paramref name="heading"/> is given: then only lanes running within 90° of it.
    /// GUESSED: the exe's route builder (0x5826f0) works on road curves and crossings and was not traced.</summary>
    RoadRoute? FindRoute(Vector3 from, Vector3 to, Vector3? heading = null);

    /// <summary><c>getStartDirection(a, b)</c>: unit route direction at a towards b, null without a route. The use of the route
    /// tangent at a is MEASURED (0x583840); the route is <see cref="FindRoute"/>'s (GUESSED).</summary>
    Vector3? StartDirection(Vector3 from, Vector3 to);

    /// <summary><c>getRouteLength(a, b)</c>: length of <see cref="FindRoute"/>, -1 without a route (GUESSED value).</summary>
    float RouteLength(Vector3 from, Vector3 to);

    /// <summary>Centreline sample of a lane at distance s (clamped).</summary>
    PathSample SampleLane(int laneId, float s);
}

/// <summary>The road graph of a <see cref="TrcFile"/>.</summary>
public sealed class RoadGraph : IRoadGraph
{
    /// <summary>x window of the nearest-lane search (MEASURED 20.0 at 0x5f0c4c).</summary>
    public const float LaneWindow = 20f;

    /// <summary>Height tolerance (MEASURED 5.0 at 0x5f45c8).</summary>
    public const float LevelTolerance = 5f;

    private readonly RoadNode[] _nodes;
    private readonly Road[] _roads;
    private readonly Lane[] _lanes;
    private readonly int _firstLane;
    private readonly (float XMin, float XMax, Lane Lane, float ZMin, float ZMax)[] _sorted;
    private readonly float[] _runMax;

    public RoadGraph(TrcFile file, string? name = null)
    {
        File = file;
        Name = name ?? "trc";
        _nodes = file.Nodes.Select(n => new RoadNode(n)).ToArray();
        RoadNode? NodeOf(int id) => id >= 1 && id <= _nodes.Length ? _nodes[id - 1] : null;
        _roads = file.Roads.Select(r => new Road(r, NodeOf(r.StartNode), NodeOf(r.EndNode))).ToArray();
        _lanes = file.Lanes.Select(l => new Lane(l)).ToArray();
        _firstLane = file.FirstLaneId;
        foreach (var l in _lanes)
        {
            if (file.Road(l.Source.RoadId) is { } r)
            {
                l.Road = _roads[r.Id - file.FirstRoadId];
                l.Road.Lanes.Add(l);
            }
            else
            {
                throw new Slrr.Core.SlrrFormatException($"trc: lane {l.Id} names road {l.Source.RoadId}");
            }

            l.Left = LaneById(l.Source.LeftLaneId);
            l.Right = LaneById(l.Source.RightLaneId);
        }

        foreach (var n in _nodes)
        {
            foreach (var link in n.Source.Links)
            {
                if (LaneById(link.LaneId) is { } lane)
                {
                    if (link.Starts)
                    {
                        lane.StartNode = n;
                    }
                    else
                    {
                        lane.EndNode = n;
                    }
                }
            }

            foreach (var c in n.Source.Connectors)
            {
                if (c.OutLink >= n.Source.Links.Count || c.InLink >= n.Source.Links.Count)
                {
                    continue;
                }

                var from = LaneById(n.Source.Links[c.InLink].LaneId);
                var to = LaneById(n.Source.Links[c.OutLink].LaneId);
                if (from is null || to is null)
                {
                    continue;
                }

                var rc = new RoadConnector(n, from, to, new RoadCurve([c.Curve], MathF.Min(from.SpeedLimit, to.SpeedLimit)), c);
                n.Connectors.Add(rc);
                from.Exits.Add(rc);
                n.Radius = MathF.Max(n.Radius, MathF.Max(Vector3.Distance(n.Position, c.Curve.P0), Vector3.Distance(n.Position, c.Curve.P1)));
            }
        }

        // x-sorted lane index rebuilt from the geometry (the file's table has the same purpose; see TrcSortEntry)
        _sorted = _lanes.Select(l => { var (mn, mx) = l.Curve.Bounds(); return (mn.X, mx.X, l, mn.Z, mx.Z); }).OrderBy(t => t.Item1).ToArray();
        _runMax = new float[_sorted.Length];
        var run = float.MinValue;
        for (var i = 0; i < _sorted.Length; i++)
        {
            run = MathF.Max(run, _sorted[i].XMax);
            _runMax[i] = run;
        }
    }

    public TrcFile File { get; }

    public string Name { get; }

    public IReadOnlyList<RoadNode> Nodes => _nodes;

    public IReadOnlyList<Road> Roads => _roads;

    public IReadOnlyList<Lane> Lanes => _lanes;

    public Lane? LaneById(int id) => id >= _firstLane && id < _firstLane + _lanes.Length ? _lanes[id - _firstLane] : null;

    public LaneHit? NearestLane(Vector3 p) => NearestLane(p, null);

    private LaneHit? NearestLane(Vector3 p, Func<Lane, bool>? filter)
    {
        // last index whose xmin < p.x + window, then walk down while the running max still reaches p.x - window
        int lo = 0, hi = _sorted.Length - 1, idx = -1;
        while (lo <= hi)
        {
            var mid = (lo + hi) >> 1;
            if (_sorted[mid].XMin < p.X + LaneWindow)
            {
                idx = mid;
                lo = mid + 1;
            }
            else
            {
                hi = mid - 1;
            }
        }

        LaneHit? best = null;
        var bestD2 = float.MaxValue;
        for (var i = idx; i >= 0 && _runMax[i] > p.X - LaneWindow; i--)
        {
            var (xmin, xmax, lane, zmin, zmax) = _sorted[i];
            if (xmax <= p.X - LaneWindow || (filter is not null && !filter(lane)))
            {
                continue;
            }

            // a lane whose (sampled, so slightly loose: 1 m slack) x/z box is farther than the best hit cannot win
            var bx = MathF.Max(0, MathF.Max(xmin - p.X, p.X - xmax) - 1);
            var bz = MathF.Max(0, MathF.Max(zmin - p.Z, p.Z - zmax) - 1);
            if ((bx * bx) + (bz * bz) >= bestD2 || !lane.Curve.Project(p.X, p.Z, out var s, out var lat))
            {
                continue;
            }

            var q = lane.Curve.PointAt(s);
            if (MathF.Abs(q.Y - p.Y) >= LevelTolerance)
            {
                continue;
            }

            var d2 = Vector3.DistanceSquared(q, p);
            if (d2 < bestD2)
            {
                bestD2 = d2;
                best = new LaneHit(lane, s, lat, q, MathF.Sqrt(d2));
            }
        }

        return best;
    }

    public (Vector3 Position, Vector3 Direction)? AlignToRoad(Vector3 p)
    {
        if (NearestLane(p) is not { } hit)
        {
            return null;
        }

        var road = hit.Lane.Road.Curve;
        road.Project(p.X, p.Z, out var s, out _);
        var t = road.TangentAt(s);
        if (road.Length * 0.5f <= s)
        {
            t = -t;
        }

        return (road.PointAt(s), t);
    }

    public RoadNode? NearestCross(Vector3 p, float distance, bool sameLevel = false)
    {
        RoadNode? best = null;
        var bestErr = 0f;
        foreach (var n in _nodes)
        {
            if (sameLevel && MathF.Abs(n.Position.Y - p.Y) >= LevelTolerance)
            {
                continue;
            }

            var err = MathF.Abs(Vector3.Distance(n.Position, p) - distance);
            if (best is null || err < bestErr)
            {
                best = n;
                bestErr = err;
            }
        }

        return best;
    }

    public Vector3? StartDirection(Vector3 from, Vector3 to)
    {
        var r = FindRoute(from, to);
        if (r is null || r.Length <= 0)
        {
            return null;
        }

        var t = r.Sample(0).Tangent;
        return t == Vector3.Zero ? null : t;
    }

    public float RouteLength(Vector3 from, Vector3 to) => FindRoute(from, to)?.Length ?? -1f;

    public PathSample SampleLane(int laneId, float s) =>
        LaneById(laneId) is { } l ? l.Curve.Sample(s) : throw new ArgumentOutOfRangeException(nameof(laneId), laneId, "no such lane");

    public RoadRoute? FindRoute(Vector3 from, Vector3 to, Vector3? heading = null)
    {
        if (NearestLane(from) is not { } a || NearestLane(to) is not { } b)
        {
            return null;
        }

        // every lane of the start road / goal road is a candidate (the nearest lane may run the wrong way)
        var starts = a.Lane.Road.Lanes.Select(l => (Lane: l, S: Proj(l, from))).ToList();
        if (heading is { } h && starts.Where(x => Vector3.Dot(x.Lane.Curve.TangentAt(x.S), h) > 0).ToList() is { Count: > 0 } along)
        {
            starts = along;
        }

        var goals = b.Lane.Road.Lanes.ToDictionary(l => l.Id, l => Proj(l, to));

        var n = _lanes.Length;
        var endCost = new float[n];
        var arrive = new float[n];
        var pred = new RoadConnector?[n];
        var fromStart = new bool[n];
        Array.Fill(endCost, float.PositiveInfinity);
        Array.Fill(arrive, float.PositiveInfinity);
        var queue = new PriorityQueue<int, float>();
        RoadRoute? bestRoute = null;
        var bestLen = float.PositiveInfinity;
        foreach (var (lane, s) in starts)
        {
            var i = lane.Id - _firstLane;
            var c = lane.Curve.Length - s;
            if (c < endCost[i])
            {
                endCost[i] = c;
                fromStart[i] = true;
                queue.Enqueue(i, c);
            }

            if (goals.TryGetValue(lane.Id, out var g) && g >= s && g - s < bestLen)
            {
                bestLen = g - s;
                bestRoute = new RoadRoute([new RouteLeg(lane.Curve, s, g, lane)]);
            }
        }

        var startS = starts.ToDictionary(x => x.Lane.Id, x => x.S);
        while (queue.TryDequeue(out var i, out var cost))
        {
            if (cost > endCost[i] || cost >= bestLen)
            {
                continue;
            }

            foreach (var ex in _lanes[i].Exits)
            {
                var j = ex.To.Id - _firstLane;
                var at = cost + ex.Curve.Length;
                if (at >= arrive[j])
                {
                    continue;
                }

                arrive[j] = at;
                pred[j] = ex;
                if (goals.TryGetValue(ex.To.Id, out var g) && at + g < bestLen)
                {
                    bestLen = at + g;
                    bestRoute = Build(ex.To, g, pred, fromStart, startS);
                }

                var ec = at + ex.To.Curve.Length;
                if (ec < endCost[j])
                {
                    endCost[j] = ec;
                    fromStart[j] = false;
                    queue.Enqueue(j, ec);
                }
            }
        }

        return bestRoute;
    }

    private RoadRoute? Build(Lane goal, float goalS, RoadConnector?[] pred, bool[] fromStart, Dictionary<int, float> startS)
    {
        var legs = new List<RouteLeg> { new(goal.Curve, 0, goalS, goal) };
        var cur = goal;
        var guard = 0;
        while (pred[cur.Id - _firstLane] is { } c && guard++ < _lanes.Length)
        {
            legs.Add(new RouteLeg(c.Curve, 0, c.Curve.Length, null));
            var prev = c.From;
            if (fromStart[prev.Id - _firstLane] && startS.TryGetValue(prev.Id, out var s0))
            {
                legs.Add(new RouteLeg(prev.Curve, s0, prev.Curve.Length, prev));
                legs.Reverse();
                return new RoadRoute(legs);
            }

            legs.Add(new RouteLeg(prev.Curve, 0, prev.Curve.Length, prev));
            cur = prev;
        }

        return null;
    }

    private static float Proj(Lane l, Vector3 p)
    {
        l.Curve.Project(p.X, p.Z, out var s, out _);
        return s;
    }
}

/// <summary>Part of a route: [S0, S1] of a curve (a lane when <see cref="Lane"/> is set, else a turn path).</summary>
public readonly record struct RouteLeg(RoadCurve Curve, float S0, float S1, Lane? Lane)
{
    public float Length => S1 - S0;
}

/// <summary>A route through the lane graph, sampled by distance from its start.</summary>
public sealed class RoadRoute : IRoadPath
{
    private readonly float[] _start;

    public RoadRoute(IReadOnlyList<RouteLeg> legs)
    {
        Legs = legs;
        _start = new float[legs.Count];
        var acc = 0f;
        for (var i = 0; i < legs.Count; i++)
        {
            _start[i] = acc;
            acc += legs[i].Length;
        }

        Length = acc;
    }

    public IReadOnlyList<RouteLeg> Legs { get; }

    public float Length { get; }

    public IEnumerable<Lane> LanesOnRoute => Legs.Where(l => l.Lane is not null).Select(l => l.Lane!);

    public PathSample Sample(float s)
    {
        s = Math.Clamp(s, 0, Length);
        var i = Array.BinarySearch(_start, s);
        if (i < 0)
        {
            i = ~i - 1;
        }

        i = Math.Clamp(i, 0, Legs.Count - 1);
        var leg = Legs[i];
        var cs = leg.S0 + (s - _start[i]);
        var p = leg.Curve.Sample(cs);
        return p with { Distance = s };
    }

    public bool Project(Vector3 p, out float s, out float lateral)
    {
        s = 0;
        lateral = 0;
        var best = float.MaxValue;
        var found = false;
        for (var i = 0; i < Legs.Count; i++)
        {
            var leg = Legs[i];
            if (!leg.Curve.Project(p.X, p.Z, out var cs, out var lat))
            {
                continue;
            }

            cs = Math.Clamp(cs, leg.S0, leg.S1);
            var q = leg.Curve.PointAt(cs);
            var d = ((q.X - p.X) * (q.X - p.X)) + ((q.Z - p.Z) * (q.Z - p.Z));
            if (d < best)
            {
                best = d;
                s = _start[i] + (cs - leg.S0);
                lateral = lat;
                found = true;
            }
        }

        return found;
    }
}
