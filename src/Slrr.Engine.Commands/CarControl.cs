using System.Numerics;

namespace Slrr.Engine.Commands;

/// <summary>Hold state of a car set by <c>start</c>/<c>stop</c>/<c>idle</c>/<c>brake</c> (semantics GUESSED from the call sites:
/// City.startRace sends <c>reset, stop, idle</c> at the start line and <c>start</c> on the green light; TestTrack
/// sends <c>brake</c> or <c>stop</c> before a dialog and <c>start</c> after it).</summary>
public enum CarHold
{
    /// <summary>Drivable (initial state, and after <c>start</c>).</summary>
    Free,

    /// <summary><c>stop</c>: held in place, no drive.</summary>
    Stopped,

    /// <summary><c>idle</c>: held with the engine idling.</summary>
    Idle,

    /// <summary><c>brake</c>: full brake until <c>start</c>.</summary>
    Braking,
}

/// <summary>
/// Command-driven control state of one car: everything the car verbs set, for the physics adapter to read
/// (Slrr.Physics) and for <see cref="KinematicVehicles"/>. Serial counters let a consumer notice one-shot verbs
/// (<c>reset</c>, <c>reload</c>, <c>setvel</c>, <c>wakeup</c>) without a callback.
/// </summary>
public sealed class CarControlState(int carId)
{
    public int CarId { get; } = carId;

    public CarHold Hold { get; set; }

    public bool Suspended { get; set; }

    /// <summary><c>setsteer f</c> (scripts send 0 at race start and random values in menus).</summary>
    public float? SteerOverride { get; set; }

    public int Horn { get; set; }

    public Dictionary<int, int> Filters { get; } = [];

    public (int A, int B) Corpse { get; set; }

    public (int A, int B) Osd { get; set; }

    public (int A, int B) Texture { get; set; }

    public int Mesh { get; set; }

    public int Team { get; set; }

    public int Transmission { get; set; } = -1;

    /// <summary><c>abs</c>, <c>asr</c>, <c>esp</c>, <c>difflock</c>, <c>steerhelp</c>, <c>cruise</c>, <c>damage_multiplier</c>, <c>setpitch</c>, <c>addroll</c>.</summary>
    public Dictionary<string, float> Settings { get; } = new(StringComparer.Ordinal);

    public bool Autopilot { get; set; }

    public float[]? AiCheat { get; set; }

    public (Vector3 Linear, Vector3 Angular)? RequestedVelocity { get; set; }

    public int InteriorCamera { get; set; }

    public int ResetSerial { get; set; }

    public int ReloadSerial { get; set; }

    public int WakeSerial { get; set; }

    public int VelocitySerial { get; set; }

    public bool FlapOpen { get; set; }

    public List<string> Verbs { get; } = [];
}

/// <summary>
/// The car verbs (group 0x610474..0x6108fc minus the part-structure verbs, which Slrr.Engine.Parts owns). Formats MEASURED;
/// effects GUESSED (see <see cref="CarHold"/>); the physics adapter is the consumer.
/// </summary>
public sealed class CarControl : ICarControl
{
    private readonly Dictionary<int, CarControlState> _cars = [];

    public IReadOnlyCollection<CarControlState> Cars => _cars.Values;

    public CarControlState? Get(int carId) => _cars.GetValueOrDefault(carId);

    public CarControlState GetOrCreate(int carId)
    {
        if (!_cars.TryGetValue(carId, out var s))
        {
            s = new CarControlState(carId);
            _cars[carId] = s;
        }

        return s;
    }

    public void Forget(int carId) => _cars.Remove(carId);

