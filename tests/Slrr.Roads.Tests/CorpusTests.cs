using System.Numerics;
using Slrr.Cli;
using Slrr.Roads;
using Slrr.World;
using Xunit;
using static Slrr.Roads.Tests.Approx;
using Xunit.Abstractions;

namespace Slrr.Roads.Tests;

/// <summary>Game data for corpus tests: $SLRR_GAME_ROOT or the checkout (tests pass vacuously without it).</summary>
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
            if (File.Exists(Path.Combine(dir.FullName, "Slrr.sln")) && File.Exists(Path.Combine(dir.FullName, "sl.rpk")))
            {
                return dir.FullName;
            }
        }

        return null;
    }

    public static string? Data(params string[] rel) =>
        Find() is { } r && System.IO.File.Exists(Path.Combine([r, .. rel])) ? Path.Combine([r, .. rel]) : null;
}

/// <summary>The city world (city.rpk + its EXTP extension) and road graph, loaded once for the corpus tests.</summary>
public sealed class CityFixture : IDisposable
{
    public CityFixture()
    {
        Root = GameRoot.Find();
        if (Root is null || GameRoot.Data("maps", "city.rpk") is not { } map)
        {
            return;
        }

        World = WorldLoader.Load(map, new WorldLoadOptions { GameRoot = Root, IncludeObjectCollision = false, BuildRenderScene = false });
        Graph = new RoadGraphCatalog(Root).ForArchive(World.Archive);
    }

    public string? Root { get; }

    public StaticWorld? World { get; private set; }

    public RoadGraph? Graph { get; private set; }

    public void Dispose()
    {
        World = null;
        Graph = null;
        GC.Collect();
    }
}

public class CorpusTests(CityFixture city, ITestOutputHelper output) : IClassFixture<CityFixture>
{
    // Valocity.posGarage[0..2] (MEASURED from the decompiled class; y as the script writes it)
    public static readonly Vector3[] Garages = [new(-278.518f, 9.8f, 1033.002f), new(355.381f, 1.6f, 418.244f), new(-531.138f, 5.05f, -149.357f)];

    [Fact]
    public void CityTrcParsesExactlyAndPassesTheStructuralChecks()
    {
        if (GameRoot.Data("maps", "city", "ai", "city.trc") is not { } path)
        {
            return;
        }

        var bytes = File.ReadAllBytes(path);
        var f = TrcFile.Read(bytes);
        Assert.Equal(843_301, bytes.Length);
        Assert.Equal(53, f.NodeTableA);
        Assert.Equal(271, f.Nodes.Count);
        Assert.Equal(484, f.Roads.Count);
        Assert.Equal(1572, f.Lanes.Count);
        Assert.Equal(3854, f.Nodes.Sum(n => n.Connectors.Count));
        Assert.Equal(756, f.FirstLaneId);
        Assert.Equal(bytes, f.Write());

        var o = new StringWriter();
        var rc = CmdRoads.Run(["verify", path], o, o);
        output.WriteLine(o.ToString());
        Assert.Equal(0, rc);
    }

    [Fact]
    public void TheCityMapNamesItsRoadGraph()
    {
        if (city.World is null)
        {
            return;
        }

        Assert.Equal(@"maps\city\ai\city.trc", RoadGraphCatalog.TrcPathOf(city.World.Archive));
        Assert.NotNull(city.Graph);
        foreach (var other in new[] { "ROC_track.rpk", "test_track.rpk", "DriftTrack.rpk", "city2.rpk" })
        {
            if (GameRoot.Data("maps", other) is { } p)
            {
                Assert.Null(RoadGraphCatalog.TrcPathOf(Slrr.Formats.Rpak.RpakArchive.Read(p)));
            }
        }
    }

