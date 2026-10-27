using System.Numerics;
using Slrr.Engine.Parts;
using Slrr.Physics;
using Xunit;

namespace Slrr.Game.Physics.Tests;

public class SceneTests
{
    [Theory]
    [InlineData(0.0, 0.0, 0.0)]
    [InlineData(2.88, 0.05, 0.0)]
    [InlineData(-1.2, 0.3, -0.4)]
    [InlineData(3.0, -0.7, 1.1)]
    public void YprRoundTripsThroughTheSystemNumericsConvention(double yaw, double pitch, double roll)
    {
        var q = Conv.FromYpr(yaw, pitch, roll);
        var m = Matrix4x4.CreateFromYawPitchRoll((float)yaw, (float)pitch, (float)roll);
        var v = new Vector3(0.3f, -0.2f, 1f);
        var a = Vector3.Transform(v, m);
        var b = q.Rotate(v.ToVec3()).ToVector3();
        Assert.True(Vector3.Distance(a, b) < 1e-5f, $"{a} vs {b}");
        var ypr = Conv.ToYpr(q);
        Assert.Equal(yaw, ypr.Yaw, 4);
        Assert.Equal(pitch, ypr.Pitch, 4);
        Assert.Equal(roll, ypr.Roll, 4);
    }

    [Fact]
    public void AccumulatorRunsFixedStepsAndKeepsTheRemainder()
    {
        var scene = TestWorlds.Scene(TestWorlds.Build());
        Assert.Equal(4, scene.Advance(1.0 / 30));
        Assert.Equal(0, scene.Advance(0.005));
        Assert.Equal(1, scene.Advance(0.005)); // 0.010 >= 1/120
        Assert.InRange(scene.Alpha, 0.19, 0.21);
        scene.MaxStepsPerFrame = 3;
        Assert.Equal(3, scene.Advance(1.0));
        Assert.True(scene.DroppedTime > 0.9);
        Assert.Equal(8, scene.Steps);
    }

    private static (PhysicsScene Scene, FullCar Car) Drive(ScriptedInput input, double seconds, float? wall = null)
    {
        var scene = TestWorlds.Scene(TestWorlds.Build(wall));
        var car = scene.AddCar(7, "test", TestWorlds.Car());
        car.Place(Vec3.Zero, Quat.Identity);
        car.Input = input;
        car.State = CarRunState.Driving;
        while (scene.Time < seconds - 1e-9)
        {
            scene.Advance(1.0 / 60);
        }

        return (scene, car);
    }

    [Fact]
    public void FlatGroundDriveIsDeterministicAndSane()
    {
        static ScriptedInput Script() => new ScriptedInput().At(0).At(0.5, throttle: 1).At(4, throttle: 1, steer: 0.1).At(6, brake: 1);
        var (scene, car) = Drive(Script(), 8);
        var (_, again) = Drive(Script(), 8);
        Assert.Equal(car.Position, again.Position);
        Assert.Equal(car.Velocity, again.Velocity);
        Assert.Equal(car.Orientation, again.Orientation);

        var t = car.Telemetry!;
        Assert.True(t.Wheels.All(w => w.Grounded));
        Assert.True(car.Odometer > 40, $"odometer {car.Odometer}");
        Assert.True(car.Origin.Z < -40, "drove forward (-Z)");
        Assert.True(car.Drivetrain.Shifts >= 2, $"shifts {car.Drivetrain.Shifts}");
        Assert.InRange(t.GVertical, 0.7f, 1.3f);
        Assert.Empty(scene.AllImpacts);
    }

