using Slrr.Physics.Loading;
using Xunit;

namespace Slrr.Physics.Tests;

/// <summary>Gaps-wave behaviour: exe-measured drivetrain/limiter/slip/surface pieces, placement, damage, validation.</summary>
public class GapsWaveTests
{
    /// <summary>Split friction: ice (0.05) under the right half (x &gt; 0), dry tarmac on the left.</summary>
    private sealed class SplitMuGround : IGroundQuery
    {
        public bool Raycast(Vec3 origin, Vec3 direction, double maxDistance, out GroundHit hit)
        {
            var ok = new FlatGround(friction: origin.X > 0 ? 0.05 : 1.0).Raycast(origin, direction, maxDistance, out hit);
            return ok;
        }
    }

    /// <summary>Plane rising forward (-Z) by <c>grade</c> and to the left (-X) by <c>cross</c>.</summary>
    private sealed class SlopeGround(double grade, double cross) : IGroundQuery
    {
        public bool Raycast(Vec3 o, Vec3 d, double max, out GroundHit hit)
        {
            var n = new Vec3(cross, 1, grade).Normalized();
            var den = d.Dot(n);
            hit = default;
            if (Math.Abs(den) < 1e-9)
            {
                return false;
            }

            var t = (Vec3.Zero - o).Dot(n) / den;
            if (t < -1 || t > max)
            {
                return false;
            }

            hit = new GroundHit(o + d * t, n, t);
            return true;
        }
    }

    [Theory]
    [InlineData(5000, 4900, 7000, 1.0)]   // well below, not rising past the limit
    [InlineData(6990, 6980, 7000, 1.0)]   // predicted 7000: exactly at the limit
    [InlineData(6990, 6970, 7000, 0.5)]   // predicted 7010: half the step
    [InlineData(7050, 7040, 7000, 0.0)]   // above and still rising
    [InlineData(7050, 7150, 7000, 0.5)]   // above but falling to 6950: half the step
    public void RevLimiterIsThePredictiveFractionOfTheExe(double rpm, double prev, double limit, double expected) =>
        Assert.Equal(expected, Vehicle.RevLimiterFactor(rpm, prev, limit), 6);

    [Fact]
    public void ExeStyleDriveBeatsAnOpenDifferentialOnSplitFriction()
    {
        double Run(bool exeDrive)
        {
            var spec = TestCars.Simple();
            spec.Drivetrain.EqualSpinDrive = exeDrive;
            spec.Drivetrain.DiffLock = exeDrive ? 0.1 : 0;
            var car = new Vehicle(spec, new SplitMuGround());
            car.Reset(Vec3.Zero, 0);
            var input = new DriverInput { Throttle = 1, Gear = 1 };
            for (var i = 0; i < 120 * 2; i++)
            {
                car.Step(input);
            }

            return car.ForwardSpeed;
        }

        var open = Run(false);
        var exe = Run(true);
        Assert.True(exe > open * 1.5, $"exe-style drive {exe:F2} m/s vs open differential {open:F2} m/s");
    }

    [Fact]
    public void PlacingOnASlopeDoesNotKickTheCar()
    {
        var spec = TestCars.Simple();
        var car = new Vehicle(spec, new SlopeGround(0.2, 0.15));
        Assert.True(car.PlaceOnGround(new Vec3(0, 3, 0), Quat.Identity));
        var statics = spec.StaticWheelLoads();
        double maxLoad = 0, maxSpeed = 0;
        var input = new DriverInput { Brake = 1, Gear = 0 };
        for (var i = 0; i < 240; i++)
        {
            car.Step(input);
            maxLoad = Math.Max(maxLoad, car.Wheels.Max(w => w.Load));
            maxSpeed = Math.Max(maxSpeed, car.Velocity.Length);
        }

        Assert.All(car.Wheels, w => Assert.True(w.Grounded));
        Assert.True(maxLoad < 2.5 * statics.Max(), $"max load {maxLoad:F0} N vs static {statics.Max():F0} N");
        Assert.True(maxSpeed < 0.5, $"max speed {maxSpeed:F2} m/s");
        Assert.True(car.Up.Y < 0.99, "the body follows the slope");
        var yaw = Math.Atan2(-car.Forward.X, -car.Forward.Z);
        Assert.InRange(yaw, -0.01, 0.01); // held by the brakes: only settling creep
    }

    [Fact]
    public void LowFrictionSurfaceLengthensBraking()
    {
        var spec = TestCars.Simple();
        var dry = Scenarios.Braking100(spec, new FlatGround());
        var sand = Scenarios.Braking100(spec, new FlatGround(friction: 0.6));
        Assert.True(dry.Completed && sand.Completed);
        Assert.True(sand.Value > dry.Value * 1.3, $"dry {dry.Value:F1} m, friction 0.6 {sand.Value:F1} m");
    }

    [Fact]
    public void DamageScalesEngineAndBrakesByTheMeasuredCurve()
    {
        Assert.Equal(1.0, TyreModel.DamageFactor(0), 12);
        Assert.Equal(1 - 0.6 * 0.25, TyreModel.DamageFactor(0.5), 12);
        Assert.Equal(0.1, TyreModel.DamageFactor(1.0), 12);

        double SpeedAfter(Action<Vehicle> damage)
        {
            var car = new Vehicle(TestCars.Simple(), new FlatGround());
            damage(car);
            var input = new DriverInput { Throttle = 1, Gear = 1 };
            for (var i = 0; i < 240; i++)
            {
                car.Step(input);
            }

            return car.ForwardSpeed;
        }

        var intact = SpeedAfter(_ => { });
        var wrecked = SpeedAfter(c => c.EngineDamage = 1);
        Assert.True(wrecked < intact * 0.4, $"intact {intact:F2} m/s, engine damage 1: {wrecked:F2} m/s");
    }

    [Fact]
    public void SyntheticCarPassesTheValidationSuite()
    {
        var results = Validation.Run(TestCars.Simple());
        Assert.Equal(8, results.Count);
        Assert.All(results, r => Assert.True(r.Pass, $"{r.Name} = {r.Value:F2} {r.Unit} not in [{r.Min:F2}, {r.Max:F2}] ({r.Detail})"));
    }

    [Fact]
    public void ChassisBoxesComeFromThePhysicsMeshes()
    {
        if (GameRoot.Root is not { } root)
        {
            return;
        }

        var car = CarLoader.Load(root, CarLoader.FindCar(root, "Baiern_DevilSport")!);
        var spec = SpecBuilder.Build(car.Chassis, car.Dyno);
        Assert.True(spec.Body.Count >= 8, $"{spec.Body.Count} boxes");
        var frame = spec.Body.Single(b => b.Source.EndsWith("_phys_chassis_frame.scx", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(546, frame.Mass, 1);
        Assert.Equal(0.781, frame.HalfExtents.X, 2); // mesh x +-78.1 cm
        Assert.Equal(1.548, frame.HalfExtents.Z, 2);
    }
}
