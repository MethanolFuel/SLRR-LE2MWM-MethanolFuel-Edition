using Slrr.Cli;
using Xunit;
using Xunit.Abstractions;

namespace Slrr.Engine.Commands.Tests;

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
}

/// <summary>
/// The shipped race scripts driven headlessly through the game host with the command engine and the kinematic stand-in
/// cars (no physics adapter). Every number asserted here is computed by the scripts themselves from the triggers and
/// vehicle state this project provides; none is a parity claim against the original game.
/// </summary>
public class CorpusTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData("Test Track", "TestTrack")]
    [InlineData("Drag Track", "Drag_Track")]
    [InlineData("Valo City", "Valocity")]
    public void TrackEntryCommandsAreAllConsumed(string track, string section)
    {
        if (GameRoot.Find() is not { } root)
        {
            return;
        }

        using var s = new RaceSession(root);
        Assert.True(s.EnterTrack(track));
        Assert.Equal(section, s.Section);
        s.Host.RunFor(5);
        var e = s.Engine;
        foreach (var l in e.Describe().Where(l => !l.Contains("'chassis'", StringComparison.Ordinal)))
        {
            output.WriteLine(l);
        }

        Assert.All(e.Stats, kv => Assert.True(kv.Value.Rejected == 0, kv.Key));
        Assert.True(e.Stats.Values.Sum(v => v.Consumed) >= 30);

        // The follow camera: GII_OWNER answered → "viewport"/"camera" reach the player's controller → viewport 0 shows a
        // camera that follows the player's car.
        var controller = e.Race.Controllers.Single(c => c.Kind == "player");
        Assert.Equal(s.PlayerCarId, controller.Controlled);
        Assert.NotEqual(0, controller.Camera);
        Assert.Equal(controller.Camera, e.Cameras.ViewFor(0)!.Binding.SourceId);
        Assert.DoesNotContain(s.Host.Missing, m => m.Key.Name is "getSpeedSquare" or "updateNavigator" or "length");
        Assert.Empty(s.Host.ThreadErrors);
    }

    [Fact]
    public void TestTrackAccelerationRoundIsTimedByTheScripts()
    {
        if (GameRoot.Find() is not { } root)
        {
            return;
        }

        (float Q, float M, float A, float Top) Once(out string police)
        {
            using var s = new RaceSession(root);
            Assert.True(s.EnterTrack("Test Track"));
            s.Host.RunFor(16);                                   // TestTrack.enter intro (4 sleeps, 14 s)
            var r = s.RunAccelerationTest();
            var brain = s.Engine.Race.Controllers.Single(c => c.Kind == "bot");
            var horn = s.Engine.Horn(brain.Controlled);
            police = $"{brain.Mode} horn {horn}";
            Assert.Empty(s.Host.ThreadErrors);
            return r;
        }

        var r1 = Once(out var police1);
        output.WriteLine($"quarter {r1.Q} mile {r1.M} 0-100 {r1.A} line speed {r1.Top}; police {police1}");

        // Kinematic stand-in: v' = 5 - 0.08 v. The script starts its clock at v > 0.5 m/s and stops it when its trigger
        // event arrives (sphere radius 20 around z=-1059.785 / 148.21, start at z=-1461.785): 382 m and 1590 m.
        Assert.InRange(r1.Q, Expected(382f) - 0.2f, Expected(382f) + 0.2f);
        Assert.InRange(r1.M, Expected(1590f) - 0.2f, Expected(1590f) + 0.2f);
        Assert.InRange(r1.A, 6.9f, 7.5f);

        // Police_Trigger (r 15 at x=14, z=-1128) lies on the run: the script makes the police bot follow and honk.
        Assert.Equal("Follow horn 1", police1);

        var r2 = Once(out var police2);
        Assert.Equal(r1, r2);
        Assert.Equal(police1, police2);
    }

    /// <summary>Time from v = 0.5 m/s to distance <paramref name="d"/> for v' = a - k v (closed form, bisection).</summary>
    private static float Expected(float d)
    {
        const double a = 5, k = 0.08;
        double X(double t) => a / k * (t - ((1 - Math.Exp(-k * t)) / k));
        double lo = 0, hi = 200;
        for (var i = 0; i < 80; i++)
        {
            var mid = (lo + hi) / 2;
            (lo, hi) = X(mid) < d ? (mid, hi) : (lo, mid);
        }

        var t0 = -Math.Log(1 - (0.5 * k / a)) / k;
        return (float)(lo - t0);
    }

    [Fact]
    public void CmdRaceCoverageListsTheVerbs()
    {
        var o = new StringWriter();
        Assert.Equal(0, CmdRace.Run(["--coverage", "--root", "/nonexistent"], o, TextWriter.Null));
        Assert.Contains("call-site coverage over 526", o.ToString(), StringComparison.Ordinal);
    }
}
