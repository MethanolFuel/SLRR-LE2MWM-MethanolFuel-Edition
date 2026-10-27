using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using Slrr.Core;
using Slrr.World;

namespace Slrr.Cli;

/// <summary>
/// <c>world</c>: static driving world (collision, render scene, surfaces) of a map archive.
/// <code>
///   world stats    &lt;map.rpk&gt; [--root dir]             build times, counts, bounds
///   world surfaces &lt;map.rpk&gt; [--root dir]             surface table with triangle counts
///   world height   &lt;map.rpk&gt; x z [fromY] [--root dir]  ground probe
///   world ray      &lt;map.rpk&gt; ox oy oz dx dy dz [maxDist] [--root dir]
///   world map      &lt;map.rpk&gt; out.png|out.svg [--cell m] [--surface] [--bounds x0 z0 x1 z1] [--root dir]
///   world bench    &lt;map.rpk&gt; [count] [--root dir]      build time + ground/ray query rates
///   world verify   &lt;map.rpk&gt; [--root dir]             data checks (normals, objects on ground, bodies in bounds)
///   world angles   &lt;map.rpk&gt; [--root dir]             fit placed-object angle conventions to record boxes
/// </code>
/// </summary>
public static class CmdWorld
{
    private const string Usage = """
        world stats|surfaces|height|ray|map|bench|verify|angles <map.rpk> [args] [--root gameRoot]
          world height <map.rpk> x z [fromY]
          world ray <map.rpk> ox oy oz dx dy dz [maxDist]
          world map <map.rpk> out.png|out.svg [--cell metres] [--surface] [--bounds x0 z0 x1 z1]
          world bench <map.rpk> [queryCount]
        """;

    public static int Run(string[] args, TextWriter o, TextWriter e)
    {
        if (args.Length > 0 && args[0] == "world")
        {
            args = args[1..];
        }

        if (args.Length < 2)
        {
            e.WriteLine(Usage);
            return 2;
        }

        try
        {
            var cmd = args[0];
            var path = args[1];
            var rest = args[2..].ToList();
            var root = TakeOption(rest, "--root") ?? FindRoot(path);
            var opts = new WorldLoadOptions { GameRoot = root, BuildRenderScene = cmd is "stats" or "bench" or "map" or "surfaces" or "verify" };
            switch (cmd)
            {
                case "stats":
                    return Stats(Load(path, opts), o);
                case "surfaces":
                    return Surfaces(Load(path, opts), o);
                case "height":
                    return Height(Load(path, opts), rest, o, e);
                case "ray":
                    return Ray(Load(path, opts), rest, o, e);
                case "map":
                    return Map(Load(path, opts), rest, o, e);
                case "bench":
                    return Bench(path, opts, rest, o);
                case "verify":
                    return Verify(path, root, o);
                case "angles":
                    return Angles(Load(path, opts), o);
                default:
                    e.WriteLine(Usage);
                    return 2;
            }
        }
        catch (Exception ex) when (ex is SlrrFormatException or IOException or InvalidOperationException or UnauthorizedAccessException or FormatException)
        {
            e.WriteLine($"error: {ex.Message}");
            return 1;
        }
    }

    private static StaticWorld Load(string path, WorldLoadOptions opts) => WorldLoader.Load(path, opts);

    private static string? FindRoot(string rpkPath)
    {
        for (var d = new DirectoryInfo(Path.GetDirectoryName(Path.GetFullPath(rpkPath)) ?? "."); d is not null; d = d.Parent)
        {
            if (File.Exists(Path.Combine(d.FullName, "sl.rpk")))
            {
                return d.FullName;
            }
        }

        var cwd = Directory.GetCurrentDirectory();
        return File.Exists(Path.Combine(cwd, "sl.rpk")) ? cwd : null;
    }

    private static string? TakeOption(List<string> a, string name)
    {
        var i = a.IndexOf(name);
        if (i < 0 || i + 1 >= a.Count)
        {
            return null;
        }

        var v = a[i + 1];
        a.RemoveRange(i, 2);
        return v;
    }

    private static float P(string s) => float.Parse(s, CultureInfo.InvariantCulture);

    private static string F(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);

