using Slrr.Physics;
using Slrr.Physics.Loading;
using Slrr.World;
using Xunit;
using Xunit.Abstractions;

namespace Slrr.Game.Physics.Tests;

/// <summary>Gaps wave: impacts with impulse/energy/damage, the impact source interface, rollover recovery, the
/// wheelspin guard of the automatic box, the difflock verb, chassis boxes, and the Caynon start (corpus).</summary>
public class GapsWaveTests(ITestOutputHelper output)
{
    private static (PhysicsScene Scene, FullCar Car) Scene(float? wall = null)
    {
        var scene = TestWorlds.Scene(TestWorlds.Build(wall));
        var car = scene.AddCar(7, "test", TestWorlds.Car());
        car.Place(Vec3.Zero, Quat.Identity);
        car.State = CarRunState.Driving;
        return (scene, car);
    }

    [Fact]
    public void WallImpactCarriesImpulseEnergyAndDamage()
    {
        var (scene, car) = Scene(wall: -60);
        car.Input = new ScriptedInput().At(0, throttle: 1);
        IImpactSource source = scene;
        var seen = new List<ImpactEvent>();
        source.Impact += seen.Add;
        while (scene.Time < 10)
        {
            scene.Advance(1.0 / 60);
        }

        Assert.NotEmpty(seen);
        var hit = seen.MaxBy(i => i.Energy);
        output.WriteLine(FormattableString.Invariant(
            $"hit t={hit.Time:F2} dv {hit.DeltaV:F2} m/s impulse {hit.Impulse:F0} N s closing {hit.RelativeSpeed:F2} m/s energy {hit.Energy:F0} J damage {hit.Damage:F3}; car damage {car.Damage:F3}"));
        Assert.Equal(7, hit.CarId);
        Assert.Equal(0, hit.Other);
        Assert.Equal(1, hit.Surface);
        Assert.True(hit.Impulse > 1000, $"impulse {hit.Impulse}");
        Assert.True(hit.RelativeSpeed > 5, $"closing {hit.RelativeSpeed}");
        Assert.InRange(hit.Energy, 0.5 * 1200 * 25 * 0.2, 0.5 * 1200 * 40 * 40);
        Assert.True(car.Damage > 0, "the hit damaged the car");
        Assert.Equal(car.Damage, car.Vehicle.EngineDamage, 12);
        Assert.Equal((float)car.Damage, (float)car.Telemetry!.Damage);
        Assert.Contains(car.Vehicle.Wheels, w => w.Damage > 0); // the front corner nearest the wall
    }

    [Fact]
    public void OverturnedCarIsRecoveredWhenEnabled()
    {
        var (scene, car) = Scene();
        car.Place(new Vec3(0, 3, 0), Quat.FromAxisAngle(Vec3.UnitZ, Math.PI), snap: false);
        scene.RecoverOverturnedAfter = 1.0;
        var wasOverturned = false;
        while (scene.Time < 4)
        {
            scene.Advance(1.0 / 60);
            wasOverturned |= car.Telemetry!.Overturned;
        }

        Assert.True(wasOverturned);
        Assert.False(car.Overturned);
        Assert.True(car.Orientation.Rotate(Vec3.UnitY).Y > 0.95);
        Assert.True(car.Settings.ContainsKey("recovered"));
    }

    [Fact]
    public void ChassisBoxesFromTheSpecAreUsedForMapCollisions()
    {
        var spec = TestWorlds.Car();
        spec.Body.Add(new CollisionBox(new Vec3(0, 0.1, -1.6), new Vec3(0.8, 0.25, 0.6), 100, "nose"));
        spec.Body.Add(new CollisionBox(new Vec3(0, 0.2, 0.5), new Vec3(0.8, 0.4, 1.5), 500, "cabin"));
        var scene = TestWorlds.Scene(TestWorlds.Build(-40));
        var car = scene.AddCar(9, "boxes", spec);
        Assert.Equal(2, car.Boxes.Count);
        Assert.Equal(-2.2, car.Box.Center.Z - car.Box.HalfExtents.Z, 6);
        car.Place(Vec3.Zero, Quat.Identity);
        car.State = CarRunState.Driving;
        car.Input = new ScriptedInput().At(0, throttle: 1);
        while (scene.Time < 8)
        {
            scene.Advance(1.0 / 60);
        }

        Span<Vec3> corners = stackalloc Vec3[8];
        car.BoxCorners(car.Boxes[0], corners);
        foreach (var c in corners)
        {
            Assert.True(c.Z > -40.1, $"nose corner {c} went through the wall");
        }

        Assert.NotEmpty(scene.AllImpacts);
    }

