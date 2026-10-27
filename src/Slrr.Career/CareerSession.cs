using System.Globalization;
using System.Numerics;
using Slrr.Engine.Resources;
using Slrr.Engine.Ui;
using Slrr.Game;
using Slrr.Race;
using Slrr.Vm;

namespace Slrr.Career;

/// <summary>Switches of a <see cref="CareerSession"/>.</summary>
public sealed class CareerOptions
{
    public bool Render { get; init; }

    public string? CaptureDir { get; init; }

    public int CaptureEvery { get; init; }

    public TextWriter? ScriptLog { get; init; }

    public CollisionDamageOptions Damage { get; init; } = new();

    /// <summary>Echo every transcript line here as it is written (CLI progress).</summary>
    public TextWriter? Echo { get; init; }
}

/// <summary>
/// One career run on a <see cref="RaceHost"/> with the career pieces added: <see cref="CareerNatives"/> (the shipped
/// cheat-code check) and the <see cref="CollisionDamageBridge"/>. Holds the transcript (screens reached and steps) and the
/// helpers the race-type flows use: clicking through menus, career state views, the test-only career setter and the
/// stand-in driver.
/// </summary>
public sealed class CareerSession : IDisposable
{
    private int _visitsLogged;

    private CareerSession(RaceHost race, CareerNatives natives, CareerOptions options)
    {
        Race = race;
        Natives = natives;
        Options = options;
        Damage = new CollisionDamageBridge(race.Host, race.Physics, () => race.MapSource.CurrentMap is { } m ? MapInstanceId(m.ArchiveName) : 0, options.Damage);
        Driver = new StandInDriver(this);
        Cheats = new CareerTestState(this);
    }

    public static CareerSession Create(string root, CareerOptions? options = null)
    {
        options ??= new CareerOptions();
        var natives = new CareerNatives();
        var race = RaceHost.Create(root, new RaceHostOptions
        {
            Render = options.Render,
            CaptureDir = options.CaptureDir,
            CaptureEvery = options.CaptureEvery,
            ScriptLog = options.ScriptLog,
            MapRenderScene = options.Render,
            ExtraModuleFactories = [_ => natives],
        });
        return new CareerSession(race, natives, options);
    }

    public RaceHost Race { get; }

    public GameHost Host => Race.Host;

    public CareerNatives Natives { get; }

    public CareerOptions Options { get; }

    public CollisionDamageBridge Damage { get; }

    public StandInDriver Driver { get; }

    /// <summary>TEST-ONLY career state writes (money, prestige, club, time). Every use is logged as "TEST-ONLY".</summary>
    public CareerTestState Cheats { get; }

    /// <summary>Steps and screens in order.</summary>
    public List<string> Transcript { get; } = [];

    /// <summary>Distinct screens (section > dialog "header") in the order first reached.</summary>
    public List<string> Screens { get; } = [];

    // ------------------------------------------------------------------ transcript

    public void Step(string text)
    {
        SyncScreens();
        var line = string.Create(CultureInfo.InvariantCulture, $"[{Host.Vm.Time,8:F2}s] {text}");
        Transcript.Add(line);
        Options.Echo?.WriteLine(line);
    }

    /// <summary>Copies new screen visits into the transcript.</summary>
    public void SyncScreens()
    {
        var visits = Host.Screens.Visits;
        for (; _visitsLogged < visits.Count; _visitsLogged++)
        {
            var v = visits[_visitsLogged];
            var line = "  screen " + v;
            Transcript.Add(line);
            Options.Echo?.WriteLine(line);
            var key = v.Key + (v.Header is null ? string.Empty : $" \"{v.Header}\"");
            if (!Screens.Contains(key))
            {
                Screens.Add(key);
            }
        }
    }

    public string State() => string.Create(CultureInfo.InvariantCulture,
        $"section {Section}, screen {Host.Screens.Current}, money ${Money}, prestige {Prestige:0.####}, club {Club}, rank {Rank}, day {Day}, time {TimeOfDay}");

    // ------------------------------------------------------------------ views

    public string Section => Race.Section;

    public VmObject? Player => Host.Player;

    public VmObject? Car => Race.PlayerCar;

