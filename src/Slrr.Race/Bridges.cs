using System.Numerics;
using Slrr.Engine.Commands;
using Slrr.Engine.MapNatives;
using Slrr.Engine.Parts;
using Slrr.Engine.Resources;
using Slrr.Game;
using Slrr.Game.Physics;
using Slrr.Game.Render.Cars;
using Slrr.GameAudio;
using Slrr.World;
using AudioImpact = Slrr.GameAudio.ImpactEvent;
using CmdYpr = Slrr.Engine.Commands.Ypr;

namespace Slrr.Race;

/// <summary>
/// <see cref="IPhysicsWorld"/> decorator that puts the command engine (src/Slrr.Engine.Commands) in front of a physics
/// world: every command the host routes to physics is offered to both (the command engine records car verbs even when the
/// physics adapter consumes them); each frame the inner physics steps first, then the command engine (AI, triggers,
/// traffic, cameras; plus the kinematic stand-in cars when <paramref name="inner"/> provides no vehicle state).
/// Moved here from tools/Slrr.Cli/CmdRace.cs by the race-wave integration.
/// </summary>
public sealed class CommandsPhysicsWorld(IPhysicsWorld? inner = null, Func<ResourceEngine, CommandEngine>? factory = null) : IPhysicsWorld
{
    public CommandEngine? Engine { get; private set; }

    public IPhysicsWorld? Inner => inner;

    public void Attach(IGameWorld world)
    {
        inner?.Attach(world);
        Engine = factory?.Invoke(world.Resources) ?? CommandEngine.ForResources(world.Resources);
    }

    public bool Command(int targetId, string command)
    {
        var mine = Engine?.Execute(targetId, command).Consumed ?? false;
        var other = inner?.Command(targetId, command) ?? false;
        return mine || other;
    }

    public void Step(float dt)
    {
        inner?.Step(dt);
        Engine?.Step(dt);
    }
}

/// <summary>
/// Keeps the physics scene to the cars of the map being driven (integration fix). <see cref="GamePhysicsWorld"/> binds every
/// chassis that receives a car verb and never forgets it, and it has one global <see cref="IMapSource"/>: the used-car
/// dealer's cars (left at the dealer scene's origin, or detached with parent 0 after the purchase) stayed in the scene as
/// held, invisible bodies at (0, y, 0) of the race map, and the player's car hit them at 174 km/h on Test Track (MEASURED:
/// car/car contact at z = -3.5, speed 48.3 -> 23.4 m/s in one frame, with chassis-vs-map collision switched off). Before
/// every step this removes cars whose instance is gone or not below the current map (<see cref="IMapGroundProvider.MapOf"/>);
/// a removed car is bound again by its next car verb.
/// </summary>
public sealed class MapScopedPhysics(GamePhysicsWorld physics, Func<IMapGroundProvider?> maps, MapWorldsSource source) : IPhysicsWorld
{
    private IGameWorld? _world;

    /// <summary>Cars removed so far (id, name, reason) for reports.</summary>
    public List<string> Removed { get; } = [];

    public void Attach(IGameWorld world)
    {
        _world = world;
        physics.Attach(world);
    }

    public bool Command(int targetId, string command) => physics.Command(targetId, command);

    public void Step(float dt)
    {
        Prune();
        physics.Step(dt);
    }

    private void Prune()
    {
        if (_world is null)
        {
            return;
        }

        var current = source.CurrentMap;
        var provider = maps();
        foreach (var car in physics.Scene.AllCars.ToList())
        {
            var node = _world.Resources.Tree.Get(car.Id);
            string? reason = node is null ? "instance gone"
                : node.ParentId == 0 ? "detached (parent 0)"
                : current is not null && provider is not null && !ReferenceEquals(provider.MapOf(car.Id), current) ? $"not below the current map {current.ArchiveName}"
                : null;
            if (reason is not null && physics.Scene.Remove(car.Id))
            {
                Removed.Add($"{car.Id} {car.Name}: {reason}");
            }
        }
    }
}

