using System.Diagnostics;
using Slrr.Physics;
using Slrr.World;
using PhysGround = Slrr.Physics.IGroundQuery;

namespace Slrr.Game.Physics;

/// <summary>
/// The headless simulation core, independent of the script host: cars (full <see cref="Vehicle"/>s and
/// <see cref="SimpleCar"/>s), the map ground and collision geometry, a fixed 120 Hz step driven by an accumulator, chassis
/// box collisions and per-step telemetry. Deterministic: cars step in insertion order (or in parallel, which gives the
/// same result because cars only interact in the serial collision pass), plain double arithmetic.
/// The 120 Hz rate is this project's choice (Slrr.Physics' step); the exe's physics rate was not measured.
/// </summary>
public sealed class PhysicsScene : ICarTelemetrySource
{
    public const double StepRate = Vehicle.StepRate;
    public const double FixedStep = 1.0 / StepRate;

    private readonly List<SimCar> _cars = [];
    private readonly Dictionary<int, SimCar> _byId = [];
    private readonly List<ImpactEvent> _impacts = [];
    private readonly List<ImpactEvent> _frameImpacts = [];
    private CarTelemetry[] _telemetry = [];
    private double _accumulator;

    public SwitchableGround Ground { get; } = new();

    public IStaticCollision? Collision { get; private set; }

    public StaticWorld? World { get; private set; }

    public CollisionSolver Solver { get; } = new();

    /// <summary>At most this many fixed steps per <see cref="Advance"/> (a slow frame drops the rest: GUESSED 0.1 s).</summary>
    public int MaxStepsPerFrame { get; set; } = 12;

    /// <summary>Step cars on worker threads (same results; useful from ~8 full cars).</summary>
    public bool Parallel { get; set; }

    /// <summary>Cars whose centre of mass drops this far below the map's lowest point are held (fell through the world).</summary>
    public double FallMargin { get; set; } = 50;

    public long Steps { get; private set; }

    public double Time => Steps * FixedStep;

    /// <summary>Simulation time dropped because a frame needed more than <see cref="MaxStepsPerFrame"/> steps.</summary>
    public double DroppedTime { get; private set; }

    /// <summary>Wall time spent in <see cref="StepOnce"/> [ms] (all steps).</summary>
    public double StepWallMs { get; private set; }

    public IReadOnlyList<SimCar> AllCars => _cars;

    public IReadOnlyList<CarTelemetry> Cars => _telemetry;

    public double Alpha => _accumulator / FixedStep;

    public IReadOnlyList<ImpactEvent> Impacts => _frameImpacts;

    /// <summary>Every impact since the scene was created (tests, damage bookkeeping).</summary>
    public IReadOnlyList<ImpactEvent> AllImpacts => _impacts;

    public event Action<ImpactEvent>? Impact;

    /// <summary>Raised after every fixed step (after collisions and telemetry).</summary>
    public event Action<PhysicsScene>? Stepped;

    public CarTelemetry? Get(int id) => _byId.TryGetValue(id, out var c) ? c.Telemetry : null;

    public SimCar? Car(int id) => _byId.GetValueOrDefault(id);

    /// <summary>Uses a loaded map: ground for the suspension and its collision world for the chassis boxes.</summary>
    public void SetWorld(StaticWorld? world, SurfaceMapping? surfaces = null)
    {
        World = world;
        if (world is null)
        {
            SetGround(null, null);
            return;
        }

        SetGround(new WorldGround(world.Ground, surfaces), new CollisionWorldShapes(world.Collision));
    }

    /// <summary>Any ground/collision pair (tests: <see cref="FlatGround"/> and a hand-built <see cref="CollisionWorld"/>).</summary>
    public void SetGround(PhysGround? ground, IStaticCollision? collision)
    {
        Ground.Target = ground;
        Collision = collision;
    }

    public FullCar AddCar(int id, string name, VehicleSpec spec, int substeps = 8)
    {
        var car = new FullCar(id, name, spec, Ground, substeps);
        Add(car);
        return car;
    }

    public SimpleCar AddSimpleCar(int id, string name, SimpleCarSpec spec)
    {
        var car = new SimpleCar(id, name, spec, Ground);
        Add(car);
        return car;
    }

    public void Add(SimCar car)
    {
        if (!_byId.TryAdd(car.Id, car))
        {
            throw new InvalidOperationException($"car id {car.Id} already in the scene");
        }

        _cars.Add(car);
    }

    public bool Remove(int id)
    {
        if (!_byId.Remove(id, out var car))
        {
            return false;
        }

        _cars.Remove(car);
        _telemetry = _telemetry.Where(t => t.Id != id).ToArray();
        return true;
    }

    /// <summary>Adds <paramref name="dt"/> seconds to the accumulator and runs the due fixed steps. Returns the steps run.</summary>
    public int Advance(double dt)
    {
        _frameImpacts.Clear();
        if (!(dt > 0))
        {
            return 0;
        }

        _accumulator += dt;
        var n = 0;
        while (_accumulator >= FixedStep - 1e-12)
        {
            if (n >= MaxStepsPerFrame)
            {
                DroppedTime += _accumulator;
                _accumulator = 0;
                break;
            }

            _accumulator -= FixedStep;
            StepOnce();
            n++;
        }

        if (_accumulator < 0)
        {
            _accumulator = 0;
        }

        return n;
    }

    /// <summary>One fixed 1/120 s step of every car, then collisions, then telemetry.</summary>
    public void StepOnce()
    {
        var sw = Stopwatch.StartNew();
        var h = FixedStep;
        var hasGround = Ground.Target is not null;
        foreach (var car in _cars)
        {
            car.ReadControls();
        }

        if (Parallel && _cars.Count > 1)
        {
            System.Threading.Tasks.Parallel.For(0, _cars.Count, i => StepCar(_cars[i], h, hasGround));
        }
        else
        {
            foreach (var car in _cars)
            {
                StepCar(car, h, hasGround);
            }
        }

        Steps++;
        var t = Time;
        var before = _impacts.Count;
        if (Collision is { } world)
        {
            foreach (var car in _cars)
            {
                if (car.Simulated)
                {
                    Solver.Static(car, world, t, _impacts);
                }
            }
        }

        Solver.Cars(_cars, t, _impacts);
        if (World is { } map)
        {
            var floor = map.Collision.Bounds.Min.Y - FallMargin;
            foreach (var car in _cars)
            {
                if (car.Simulated && car.Position.Y < floor)
                {
                    car.State = CarRunState.Held;
                    car.Settings["fell"] = t.ToString("F3", System.Globalization.CultureInfo.InvariantCulture);
                }
            }
        }

        if (_telemetry.Length != _cars.Count)
        {
            _telemetry = new CarTelemetry[_cars.Count];
        }

        for (var i = 0; i < _cars.Count; i++)
        {
            _cars[i].Publish(Steps, h);
            _telemetry[i] = _cars[i].Telemetry!;
        }

        for (var i = before; i < _impacts.Count; i++)
        {
            _frameImpacts.Add(_impacts[i]);
            Impact?.Invoke(_impacts[i]);
        }

        StepWallMs += sw.Elapsed.TotalMilliseconds;
        Stepped?.Invoke(this);
    }

    private static void StepCar(SimCar car, double h, bool hasGround)
    {
        if (car.Simulated && hasGround)
        {
            car.Step(h);
        }
        else
        {
            car.StepHeld(h);
        }
    }
}