    public int CarId => Race.PlayerCarId;

    public int Money => Probe.Field(Player, "money").AsInt;

    public float Prestige => Probe.Field(Player, "prestige").AsFloat;

    public int Club => Probe.Field(Player, "club").AsInt;

    public int RacesWon => Probe.Field(Player, "races_won").AsInt;

    public int RacesLost => Probe.Field(Player, "races_lost").AsInt;

    public int Day => Probe.Static(Host.Vm, "java.game.GameLogic", "day").AsInt;

    /// <summary>Game clock (GameLogic.time, seconds after midnight) as hh:mm.</summary>
    public string TimeOfDay
    {
        get
        {
            var t = Probe.Static(Host.Vm, "java.game.GameLogic", "time").AsFloat;
            var h = (int)(t / 3600);
            return string.Create(CultureInfo.InvariantCulture, $"{h:00}:{(int)(t - h * 3600) / 60:00}");
        }
    }

    /// <summary>Index of the player in <c>GameLogic.speedymen</c> (0 = last of club 0, 59 = champion), -1 if unknown.</summary>
    public int Rank
    {
        get
        {
            if (Probe.Static(Host.Vm, "java.game.GameLogic", "speedymen").AsObject is not VmArray a || Player is null)
            {
                return -1;
            }

            for (var i = 0; i < a.Items.Length; i++)
            {
                if (ReferenceEquals(a.Items[i].AsObject, Player))
                {
                    return i;
                }
            }

            return -1;
        }
    }

    public IEnumerable<string> TopTexts() => Host.Screens.TopTexts();

    public IEnumerable<string> Choices() => Host.Screens.Choices();

    public VmObject? ActiveSection => Host.ActiveSection;

    public int MapInstanceId(string? archive)
    {
        if (ActiveSection is { } s && Probe.Field(s, "map").AsObject is { } map)
        {
            return ResourceEngine.IdOf(map);
        }

        return 0;
    }

    // ------------------------------------------------------------------ actions

    /// <summary>Clicks a gadget once it is clickable (up to <paramref name="appear"/> s), logs it.</summary>
    public bool Click(string label, float appear = 5f, bool contains = false)
    {
        var end = Host.Vm.Time + appear;
        while (!Host.Input.Click(label, contains))
        {
            if (Host.Vm.Time > end)
            {
                Step($"click '{label}': NOT FOUND on {Host.Screens.Current}; choices: {string.Join(" | ", Choices().Take(30))}");
                return false;
            }

            Host.RunFrame();
        }

        Step($"click '{label}'");
        return true;
    }

    public bool WaitFor(Func<bool> done, float seconds, string what)
    {
        var ok = Host.RunUntil(done, seconds);
        Step($"{(ok ? "reached" : "TIMEOUT waiting for")} {what} ({Host.Screens.Current})");
        return ok;
    }

    public bool WaitSection(string section, float seconds = 30f) => WaitFor(() => Section == section, seconds, "section " + section);

    /// <summary>True while a modal dialog (a <c>java.render.osd.dialog.Dialog</c> owns the top Osd) is on screen.</summary>
    public bool DialogOnTop =>
        Host.TopOsd is { } osd && Probe.Field(osd, "globalHandler").AsObject is { } h && h.Class.IsSubclassOf("java.render.osd.dialog.Dialog");

    /// <summary>
    /// Calls an instance method of a script object on its own script thread and pumps frames until it ends (it may open
    /// dialogs or sleep). Returns the result, or null when it did not end in <paramref name="maxSeconds"/>.
    /// </summary>
    public Value? Call(VmObject obj, string method, float maxSeconds, params Value[] args)
    {
        var m = obj.Class.FindMethods(method, args.Length).FirstOrDefault(x => !x.IsStatic && x.Code is not null)
                ?? throw new VmException($"{obj.Class.Name} has no {method} with {args.Length} arguments");
        var t = Host.Vm.Spawn(m, obj, args, "career:" + method);
        Host.Driver.Settle();
        return Host.RunUntil(() => t.State == VmThreadState.Finished, maxSeconds) ? t.Result : null;
    }

    public Value Str(string s) => Value.Obj(Host.Vm.NewString(s));

