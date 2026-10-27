using System.Diagnostics;
using System.Numerics;
using Slrr.World;
using Xunit;

namespace Slrr.World.Tests;

internal static class GameRoot
{
    public static string? Find()
    {
        var env = Environment.GetEnvironmentVariable("SLRR_GAME_ROOT");
        if (!string.IsNullOrEmpty(env) && File.Exists(Path.Combine(env, "sl.rpk")))
        {
            return env;
        }

        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "sl.rpk")) && Directory.Exists(Path.Combine(dir.FullName, "maps")))
            {
                return dir.FullName;
            }
        }

        return null;
    }
}

/// <summary>Shipped-map tests; they pass vacuously when no game content is found (see docs/DEVELOPMENT.md).</summary>
public class CorpusTests
{
    private static StaticWorld? Load(string rel, bool render = true)
    {
        var root = GameRoot.Find();
        if (root is null)
        {
            return null;
        }

        var path = Path.Combine(root, rel);
        return File.Exists(path) ? WorldLoader.Load(path, new WorldLoadOptions { GameRoot = root, BuildRenderScene = render }) : null;
    }

    [Theory]
    [InlineData("maps/DamageTrack.rpk")]
    [InlineData("maps/MP1.rpk")]
    [InlineData("maps/DriftTrack.rpk")]
    [InlineData("maps/Addon_Maps/Drag_Track.rpk")]
    public void SmallMapsBuildAndAnswerGroundQueriesAtTriangleCentroids(string rel)
    {
        var w = Load(rel);
        if (w is null)
        {
            return;
        }

        var c = w.Collision;
        Assert.True(c.TriangleCount > 1000);
        Assert.True(w.Render.SubmeshCount > 0);
        Assert.Equal(0, w.Stats.BadPhysIndices);
        var checkedCount = 0;
        for (var t = 0; t < c.TriangleCount; t += 7)
        {
            if (c.Normal(t).Y < 0.5f)
            {
                continue;
            }

            var p = (c.Vertex(t, 0) + c.Vertex(t, 1) + c.Vertex(t, 2)) / 3f;
            Assert.True(c.TryGetGround(p.X, p.Z, float.PositiveInfinity, out var h));
            Assert.True(h.Position.Y >= p.Y - 1e-3f, "top-most ground must not be below the triangle itself");
            Assert.True(c.TryGetGround(p.X, p.Z, p.Y + 1e-3f, out var h2));
            Approx.Eq(p.Y, h2.Position.Y, 2);
            checkedCount++;
        }

        Assert.True(checkedCount > 100);
    }

    [Fact]
    public void PhysSurfaceAndPolyMaterialShareTheIndexSpace()
    {
        var w = Load("maps/MP1.rpk");
        if (w is null)
        {
            return;
        }

        var phys = new double[w.Surfaces.Count];
        var poly = new double[w.Surfaces.Count];
        for (var t = 0; t < w.Collision.TriangleCount; t++)
        {
            phys[w.Collision.SurfaceOf(t)]++;
        }

        foreach (var s in w.Render.Chunks.SelectMany(ch => ch.Submeshes))
        {
            poly[s.Material]++;
        }

        // every surface used by collision is also drawn (MEASURED r = 1.0 on 6 maps; here a loose structural check)
        for (var i = 0; i < phys.Length; i++)
        {
            Assert.Equal(phys[i] > 0, poly[i] > 0);
        }

        Assert.Equal("_p_asfalt", w.Surfaces.Get(2).Name);
    }

    [Fact]
    public void CityBuildsFastAndPlacedObjectsSitOnTheGround()
    {
        var root = GameRoot.Find();
        var path = root is null ? null : Path.Combine(root, "maps", "city.rpk");
        if (path is null || !File.Exists(path))
        {
            return;
        }

        var sw = Stopwatch.StartNew();
        var w = WorldLoader.Load(path, new WorldLoadOptions { GameRoot = root, IncludeObjectCollision = false });
        sw.Stop();
        Assert.True(sw.Elapsed.TotalSeconds < 30, $"build took {sw.Elapsed.TotalSeconds:0.0} s"); // measured ~1-3 s; generous for loaded CI
        Assert.Equal(108, w.Surfaces.Count);
        Assert.True(w.Objects.Count > 10000);
        Assert.Equal(0, w.Render.MaterialsOutOfRange);

        var within = 0;
        var tested = 0;
        foreach (var o in w.Objects)
        {
            if (w.Collision.TryGetGround(o.Position.X, o.Position.Z, o.Position.Y + 1f, out var h))
            {
                tested++;
                if (MathF.Abs(o.Position.Y - h.Position.Y) <= 0.5f)
                {
                    within++;
                }
            }
        }

        // MEASURED 91.7% on city.rpk (the rest are on roofs, bridges or not over a PHYS triangle at all)
        Assert.True(tested > 8000);
        Assert.True(within > tested * 0.85, $"{within}/{tested}");
    }