    [Fact]
    public void ChassisBoxStopsAtAWallAndReportsTheImpact()
    {
        var (scene, car) = Drive(new ScriptedInput().At(0, throttle: 1), 10, wall: -60);
        Span<Vec3> corners = stackalloc Vec3[8];
        car.BoxCorners(corners);
        foreach (var c in corners)
        {
            Assert.True(c.Z > -60.1, $"corner {c} went through the wall");
        }

        Assert.NotEmpty(scene.AllImpacts);
        var hit = scene.AllImpacts[0];
        Assert.Equal(7, hit.CarId);
        Assert.True(hit.Normal.Z > 0.9f, $"normal {hit.Normal}");
        Assert.Equal(1, hit.Surface);
        Assert.Equal(2, hit.Source);
        Assert.True(hit.DeltaV > 5, $"dv {hit.DeltaV}");
        Assert.True(car.ImpactTotal >= hit.DeltaV);
    }

    [Fact]
    public void BoxTriangleSeparatingAxis()
    {
        Span<Vec3> axes = [Vec3.UnitX, Vec3.UnitY, Vec3.UnitZ];
        var half = new Vec3(1, 0.5, 2);
        // box resting 2 cm into a big floor triangle
        Assert.True(CollisionSolver.BoxTriangle(new Vec3(0, 0.48, 0), axes, half, new Vec3(-10, 0, -10), new Vec3(-10, 0, 10), new Vec3(10, 0, 0), out var n, out var d));
        Assert.Equal(0.02, d, 6);
        Assert.Equal(1.0, n.Y, 6);
        // 1 cm above: separated
        Assert.False(CollisionSolver.BoxTriangle(new Vec3(0, 0.51, 0), axes, half, new Vec3(-10, 0, -10), new Vec3(-10, 0, 10), new Vec3(10, 0, 0), out _, out _));
    }

    [Fact]
    public void ManualBoxShiftsOnGearAxisEdges()
    {
        var input = new ScriptedInput().At(0).At(0.2, gear: 1).At(0.3).At(0.4, gear: 1).At(0.5).At(1.0, gear: -1).At(1.1);
        var scene = TestWorlds.Scene(TestWorlds.Build());
        var car = scene.AddCar(7, "test", TestWorlds.Car());
        car.Place(Vec3.Zero, Quat.Identity);
        car.Drivetrain.Mode = TransmissionMode.Manual;
        car.Input = input;
        car.State = CarRunState.Driving;
        var gears = new List<int>();
        scene.Stepped += _ => gears.Add(car.Drivetrain.Gear);
        while (scene.Time < 1.5)
        {
            scene.Advance(1.0 / 60);
        }

        Assert.Equal(2, gears.Max());
        Assert.Equal(1, gears[^1]);
    }

    [Fact]
    public void CommandVerbsChangeTheRunState()
    {
        var scene = TestWorlds.Scene(TestWorlds.Build());
        var car = scene.AddCar(3, "test", TestWorlds.Car());
        car.Place(new Vec3(0, 5, 0), Conv.FromYpr(1.0, 0.2, 0.1));
        Assert.Equal(CarRunState.Held, car.State);
        Assert.InRange(car.Origin.Y, 0.4, 0.8); // snapped: wheels (cfg y -0.3, radius 0.3) on the ground

        GamePhysicsWorld.Execute(car, "start", ["start"]);
        Assert.Equal(CarRunState.Driving, car.State);
        GamePhysicsWorld.Execute(car, "setsteer", ["setsteer", "-0.6"]);
        Assert.Equal(-0.6, car.ScriptSteer, 5);
        GamePhysicsWorld.Execute(car, "transmission", ["transmission", "2"]);
        Assert.Equal(TransmissionMode.ManualClutch, car.Drivetrain.Mode);
        GamePhysicsWorld.Execute(car, "brake", ["brake"]);
        car.ReadControls();
        Assert.Equal(1, car.Controls.Brake);
        GamePhysicsWorld.Execute(car, "stop", ["stop"]);
        Assert.False(car.EngineRunning);
        Assert.Equal(CarRunState.Held, car.State);
        GamePhysicsWorld.Execute(car, "reset", ["reset"]);
        var ypr = Conv.ToYpr(car.Orientation);
        Assert.Equal(1.0, ypr.Yaw, 4);
        Assert.Equal(0.0, ypr.Pitch, 4);
        Assert.Equal(0.0, ypr.Roll, 4);
        GamePhysicsWorld.Execute(car, "setvel", ["setvel", "1,2,3,0,0,0"]);
        Assert.Equal(new Vec3(1, 2, 3), car.Velocity);
        Assert.Equal("1,2,3,0,0,0", car.Settings["setvel"]);
    }

