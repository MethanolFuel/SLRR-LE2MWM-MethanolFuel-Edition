using System.Globalization;
using System.Numerics;
using Slrr.Engine.Resources;
using Slrr.Game;
using Slrr.Vm;

namespace Slrr.Career;

/// <summary>Options of the City flows.</summary>
public sealed class CityOptions
{
    /// <summary>
    /// STAND-IN placement of the player's car after entering Valo City. The script puts it at the club garage exit
    /// (club 0: (-278.5, 9.8, 1033.0)), where the loaded city has no ground (MEASURED; the city archive's collision
    /// covers z ≤ 328 only). Default: the script's own free-ride start (-83.8, 12.9, -558.7), which has ground.
    /// Null keeps the script's position (to record the blocker).
    /// </summary>
    public Vector3? PlayerStart { get; init; } = new Vector3(-83.8f, 12.9f, -558.7f);

    public float PlayerYaw { get; init; } = -2.0f;

    /// <summary>Day race: STAND-IN finish distance from the start along the player's heading [m] when the road queries
    /// put the finish onto the start (no road graph: getNearestCross(p, 500..800) returns p).</summary>
    public float DayFinishDistance { get; init; } = 120f;

    /// <summary>Day race: prize steps to click in RaceSetup (0 = "Prestige only"; club 0: $100 a step).</summary>
    public int PrizeSteps { get; init; } = 2;

    /// <summary>
    /// Night race: how often the shipped cheat "fastpromotion" moves the player up a club before driving out (CHEAT).
    /// Club 0's night race spots (NightracesData 0..4, z &gt; 600) and garage exit have no ground in the loaded city; club 2's
    /// garage exit (-531.1, 5.05, -149.4) and most of its spots (12..16) have. 0 keeps club 0 and records the blocker.
    /// </summary>
    public int NightPromotions { get; init; } = 2;
}

/// <summary>
/// Valo City (java.game.Valocity extends City): reached from the garage through "Choose Track" → TrackSelect →
/// "Valo City" (the MWM edition has no separate city button; Garage CMD_HITTHESTREET is "Change driver skin/model",
/// MEASURED from Garage.class). Day (04:00-22:00): club racers drive in traffic; the player honks at one within 10 m
/// (RacerTalkDialog) → RaceSetup (route, prize) → START RACE → countdown → finish trigger → RaceDialog → payout. Night:
/// a drag race at the night race start (green flag): PARTICIPATE → NrOpponentDialog → countdown → finish → payout.
/// </summary>
public static class CityFlows
{
    public const int MapCmdCarAdd = 16; // EVENT_COMMAND

    // ================================================================== entering the city

