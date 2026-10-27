using System.Diagnostics;
using System.Numerics;
using Slrr.AI.Driving;
using Slrr.AI.Roads;
using Slrr.AI.Traffic;
using Slrr.Game.Physics;
using Slrr.Physics;
using PhysGround = Slrr.Physics.IGroundQuery;

namespace Slrr.AI.Race;

/// <summary>Options of <see cref="HeadlessRace.Run"/>.</summary>
public sealed record HeadlessRaceOptions
{
    public int AiCars { get; init; } = 8;

    public int TrafficCars { get; init; } = 30;

    public RaceRules Rules { get; init; } = new();

    /// <summary>Road source and race route (default: <see cref="SyntheticRoads.World"/>).</summary>
    public IRoadSource? Roads { get; init; }

    public IRoadPath? Route { get; init; }

    /// <summary>Ground (default flat at y = 0) and optional static collision (a shipped map's world).</summary>
    public PhysGround? Ground { get; init; }

    public IStaticCollision? Collision { get; init; }

    /// <summary>Car spec per AI car (default: <see cref="SyntheticCars.Default"/> with torque 0.9..1.25).</summary>
    public Func<int, VehicleSpec>? Spec { get; init; }

    /// <summary>AI_level (raw script value) per car (default spread 0..1).</summary>
    public Func<int, double>? Level { get; init; }

    /// <summary>Measure grip/braking with Slrr.Physics' scenarios (else 0.8 g, GUESSED).</summary>
    public bool MeasureCaps { get; init; } = true;

    public double FrameTime { get; init; } = 1.0 / 60;

    public TrafficSettings Traffic { get; init; } = new();

    /// <summary>Extra seconds simulated after the race is done (0) and the safety limit.</summary>
    public double MaxSeconds { get; init; } = 900;
}

/// <summary>Outcome of a headless race.</summary>
public sealed record HeadlessRaceResult(
    RaceResults Results,
    IReadOnlyList<RaceEntrant> Entrants,
    double SimSeconds,
    long Steps,
    double MsPerStep,
    double TrafficMsPerFrame,
    int MaxSceneCars,
    int TrafficSpawned,
    int TrafficDespawned,
    int TrafficPeak,
    int CarImpacts,
    double MinTrafficGap,
    ulong StateHash,
    IReadOnlyList<string> Notes);

/// <summary>
/// Runs a whole race headless: AI full cars (Slrr.Physics vehicles) on the race route with grid, countdown, laps and
/// results, plus a traffic population (SimpleCars) on the traffic lanes around a focus car that drives the lanes too.
/// Deterministic: same options → same <see cref="HeadlessRaceResult.StateHash"/>.
/// </summary>
public static class HeadlessRace
{
    public const int FirstAiId = 0x7000_0000;

