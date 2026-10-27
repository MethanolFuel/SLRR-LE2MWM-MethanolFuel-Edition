using System.Globalization;
using Slrr.Engine.Parts;
using Slrr.Engine.Resources;
using Slrr.Physics;
using Slrr.Physics.Dyno;
using Slrr.Vm;
using Slrr.World;

namespace Slrr.Game.Physics;

/// <summary>Where the currently loaded map comes from (cross-area: the map/GroundRef owner implements it).</summary>
public interface IMapSource
{
    /// <summary>The map the cars drive on, or null (garage, menus: cars stay where the scripts put them).</summary>
    StaticWorld? Current { get; }
}

/// <summary>A fixed map (tests, CLI, or a host that loads the map itself).</summary>
public sealed class FixedMapSource(StaticWorld? world) : IMapSource
{
    public StaticWorld? Current { get; set; } = world;
}

public sealed class GamePhysicsOptions
{
    public IMapSource? Maps { get; init; }

    public SpecOptions? Spec { get; init; }

    public SurfaceMapping? Surfaces { get; init; }

    /// <summary>Bind a chassis the first time a car verb targets it (default). Off: only <see cref="GamePhysicsWorld.Bind"/>.</summary>
    public bool BindOnCommand { get; init; } = true;

    /// <summary>Write pose, velocities and the chassis telemetry cells back into the engine objects every frame.</summary>
    public bool WriteBack { get; init; } = true;

    /// <summary>Parallel car stepping (see <see cref="PhysicsScene.Parallel"/>).</summary>
    public bool Parallel { get; init; }
}

/// <summary>
/// The host's <see cref="IPhysicsWorld"/> on Slrr.Physics + Slrr.World. Binds chassis parts (by instance id, the id
/// the scripts' <c>car.id()</c> carries) to simulated cars, executes the car command verbs, steps the
/// <see cref="PhysicsScene"/> at a fixed 120 Hz from the host frame (accumulator), maps the human controller's virtual
/// axes to the player car, and writes pose/velocity/telemetry back so <c>getPos/getOri/getVel</c>,
/// <c>Chassis.getIndexedData</c> and <c>Vehicle.getSpeedSquare</c> (<see cref="PhysicsNatives"/>) see the simulation.
/// Labels in docs/race/game-physics.md.
/// </summary>
public sealed class GamePhysicsWorld : IPhysicsWorld, ICarTelemetrySource
{
    private static readonly char[] Separators = [' ', '\t'];

    /// <summary>Car verbs this adapter consumes (entry-points.md section 5, car group 0x61047c..0x6108fc + plain words; M names).</summary>
    public static readonly IReadOnlySet<string> CarVerbs = new HashSet<string>(StringComparer.Ordinal)
    {
        "start", "stop", "idle", "reset", "brake", "suspend", "wakeup", "setsteer", "transmission", "steerhelp", "asr", "abs",
        "esp", "difflock", "cruise", "sethorn", "setvel", "filter", "damage_multiplier", "corpse", "reload", "pickup", "team",
        "addroll", "setpitch", "ai_cheat",
    };

    private readonly GamePhysicsOptions _options;
    private readonly ExeDynoCalculator _dyno = new();
    private readonly Dictionary<int, (float[] Pos, float[] Ori)> _written = [];
    private readonly HashSet<int> _dirty = [];
    private readonly Dictionary<int, int> _controllerCar = [];
    private IGameWorld? _world;

    public GamePhysicsWorld(GamePhysicsOptions? options = null)
    {
        _options = options ?? new GamePhysicsOptions();
        Scene.Parallel = _options.Parallel;
    }

    public PhysicsScene Scene { get; } = new();

    public IGameWorld? World => _world;

    public IReadOnlyList<CarTelemetry> Cars => Scene.Cars;

    public double Alpha => Scene.Alpha;

    public IReadOnlyList<ImpactEvent> Impacts => Scene.Impacts;

    /// <summary>Commands seen (target id, text, consumed) - transcript for tests/diagnostics.</summary>
    public List<(int Target, string Text, bool Consumed)> CommandLog { get; } = [];

    /// <summary>Messages: bindings, spec notes, rebuilds, failures.</summary>
    public List<string> Log { get; } = [];

    public CarTelemetry? Get(int id) => Scene.Get(id);

