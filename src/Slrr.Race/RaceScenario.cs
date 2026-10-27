using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using Slrr.Game;
using Slrr.Game.Physics;
using Slrr.Vm;

namespace Slrr.Race;

/// <summary>What <see cref="RaceScenario"/> drives and how long.</summary>
public sealed class RaceScenarioOptions
{
    /// <summary>TrackSelect labels tried in order until one is entered.</summary>
    public IReadOnlyList<string> Tracks { get; init; } = ["Drag Track", "Test Track"];

    /// <summary>Seconds of game time with the throttle held after the track was entered.</summary>
    public float DriveSeconds { get; init; } = 10f;

    /// <summary>Steering held during the drive (-1..1, AXIS_TURN_LEFTRIGHT: +1 = left).</summary>
    public float Steer { get; init; }

    /// <summary>Seconds the track runs before the throttle goes down (Track.enter timers, camera settle).</summary>
    public float SettleSeconds { get; init; } = 2f;

    /// <summary>On Test Track: also run the scripts' acceleration round (place the car in round 0's start trigger, full throttle).</summary>
    public bool AccelerationRound { get; init; } = true;

    public float AccelerationTimeout { get; init; } = 90f;
}

/// <summary>One sample of the player car during the drive.</summary>
public readonly record struct DriveSample(float Time, Vector3 Position, float Speed, float Rpm, int Gear, int WheelsGrounded, float Throttle);

/// <summary>Measured outcome of a <see cref="RaceScenario"/> run (every number comes from VM-visible state or the physics telemetry).</summary>
public sealed class RaceScenarioResult
{
    public List<string> Steps { get; } = [];

    public bool Booted { get; set; }

    public bool BoughtCar { get; set; }

    public string? CarClass { get; set; }

    public int CarParts { get; set; }

    public string? DriveableAnswer { get; set; }

    /// <summary><see cref="RaceHost.EngineReport"/> right after the purchase.</summary>
    public string? EngineBefore { get; set; }

    public string? Track { get; set; }

    public string? TrackClass { get; set; }

    public bool EnteredTrack { get; set; }

    public bool CarBound { get; set; }

    public string? CarName { get; set; }

    public float CarMass { get; set; }

    public Vector3 Start { get; set; }

    public Vector3 End { get; set; }

    public float Distance => Vector3.Distance(Start, End);

    public float MaxSpeed { get; set; }

    public float FinalSpeed { get; set; }

    /// <summary><c>Vehicle.getSpeedSquare()</c> evaluated through the VM at the end of the drive (m²/s²).</summary>
    public float ScriptSpeedSquare { get; set; }

    /// <summary>Script-visible position <c>player.car.getPos()</c> at the end (instance node).</summary>
    public Vector3 ScriptPosition { get; set; }

    public float GroundedFraction { get; set; }

    public float MaxRpm { get; set; }

    public int MaxGear { get; set; }

    public List<DriveSample> Samples { get; } = [];

    /// <summary>Most engine-sample voices the player car's sound model had during the drive (audio on).</summary>
    public int AudioEngineVoices { get; set; }

    /// <summary>Highest table rpm the sound model was driven with (exe formula rpm' = |omega| x 9.5493 + 1).</summary>
    public float AudioTableRpmMax { get; set; }

    /// <summary>Player car once per second of the TestTrack acceleration round.</summary>
    public List<DriveSample> AccelSamples { get; } = [];

    /// <summary>EVENT_TRIGGER_ON/OFF the command engine posted during the acceleration round.</summary>
    public List<string> AccelTriggerEvents { get; set; } = [];

    public (float QuarterMile, float Mile, float ZeroTo100, float ZeroTo200, float MaxSpeedKmh)? Acceleration { get; set; }

    public List<string> ThreadErrors { get; } = [];

    public List<string> Missing { get; } = [];

    public List<string> Notes { get; } = [];

    public double WallMs { get; set; }

    public double DriveWallMs { get; set; }

    public int DriveFrames { get; set; }

    public long PhysicsSteps { get; set; }

    public bool Moved => Distance > 5f && MaxSpeed > 1f;
}

/// <summary>
/// The scripted end-to-end run: boot → main menu → new career → buy a used car (the embedded smoke script up to the
/// purchase) → Garage "Choose Track" → TrackSelect → a shipped map → TAKE IT → throttle (and steering) held through the
/// game's input layer (keyboard keys mapped by the shipped ControlSet) → VM/physics assertions.
/// </summary>
public sealed class RaceScenario(RaceHost race, RaceScenarioOptions? options = null)
{
    private const int DikUp = 200;
    private const int DikLeft = 203;
    private const int DikRight = 205;

