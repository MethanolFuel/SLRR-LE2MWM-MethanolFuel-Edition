using System.Globalization;
using System.Numerics;

namespace Slrr.Engine.Commands;

/// <summary>What a bot brain is doing (set by the AI_* verbs).</summary>
public enum AiMode
{
    /// <summary>No AI verb yet: no input.</summary>
    Idle,

    /// <summary><c>AI_suspend</c>: no input until the next mode verb.</summary>
    Suspended,

    /// <summary><c>AI_stop</c>: brake to standstill.</summary>
    Stopped,

    /// <summary><c>AI_follow x,y,z id</c>.</summary>
    Follow,

    /// <summary><c>AI_race x,y,z opponent</c>.</summary>
    Race,

    /// <summary><c>AI_GoToTarget x,y,z [x,y,z]</c>.</summary>
    GoToTarget,

    /// <summary><c>AI_GoToTraffic</c> / <c>AI_GoToTrafficSlow</c>: the car belongs to the traffic director.</summary>
    Traffic,

    /// <summary><c>AI_spline f name d</c> (recorded, driven like GoToTarget towards nothing: no input).</summary>
    Spline,
}

/// <summary>
/// A controller (native kind <c>player</c>, java.io.Controller) or a bot brain (native kind <c>bot</c>, sl.rpk:0x6E):
/// the brain accepts the controller verbs too (MEASURED: Bot sends <c>controllable</c>/<c>leave</c>/<c>renderinstance</c>/
/// <c>camera</c>/<c>osd</c>/<c>viewport</c> to its brain; the exe has the controller format block twice).
/// </summary>
public sealed class ControllerState(int id, string? kind)
{
    public int Id { get; } = id;

    public string? Kind { get; } = kind;

    public bool IsBrain => Kind == "bot";

    /// <summary>Object driven by this controller (<c>controllable</c>): a car, or a camera for the free camera.</summary>
    public int Controlled { get; set; }

    public int Camera { get; set; }

    public int Viewport { get; set; }

    /// <summary>HUD Osd bound with <c>osd id</c> (0 = none).</summary>
    public int Osd { get; set; }

    public int RenderInstance { get; set; }

    public int Device { get; set; } = -1;

    public int Team { get; set; }

    public int Lost { get; set; }

    public int Control { get; set; }

    public int SetControlCount { get; set; }

    // ---- brain
    public AiMode Mode { get; set; }

    public float Level { get; set; } = 1f;

    public Vector2 Params { get; set; }

    public bool RaceArmed { get; set; }

    /// <summary><c>AI_BeginRace f</c> (stored as reaction delay in seconds, GUESSED).</summary>
    public float BeginRaceValue { get; set; }

    public float NightRace { get; set; } = -1f;

    public float Smooth { get; set; }

    public int Horn { get; set; }

    public int FollowId { get; set; }

    public Vector3 FollowOffset { get; set; }

    public Vector3 Target { get; set; }

    public Vector3? Target2 { get; set; }

    public int Opponent { get; set; }

    /// <summary>AI_race: the target point was reached (the car then keeps its heading).</summary>
    public bool Arrived { get; set; }

    public (float F, string? Name, int D) Spline { get; set; }

    /// <summary>Sim time the current mode started.</summary>
    public float ModeSince { get; set; }

    public DriveInput LastInput { get; set; }

    public List<string> Verbs { get; } = [];
}

/// <summary>A trigger volume read from its creation params (<c>x,y,z,0,0,0,sphere,R</c> or <c>...,box,a,b,c</c>; MEASURED from
/// java.game.Trigger and the exe's own <c>%.2f,%.2f,%.2f,0,0,0,sphere,%.2f</c> at 0x60db58).</summary>
public sealed record TriggerVolume(int Id, string Name, Vector3 Center, bool Sphere, float Radius, Vector3 Size);

/// <summary>One EVENT_TRIGGER_ON/OFF emission.</summary>
public sealed record TriggerEvent(float Time, int TriggerId, string TriggerName, int CarId, bool On);

/// <summary>Lap/checkpoint progress of one car on a route (host telemetry; not an exe verb).</summary>
public sealed class RouteProgress
{
    public required int CarId { get; init; }

    public required IReadOnlyList<Vector3> Points { get; init; }

    public required float Radius { get; init; }

    public required bool Loop { get; init; }

    public int Next { get; set; }