    public void Attach(IGameWorld world)
    {
        _world = world;
        world.Parts.ChassisUpdated += (part, _) =>
        {
            var id = ResourceEngine.IdOf(part.Object);
            if (Scene.Car(id) is not null)
            {
                _dirty.Add(id);
            }
        };
    }

    public bool Command(int targetId, string command)
    {
        var tok = command.Split(Separators, StringSplitOptions.RemoveEmptyEntries);
        if (tok.Length == 0)
        {
            return false;
        }

        var verb = tok[0];
        if (verb is "controllable" or "leave")
        {
            NoteController(targetId, verb, tok);
            CommandLog.Add((targetId, command, false));
            return false; // still queued for the controller/brain owner
        }

        if (!CarVerbs.Contains(verb))
        {
            return false;
        }

        var car = Scene.Car(targetId) ?? (_options.BindOnCommand ? Bind(targetId) : null);
        if (car is null)
        {
            CommandLog.Add((targetId, command, false));
            return false;
        }

        Execute(car, verb, tok);
        CommandLog.Add((targetId, command, true));
        return true;
    }

    /// <summary>Executes one car verb. Effects GUESSED from the verbs' use in the scripts (docs/race/game-physics.md).</summary>
    public static void Execute(SimCar car, string verb, string[] tok)
    {
        float F(int i) => tok.Length > i && float.TryParse(tok[i], NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0f;
        switch (verb)
        {
            case "start":
                car.EngineRunning = true;
                car.BrakeOverride = false;
                car.Suspended = false;
                car.State = CarRunState.Driving;
                break;
            case "stop":
                car.EngineRunning = false;
                car.State = CarRunState.Held;
                ZeroMotion(car);
                break;
            case "idle":
                car.EngineRunning = true;
                break;
            case "reset":
                // Keeps the run state (integration fix, GUESSED from the scripts): Track.lockCar ends with "reset; reload"
                // and nothing sends "start" afterwards although the player is meant to drive; scripts hold a car with
                // "stop" (City grids: reset; setMatrix; stop; idle ... start). Reset = upright, at rest, on the ground.
                car.BrakeOverride = false;
                ZeroMotion(car);
                car.Place(car.Origin, Conv.FromYpr(Conv.ToYpr(car.Orientation).Yaw, 0, 0));
                break;
            case "brake":
                car.BrakeOverride = true;
                break;
            case "suspend":
                car.Suspended = true;
                break;
            case "wakeup":
                car.Suspended = false;
                break;
            case "setsteer":
                car.ScriptSteer = Math.Clamp(F(1), -1f, 1f);
                break;
            case "transmission":
                if (car is FullCar fc && int.TryParse(tok.ElementAtOrDefault(1), NumberStyles.Integer, CultureInfo.InvariantCulture, out var mode)
                    && Enum.IsDefined(typeof(TransmissionMode), mode))
                {
                    fc.Drivetrain.Mode = (TransmissionMode)mode;
                }

                break;
            case "steerhelp":
                if (car is FullCar f2)
                {
                    f2.Drivetrain.SteerHelp = Math.Clamp(F(1), 0f, 4f);
                }

                break;
            case "sethorn":
                car.Controls.Horn = F(1) != 0;
                break;
            case "setvel":
                // "%*s %f,%f,%f,%f,%f,%f" (M format): linear then angular velocity (order GUESSED).
                var v = (tok.Length > 1 ? string.Join(",", tok.Skip(1)) : "").Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .Select(s => double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : 0).ToArray();
                if (v.Length >= 3)
                {
                    car.Velocity = new Vec3(v[0], v[1], v[2]);
                }

                if (v.Length >= 6)
                {
                    car.AngularVelocity = new Vec3(v[3], v[4], v[5]);
                }

                break;
        }

        // Every verb's last arguments are kept for inspection (abs/asr/difflock/cruise/filter: assists not simulated).
        car.Settings[verb] = string.Join(' ', tok.Skip(1));
    }

    private static void ZeroMotion(SimCar car)
    {
        car.Velocity = Vec3.Zero;
        car.AngularVelocity = Vec3.Zero;
    }

    private void NoteController(int controllerId, string verb, string[] tok)
    {
        if (_world is null || tok.Length < 2 || !int.TryParse(tok[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var carId))
        {
            return;
        }

        var ctl = _world.Resources.Tree.Get(controllerId)?.Script;
        var human = ctl is not null && _world.Ui.Controllers.ContainsKey(ctl);
        var car = Scene.Car(carId) ?? (verb == "controllable" && _options.BindOnCommand ? Bind(carId) : null);
        if (car is null)
        {
            return;
        }

        if (verb == "controllable")
        {
            _controllerCar[controllerId] = carId;
            if (human)
            {
                car.Input = new VirtualAxisInput(_world.Ui, () => ctl);
                Log.Add($"car {carId}: input from human controller {controllerId}");
            }
            else
            {
                Log.Add($"car {carId}: controlled by {controllerId} (not a human controller: input left to the AI owner)");
            }
        }
        else if (_controllerCar.Remove(controllerId) && car.Input is VirtualAxisInput)
        {
            car.Input = null;
        }
    }

    /// <summary>Binds the chassis instance <paramref name="chassisId"/> to a simulated car (null when it is not a car).</summary>
    public SimCar? Bind(int chassisId)
    {
        if (Scene.Car(chassisId) is { } existing)
        {
            return existing;
        }

        if (_world is null || _world.Resources.Tree.Get(chassisId) is not { } node || _world.Parts.Find(node.Script) is not { Chassis: not null } part)
        {
            return null;
        }

        SimCar car;
        try
        {
            car = Create(chassisId, part);
        }
        catch (Exception ex) when (ex is InvalidOperationException or VmException or ArgumentException)
        {
            Log.Add($"bind {chassisId}: {ex.Message}");
            return null;
        }

        Scene.Add(car);
        var ori = Conv.FromYpr(node.Ori[0], node.Ori[1], node.Ori[2]);
        car.Place(new Vec3(node.Pos[0], node.Pos[1], node.Pos[2]), ori, snap: Scene.Ground.Target is not null);
        Log.Add($"bound car {chassisId} {car.Name} ({car.GetType().Name}, {car.Mass:F0} kg)");
        return car;
    }

    /// <summary>Attaches a control source (an AI brain, a replay) to a bound car.</summary>
    public bool SetInput(int carId, IVehicleInputSource? input)
    {
        if (Scene.Car(carId) is not { } car)
        {
            return false;
        }

        car.Input = input;
        return true;
    }

    private SimCar Create(int id, PartState chassis)
    {
        var spec = SpecBuilder.Build(chassis, _dyno, _options.Spec);
        var name = chassis.Object.Class.Name;
        if (spec.Wheels.Count < 3)
        {
            // No wheel lines (traffic/dummy shells): the simple body (GUESSED fallback).
            return new SimpleCar(id, name, SimpleCarSpec.From(spec), Scene.Ground);
        }

        foreach (var n in spec.Notes)
        {
            Log.Add($"car {id}: {n}");
        }

        return new FullCar(id, name, spec, Scene.Ground);
    }

    public void Step(float dt)
    {
        var map = _options.Maps?.Current;
        if (!ReferenceEquals(map, Scene.World) && (map is not null || Scene.World is not null))
        {
            Scene.SetWorld(map, _options.Surfaces);
            Log.Add($"map {(map?.Name ?? "none")}");
        }

        Rebuild();
        PullScriptPoses();
        Scene.Advance(dt);
        if (_options.WriteBack)
        {
            WriteBack(dt);
        }
    }

    /// <summary>Re-creates cars whose parts changed (<c>forceUpdate</c>), keeping pose and run state.</summary>
    private void Rebuild()
    {
        if (_world is null || _dirty.Count == 0)
        {
            return;
        }

        foreach (var id in _dirty.Order().ToList())
        {
            if (Scene.Car(id) is not { } old || _world.Resources.Tree.Get(id) is not { } node || _world.Parts.Find(node.Script) is not { Chassis: not null } part)
            {
                continue;
            }

            SimCar fresh;
            try
            {
                fresh = Create(id, part);
            }
            catch (Exception ex) when (ex is InvalidOperationException or VmException or ArgumentException)
            {
                Log.Add($"rebuild {id}: {ex.Message}");
                continue;
            }

            Scene.Remove(id);
            Scene.Add(fresh);
            fresh.Place(old.Origin, old.Orientation, snap: false);
            fresh.State = old.State;
            fresh.Suspended = old.Suspended;
            fresh.EngineRunning = old.EngineRunning;
            fresh.Input = old.Input;
            fresh.ScriptSteer = old.ScriptSteer;
            Log.Add($"rebuilt car {id} after a parts update");
        }

        _dirty.Clear();
    }

    /// <summary>A script <c>setPos/setMatrix/setState</c> since the last write-back teleports the car (snapped to the ground).</summary>
    private void PullScriptPoses()
    {
        if (_world is null)
        {
            return;
        }

        foreach (var car in Scene.AllCars)
        {
            if (_world.Resources.Tree.Get(car.Id) is not { } node || !_written.TryGetValue(car.Id, out var w))
            {
                continue;
            }

            if (!node.Pos.AsSpan().SequenceEqual(w.Pos) || !node.Ori.AsSpan().SequenceEqual(w.Ori))
            {
                car.Place(new Vec3(node.Pos[0], node.Pos[1], node.Pos[2]), Conv.FromYpr(node.Ori[0], node.Ori[1], node.Ori[2]),
                    snap: Scene.Ground.Target is not null);
                Log.Add(FormattableString.Invariant($"car {car.Id} placed by script at ({node.Pos[0]:F2}, {node.Pos[1]:F2}, {node.Pos[2]:F2})"));
            }
        }
    }

    private void WriteBack(float dt)
    {
        if (_world is null)
        {
            return;
        }

        foreach (var car in Scene.AllCars)
        {
            if (car.Telemetry is not { } t || _world.Resources.Tree.Get(car.Id) is not { } node)
            {
                continue;
            }

            node.Pos[0] = t.Position.X;
            node.Pos[1] = t.Position.Y;
            node.Pos[2] = t.Position.Z;
            node.Ori[0] = t.Ypr.Yaw;
            node.Ori[1] = t.Ypr.Pitch;
            node.Ori[2] = t.Ypr.Roll;
            node.Vel[0] = t.Velocity.X;
            node.Vel[1] = t.Velocity.Y;
            node.Vel[2] = t.Velocity.Z;
            node.AngVel[0] = t.AngularVelocity.X;
            node.AngVel[1] = t.AngularVelocity.Y;
            node.AngVel[2] = t.AngularVelocity.Z;
            _written[car.Id] = ((float[])node.Pos.Clone(), (float[])node.Ori.Clone());

            if (_world.Parts.Find(node.Script)?.Chassis is { } cs)
            {
                WriteTelemetry(cs.Telemetry, t, car.Simulated ? dt : 0f);
                cs.Mileage = (float)(cs.Mileage + (t.Odometer - LastOdometer(car.Id, t.Odometer)));
            }
        }
    }

    private readonly Dictionary<int, double> _odometer = [];

    private double LastOdometer(int id, double now)
    {
        var last = _odometer.GetValueOrDefault(id, now);
        _odometer[id] = now;
        return last;
    }

    /// <summary>
    /// The chassis telemetry cells <c>Chassis.getIndexedData</c> reads (offsets/names MEASURED from Chassis.java via
    /// docs/engine/parts.md; the values' units and encodings GUESSED: RPM in rpm, GearNumber = our gear number with -1
    /// reverse, strengths 0..1, CurrentSteering = steer -1..1, CarRuntime seconds, Mileage metres).
    /// </summary>
    public static void WriteTelemetry(ChassisTelemetry cells, CarTelemetry t, float dt)
    {
        cells.SetFloat(ChassisTelemetry.Rpm, t.Rpm);
        cells.SetInt(ChassisTelemetry.GearNumber, t.Gear);
        cells.SetFloat(ChassisTelemetry.ThrottleStrength, t.Throttle);
        cells.SetFloat(ChassisTelemetry.BrakeStrength, t.Brake);
        cells.SetFloat(ChassisTelemetry.ClutchStrength, t.Clutch);
        cells.SetFloat(ChassisTelemetry.Handbrake, t.Handbrake);
        cells.SetFloat(ChassisTelemetry.NitroStrength, t.Nitro ? 1f : 0f);
        cells.SetFloat(ChassisTelemetry.CurrentSteering1, t.Steer);
        cells.SetFloat(ChassisTelemetry.CurrentSteering2, t.Steer);
        cells.SetFloat(ChassisTelemetry.CurrentSteering3, t.Steer);
        cells.SetFloat(ChassisTelemetry.ExhaustActivity, t.EngineRunning ? Math.Clamp(t.Throttle, 0.05f, 1f) : 0f);
        cells.SetFloat(ChassisTelemetry.GearStrength, t.Clutch);
        cells.SetFloat(ChassisTelemetry.CarRuntime, cells.GetFloat(ChassisTelemetry.CarRuntime) + dt);
    }
}
