using System.Diagnostics;
using Slrr.Physics;
using Slrr.Physics.Loading;
using Slrr.World;

namespace Slrr.Game.Physics;

public sealed class HeadlessDriveOptions
{
    public required string Root { get; init; }

    /// <summary>Car gametype query (<see cref="CarLoader.FindCar"/>).</summary>
    public string Car { get; init; } = "Baiern_DevilSport";

    /// <summary>Map archive, game-relative or absolute.</summary>
    public string Map { get; init; } = "maps/test_track.rpk";

    /// <summary>Chassis origin start (x, y, z); null = the known start of the map (<see cref="HeadlessDrive.KnownStart"/>).</summary>
    public Vec3? At { get; init; }

    public double? Yaw { get; init; }

    public double Seconds { get; init; } = 12;

    /// <summary>Cars on the grid (the first is the measured/traced one, all get the same input).</summary>
    public int Cars { get; init; } = 1;

    /// <summary>Extra cars as <see cref="SimpleCar"/>s instead of full vehicles.</summary>
    public bool SimpleExtras { get; init; }

    public bool Parallel { get; init; }

    public IVehicleInputSource? Input { get; init; }

    /// <summary>Trace interval [s].</summary>
    public double TraceEvery { get; init; } = 0.5;

    /// <summary>Host frame length fed to the accumulator (default 1/60 s: two fixed steps per frame).</summary>
    public double FrameTime { get; init; } = 1.0 / 60;

    /// <summary>Reuse an already loaded map (benchmarks, determinism runs).</summary>
    public StaticWorld? World { get; init; }

    /// <summary>Reuse an already built car (benchmarks, determinism runs).</summary>
    public VehicleSpec? Spec { get; init; }
}

public sealed class HeadlessDriveResult
{
    public required VehicleSpec Spec { get; init; }

    public required StaticWorld World { get; init; }

    public required Vec3 Start { get; init; }

    public required double StartYaw { get; init; }

    public double CarLoadMs { get; init; }

    public double MapLoadMs { get; init; }

    public required IReadOnlyList<CarTelemetry> Trace { get; init; }

    public required CarTelemetry Final { get; init; }

    public required IReadOnlyList<ImpactEvent> Impacts { get; init; }

    public required long Steps { get; init; }

    public required int Cars { get; init; }

    /// <summary>Wall milliseconds per 120 Hz step (all cars).</summary>
    public required double MsPerStep { get; init; }

    public required double MaxSpeed { get; init; }

    public required double MaxRpm { get; init; }

    public required int MaxGear { get; init; }

    /// <summary>Max |longitudinal g| outside impacts.</summary>
    public required double MaxAbsGLong { get; init; }

    public required double MaxAbsGLat { get; init; }

    /// <summary>Fraction of steps with all wheels of the traced car on the ground.</summary>
    public required double GroundedFraction { get; init; }

    public required double Distance { get; init; }

    /// <summary>FNV-1a hash over every step's pose/velocity/rpm bits of the traced car (determinism check).</summary>
    public required ulong StateHash { get; init; }

    public required IReadOnlyList<string> Notes { get; init; }
}

/// <summary>
/// Places a shipped car (built by its own part scripts through <see cref="CarLoader"/>) on a shipped map (through
/// <see cref="WorldLoader"/>) and drives a scripted input segment through <see cref="PhysicsScene"/>: no game scripts,
/// no host. Used by the <c>carsim</c> CLI and the corpus tests.
/// </summary>
public static class HeadlessDrive
{
    /// <summary>
    /// Start positions read from the shipped scripts. test_track: <c>TestTrack.posExit = (189.2, -3.8, -1417.0)</c>,
    /// <c>oriExit = Ypr(2.88, 0.05, 0)</c>, which TestTrack uses as posStart/oriStart (MEASURED, decompiled
    /// TestTrack.class; the map's ground there is at y = -3.81).
    /// </summary>
    public static (Vec3 Pos, double Yaw)? KnownStart(string map) =>
        Path.GetFileNameWithoutExtension(map).ToLowerInvariant() switch
        {
            "test_track" => (new Vec3(189.2, -3.8, -1417.0), 2.88),
            _ => null,
        };

    /// <summary>
    /// The default scripted segment (GUESSED driver), laid out for the test_track start: engine idling 1 s, full
    /// throttle with the automatic box up the exit ramp (heading ~(-0.26, 0.97) in x/z), a small left-right weave,
    /// then the brake to a stop before the obstacle ~120 m ahead (an object at about (156, -1295)).
    /// </summary>
    public static ScriptedInput LapSegment() => new ScriptedInput()
        .At(0.0)
        .At(1.0, throttle: 1.0)
        .At(3.0, throttle: 1.0, steer: 0.02)
        .At(4.0, throttle: 1.0, steer: -0.02)
        .At(5.0, throttle: 1.0)
        .At(6.5, brake: 1.0)
        .At(9.5, brake: 0.3);

