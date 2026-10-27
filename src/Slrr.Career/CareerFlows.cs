using System.Globalization;
using System.Numerics;
using Slrr.Game;
using Slrr.Race;
using Slrr.Vm;

namespace Slrr.Career;

/// <summary>How a race-type flow ended: the milestones reached in order and the first unmet dependency.</summary>
public sealed class FlowResult(string name)
{
    public string Name { get; } = name;

    /// <summary>Milestones reached (e.g. "garage", "roc-entry-dialog", "race-started", "finish", "results", "payout", "garage-return", "saved").</summary>
    public List<string> Milestones { get; } = [];

    /// <summary>
    /// The first unmet dependency met on the way (null when none), with the area that owns it. The flow may still have
    /// continued past it with a logged stand-in; <see cref="Completed"/> says whether it reached its end.
    /// </summary>
    public string? FirstBlocker { get; set; }

    /// <summary>Every unmet dependency in order (stand-ins replaced some of them).</summary>
    public List<string> Blockers { get; } = [];

    /// <summary>The flow reached its last milestone (garage again and the career saved / the repair quote rose).</summary>
    public bool Completed { get; set; }

    public Dictionary<string, string> Numbers { get; } = new(StringComparer.Ordinal);

    public bool Reached(string milestone) => Milestones.Contains(milestone);

    public void Mark(CareerSession s, string milestone, string? detail = null)
    {
        Milestones.Add(milestone);
        s.Step($"MILESTONE {milestone}{(detail is null ? string.Empty : ": " + detail)}");
    }

    public void Block(CareerSession s, string blocker)
    {
        FirstBlocker ??= blocker;
        Blockers.Add(blocker);
        s.Step("BLOCKED: " + blocker);
    }

    public void Num(string key, FormattableString value) => Numbers[key] = value.ToString(CultureInfo.InvariantCulture);
}

/// <summary>
/// The career-side race flows, driven through the shipped scripts: boot → new career → used car (embedded smoke play
/// script, as Slrr.Race) → the race type's menus → race → results → payout → garage → save. Stand-ins (clearly logged
/// STAND-IN / TEST-ONLY / CHEAT in the transcript) replace what the road graph and the AI do not provide yet.
/// </summary>
public static class CareerFlows
{
    /// <summary>Boot, new career and the first used car (the shared prefix of every flow).</summary>
    public static bool BootAndBuy(CareerSession s, FlowResult r)
    {
        var scenario = new RaceScenario(s.Race, new RaceScenarioOptions());
        var ok = scenario.BootAndBuy();
        foreach (var line in scenario.Result.Steps)
        {
            s.Step("race-wave: " + line.Trim());
        }

        if (!ok)
        {
            r.Block(s, $"boot/new career/used car failed ({s.Host.Screens.Current}); Slrr.Game/Slrr.Race");
            return false;
        }

        s.WaitFor(() => s.Section == "Garage" && !s.DialogOnTop, 10, "garage idle");
        r.Mark(s, "garage", s.State());
        r.Num("money-after-purchase", $"{s.Money}");
        return true;
    }

    // ================================================================== ROC

