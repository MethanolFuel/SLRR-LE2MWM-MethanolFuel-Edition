using Xunit;

namespace Slrr.Game.Tests;

/// <summary>
/// The scripted smoke path on the shipped data, asserting state through the VM after every step:
/// main menu -> new career -> garage -> used car dealer -> catalog (buy part) -> install -> leave garage, then
/// a save/load round trip in a second host. Passes vacuously without game data.
/// </summary>
public class SmokePathTests
{
    private const string BoughtPart = "swaybar_Street_front";

    [Fact]
    public void MainMenuNewCareerGarageBuyPartInstallLeaveAndReload()
    {
        if (GameRoot.Find() is not { } root)
        {
            return;
        }

        var save = GameRoot.TempDir("smoke");
        try
        {
            int moneyAfterPart;
            int installedType;
            using (var host = new GameHost(new GameHostOptions { Root = root, SaveRoot = save }))
            {
                (moneyAfterPart, installedType) = PlaySmokePath(host);
            }

            // Leaving the garage autosaved the career (GameLogic.autoSaveQuiet -> save/carrer/<name>-<club>/).
            Assert.True(File.Exists(Path.Combine(save, "carrer", "Bob-1", "main")));
            Assert.True(File.Exists(Path.Combine(save, "carrer", "Bob-1", "PlayerCar0")));

            using var again = new GameHost(new GameHostOptions { Root = root, SaveRoot = save });
            Assert.True(again.Boot());
            Assert.True(again.RunUntil(() => Section(again) == "MainMenu", 30));
            Assert.True(Click(again, "LOAD CAREER"));
            Assert.True(Click(again, "Bob-1"));
            Assert.True(Click(again, "LOAD CAREER"));           // the requester's OK button (top Osd first)
            again.RunFor(10);
            Assert.Equal("Bob", again.View.PlayerName);
            Assert.Equal(moneyAfterPart, again.View.Money);

            // The car file loads through Vehicle.load -> Part.createFromFile -> updatevariables, which stops on the
            // VM gap `float && float` (docs/phase4/game.md, VM gap 1). Until the VM accepts it, the reloaded car is
            // either complete or the load thread died exactly there.
            if (again.View.Car is not null && again.View.InstalledParts.Count > 1)
            {
                Assert.Contains(again.View.InstalledPartTypes, t => t == installedType);
            }
            else
            {
                Assert.Contains(again.ThreadErrors, e => e.Contains("non-integral operands (&&)", StringComparison.Ordinal));
            }
        }
        finally
        {
            Directory.Delete(save, true);
        }
    }

    private static (int Money, int PartType) PlaySmokePath(GameHost host)
    {
        Assert.True(host.Boot());
        Assert.All(host.BootSteps, s => Assert.True(s.Ok, s.Name));

        // main menu
        Assert.True(host.RunUntil(() => Section(host) == "MainMenu", 30));
        Assert.Contains("NEW CAREER", host.Screens.Choices());

        // new career: name, character, garage
        Assert.True(Click(host, "NEW CAREER"));
        Assert.Contains("StringRequesterDialog", host.Screens.Current);
        host.Input.Type("Bob");
        Assert.True(Click(host, "OK"));
        Assert.Contains("PlayerSetupDialog", host.Screens.Current);
        Assert.True(Click(host, "OK"));
        Assert.True(host.RunUntil(() => Section(host) == "Garage", 30));
        Assert.Equal("Bob", host.View.PlayerName);
        Assert.Equal(20000, host.View.Money);                  // GameLogic.INITIAL_PLAYER_MONEY
        Assert.Null(host.View.Car);
        Assert.True(Click(host, "OK"));                          // WELCOME! hint

        // used car dealer
        Assert.True(Click(host, "Buy used cars or sell your car"));
        Assert.True(host.RunUntil(() => Section(host) == "CarMarket", 10));
        host.RunFor(6);
        host.Input.TapKey(InputInjector.KeyCode("Enter"));
        Assert.True(host.RunUntil(() => host.Driver.VisibleStrings().Any(t => t.Contains("Car Dealer", StringComparison.Ordinal)), 10));
        Assert.True(Retry(host, () => host.Input.ClickCommand(host.Input.AxisCode("SELECT"))));
        Assert.True(host.RunUntil(() => host.Screens.Current.Contains("NoYesDialog", StringComparison.Ordinal), 5));
        Assert.True(Click(host, "YES"));
        Assert.True(host.RunUntil(() => Section(host) == "Garage", 30));
        var car = host.View.Car;
        Assert.NotNull(car);
        Assert.Equal("java.game.Vehicle", car!.Class.Name);
        var moneyAfterCar = host.View.Money;
        Assert.InRange(moneyAfterCar, 0, 19999);
        var partsBefore = host.View.InstalledParts.Count;
        Assert.True(partsBefore > 30, $"stock car has {partsBefore} parts");
        Assert.DoesNotContain(host.View.InstalledPartClasses, c => c.Contains(BoughtPart, StringComparison.Ordinal));

        // catalog: buy the anti-roll bar
        Assert.True(Click(host, "Browse the Catalog"));
        Assert.True(host.RunUntil(() => Section(host) == "Catalog", 10));
        Assert.True(Click(host, "RUNNING GEAR"));
        Assert.True(Click(host, "OTHERS"));
        Assert.True(Retry(host, () => host.Input.Click("Street front 25000", contains: true)));
        Assert.True(host.RunUntil(() => host.Screens.Current.Contains("BuyCatalogItemDialog", StringComparison.Ordinal), 5));
        Assert.Contains(host.Driver.VisibleStrings(), t => t.Contains("installable right away", StringComparison.Ordinal));
        Assert.True(Click(host, "YES"));
        host.RunFor(0.5f);
        var price = moneyAfterCar - host.View.Money;
        Assert.Equal(293, price);                                // catalog label "$293"
        Assert.Equal(1, host.View.PartsBinCount);

        // back to the garage, mechanic mode, drag the item onto the car
        Assert.True(Retry(host, () => host.Input.ClickCommand(0)));   // Catalog CMD_EXIT
        Assert.True(host.RunUntil(() => Section(host) == "Garage", 15));
        host.RunFor(3);
        Assert.True(Click(host, "Install/Remove parts"));
        Assert.True(Retry(host, () => host.Input.DropOnto("Street front", Probe.Id(host.View.Chassis))));
        host.RunFor(1);
        var installed = host.View.InstalledParts.FirstOrDefault(p => p.Object.Class.Name.Contains(BoughtPart, StringComparison.Ordinal));
        Assert.NotNull(installed);
        Assert.Equal(partsBefore + 1, host.View.InstalledParts.Count);
        Assert.NotEqual(0, installed!.TypeId);
        Assert.Equal(host.View.Money, moneyAfterCar - price);   // installing in the career garage is free of charge
        Assert.Contains(host.Natives.PartCommands, c => c.Text.StartsWith("install 0 ", StringComparison.Ordinal) && c.Target == Probe.Id(installed.Object));

        // leave the garage
        Assert.True(Click(host, "Go back to Main Menu"));
        Assert.True(host.RunUntil(() => Section(host) == "MainMenu", 15));
        Assert.Empty(host.Missing);
        return (host.View.Money, installed.TypeId);
    }

