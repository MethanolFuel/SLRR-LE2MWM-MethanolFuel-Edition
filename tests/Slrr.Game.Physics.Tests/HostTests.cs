using Xunit;
using Xunit.Abstractions;

namespace Slrr.Game.Physics.Tests;

/// <summary>The adapter inside the real host (vacuous without the game data).</summary>
public class HostTests(ITestOutputHelper output)
{
    [Fact]
    public void SmokePathStillPassesWithThePhysicsAdapterAndBindsTheDealerCars()
    {
        if (GameRoot.Root is not { } root)
        {
            return;
        }

        var physics = new GamePhysicsWorld();
        using var host = new GameHost(new GameHostOptions { Root = root, Physics = physics, ExtraModules = [new PhysicsNatives(physics)] });
        Assert.True(host.Boot());
        var runner = new PlayRunner(host);
        runner.Run(PlayScript.Parse(PlayCommand.SmokeScript()));
        foreach (var l in physics.Log.Take(30))
        {
            output.WriteLine(l);
        }

        foreach (var c in physics.CommandLog.Take(30))
        {
            output.WriteLine($"cmd {c.Target}: {c.Text} -> {(c.Consumed ? "consumed" : "queued")}");
        }

        output.WriteLine($"{physics.CommandLog.Count(c => c.Consumed)} car verbs consumed, {physics.Log.Count(l => l.StartsWith("rebuilt", StringComparison.Ordinal))} rebuilds");
        output.WriteLine($"{physics.Scene.AllCars.Count} cars bound, {physics.Scene.Steps} steps, missing natives: {string.Join(", ", host.Missing.Select(m => m.Key.ToString()).Take(10))}");
        Assert.All(runner.Results, r => Assert.True(r.Ok, $"L{r.Step.Line} {r.Step.Text}: {r.Detail}"));
        Assert.DoesNotContain(host.Missing, m => m.Key.ClassName == PhysicsNatives.VehicleClass);

        // The dealer sends reset/setsteer/stop/start to its showroom cars (CarMarket.class): they are bound and,
        // without a map, held where the scripts put them.
        Assert.Contains(physics.CommandLog, c => c.Consumed && c.Text.StartsWith("reset", StringComparison.Ordinal));
        Assert.NotEmpty(physics.Scene.AllCars);
        Assert.All(physics.Scene.AllCars, c => Assert.True(double.IsFinite(c.Position.X) && double.IsFinite(c.Position.Y)));
        Assert.True(physics.Scene.Steps > 100, $"{physics.Scene.Steps} fixed steps");
    }
}