    /// <summary>Garage → Choose Track → TrackSelect → "Valo City" → TAKE IT → City entered; then the stand-in placement.</summary>
    public static bool EnterCity(CareerSession s, FlowResult r, CityOptions o)
    {
        if (!s.Click("Choose Track") || !s.WaitSection("TrackSelect", 15))
        {
            r.Block(s, $"Choose Track did not reach TrackSelect ({s.Host.Screens.Current}): {string.Join(" ", s.TopTexts().Take(6))}");
            return false;
        }

        s.Host.RunFor(1f);
        var found = false;
        for (var page = 0; page < 3 && !(found = s.Host.Input.Click("Valo City")); page++)
        {
            if (!s.Host.Input.Click("Next Page") && !s.Host.Input.ClickCommand(Slrr.Race.RaceScenario.TrackSelectCmdDown))
            {
                break;
            }

            s.Host.RunFor(0.5f);
        }

        if (!found)
        {
            r.Block(s, "'Valo City' not in TrackSelect");
            return false;
        }

        s.Step("click 'Valo City'");
        s.Host.RunFor(0.5f);
        s.Click("TAKE IT");
        var options = false;
        var ok = s.Host.RunUntil(() =>
        {
            if (!options && s.Host.Screens.Current.Contains("Options", StringComparison.Ordinal))
            {
                options = true;
                s.Host.Input.Click("OK");
            }

            return s.Race.InTrack;
        }, 120);
        s.Step($"TAKE IT -> {(ok ? "entered" : "NOT entered")} {s.ActiveSection?.Class.Name} map {s.Race.MapSource.CurrentMap?.ArchiveName ?? "none"}");
        if (!ok || s.ActiveSection?.Class.IsSubclassOf("java.game.City") != true)
        {
            r.Block(s, $"City not entered ({s.Host.Screens.Current})");
            return false;
        }

        var city = s.ActiveSection!;
        r.Mark(s, "city", string.Create(CultureInfo.InvariantCulture,
            $"{city.Class.Name}, night {Probe.Field(city, "nightTime").AsInt}, time {s.TimeOfDay}, script start {Fmt(CareerFlows.ReadVector(Probe.Field(city, "posStart").AsObject))}"));
        foreach (var d in s.Dismiss())
        {
            r.Num("city-hint", $"{d}");
        }

        s.Host.RunFor(2f);
        var t = s.Race.PlayerTelemetry;
        var grounded = t?.Wheels.Count(w => w.Grounded) ?? 0;
        r.Num("city-script-start-grounded-wheels", $"{grounded}");
        r.Num("city-script-start-y-after-2s", $"{t?.Position.Y ?? float.NaN:F1}");
        if (grounded == 0)
        {
            r.Block(s, $"no ground at the club garage exit {Fmt(CareerFlows.ReadVector(Probe.Field(city, "posStart").AsObject))}: the car falls (y {t?.Position.Y:F1}); City ground is Slrr.Roads' area");
            if (o.PlayerStart is null)
            {
                return false;
            }
        }

        if (grounded == 0 && o.PlayerStart is { } p)
        {
            s.Driver.Place(p, o.PlayerYaw);
            s.Race.Physics.Command(s.CarId, "start");
            s.Host.RunFor(2f);
            t = s.Race.PlayerTelemetry;
            grounded = t?.Wheels.Count(w => w.Grounded) ?? 0;
            s.Step($"after the stand-in placement: {grounded} wheels grounded at {Fmt(t?.Position ?? default)}");
            r.Num("city-standin-grounded-wheels", $"{grounded}");
        }

        return grounded > 0;
    }

    // ================================================================== day race

