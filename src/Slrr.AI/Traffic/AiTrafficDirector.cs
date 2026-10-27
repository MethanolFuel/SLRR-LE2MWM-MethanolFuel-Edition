using System.Globalization;
using System.Numerics;
using Slrr.AI.Driving;
using Slrr.AI.Roads;
using Slrr.Engine.Commands;
using Slrr.Game.Physics;
using Slrr.Physics;

namespace Slrr.AI.Traffic;

/// <summary>Traffic tunables; all GUESSED (the exe's traffic model was not traced).</summary>
public sealed record TrafficSettings
{
    /// <summary>Most traffic cars spawned around the focus at once.</summary>
    public int MaxCars { get; init; } = 30;

    /// <summary>Spawn ring around the focus (m): not closer than <see cref="SpawnMin"/>, not further than <see cref="SpawnMax"/>.</summary>
    public float SpawnMin { get; init; } = 60;

    public float SpawnMax { get; init; } = 300;

    /// <summary>Spawned cars further than this from the focus are removed.</summary>
    public float DespawnDistance { get; init; } = 380;

    /// <summary>Free space needed around a spawn point (m).</summary>
    public float SpawnClearance { get; init; } = 18;

    /// <summary>Population check interval (s) and spawns per check.</summary>
    public double SpawnInterval { get; init; } = 0.25;

    public int SpawnsPerCheck { get; init; } = 2;

    /// <summary>Cruise speed (m/s); the slow variant (<c>AI_GoToTrafficSlow</c>) halves it. Same values the commands layer
    /// used (12 / 6 m/s).</summary>
    public double CruiseSpeed { get; init; } = 12;

    /// <summary>Random speed spread per car (± fraction).</summary>
    public double SpeedSpread { get; init; } = 0.15;

    /// <summary>Comfortable lateral acceleration for traffic (m/s²).</summary>
    public double LateralAccel { get; init; } = 2.5;

    public double BrakeDecel { get; init; } = 4.0;

    /// <summary>First id given to spawned SimpleCars (they have no script instance; the id range is this project's choice).</summary>
    public int FirstSpawnId { get; init; } = 0x7100_0000;

    public ulong Seed { get; init; } = 0x5EED_7AFF1C;

    /// <summary>Body of spawned cars.</summary>
    public SimpleCarSpec CarSpec { get; init; } = new();
}

/// <summary>A traffic car (wraps the commands layer's <see cref="TrafficCar"/> record for <see cref="ITrafficDirector.Cars"/>).</summary>
public sealed class TrafficEntry
{
    internal TrafficEntry(TrafficCar info, SimCar car, PathFollowingDriver driver, bool spawned)
    {
        Info = info;
        Car = car;
        Driver = driver;
        Spawned = spawned;
    }

    public TrafficCar Info { get; }

    public SimCar Car { get; }

    public PathFollowingDriver Driver { get; }

    /// <summary>Created by the population (removed again when far away) rather than handed over by a script.</summary>
    public bool Spawned { get; }

    public IRoadPath? Lane { get; internal set; }

    public int LaneChanges { get; internal set; }
}

/// <summary>
/// Lane-following traffic on top of <see cref="PhysicsScene"/>: spawns <see cref="SimpleCar"/>s on the road source's lanes
/// in a ring around a focus point (the player), removes them when far away, drives every traffic car with a
/// <see cref="PathFollowingDriver"/> on its lane (cruise speed, curvature-limited, gap keeping to any car ahead in the
/// lane, no overtaking) and picks a successor lane at lane ends. Replaces the commands layer's straight-driving
/// <see cref="TrafficDirector"/>; same events: <c>car_add carId trafficId</c> / <c>car_rem carId trafficId</c> posted to the
/// map (format MEASURED, exe 0x610394/0x6103a4; token roles MEASURED from City.handleEvent). Deterministic (own seeded RNG,
/// fixed iteration order).
/// </summary>
public sealed class AiTrafficDirector : ITrafficDirector
{
    public const int EventCommand = 16;

    private readonly PhysicsScene _scene;
    private readonly IRoadSource _roads;
    private readonly IScriptEventSink? _events;
    private readonly SortedDictionary<int, TrafficEntry> _entries = [];
    private ulong _rng;
    private int _nextTraffic = 1;
    private int _nextSpawnId;
    private double _nextCheck;

