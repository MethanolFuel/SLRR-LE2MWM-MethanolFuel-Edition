using System.Globalization;
using Slrr.Engine.Resources;

namespace Slrr.Engine.Commands;

/// <summary>Result of offering one command string to the engine.</summary>
/// <param name="Consumed">True when a handler took it (the host then does not queue it as a script event).</param>
public sealed record CommandOutcome(bool Consumed, string Verb, VerbGroup? Group, string? Kind, string Text);

/// <summary>One line of the command transcript.</summary>
public sealed record CommandRecord(float Time, int TargetId, string? Kind, string Text, bool Consumed);

/// <summary>
/// The command state machine: routes every <c>EVENT_COMMAND</c> text sent to a native object to the subsystem of the
/// target's kind (MEASURED kinds and formats; see <see cref="VerbCatalog"/>) and steps the subsystems once per frame.
/// <list type="bullet">
/// <item><c>camera</c> → <see cref="CameraRig"/>; <c>car</c> → <see cref="CarControl"/> (+ <c>render</c>/<c>hide</c> interior views
/// → <see cref="CameraRig"/>); <c>player</c> and <c>bot</c> → <see cref="RaceDirector"/>; <c>cursor</c>/map → <see cref="SceneCommands"/>.</item>
/// <item>Part structure verbs (<c>install</c>, <c>remove</c>, <c>add_*</c>, ...) and objects of other kinds are never consumed.</item>
/// </list>
/// Without a physics adapter the engine owns a <see cref="KinematicVehicles"/> stand-in. Deterministic: the only
/// inputs are the commands, <see cref="Step"/>'s dt and the object poses.
/// </summary>
public sealed class CommandEngine
{
    private static readonly HashSet<string> GroundVerbs = ["obs_add", "obs_rem", "fog"];
    private readonly List<CommandRecord> _transcript = [];
    private readonly Dictionary<string, (int Consumed, int Rejected)> _stats = new(StringComparer.Ordinal);

    public CommandEngine(IObjectDirectory world, IScriptEventSink events, IVehicleState? vehicles = null, IVehicleDriver? driver = null)
    {
        World = world;
        Events = events;
        Cars = new CarControl();
        if (vehicles is null || driver is null)
        {
            Kinematic = new KinematicVehicles(world, Cars);
        }

        Vehicles = vehicles ?? Kinematic!;
        Driver = driver ?? Kinematic!;
        Cameras = new CameraRig(world, Vehicles);
        Race = new RaceDirector(world, Vehicles, Driver, events);
        Traffic = new TrafficDirector(Vehicles, Driver, events);
        Race.Traffic = Traffic;
        Cameras.FollowTargetOf = Race.FollowTargetOfCamera;
        Race.Participates = IsActiveCar;
        Scene = new SceneCommands();
    }

    /// <summary>Engine over Slrr.Engine.Resources (instance tree + event queue).</summary>
    public static CommandEngine ForResources(ResourceEngine resources, IVehicleState? vehicles = null, IVehicleDriver? driver = null)
    {
        var dir = new ResourceObjectDirectory(resources);
        return new CommandEngine(dir, dir, vehicles, driver);
    }

    public IObjectDirectory World { get; }

    public IScriptEventSink Events { get; }

    public IVehicleState Vehicles { get; }

    public IVehicleDriver Driver { get; }

    /// <summary>The stand-in vehicles when no physics adapter was passed (null otherwise).</summary>
    public KinematicVehicles? Kinematic { get; }

    public CarControl Cars { get; }

    public CameraRig Cameras { get; }

    public RaceDirector Race { get; }

    public TrafficDirector Traffic { get; }

    public SceneCommands Scene { get; }

    /// <summary>When true, car verbs are recorded but reported as not consumed, so a physics adapter offered the same
    /// command after this engine still sees them (the host decorator offers every command to both anyway).</summary>
    public bool PassCarVerbsThrough { get; set; }

    /// <summary>Sim time (sum of <see cref="Step"/> dt).</summary>
    public float Time { get; private set; }

    public IReadOnlyList<CommandRecord> Transcript => _transcript;

    /// <summary>Per verb: how many commands were consumed / offered but not consumed.</summary>
    public IReadOnlyDictionary<string, (int Consumed, int Rejected)> Stats => _stats;

    /// <summary>Maximum transcript length kept (oldest lines are not dropped; recording stops).</summary>
    public int TranscriptLimit { get; set; } = 20_000;

    public CommandOutcome Execute(int targetId, string text)
    {
        var verb = ParsedCommand.VerbOf(text);
        var target = World.Get(targetId);
        var kind = target?.Kind;
        var group = target is null ? null : GroupOf(target, verb);
        var consumed = false;
        if (target is not null && group is { } g)
        {
            var cmd = ParsedCommand.Parse(text, g);
            consumed = g switch
            {
                VerbGroup.Camera => Cameras.Execute(target, cmd),
                VerbGroup.Car => Cameras.Execute(target, cmd) | (Cars.Execute(target, cmd) && !PassCarVerbsThrough),
                VerbGroup.Controller or VerbGroup.Brain => Race.Execute(target, cmd),
                VerbGroup.Cursor => Scene.ExecuteCursor(target, cmd),
                VerbGroup.Ground => Scene.ExecuteGround(target, cmd),
                _ => false,
            };
        }

        var s = _stats.GetValueOrDefault(verb);
        _stats[verb] = consumed ? (s.Consumed + 1, s.Rejected) : (s.Consumed, s.Rejected + 1);
        if (_transcript.Count < TranscriptLimit)
        {
            _transcript.Add(new CommandRecord(Time, targetId, kind, text, consumed));
        }

        return new CommandOutcome(consumed, verb, group, kind, text);
    }