    [Fact]
    public void ValoCityGaragesHaveGroundThroughTheExtpExtension()
    {
        if (city.World is not { } w)
        {
            return;
        }

        Assert.Equal([@"maps\city2.rpk"], w.Extensions.Select(x => x.Name));
        Assert.Equal(108, w.Extensions[0].SurfaceOffset);
        foreach (var gpos in Garages)
        {
            Assert.True(w.Ground.TryGetGround(gpos.X, gpos.Z, gpos.Y + 2, out var hit), $"no ground at {gpos}");
            output.WriteLine($"garage {gpos}: ground {hit.Position.Y:0.000} ({w.Surfaces.Get(hit.Surface).Name})");
            Assert.True(MathF.Abs(hit.Position.Y - gpos.Y) < 0.02f, $"{gpos}: ground {hit.Position.Y}");
        }

        // the two northern garages stand on city2.rpk's ground (surface ids after city.scm's 108), the southern one on city.rpk's
        // (a load of city.rpk alone finds no ground at garages 0 and 1: `world height maps/city.rpk -278.5 1033` before the fix)
        int Src(Vector3 p) => w.Ground.TryGetGround(p.X, p.Z, p.Y + 2, out var h) ? h.Surface : -1;
        Assert.True(Src(Garages[0]) >= 108);
        Assert.True(Src(Garages[1]) >= 108);
        Assert.InRange(Src(Garages[2]), 0, 107);
    }

    [Fact]
    public void LanesLieOnTheMergedCityGround()
    {
        if (city.World is not { } w || city.Graph is not { } g)
        {
            return;
        }

        int n = 0, ground = 0, within = 0;
        var errs = new List<float>();
        foreach (var l in g.Lanes)
        {
            for (var s = 0f; s <= l.Curve.Length; s += 10)
            {
                var p = l.Curve.PointAt(s);
                n++;
                if (w.Ground.TryGetGround(p.X, p.Z, p.Y + 2, out var hit))
                {
                    ground++;
                    errs.Add(MathF.Abs(hit.Position.Y - p.Y));
                    within += MathF.Abs(hit.Position.Y - p.Y) < 0.5f ? 1 : 0;
                }
            }
        }

        errs.Sort();
        output.WriteLine($"lane samples {n}: ground below {ground}, within 0.5 m {within}, median |dy| {errs[errs.Count / 2]:0.000}, p90 {errs[errs.Count * 9 / 10]:0.000}");
        Assert.True(ground > n * 0.95, $"{ground}/{n}");
        Assert.True(within > ground * 0.9, $"{within}/{ground}");
    }

    [Fact]
    public void RoadQueriesAnswerAcrossTheCity()
    {
        if (city.Graph is not { } g)
        {
            return;
        }

        foreach (var gpos in Garages)
        {
            var a = g.AlignToRoad(gpos);
            output.WriteLine($"alignToRoad {gpos}: {a?.Position} dir {a?.Direction}; nearest cross {g.NearestCross(gpos, 0)?.Position}");
            Assert.NotNull(a);
            Near(1f, a.Value.Direction.Length(), 1e-3f);
            Assert.True(Vector3.Distance(a.Value.Position, gpos) < 60);
        }

        var r = g.FindRoute(Garages[0], Garages[1]);
        Assert.NotNull(r);
        var straight = Vector3.Distance(Garages[0], Garages[1]);
        output.WriteLine($"route garage0 -> garage1: {r.Length:0.0} m (straight {straight:0.0} m), {r.Legs.Count} legs");
        Assert.True(r.Length > straight * 0.9f && r.Length < straight * 4);
        Assert.NotNull(g.StartDirection(Garages[0], Garages[1]));
        for (var s = 0f; s < r.Length; s += 5)
        {
            var a = r.Sample(s);
            var b = r.Sample(MathF.Min(r.Length, s + 5));
            Assert.True(Vector3.Distance(a.Position, b.Position) < 5.5f, $"jump at {s}");
        }

        // every lane reaches at least one other lane (the graph is drivable)
        Assert.True(g.Lanes.Count(l => l.Exits.Count > 0) > g.Lanes.Count * 0.95);
    }

    [Fact]
    public void ShippedSplinesParse()
    {
        if (GameRoot.Data("maps", "ROC_track", "race.spl") is not { } race)
        {
            return;
        }

        var s = SplineFile.Read(race);
        Assert.Equal(52, s.Points.Count);
        Assert.All(s.Points, p => Assert.Equal(3, p.Values.Length));
        var t = SplineFile.Read(GameRoot.Data("maps", "test_track", "test.spl")!);
        Assert.Equal(2, t.Points.Count);
        Near(4000f, t.Length, 1e-1f);
        foreach (var f in Directory.GetFiles(Path.Combine(GameRoot.Find()!, "cars", "racers", "ROC_cars"), "*.spl"))
        {
            var r = SplineFile.Read(f);
            Assert.Equal(57, r.Points.Count);
            Assert.All(r.Points, p => Assert.Equal(6, p.Values.Length));
        }
    }
}