    [Fact]
    public void AutomaticBoxDoesNotUpshiftOnWheelspinAtAStandstill()
    {
        // Tyres with almost no grip: the driven wheels spin up while the car hardly moves.
        var spec = TestWorlds.Car();
        foreach (var w in spec.Wheels)
        {
            w.Tyre.P[2] = 0.02;
        }

        var scene = TestWorlds.Scene(TestWorlds.Build());
        var car = scene.AddCar(3, "ice", spec);
        car.Place(Vec3.Zero, Quat.Identity);
        car.State = CarRunState.Driving;
        car.Input = new ScriptedInput().At(0, throttle: 1);
        while (scene.Time < 5)
        {
            scene.Advance(1.0 / 60);
        }

        Assert.True(car.Speed < 5, $"speed {car.Speed}");
        Assert.Equal(1, car.Drivetrain.Gear);
    }

    [Fact]
    public void DifflockVerbFollowsTheExe()
    {
        var (_, car) = Scene();
        GamePhysicsWorld.Execute(car, "difflock", ["difflock", "0.0"]);
        Assert.Equal(0.1, car.Spec.Drivetrain.DiffLock, 6);
        GamePhysicsWorld.Execute(car, "difflock", ["difflock", "0.75"]);
        Assert.Equal(0.75, car.Spec.Drivetrain.DiffLock, 6);
        GamePhysicsWorld.Execute(car, "damage_multiplier", ["damage_multiplier", "0.5"]);
        Assert.Equal(0.5, car.DamageMultiplier, 6);
    }

    [Fact]
    public void SurfaceMappingUsesTheExeDefaults()
    {
        var mapping = new SurfaceMapping();
        var (f, rr, cc) = mapping.Map(null, 3);
        Assert.Equal(1.0, f);
        Assert.Equal(0.001, rr, 9);
        Assert.Equal(0.2, cc, 9);
    }

    [Fact]
    public void CaynonStartIsNoLongerStuck()
    {
        if (GameRoot.Root is not { } root)
        {
            return;
        }

        var car = CarLoader.Load(root, CarLoader.FindCar(root, "Duhen_SunStrip_1_5")!);
        var spec = SpecBuilder.Build(car.Chassis, car.Dyno);
        var world = WorldLoader.Load(Path.Combine(root, "maps", "Addon_Maps", "Caynon.rpk"), new WorldLoadOptions { GameRoot = root, BuildRenderScene = false });

        // CaynonTrack.posStart (0, 8.7, -20), oriStart Ypr(0, 0, 0) (MEASURED, decompiled CaynonTrack.class).
        var r = HeadlessDrive.Run(new HeadlessDriveOptions
        {
            Root = root,
            Spec = spec,
            World = world,
            At = new Vec3(0, 8.7, -20),
            Yaw = 0,
            Seconds = 6,
            Input = new ScriptedInput().At(0).At(1.0, throttle: 1.0),
        });
        output.WriteLine(FormattableString.Invariant($"Caynon: distance {r.Distance:F1} m, max {r.MaxSpeed * 3.6:F1} km/h, gear {r.MaxGear}, grounded {r.GroundedFraction:P0}"));
        Assert.True(r.Distance > 10, $"moved only {r.Distance:F1} m");
    }

    [Fact]
    public void PhysCheckCommandRunsForOneCar()
    {
        if (GameRoot.Root is not { } root)
        {
            return;
        }

        var o = new StringWriter();
        var e = new StringWriter();
        var rc = Slrr.Cli.CmdPhysCheck.Run(["Baiern_DevilSport", "--root", root, "--quiet"], o, e);
        output.WriteLine(o.ToString());
        Assert.Contains("outliers: 0", o.ToString());
        Assert.Equal(0, rc);
    }
}