    public RaceScenarioOptions Options { get; } = options ?? new RaceScenarioOptions();

    public RaceScenarioResult Result { get; } = new();

    private GameHost Host => race.Host;

    private void Step(string s) => Result.Steps.Add(string.Create(CultureInfo.InvariantCulture, $"[{Host.Vm.Time,7:F2}s] {s}"));

    public RaceScenarioResult Run()
    {
        var wall = Stopwatch.StartNew();
        try
        {
            if (BootAndBuy() && EnterTrack())
            {
                Drive();
                if (Options.AccelerationRound && Result.TrackClass == "java.game.TestTrack")
                {
                    AccelerationRound();
                }
            }
        }
        finally
        {
            Result.WallMs = wall.Elapsed.TotalMilliseconds;
            Result.ThreadErrors.AddRange(Host.ThreadErrors);
            Result.Missing.AddRange(Host.Missing.Select(m => $"{m.Key} x{m.Calls} [{m.Screen}]"));
        }

        return Result;
    }

    // ------------------------------------------------------------------ menu path

    public bool BootAndBuy()
    {
        Result.Booted = race.Measure("boot", () => Host.Boot() && Host.RunUntil(() => race.Section == "MainMenu", 30));
        Step($"boot -> {race.Section} ({Result.Booted})");
        if (!Result.Booted)
        {
            return false;
        }

        // the embedded smoke script up to (not including) the catalog part: new career, dealer, buy the first used car
        var steps = PlayScript.Parse(PlayCommand.SmokeScript());
        var cut = steps.FindIndex(s => s.Verb == "click" && s.Args.Count > 0 && s.Args[0].Contains("Catalog", StringComparison.Ordinal));
        var runner = new PlayRunner(Host) { StopOnFailure = true };
        race.Measure("career+buy", () =>
        {
            runner.Run(cut < 0 ? steps : steps.Take(cut));
            return 0;
        });
        foreach (var r in runner.Results.Where(r => !r.Ok))
        {
            Step($"play line {r.Step.Line} FAILED: {r.Step.Text} ({r.Detail})");
        }

        Result.CarClass = PlayerCarClass();
        Result.CarParts = Host.View.InstalledParts.Count;
        Result.BoughtCar = !runner.Failed && Result.CarClass == "java.game.Vehicle";
        Step($"career + used car: {Result.CarClass ?? "none"}, {Result.CarParts} parts, money {Host.View.Money}, section {race.Section}");
        Result.EngineBefore = race.EngineReport();
        Step("engine: " + Result.EngineBefore);
        return Result.BoughtCar;
    }

    private string? PlayerCarClass() => race.PlayerCar?.Class.Name;

    public bool EnterTrack()
    {
        var entered = race.Measure("enter track", () =>
        {
            if (!ClickWait("Choose Track", () => race.Section == "TrackSelect", 15))
            {
                Result.DriveableAnswer = string.Join(" ", Host.Driver.VisibleStrings().SkipWhile(s => !s.Contains("because", StringComparison.Ordinal)).Take(6));
                Step($"Choose Track did not reach TrackSelect: screen {Host.Screens.Current}; texts: {Result.DriveableAnswer}");
                return false;
            }

            Step("Garage -> Choose Track -> TrackSelect");
            Host.RunFor(1f);
            foreach (var track in Options.Tracks)
            {
                if (TryTrack(track))
                {
                    return true;
                }
            }

            return false;
        });
        Result.EnteredTrack = entered;
        return entered;
    }

    private bool ClickWait(string label, Func<bool> done, float timeout)
    {
        var end = Host.Vm.Time + 3f;
        while (!Host.Input.Click(label))
        {
            if (Host.Vm.Time > end)
            {
                return false;
            }

            Host.RunFrame();
        }

        return Host.RunUntil(done, timeout);
    }