/// <summary>
/// The command engine's vehicle view (<see cref="IVehicleState"/>) and AI input sink (<see cref="IVehicleDriver"/>) over
/// <see cref="GamePhysicsWorld"/>, so cameras, triggers, AI brains and <c>getSpeedSquare</c> all see the one simulation.
/// Steering sign: Commands' <see cref="DriveInput.Steer"/> is +1 = right, physics' <see cref="CarControls.Steer"/> is
/// +1 = left (both documented by their areas), so it is negated here.
/// </summary>
public sealed class PhysicsVehicleBridge(GamePhysicsWorld physics) : IVehicleState, IVehicleDriver
{
    private readonly Dictionary<int, DriveInputSource> _sources = [];

    public bool TryGetVehicle(int carId, out Pose pose)
    {
        if (physics.Get(carId) is not { } t)
        {
            pose = default;
            return false;
        }

        pose = new Pose(t.Position, new CmdYpr(t.Ypr.Yaw, t.Ypr.Pitch, t.Ypr.Roll), t.Velocity);
        return true;
    }

    public void SetInput(int carId, DriveInput input)
    {
        if (physics.Scene.Car(carId) is not { } car)
        {
            return;
        }

        if (car.Input is VirtualAxisInput)
        {
            return; // the human driver's car keeps the input layer
        }

        if (!_sources.TryGetValue(carId, out var src) || !ReferenceEquals(car.Input, src))
        {
            src = new DriveInputSource();
            _sources[carId] = src;
            car.Input = src;
        }

        src.Input = input;
    }

    private sealed class DriveInputSource : IVehicleInputSource
    {
        public DriveInput Input { get; set; }

        public void Read(double time, CarControls into)
        {
            into.Clear();
            into.Throttle = Math.Clamp(Input.Throttle, 0f, 1f);
            into.Brake = Math.Clamp(Input.Brake, 0f, 1f);
            into.Steer = -Math.Clamp(Input.Steer, -1f, 1f);
        }
    }
}

/// <summary>
/// The map the cars drive on for the physics adapter: the most recently loaded <see cref="MapState"/> whose world exists
/// (Track.enter loads exactly one ground; Track.exit unloads it). Registered as an <see cref="IMapWorldListener"/>.
/// </summary>
public sealed class MapWorldsSource : IMapSource, IMapWorldListener
{
    private readonly List<MapState> _loaded = [];

    public StaticWorld? Current
    {
        get
        {
            for (var i = _loaded.Count - 1; i >= 0; i--)
            {
                if (_loaded[i].World is { } w)
                {
                    return w;
                }
            }

            return null;
        }
    }

    public MapState? CurrentMap => _loaded.LastOrDefault(m => m.World is not null);

    public void MapLoaded(MapState map)
    {
        _loaded.Remove(map);
        _loaded.Add(map);
    }

    public void MapUnloaded(MapState map) => _loaded.Remove(map);

    public void FogChanged(MapState map)
    {
    }
}

/// <summary>Audio telemetry of one physics car (<see cref="IVehicleAudioTelemetry"/> over <see cref="CarTelemetry"/>).</summary>
public sealed class PhysicsAudioTelemetry(GamePhysicsWorld physics, int carId) : IVehicleAudioTelemetry
{
    private readonly List<AudioImpact> _impacts = [];
    private long _lastImpactFrame = -1;

    private CarTelemetry? T => physics.Get(carId);

    /// <summary>Silent unless the car is simulated with its engine on (dealer/showroom cars idle in place and stay quiet: GUESSED).</summary>
    public bool Active => T is { Simulated: true, EngineRunning: true };

    public Vector3 Position => T?.Position ?? default;

    public float EngineAngularSpeed => (T?.Rpm ?? 0f) / 9.549297f;

    public float Throttle => T?.Throttle ?? 0f;

    public bool StarterEngaged => false;

    /// <summary>Mean spin rate of the wheels (rad/s): the driveline output speed as the whine model wants it is GUESSED to be the wheel speed.</summary>
    public float DriveShaftAngularSpeed => T is { Wheels.Count: > 0 } t ? t.Wheels.Average(w => w.SpinRate) : 0f;

    public float DriveShaftTorque => T?.EngineTorque ?? 0f;

    public float Speed => T?.Speed ?? 0f;

    public int WheelCount => T?.Wheels.Count ?? 0;

