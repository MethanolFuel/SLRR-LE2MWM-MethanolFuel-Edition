using Xunit;
using Xunit.Abstractions;

namespace Slrr.Physics.Tests;

public class VehicleTests(ITestOutputHelper output)
{
    /// <summary>A conservative configuration: frictionless tyres, no damping, no aero, no rolling/bearing losses.</summary>
    private static VehicleSpec Conservative()
    {
        var spec = TestCars.Simple();
        spec.Rollbars.Clear();
        foreach (var w in spec.Wheels)
        {
            w.DampingBump = w.DampingRebound = 0;
            w.Tyre.P[2] = 0; // D = 0: the magic formula returns 0
            w.Tyre.RollResistance = 0;
            w.Tyre.Bearing = 0;
        }

        return spec;
    }

    [Fact]
    public void BouncingOnUndampedSpringsConservesEnergy()
    {
        var car = new Vehicle(Conservative(), new FlatGround()) { Aero = false };
        car.Position += new Vec3(0, 0.03, 0); // 3 cm drop, stays inside the spring travel
        car.AngularVelocity = new Vec3(0.2, 0, 0.1); // some pitch/roll as well
        car.SyncContacts();
        var e0 = car.MechanicalEnergy();
        double min = e0, max = e0, firstSum = 0, lastSum = 0;
        const int Seconds = 20;
        for (var i = 0; i < 120 * Seconds; i++)
        {
            car.Step(new DriverInput { Gear = 0, Clutch = 0 });
            var e = car.MechanicalEnergy();
            min = Math.Min(min, e);
            max = Math.Max(max, e);
            firstSum += i < 240 ? e : 0;
            lastSum += i >= 120 * Seconds - 240 ? e : 0;
            Assert.True(car.Wheels.All(w => w.Grounded), "a wheel left the ground");
        }

        // The oscillation's own energy is m*g*0.03 plus the rotation (~200 J). Semi-implicit Euler keeps the error
        // bounded (it oscillates, it does not grow): spread < 8 % (measured 6.1 %), drift of the 2 s mean over 20 s < 1 % (measured 0.3 %).
        var swing = 0.5 * car.Spec.Mass * car.Spec.Gravity * 0.03;
        var drift = Math.Abs(lastSum - firstSum) / 240;
        output.WriteLine($"E0 {e0:F2} J, min {min:F2}, max {max:F2}, spread {max - min:F3} J, drift {drift:F3} J vs swing {swing:F1} J");
        Assert.True(max - min < 0.08 * swing, $"energy spread {max - min} J");
        Assert.True(drift < 0.01 * swing, $"energy drift {drift} J");
    }

    [Fact]
    public void DampingAndTyresOnlyRemoveEnergy()
    {
        var car = new Vehicle(TestCars.Simple(), new FlatGround());
        car.Position += new Vec3(0, 0.03, 0);
        car.SyncContacts();
        var e0 = car.MechanicalEnergy();
        var prev = e0;
        for (var i = 0; i < 120 * 3; i++)
        {
            car.Step(new DriverInput { Gear = 0, Clutch = 0 });
            var e = car.MechanicalEnergy();
            Assert.True(e <= prev + 1e-6 * Math.Abs(e0) + 1.0, $"energy rose at step {i}: {prev} -> {e}");
            prev = e;
        }

        Assert.True(prev < e0);
    }

    [Fact]
    public void SameInputsGiveBitIdenticalRuns()
    {
        static (Vec3, Quat, Vec3, double) Run()
        {
            var car = new Vehicle(TestCars.Simple(), new FlatGround());
            for (var i = 0; i < 120 * 8; i++)
            {
                var t = i / 120.0;
                car.Step(new DriverInput
                {
                    Throttle = t < 5 ? 1 : 0,
                    Brake = t >= 5 ? 0.6 : 0,
                    Steer = Math.Sin(t),
                    Gear = 1,
                    Clutch = Math.Clamp((car.EngineRpm - 1200) / 1200, 0, 1),
                });
            }

            return (car.Position, car.Orientation, car.Velocity, car.EngineSpeed);
        }

        var a = Run();
        var b = Run();
        Assert.Equal(a, b); // record struct equality on doubles: bit-identical (no NaN involved)
        Assert.True(a.Item1.Length > 10);
    }

    [Fact]
    public void CarSettlesAtTheStaticRideHeight()
    {
        var car = new Vehicle(TestCars.Simple(), new FlatGround());
        var y0 = car.Position.Y;
        for (var i = 0; i < 120 * 3; i++)
        {
            car.Step(new DriverInput { Gear = 0, Clutch = 0 });
        }

        Assert.InRange(car.Position.Y - y0, -0.01, 0.01);
        Assert.InRange(car.Velocity.Length, 0, 0.01);
        var total = car.Wheels.Sum(w => w.Load);
        Assert.InRange(total, 0.98 * car.Spec.Mass * car.Spec.Gravity, 1.02 * car.Spec.Mass * car.Spec.Gravity);
    }

    [Fact]
    public void MoreTorqueAccelerateFasterAndMoreMassSlower()
    {
        var baseline = Scenarios.ZeroTo100(TestCars.Simple()).Value;
        var stronger = Scenarios.ZeroTo100(TestCars.Simple(torqueScale: 1.5)).Value;
        var heavier = Scenarios.ZeroTo100(TestCars.Simple(mass: 1600)).Value;
        output.WriteLine($"0-100: base {baseline:F2} s, 1.5x torque {stronger:F2} s, 1600 kg {heavier:F2} s");
        Assert.True(stronger < baseline);
        Assert.True(heavier > baseline);
    }

    [Fact]
    public void StrongerBrakesStopShorterUntilTheTyresLimit()
    {
        var d = new[] { 300.0, 600, 1200, 2400 }.Select(t => Scenarios.Braking100(TestCars.Simple(brakeTorque: t)).Value).ToArray();
        output.WriteLine("100-0 m: " + string.Join(", ", d.Select(x => x.ToString("F1", System.Globalization.CultureInfo.InvariantCulture))));
        for (var i = 1; i < d.Length; i++)
        {
            Assert.True(d[i] <= d[i - 1] + 0.5, $"brake {i}: {d[i]} > {d[i - 1]}");
        }

        var mu = 1.9 * Math.Sin(0.45 * Math.PI / 2); // D * sin(C pi/2): sliding grip of the test tyre
        Assert.True(d[^1] > 0.9 * Math.Pow(100 / 3.6, 2) / (2 * mu * 9.8067), "stopped faster than the tyre allows");
    }

    [Fact]
    public void ScenariosProduceSaneNumbersForTheTestCar()
    {
        var spec = TestCars.Simple();
        var r = Scenarios.All(spec);
        foreach (var x in r)
        {
            output.WriteLine($"{x.Name}: {x.Value:F2} {x.Unit} {x.Detail}");
        }

        Assert.InRange(r[0].Value, 5, 30); // 0-100 s
        Assert.InRange(r[1].Value, 120, 260); // km/h
        Assert.InRange(r[2].Value, 20, 80); // m
        Assert.InRange(r[3].Value, 0.5, 2.0); // g
    }
}