    public int Laps { get; set; }

    public float StartTime { get; set; } = float.NaN;

    public float LapStart { get; set; } = float.NaN;

    public List<float> Splits { get; } = [];

    public List<float> LapTimes { get; } = [];

    public bool Finished { get; set; }

    public bool WrongWay { get; set; }

    public float WrongWayTime { get; set; }
}

/// <summary>
/// Controllers, bot brains (AI driving), triggers and race telemetry.
/// MEASURED: verb formats, the brain/controller verb sharing, trigger params, event token 0 = car id
/// (City.event_handlerRaceFinish, TestTrack.event_handlerTrigger compare <c>p2.token(0).intValue()</c> with a car id),
/// <c>ai_info_entertraffic 0x%x 0x%x 0x%x</c> (0x60dbc8) posted to the car (Bot listens on its car with addNotification).
/// GUESSED: the AI driving model (pure pursuit, speeds from AI_level), what AI_BeginRace's float means, event ordering.
/// </summary>
public sealed class RaceDirector(IObjectDirectory world, IVehicleState vehicles, IVehicleDriver driver, IScriptEventSink events) : IRaceDirector
{
    public const int EventTriggerOn = 32;
    public const int EventTriggerOff = 64;
    public const int EventCommand = 16;

    private readonly Dictionary<int, ControllerState> _controllers = [];
    private readonly Dictionary<int, TriggerVolume?> _triggers = [];
    private readonly Dictionary<(int Trigger, int Car), bool> _inside = [];
    private readonly List<TriggerEvent> _log = [];
    private readonly Dictionary<int, RouteProgress> _routes = [];
    private readonly Dictionary<int, float> _crime = [];

    public ITrafficDirector? Traffic { get; set; }

    /// <summary>Which car-kind instances can set off triggers (default: all). The engine limits it to cars that behave as
    /// cars (received a car verb, are controlled or in traffic): GUESSED rule that keeps car-kind props such as the
    /// Navigator's 'Camera GPS Marker Base' and the catalogue's chassis instances out.</summary>
    public Func<int, bool> Participates { get; set; } = _ => true;

    /// <summary>Sim clock of this director (sum of Step dt).</summary>
    public float Time { get; private set; }

    /// <summary>Top AI speed for AI_level 0 (m/s, GUESSED).</summary>
    public float BaseAiSpeed { get; set; } = 22f;

    /// <summary>Extra AI speed per AI_level unit (m/s, GUESSED), capped by <see cref="MaxAiSpeed"/>.</summary>
    public float AiSpeedPerLevel { get; set; } = 4f;

    public float MaxAiSpeed { get; set; } = 70f;

    public IReadOnlyList<TriggerEvent> TriggerLog => _log;

    public IReadOnlyCollection<ControllerState> Controllers => _controllers.Values;

    public IReadOnlyCollection<TriggerVolume> Triggers => _triggers.Values.OfType<TriggerVolume>().ToList();

    public ControllerState? Controller(int id) => _controllers.GetValueOrDefault(id);

    public int OwnerOf(int objectId) =>
        objectId == 0 ? 0 : _controllers.Values.Where(c => c.Controlled == objectId).Select(c => c.Id).FirstOrDefault();

    /// <summary>The object the controller owning camera <paramref name="cameraId"/> drives (follow camera target).</summary>
    public int FollowTargetOfCamera(int cameraId) =>
        cameraId == 0 ? 0 : _controllers.Values.Where(c => c.Camera == cameraId && c.Controlled != cameraId).Select(c => c.Controlled).FirstOrDefault();

    public ControllerState GetOrCreate(ObjectInfo o)
    {
        if (!_controllers.TryGetValue(o.Id, out var c))
        {
            c = new ControllerState(o.Id, o.Kind);
            _controllers[o.Id] = c;
        }

        return c;
    }

