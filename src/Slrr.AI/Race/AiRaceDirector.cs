using System.Numerics;
using Slrr.AI.Driving;
using Slrr.AI.Roads;
using Slrr.Engine.Commands;
using Slrr.Game.Physics;

namespace Slrr.AI.Race;

/// <summary>What the director does with one bot brain's car.</summary>
public sealed class BrainBinding
{
    internal BrainBinding(int brainId, int carId, PathFollowingDriver driver)
    {
        BrainId = brainId;
        CarId = carId;
        Driver = driver;
    }

    public int BrainId { get; }

    public int CarId { get; }

    public PathFollowingDriver Driver { get; }

    /// <summary>Verb that configured the driver (AI_race, AI_spline, AI_BeginRace, AI_stop).</summary>
    public string Verb { get; internal set; } = "";

    /// <summary>True when the route came from the road source; false = straight line fallback / spline file.</summary>
    public bool Routed { get; internal set; }

    public string? Note { get; internal set; }

    internal int VerbCount;
    internal Vector3 Target;
    internal string? Spline;
}

/// <summary>
/// The race director behind the commands layer's <see cref="IRaceDirector"/>: it keeps Slrr.Engine.Commands'
/// <see cref="RaceDirector"/> for controllers, triggers and verb bookkeeping (<see cref="ControllerState"/>) and replaces
/// its straight-line AI for the brain modes the race scripts use by a <see cref="PathFollowingDriver"/> on the brain's
/// physics car. Two ways to plug it in (docs/gaps/ai.md): as the engine's director (calls <see cref="Execute"/> and
/// <see cref="Step"/>), or as an observer of an existing engine (<see cref="Sync"/> after <c>CommandEngine.Step</c>) with
/// the engine's driver wrapped by <see cref="Gate"/> so the old straight-line inputs do not reach the cars it drives.
/// <para>Verb semantics (MEASURED = exe handler 0x42cec0.. / scripts, GUESSED = this project):</para>
/// <list type="bullet">
/// <item><c>AI_race x,y,z id</c> (MEASURED: format <c>%*s %f,%f,%f %d</c>, mode 2, appends the point to a target list,
/// queries the road object at 0x798a54 for car and target, sends the car <c>transmission 0</c>, keeps the opponent id; one of 8
/// race slots at 0x60d6e4 is taken): drive the road source's route from the car to the point, through it (GUESSED; the
/// scripts brake the bot after their finish trigger). No route → straight line (as before).</item>
/// <item><c>AI_spline f name d</c> (MEASURED: <c>%*s %f %s %d</c>, loads the file, mode 3, <c>transmission 0</c>; ROCTrack sends
/// <c>0.5 cars/racers/ROC_cars/roc_XX.spl playerCarId</c>): follow the spline file (<see cref="SplFile"/>) from its start.</item>
/// <item><c>AI_BeginRace f</c> (MEASURED: f default 1.0, clamped to [0, 1], mode 8): hold the brakes and rev at f (GUESSED role).</item>
/// <item><c>AI_stop</c> (MEASURED: mode 0, brake input 127): full brake.</item>
/// <item><c>AI_level v</c> (MEASURED transform, <see cref="AiLevel.Stored"/>) and <c>AI_params a b</c> (stored, GUESSED role,
/// <see cref="AiLevel.WithParams"/>) shape the speed targets; <c>AI_NightRace</c> resets the level to 0.5 (MEASURED).</item>
/// <item>Other modes (<c>AI_follow</c>, <c>AI_GoToTarget</c>, <c>AI_suspend</c>, traffic) stay with the commands layer's director.</item>
/// </list>
/// Our automatic box keeps shifting for the AI: the exe's <c>transmission 0</c> (manual, the brain shifts) is noted, not copied.
/// </summary>
public sealed class AiRaceDirector : IRaceDirector
{
    private readonly RaceDirector _inner;
    private readonly PhysicsScene _scene;
    private readonly Dictionary<int, BrainBinding> _bindings = [];

    public AiRaceDirector(RaceDirector inner, PhysicsScene scene, IRoadSource? roads = null, Func<string, string?>? resolveFile = null)
    {
        _inner = inner;
        _scene = scene;
        Roads = roads;
        ResolveFile = resolveFile ?? (p => File.Exists(p) ? p : null);
    }