    /// <summary>
    /// Day race (club racer in traffic). Stand-ins: traffic does not exist (MapNatives records addTrafficCar, no car is
    /// spawned or announced), so the flow takes a club racer the script put into traffic (<c>bot.dummycar</c>, created
    /// by Valocity.enter at y -10000), places it 7 m ahead of the player and posts the <c>car_add</c> the exe's traffic
    /// would post to the map (format "car_add %d %d" MEASURED 0x610394, City.handleEvent reads car id, traffic id).
    /// The opponent does not drive (AI_race is not simulated); the player drives to the finish.
    /// </summary>
    public static FlowResult Day(CareerSession s, CityOptions? o = null)
    {
        o ??= new CityOptions();
        var r = new FlowResult("city-day");
        if (!CareerFlows.BootAndBuy(s, r) || !EnterCity(s, r, o))
        {
            return r;
        }

        var city = s.ActiveSection!;
        if (Probe.Field(city, "nightTime").AsInt != 0)
        {
            r.Block(s, "it is night: day races need 04:00-22:00");
            return r;
        }

        // ---- the opponent: a club-mate the player may challenge (GameLogic.canChallenge: same club, prestige + 0.05 >= his)
        var bot = PickDayOpponent(s, r);
        if (bot is null)
        {
            return r;
        }

        var dummy = Probe.Field(bot, "dummycar").AsObject;
        var dummyId = ResourceEngine.IdOf(dummy);
        var trafficId = Probe.Field(bot, "traffic_id").AsInt;
        var mapId = s.MapInstanceId(null);
        var me = s.Race.PlayerTelemetry!;
        var ahead = me.Position + Forward(me) * 7f;
        if (s.Host.Resources.Tree.Get(dummyId) is { } dn)
        {
            dn.Pos[0] = ahead.X;
            dn.Pos[1] = ahead.Y + 0.5f;
            dn.Pos[2] = ahead.Z;
            dn.Ori[0] = me.Ypr.Yaw;
        }

        s.Host.Resources.QueueEvent(mapId, mapId, MapCmdCarAdd, $"car_add {dummyId} {trafficId}");
        s.Step($"STAND-IN traffic: club racer {Name(s, bot)} dummycar #{dummyId:X} (traffic id {trafficId}) placed 7 m ahead, posted 'car_add {dummyId} {trafficId}' to the map #{mapId:X}");
        if (!s.WaitFor(() => Probe.Vector(s.Host.Vm, Probe.Field(city, "opponentCars").AsObject).Count > 0, 5, "City.opponentCars has the racer"))
        {
            r.Block(s, "City did not register the car_add (opponentCars empty)");
            return r;
        }

        r.Mark(s, "opponent-in-traffic");
        s.WaitFor(() => Probe.Field(city, "oppStatusDisplayed").AsInt != 0, 5, "opponent status on the HUD");
        var dpos = s.Host.Resources.Tree.Get(dummyId) is { } dn2 ? new Vector3(dn2.Pos[0], dn2.Pos[1], dn2.Pos[2]) : default;
        s.Step($"racer dummycar at {Fmt(dpos)} ({Vector3.Distance(dpos, s.Race.PlayerTelemetry?.Position ?? default):F1} m from the player), physics car {(s.Race.Physics.Scene.Car(dummyId) is { } dc ? dc.State.ToString() : "none")}, carrerInProgress {Probe.Static(s.Host.Vm, "java.game.GameLogic", "carrerInProgress").AsInt}, gameMode {Probe.Static(s.Host.Vm, "java.game.GameLogic", "gameMode").AsInt}");
        r.Num("hud-opponent", $"{string.Join(" | ", HudTexts(s).Take(6))}");

        // ---- honk (AXIS_HORN through the input layer; Vehicle.getHorn reads it) until the talk dialog opens
        var talk = s.Host.RunUntil(() =>
        {
            s.Driver.Hold(StandInDriver.AxisHorn, 1f);
            return s.TopDialog is "RacerTalkDialog" || s.Section == "RaceSetup";
        }, 10);
        s.Driver.Release();
        s.Step($"horn held: {(talk ? s.TopDialog ?? s.Section : "no reaction")}");
        if (!talk)
        {
            r.Block(s, $"no RacerTalkDialog after honking (oppStatus {Probe.Field(city, "oppStatusDisplayed").AsInt}, raceBot {Probe.Field(city, "raceBot").AsObject is not null}, abandoned2 {Probe.Field(city, "abandoned2").AsInt})");
            return r;
        }

        if (s.TopDialog is "RacerTalkDialog")
        {
            r.Mark(s, "racer-talk", string.Join(" | ", s.TopTexts().Take(6)));
            s.Host.Input.ClickCommand(0); // " 1. \"Wanna race with you!\"" (RacerTalkDialog.CMD_RACE)
            s.Host.RunFor(0.5f);
            s.Step("talk: " + string.Join(" | ", s.TopTexts().Take(6)));
            if (!s.Host.Input.ClickCommand(2)) // CMD_EXITNRACE, only offered when canChallenge
            {
                r.Block(s, $"the racer refused: {string.Join(" | ", s.TopTexts().Take(4))}");
                s.Host.Input.ClickCommand(1);
                return r;
            }
        }

        if (!s.WaitSection("RaceSetup", 10))
        {
            r.Block(s, "RaceSetup not reached");
            return r;
        }

        var setup = s.ActiveSection!;
        var pStart = CareerFlows.ReadVector(Probe.Field(setup, "pStart").AsObject);
        var pFinish = CareerFlows.ReadVector(Probe.Field(setup, "pFinish").AsObject);
        r.Mark(s, "race-setup", $"{string.Join(" | ", s.TopTexts().Take(4))}; start {Fmt(pStart)}, finish {Fmt(pFinish)} ({Vector3.Distance(pStart, pFinish):F1} m)");
        if (Vector3.Distance(pStart, pFinish) < 25f)
        {
            r.Num("road-graph-gap", $"getNearestCross(pStart, 500..800) gave a finish {Vector3.Distance(pStart, pFinish):F1} m from the start (Slrr.Roads)");
            var dir = Forward(s.Race.PlayerTelemetry!);
            var f = pStart + dir * o.DayFinishDistance;
            var vf = Probe.Field(setup, "pFinish").AsObject!;
            SetVector(vf, f);
            s.Step($"STAND-IN/TEST-ONLY RaceSetup.pFinish moved {o.DayFinishDistance:F0} m along the player's heading to {Fmt(f)} (no road graph)");
            pFinish = f;
        }

        for (var i = 0; i < o.PrizeSteps; i++)
        {
            s.Host.Input.ClickCommand(6); // RaceSetup.CMD_PRIZE
            s.Host.RunFor(0.2f);
        }

        var prize = Probe.Field(setup, "forMoney").AsInt;
        r.Num("day-prize", $"{prize}");
        var money0 = s.Money;
        s.Step($"prize ${prize}; money ${money0}");
        if (!s.Click("START RACE"))
        {
            r.Block(s, "START RACE not clickable");
            return r;
        }

        if (!s.WaitFor(() => s.Section == "Valocity" && Probe.Field(city, "raceState").AsInt == 1, 10, "race state 1 in the city"))
        {
            r.Block(s, "race did not start");
            return r;
        }

        r.Mark(s, "race-started", $"bot car #{ResourceEngine.IdOf(Probe.Field(Probe.Field(city, "raceBot").AsObject, "car").AsObject):X}");
        // countdown: timers 9..12 → "3", "2", "1", "GO!" → startRace2 (player car 'start')
        var sawGo = s.Host.RunUntil(() => HudTexts(s).Any(x => x == "GO!"), 10);
        r.Mark(s, sawGo ? "countdown-go" : "countdown-no-go", string.Join(" | ", HudTexts(s).Take(5)));
        var raceFinish = CareerFlows.ReadVector(Probe.Field(city, "raceFinish").AsObject);
        s.Driver.DriveTo(raceFinish, () => Probe.Field(city, "raceState").AsInt == 2, 60, maxSpeed: 22f);
        if (Probe.Field(city, "raceState").AsInt != 2)
        {
            r.Block(s, $"player did not reach the day-race finish trigger at {Fmt(raceFinish)}");
            return r;
        }

        r.Mark(s, "finish", $"time warp {s.Host.SystemArea.TimeWarp}");
        var dialog = "";
        var ok = s.Host.RunUntil(() =>
        {
            if (s.TopDialog is "RaceDialog" && dialog.Length == 0)
            {
                dialog = string.Join(" | ", s.TopTexts().Take(8)).Replace('\n', ' ');
            }

            return dialog.Length > 0;
        }, 20);
        if (ok)
        {
            r.Mark(s, "results", dialog);
        }

        // Frontend.loadingScreen.userWait(): the player confirms the dialog
        s.Dismiss(2, 1f);
        s.Host.RunFor(2f);
        r.Mark(s, "payout", $"money ${money0} -> ${s.Money} (prize {prize}), races won {s.RacesWon}, lost {s.RacesLost}, prestige {s.Prestige:0.####}");
        r.Num("day-money-before", $"{money0}");
        r.Num("day-money-after", $"{s.Money}");
        ReturnToGarage(s, r);
        CareerFlows.SaveAndCheck(s, r);
        return r;
    }

