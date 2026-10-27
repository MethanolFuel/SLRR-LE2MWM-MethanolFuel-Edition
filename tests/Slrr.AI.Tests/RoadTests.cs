using System.Numerics;
using Slrr.AI.Driving;
using Slrr.AI.Race;
using Slrr.AI.Roads;
using Xunit;

namespace Slrr.AI.Tests;

public sealed class RoadTests
{
    private static SampledPath Circle(float r, bool left)
    {
        // counter-clockwise seen from +Y with forward -Z at the start is a LEFT turn (left = -X)
        var pts = SyntheticRoads.Turtle([SyntheticRoads.Leg.Arc(r, left ? 360 : -360)], Vector3.Zero, 0, 2f);
        pts.RemoveAt(pts.Count - 1);
        return SampledPath.Through(pts, closed: true);
    }

    [Theory]
    [InlineData(40f, true)]
    [InlineData(40f, false)]
    [InlineData(150f, true)]
    public void CircleHasSignedCurvatureOneOverRadius(float r, bool left)
    {
        var c = Circle(r, left);
        Assert.InRange(c.Length, 2 * MathF.PI * r * 0.995f, 2 * MathF.PI * r * 1.005f);
        for (var s = 0f; s < c.Length; s += 7.3f)
        {
            var k = c.Sample(s).Curvature;
            Assert.InRange(k * (left ? 1 : -1), 0.97f / r, 1.03f / r);
        }
    }

    [Fact]
    public void ProjectInvertsSampleAndWrapsOnLoops()
    {
        var c = SyntheticRoads.RaceCircuit();
        Assert.InRange(c.Length, 1500, 1700);
        Assert.True(Vector3.Distance(c.Sample(0).Position, c.Sample(c.Length).Position) < 0.01f);
        for (var s = 0f; s < c.Length; s += 37f)
        {
            var p = c.Sample(s).Position + (RoadMath.LeftOf(c.Sample(s).Tangent) * 2f);
            var back = c.Project(p, s, 20f);
            var diff = Math.Abs(back - s);
            Assert.True(Math.Min(diff, c.Length - diff) < 0.6f, $"{s} -> {back}");
        }

        Assert.True(Vector3.Distance(c.Sample(-10).Position, c.Sample(c.Length - 10).Position) < 0.01f);
    }

    [Fact]
    public void RouteAlongALoopWrapsPastTheStart()
    {
        var src = SyntheticRoads.World(out var circuit);
        var from = circuit.Sample(circuit.Length - 100).Position;
        var to = circuit.Sample(200).Position;
        var route = src.Route(from, to)!;
        Assert.InRange(route.Length, 299, 301);
        Assert.True(Vector3.Distance(route.Sample(150).Position, circuit.Sample(50).Position) < 0.5f);
        Assert.Null(src.Route(from, new Vector3(5000, 0, 5000)));
    }

    [Fact]
    public void GridFollowsTheCityLayout()
    {
        var src = SyntheticRoads.World(out var circuit);
        var grid = ((IRoadSource)src).StartGrid(circuit, 4);
        // MEASURED: two columns 1.75 m left and right of the start, facing along the route (City.startRace)
        Assert.Equal(3.5f, Vector3.Distance(grid[0].Position, grid[1].Position), 0.01f);
        Assert.Equal(RoadMath.YawOf(circuit.Sample(0).Tangent), grid[0].Yaw, 0.001f);
        Assert.Equal(8f, Vector3.Distance(grid[0].Position, grid[2].Position), 0.1f);
    }

    [Fact]
    public void SplineFormatDefaultsAndColumns()
    {
        var pts = SplFile.Parse("1 2 3 0 0 -1\t7.0\t100.00\t0\t150.0\t200.0\t1\n0 0 -10 0 0 -1 5 1.35\nbad line\n");
        Assert.Equal(2, pts.Count);
        Assert.Equal(12, pts[0].Columns);
        Assert.Equal(150.0, pts[0].C10);
        Assert.Equal(8, pts[1].Columns);
        Assert.Equal(SplFile.NoLimit, pts[1].C10);
        var path = SplFile.ToPath(pts, "t", useSpeedLimits: true);
        Assert.Equal(150 / 3.6f, path.Sample(1).SpeedLimit, 0.001f);
    }

    [Fact]
    public void AiLevelTransformIsTheExes()
    {
        Assert.Equal(0.25, AiLevel.Stored(-3));
        Assert.Equal(0.5, AiLevel.Stored(0.5));
        Assert.Equal(0.75, AiLevel.Stored(1.0));
        Assert.Equal(0.75, AiLevel.Stored(2.0));
        Assert.Equal(0.6, AiLevel.Stored(0.7), 6);
    }

    [Fact]
    public void SpeedProfileBrakesBeforeCorners()
    {
        var c = SyntheticRoads.RaceCircuit();
        var p = new SpeedProfile(c, 8, 6, 60, false);
        // R 30 hairpin after the first 150 m: sqrt(8 * 30) = 15.5 m/s; 50 m before it at most sqrt(15.5² + 2*6*50)
        Assert.InRange(p.At(150 + 23), 14, 16.2);
        Assert.True(p.At(100) <= Math.Sqrt((16.2 * 16.2) + (2 * 6 * 50)) + 0.5);
        Assert.InRange(p.Min, 13, 16.2);
    }

    [Fact]
    public void ResultsUseTheDialogsNoTimeSentinel()
    {
        var r = new RaceResults("x", 1, 100, [
            new RaceResultRow(1, "a", 1, EntrantStatus.Finished, null, 61.25, [61.25], 61.25, 0, 100, 0),
            new RaceResultRow(2, "b", 2, EntrantStatus.Dnf, "finish timeout", null, [], null, null, 50, 0)]);
        var d = r.DialogFor("b", "a");
        Assert.Equal("a", d.WinnerName);
        Assert.Equal(61.25f, d.WinnerTime);
        Assert.Equal(RaceResults.NoTime, d.LoserTime);
        Assert.Equal("1:01.25", RaceResults.FormatTime(61.25));
        Assert.Equal("--:--:--", RaceResults.FormatTime(null));
    }

    [Fact]
    public void CaseInsensitiveGameFileLookup()
    {
        var dir = Path.Combine(Path.GetTempPath(), "slrr-ai-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(dir, "cars", "Racers"));
        File.WriteAllText(Path.Combine(dir, "cars", "Racers", "ROC_SD.spl"), "x");
        try
        {
            Assert.Equal(Path.Combine(dir, "cars", "Racers", "ROC_SD.spl"), GameFiles.Resolve(dir, "cars/racers/roc_SD.spl"));
            Assert.Null(GameFiles.Resolve(dir, "cars/racers/none.spl"));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }
}