    private bool TryTrack(string track)
    {
        if (race.Section != "TrackSelect")
        {
            Step($"not in TrackSelect ({race.Section}) before '{track}'");
            return false;
        }

        var clicked = false;
        for (var page = 0; page < 3 && !(clicked = Host.Input.Click(track)); page++)
        {
            if (!Host.Input.Click("Next Page") && !Host.Input.ClickCommand(TrackSelectCmdDown))
            {
                break;
            }

            Host.RunFor(0.5f);
        }

        if (!clicked)
        {
            Step($"'{track}' not found in TrackSelect");
            return false;
        }

        Host.RunFor(0.5f);
        Host.Input.Click("TAKE IT");
        var dialogDone = false;
        var ok = Host.RunUntil(() =>
        {
            if (!dialogDone && Host.Screens.Current.Contains("Options", StringComparison.Ordinal))
            {
                dialogDone = true;
                Host.Input.Click("OK");
            }

            return race.InTrack;
        }, 90);
        Result.Track = track;
        Result.TrackClass = Host.ActiveSection?.Class.Name;
        Step($"TAKE IT '{track}' -> {(ok ? "entered" : "NOT entered")} {Result.TrackClass} (screen {Host.Screens.Current})");
        if (!ok && race.Section != "TrackSelect" && Host.ActiveSection is { } s && Probe.Field(s, "parentState").AsObject is { } parent)
        {
            Host.CallStaticOnThread("java.game.GameLogic", "changeActiveSection", 30f, Value.Obj(parent));
        }

        return ok;
    }

    /// <summary>TrackSelect CMD_DOWN (page down), MEASURED from TrackSelect.class.</summary>
    public const int TrackSelectCmdDown = 4;

    // ------------------------------------------------------------------ driving

    public void Drive()
    {
        Host.RunFor(Options.SettleSeconds);
        var id = race.PlayerCarId;
        var car = race.Physics.Scene.Car(id);
        Result.CarBound = car is not null;
        Result.CarName = car?.Name;
        Result.CarMass = (float)(car?.Mass ?? 0);
        if (car is null)
        {
            Step($"player car {id:X} is not bound to the physics adapter");
            return;
        }

        var t0 = race.PlayerTelemetry;
        Result.Start = t0?.Position ?? default;
        Step(string.Create(CultureInfo.InvariantCulture,
            $"car {car.Name} {car.Mass:F0} kg at ({Result.Start.X:F1}, {Result.Start.Y:F1}, {Result.Start.Z:F1}), state {car.State}, input {car.Input?.GetType().Name ?? "none"}, map {race.MapSource.CurrentMap?.ArchiveName ?? "none"}"));

        var steps0 = race.Physics.Scene.Steps;
        var grounded = 0;
        var wheels = 0;
        var frames0 = Host.Frame;
        var sw = Stopwatch.StartNew();
        Host.Input.KeyDown(DikUp);
        var steerKey = Options.Steer > 0.01f ? DikLeft : Options.Steer < -0.01f ? DikRight : 0;
        if (steerKey != 0)
        {
            Host.Input.KeyDown(steerKey);
        }

        Step("throttle axis 28: " + race.InputReport(28));
        var end = Host.Vm.Time + Options.DriveSeconds;
        while (Host.Vm.Time < end - 1e-4f)
        {
            Host.RunFrame();
            if (race.PlayerTelemetry is not { } t)
            {
                continue;
            }

            var g = t.Wheels.Count(w => w.Grounded);
            grounded += g;
            wheels += t.Wheels.Count;
            Result.MaxSpeed = Math.Max(Result.MaxSpeed, t.Speed);
            Result.MaxRpm = Math.Max(Result.MaxRpm, t.Rpm);
            Result.MaxGear = Math.Max(Result.MaxGear, t.Gear);
            if (race.Audio?.AudioOf(id) is { } va)
            {
                Result.AudioEngineVoices = Math.Max(Result.AudioEngineVoices, va.EngineVoiceCount);
                Result.AudioTableRpmMax = Math.Max(Result.AudioTableRpmMax, va.LastTableRpm);
            }

            if (Host.Frame % 15 == 0)
            {
                Result.Samples.Add(new DriveSample(Host.Vm.Time, t.Position, t.Speed, t.Rpm, t.Gear, g, t.Throttle));
            }
        }

        Host.Input.KeyUp(DikUp);
        if (steerKey != 0)
        {
            Host.Input.KeyUp(steerKey);
        }

        Result.DriveWallMs = sw.Elapsed.TotalMilliseconds;
        Result.DriveFrames = (int)(Host.Frame - frames0);
        Result.PhysicsSteps = race.Physics.Scene.Steps - steps0;
        var c = race.Costs.GetValueOrDefault("drive");
        race.Costs["drive"] = (c.Frames + Result.DriveFrames, c.Ms + Result.DriveWallMs);
        var tEnd = race.PlayerTelemetry;
        Result.End = tEnd?.Position ?? default;
        Result.FinalSpeed = tEnd?.Speed ?? 0f;
        Result.GroundedFraction = wheels == 0 ? 0f : grounded / (float)wheels;
        Result.ScriptSpeedSquare = CallFloat(race.PlayerCar, "getSpeedSquare");
        Result.ScriptPosition = Host.Resources.Tree.Get(id) is { } node ? new Vector3(node.Pos[0], node.Pos[1], node.Pos[2]) : default;
        Step(string.Create(CultureInfo.InvariantCulture,
            $"drive {Options.DriveSeconds:F1}s: moved {Result.Distance:F1} m, max {Result.MaxSpeed * 3.6f:F1} km/h, final {Result.FinalSpeed * 3.6f:F1} km/h, " +
            $"rpm max {Result.MaxRpm:F0}, gear max {Result.MaxGear}, wheels grounded {Result.GroundedFraction:P1}, script getSpeedSquare {Result.ScriptSpeedSquare:F1}"));
    }