    private static VmObject? PickDayOpponent(CareerSession s, FlowResult r)
    {
        if (Probe.Static(s.Host.Vm, "java.game.GameLogic", "speedymen").AsObject is not VmArray men)
        {
            r.Block(s, "GameLogic.speedymen missing");
            return null;
        }

        var club = s.Club;
        var prestige = s.Prestige;
        var candidates = men.Items.Select(v => v.AsObject).Where(b => b is not null && b.Class.Name == "java.game.Bot"
            && Probe.Field(b, "club").AsInt == club && Probe.Field(b, "dummycar").AsObject is not null).ToList();
        var ok = candidates.FirstOrDefault(b => prestige + 0.05f >= Probe.Field(b, "prestige").AsFloat);
        s.Step(string.Create(CultureInfo.InvariantCulture,
            $"club {club} racers in traffic: {candidates.Count}; challengeable (prestige <= {prestige + 0.05f:0.###}): {candidates.Count(b => prestige + 0.05f >= Probe.Field(b, "prestige").AsFloat)}"));
        if (ok is null)
        {
            r.Block(s, "no club racer in traffic the player may challenge");
        }

        return ok;
    }

    // ================================================================== night race

    /// <summary>
    /// Night drag race. The garage clock is moved with the shipped "Advance time 6 hours" button until 22:00-04:00; the
    /// night race start/finish come from NightracesData (index random·5 + club·6). The player's car is placed next to the
    /// start (STAND-IN; also because club 0's race spots, z &gt; 600, have no ground in the loaded city), PARTICIPATE,
    /// the opponent list (NrOpponentDialog), the countdown, the drive to the finish (the opponent's AI_NightRace is not
    /// simulated: it does not move; the script gives it 1000 s after a 10 s timer), RaceDialog, payout, back to the garage.
    /// </summary>
    public static FlowResult Night(CareerSession s, CityOptions? o = null)
    {
        o ??= new CityOptions();
        var r = new FlowResult("city-night");
        if (!CareerFlows.BootAndBuy(s, r))
        {
            return r;
        }

        for (var i = 0; i < 4 && !IsNight(s); i++)
        {
            s.Click("Advance time 6 hours");
            s.Host.RunFor(0.5f);
        }

        for (var i = 0; i < o.NightPromotions; i++)
        {
            s.ShippedCheat("fastpromotion");
            s.Dismiss();
        }

        r.Mark(s, "night", $"garage clock {s.TimeOfDay}, day {s.Day}, club {s.Club}, rank {s.Rank}");
        if (!EnterCity(s, r, o))
        {
            return r;
        }

        var city = s.ActiveSection!;
        if (Probe.Field(city, "nightTime").AsInt == 0)
        {
            r.Block(s, $"City thinks it is day ({s.TimeOfDay})");
            return r;
        }

        var pS = CareerFlows.ReadVector(Probe.Field(city, "pS").AsObject);
        var pF = CareerFlows.ReadVector(Probe.Field(city, "pF").AsObject);
        var dirS = CareerFlows.ReadVector(Probe.Field(city, "dirS").AsObject);
        r.Num("night-start", $"{Fmt(pS)}");
        r.Num("night-finish", $"{Fmt(pF)} ({Vector3.Distance(pS, pF):F0} m)");
        var groundS = HasGround(s, pS);
        r.Num("night-start-has-ground", $"{groundS}");
        if (!groundS)
        {
            r.Block(s, $"night race start {Fmt(pS)} has no ground in the loaded city (Slrr.Roads: City ground)");
            return r;
        }

        var oriS = Probe.Field(Probe.Field(city, "oriS").AsObject, "y").AsFloat;
        s.Driver.Place(pS - dirS * 6f, oriS);
        if (!s.WaitFor(() => Probe.Field(city, "nrNear").AsInt == 1, 10, "night race start trigger (nrNear)"))
        {
            r.Block(s, "the night race start trigger did not fire");
            return r;
        }

        r.Mark(s, "night-race-spot", string.Join(" | ", s.Choices().Take(6)));
        var money0 = s.Money;
        if (!s.Click("PARTICIPATE"))
        {
            r.Block(s, "PARTICIPATE not offered");
            return r;
        }

        if (s.WaitFor(() => s.TopDialog is "NrOpponentDialog" or "WarningDialog", 10, "opponent list"))
        {
            r.Mark(s, "opponent-list", string.Join(" | ", s.TopTexts().Take(8)).Replace('\n', ' '));
            if (s.TopDialog is "WarningDialog")
            {
                r.Block(s, string.Join(" ", s.TopTexts()));
                return r;
            }

            s.Host.Input.ClickCommand(0);
        }

        if (!s.WaitFor(() => Probe.Field(city, "nrStat").AsInt == 6, 40, "night race running (NR_RACE)"))
        {
            r.Block(s, $"night race did not start (nrStat {Probe.Field(city, "nrStat").AsInt}, {s.Host.Screens.Current})");
            return r;
        }

        r.Mark(s, "race-started", $"prize {Probe.Field(city, "nrPrize").AsInt}, opponent {s.Host.Vm.GetString(Probe.Field(city, "nrName1").AsObject)}");
        s.Driver.DriveTo(pF, () => Probe.Field(city, "nrFinished2").AsInt == 1, 120, maxSpeed: 30f);
        if (Probe.Field(city, "nrFinished2").AsInt != 1)
        {
            r.Block(s, "player did not reach the night race finish trigger");
            return r;
        }

        r.Mark(s, "finish", string.Create(CultureInfo.InvariantCulture, $"script nrTime2 {Probe.Field(city, "nrTime2").AsFloat:F2} s"));
        var dialog = "";
        s.Host.RunUntil(() =>
        {
            if (s.TopDialog is "RaceDialog" && dialog.Length == 0)
            {
                dialog = string.Join(" | ", s.TopTexts().Take(8)).Replace('\n', ' ');
            }

            return Probe.Field(city, "nrStat").AsInt == 0;
        }, 60);
        if (dialog.Length > 0)
        {
            r.Mark(s, "results", dialog);
        }

        s.Host.RunFor(4f);
        r.Mark(s, "payout", $"money ${money0} -> ${s.Money}, races won {s.RacesWon}, lost {s.RacesLost}, prestige {s.Prestige:0.####}");
        r.Num("night-money-before", $"{money0}");
        r.Num("night-money-after", $"{s.Money}");
        ReturnToGarage(s, r);
        CareerFlows.SaveAndCheck(s, r);
        return r;
    }