    private static int Stats(StaticWorld w, TextWriter o)
    {
        var s = w.Stats;
        var c = w.Collision;
        o.WriteLine($"{w.Name}: {s.Nodes} nodes, {s.PhysChunks} PHYS chunks, {s.PolyChunks} POLY chunks");
        o.WriteLine($"collision: {c.TriangleCount} triangles ({s.MapTriangles} map + {s.ObjectTriangles} object-shape), {c.NodeCount} BVH nodes, {s.DegenerateDropped} degenerate dropped, {s.BadPhysIndices} bad indices");
        o.WriteLine($"bounds: {c.Bounds.Min} .. {c.Bounds.Max}");
        o.WriteLine($"surface table: {w.Surfaces.Count} entries" + (w.ScmPath is null ? " (anonymous: no text .scm found)" : $" from {Path.GetFileName(w.ScmPath)}"));
        o.WriteLine($"render: {w.Render.Chunks.Count} chunks, {w.Render.SubmeshCount} submeshes, {w.Render.VertexCount} vertices, {w.Render.TriangleCount} triangles, {w.Render.MaterialsOutOfRange} submeshes with out-of-range material");
        o.WriteLine($"objects: {s.Objects} placed, {s.ObjectsWithBodies} with body shapes ({s.StaticBodies} static, {s.DynamicBodies} dynamic)");
        o.WriteLine($"build ms: read archive {F(s.ReadArchiveMs)}, instance forest {F(s.ReadForestMs)}, PHYS extract {F(s.CollisionExtractMs)}, objects {F(s.ObjectsMs)}, BVH+ground grid {F(s.BvhBuildMs)}, render scene {F(s.RenderSceneMs)}, total {F(s.TotalMs)}");
        return 0;
    }

    private static int Surfaces(StaticWorld w, TextWriter o)
    {
        var counts = new Dictionary<int, int>();
        for (var i = 0; i < w.Collision.TriangleCount; i++)
        {
            var sfc = w.Collision.SurfaceOf(i);
            counts[sfc] = counts.GetValueOrDefault(sfc) + 1;
        }

        var renderUse = new Dictionary<int, int>();
        foreach (var sub in w.Render.Chunks.SelectMany(ch => ch.Submeshes))
        {
            renderUse[sub.Material] = renderUse.GetValueOrDefault(sub.Material) + sub.TriangleCount;
        }

        o.WriteLine("idx  class     friction rollres collcoeff  skidSM      skidPS      physTris renderTris name");
        foreach (var s in w.Surfaces.Items)
        {
            o.WriteLine($"{s.Index,3}  {s.Class,-9} {F(s.Friction),8} {F(s.RollResistance),7} {F(s.CollisionCoeff),9}  0x{s.SkidMarkResource:X8}  0x{s.SkidParticleResource:X8}  {counts.GetValueOrDefault(s.Index),8} {renderUse.GetValueOrDefault(s.Index),10} {s.Name}{(s.HasExplicitProperties ? " *" : string.Empty)}");
        }

        if (counts.GetValueOrDefault(SurfaceTable.ObjectSurfaceId) > 0)
        {
            o.WriteLine($"obj  Object    object-shape triangles: {counts[SurfaceTable.ObjectSurfaceId]}");
        }

        o.WriteLine("(* = the .scm gives explicit friction/rollres/collcoeff/skid lines for this material)");
        return 0;
    }

    private static int Height(StaticWorld w, List<string> a, TextWriter o, TextWriter e)
    {
        if (a.Count < 2)
        {
            e.WriteLine("usage: world height <map.rpk> x z [fromY]");
            return 2;
        }

        var x = P(a[0]);
        var z = P(a[1]);
        var from = a.Count > 2 ? P(a[2]) : float.PositiveInfinity;
        if (!w.Collision.TryGetGround(x, z, from, out var hit))
        {
            o.WriteLine("no ground");
            return 1;
        }

        var s = w.Surfaces.Get(hit.Surface);
        o.WriteLine($"height {F(hit.Position.Y)} normal {hit.Normal} surface {hit.Surface} '{s.Name}' {s.Class} friction {F(s.Friction)} triangle {hit.Triangle} [{w.Collision.Vertex(hit.Triangle, 0)} {w.Collision.Vertex(hit.Triangle, 1)} {w.Collision.Vertex(hit.Triangle, 2)}] source 0x{hit.Source:X8}");
        return 0;
    }

