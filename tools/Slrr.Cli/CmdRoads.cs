using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using Slrr.Formats.Rpak;
using Slrr.Roads;
using Slrr.World;

namespace Slrr.Cli;

/// <summary>
/// <c>roads</c>: the binary AI road graph (<c>.trc</c>) and <c>.spl</c> race lines (src/Slrr.Roads, docs/gaps/roads.md).
/// A source is a <c>.trc</c> file or a map archive whose root names one (<c>maps/city.rpk</c>); <c>--root</c> = game directory.
/// </summary>
public static class CmdRoads
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public const string Usage =
        "roads stats|verify <src> [--root R]\n" +
        "roads dump <src> [nodes|roads|lanes|connectors] [--limit N]\n" +
        "roads map <src> <out.png|out.svg> [--cell 3] [--height] [--route x1 y1 z1 x2 y2 z2] [--spl file.spl]... [--bounds x0 z0 x1 z1]\n" +
        "roads route <src> x1 y1 z1 x2 y2 z2 [--samples N]\n" +
        "roads align <src> x y z\n" +
        "roads cross <src> x y z [distance]\n" +
        "roads spl <file.spl>\n" +
        "roads bench <src> [N]";

    public static int Run(string[] args, TextWriter o, TextWriter e)
    {
        if (args.Length < 2)
        {
            e.WriteLine(Usage);
            return 2;
        }

        var root = Opt(args, "--root") ?? ".";
        try
        {
            switch (args[0])
            {
                case "spl":
                    return Spl(args[1], o);
                case "verify":
                    return Verify(Src(args[1], root, e), o);
            }

            var g = Graph(args[1], root, e);
            if (g is null)
            {
                return 1;
            }

            var pos = args.Skip(2).TakeWhile(a => !a.StartsWith("--", StringComparison.Ordinal)).ToArray();
            switch (args[0])
            {
                case "stats":
                    return Stats(g, o);
                case "dump":
                    return Dump(g, pos.FirstOrDefault() ?? "nodes", int.Parse(Opt(args, "--limit") ?? "20", Inv), o);
                case "map":
                    return Map(g, args, pos, root, o, e);
                case "route":
                    return Route(g, Vec(pos, 0), Vec(pos, 3), int.Parse(Opt(args, "--samples") ?? "8", Inv), o);
                case "align":
                    return Align(g, Vec(pos, 0), o);
                case "bench":
                    return Bench(g, pos.Length > 0 ? int.Parse(pos[0], Inv) : 20000, o);
                case "cross":
                    var n = g.NearestCross(Vec(pos, 0), pos.Length > 3 ? F(pos[3]) : 0);
                    o.WriteLine(n is null ? "no crossing" : $"node {n.Id} table {(n.Source.Table == 0 ? 'A' : 'B')} at {V(n.Position)}, distance {Vector3.Distance(n.Position, Vec(pos, 0)):0.00}");
                    return 0;
                default:
                    e.WriteLine(Usage);
                    return 2;
            }
        }
        catch (Exception ex) when (ex is IOException or FormatException or Slrr.Core.SlrrFormatException or IndexOutOfRangeException or ArgumentException)
        {
            e.WriteLine($"roads: {ex.Message}");
            return 1;
        }
    }

    private static string? Opt(string[] a, string name)
    {
        var i = Array.IndexOf(a, name);
        return i >= 0 && i + 1 < a.Length ? a[i + 1] : null;
    }

    private static float F(string s) => float.Parse(s, NumberStyles.Float, Inv);

    private static Vector3 Vec(string[] p, int i) => new(F(p[i]), F(p[i + 1]), F(p[i + 2]));

    private static string V(Vector3 v) => string.Create(Inv, $"({v.X:0.00}, {v.Y:0.00}, {v.Z:0.00})");

    /// <summary>The .trc path of a source (a .trc file or a map archive naming one).</summary>
    public static string? Src(string src, string root, TextWriter e)
    {
        if (!src.EndsWith(".rpk", StringComparison.OrdinalIgnoreCase))
        {
            return src;
        }

        var trc = RoadGraphCatalog.TrcPathOf(RpakArchive.Read(src));
        if (trc is null)
        {
            e.WriteLine($"{src}: no map root names a .trc road graph");
            return null;
        }

        var path = new ObjectCatalog(root).ResolvePath(trc);
        if (path is null)
        {
            e.WriteLine($"{src}: {trc} not found under {root}");
        }

        return path;
    }

    public static RoadGraph? Graph(string src, string root, TextWriter e) =>
        Src(src, root, e) is { } p ? new RoadGraph(TrcFile.Read(p), Path.GetFileName(p)) : null;

    private static int Stats(RoadGraph g, TextWriter o)
    {
        var f = g.File;
        o.WriteLine($"{g.Name}: version {f.Version}, {f.Nodes.Count} crossings ({f.NodeTableA} table A + {f.Nodes.Count - f.NodeTableA} table B), {f.Roads.Count} roads, {f.Lanes.Count} lanes");
        o.WriteLine($"ids: crossings 1..{f.Nodes.Count}, roads {f.FirstRoadId}..{f.FirstLaneId - 1}, lanes {f.FirstLaneId}..{f.FirstLaneId + f.Lanes.Count - 1}");
        var conns = g.Nodes.Sum(n => n.Connectors.Count);
        o.WriteLine($"turn paths: {conns}; links per crossing {g.Nodes.Min(n => n.Source.Links.Count)}..{g.Nodes.Max(n => n.Source.Links.Count)}");
        o.WriteLine($"lanes per road: {string.Join(", ", g.Roads.GroupBy(r => r.Lanes.Count).OrderBy(x => x.Key).Select(x => $"{x.Key}:{x.Count()}"))}");
        o.WriteLine($"curve pieces: roads {f.Roads.Sum(r => r.Pieces.Count)}, lanes {f.Lanes.Sum(l => l.Pieces.Count)}, turn paths {conns}");
        o.WriteLine(string.Create(Inv, $"length: roads {g.Roads.Sum(r => r.Curve.Length) / 1000:0.0} km, lanes {g.Lanes.Sum(l => l.Curve.Length) / 1000:0.0} km, turn paths {g.Nodes.Sum(n => n.Connectors.Sum(c => c.Curve.Length)) / 1000:0.0} km"));
        o.WriteLine($"speed values (m/s): {string.Join(", ", g.Lanes.GroupBy(l => l.SpeedLimit).OrderBy(x => x.Key).Select(x => string.Create(Inv, $"{x.Key:0.000} ({x.Key * 3.6f:0} km/h) x{x.Count()}")))}");
        o.WriteLine($"lanes with left/right neighbour: {g.Lanes.Count(l => l.Left is not null)}/{g.Lanes.Count(l => l.Right is not null)}; reversed lanes {g.Lanes.Count(l => l.Reverse)}");
        var (x0, z0, x1, z1) = RoadMapImage.BoundsOf(g, 0);
        o.WriteLine(string.Create(Inv, $"bounds: x {x0:0.0}..{x1:0.0}, z {z0:0.0}..{z1:0.0}, y {g.Lanes.Min(l => l.Curve.Bounds().Min.Y):0.0}..{g.Lanes.Max(l => l.Curve.Bounds().Max.Y):0.0}"));
        var dead = g.Lanes.Count(l => l.Exits.Count == 0);
        o.WriteLine($"lanes without a turn path at their end: {dead}");
        return 0;
    }

    /// <summary>Re-checks the MEASURED structural facts of docs/formats/trc.md on a file; exit 1 if any fails.</summary>
    private static int Verify(string? path, TextWriter o)
    {
        if (path is null)
        {
            return 1;
        }

        var bytes = File.ReadAllBytes(path);
        var f = TrcFile.Read(bytes);
        var g = new RoadGraph(f, Path.GetFileName(path));
        var fails = 0;
        void Check(string what, int ok, int total)
        {
            o.WriteLine($"{(ok == total ? "ok  " : "FAIL")} {what}: {ok}/{total}");
            fails += ok == total ? 0 : 1;
        }

        Check("byte-identical rewrite", f.Write().AsSpan().SequenceEqual(bytes) ? 1 : 0, 1);
        var links = f.Nodes.SelectMany(n => n.Links).ToList();
        Check("lane ids appear once as start and once as end", f.Lanes.Count(l => links.Count(k => k.LaneId == l.Id && k.Starts) == 1 && links.Count(k => k.LaneId == l.Id && !k.Starts) == 1), f.Lanes.Count);
        Check("lane EndLink/StartLink index the end/start crossing's links", f.Lanes.Count(l =>
            g.LaneById(l.Id) is { EndNode: { } en, StartNode: { } sn } && en.Source.Links[(int)l.EndLink].LaneId == l.Id && sn.Source.Links[(int)l.StartLink].LaneId == l.Id), f.Lanes.Count);
        var conns = g.Nodes.SelectMany(n => n.Connectors).ToList();
        Check("turn path runs from in-lane end to out-lane start (<1 cm)", conns.Count(c => Vector3.Distance(c.Curve.Start, c.From.Curve.End) < 0.01f && Vector3.Distance(c.Curve.End, c.To.Curve.Start) < 0.01f), f.Nodes.Sum(n => n.Connectors.Count));
        Check("road curve start/end = second/first vector", f.Roads.Count(r => Vector3.Distance(r.Pieces[0].P0, r.StartPosition) < 1e-3f && Vector3.Distance(r.Pieces[^1].P1, r.EndPosition) < 1e-3f), f.Roads.Count);
        Check("lane curve start/end = second/first vector", f.Lanes.Count(l => Vector3.Distance(l.Pieces[0].P0, l.StartPosition) < 1e-3f && Vector3.Distance(l.Pieces[^1].P1, l.EndPosition) < 1e-3f), f.Lanes.Count);
        Check("road end crossing nearer the curve end", g.Roads.Count(r => r.EndNode is { } en && r.StartNode is { } sn && Vector3.Distance(en.Position, r.Curve.End) < Vector3.Distance(sn.Position, r.Curve.End)), f.Roads.Count);
        Check("lane Reverse == runs against the road", g.Lanes.Count(l =>
            (Vector3.Distance(l.Curve.Start, l.Road.Curve.Start) + Vector3.Distance(l.Curve.End, l.Road.Curve.End) < Vector3.Distance(l.Curve.Start, l.Road.Curve.End) + Vector3.Distance(l.Curve.End, l.Road.Curve.Start)) == !l.Reverse), f.Lanes.Count);
        Check("road FirstLaneId is its lowest lane", g.Roads.Count(r => r.Lanes.Count > 0 && r.Lanes.Min(l => l.Id) == r.Source.FirstLaneId), f.Roads.Count);
        Check("left/right neighbours are mutual", g.Lanes.Count(l => (l.Right is null || l.Right.Left == l) && (l.Left is null || l.Left.Right == l)), f.Lanes.Count);
        var pieces = f.Roads.SelectMany(r => r.Pieces).Concat(f.Lanes.SelectMany(l => l.Pieces)).Concat(f.Nodes.SelectMany(n => n.Connectors.Select(c => c.Curve))).ToList();
        var maxErr = pieces.Max(p => MathF.Abs(p.ArcLength() - p.Length));
        Check("piece Length == Length2", pieces.Count(p => p.Length == p.Length2), pieces.Count);
        Check("piece Hermite arc length within 0.4 m of Length", pieces.Count(p => MathF.Abs(p.ArcLength() - p.Length) < 0.4f), pieces.Count);
        o.WriteLine(string.Create(Inv, $"     max |arc length - Length| = {maxErr:0.000} m over {pieces.Count} pieces"));
        Check("sort table LaneIndex is a permutation", f.Lanes.Select(l => l.Sort.LaneIndex).Distinct().Count(i => i >= 0 && i < f.Lanes.Count), f.Lanes.Count);
        return fails == 0 ? 0 : 1;
    }

    private static int Dump(RoadGraph g, string what, int limit, TextWriter o)
    {
        switch (what)
        {
            case "nodes":
                foreach (var n in g.Nodes.Take(limit))
                {
                    o.WriteLine($"node {n.Id} {(n.Source.Table == 0 ? 'A' : 'B')} {V(n.Position)} links [{string.Join(' ', n.Source.Links.Select(k => $"{k.LaneId}{(k.Starts ? "+" : "-")}"))}] turn paths {n.Connectors.Count} tail {n.Source.Tail}");
                }

                break;
            case "roads":
                foreach (var r in g.Roads.Take(limit))
                {
                    o.WriteLine(string.Create(Inv, $"road {r.Id} {r.StartNode?.Id}->{r.EndNode?.Id} {V(r.Curve.Start)}->{V(r.Curve.End)} len {r.Curve.Length:0.0} pieces {r.Curve.Pieces.Count} lanes [{string.Join(' ', r.Lanes.Select(l => $"{l.Id}{(l.Reverse ? "r" : "")}"))}] flags {r.Source.Flags}"));
                }

                break;
            case "lanes":
                foreach (var l in g.Lanes.Take(limit))
                {
                    o.WriteLine(string.Create(Inv, $"lane {l.Id} road {l.Road.Id}{(l.Reverse ? " rev" : "")} {l.StartNode?.Id}->{l.EndNode?.Id} len {l.Curve.Length:0.0} speed {l.SpeedLimit:0.000} left {l.Left?.Id} right {l.Right?.Id} next {l.Source.NextLaneId} exits [{string.Join(' ', l.Exits.Select(x => x.To.Id))}] sort ({l.Source.Sort.XMin:0.0},{l.Source.Sort.RunningXMax:0.0},{l.Source.Sort.XMax:0.0},#{l.Source.Sort.LaneIndex})"));
                }

                break;
            case "connectors":
                foreach (var c in g.Nodes.SelectMany(n => n.Connectors).Take(limit))
                {
                    o.WriteLine(string.Create(Inv, $"node {c.Node.Id}: lane {c.From.Id} -> {c.To.Id} code {c.Source.Code} turn {c.Source.Turn} len {c.Curve.Length:0.0}"));
                }

                break;
            default:
                o.WriteLine("dump what: nodes|roads|lanes|connectors");
                return 2;
        }

        return 0;
    }

    private static int Align(RoadGraph g, Vector3 p, TextWriter o)
    {
        var hit = g.NearestLane(p);
        o.WriteLine(hit is { } h ? string.Create(Inv, $"nearest lane {h.Lane.Id} (road {h.Lane.Road.Id}) at {h.Distance:0.0} m along, lateral {h.Lateral:0.00}, point {V(h.Position)}, {h.Range:0.00} m away") : "no lane within 20 m (x) / 5 m (y)");
        o.WriteLine(g.AlignToRoad(p) is { } a ? $"alignToRoad: {V(a.Position)} dir {V(a.Direction)}" : "alignToRoad: none");
        return 0;
    }

    private static int Route(RoadGraph g, Vector3 a, Vector3 b, int samples, TextWriter o)
    {
        var sw = Stopwatch.StartNew();
        var r = g.FindRoute(a, b);
        var ms = sw.Elapsed.TotalMilliseconds;
        if (r is null)
        {
            o.WriteLine(string.Create(Inv, $"no route ({ms:0.00} ms)"));
            return 1;
        }

        o.WriteLine(string.Create(Inv, $"route {r.Length:0.0} m (straight {Vector3.Distance(a, b):0.0} m), {r.Legs.Count} legs, lanes {string.Join(' ', r.LanesOnRoute.Select(l => l.Id))} ({ms:0.00} ms)"));
        o.WriteLine($"start direction {V(r.Sample(0).Tangent)}");
        for (var i = 0; i <= samples; i++)
        {
            var s = r.Sample(r.Length * i / Math.Max(1, samples));
            o.WriteLine(string.Create(Inv, $"  {s.Distance,8:0.0} m {V(s.Position)} tangent {V(s.Tangent)} curvature {s.Curvature:0.0000} speed {s.SpeedLimit:0.0} lane {s.LaneId}"));
        }

        return 0;
    }

    private static int Map(RoadGraph g, string[] args, string[] pos, string root, TextWriter o, TextWriter e)
    {
        if (pos.Length < 1)
        {
            e.WriteLine(Usage);
            return 2;
        }

        var cell = F(Opt(args, "--cell") ?? "3");
        var (x0, z0, x1, z1) = RoadMapImage.BoundsOf(g);
        if (Array.IndexOf(args, "--bounds") is var bi and >= 0)
        {
            (x0, z0, x1, z1) = (F(args[bi + 1]), F(args[bi + 2]), F(args[bi + 3]), F(args[bi + 4]));
        }

        var img = new RoadMapImage(g, x0, z0, x1, z1, cell);
        if (Array.IndexOf(args, "--height") >= 0 && args[1].EndsWith(".rpk", StringComparison.OrdinalIgnoreCase))
        {
            var w = WorldLoader.Load(args[1], new WorldLoadOptions { GameRoot = root, IncludeObjectCollision = false, BuildRenderScene = false });
            img.Background = HeightMap.Sample(w.Ground, x0, z0, x1, z1, cell);
            o.WriteLine($"height map: {w.Name} + [{string.Join(", ", w.Extensions.Select(x => x.Name))}], {img.Background.Covered}/{img.Background.Width * img.Background.Height} cells with ground");
        }

        if (Array.IndexOf(args, "--route") is var ri and >= 0)
        {
            var p = args[(ri + 1)..(ri + 7)];
            img.Route = g.FindRoute(Vec(p, 0), Vec(p, 3));
            o.WriteLine(img.Route is { } rr ? string.Create(Inv, $"route {rr.Length:0.0} m") : "no route");
        }

        for (var i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == "--spl")
            {
                img.Extra.Add(SplineFile.Read(args[i + 1]));
            }
        }

        var outPath = pos[0];
        if (outPath.EndsWith(".svg", StringComparison.OrdinalIgnoreCase))
        {
            File.WriteAllText(outPath, img.ToSvg());
        }
        else
        {
            File.WriteAllBytes(outPath, img.ToPng());
        }

        o.WriteLine(string.Create(Inv, $"wrote {outPath}: {img.Width}x{img.Height}, cell {cell} m, x {x0:0}..{x1:0}, z {z0:0}..{z1:0}"));
        return 0;
    }

    /// <summary>Query rates on points 0-3 m beside random lane points (one thread).</summary>
    private static int Bench(RoadGraph g, int n, TextWriter o)
    {
        var rng = new Random(7);
        var pts = Enumerable.Range(0, n).Select(_ =>
        {
            var l = g.Lanes[rng.Next(g.Lanes.Count)];
            return l.Curve.PointAt((float)rng.NextDouble() * l.Curve.Length) + new Vector3((float)rng.NextDouble() * 3, 0.5f, (float)rng.NextDouble() * 3);
        }).ToArray();
        g.NearestLane(pts[0]);
        var sw = Stopwatch.StartNew();
        var hits = pts.Count(p => g.NearestLane(p) is not null);
        var nl = sw.Elapsed.TotalMilliseconds;
        sw.Restart();
        var al = pts.Count(p => g.AlignToRoad(p) is not null);
        var am = sw.Elapsed.TotalMilliseconds;
        sw.Restart();
        var routes = 0;
        var len = 0f;
        var rn = Math.Min(200, n / 2);
        for (var i = 0; i < rn; i++)
        {
            if (g.FindRoute(pts[2 * i], pts[(2 * i) + 1]) is { } r)
            {
                routes++;
                len += r.Length;
            }
        }

        var rm = sw.Elapsed.TotalMilliseconds;
        o.WriteLine(string.Create(Inv, $"nearest lane: {n} queries, {hits} hits, {nl * 1000 / n:0.0} us each"));
        o.WriteLine(string.Create(Inv, $"alignToRoad: {al} hits, {am * 1000 / n:0.0} us each"));
        o.WriteLine(string.Create(Inv, $"routes: {routes}/{rn} found, mean {len / Math.Max(1, routes):0} m, {rm / rn:0.00} ms each"));
        return 0;
    }

    private static int Spl(string path, TextWriter o)
    {
        var s = SplineFile.Read(path);
        o.WriteLine(string.Create(Inv, $"{Path.GetFileName(path)}: {s.Points.Count} points, {s.Points.Select(p => p.Values.Length + 6).Distinct().Count()} column count(s) [{string.Join(",", s.Points.Select(p => p.Values.Length + 6).Distinct())}], Catmull-Rom length {s.Length:0.0} m"));
        var closed = s.Points.Count > 1 ? Vector3.Distance(s.Points[0].Position, s.Points[^1].Position) : 0;
        o.WriteLine(string.Create(Inv, $"first {V(s.Points[0].Position)} last {V(s.Points[^1].Position)} (gap {closed:0.0} m)"));
        var cols = s.Points.Count == 0 ? 0 : s.Points.Max(p => p.Values.Length);
        for (var c = 0; c < cols; c++)
        {
            var vals = s.Points.Where(p => p.Values.Length > c).Select(p => p.Values[c]).ToList();
            o.WriteLine(string.Create(Inv, $"  column {c + 7}: {vals.Min():0.###}..{vals.Max():0.###}, {vals.Distinct().Count()} distinct"));
        }

        var dirDot = s.Points.Zip(s.Points.Skip(1), (p, q) => Vector3.Dot(Vector3.Normalize(q.Position - p.Position), p.Direction)).ToList();
        if (dirDot.Count > 0)
        {
            o.WriteLine(string.Create(Inv, $"  direction · chord: mean {dirDot.Average():0.000}, min {dirDot.Min():0.000}"));
        }

        return 0;
    }
}