    /// <summary>
    /// Race of Champions: Garage "Go to the Race Of Champions" → ROCEntryDialog (the four requirements as the script
    /// computes them) → entry → Garage in ROC mode → "Go on the next race" → ROCTrack (RaceDialog "Run n of 3", countdown
    /// 3-2-1-GO from the script's timers) → drive from the start trigger to the finish trigger → result dialog → garage.
    /// Entry: <paramref name="entry"/> = "cheat" (the shipped code "letmeroc", <see cref="CareerNatives"/>), "test" (TEST-ONLY
    /// money/prestige plus "ilikecheating" for the car-prestige and rank requirements) or "none" (stop at the dialog).
    /// The opponent: the script creates it (<c>roc.getCurrentCarName()</c>, a saved car) and sends <c>AI_spline</c>, which no
    /// area simulates yet, so it stays at its grid slot (stand-in: an opponent that does not move).
    /// </summary>
    public static FlowResult Roc(CareerSession s, string entry = "cheat", int runs = 2, bool shippedOpponent = false)
    {
        var r = new FlowResult("roc");
        if (!BootAndBuy(s, r))
        {
            return r;
        }

        // ---- entry requirements, as the shipped Garage computes them
        if (!s.Click("Go to the Race Of Champions"))
        {
            r.Block(s, "garage ROC button not found");
            return r;
        }

        if (!s.WaitFor(() => s.TopDialog == "ROCEntryDialog", 5, "ROCEntryDialog"))
        {
            r.Block(s, "ROCEntryDialog did not open");
            return r;
        }

        var reqs = Probe.Field(s.Host.TopOsd is { } osd ? Probe.Field(osd, "globalHandler").AsObject : null, "reqs").AsInt;
        r.Mark(s, "roc-entry-dialog", $"reqs = {reqs} (1 money >= $100000: {(reqs & 1) != 0}, 2 top-5 rank: {(reqs & 2) != 0}, 4 car prestige >= 7.5: {(reqs & 4) != 0}, 8 parts-bin weight: {(reqs & 8) != 0}); texts: {string.Join(" | ", s.TopTexts().Take(8))}");
        r.Num("roc-reqs", $"{reqs}");
        r.Num("car-prestige", $"{CollisionDamageBridge.CallFloat(s.Host, s.Car, "getPrestige")}");
        s.Click("GO BACK TO GARAGE");
        s.WaitFor(() => s.Section == "Garage" && !s.DialogOnTop, 5, "garage");

        switch (entry)
        {
            case "none":
                r.Block(s, $"ROC entry needs all four requirements (reqs {reqs} != 15): $100000, top 5 of the last club, car prestige 750; not earned by a fresh career");
                return r;
            case "test":
                s.Cheats.SetMoney(150000);
                s.Cheats.SetPrestige(1.0f);
                // rank (top 5 of the Red Flame club) and car prestige are not career numbers a setter can fake
                // consistently; the shipped "ilikecheating" adds the dialog's "[CHEAT] ALWAYS ENTER ROC" button.
                s.ShippedCheat("ilikecheating");
                if (!s.Click("Go to the Race Of Champions") || !s.WaitFor(() => s.TopDialog == "ROCEntryDialog", 5, "ROCEntryDialog"))
                {
                    r.Block(s, "ROCEntryDialog did not reopen");
                    return r;
                }

                s.Click("[CHEAT] ALWAYS ENTER ROC");
                s.Host.RunFor(1f);
                if (s.TopDialog is "YesNoDialog")
                {
                    s.Click("YES", 3, contains: true);
                }

                break;
            default:
                s.ShippedCheat("letmeroc");
                break;
        }

        if (!s.WaitFor(() => s.Section == "Garage" && Probe.Field(s.ActiveSection, "roc").AsObject is not null, 15, "garage in ROC mode"))
        {
            r.Block(s, "ROC not entered");
            return r;
        }

        var welcome = s.Dismiss();
        r.Num("roc-welcome", $"{string.Join(" / ", welcome)}");
        r.Mark(s, "roc-garage", s.State());
        if (!shippedOpponent)
        {
            r.Blockers.Add("shipped ROC opponent cars (cars/racers/ROC_cars) do not load with this edition's part scripts (content; replaced by a copy of the player's car)");
            CloneOpponentCar(s);
        }

        var moneyBefore = s.Money;
        for (var run = 1; run <= runs; run++)
        {
            if (!RocRun(s, r, run))
            {
                return r;
            }
        }

        r.Num("money-before-runs", $"{moneyBefore}");
        r.Num("money-after-runs", $"{s.Money}");
        SaveAndCheck(s, r);
        return r;
    }

    /// <summary>
    /// STAND-IN for the ROC opponents' saved cars: the shipped files (cars/racers/ROC_cars/roc_*) do not match this edition's
    /// part scripts (MEASURED: e.g. OilPan.load reads a save version and six floats the files do not contain, so Vehicle.load
    /// goes out of step and creates type 0x100, ROCTrack.enter dies on null.load()). The player's car is saved through the
    /// script (Vehicle.save) to save/standin/roc_opponent and Roc's car names point there (TEST-ONLY writes of
    /// Roc.carSaveDir and roc.carNames).
    /// </summary>
    public static void CloneOpponentCar(CareerSession s)
    {
        const string dir = "save/standin/";
        s.Call(s.Car!, "save", 10, s.Str(dir + "roc_opponent"));
        Slrr.Engine.Ui.Script.SetStatic(s.Host.Vm, "java.game.Roc", "carSaveDir", s.Str(dir));
        if (Probe.Field(Probe.Field(s.ActiveSection, "roc").AsObject, "carNames").AsObject is VmArray names)
        {
            for (var i = 0; i < names.Items.Length; i++)
            {
                names.Items[i] = s.Str("roc_opponent");
            }
        }

        s.Step("STAND-IN/TEST-ONLY ROC opponent car = copy of the player's car (save/standin/roc_opponent); Roc.carSaveDir and roc.carNames rewritten");
    }