    public static HeadlessRaceResult Run(HeadlessRaceOptions o)
    {
        var notes = new List<string>();
        SampledPath? circuit = null;
        var roads = o.Roads ?? SyntheticRoads.World(out circuit);
        var route = o.Route ?? circuit ?? throw new ArgumentException("a route is needed with a custom road source");
        var scene = new PhysicsScene();
        scene.SetGround(o.Ground ?? new FlatGround(), o.Collision);

        var session = new RaceSession(scene, roads, route, o.Rules);
        for (var i = 0; i < o.AiCars; i++)
        {
            var spec = o.Spec?.Invoke(i) ?? SyntheticCars.Default(0.9 + (0.35 * i / Math.Max(1, o.AiCars - 1)), name: $"ai{i}");
            var car = scene.AddCar(FirstAiId + i, $"ai{i}", spec);
            var caps = o.MeasureCaps ? VehicleCaps.Measure(spec) : VehicleCaps.For(car);
            var level = o.Level?.Invoke(i) ?? (o.AiCars > 1 ? i / (double)(o.AiCars - 1) : 0.5);
            session.Add(car, $"ai{i}", ai: true, caps, new DriverSettings { Level = AiLevel.Stored(level) });
            if (i == 0)
            {
                notes.Add($"caps ai0: {caps.Source}");
            }
        }

        // traffic: a focus car cruising on the first lane, the population around it
        AiTrafficDirector? traffic = null;
        SimCar? focus = null;
        if (o.TrafficCars > 0 && roads.Lanes.Count > 0)
        {
            traffic = new AiTrafficDirector(scene, roads, null, o.Traffic with { MaxCars = o.TrafficCars });
            focus = scene.AddSimpleCar(FirstAiId + 0x10000, "focus", new SimpleCarSpec());
            var lane = roads.Lanes[0];
            var start = lane.Sample(0);
            focus.Place(new Vec3(start.Position.X, start.Position.Y + 0.5, start.Position.Z), Conv.FromYpr(RoadMath.YawOf(start.Tangent), 0, 0));
            traffic.Add(0, focus.Id, start.Position);
            var focusCar = focus;
            traffic.Focus = () => focusCar.Position.ToVector3();
        }

        var hash = 14695981039346656037UL;
        void Mix(double v)
        {
            hash ^= (ulong)BitConverter.DoubleToInt64Bits(v);
            hash *= 1099511628211UL;
        }

        var maxCars = 0;
        var peak = 0;
        var minGap = double.PositiveInfinity;
        scene.Stepped += s =>
        {
            maxCars = Math.Max(maxCars, s.AllCars.Count);
            if (s.Steps % 12 == 0)
            {
                foreach (var c in s.AllCars)
                {
                    Mix(c.Id);
                    Mix(c.Position.X);
                    Mix(c.Position.Y);
                    Mix(c.Position.Z);
                    Mix(c.Velocity.X);
                    Mix(c.Velocity.Z);
                }

                if (traffic is not null)
                {
                    peak = Math.Max(peak, traffic.Entries.Count(e => e.Spawned));
                    minGap = Math.Min(minGap, MinGap(traffic));
                }
            }
        };

        var read0 = PathFollowingDriver.TotalReadSeconds;
        session.StartCountdown();
        var trafficWatch = new Stopwatch();
        long frames = 0;
        while (session.Phase != RacePhase.Done && scene.Time < o.MaxSeconds)
        {
            scene.Advance(o.FrameTime);
            trafficWatch.Start();
            traffic?.Step((float)o.FrameTime);
            trafficWatch.Stop();
            frames++;
        }

        if (session.Phase != RacePhase.Done)
        {
            notes.Add($"race not done after {o.MaxSeconds} s");
        }

        notes.Add(FormattableString.Invariant($"AI driver Read: {(PathFollowingDriver.TotalReadSeconds - read0) * 1000 / Math.Max(1, scene.Steps):F3} ms per step for all drivers"));
        var impacts = scene.AllImpacts.Count(i => i.Other != 0 && i.CarId < i.Other);
        return new HeadlessRaceResult(session.Results(), session.Entrants, scene.Time, scene.Steps, scene.StepWallMs / Math.Max(1, scene.Steps),
            trafficWatch.Elapsed.TotalMilliseconds / Math.Max(1, frames), maxCars, traffic?.Spawned ?? 0, traffic?.Despawned ?? 0, peak, impacts,
            minGap, hash, notes);
    }

    /// <summary>Smallest bumper-to-bumper gap (centre distance - car length) between two traffic cars on the same lane (m).</summary>
    public static double MinGap(AiTrafficDirector traffic)
    {
        var min = double.PositiveInfinity;
        var list = traffic.Entries.ToList();
        for (var i = 0; i < list.Count; i++)
        {
            for (var j = i + 1; j < list.Count; j++)
            {
                if (!ReferenceEquals(list[i].Lane, list[j].Lane))
                {
                    continue;
                }

                var d = Vector3.Distance(list[i].Car.Position.ToVector3(), list[j].Car.Position.ToVector3());
                min = Math.Min(min, d - 4.3);
            }
        }

        return min;
    }
}
