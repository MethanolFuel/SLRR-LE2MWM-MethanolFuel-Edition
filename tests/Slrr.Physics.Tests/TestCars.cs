using Slrr.Physics.Dyno;

namespace Slrr.Physics.Tests;

/// <summary>Synthetic cars for tests that must run without the game data.</summary>
internal static class TestCars
{
    /// <summary>A 1200 kg rear-drive car with the default DynoData engine (1.9 L) and stock-like tyre numbers.</summary>
    public static VehicleSpec Simple(double torqueScale = 1.0, double brakeTorque = 2000, double mass = 1200)
    {
        var model = ExeDynoCalculator.Build(DynoDataDefaults.Create(), 28);
        var spec = new VehicleSpec
        {
            Name = "test",
            Mass = mass,
            CenterOfMass = new Vec3(0, 0.1, -0.1),
            Inertia = new Vec3(mass / 12 * (1.3 * 1.3 + 4.2 * 4.2), mass / 12 * (1.75 * 1.75 + 4.2 * 4.2), mass / 12 * (1.75 * 1.75 + 1.3 * 1.3)),
            DragCoefficient = 0.32,
            FrontalArea = 1.9,
            Engine = new EngineSpec
            {
                Torque = model.TorqueTables[0]!.Select(t => (float)(t * torqueScale)).ToArray(),
                TableStep = model.TableStep,
                IdleRpm = 800,
                RpmLimit = 6750,
                MaxRpm = 7000,
                Inertia = 0.15,
            },
            Drivetrain = new DrivetrainSpec { Ratios = [0, 3.6, 2.2, 1.5, 1.15, 0.92, 0, -3.5], Gears = 5, FinalDrive = 3.9, ClutchCapacity = 500 },
        };
        foreach (var (name, x, z) in new[] { ("FL", -0.75, -1.3), ("FR", 0.75, -1.3), ("RL", -0.75, 1.2), ("RR", 0.75, 1.2) })
        {
            var front = z < 0;
            var tyre = new TyreParams { RollResistance = 0.01, Bearing = 20 };
            double[] p = [0.45, 0, 1.9, 0, 25, 0, 0, 0, -2, 0, 0, 30000, 1.4, 0.01, -0.5, 0, 0];
            p.CopyTo(tyre.P, 0);
            spec.Wheels.Add(new WheelSpec
            {
                Name = name,
                Position = new Vec3(x, -0.3, z),
                Radius = 0.3,
                Mass = 10,
                Steers = front,
                MaxSteer = 0.6,
                Drive = front ? 0 : 0.5,
                BrakeTorque = brakeTorque * (front ? 1.2 : 0.8),
                SpringRate = 40000,
                RestLength = 0.35,
                MinLength = 0.15,
                MaxLength = 0.35,
                DampingBump = 3000,
                DampingRebound = 2100,
                Tyre = tyre,
            });
        }

        spec.Rollbars.Add(new RollbarSpec(0, 1, 20000, 1000));
        return spec;
    }
}