    private static int Ray(StaticWorld w, List<string> a, TextWriter o, TextWriter e)
    {
        if (a.Count < 6)
        {
            e.WriteLine("usage: world ray <map.rpk> ox oy oz dx dy dz [maxDist]");
            return 2;
        }

        var origin = new Vector3(P(a[0]), P(a[1]), P(a[2]));
        var dir = new Vector3(P(a[3]), P(a[4]), P(a[5]));
        var max = a.Count > 6 ? P(a[6]) : 100000f;
        if (!w.Collision.Raycast(origin, dir, max, out var hit))
        {
            o.WriteLine("no hit");
            return 1;
        }

        o.WriteLine($"hit at {hit.Position} t {F(hit.Distance)} normal {hit.Normal} surface {hit.Surface} '{w.Surfaces.Get(hit.Surface).Name}' source 0x{hit.Source:X8}");
        return 0;
    }

    private static int Map(StaticWorld w, List<string> a, TextWriter o, TextWriter e)
    {
        if (a.Count < 1)
        {
            e.WriteLine("usage: world map <map.rpk> out.png|out.svg [--cell m] [--surface] [--bounds x0 z0 x1 z1]");
            return 2;
        }

        var outPath = a[0];
        var cellOpt = TakeOption(a, "--cell");
        var bySurface = a.Remove("--surface");
        var (mn, mx) = w.Collision.Bounds;
        float x0 = mn.X, z0 = mn.Z, x1 = mx.X, z1 = mx.Z;
        var bi = a.IndexOf("--bounds");
        if (bi >= 0 && bi + 4 < a.Count)
        {
            x0 = P(a[bi + 1]);
            z0 = P(a[bi + 2]);
            x1 = P(a[bi + 3]);
            z1 = P(a[bi + 4]);
        }

        var svg = outPath.EndsWith(".svg", StringComparison.OrdinalIgnoreCase);
        var cell = cellOpt is null ? MathF.Max(1f, MathF.Max(x1 - x0, z1 - z0) / (svg ? 256f : 1024f)) : P(cellOpt);
        var sw = Stopwatch.StartNew();
        var map = HeightMap.Sample(w.Collision, x0, z0, x1, z1, cell);
        var ms = sw.Elapsed.TotalMilliseconds;
        if (svg)
        {
            File.WriteAllText(outPath, map.ToSvg(bySurface, w.Surfaces));
        }
        else
        {
            File.WriteAllBytes(outPath, map.ToPng(bySurface, w.Surfaces));
        }

        o.WriteLine($"wrote {outPath}: {map.Width}x{map.Height} cells of {F(cell)} m, covered {map.Covered}/{map.Width * map.Height}, height {F(map.MinHeight)}..{F(map.MaxHeight)} m, sampled in {F(ms)} ms ({F(map.Width * (double)map.Height / ms * 1000)} queries/s, parallel)");
        return 0;
    }