    // ================================================================== back to the garage

    /// <summary>
    /// ESC (AXIS_MENU) → IngameMenu → its exit item (IngameMenu.CMD_BACKTOGARAGE 50; in the city "TOW CAR TO GARAGE FOR $n")
    /// → the track's parent section. A track entered from TrackSelect returns there; its BACK leads to the garage.
    /// </summary>
    public static void ReturnToGarage(CareerSession s, FlowResult r)
    {
        var money0 = s.Money;
        s.Host.Input.TapAxis(36); // AXIS_MENU → Track CMD_INGAMEMENU
        s.Host.RunFor(1f);
        var label = s.Choices().FirstOrDefault(c => c.Contains("GARAGE", StringComparison.OrdinalIgnoreCase) || c.Contains("EXIT", StringComparison.OrdinalIgnoreCase));
        s.Step("in-game menu: " + string.Join(" | ", s.Choices().Take(8)));
        if (!s.Host.Input.ClickCommand(50))
        {
            r.Block(s, "in-game menu exit item not found");
            return;
        }

        s.Host.RunFor(1f);
        s.Dismiss();
        s.WaitFor(() => s.Section is "Garage" or "TrackSelect", 30, "track left");
        if (s.Section == "TrackSelect")
        {
            s.Click("BACK");
        }

        if (s.WaitFor(() => s.Section == "Garage", 30, "garage"))
        {
            s.Dismiss();
            r.Mark(s, "garage-return", $"via '{label}', money ${money0} -> ${s.Money}; {s.State()}");
        }
        else
        {
            r.Block(s, $"did not return to the garage ({s.Host.Screens.Current})");
        }
    }

