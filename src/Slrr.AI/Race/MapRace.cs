using System.Globalization;
using System.Numerics;
using Slrr.AI.Roads;
using Slrr.Game.Physics;
using Slrr.World;
using PhysGround = Slrr.Physics.IGroundQuery;

namespace Slrr.AI.Race;

/// <summary>How well a route lies on a map: ground under it and walls across it.</summary>
/// <param name="GroundCoverage">Fraction of 5 m samples with ground within 3 m of the route height.</param>
/// <param name="WallHits">5 m route pieces crossed by map geometry 1 m above the route (walls, barriers, buildings).</param>
public sealed record RouteFit(float TangentScale, double GroundCoverage, double MeanHeightError, int WallHits, string Gaps);

/// <summary>
/// A race route fitted from shipped data until the Roads area's graph exists: a shipped AI spline (<c>.spl</c>) as the
/// route on its shipped map (ground + collision from Slrr.World). The spline↔map pairing is MEASURED from the scripts
/// (ROCTrack loads <c>maps.ROC_track</c> and drives its bot on <c>cars/racers/ROC_cars/*.spl</c>). The Hermite tangent
/// scale of the spline segments is FITTED here against the map (fewest walls crossed, most ground under it), because the
/// exe's segment routine (0x57de20) was not traced: with chord-length tangents the long ROC segments bulge through walls.
/// </summary>
public sealed record MapRace(PathSetRoadSource Roads, IRoadPath Route, PhysGround Ground, IStaticCollision Collision, StaticWorld World,
    RouteFit Fit, IReadOnlyList<RouteFit> Tried, string Describe)
{
    public static readonly float[] TangentScales = [1f, 0.5f, 0.33f, 0.2f];

    public double GroundCoverage => Fit.GroundCoverage;

    public static MapRace Prepare(string root, string splFile, string? map = null, bool objectCollision = true, float? cutAt = null)
    {
        map ??= "maps/ROC_track.rpk";
        var splPath = GameFiles.Resolve(root, splFile) ?? (File.Exists(splFile) ? splFile : throw new FileNotFoundException(splFile));
        var mapPath = GameFiles.Resolve(root, map) ?? throw new FileNotFoundException(map);
        var world = WorldLoader.Load(mapPath, new WorldLoadOptions { GameRoot = root, BuildRenderScene = false, IncludeObjectCollision = objectCollision });
        var points = SplFile.Load(splPath);
        var tried = new List<(SampledPath Path, RouteFit Fit)>();
        foreach (var k in TangentScales)
        {
            var path = SplFile.ToPath(points, Path.GetFileName(splPath), tangentScale: k);
            tried.Add((path, Evaluate(world.Ground, path, k)));
        }

        var best = tried.OrderBy(t => t.Fit.WallHits).ThenByDescending(t => t.Fit.GroundCoverage).ThenBy(t => Array.IndexOf(TangentScales, t.Fit.TangentScale)).First();
        IRoadPath route = cutAt is { } cut && cut < best.Path.Length ? new SubPath(best.Path, 0, cut, $"{best.Path.Name}[0..{cut:F0}]") : best.Path;
        var f = best.Fit;
        var roads = new PathSetRoadSource().AddRoute(route);
        var text = string.Create(CultureInfo.InvariantCulture,
            $"route {route.Name}: {route.Length:F0} m on {Path.GetFileName(mapPath)}, tangent scale {f.TangentScale} (tried {string.Join(", ", tried.Select(t => $"{t.Fit.TangentScale}: {t.Fit.WallHits} walls, {t.Fit.GroundCoverage * 100:F1} % ground"))}); ground under {f.GroundCoverage * 100:F1} % of the 5 m samples (mean |dy| {f.MeanHeightError:F2} m), no ground within 3 m at s = {f.Gaps}");
        return new MapRace(roads, route, new WorldGround(world.Ground), new CollisionWorldShapes(world.Collision), world, f, tried.Select(t => t.Fit).ToList(), text);
    }

    public static RouteFit Evaluate(Slrr.World.IGroundQuery ground, IRoadPath route, float tangentScale)
    {
        int n = 0, hit = 0, walls = 0;
        double err = 0;
        var gaps = new List<string>();
        var gapStart = -1f;
        for (var s = 0f; s < route.Length; s += 5f)
        {
            var p = route.Sample(s).Position;
            n++;
            if (ground.TryGetGround(p.X, p.Z, p.Y + 3f, out var g) && MathF.Abs(g.Position.Y - p.Y) < 3f)
            {
                hit++;
                err += Math.Abs(g.Position.Y - p.Y);
                if (gapStart >= 0)
                {
                    gaps.Add(string.Create(CultureInfo.InvariantCulture, $"{gapStart:F0}-{s:F0}"));
                    gapStart = -1;
                }
            }
            else if (gapStart < 0)
            {
                gapStart = s;
            }

            var q = route.Sample(s + 5f).Position;
            var up = new Vector3(0, 1.0f, 0);
            var d = q - p;
            if (d.LengthSquared() > 1e-4f && ground.Raycast(p + up, Vector3.Normalize(d), d.Length(), out _))
            {
                walls++;
            }
        }

        if (gapStart >= 0)
        {
            gaps.Add(string.Create(CultureInfo.InvariantCulture, $"{gapStart:F0}-end"));
        }

        return new RouteFit(tangentScale, n == 0 ? 0 : hit / (double)n, hit == 0 ? double.NaN : err / hit, walls, gaps.Count == 0 ? "-" : string.Join(", ", gaps));
    }
}