    public bool Execute(ObjectInfo target, ParsedCommand cmd)
    {
        var c = GetOrCreate(target);
        c.Verbs.Add(cmd.Text);
        switch (cmd.Verb)
        {
            case "controllable" when cmd.Fields >= 1:
                c.Controlled = cmd.Int(0);
                return true;
            case "leave" when cmd.Fields >= 1:
                if (c.Controlled == cmd.Int(0))
                {
                    if (c.IsBrain && c.Controlled != 0)
                    {
                        driver.SetInput(c.Controlled, DriveInput.None);
                    }

                    c.Controlled = 0;
                }

                return true;
            case "camera" when cmd.Fields >= 1:
                c.Camera = cmd.Int(0);
                return true;
            case "viewport" when cmd.Fields >= 1:
                c.Viewport = cmd.Int(0);
                return true;
            case "osd" when cmd.Fields >= 1:
                c.Osd = cmd.Int(0);
                return true;
            case "renderinstance" when cmd.Fields >= 1:
                c.RenderInstance = cmd.Int(0);
                return true;
            case "device" when cmd.Fields >= 1:
                c.Device = cmd.Int(0);
                return true;
            case "team" when cmd.Fields >= 1:
                c.Team = cmd.Int(0);
                return true;
            case "lost" when cmd.Fields >= 1:
                c.Lost = cmd.Int(0);
                return true;
            case "control" when cmd.Fields >= 1:
                c.Control = cmd.Int(0);
                return true;
            case "setcontrol":
                c.SetControlCount++;
                return true;
        }

        return cmd.Verb.StartsWith("AI_", StringComparison.Ordinal) && ExecuteAi(target, c, cmd);
    }

    private bool ExecuteAi(ObjectInfo target, ControllerState c, ParsedCommand cmd)
    {
        switch (cmd.Verb)
        {
            case "AI_level":
                c.Level = cmd.Float(0, c.Level);
                return true;
            case "AI_params":
                c.Params = new Vector2(cmd.Float(0), cmd.Float(1));
                return true;
            case "AI_smooth":
                c.Smooth = cmd.Float(0);
                return true;
            case "AI_horn":
                c.Horn = cmd.Int(0);
                return true;
            case "AI_NightRace":
                c.NightRace = cmd.Float(0, 0f);
                return true;
            case "AI_BeginRace":
                c.RaceArmed = true;
                c.BeginRaceValue = cmd.Float(0);
                c.Mode = AiMode.Stopped;
                c.ModeSince = Time;
                return true;
            case "AI_suspend":
                SetMode(c, AiMode.Suspended);
                return true;
            case "AI_stop":
                SetMode(c, AiMode.Stopped);
                return true;
            case "AI_follow" when cmd.Fields >= 4:
                c.FollowOffset = cmd.Vec(0);
                c.FollowId = cmd.Int(3);
                SetMode(c, AiMode.Follow);
                return true;
            case "AI_race" when cmd.Fields >= 3:
                c.Target = cmd.Vec(0);
                c.Opponent = cmd.Int(3);
                SetMode(c, AiMode.Race);
                return true;
            case "AI_GoToTarget" when cmd.Fields >= 3:
                c.Target = cmd.Vec(0);
                c.Target2 = cmd.Fields >= 6 ? cmd.Vec(3) : null;
                SetMode(c, AiMode.GoToTarget);
                return true;
            case "AI_spline":
                c.Spline = (cmd.Float(0), cmd.Str(1), cmd.Int(2));
                SetMode(c, AiMode.Spline);
                return true;
            case "AI_GoToTraffic":
            case "AI_GoToTrafficSlow":
                SetMode(c, AiMode.Traffic);
                if (c.Controlled != 0 && Traffic is { } t)
                {
                    var map = MapOf(target);
                    var tid = t.Add(map, c.Controlled, null);
                    if (Traffic is TrafficDirector td)
                    {
                        td.SetSlow(tid, cmd.Verb == "AI_GoToTrafficSlow");
                    }

                    // MEASURED format (0x60dbc8); value order GUESSED so that Bot reads token(1) as the traffic id.
                    events.Queue(c.Controlled, target.Id, EventCommand,
                        string.Create(CultureInfo.InvariantCulture, $"ai_info_entertraffic 0x{tid:x} 0x{c.Controlled:x} 0x{target.Id:x}"));
                }

                return true;
            default:
                return false;
        }
    }

    private int MapOf(ObjectInfo o)
    {
        for (var p = world.Get(o.ParentId); p is not null; p = world.Get(p.ParentId))
        {
            if (p.Kind == "ground" || p.ScriptClass is null)
            {
                return p.Id;
            }
        }

        return o.ParentId;
    }

    private void SetMode(ControllerState c, AiMode m)
    {
        c.Mode = m;
        c.ModeSince = Time;
        c.Arrived = false;
    }