    public static HeadlessDriveResult Run(HeadlessDriveOptions o)
    {
        var notes = new List<string>();
        var sw = Stopwatch.StartNew();
        var spec = o.Spec;
        if (spec is null)
        {
            var type = CarLoader.FindCar(o.Root, o.Car) ?? throw new ArgumentException($"no car matching '{o.Car}'");
            var loaded = CarLoader.Load(o.Root, type);
            spec = SpecBuilder.Build(loaded.Chassis, loaded.Dyno);
            if (loaded.EngineCompletedInCSharp)
            {
                notes.Add("engine data completed in C# (CarLoader.CompleteEngine): numbers are not the game's");
            }
        }

        var carMs = sw.Elapsed.TotalMilliseconds;
        sw.Restart();
        var world = o.World ?? WorldLoader.Load(Path.IsPathRooted(o.Map) ? o.Map : Path.Combine(o.Root, o.Map),
            new WorldLoadOptions { GameRoot = o.Root, BuildRenderScene = false });
        var mapMs = sw.Elapsed.TotalMilliseconds;

        var known = KnownStart(o.Map);
        var start = o.At ?? known?.Pos ?? CenterStart(world);
        var yaw = o.Yaw ?? known?.Yaw ?? 0;
        if (o.At is null && known is null)
        {
            notes.Add("no known start for this map: ground under the centre of the collision bounds (GUESSED)");
        }

        var scene = new PhysicsScene { Parallel = o.Parallel };
        scene.SetWorld(world);
        var input = o.Input ?? LapSegment();
        var ori = Conv.FromYpr(yaw, 0, 0);
        var left = ori.Rotate(new Vec3(-1, 0, 0));
        var forward = ori.Rotate(new Vec3(0, 0, -1));
        for (var i = 0; i < Math.Max(1, o.Cars); i++)
        {
            // Grid ahead of the start (behind it on test_track is the building): 2 columns 3.5 m apart (the second to
            // the car's left), rows 8 m apart. The first car stays exactly on the start.
            var pos = start + left * (i % 2 * 3.5) + forward * (i / 2 * 8.0);
            SimCar car = i > 0 && o.SimpleExtras
                ? scene.AddSimpleCar(1000 + i, $"simple{i}", SimpleCarSpec.From(spec))
                : scene.AddCar(1000 + i, i == 0 ? spec.Name : $"{spec.Name}#{i}", spec);
            car.Place(pos, ori);
            car.Input = input;
            car.State = CarRunState.Driving;
        }

        var traced = scene.AllCars[0];
        var trace = new List<CarTelemetry>();
        var hash = 14695981039346656037UL;
        void Mix(double v)
        {
            hash ^= (ulong)BitConverter.DoubleToInt64Bits(v);
            hash *= 1099511628211UL;
        }

        double maxSpeed = 0, maxRpm = 0, maxGl = 0, maxGlat = 0;
        int maxGear = 0, grounded = 0;
        var nextTrace = 0.0;
        var lastImpact = double.NegativeInfinity;
        scene.Impact += i =>
        {
            if (i.CarId == traced.Id)
            {
                lastImpact = i.Time;
            }
        };
        var steps = (long)Math.Round(o.Seconds * PhysicsScene.StepRate);
        scene.Stepped += s =>
        {
            var t = traced.Telemetry!;
            Mix(traced.Position.X);
            Mix(traced.Position.Y);
            Mix(traced.Position.Z);
            Mix(traced.Velocity.X);
            Mix(traced.Velocity.Y);
            Mix(traced.Velocity.Z);
            Mix(traced.Orientation.W);
            Mix(t.Rpm);
            maxSpeed = Math.Max(maxSpeed, t.Speed);
            maxRpm = Math.Max(maxRpm, t.Rpm);
            maxGear = Math.Max(maxGear, t.Gear);
            // g maxima skip the first half second (settling) and 0.15 s after an impact (crash spikes are reported as impacts).
            if (s.Steps > PhysicsScene.StepRate / 2 && s.Time - lastImpact > 0.15)
            {
                maxGl = Math.Max(maxGl, Math.Abs(t.GLongitudinal));
                maxGlat = Math.Max(maxGlat, Math.Abs(t.GLateral));
            }

            if (t.Wheels.All(w => w.Grounded))
            {
                grounded++;
            }

            if (t.Time >= nextTrace - 1e-9)
            {
                trace.Add(t);
                nextTrace += o.TraceEvery;
            }
        };

        while (scene.Steps < steps)
        {
            scene.Advance(Math.Min(o.FrameTime, (steps - scene.Steps) * PhysicsScene.FixedStep + 1e-9));
        }

        var final = traced.Telemetry!;
        return new HeadlessDriveResult
        {
            Spec = spec,
            World = world,
            Start = start,
            StartYaw = yaw,
            CarLoadMs = carMs,
            MapLoadMs = mapMs,
            Trace = trace,
            Final = final,
            Impacts = scene.AllImpacts.ToList(),
            Steps = scene.Steps,
            Cars = scene.AllCars.Count,
            MsPerStep = scene.StepWallMs / Math.Max(1, scene.Steps),
            MaxSpeed = maxSpeed,
            MaxRpm = maxRpm,
            MaxGear = maxGear,
            MaxAbsGLong = maxGl,
            MaxAbsGLat = maxGlat,
            GroundedFraction = grounded / (double)Math.Max(1, scene.Steps),
            Distance = traced.Odometer,
            StateHash = hash,
            Notes = notes,
        };
    }

    private static Vec3 CenterStart(StaticWorld world)
    {
        var (min, max) = world.Collision.Bounds;
        var c = (min + max) * 0.5f;
        return world.Ground.TryGetGround(c.X, c.Z, float.PositiveInfinity, out var hit) ? hit.Position.ToVec3() : c.ToVec3();
    }
}