    public AiTrafficDirector(PhysicsScene scene, IRoadSource roads, IScriptEventSink? events = null, TrafficSettings? settings = null)
    {
        _scene = scene;
        _roads = roads;
        _events = events;
        Settings = settings ?? new TrafficSettings();
        _rng = Settings.Seed | 1;
        _nextSpawnId = Settings.FirstSpawnId;
    }

    public TrafficSettings Settings { get; }

    /// <summary>Where traffic is spawned around (the player's car); null = no spawning.</summary>
    public Func<Vector3?>? Focus { get; set; }

    /// <summary>Map instance id the car_add/car_rem events are posted to.</summary>
    public int MapId { get; set; }

    public IReadOnlyCollection<TrafficCar> Cars => _entries.Values.Select(e => e.Info).ToList();

    public IReadOnlyCollection<TrafficEntry> Entries => _entries.Values;

    public int Spawned { get; private set; }

    public int Despawned { get; private set; }

    public TrafficEntry? Get(int trafficId) => _entries.GetValueOrDefault(trafficId);

    public TrafficEntry? ByCar(int carId) => _entries.Values.FirstOrDefault(e => e.Car.Id == carId);

    /// <summary>Hands an existing physics car to traffic (<c>AI_GoToTraffic</c>, <c>GroundRef.addTrafficCar</c>): it is
    /// put on the nearest lane (from <paramref name="position"/> or its own position) and driven from there.</summary>
    public int Add(int mapId, int carId, Vector3? position) => Add(mapId, carId, position, slow: false);

    public int Add(int mapId, int carId, Vector3? position, bool slow)
    {
        if (ByCar(carId) is { } existing)
        {
            return existing.Info.TrafficId;
        }

        if (_scene.Car(carId) is not { } car)
        {
            return 0;
        }

        var at = position ?? car.Position.ToVector3();
        var lane = _roads.Nearest(at, 200f)?.Path;
        return Register(mapId, car, lane, spawned: false, slow, at);
    }

    private int Register(int mapId, SimCar car, IRoadPath? lane, bool spawned, bool slow, Vector3 at)
    {
        var info = new TrafficCar(_nextTraffic++, mapId, car.Id) { Slow = slow };
        var spread = 1 + (Settings.SpeedSpread * ((NextDouble() * 2) - 1));
        var caps = VehicleCaps.For(car) with
        {
            LateralAccel = Settings.LateralAccel,
            BrakeDecel = Settings.BrakeDecel,
            TopSpeed = Settings.CruiseSpeed * spread * (slow ? 0.5 : 1),
        };
        var driver = new PathFollowingDriver(car, caps, new DriverSettings { Overtake = false, Level = AiLevel.Default })
        {
            Others = _scene.AllCars,
        };
        var e = new TrafficEntry(info, car, driver, spawned) { Lane = lane };
        driver.SetPath(lane, _roads.Next(lane ?? EmptyPath.Instance).Count == 0 && lane is { Closed: false } ? PathEnd.Stop : PathEnd.Continue);
        car.Input = driver;
        car.State = CarRunState.Driving;
        _entries[info.TrafficId] = e;
        _events?.Queue(mapId, car.Id, EventCommand, string.Create(CultureInfo.InvariantCulture, $"car_add {car.Id} {info.TrafficId}"));
        return info.TrafficId;
    }

    public void Remove(int trafficId)
    {
        if (!_entries.Remove(trafficId, out var e))
        {
            return;
        }

        if (ReferenceEquals(e.Car.Input, e.Driver))
        {
            e.Car.Input = null;
        }

        if (e.Spawned)
        {
            _scene.Remove(e.Car.Id);
            Despawned++;
        }

        _events?.Queue(e.Info.MapId, e.Car.Id, EventCommand, string.Create(CultureInfo.InvariantCulture, $"car_rem {e.Car.Id} {e.Info.TrafficId}"));
    }

    public void RemoveAll()
    {
        foreach (var id in _entries.Keys.ToList())
        {
            Remove(id);
        }
    }