    private static string? Section(GameHost h) => h.ActiveSection is { } a ? ScreenTracker.Short(a.Class.Name) : null;

    private static bool Click(GameHost h, string label)
    {
        var ok = Retry(h, () => h.Input.Click(label));
        h.RunFor(0.25f);
        return ok;
    }

    private static bool Retry(GameHost h, Func<bool> attempt, float seconds = 3f)
    {
        var end = h.Vm.Time + seconds;
        while (!attempt())
        {
            if (h.Vm.Time >= end)
            {
                return false;
            }

            h.RunFrame();
        }

        return true;
    }

    [Fact]
    public void EmbeddedSmokeScriptPassesThroughThePlayRunner()
    {
        if (GameRoot.Find() is not { } root)
        {
            return;
        }

        using var host = new GameHost(new GameHostOptions { Root = root });
        Assert.True(host.Boot());
        var runner = new PlayRunner(host);
        runner.Run(PlayScript.Parse(PlayCommand.SmokeScript()));
        Assert.All(runner.Results, r => Assert.True(r.Ok, $"L{r.Step.Line} {r.Step.Text}: {r.Detail}"));
        Assert.Contains(host.Screens.Visits, v => v.Section == "Catalog");
        Assert.Contains(host.Screens.Visits, v => v.Section == "CarMarket");
    }

    [Fact]
    public void WithoutHostNativesTheGarageNeverFinishesLoading()
    {
        if (GameRoot.Find() is not { } root)
        {
            return;
        }

        using var host = new GameHost(new GameHostOptions { Root = root, HostNatives = false, ScriptShims = false, PlaceholderDyno = false });
        Assert.True(host.Boot());
        var runner = new PlayRunner(host) { StopOnFailure = true, ClickTimeout = 1f };
        runner.Run(PlayScript.Parse(PlayCommand.SmokeScript()));
        Assert.True(runner.Failed);
        Assert.Contains(host.ThreadErrors, e => e.Contains("Button.setToolTip", StringComparison.Ordinal));
        Assert.Contains(host.Missing, m => m.Key.Name == "nplay");
    }

    [Fact]
    public void MainMenuTrackSelectionIsReachedAndListsTheMaps()
    {
        if (GameRoot.Find() is not { } root)
        {
            return;
        }

        using var host = new GameHost(new GameHostOptions { Root = root });
        Assert.True(host.Boot());
        Assert.True(host.RunUntil(() => Section(host) == "MainMenu", 30));
        Assert.True(Click(host, "SELECT TRACK"));
        Assert.True(host.RunUntil(() => Section(host) == "TrackSelect", 10));
        host.RunFor(2);
        var choices = host.Screens.Choices().ToList();
        Assert.Contains("TAKE IT", choices);
        Assert.Contains("Drag Track", choices);
        Assert.Contains("Test Track", choices);
    }

    [Fact]
    public void CmdPlayRunsTheSmokePathAndPrintsATranscript()
    {
        if (GameRoot.Find() is not { } root)
        {
            return;
        }

        var o = new StringWriter();
        var e = new StringWriter();
        var code = Slrr.Cli.CmdPlay.Run(["--smoke", "--root", root], o, e);
        var text = o.ToString();
        Assert.True(code == 0, text + e);
        Assert.Contains("steps ok", text, StringComparison.Ordinal);
        Assert.Contains("screens reached:", text, StringComparison.Ordinal);
        Assert.Contains("Catalog > BuyCatalogItemDialog", text, StringComparison.Ordinal);
        Assert.Contains("natives missing (0", text, StringComparison.Ordinal);
    }
}