    [Theory]
    [InlineData("maps/city.rpk")]
    [InlineData("maps/ROC_track.rpk")]
    public void ObjectAnglesFollowTheDefaultConvention(string rel)
    {
        var w = Load(rel, render: false);
        if (w is null)
        {
            return;
        }

        var objs = w.Objects.Where(o => o.Type?.Bounds is not null).ToList();
        Assert.True(objs.Count > 1000);
        // record box == rotated local bounds within 5 cm: MEASURED 9708/9708 (city) and 1791/1791 (ROC_track)
        var ok = objs.Count(o => PlacementAnalysis.BoxError(o, Placement.Convention.Default) < 0.05);
        Assert.True(ok >= objs.Count * 0.999, $"{ok}/{objs.Count}");

        // the mirrored yaw must fit strictly fewer objects (symmetric objects fit both, so only a margin is asserted)
        var mirrored = Placement.Convention.Default with { SignA = -1 };
        var yawed = objs.Where(o => Math.Abs(o.Angles.X) > 0.05f).ToList();
        var good = yawed.Count(o => PlacementAnalysis.BoxError(o, Placement.Convention.Default) < 0.05);
        var bad = yawed.Count(o => PlacementAnalysis.BoxError(o, mirrored) < 0.05);
        Assert.True(good > bad + (yawed.Count / 10), $"default {good} vs mirrored {bad} of {yawed.Count}");
    }

    [Fact]
    public void ConcurrentGroundQueriesAreConsistent()
    {
        var w = Load("maps/DriftTrack.rpk", render: false);
        if (w is null)
        {
            return;
        }

        var c = w.Collision;
        var pts = Enumerable.Range(0, c.TriangleCount).Where(t => c.Normal(t).Y > 0.5f).Take(5000)
            .Select(t => (c.Vertex(t, 0) + c.Vertex(t, 1) + c.Vertex(t, 2)) / 3f).ToArray();
        var expected = pts.Select(p => c.HeightAt(p.X, p.Z)).ToArray();
        var bad = 0;
        Parallel.For(0, 8, _ =>
        {
            for (var i = 0; i < pts.Length; i++)
            {
                if (c.HeightAt(pts[i].X, pts[i].Z) != expected[i])
                {
                    Interlocked.Increment(ref bad);
                }
            }
        });
        Assert.Equal(0, bad);
    }

    [Fact]
    public void CollisionRatesMeetTargetsOnCity()
    {
        var w = Load("maps/city.rpk", render: false);
        if (w is null)
        {
            return;
        }

        var c = w.Collision;
        var rng = new Random(3);
        var up = Enumerable.Range(0, c.TriangleCount).Where(t => c.Normal(t).Y > 0.5f).ToArray();
        var pts = Enumerable.Range(0, 200_000).Select(_ => { var t = up[rng.Next(up.Length)]; return (c.Vertex(t, 0) + c.Vertex(t, 1) + c.Vertex(t, 2)) / 3f; }).ToArray();
        c.TryGetGround(pts[0].X, pts[0].Z, float.PositiveInfinity, out _); // warm up
        var sw = Stopwatch.StartNew();
        var hits = 0;
        foreach (var p in pts)
        {
            if (c.TryGetGround(p.X, p.Z, float.PositiveInfinity, out _))
            {
                hits++;
            }
        }

        var rate = pts.Length / sw.Elapsed.TotalSeconds;
        Assert.Equal(pts.Length, hits);
        // target 1e6/s (measured 0.7-1.3 M/s single thread on a loaded 4 core box); assert a conservative floor
        Assert.True(rate > 150_000, $"{rate:0} q/s");
    }
}