    public void SetBehaviour(int trafficId, int mode)
    {
        if (_entries.TryGetValue(trafficId, out var e))
        {
            e.Info.Behaviour = mode;
        }
    }

    /// <summary>Per frame (or per physics step): lane changes at lane ends, despawn, spawn.</summary>
    public void Step(float dt)
    {
        var t = _scene.Time;
        foreach (var e in _entries.Values.ToList())
        {
            if (_scene.Car(e.Car.Id) is null)
            {
                _entries.Remove(e.Info.TrafficId);
                continue;
            }

            if (e.Lane is { Closed: false } lane && e.Driver.Progress >= lane.Length - 3)
            {
                var next = _roads.Next(lane);
                if (next.Count > 0)
                {
                    var pick = next[(int)(NextU64() % (ulong)next.Count)];
                    e.Lane = pick;
                    e.LaneChanges++;
                    e.Driver.SetPath(pick, _roads.Next(pick).Count == 0 && !pick.Closed ? PathEnd.Stop : PathEnd.Continue);
                }
                else if (e.Spawned)
                {
                    Remove(e.Info.TrafficId);
                }
            }
        }

        if (Focus?.Invoke() is not { } focus || t < _nextCheck)
        {
            return;
        }

        _nextCheck = t + Settings.SpawnInterval;
        foreach (var e in _entries.Values.Where(e => e.Spawned).ToList())
        {
            if (RoadMath.Horizontal(e.Car.Position.ToVector3() - focus) > Settings.DespawnDistance)
            {
                Remove(e.Info.TrafficId);
            }
        }

        var lanes = _roads.Lanes;
        if (lanes.Count == 0)
        {
            return;
        }

        var total = lanes.Sum(l => (double)l.Length);
        for (var k = 0; k < Settings.SpawnsPerCheck && _entries.Values.Count(e => e.Spawned) < Settings.MaxCars; k++)
        {
            // a random point on the network (length weighted), kept if it lies in the spawn ring and is free
            var x = NextDouble() * total;
            var lane = lanes[^1];
            foreach (var l in lanes)
            {
                if (x < l.Length)
                {
                    lane = l;
                    break;
                }

                x -= l.Length;
            }

            var smp = lane.Sample((float)x);
            var d = RoadMath.Horizontal(smp.Position - focus);
            if (d < Settings.SpawnMin || d > Settings.SpawnMax || !Free(smp.Position))
            {
                continue;
            }

            var car = new SimpleCar(_nextSpawnId++, "traffic", Settings.CarSpec, _scene.Ground);
            _scene.Add(car);
            car.Place(new Vec3(smp.Position.X, smp.Position.Y + 0.5, smp.Position.Z), Conv.FromYpr(RoadMath.YawOf(smp.Tangent), 0, 0));
            var v = Settings.CruiseSpeed * 0.8;
            var fwd = car.Orientation.Rotate(new Vec3(0, 0, -1));
            car.Velocity = fwd * v; // spawned rolling (GUESSED), so the stream does not start from standstill
            Register(MapId, car, lane, spawned: true, slow: false, smp.Position);
            Spawned++;
        }
    }

    private bool Free(Vector3 p)
    {
        var c2 = Settings.SpawnClearance * Settings.SpawnClearance;
        foreach (var c in _scene.AllCars)
        {
            var d = c.Position.ToVector3() - p;
            if ((d.X * d.X) + (d.Z * d.Z) < c2)
            {
                return false;
            }
        }

        return true;
    }

    // xorshift64*: deterministic, independent of System.Random's implementation
    private ulong NextU64()
    {
        _rng ^= _rng >> 12;
        _rng ^= _rng << 25;
        _rng ^= _rng >> 27;
        return _rng * 0x2545F4914F6CDD1DUL;
    }

    private double NextDouble() => (NextU64() >> 11) * (1.0 / (1UL << 53));

    private sealed class EmptyPath : IRoadPath
    {
        public static readonly EmptyPath Instance = new();

        public float Length => 0;

        public bool Closed => true;

        public string Name => "";

        public PathSample Sample(float s) => default;

        public float Project(Vector3 p, float hint = float.NaN, float window = float.PositiveInfinity) => 0;
    }
}
