using Slrr.AI.Race;
using Xunit;
using Xunit.Abstractions;

namespace Slrr.AI.Tests;

/// <summary>The headless multi-car proof: 8 AI full cars race 2 laps of the synthetic circuit while 30 traffic cars run on
/// the ring road; everything deterministic.</summary>
public sealed class RaceTests(ITestOutputHelper output)
{
    private static readonly HeadlessRaceOptions Full = new()
    {
        AiCars = 8,
        TrafficCars = 30,
        // City's 10 s after the first finisher is for its two-car races; a field of 8 gets 120 s here (GUESSED)
        Rules = new RaceRules { Laps = 2, FinishTimeout = 120 },
    };

    private void Report(HeadlessRaceResult r)
    {
        output.WriteLine(r.Results.Format());
        output.WriteLine(FormattableString.Invariant(
            $"sim {r.SimSeconds:F1} s, {r.Steps} steps, {r.MsPerStep:F3} ms/step ({r.MaxSceneCars} cars), traffic director {r.TrafficMsPerFrame:F3} ms/frame; traffic spawned {r.TrafficSpawned}, despawned {r.TrafficDespawned}, peak {r.TrafficPeak}, min same-lane gap {r.MinTrafficGap:F1} m; car/car impacts {r.CarImpacts}; hash {r.StateHash:X16}"));
        foreach (var e in r.Entrants)
        {
            output.WriteLine(FormattableString.Invariant(
                $"  {e.Name}: max |lateral| {e.MaxLateral:F2} m, outside the half width {100.0 * e.OffRoadSteps / Math.Max(1, e.RacingSteps):F2} % of steps, recoveries {string.Join(", ", e.Driver!.Recoveries.Select(x => x.Kind))}"));
        }

        foreach (var n in r.Notes)
        {
            output.WriteLine(n);
        }
    }

    [Fact]
    public void EightAiCarsAndThirtyTrafficCarsFinishDeterministically()
    {
        var a = HeadlessRace.Run(Full);
        Report(a);
        Assert.All(a.Results.Rows, row => Assert.Equal(EntrantStatus.Finished, row.Status));
        Assert.All(a.Results.Rows, row => Assert.Equal(2, row.LapTimes.Count));
        // on the road: outside the 7 m half width for under 5 % of the racing steps, every car (the rest: respawns after rolls)
        Assert.All(a.Entrants, e => Assert.True(e.OffRoadSteps < 0.05 * e.RacingSteps, $"{e.Name} off the road {e.OffRoadSteps}/{e.RacingSteps}"));
        Assert.Equal(30, a.TrafficPeak);
        Assert.True(a.TrafficDespawned > 0);
        Assert.True(a.MinTrafficGap > 0.5, $"traffic gap {a.MinTrafficGap:F1}");

        var b = HeadlessRace.Run(Full);
        Assert.Equal(a.StateHash, b.StateHash);
        Assert.Equal(a.Results.Rows.Select(r => r.Time), b.Results.Rows.Select(r => r.Time));
    }

    [Fact]
    public void RubberBandingIsOffByDefaultAndSqueezesTheFieldWhenOn()
    {
        var o = new HeadlessRaceOptions { AiCars = 4, TrafficCars = 0, MeasureCaps = false, Rules = new RaceRules { Laps = 1, FinishTimeout = 120 } };
        var off = HeadlessRace.Run(o);
        var on = HeadlessRace.Run(o with { Rules = o.Rules with { RubberBand = true } });
        double Spread(HeadlessRaceResult r) => r.Results.Rows.Max(x => x.Time ?? 1e9) - r.Results.Rows.Min(x => x.Time ?? 1e9);
        output.WriteLine(off.Results.Format());
        output.WriteLine(on.Results.Format());
        Assert.False(new RaceRules().RubberBand);
        Assert.All(on.Results.Rows, row => Assert.Equal(EntrantStatus.Finished, row.Status));
        Assert.True(Spread(on) < Spread(off), $"spread {Spread(on):F2} vs {Spread(off):F2}");
    }
}