    public RaceDirector Inner => _inner;

    public IRoadSource? Roads { get; set; }

    /// <summary>Maps a script file name (<c>AI_spline</c>) to a readable path (see <see cref="GameFiles.Resolver"/>).</summary>
    public Func<string, string?> ResolveFile { get; set; }

    /// <summary>Run Slrr.Physics' skidpad/braking scenarios once per car spec for the grip numbers (else 0.8 g, GUESSED).</summary>
    public bool MeasureCaps { get; set; }

    public DriverSettings Settings { get; set; } = new();

    /// <summary>Hermite tangent scale for <c>AI_spline</c> files: 0.33 was FITTED on ROC_DS.spl against maps/ROC_track (fewest
    /// walls crossed, see <see cref="MapRace"/>); the exe's value is not known.</summary>
    public float SplineTangentScale { get; set; } = 0.33f;

    public IReadOnlyDictionary<int, BrainBinding> Bindings => _bindings;

    public IReadOnlyList<TriggerEvent> TriggerLog => _inner.TriggerLog;

    public ControllerState? Controller(int id) => _inner.Controller(id);

    public int OwnerOf(int objectId) => _inner.OwnerOf(objectId);

    /// <summary>True when this director drives <paramref name="carId"/>.</summary>
    public bool Owns(int carId) => _bindings.Values.Any(b => b.CarId == carId);

    /// <summary>Wraps the engine's driver: inputs for cars this director drives are dropped.</summary>
    public IVehicleDriver Gate(IVehicleDriver driver) => new GateDriver(() => this, driver);

    /// <summary>A gate for an engine that has to exist before the director (the director is built over the engine's
    /// <see cref="CommandEngine.Race"/>): drops inputs for cars <paramref name="director"/>() owns once it is set.</summary>
    public static IVehicleDriver Gate(IVehicleDriver driver, Func<AiRaceDirector?> director) => new GateDriver(director, driver);

    public bool Execute(ObjectInfo target, ParsedCommand command)
    {
        var r = _inner.Execute(target, command);
        Sync();
        return r;
    }

    public void Step(float dt)
    {
        _inner.Step(dt);
        Sync();
    }

    /// <summary>Brings the drivers in line with the brains' state (call once per frame after the command engine stepped).</summary>
    public void Sync()
    {
        foreach (var c in _inner.Controllers.Where(c => c.IsBrain).OrderBy(c => c.Id).ToList())
        {
            SyncBrain(c);
        }

        foreach (var id in _bindings.Keys.Where(id => _inner.Controller(id) is null).ToList())
        {
            Release(id);
        }
    }

    private void SyncBrain(ControllerState c)
    {
        var car = c.Controlled == 0 ? null : _scene.Car(c.Controlled);
        _bindings.TryGetValue(c.Id, out var b);
        if (car is null)
        {
            Release(c.Id);
            return;
        }

        if (b is not null && b.CarId == car.Id && b.VerbCount == c.Verbs.Count)
        {
            return;
        }

        var modeVerb = c.Verbs.LastOrDefault(v => v.StartsWith("AI_", StringComparison.Ordinal) && !IsParamVerb(v)) ?? "";
        var verb = ParsedCommand.VerbOf(modeVerb);
        var own = c.Mode switch
        {
            AiMode.Race or AiMode.Spline => true,
            AiMode.Stopped => verb is "AI_BeginRace" or "AI_stop",
            _ => false,
        };
        if (!own)
        {
            Release(c.Id);
            return;
        }

        if (b is null || b.CarId != car.Id)
        {
            Release(c.Id);
            b = new BrainBinding(c.Id, car.Id, new PathFollowingDriver(car, CapsFor(car), Settings) { Others = _scene.AllCars });
            _bindings[c.Id] = b;
        }

        b.VerbCount = c.Verbs.Count;
        var d = b.Driver;
        d.Caps = Params(c, CapsFor(car));
        d.SetLevel(LevelOf(c));
        car.Input = d;
        b.Verb = verb;
        switch (c.Mode)
        {
            case AiMode.Stopped when verb == "AI_BeginRace":
                d.Mode = DriverMode.Launch;
                d.LaunchThrottle = Math.Clamp(c.BeginRaceValue, 0f, 1f);
                break;
            case AiMode.Stopped:
                d.Mode = DriverMode.Hold;
                break;
            case AiMode.Race:
                if (d.Path is null || b.Target != c.Target || b.Spline is not null)
                {
                    var from = car.Position.ToVector3();
                    var route = Roads?.Route(from, c.Target);
                    b.Routed = route is not null;
                    b.Note = route is null ? "no road route: straight line (GUESSED fallback)" : $"route {route.Name} {route.Length:F0} m";
                    route ??= SampledPath.Through([from, c.Target], closed: false, halfWidth: 6f, name: "straight");
                    d.SetPath(route, PathEnd.Continue);
                    b.Target = c.Target;
                    b.Spline = null;
                }

                d.Mode = DriverMode.Drive;
                break;
            case AiMode.Spline:
                var name = c.Spline.Name ?? "";
                if (d.Path is null || b.Spline != name)
                {
                    var file = ResolveFile(name);
                    if (file is null)
                    {
                        b.Note = $"spline '{name}' not found: brake";
                        d.SetPath(null);
                    }
                    else
                    {
                        var path = SplFile.ToPath(SplFile.Load(file), name, tangentScale: SplineTangentScale);
                        d.SetPath(path, PathEnd.Continue);
                        b.Note = $"spline {name}: {path.Length:F0} m";
                        b.Routed = false;
                    }

                    b.Spline = name;
                }

                d.Mode = DriverMode.Drive;
                break;
        }
    }