    /// <summary>
    /// Reads and closes informational dialogs on top (WarningDialog and the like) by clicking their OK/CONTINUE button,
    /// logging their texts, up to <paramref name="max"/> dialogs. Returns the texts read.
    /// </summary>
    public List<string> Dismiss(int max = 4, float settle = 0.5f)
    {
        var read = new List<string>();
        for (var i = 0; i < max; i++)
        {
            Host.RunFor(settle);
            if (!DialogOnTop)
            {
                break;
            }

            var text = string.Join(" | ", TopTexts().Take(6)).Replace('\n', ' ');
            read.Add($"{TopDialog}: {text}");
            Step($"dialog {TopDialog}: {text}");
            var before = Host.TopOsd;
            if (!Host.Input.Click("OK") && !Host.Input.Click("CONTINUE") && !Host.Input.Click("YES"))
            {
                Host.Input.TapAxis(34); // AXIS_SELECT
            }

            Host.RunUntil(() => !ReferenceEquals(Host.TopOsd, before), 3);
        }

        return read;
    }

    /// <summary>The top dialog's class name (short), or null.</summary>
    public string? TopDialog => Host.Screens.Last?.Dialog;

    /// <summary>
    /// Types one of the shipped cheat codes into the 16-character ring that <c>kismajomCheck</c> reads (see
    /// <see cref="TypedRing"/>) and clicks Garage "Fine tune specific parts", which calls the check. The shipped game's own
    /// mechanism, but a cheat: transcripts mark it CHEAT.
    /// </summary>
    public bool ShippedCheat(string typed)
    {
        Natives.Typed.Type(typed);
        Step($"CHEAT (shipped code) typed '{typed}'");
        var before = Natives.Checks.Count;
        if (!Click("Fine tune specific parts"))
        {
            return false;
        }

        Host.RunFor(1.5f);
        var r = Natives.Checks.Count > before ? Natives.Checks[^1] : -2;
        Step($"kismajomCheck -> {r}; {State()}");
        if (Section == "Garage" && Probe.Field(ActiveSection, "mode").AsInt != 0 && r < 0)
        {
            Host.Input.TapAxis(35); // AXIS_CANCEL leaves the tuning mode
            Host.RunFor(1f);
        }

        return r >= 0;
    }

    public void Dispose() => Race.Dispose();
}

/// <summary>
/// TEST-ONLY writes into the career state (script fields of <c>GameLogic.player</c> and GameLogic statics), for reaching
/// a race type without playing the career up to it. Nothing in the shipped game does this; every call is logged in the
/// transcript with "TEST-ONLY". Values are written as the scripts would hold them (money int, prestige float 0..1).
/// </summary>
public sealed class CareerTestState(CareerSession s)
{
    public void SetMoney(int money)
    {
        Write(s.Player, "money", Value.Int(money));
        s.Step($"TEST-ONLY set player.money = {money}");
    }

    public void SetPrestige(float prestige)
    {
        Write(s.Player, "prestige", Value.Float(prestige));
        s.Step(string.Create(CultureInfo.InvariantCulture, $"TEST-ONLY set player.prestige = {prestige}"));
    }

    /// <summary>GameLogic.setTime(seconds after midnight) on a script thread (also syncs the engine clock like the script does).</summary>
    public void SetTime(float secondsAfterMidnight)
    {
        s.Host.CallStaticOnThread("java.game.GameLogic", "setTime", 5f, Value.Float(secondsAfterMidnight));
        s.Step(string.Create(CultureInfo.InvariantCulture, $"TEST-ONLY GameLogic.setTime({secondsAfterMidnight}) -> {s.TimeOfDay}"));
    }

    private static void Write(VmObject? obj, string field, Value v)
    {
        if (obj?.Class.FindField(field) is { IsStatic: false } f)
        {
            obj.Fields[f.Slot] = VmTypes.Coerce(v, f.Descriptor);
        }
    }
}

