using Slrr.Physics;
using Slrr.Physics.Loading;
using Slrr.World;
using Xunit;
using Xunit.Abstractions;

namespace Slrr.Game.Physics.Tests;

/// <summary>Shipped car on a shipped map (vacuous without the game data). Car and map are loaded once.</summary>
public class CorpusTests(ITestOutputHelper output)
{
    private static readonly Lazy<(VehicleSpec Spec, StaticWorld World)?> s_data = new(() =>
    {
        if (GameRoot.Root is not { } root)
        {
            return null;
        }

        var car = CarLoader.Load(root, CarLoader.FindCar(root, "Baiern_DevilSport")!);
        var spec = SpecBuilder.Build(car.Chassis, car.Dyno);
        var world = WorldLoader.Load(Path.Combine(root, "maps", "test_track.rpk"), new WorldLoadOptions { GameRoot = root, BuildRenderScene = false });
        return (spec, world);
    });

    private HeadlessDriveResult Drive(int cars = 1, bool simple = false, bool parallel = false, double seconds = 12)
    {
        var (spec, world) = s_data.Value!.Value;
        return HeadlessDrive.Run(new HeadlessDriveOptions
        {
            Root = GameRoot.Root!,
            Spec = spec,
            World = world,
            Cars = cars,
            SimpleExtras = simple,
            Parallel = parallel,
            Seconds = seconds,
        });
    }

    private void Print(HeadlessDriveResult r)
    {
        foreach (var t in r.Trace)
        {
            output.WriteLine(FormattableString.Invariant(
                $"{t.Time,6:F2} pos {t.Position.X,8:F2} {t.Position.Y,7:F2} {t.Position.Z,9:F2} {t.SpeedKmh,6:F1} km/h {t.Rpm,6:F0} rpm g{t.Gear} thr {t.Throttle:F1} brk {t.Brake:F1} st {t.Steer,5:F2} gL {t.GLongitudinal,5:F2} gT {t.GLateral,5:F2} gV {t.GVertical,5:F2} gr {t.Wheels.Count(w => w.Grounded)}"));
        }

        foreach (var i in r.Impacts.Take(8))
        {
            output.WriteLine(FormattableString.Invariant($"impact t={i.Time:F2} car {i.CarId} dv {i.DeltaV:F2} n {i.Normal} other {i.Other} src 0x{i.Source:X}"));
        }

        output.WriteLine(FormattableString.Invariant(
            $"{r.Cars} car(s): {r.MsPerStep:F4} ms/step; max {r.MaxSpeed * 3.6:F1} km/h, rpm {r.MaxRpm:F0}, gear {r.MaxGear}, |gL| {r.MaxAbsGLong:F2}, |gT| {r.MaxAbsGLat:F2}, dist {r.Distance:F1} m, grounded {r.GroundedFraction:P1}, impacts {r.Impacts.Count}, hash {r.StateHash:X16}"));
    }

    [Fact]
    public void DevilSportDrivesTheTestTrackSegmentDeterministically()
    {
        if (s_data.Value is null)
        {
            return;
        }

        var a = Drive();
        Print(a);
        var b = Drive();
        Assert.Equal(a.StateHash, b.StateHash);
        Assert.Equal(a.Final.Position, b.Final.Position);

        // Placement on the ground query: the start is TestTrack.posExit; the car rests on the ground (y -3.81 there).
        var first = a.Trace[0];
        Assert.InRange(first.Position.X, 188.0f, 190.5f);
        Assert.True(first.Wheels.All(w => w.Grounded), "all wheels on the ground at the start");
        Assert.InRange(first.GVertical, 0.8f, 1.2f);

        // Sane telemetry over the segment (bounds are plausibility, not game parity).
        Assert.InRange(a.MaxSpeed * 3.6, 60, 300);
        Assert.True(a.MaxRpm <= a.Spec.Engine.RpmLimit * 1.1, $"rpm {a.MaxRpm}");
        Assert.True(a.MaxGear >= 2, $"gear {a.MaxGear}");
        Assert.InRange(a.MaxAbsGLong, 0.2, 2.5);
        Assert.InRange(a.MaxAbsGLat, 0.0, 2.5);
        Assert.True(a.GroundedFraction > 0.9, $"grounded {a.GroundedFraction}");
        Assert.InRange(a.Distance, 50, 1000);
        Assert.True(a.Final.Speed < a.MaxSpeed * 0.5, "the brake phase slowed the car down");
        var accel = a.Trace.First(t => t.Time >= 2.0);
        Assert.True(accel.GLongitudinal > 0.1 && accel.Throttle > 0.5, "accelerating forward under throttle");
        var braking = a.Trace.First(t => t.Time >= 7.5);
        Assert.True(braking.GLongitudinal < -0.2, $"decelerating under the brake ({braking.GLongitudinal})");
    }

    [Fact]
    public void StepCostForOneAndTwentyCars()
    {
        if (s_data.Value is null)
        {
            return;
        }

        Drive(2, seconds: 1); // JIT warm-up
        foreach (var (cars, simple, parallel) in new[] { (1, false, false), (20, false, false), (20, false, true), (20, true, false) })
        {
            // best of 3 (the machine is shared; the minimum is the least disturbed run); the runs must agree bit for bit
            var runs = Enumerable.Range(0, 3).Select(_ => Drive(cars, simple, parallel, seconds: 6)).ToList();
            var r = runs.MinBy(x => x.MsPerStep)!;
            Assert.All(runs, x => Assert.Equal(r.StateHash, x.StateHash));
            output.WriteLine(FormattableString.Invariant(
                $"{cars} car(s){(simple ? " (1 full + simple)" : "")}{(parallel ? " parallel" : "")}: {r.MsPerStep:F4} ms per 120 Hz step (best of 3, worst {runs.Max(x => x.MsPerStep):F4}), impacts {r.Impacts.Count}, lead car {r.MaxSpeed * 3.6:F1} km/h"));

            Assert.True(r.MsPerStep < 50, $"{r.MsPerStep} ms per step");
            Assert.True(double.IsFinite(r.Final.Position.X));
        }
    }

    [Fact]
    public void CarSimCommandRuns()
    {
        if (s_data.Value is null)
        {
            return;
        }

        var o = new StringWriter();
        var e = new StringWriter();
        var rc = Slrr.Cli.CmdCarSim.Run(["Baiern_DevilSport", "--root", GameRoot.Root!, "--seconds", "12", "--every", "1"], o, e);
        output.WriteLine(o.ToString());
        output.WriteLine(e.ToString());
        Assert.Equal(0, rc);
        Assert.Contains("step cost", o.ToString());
    }
}