    /// <summary>Which parser of the exe a verb sent to <paramref name="target"/> reaches (null = none of this project's).</summary>
    public static VerbGroup? GroupOf(ObjectInfo target, string verb)
    {
        var spec = VerbCatalog.Find(verb);
        if (spec?.Group == VerbGroup.Part)
        {
            return null;
        }

        return target.Kind switch
        {
            "camera" => VerbGroup.Camera,
            "car" => VerbGroup.Car,
            "player" => VerbGroup.Controller,
            "bot" => verb.StartsWith("AI_", StringComparison.Ordinal) ? VerbGroup.Brain : VerbGroup.Controller,
            "cursor" => VerbGroup.Cursor,
            "ground" => VerbGroup.Ground,
            null or "" when GroundVerbs.Contains(verb) => VerbGroup.Ground,
            _ => null,
        };
    }

    /// <summary>
    /// One frame: AI brains produce inputs and triggers fire (on the poses of the previous frame), traffic cruises, the
    /// stand-in vehicles move (when owned), cameras follow.
    /// </summary>
    public void Step(float dt)
    {
        Time += dt;
        Race.Step(dt);
        Traffic.Step(dt);
        Kinematic?.Step(dt);
        Cameras.Step(dt);
    }

    /// <summary>
    /// <c>GameRef.getInfo</c> keys this area can answer for cars when the generic path returns 0 (GUESSED values;
    /// key numbers MEASURED from GameType): GII_OWNER (24) = controller/brain that took the car with <c>controllable</c>,
    /// GII_BONE (1) = the car itself (its render root), GII_RENDER (48) = number of interior cameras (4),
    /// GII_CATEGORY (7) = GIR_CAT_VEHICLE (5).
    /// </summary>
    public int? AnswerInfo(int objectId, int key)
    {
        if (World.Get(objectId) is not { Kind: "car" })
        {
            return null;
        }

        return key switch
        {
            24 => Race.OwnerOf(objectId) is var o and not 0 ? o : null,
            1 => objectId,
            48 => InteriorCameras,
            7 => 5,
            _ => null,
        };
    }

    /// <summary>A car-kind instance that behaves as a car: received a car verb, is controlled, or is in traffic (GUESSED).</summary>
    public bool IsActiveCar(int carId) => Cars.Get(carId) is not null || Race.OwnerOf(carId) != 0 || Traffic.ByCar(carId) is not null;

    /// <summary>Interior cameras per car reported for GII_RENDER (GUESSED; Track.changeCamInternal cycles below it).</summary>
    public int InteriorCameras { get; set; } = 4;

    /// <summary><c>Vehicle.getSpeedSquare()</c> (m²/s²): from <see cref="Vehicles"/>.</summary>
    public float SpeedSquare(int carId) => Vehicles.TryGetVehicle(carId, out var p) ? p.Velocity.LengthSquared() : 0f;

    /// <summary><c>Vehicle.getHorn()</c>: the last <c>sethorn</c> value.</summary>
    public int Horn(int carId) => Cars.Get(carId)?.Horn ?? 0;

    /// <summary>Lines describing the state (for the CLI and test output).</summary>
    public IEnumerable<string> Describe()
    {
        var ci = CultureInfo.InvariantCulture;
        foreach (var c in Race.Controllers.OrderBy(c => c.Id))
        {
            yield return string.Create(ci, $"controller {c.Id} [{c.Kind}] controls {c.Controlled} camera {c.Camera} viewport {c.Viewport} osd {c.Osd} mode {c.Mode} level {c.Level}");
        }

        foreach (var c in Cameras.Cameras.OrderBy(c => c.Id))
        {
            yield return string.Create(ci, $"camera {c.Id} pos ({c.Position.X:F2},{c.Position.Y:F2},{c.Position.Z:F2}) yaw {c.Orientation.Yaw:F3} fov {c.Fov:F1} look {c.Look?.ObjectId} move {c.Move?.ObjectId} verbs {c.Verbs.Count}");
        }

        foreach (var v in Cameras.Views.Values.OrderBy(v => v.ViewportId))
        {
            yield return string.Create(ci, $"viewport {v.ViewportId} <- {(v.Interior ? "car interior" : "camera")} {v.SourceId} cam {v.CameraIndex}");
        }

        foreach (var info in World.OfKind("car"))
        {
            var car = Cars.Get(info.Id);
            var pose = Vehicles.TryGetVehicle(info.Id, out var p) ? p : default;
            yield return string.Create(ci,
                $"car {info.Id} '{info.Name}' [{info.ScriptClass}] parent {info.ParentId} hold {car?.Hold} suspended {car?.Suspended} horn {car?.Horn} pos ({pose.Position.X:F1},{pose.Position.Y:F1},{pose.Position.Z:F1}) speed {pose.Speed:F2}");
        }

        foreach (var t in Race.Triggers.OrderBy(t => t.Id))
        {
            yield return string.Create(ci, $"trigger {t.Id} '{t.Name}' at ({t.Center.X:F1},{t.Center.Y:F1},{t.Center.Z:F1}) {(t.Sphere ? $"r {t.Radius}" : $"box {t.Size}")}");
        }

        foreach (var e in Race.TriggerLog)
        {
            yield return string.Create(ci, $"t={e.Time:F2} trigger {(e.On ? "ON " : "OFF")} '{e.TriggerName}' car {e.CarId}");
        }

        foreach (var t in Traffic.Cars)
        {
            yield return string.Create(ci, $"traffic {t.TrafficId} car {t.CarId} map {t.MapId}");
        }
    }
}