    /// <summary>Police/crime level returned by <c>Vehicle.hasCrime()</c> (GUESSED: 0 unless set by the host).</summary>
    public float CrimeOf(int carId) => _crime.GetValueOrDefault(carId);

    public void SetCrime(int carId, float value) => _crime[carId] = value;

    // ------------------------------------------------------------------ routes (telemetry)

    /// <summary>Tracks a car over checkpoints (lap/split times, wrong-way); host telemetry, GUESSED rules.</summary>
    public RouteProgress TrackRoute(int carId, IReadOnlyList<Vector3> points, float radius, bool loop)
    {
        var r = new RouteProgress { CarId = carId, Points = points, Radius = radius, Loop = loop };
        _routes[carId] = r;
        return r;
    }

    public RouteProgress? Route(int carId) => _routes.GetValueOrDefault(carId);

    // ------------------------------------------------------------------ step

    public void Step(float dt)
    {
        Time += dt;
        StepTriggers();
        foreach (var c in _controllers.Values.ToList())
        {
            if (world.Get(c.Id) is null)
            {
                _controllers.Remove(c.Id);
                continue;
            }

            if (c.IsBrain && c.Controlled != 0 && c.Mode != AiMode.Traffic)
            {
                var input = DriveFor(c);
                c.LastInput = input;
                driver.SetInput(c.Controlled, input);
            }
        }

        StepRoutes(dt);
    }

    private void StepTriggers()
    {
        foreach (var t in world.OfKind("trigger"))
        {
            if (!_triggers.ContainsKey(t.Id))
            {
                _triggers[t.Id] = ParseTrigger(t);
            }
        }

        foreach (var id in _triggers.Keys.Where(id => world.Get(id) is null).ToList())
        {
            _triggers.Remove(id);
            foreach (var k in _inside.Keys.Where(k => k.Trigger == id).ToList())
            {
                _inside.Remove(k);
            }
        }

        var cars = world.OfKind("car").Where(c => Participates(c.Id)).ToList();
        var changes = new List<TriggerEvent>();
        foreach (var tv in _triggers.Values.OfType<TriggerVolume>().OrderBy(t => t.Id))
        {
            foreach (var car in cars)
            {
                if (!vehicles.TryGetVehicle(car.Id, out var pose))
                {
                    continue;
                }

                var inside = Contains(tv, pose.Position);
                if (inside != _inside.GetValueOrDefault((tv.Id, car.Id)))
                {
                    _inside[(tv.Id, car.Id)] = inside;
                    changes.Add(new TriggerEvent(Time, tv.Id, tv.Name, car.Id, inside));
                }
            }
        }

        // OFF before ON within one step (GUESSED order): TestTrack.event_handlerTrigger keeps the first trigger entered and
        // clears it on any OFF, so a car jumping from one trigger into another must leave first.
        foreach (var e in changes.Where(e => !e.On).Concat(changes.Where(e => e.On)))
        {
            _log.Add(e);
            events.Queue(e.TriggerId, e.CarId, e.On ? EventTriggerOn : EventTriggerOff, e.CarId.ToString(CultureInfo.InvariantCulture));
        }
    }

    private static bool Contains(TriggerVolume t, Vector3 p)
    {
        if (t.Sphere)
        {
            return Vector3.DistanceSquared(t.Center, p) <= t.Radius * t.Radius;
        }

        var d = Vector3.Abs(p - t.Center);
        return d.X <= t.Size.X / 2 && d.Y <= t.Size.Y / 2 && d.Z <= t.Size.Z / 2;
    }

    /// <summary>Trigger volume from its params; position from the instance (ApplyPlacement read the first three numbers).</summary>
    public TriggerVolume? ParseTrigger(ObjectInfo t)
    {
        var parts = (t.Params ?? string.Empty).Split(',', StringSplitOptions.TrimEntries);
        if (parts.Length < 3)
        {
            return null;
        }

        var center = world.TryGetPose(t.Id, out var p) ? p.Position : default;
        var shape = parts.Length > 6 ? parts[6].ToLowerInvariant() : "sphere";
        float F(int i, float d) => i < parts.Length && float.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : d;
        return shape == "box"
            ? new TriggerVolume(t.Id, t.Name, center, false, 0, new Vector3(F(7, 1), F(8, 1), F(9, 1)))
            : new TriggerVolume(t.Id, t.Name, center, true, F(7, 20f), default);
    }