    public float WheelSlip(int index) => T is { } t && index < t.Wheels.Count ? Math.Clamp(t.Wheels[index].Skid, 0f, 1f) : 0f;

    public Vector3 WheelPosition(int index) => T is { } t && index < t.Wheels.Count ? t.Wheels[index].Center : Position;

    /// <summary>Collects this car's impacts of the last physics frame (called once per host frame by <see cref="RaceAudioSink"/>).</summary>
    public void Collect(long frame)
    {
        if (frame == _lastImpactFrame)
        {
            return;
        }

        _lastImpactFrame = frame;
        foreach (var i in physics.Impacts)
        {
            if (i.CarId == carId)
            {
                _impacts.Add(new AudioImpact(i.DeltaV, i.Point));
            }
        }
    }

    public void DrainImpacts(ICollection<AudioImpact> into)
    {
        foreach (var e in _impacts)
        {
            into.Add(e);
        }

        _impacts.Clear();
    }
}

/// <summary>
/// The host's audio sink in a race: <see cref="GameAudioSink"/> plus the per-frame sync that attaches every car the
/// physics adapter bound (<c>AttachChassis</c> with <see cref="PhysicsAudioTelemetry"/>) and drops cars that left.
/// </summary>
public sealed class RaceAudioSink(GameAudioSink inner, GamePhysicsWorld physics) : Slrr.Game.IAudioSink, IDisposable
{
    private readonly Dictionary<int, (ChassisState Chassis, PhysicsAudioTelemetry Telemetry)> _cars = [];
    private IGameWorld? _world;

    public GameAudioSink Inner => inner;

    public IReadOnlyCollection<int> AttachedCars => _cars.Keys;

    /// <summary>The sound model of a physics car (null until it was attached and updated once).</summary>
    public VehicleAudio? AudioOf(int carId) => _cars.TryGetValue(carId, out var e) ? inner.VehicleOf(e.Chassis) : null;

    public void Attach(IGameWorld world)
    {
        _world = world;
        inner.Attach(world);
    }

    public int Play(int soundId, Vector3? position, float a, float b, float c, int flags) => inner.Play(soundId, position, a, b, c, flags);

    public void Stop(int handle) => inner.Stop(handle);

    public void Update(float dt)
    {
        Sync();
        inner.Update(dt);
    }

    private void Sync()
    {
        if (_world is null)
        {
            return;
        }

        foreach (var car in physics.Scene.AllCars)
        {
            var node = _world.Resources.Tree.Get(car.Id);
            var cs = node is null ? null : _world.Parts.Find(node.Script)?.Chassis;
            if (cs is null)
            {
                continue;
            }

            if (!_cars.TryGetValue(car.Id, out var e) || !ReferenceEquals(e.Chassis, cs))
            {
                if (e.Chassis is not null)
                {
                    inner.RemoveVehicle(e.Chassis);
                }

                e = (cs, new PhysicsAudioTelemetry(physics, car.Id));
                _cars[car.Id] = e;
                inner.AttachChassis(cs, e.Telemetry);
            }

            e.Telemetry.Collect(_world.Frame);
        }

        foreach (var id in _cars.Keys.ToList())
        {
            if (physics.Scene.Car(id) is null)
            {
                inner.RemoveVehicle(_cars[id].Chassis);
                _cars.Remove(id);
            }
        }
    }

    public void Dispose() => inner.Dispose();
}

/// <summary>Render telemetry from the physics adapter: wheel spin/steer/travel and lamp states; host-derived otherwise.</summary>
public sealed class PhysicsRenderTelemetry(GamePhysicsWorld physics) : ICarTelemetry
{
    private readonly HostCarTelemetry _fallback = new();

    public CarState? Get(IGameWorld world, ResourceNode node, PartState chassis, float dt)
    {
        if (physics.Get(node.Id) is not { } t)
        {
            return _fallback.Get(world, node, chassis, dt);
        }

        var s = new CarState
        {
            BrakeLights = t.Brake > 0.1f,
            Reversing = t.Gear < 0,
        };
        for (var i = 0; i < 4 && i < t.Wheels.Count; i++)
        {
            var w = t.Wheels[i];
            s.Wheels[i] = new WheelPose(w.Rotation, w.SteerAngle, w.Travel);
        }

        return s;
    }
}
