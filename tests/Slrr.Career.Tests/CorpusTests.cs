using Xunit;
using Xunit.Abstractions;

namespace Slrr.Career.Tests;

/// <summary>Game data root ($SLRR_GAME_ROOT or the repository checkout); corpus tests pass vacuously without it.</summary>
internal static class GameRoot
{
    private static readonly Lazy<string?> s_root = new(() =>
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
    });

    public static string? Root => s_root.Value;
}

/// <summary>
/// One scripted run per race type on the shipped data, as far as it goes today. Every assertion reads script state
/// (fields of GameLogic.player, the track sections, Roc) or what the scripts put on screen; stand-ins are logged in
/// the transcript (written to the test output).
/// </summary>
public sealed class CorpusTests(ITestOutputHelper output)
{
    private (CareerSession, FlowResult) Run(Func<CareerSession, FlowResult> flow)
    {
        var s = CareerSession.Create(GameRoot.Root!);
        var r = flow(s);
        foreach (var line in s.Transcript)
        {
            output.WriteLine(line);
        }

        output.WriteLine("milestones: " + string.Join(" -> ", r.Milestones));
        output.WriteLine("first unmet dependency: " + (r.FirstBlocker ?? "none"));
        foreach (var (k, v) in r.Numbers)
        {
            output.WriteLine($"  {k} = {v}");
        }

        foreach (var e in s.Host.ThreadErrors)
        {
            output.WriteLine("thread error: " + e);
        }

        return (s, r);
    }

    [Fact]
    public void RaceOfChampionsTwoRunsWonThroughTheShippedCheatCode()
    {
        if (GameRoot.Root is null)
        {
            return;
        }

        var (s, r) = Run(x => CareerFlows.Roc(x, "cheat", 2));
        using (s)
        {
            Assert.Equal("8", r.Numbers["roc-reqs"]); // a fresh career meets only the parts-bin weight requirement
            Assert.True(r.Completed, r.FirstBlocker);
            Assert.True(r.Reached("roc-results-1") && r.Reached("roc-results-2"));
            Assert.Contains(s.Transcript, l => l.Contains("you qualified", StringComparison.Ordinal));
            Assert.Equal(1, Slrr.Game.Probe.Field(Slrr.Game.Probe.Field(s.ActiveSection, "roc").AsObject, "actRound").AsInt);
            Assert.Equal(3999, s.Money); // the entry fee is only charged when the ROC ends (ROCTrack.finishRocRace)
            Assert.Empty(s.Host.ThreadErrors);
        }
    }

    [Fact]
    public void ShippedRocOpponentCarsDoNotLoadWithThisEditionsPartScripts()
    {
        if (GameRoot.Root is null)
        {
            return;
        }

        var (s, r) = Run(x => CareerFlows.Roc(x, "cheat", 1, shippedOpponent: true));
        using (s)
        {
            Assert.False(r.Completed);
            Assert.False(r.Reached("roc-go-1"));
            Assert.Contains(s.Host.ThreadErrors, e => e.Contains("createFromFile", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void CityDayRaceAgainstAClubRacerPaysThePrize()
    {
        if (GameRoot.Root is null)
        {
            return;
        }

        var (s, r) = Run(x => CityFlows.Day(x));
        using (s)
        {
            Assert.StartsWith("no ground at the club garage exit", r.FirstBlocker, StringComparison.Ordinal);
            Assert.True(r.Completed, string.Join(" / ", r.Blockers));
            Assert.True(r.Reached("racer-talk") || r.Reached("race-setup"));
            Assert.True(r.Reached("countdown-go") && r.Reached("finish") && r.Reached("results") && r.Reached("garage-return"));
            var prize = int.Parse(r.Numbers["day-prize"], System.Globalization.CultureInfo.InvariantCulture);
            Assert.True(prize > 0);
            Assert.Equal(int.Parse(r.Numbers["day-money-before"], System.Globalization.CultureInfo.InvariantCulture) + prize,
                int.Parse(r.Numbers["day-money-after"], System.Globalization.CultureInfo.InvariantCulture));
            Assert.Equal(1, s.RacesWon);
        }
    }

    [Fact]
    public void CityNightDragRacePaysThePrize()
    {
        if (GameRoot.Root is null)
        {
            return;
        }

        var (s, r) = Run(x => CityFlows.Night(x));
        using (s)
        {
            Assert.True(r.Completed, string.Join(" / ", r.Blockers));
            Assert.True(r.Reached("opponent-list") && r.Reached("finish") && r.Reached("results") && r.Reached("garage-return"));
            Assert.True(int.Parse(r.Numbers["night-money-after"], System.Globalization.CultureInfo.InvariantCulture)
                        > int.Parse(r.Numbers["night-money-before"], System.Globalization.CultureInfo.InvariantCulture));
            Assert.Equal(2, s.Club);
        }
    }

    [Fact]
    public void ACrashReachesCityAsEventCollisionAndRaisesTheRepairQuote()
    {
        if (GameRoot.Root is null)
        {
            return;
        }

        var (s, r) = Run(x => DamageFlow.Run(x));
        using (s)
        {
            Assert.True(r.Reached("script-collision"), string.Join(" / ", r.Blockers));
            Assert.True(r.Reached("repair-screen-shows-damage"), string.Join(" / ", r.Blockers));
            Assert.True(r.Completed);
        }
    }
}