    public bool Execute(ObjectInfo target, ParsedCommand cmd)
    {
        var s = GetOrCreate(target.Id);
        s.Verbs.Add(cmd.Text);
        switch (cmd.Verb)
        {
            case "start":
                s.Hold = CarHold.Free;
                return true;
            case "stop":
                s.Hold = CarHold.Stopped;
                return true;
            case "idle":
                s.Hold = CarHold.Idle;
                return true;
            case "brake":
                s.Hold = CarHold.Braking;
                return true;
            case "reset":
                s.ResetSerial++;
                return true;
            case "reload":
                s.ReloadSerial++;
                return true;
            case "suspend":
                s.Suspended = true;
                return true;
            case "wakeup":
                s.Suspended = false;
                s.WakeSerial++;
                return true;
            case "setsteer" when cmd.Fields >= 1:
                s.SteerOverride = cmd.Float(0);
                return true;
            case "sethorn" when cmd.Fields >= 1:
                s.Horn = cmd.Int(0);
                return true;
            case "filter" when cmd.Fields >= 2:
                s.Filters[cmd.Int(0)] = cmd.Int(1);
                return true;
            case "corpse" when cmd.Fields >= 1:
                s.Corpse = (cmd.Int(0), cmd.Int(1));
                return true;
            case "osd" when cmd.Fields >= 1:
                s.Osd = (cmd.Int(0), cmd.Int(1));
                return true;
            case "texture" when cmd.Fields >= 1:
                s.Texture = (cmd.Int(0), cmd.Int(1));
                return true;
            case "mesh" when cmd.Fields >= 1:
                s.Mesh = cmd.Int(0);
                return true;
            case "team" when cmd.Fields >= 1:
                s.Team = cmd.Int(0);
                return true;
            case "transmission" when cmd.Fields >= 1:
                s.Transmission = cmd.Int(0);
                return true;
            case "abs" or "asr" or "esp" or "difflock" or "steerhelp" or "cruise" or "damage_multiplier" or "setpitch" or "addroll" when cmd.Fields >= 1:
                s.Settings[cmd.Verb] = cmd.Float(0);
                return true;
            case "setvel" when cmd.Fields >= 3:
                s.RequestedVelocity = (cmd.Vec(0), cmd.Fields >= 6 ? cmd.Vec(3) : default);
                s.VelocitySerial++;
                return true;
            case "autopilot_on":
                s.Autopilot = true;
                return true;
            case "autopilot_off":
                s.Autopilot = false;
                return true;
            case "ai_cheat":
                s.AiCheat = cmd.Values.OfType<float>().ToArray();
                return true;
            case "flap_toggle":
                s.FlapOpen = !s.FlapOpen;
                return true;
            case "activate" when cmd.Fields >= 1:
            case "next":
                s.InteriorCamera++;
                return true;
            case "prev":
                s.InteriorCamera = Math.Max(0, s.InteriorCamera - 1);
                return true;
            case "deactivate":
            case "pickup":
            case "cancel":
                return true;
            default:
                return false;
        }
    }
}

/// <summary>Mouse cursor and map verbs that only set state (cursor visibility/lock, map observers, fog).</summary>
public sealed class SceneCommands
{
    public sealed class CursorState(int id)
    {
        public int Id { get; } = id;

        public bool Enabled { get; set; } = true;

        public bool Locked { get; set; }

        public bool Standby { get; set; }

        public int SensReads { get; set; }

        public (int Viewport, int Camera, int Cursor) Binding { get; set; }

        public (int A, string? Pointer, int B) Mode { get; set; }

        public int Color { get; set; }

        public (int A, int B) Active { get; set; }

        public Vector2 Position { get; set; }
    }

    private readonly Dictionary<int, CursorState> _cursors = [];
    private readonly Dictionary<int, SortedSet<int>> _observers = [];

    public IReadOnlyCollection<CursorState> Cursors => _cursors.Values;

    public CursorState? Cursor(int id) => _cursors.GetValueOrDefault(id);

    /// <summary>Objects registered with <c>obs_add</c> on a map (GUESSED role: centres for streaming/traffic/pedestrians).</summary>
    public IReadOnlyCollection<int> Observers(int mapId) => _observers.TryGetValue(mapId, out var s) ? s : [];

    public Dictionary<int, int> Fog { get; } = [];

    public bool ExecuteCursor(ObjectInfo target, ParsedCommand cmd)
    {
        if (!_cursors.TryGetValue(target.Id, out var c))
        {
            c = new CursorState(target.Id);
            _cursors[target.Id] = c;
        }

        switch (cmd.Verb)
        {
            case "enable":
                c.Enabled = true;
                return true;
            case "disable":
                c.Enabled = false;
                return true;
            case "lock":
                c.Locked = true;
                return true;
            case "unlock":
                c.Locked = false;
                return true;
            case "standby":
                c.Standby = true;
                return true;
            case "sens":
                c.SensReads++;
                return true;
            case "cursor":
                c.Binding = (cmd.Int(0), cmd.Int(1), cmd.Int(2));
                return true;
            case "mode":
                c.Mode = (cmd.Int(0), cmd.Str(1), cmd.Int(2));
                return true;
            case "color":
                c.Color = cmd.Int(0);
                return true;
            case "activate":
                c.Active = (cmd.Int(0), cmd.Int(1));
                return true;
            case "deactivate":
                c.Active = default;
                return true;
            case "move" when cmd.Fields >= 2:
                c.Position = new Vector2(cmd.Float(0), cmd.Float(1));
                return true;
            case "relmove" when cmd.Fields >= 2:
                c.Position += new Vector2(cmd.Float(0), cmd.Float(1));
                return true;
            default:
                return false;
        }
    }

    public bool ExecuteGround(ObjectInfo target, ParsedCommand cmd)
    {
        switch (cmd.Verb)
        {
            case "obs_add" when cmd.Fields >= 1:
                if (!_observers.TryGetValue(target.Id, out var s))
                {
                    s = [];
                    _observers[target.Id] = s;
                }

                s.Add(cmd.Int(0));
                return true;
            case "obs_rem" when cmd.Fields >= 1:
                if (_observers.TryGetValue(target.Id, out var r))
                {
                    r.Remove(cmd.Int(0));
                }

                return true;
            case "fog":
                Fog[target.Id] = cmd.Int(0);
                return true;
            default:
                return false;
        }
    }
}