    // ================================================================== helpers

    public static bool IsNight(CareerSession s)
    {
        var h = Probe.Static(s.Host.Vm, "java.game.GameLogic", "time").AsFloat / 3600f;
        return h < 4 || h > 22;
    }

    public static bool HasGround(CareerSession s, Vector3 p) =>
        s.Race.MapSource.CurrentMap?.Ground is { } g && Slrr.Engine.MapNatives.MapWorlds.TryGround(g, p, 5f, out _);

    private static IEnumerable<string> HudTexts(CareerSession s) =>
        s.Host.Screens.LiveOsds().SelectMany(osd => s.Host.Driver.TextsOf(osd).Where(s.Host.Driver.IsVisible).Select(t => t.Text ?? string.Empty))
            .Where(t => t.Length > 0);

    private static Vector3 Forward(Slrr.Game.Physics.CarTelemetry t)
    {
        var f = Vector3.Transform(-Vector3.UnitZ, t.Orientation) /* body frame: -Z forward (Slrr.Game.Physics) */;
        f.Y = 0;
        return f.LengthSquared() > 1e-6f ? Vector3.Normalize(f) : Vector3.UnitZ;
    }

    private static void SetVector(VmObject v, Vector3 p)
    {
        void W(string n, float x)
        {
            if (v.Class.FindField(n) is { IsStatic: false } f)
            {
                v.Fields[f.Slot] = Value.Float(x);
            }
        }

        W("x", p.X);
        W("y", p.Y);
        W("z", p.Z);
    }

    private static string Name(CareerSession s, VmObject bot) => s.Host.Vm.GetString(Probe.Field(bot, "name").AsObject) ?? "?";

    internal static string Fmt(Vector3 v) => string.Create(CultureInfo.InvariantCulture, $"({v.X:F1}, {v.Y:F1}, {v.Z:F1})");
}