    [Fact]
    public void HeldCarsDoNotMoveAndIdle()
    {
        var scene = TestWorlds.Scene(TestWorlds.Build());
        var car = scene.AddCar(3, "test", TestWorlds.Car());
        car.Place(new Vec3(10, 0, 10), Quat.Identity);
        var before = car.Position;
        while (scene.Time < 1)
        {
            scene.Advance(1.0 / 30);
        }

        Assert.Equal(before, car.Position);
        Assert.InRange(car.Telemetry!.Rpm, 799, 801);
        Assert.False(car.Telemetry.Simulated);
    }

    [Fact]
    public void SimpleCarFollowsARampAndCarsCollide()
    {
        var scene = TestWorlds.Scene(TestWorlds.Build(ramp: true));
        var a = scene.AddSimpleCar(1, "a", new SimpleCarSpec());
        a.Place(new Vec3(0, 0, 0), Quat.Identity);
        a.Input = new ScriptedInput().At(0, throttle: 1).At(4, brake: 1);
        a.State = CarRunState.Driving;
        var maxY = 0.0;
        scene.Stepped += _ => maxY = Math.Max(maxY, a.Origin.Y);
        while (scene.Time < 6)
        {
            scene.Advance(1.0 / 60);
        }

        Assert.True(a.Origin.Z < -20, $"z {a.Origin.Z}");
        Assert.True(maxY > 0.5, $"climbed the ramp: max y {maxY}");
        Assert.True(Conv.ToYpr(a.Orientation).Pitch is var p && Math.Abs(p) < 0.5);

        // head-on: two simple cars driving at each other
        var s2 = TestWorlds.Scene(TestWorlds.Build());
        var c1 = s2.AddSimpleCar(1, "c1", new SimpleCarSpec());
        var c2 = s2.AddSimpleCar(2, "c2", new SimpleCarSpec());
        c1.Place(new Vec3(0, 0, 0), Quat.Identity);              // forward -Z
        c2.Place(new Vec3(0, 0, -30), Conv.FromYpr(Math.PI, 0, 0)); // forward +Z
        foreach (var c in new[] { c1, c2 })
        {
            c.Input = new ScriptedInput().At(0, throttle: 1);
            c.State = CarRunState.Driving;
        }

        while (s2.Time < 4)
        {
            s2.Advance(1.0 / 60);
        }

        Assert.Contains(s2.AllImpacts, i => i.CarId == 1 && i.Other == 2);
        Assert.True(c1.Origin.Z > c2.Origin.Z, "the cars did not pass through each other");
    }

    [Fact]
    public void TelemetryCellsAreWrittenForChassisGetIndexedData()
    {
        var scene = TestWorlds.Scene(TestWorlds.Build());
        var car = scene.AddCar(3, "test", TestWorlds.Car());
        car.Place(Vec3.Zero, Quat.Identity);
        scene.Advance(1.0 / 60);
        var cells = new ChassisTelemetry();
        GamePhysicsWorld.WriteTelemetry(cells, car.Telemetry! with { Gear = 3, Throttle = 0.5f }, 0.5f);
        GamePhysicsWorld.WriteTelemetry(cells, car.Telemetry! with { Gear = 3, Throttle = 0.5f }, 0.5f);
        Assert.Equal(3, cells.GetInt(ChassisTelemetry.GearNumber));
        Assert.Equal(0.5f, cells.GetFloat(ChassisTelemetry.ThrottleStrength));
        Assert.Equal(car.Telemetry!.Rpm, cells.GetFloat(ChassisTelemetry.Rpm));
        Assert.Equal(1f, cells.GetFloat(ChassisTelemetry.CarRuntime));
    }
}
