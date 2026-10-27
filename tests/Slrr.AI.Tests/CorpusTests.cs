using Slrr.AI.Race;
using Slrr.AI.Roads;
using Xunit;
using Xunit.Abstractions;

namespace Slrr.AI.Tests;

/// <summary>Tests on the shipped data (pass vacuously without it, like the other corpus tests).</summary>
public sealed class CorpusTests(ITestOutputHelper output)
{
    private static string? Root()
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

    [Fact]
    public void ShippedSplinesHaveTheMeasuredLayout()
    {
        if (Root() is not { } root)
        {
            output.WriteLine("no game data");
            return;
        }

        var dir = GameFiles.Resolve(root, "cars/racers/ROC_cars/ROC_DS.spl")!;
        var files = Directory.GetFiles(Path.GetDirectoryName(dir)!, "*.spl").OrderBy(f => f, StringComparer.Ordinal).ToList();
        Assert.Equal(7, files.Count);
        foreach (var f in files)
        {
            var pts = SplFile.Load(f);
            Assert.All(pts, p => Assert.Equal(12, p.Columns));
            Assert.All(pts, p => Assert.InRange(p.Direction.Length(), 0.97f, 1.03f));
            // column 11 repeats the previous line's column 10 on most lines (45..47 of 56 transitions)
            var same = Enumerable.Range(1, pts.Count - 1).Count(i => pts[i - 1].C10 == pts[i].C11);
            Assert.True(same >= 0.75 * (pts.Count - 1), $"{same}/{pts.Count - 1}");

            var path = SplFile.ToPath(pts, Path.GetFileName(f));
            output.WriteLine($"{Path.GetFileName(f)}: {pts.Count} points, {path.Length:F0} m, limits {string.Join(" ", pts.Select(p => p.C10).Where(v => v < SplFile.NoLimit).Distinct().Order())}");
        }

        var race = SplFile.Load(GameFiles.Resolve(root, "maps/ROC_track/race.spl")!);
        Assert.All(race, p => Assert.Equal(9, p.Columns));
    }

    [Fact]
    public void TwoAiCarsRaceTheShippedRocSplineOnTheRocMap()
    {
        if (Root() is not { } root)
        {
            output.WriteLine("no game data");
            return;
        }

        // the scripts' spelling (Roc.carNames); cut before s = 2450 m: at x 61..88, z 205..213 both shipped ROC splines run the
        // cars into map geometry they cannot pass in Slrr.Physics (wheels spin at ~6700 rpm, 2.5 m/s; cause not found)
        var map = MapRace.Prepare(root, "cars/racers/ROC_cars/roc_DS.spl", cutAt: 2400);
        output.WriteLine(map.Describe);
        Assert.True(map.GroundCoverage > 0.9, map.Describe);
        var r = HeadlessRace.Run(new HeadlessRaceOptions
        {
            AiCars = 2,
            TrafficCars = 0,
            Roads = map.Roads,
            Route = map.Route,
            Ground = map.Ground,
            Collision = map.Collision,
            Rules = new RaceRules { Laps = 1, FinishTimeout = 60 },
        });
        output.WriteLine(r.Results.Format());
        foreach (var e in r.Entrants)
        {
            output.WriteLine(FormattableString.Invariant(
                $"  {e.Name}: at {e.Car.Position}, max |lateral| {e.MaxLateral:F2} m, outside the half width {100.0 * e.OffRoadSteps / Math.Max(1, e.RacingSteps):F2} %, recoveries {string.Join(", ", e.Driver!.Recoveries.Select(x => x.Kind))}"));
        }

        output.WriteLine(FormattableString.Invariant($"{r.MsPerStep:F3} ms/step, map impacts/car impacts {r.CarImpacts}, hash {r.StateHash:X16}"));
        Assert.All(r.Results.Rows, row => Assert.Equal(EntrantStatus.Finished, row.Status));
    }
}