    private static bool IsParamVerb(string v) =>
        ParsedCommand.VerbOf(v) is "AI_level" or "AI_params" or "AI_smooth" or "AI_horn" or "AI_NightRace";

    /// <summary>The stored level: last <c>AI_level</c> transformed (MEASURED), reset to 0.5 by a later <c>AI_NightRace</c>.</summary>
    private static double LevelOf(ControllerState c)
    {
        var lvl = c.Verbs.FindLastIndex(v => ParsedCommand.VerbOf(v) == "AI_level");
        var night = c.Verbs.FindLastIndex(v => ParsedCommand.VerbOf(v) == "AI_NightRace");
        return lvl < 0 || night > lvl ? AiLevel.Default : AiLevel.Stored(c.Level);
    }

    private static VehicleCaps Params(ControllerState c, VehicleCaps caps) =>
        c.Params == default ? caps : AiLevel.WithParams(caps, c.Params.X, c.Params.Y);

    private VehicleCaps CapsFor(SimCar car) => MeasureCaps && car is FullCar f ? VehicleCaps.Measure(f.Spec) : VehicleCaps.For(car);

    private void Release(int brainId)
    {
        if (_bindings.Remove(brainId, out var b) && _scene.Car(b.CarId) is { } car && ReferenceEquals(car.Input, b.Driver))
        {
            car.Input = null;
        }
    }

    private sealed class GateDriver(Func<AiRaceDirector?> owner, IVehicleDriver inner) : IVehicleDriver
    {
        public void SetInput(int carId, DriveInput input)
        {
            if (owner() is not { } o || !o.Owns(carId))
            {
                inner.SetInput(carId, input);
            }
        }
    }
}

/// <summary>Game file lookup for script-supplied names (the shipped scripts mix case: <c>roc_SD.spl</c> vs the file
/// <c>ROC_SD.spl</c>, MEASURED; Windows resolves it, Linux needs a case-insensitive walk).</summary>
public static class GameFiles
{
    public static Func<string, string?> Resolver(string root) => name => Resolve(root, name);

    public static string? Resolve(string root, string name)
    {
        var parts = name.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        var dir = root;
        for (var i = 0; i < parts.Length; i++)
        {
            var last = i == parts.Length - 1;
            var exact = Path.Combine(dir, parts[i]);
            if (last ? File.Exists(exact) : Directory.Exists(exact))
            {
                dir = exact;
                continue;
            }

            if (!Directory.Exists(dir))
            {
                return null;
            }

            var match = (last ? Directory.EnumerateFiles(dir) : Directory.EnumerateDirectories(dir))
                .Where(p => string.Equals(Path.GetFileName(p), parts[i], StringComparison.OrdinalIgnoreCase))
                .OrderBy(p => p, StringComparer.Ordinal)
                .FirstOrDefault();
            if (match is null)
            {
                return null;
            }

            dir = match;
        }

        return File.Exists(dir) ? dir : null;
    }
}