    private static int Bench(string path, WorldLoadOptions opts, List<string> a, TextWriter o)
    {
        var n = a.Count > 0 ? int.Parse(a[0], CultureInfo.InvariantCulture) : 1_000_000;
        var w = Load(path, opts);
        Stats(w, o);
        var (mn, mx) = w.Collision.Bounds;

        // ground queries are placed on triangle centroids (guaranteed hits), a second set on random points
        var rng = new Random(12345);
        var xs = new float[n];
        var zs = new float[n];
        var tc = w.Collision.TriangleCount;
        var up = Enumerable.Range(0, tc).Where(t => w.Collision.Normal(t).Y > 0.5f).ToArray();
        for (var i = 0; i < n; i++)
        {
            var t = up.Length > 0 ? up[rng.Next(up.Length)] : rng.Next(tc);
            var c = (w.Collision.Vertex(t, 0) + w.Collision.Vertex(t, 1) + w.Collision.Vertex(t, 2)) / 3f;
            xs[i] = c.X;
            zs[i] = c.Z;
        }

        long hits = 0;
        double sum = 0;
        var sw = Stopwatch.StartNew();
        for (var i = 0; i < n; i++)
        {
            if (w.Collision.TryGetGround(xs[i], zs[i], float.PositiveInfinity, out var h))
            {
                hits++;
                sum += h.Position.Y;
            }
        }

        var single = sw.Elapsed.TotalSeconds;
        o.WriteLine($"ground (top-most, on triangle centroids): {n} queries, {hits} hits, single thread {F(n / single)} q/s ({F(single / n * 1e6)} us each) [checksum {F(sum)}]");

        sw.Restart();
        var phits = 0L;
        Parallel.For(0, Environment.ProcessorCount, () => 0L, (t, state, acc) =>
        {
            for (var i = t; i < n; i += Environment.ProcessorCount)
            {
                if (w.Collision.TryGetGround(xs[i], zs[i], float.PositiveInfinity, out _))
                {
                    acc++;
                }
            }

            return acc;
        }, acc => Interlocked.Add(ref phits, acc));
        var par = sw.Elapsed.TotalSeconds;
        o.WriteLine($"ground, {Environment.ProcessorCount} threads: {F(n / par)} q/s");

        // "car height" queries: from 2 m above the surface, like a wheel probe
        sw.Restart();
        long h2 = 0;
        for (var i = 0; i < n; i++)
        {
            if (w.Collision.TryGetGround(xs[i], zs[i], w.Collision.HeightAt(xs[i], zs[i]) + 2f, out _))
            {
                h2++;
            }
        }

        var two = sw.Elapsed.TotalSeconds;
        o.WriteLine($"ground, bounded start (2 call pattern incl. a height lookup): {F(n / two)} pairs/s, {h2} hits");

        // coherent path: 1000 starting points, 0.1 m steps along +x (what a wheel probe sees while driving)
        sw.Restart();
        long ph = 0;
        var steps = Math.Max(1, n / 1000);
        for (var s0 = 0; s0 < 1000; s0++)
        {
            for (var k = 0; k < steps; k++)
            {
                if (w.Collision.TryGetGround(xs[s0] + (k * 0.1f), zs[s0], float.PositiveInfinity, out _))
                {
                    ph++;
                }
            }
        }

        var pt = sw.Elapsed.TotalSeconds;
        o.WriteLine($"ground, coherent path ({steps * 1000} queries, {ph} hits): {F(steps * 1000 / pt)} q/s");

        // grid vs BVH cross-check
        var bad = 0;
        var check = Math.Min(n, 200_000);
        for (var i = 0; i < check; i++)
        {
            var qx = xs[i] + 0.37f;
            var qz = zs[i] - 0.21f;
            var g1 = w.Collision.TryGetGround(qx, qz, float.PositiveInfinity, out var a1);
            var g2 = w.Collision.TryGetGroundBvh(qx, qz, float.PositiveInfinity, out var a2);
            if (g1 != g2 || (g1 && MathF.Abs(a1.Position.Y - a2.Position.Y) > 1e-3f))
            {
                if (bad++ < 3)
                {
                    o.WriteLine($"  mismatch at ({qx}, {qz}): grid {g1}/{a1.Position.Y}/tri {a1.Triangle} vs bvh {g2}/{a2.Position.Y}/tri {a2.Triangle}");
                }
            }
        }

        o.WriteLine($"grid vs BVH ground agreement on {check} offset queries: {check - bad} equal, {bad} different");

        var big = new int[5];
        for (var t = 0; t < tc; t++)
        {
            var a0 = w.Collision.Vertex(t, 0);
            var b0 = w.Collision.Vertex(t, 1);
            var c0 = w.Collision.Vertex(t, 2);
            var ext = Vector3.Max(a0, Vector3.Max(b0, c0)) - Vector3.Min(a0, Vector3.Min(b0, c0));
            var m = Math.Max(ext.X, Math.Max(ext.Y, ext.Z));
            big[m > 256 ? 4 : m > 64 ? 3 : m > 16 ? 2 : m > 4 ? 1 : 0]++;
        }

        o.WriteLine($"triangle max extent: <=4m {big[0]}, <=16m {big[1]}, <=64m {big[2]}, <=256m {big[3]}, >256m {big[4]}");
        var rn = Math.Min(n, 200_000);
        sw.Restart();
        long rh = 0;
        for (var i = 0; i < rn; i++)
        {
            var org = new Vector3(xs[i], w.Collision.HeightAt(xs[i], zs[i]) + 1.5f, zs[i]);
            var dir = new Vector3((float)Math.Cos(i * 2.399), 0f, (float)Math.Sin(i * 2.399));
            if (w.Collision.Raycast(org, dir, 500f, out _))
            {
                rh++;
            }
        }

        var rs = sw.Elapsed.TotalSeconds;
        o.WriteLine($"raycast (1.5 m above ground, horizontal, max 500 m): {rn} rays, {rh} hits, {F(rn / rs)} rays/s");

        var contacts = new List<Contact>();
        sw.Restart();
        long cc = 0;
        for (var i = 0; i < rn; i++)
        {
            contacts.Clear();
            var gy = w.Collision.HeightAt(xs[i], zs[i]);
            cc += w.Collision.OverlapSphere(new Vector3(xs[i], gy + 0.3f, zs[i]), 0.5f, contacts);
        }

        var cs = sw.Elapsed.TotalSeconds;
        o.WriteLine($"sphere overlap (r 0.5 m just above ground): {rn} queries, {cc} contacts, {F(rn / cs)} q/s");
        return 0;
    }