    /// <summary>Calls a no-argument instance method of a script object on its own thread (natives included) and returns its float.</summary>
    public float CallFloat(VmObject? obj, string method)
    {
        if (obj is null || obj.Class.FindMethods(method, 0).FirstOrDefault(m => !m.IsStatic) is not { } m)
        {
            return float.NaN;
        }

        var th = Host.Vm.Spawn(m, obj, [], "race:" + method);
        Host.Driver.Settle();
        return th.State == VmThreadState.Finished ? th.Result.AsFloat : float.NaN;
    }

    /// <summary>
    /// TestTrack acceleration round (MEASURED from TestTrack.enter: round[0] start trigger at (0,0,-1461.78) radius 10,
    /// startdir 3.14): put the player car there at rest the way a script <c>setMatrix</c> does (the physics adapter snaps it to
    /// the ground), wait for the script's timer to arm the round, then full throttle through the input layer until the
    /// script reports the 1 mile time. The results are the script's own fields of <c>player.car</c>.
    /// </summary>
    public void AccelerationRound()
    {
        var id = race.PlayerCarId;
        if (Host.Resources.Tree.Get(id) is not { } node || race.Physics.Scene.Car(id) is not { } car)
        {
            return;
        }

        var sw = Stopwatch.StartNew();
        node.Pos[0] = 0f;
        node.Pos[1] = 0f;
        node.Pos[2] = -1461.785f;
        node.Ori[0] = MathF.PI;
        node.Ori[1] = 0f;
        node.Ori[2] = 0f;
        car.Velocity = default;
        car.AngularVelocity = default;
        Host.RunFor(3f);
        var dist0 = race.PlayerTelemetry?.Odometer ?? 0;
        Host.Input.KeyDown(DikUp);
        var diag = Environment.GetEnvironmentVariable("SLRR_RACE_DIAG") == "1"; // every frame near Test Track's bridge (z -50..5)
        var triggers0 = race.CommandEngine.Race.TriggerLog.Count;
        var end = Host.Vm.Time + Options.AccelerationTimeout;
        var done = false;
        while (Host.Vm.Time < end && !(done = Field("bestTestTrackTime0") > 0.1f))
        {
            Host.RunFrame();
            if (race.PlayerTelemetry is { } t && (Host.Frame % 30 == 0 || (diag && t.Position.Z > -50 && t.Position.Z < 5)))
            {
                Result.AccelSamples.Add(new DriveSample(Host.Vm.Time, t.Position, t.Speed, t.Rpm, t.Gear, t.Wheels.Count(w => w.Grounded), t.Throttle));
            }
        }

        Host.Input.KeyUp(DikUp);
        Result.AccelTriggerEvents = race.CommandEngine.Race.TriggerLog.Skip(triggers0).Select(e => e.ToString()).ToList();
        var r = (Field("bestTestTrackTime2"), Field("bestTestTrackTime0"), Field("bestTestTrackAcc"), Field("bestTestTrackAcc120"),
            MathF.Sqrt(Math.Max(0f, Field("maxTestTrackSpeedSq"))) * 3.6f);
        Result.Acceleration = r;
        Step(string.Create(CultureInfo.InvariantCulture,
            $"acceleration round {(done ? "finished" : "NOT finished")} in {sw.Elapsed.TotalSeconds:F1} s wall: 1/4 mile {r.Item1:F3} s, 1 mile {r.Item2:F3} s, 0-100 {r.Item3:F3} s, 0-200 {r.Item4:F3} s, max {r.Item5:F1} km/h (script fields), driven {(race.PlayerTelemetry?.Odometer ?? 0) - dist0:F0} m"));
    }

    private float Field(string name) => Probe.Field(race.PlayerCar, name).AsFloat;
}
