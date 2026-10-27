using System.Globalization;
using System.Numerics;
using Slrr.Engine.Commands;
using Slrr.Engine.Resources;
using Slrr.Game;
using Slrr.Race;
using Slrr.Vm;

namespace Slrr.Cli;

/// <summary>A headless race session: game host + command engine, menu path to a track, optional scripted driving.</summary>
public sealed class RaceSession : IDisposable
{
    public RaceSession(string root, TextWriter? scriptLog = null)
    {
        Physics = new CommandsPhysicsWorld();
        Natives = new CommandNatives(() => Physics.Engine);
        Host = new GameHost(new GameHostOptions { Root = root, Physics = Physics, ExtraModules = [Natives], ScriptLog = scriptLog });
    }

    public GameHost Host { get; }

    public CommandsPhysicsWorld Physics { get; }

    public CommandNatives Natives { get; }

    public CommandEngine Engine => Physics.Engine ?? throw new InvalidOperationException("not booted");

    public string Section => Host.ActiveSection is { } s ? ScreenTracker.Short(s.Class.Name) : "";

    /// <summary>Boot → main menu → SELECT TRACK → <paramref name="track"/> → TAKE IT; true when the track's GameState is active.</summary>
    public bool EnterTrack(string track, float maxSeconds = 30)
    {
        if (!Host.Boot() || !Host.RunUntil(() => Section == "MainMenu", 30))
        {
            return false;
        }

        if (!Host.Input.Click("SELECT TRACK"))
        {
            return false;
        }

        if (!Host.RunUntil(() => Section == "TrackSelect", 10))
        {
            return false;
        }

        Host.RunFor(1);
        for (var page = 0; !Host.Input.Click(track); page++)
        {
            if (page >= 5 || !Host.Input.Click("Next Page"))
            {
                return false;
            }

            Host.RunFor(0.5f);
        }

        Host.RunFor(0.5f);
        Host.Input.Click("TAKE IT");
        return Host.RunUntil(() => Section is not ("TrackSelect" or "MainMenu" or ""), maxSeconds);
    }

    public VmObject? PlayerCar => Probe.Field(Host.Player, "car").AsObject;

    public int PlayerCarId => ResourceEngine.IdOf(PlayerCar);

    public float CarField(string name) => Probe.Field(PlayerCar, name).AsFloat;

    /// <summary>Puts a car at a pose with zero velocity (as <c>setMatrix</c> + <c>reset</c> would).</summary>
    public void Place(int carId, Vector3 position, float yaw) =>
        Engine.World.SetPose(carId, new Pose(position, new Ypr(yaw, 0, 0), default));

    /// <summary>
    /// TestTrack acceleration round (MEASURED from TestTrack.enter: round[0] start trigger at (0,0,-1461.78) radius 10,
    /// startdir 3.14; 1/4 mile trigger at z=-1059.78 and 1 mile trigger at z=148.21, radius 20): stop the player car in
    /// the start trigger facing the start direction, wait for the script's 1 s timer to arm the round, then full throttle.
    /// Returns the script's own results (player.car.bestTestTrackTime2 / Time0 / Acc) after the run.
    /// </summary>
    public (float QuarterMile, float Mile, float ZeroTo100, float TopSpeed) RunAccelerationTest(float maxSeconds = 120)
    {
        var car = PlayerCarId;
        Place(car, new Vector3(0, 0, -1461.785f), MathF.PI);
        Host.RunFor(2.5f);
        Engine.Driver.SetInput(car, new DriveInput(1, 0, 0));
        Host.RunUntil(() => CarField("bestTestTrackTime0") > 0.1f, maxSeconds);
        Engine.Driver.SetInput(car, DriveInput.FullBrake);
        var top = Engine.Vehicles.TryGetVehicle(car, out var p) ? p.Speed : 0f;
        return (CarField("bestTestTrackTime2"), CarField("bestTestTrackTime0"), CarField("bestTestTrackAcc"), top);
    }

    public void Dispose() => Host.Dispose();
}