    private static int Verify(string path, string? root, TextWriter o)
    {
        // objects are excluded from collision so "object sits on ground" is not a self-hit
        var w = WorldLoader.Load(path, new WorldLoadOptions { GameRoot = root, IncludeObjectCollision = false, BuildRenderScene = true });
        var c = w.Collision;
        int up = 0, down = 0, wall = 0, disagree = 0;
        var hugeBy = new Dictionary<int, int>();
        var physBySurface = new double[w.Surfaces.Count + 1];
        var polyByMaterial = new double[w.Surfaces.Count + 1];
        foreach (var sub in w.Render.Chunks.SelectMany(ch => ch.Submeshes))
        {
            polyByMaterial[Math.Min(sub.Material, w.Surfaces.Count)] += sub.TriangleCount;
        }

        for (var t = 0; t < c.TriangleCount; t++)
        {
            physBySurface[Math.Min(c.SurfaceOf(t), w.Surfaces.Count)]++;
            var n = c.Normal(t);
            if (n.Y > 0.05f)
            {
                up++;
            }
            else if (n.Y < -0.05f)
            {
                down++;
            }
            else
            {
                wall++;
            }

            var g = Vector3.Normalize(Vector3.Cross(c.Vertex(t, 1) - c.Vertex(t, 0), c.Vertex(t, 2) - c.Vertex(t, 0)));
            if (Vector3.Dot(g, n) < 0.999f)
            {
                disagree++;
            }
        }

        o.WriteLine($"triangles {c.TriangleCount}: normal.Y > 0.05 (ground) {up}, < -0.05 (ceiling) {down}, walls {wall}; stored normal != geometric normal: {disagree}");

        var mp = physBySurface.Average();
        var mq = polyByMaterial.Average();
        var cov = physBySurface.Zip(polyByMaterial, (a, b) => (a - mp) * (b - mq)).Sum();
        var corr = cov / Math.Sqrt(physBySurface.Sum(a => (a - mp) * (a - mp)) * polyByMaterial.Sum(b => (b - mq) * (b - mq)));
        o.WriteLine($"PHYS triangles per surface index vs POLY triangles per material index: Pearson r = {F(corr)} over {w.Surfaces.Count} indices (1.0 = same index space)");
        var wAll = WorldLoader.Load(path, new WorldLoadOptions { GameRoot = root, BuildRenderScene = false });
        var ca = wAll.Collision;
        for (var t = 0; t < ca.TriangleCount; t++)
        {
            if (ca.SurfaceOf(t) != SurfaceTable.ObjectSurfaceId)
            {
                continue;
            }

            var ext = Vector3.Max(ca.Vertex(t, 0), Vector3.Max(ca.Vertex(t, 1), ca.Vertex(t, 2))) - Vector3.Min(ca.Vertex(t, 0), Vector3.Min(ca.Vertex(t, 1), ca.Vertex(t, 2)));
            if (Math.Max(ext.X, Math.Max(ext.Y, ext.Z)) > 200)
            {
                hugeBy[ca.SourceOf(t)] = hugeBy.GetValueOrDefault(ca.SourceOf(t)) + 1;
            }
        }

        o.WriteLine($"object-shape triangles with an edge > 200 m: {hugeBy.Values.Sum()} from {hugeBy.Count} objects" + (hugeBy.Count > 0 ? " e.g. " + string.Join(", ", hugeBy.Take(5).Select(kv => $"0x{kv.Key:X} {w.Objects.FirstOrDefault(x => x.Id == (uint)kv.Key)?.Name}")) : string.Empty));
        var deltas = new List<double>();
        var noGround = 0;
        foreach (var obj in w.Objects)
        {
            if (c.TryGetGround(obj.Position.X, obj.Position.Z, obj.Position.Y + 1.0f, out var hit))
            {
                deltas.Add(obj.Position.Y - hit.Position.Y);
            }
            else
            {
                noGround++;
            }
        }

        deltas.Sort();
        if (deltas.Count > 0)
        {
            double Q(double p) => deltas[Math.Min(deltas.Count - 1, (int)(p * deltas.Count))];
            o.WriteLine($"objects {w.Objects.Count}: ground found below (y+1) for {deltas.Count}, none for {noGround}; object.y - ground.y: p5 {F(Q(0.05))} p25 {F(Q(0.25))} median {F(Q(0.5))} p75 {F(Q(0.75))} p95 {F(Q(0.95))}; within +-0.5 m: {deltas.Count(d => Math.Abs(d) <= 0.5)} ({F(100.0 * deltas.Count(d => Math.Abs(d) <= 0.5) / deltas.Count)}%)");
        }

        int inside = 0, insideFull = 0, total = 0;
        foreach (var obj in w.Objects.Where(x => x.Type?.Bounds is not null))
        {
            var (mn, mx) = obj.Type!.Bounds!.Value;
            foreach (var b in obj.Type.Bodies.Where(x => x.Kind == BodyShapeKind.Box))
            {
                total++;
                var lo = b.Position - b.Size;
                var hi = b.Position + b.Size;
                var flo = b.Position - (b.Size * 2);
                var fhi = b.Position + (b.Size * 2);
                if (flo.X >= mn.X - 0.1f && flo.Y >= mn.Y - 0.1f && flo.Z >= mn.Z - 0.1f && fhi.X <= mx.X + 0.1f && fhi.Y <= mx.Y + 0.1f && fhi.Z <= mx.Z + 0.1f)
                {
                    insideFull++;
                }

                if (lo.X >= mn.X - 0.1f && lo.Y >= mn.Y - 0.1f && lo.Z >= mn.Z - 0.1f && hi.X <= mx.X + 0.1f && hi.Y <= mx.Y + 0.1f && hi.Z <= mx.Z + 0.1f)
                {
                    inside++;
                }
            }
        }

        o.WriteLine($"box bodies of placed objects: {inside}/{total} lie inside the type's `object bounds` (+-0.1) when extents are HALF sizes; {insideFull} when the stored numbers are FULL sizes (control)");
        foreach (var obj in w.Objects.Where(x => x.Type?.Bounds is not null && x.Type.Bodies.Any(b => b.Kind == BodyShapeKind.Box)).DistinctBy(x => x.Type).Take(4))
        {
            o.WriteLine($"  sample {obj.Type!.Name}: bounds {obj.Type.Bounds!.Value.Min}..{obj.Type.Bounds!.Value.Max} boxes {string.Join("; ", obj.Type.Bodies.Where(b => b.Kind == BodyShapeKind.Box).Select(b => $"pos {b.Position} size {b.Size}"))}");
        }
        return 0;
    }