/// <summary>
/// Stand-in for the missing AI/road layer on the PLAYER side: drives the player's car towards a target point through the
/// game's input layer (virtual axis overrides of AXIS_TURN_LEFTRIGHT / AXIS_THROTTLE / AXIS_BRAKE, the axes the shipped
/// control set maps the keyboard to), or places it like a script <c>setMatrix</c> does. A test harness, not game logic.
/// </summary>
public sealed class StandInDriver(CareerSession s)
{
    public const int AxisSteer = 1;
    public const int AxisThrottle = 28;
    public const int AxisBrake = 29;
    public const int AxisHorn = 54;

    /// <summary>Places the player's car (instance node pose, physics snaps it to the ground on its next step) at rest.</summary>
    public void Place(Vector3 pos, float yaw)
    {
        var id = s.CarId;
        if (s.Host.Resources.Tree.Get(id) is { } node)
        {
            node.Pos[0] = pos.X;
            node.Pos[1] = pos.Y;
            node.Pos[2] = pos.Z;
            node.Ori[0] = yaw;
            node.Ori[1] = 0f;
            node.Ori[2] = 0f;
        }

        if (s.Race.Physics.Scene.Car(id) is { } car)
        {
            car.Velocity = default;
            car.AngularVelocity = default;
        }

        s.Step(string.Create(CultureInfo.InvariantCulture, $"STAND-IN place player car at ({pos.X:F1}, {pos.Y:F1}, {pos.Z:F1}) yaw {yaw:F2}"));
    }

    /// <summary>
    /// Pure-pursuit towards <paramref name="target"/> until <paramref name="done"/> holds or the time runs out: steer
    /// proportional to the bearing, throttle until within <paramref name="slowRadius"/>, then coast/brake.
    /// Returns the final distance to the target (NaN when the car has no telemetry).
    /// </summary>
    public float DriveTo(Vector3 target, Func<bool> done, float seconds, float maxSpeed = 25f, float slowRadius = 0f)
    {
        var end = s.Host.Vm.Time + seconds;
        var ui = s.Host.UiEngine.Input;
        var dist = float.NaN;
        var maxKmh = 0f;
        while (s.Host.Vm.Time < end && !done())
        {
            if (s.Race.PlayerTelemetry is { } t)
            {
                var to = target - t.Position;
                to.Y = 0;
                dist = to.Length();
                var fwd = Vector3.Transform(-Vector3.UnitZ, t.Orientation) /* body frame: -Z forward (Slrr.Game.Physics) */;
                fwd.Y = 0;
                if (fwd.LengthSquared() > 1e-6f && to.LengthSquared() > 1e-6f)
                {
                    fwd = Vector3.Normalize(fwd);
                    var dir = Vector3.Normalize(to);
                    var cross = fwd.Z * dir.X - fwd.X * dir.Z;
                    var dot = Vector3.Dot(fwd, dir);
                    var angle = MathF.Atan2(cross, dot);
                    ui.SetVirtualOverride(AxisSteer, Math.Clamp(angle * 2f, -1f, 1f) * SteerSign);
                }

                var slow = slowRadius > 0 && dist < slowRadius;
                ui.SetVirtualOverride(AxisThrottle, !slow && t.Speed < maxSpeed ? 1f : 0f);
                ui.SetVirtualOverride(AxisBrake, t.Speed > maxSpeed * 1.15f ? 0.5f : 0f);
                maxKmh = Math.Max(maxKmh, t.SpeedKmh);
            }

            s.Host.RunFrame();
        }

        Release();
        s.Step(string.Create(CultureInfo.InvariantCulture,
            $"STAND-IN drive to ({target.X:F1}, {target.Z:F1}): {(done() ? "done" : "not done")}, distance {dist:F1} m, max {maxKmh:F0} km/h"));
        return dist;
    }

    /// <summary>+1 or -1: the sign that maps "target to the left" onto AXIS_TURN_LEFTRIGHT (+1 = left, docs/race/integration.md).</summary>
    public float SteerSign { get; set; } = 1f;

    public void Hold(int axis, float value) => s.Host.UiEngine.Input.SetVirtualOverride(axis, value);

    public void Release()
    {
        var ui = s.Host.UiEngine.Input;
        foreach (var a in new[] { AxisSteer, AxisThrottle, AxisBrake, AxisHorn })
        {
            ui.SetVirtualOverride(a, null);
        }
    }
}