    private DriveInput DriveFor(ControllerState c)
    {
        if (!vehicles.TryGetVehicle(c.Controlled, out var me))
        {
            return DriveInput.None;
        }

        switch (c.Mode)
        {
            case AiMode.Stopped:
                return DriveInput.FullBrake;
            case AiMode.Follow:
                {
                    if (!vehicles.TryGetVehicle(c.FollowId, out var tp) && !world.TryGetPose(c.FollowId, out tp))
                    {
                        return DriveInput.FullBrake;
                    }

                    var aim = tp.ToWorld(c.FollowOffset);
                    var dist = Vector3.Distance(aim, me.Position);
                    var want = Math.Clamp(tp.Speed + ((dist - 8f) * 0.8f), 0f, TopSpeed(c));
                    return Pursue(me, aim, want);
                }

            case AiMode.Race:
                if (c.RaceArmed && Time - c.ModeSince < Math.Max(0f, c.BeginRaceValue))
                {
                    return DriveInput.FullBrake;
                }

                if (!c.Arrived && Vector3.Distance(c.Target, me.Position) < 15f)
                {
                    c.Arrived = true;
                }

                // Through the finish: keep the heading once the target point was reached (the scripts then send brake/AI_stop).
                return Pursue(me, c.Arrived ? me.Position + (me.Orientation.Forward * 100f) : c.Target, TopSpeed(c));
            case AiMode.GoToTarget:
                {
                    var dist = Vector3.Distance(c.Target, me.Position);
                    return dist < 5f ? DriveInput.FullBrake : Pursue(me, c.Target, Math.Min(TopSpeed(c), dist * 0.5f + 3f));
                }

            default:
                return DriveInput.None;
        }
    }

    private float TopSpeed(ControllerState c) => Math.Clamp(BaseAiSpeed + (AiSpeedPerLevel * c.Level), 5f, MaxAiSpeed);

    /// <summary>Pure pursuit: steer towards <paramref name="aim"/>, throttle/brake towards <paramref name="speed"/> (GUESSED controller).</summary>
    public static DriveInput Pursue(Pose me, Vector3 aim, float speed)
    {
        var to = aim - me.Position;
        to.Y = 0;
        if (to.LengthSquared() < 1e-4f)
        {
            return DriveInput.FullBrake;
        }

        var want = Ypr.FromDirection(to).Yaw;
        var err = Ypr.AngleDiff(want, me.Orientation.Yaw);

        // Yaw grows to the left in this frame (forward = (-sin y, 0, -cos y)), so a positive error means steer left (-).
        var steer = Math.Clamp(-err * 2f, -1f, 1f);
        var v = me.Speed;
        var dv = speed - v;
        return dv >= 0 ? new DriveInput(Math.Clamp(dv * 0.5f, 0f, 1f), 0f, steer) : new DriveInput(0f, Math.Clamp(-dv * 0.3f, 0f, 1f), steer);
    }

    private void StepRoutes(float dt)
    {
        foreach (var r in _routes.Values)
        {
            if (r.Finished || r.Points.Count == 0 || !vehicles.TryGetVehicle(r.CarId, out var pose))
            {
                continue;
            }

            var n = r.Points.Count;
            if (Vector3.Distance(r.Points[r.Next], pose.Position) <= r.Radius)
            {
                if (float.IsNaN(r.StartTime))
                {
                    r.StartTime = r.LapStart = Time;
                    r.Next = n > 1 ? 1 : 0;
                }
                else
                {
                    r.Splits.Add(Time - r.LapStart);
                    if (r.Next == 0)
                    {
                        // a loop's start point is also its finish line
                        r.Laps++;
                        r.LapTimes.Add(Time - r.LapStart);
                        r.LapStart = Time;
                        r.Next = n > 1 ? 1 : 0;
                    }
                    else if (r.Next == n - 1 && !r.Loop)
                    {
                        r.Finished = true;
                    }
                    else
                    {
                        r.Next = (r.Next + 1) % n;
                    }
                }
            }

            // Wrong way (GUESSED): moving faster than 2 m/s away from the next checkpoint for 2 s.
            var dir = r.Points[r.Next] - pose.Position;
            var away = pose.Speed > 2f && Vector3.Dot(dir, pose.Velocity) < 0;
            r.WrongWayTime = away ? r.WrongWayTime + dt : 0;
            r.WrongWay = r.WrongWayTime >= 2f;
        }
    }
}