/// <summary>
/// <c>race</c>: headless race-track session with the command engine (docs/race/commands.md).
/// Integration (lead): add ProjectReferences to src/Slrr.Game and src/Slrr.Engine.Commands in Slrr.Cli.csproj and
/// <c>"race" =&gt; CmdRace.Run(args[1..], o, e)</c> in Commands.cs.
/// </summary>
public static class CmdRace
{
    public const string Usage = "race [--root dir] [--track \"Test Track\"] [--seconds 10] [--accel] [--transcript] [--coverage]";

    public static int Run(string[] args, TextWriter o, TextWriter e)
    {
        string root = ".", track = "Test Track";
        float seconds = 10;
        bool accel = false, transcript = false, coverage = false;
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--root" when i + 1 < args.Length:
                    root = args[++i];
                    break;
                case "--track" when i + 1 < args.Length:
                    track = args[++i];
                    break;
                case "--seconds" when i + 1 < args.Length:
                    seconds = float.Parse(args[++i], CultureInfo.InvariantCulture);
                    break;
                case "--accel":
                    accel = true;
                    break;
                case "--transcript":
                    transcript = true;
                    break;
                case "--coverage":
                    coverage = true;
                    break;
                default:
                    e.WriteLine("usage: " + Usage);
                    return 2;
            }
        }

        if (coverage)
        {
            PrintCoverage(o);
            if (!File.Exists(Path.Combine(root, "sl.rpk")))
            {
                return 0;
            }
        }

        if (!File.Exists(Path.Combine(root, "sl.rpk")))
        {
            e.WriteLine($"no game data at {Path.GetFullPath(root)} (sl.rpk missing)");
            return 1;
        }

        using var s = new RaceSession(root);
        if (!s.EnterTrack(track))
        {
            e.WriteLine($"could not enter '{track}' (screen {s.Host.Screens.Current}; choices: {string.Join(" | ", s.Host.Screens.Choices())})");
            return 1;
        }

        o.WriteLine($"entered {s.Section} at {s.Host.Vm.Time:F2} s");
        s.Host.RunFor(seconds);
        if (accel)
        {
            var r = s.RunAccelerationTest();
            o.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"acceleration round (script results): 1/4 mile {r.QuarterMile:F3} s, 1 mile {r.Mile:F3} s, 0-100 km/h {r.ZeroTo100:F3} s, speed at the line {r.TopSpeed:F1} m/s"));
        }

        var eng = s.Engine;
        foreach (var line in eng.Describe())
        {
            o.WriteLine(line);
        }

        var consumed = eng.Stats.Values.Sum(v => v.Consumed);
        var rejected = eng.Stats.Values.Sum(v => v.Rejected);
        o.WriteLine($"commands offered {consumed + rejected}: consumed {consumed}, not consumed {rejected}");
        foreach (var (verb, st) in eng.Stats.OrderByDescending(kv => kv.Value.Consumed + kv.Value.Rejected))
        {
            o.WriteLine($"  {verb,-18} consumed {st.Consumed,4}  not consumed {st.Rejected,4}");
        }

        if (transcript)
        {
            foreach (var c in eng.Transcript)
            {
                o.WriteLine(string.Create(CultureInfo.InvariantCulture, $"  {c.Time,8:F2} {c.TargetId,7} {c.Kind,-7} {(c.Consumed ? "ok " : "-- ")} {c.Text}"));
            }
        }

        o.WriteLine($"natives missing ({s.Host.Missing.Count}): {string.Join(", ", s.Host.Missing.Select(m => $"{m.Key} x{m.Calls}"))}");
        o.WriteLine($"script thread errors ({s.Host.ThreadErrors.Count})");
        foreach (var t in s.Host.ThreadErrors.Take(10))
        {
            o.WriteLine("  " + t);
        }

        return 0;
    }

    public static void PrintCoverage(TextWriter o)
    {
        var (impl, rec, other, total) = VerbCatalog.Coverage();
        o.WriteLine($"call-site coverage over {total} literal-verb sites: implemented {impl}, recorded {rec}, other area {other}");
        foreach (var v in VerbCatalog.Verbs.OrderByDescending(v => v.ScriptSites).ThenBy(v => v.Verb, StringComparer.Ordinal))
        {
            o.WriteLine($"  {v.ScriptSites,3} {v.Verb,-18} {v.Group,-10} {v.Status,-11} {v.Format ?? "(plain)"}");
        }
    }
}
