using System.Numerics;
using Slrr.Physics;
using Slrr.Physics.Dyno;
using Slrr.World;

namespace Slrr.Game.Physics.Tests;

/// <summary>Synthetic cars and collision worlds for tests that run without the game data.</summary>
internal static class TestWorlds
{
    /// <summary>A 1200 kg rear-drive car with the default DynoData engine (same numbers as Slrr.Physics' test car).</summary>
    public static VehicleSpec Car(double mass = 1200)
    {
        var model = ExeDynoCalculator.Build(DynoDataDefaults.Create(), 28);
        var spec = new VehicleSpec
        {
            Name = "test",
            Mass = mass,
            CenterOfMass = new Vec3(0, 0.1, -0.1),
            Inertia = new Vec3(mass / 12 * (1.3 * 1.3 + 4.2 * 4.2), mass / 12 * (1.75 * 1.75 + 4.2 * 4.2), mass / 12 * (1.75 * 1.75 + 1.3 * 1.3)),
            Engine = new EngineSpec
            {
                Torque = model.TorqueTables[0]!.Select(t => (float)t).ToArray(),
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
                BrakeTorque = front ? 2400 : 1600,
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

    /// <summary>
    /// Flat ground y = 0 over ±500 m (surface 0), an optional wall across the road at z = <paramref name="wallZ"/>
    /// (3 m high, facing +Z, surface 1) and an optional ramp rising 1 m per 10 m in -Z from z = -20 (surface 0).
    /// </summary>
    public static CollisionWorld Build(float? wallZ = null, bool ramp = false)
    {
        var soup = new TriangleSoupBuilder();
        void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, int surface, int source)
        {
            var n = Vector3.Normalize(Vector3.Cross(b - a, c - a));
            soup.Add(a, b, c, n, surface, source);
            soup.Add(a, c, d, n, surface, source);
        }

        // ground (counter-clockwise seen from above => normal +Y)
        Quad(new(-500, 0, -500), new(-500, 0, 500), new(500, 0, 500), new(500, 0, -500), 0, 1);
        if (wallZ is { } z)
        {
            Quad(new(-50, 0, z), new(50, 0, z), new(50, 3, z), new(-50, 3, z), 1, 2);
        }

        if (ramp)
        {
            Quad(new(-5, 0, -20), new(5, 0, -20), new(5, 3, -50), new(-5, 3, -50), 0, 3);
        }

        return new CollisionWorld(soup, SurfaceTable.Anonymous(2));
    }

    public static PhysicsScene Scene(CollisionWorld world)
    {
        var scene = new PhysicsScene();
        scene.SetGround(new WorldGround(world), new CollisionWorldShapes(world));
        return scene;
    }
}