    private static int Angles(StaticWorld w, TextWriter o)
    {
        var withBounds = w.Objects.Where(x => x.Type?.Bounds is not null).ToList();
        o.WriteLine($"{w.Objects.Count} objects, {withBounds.Count} with type bounds, {withBounds.Count(x => x.Angles != Vector3.Zero)} with non-zero angles, {withBounds.Count(x => x.Angles.Y != 0 || x.Angles.Z != 0)} with non-zero b or c");
        var ident = PlacementAnalysis.Rank(withBounds).First(f => f.Convention is { AxisA: 1, SignA: 1, AxisB: 0, SignB: 1, AxisC: 2, SignC: 1, Order: 0 });
        o.WriteLine($"reference (a=Y+, b=X+, c=Z+, order0): within5cm {ident.Within5Cm}/{ident.Objects}, within50cm {ident.Within50Cm}, median {F(ident.MedianError)} p90 {F(ident.P90Error)}");
        void Show(string title, List<PlacementAnalysis.Fit> fits)
        {
            o.WriteLine(title);
            foreach (var f in fits.Take(8))
            {
                o.WriteLine($"  {Placement.Describe(f.Convention),-34} within5cm {f.Within5Cm,6}/{f.Objects} within50cm {f.Within50Cm,6} median {F(f.MedianError)} p90 {F(f.P90Error)}");
            }
        }

        Show("all objects with bounds:", PlacementAnalysis.Rank(withBounds));
        Show("objects with a != 0 (yaw sign / axis):", PlacementAnalysis.Rank(withBounds, x => Math.Abs(x.Angles.X) > 0.05f));
        Show("objects with b != 0:", PlacementAnalysis.Rank(withBounds, x => Math.Abs(x.Angles.Y) > 0.05f));
        Show("objects with c != 0:", PlacementAnalysis.Rank(withBounds, x => Math.Abs(x.Angles.Z) > 0.05f));
        return 0;
    }
}