    private static bool RocRun(CareerSession s, FlowResult r, int run)
    {
        var roc = Probe.Field(s.ActiveSection, "roc").AsObject;
        var won0 = Probe.Field(roc, "wonRuns").AsInt;
        var lost0 = Probe.Field(roc, "lostRuns").AsInt;
        var round0 = Probe.Field(roc, "actRound").AsInt;
        if (!s.Click("Go on the next race"))
        {
            r.Block(s, "ROC 'Go on the next race' not found");
            return false;
        }

        if (!s.WaitFor(() => s.Section == "ROCTrack" && s.Race.InTrack, 90, "ROCTrack entered"))
        {
            r.Block(s, $"ROCTrack not entered ({s.Host.Screens.Current}); thread errors: {string.Join("; ", s.Host.ThreadErrors.TakeLast(3))}");
            return false;
        }

        r.Mark(s, $"roc-track-{run}", $"map {s.Race.MapSource.CurrentMap?.ArchiveName ?? "none"}");
        var track = s.ActiveSection!;
        var bot = Probe.Field(track, "raceBot").AsObject;
        var botCar = Probe.Field(bot, "car").AsObject;
        s.Step($"opponent {s.Host.Vm.GetString(Probe.Field(bot, "name").AsObject)} car {botCar?.Class.Name ?? "none"} (#{CollisionDamageBridge.IdOf(botCar):X})");

        // the start dialog (RaceDialog "Run n of 3", shown 10 s) and the countdown (testStatus -1 → 0 → 1 = GO)
        if (!s.WaitFor(() => Probe.Field(track, "testStatus").AsInt == 1, 60, "GO (testStatus 1)"))
        {
            r.Block(s, $"ROC countdown did not reach GO (testStatus {Probe.Field(track, "testStatus").AsInt})");
            return false;
        }

        r.Mark(s, $"roc-go-{run}", $"HUD: {string.Join(" | ", s.TopTexts().Take(4))}");
        var finish = ReadVector(Probe.Field(track, "finishPos").AsObject);
        var start = s.Race.PlayerTelemetry?.Position ?? default;
        r.Num($"roc-start-{run}", $"({start.X:F1}, {start.Y:F1}, {start.Z:F1})");
        r.Num($"roc-finish-{run}", $"({finish.X:F1}, {finish.Y:F1}, {finish.Z:F1}), {Vector3.Distance(start, finish):F1} m");
        s.Driver.DriveTo(finish, () => (Probe.Field(track, "finished").AsInt & 1) != 0, 60, maxSpeed: 20f);
        var playerTime = Probe.Field(track, "playerTime").AsFloat;
        if ((Probe.Field(track, "finished").AsInt & 1) == 0)
        {
            r.Block(s, $"player did not reach the ROC finish trigger (stand-in driver), telemetry {s.Race.PlayerTelemetry?.Position}");
            return false;
        }

        r.Mark(s, $"roc-finish-{run}", string.Create(CultureInfo.InvariantCulture, $"script playerTime {playerTime:F2} s, plRank {Probe.Field(track, "plRank").AsInt}, HUD: {string.Join(" | ", s.TopTexts().Take(4))}"));
        r.Num($"roc-player-time-{run}", $"{playerTime:F3}");

        // finishRocRace: 3 s + 8 s of script sleeps, a RaceDialog, then back to the garage
        var sawDialog = false;
        var dialogText = "";
        s.Host.RunUntil(() =>
        {
            if (!sawDialog && s.TopDialog == "RaceDialog")
            {
                sawDialog = true;
                dialogText = string.Join(" | ", s.TopTexts().Take(6));
            }

            return s.Section == "Garage";
        }, 60);
        if (sawDialog)
        {
            r.Mark(s, $"roc-results-{run}", dialogText);
        }

        if (s.Section != "Garage")
        {
            r.Block(s, $"did not return to the garage after the ROC run ({s.Host.Screens.Current})");
            return false;
        }

        roc = Probe.Field(s.ActiveSection, "roc").AsObject;
        r.Mark(s, $"roc-garage-return-{run}", string.Create(CultureInfo.InvariantCulture,
            $"roc round {Probe.Field(roc, "actRound").AsInt} (was {round0}), won {Probe.Field(roc, "wonRuns").AsInt} (was {won0}), lost {Probe.Field(roc, "lostRuns").AsInt} (was {lost0}); {s.State()}"));
        s.Dismiss();
        return true;
    }

    // ================================================================== save

    /// <summary>Leaves nothing to chance: GameLogic.autoSaveQuiet's path (save/carrer/name-club/) through the scripts, then checks the files.</summary>
    public static void SaveAndCheck(CareerSession s, FlowResult r)
    {
        var name = s.Host.Vm.GetString(Probe.Field(s.Player, "name").AsObject) ?? "player";
        var dir = $"save/carrer/{name}-{s.Club + 1}";
        var status = s.Host.SaveCareer(dir);
        var main = Path.Combine(s.Host.SaveRoot, "carrer", $"{name}-{s.Club + 1}", "main");
        var exists = File.Exists(main);
        if (status == 1 && exists)
        {
            r.Mark(s, "saved", $"{dir}/main {new FileInfo(main).Length} bytes, status {status}");
            r.Completed = true;
        }
        else
        {
            r.Block(s, $"save failed: status {status}, main exists {exists}");
        }
    }

    internal static Vector3 ReadVector(VmObject? v) =>
        v is null ? default : new Vector3(Probe.Field(v, "x").AsFloat, Probe.Field(v, "y").AsFloat, Probe.Field(v, "z").AsFloat);
}
