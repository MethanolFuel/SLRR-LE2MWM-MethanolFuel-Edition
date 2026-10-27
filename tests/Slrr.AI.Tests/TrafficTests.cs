using System.Numerics;
using Slrr.AI.Roads;
using Slrr.AI.Traffic;
using Slrr.Game.Physics;
using Slrr.Physics;
using Xunit;
using Xunit.Abstractions;

namespace Slrr.AI.Tests;

public sealed class TrafficTests(ITestOutputHelper output)
{
    [Fact]
    public void FollowerKeepsItsGapBehindASlowCar()
    {
        var scene = new PhysicsScene();
        scene.SetGround(new FlatGround(), null);
        var lane = SyntheticRoads.RingLanes(Vector3.Zero, 1)[0];
        var roads = new PathSetRoadSource().AddLane(lane);
        var world = new FakeWorld(scene);
        var traffic = new AiTrafficDirector(scene, roads, world, new TrafficSettings { CruiseSpeed = 14, SpeedSpread = 0 });
        SimpleCar Put(int id, float s)
        {
            var c = scene.AddSimpleCar(id, $"t{id}", new SimpleCarSpec());
            var p = lane.Sample(s);
            c.Place(new Vec3(p.Position.X, 0.5, p.Position.Z), Conv.FromYpr(RoadMath.YawOf(p.Tangent), 0, 0));
            return c;
        }

        var slow = Put(1, 60);
        var fast = Put(2, 0);
        var slowId = traffic.Add(7, slow.Id, null, slow: true);
        var fastId = traffic.Add(7, fast.Id, null);
        Assert.Equal(["car_add 1 1", "car_add 2 2"], world.Events.Select(e => e.Text));
        Assert.All(world.Events, e => Assert.Equal(7, e.Target));

        var minGap = double.MaxValue;
        while (scene.Time < 60)
        {
            scene.Advance(1.0 / 60);
            traffic.Step(1f / 60);
            minGap = Math.Min(minGap, (slow.Position - fast.Position).Length - 4.3);
        }

        output.WriteLine($"slow {slow.Speed:F1} m/s, fast {fast.Speed:F1} m/s, min gap {minGap:F1} m, impacts {scene.AllImpacts.Count}");
        Assert.Empty(scene.AllImpacts);
        Assert.True(minGap > 3, $"gap {minGap:F1}");
        Assert.InRange(fast.Speed, slow.Speed - 1.5, slow.Speed + 1.5); // following, not overtaking (traffic)
        Assert.InRange(slow.Speed, 5, 8); // AI_GoToTrafficSlow: half the cruise speed

        traffic.Remove(slowId);
        traffic.Remove(fastId);
        Assert.Equal("car_rem 1 1", world.Events[2].Text);
        Assert.Null(slow.Input);
        Assert.NotNull(scene.Car(1)); // a handed-over car stays in the scene (only spawned cars are removed)
    }

    [Fact]
    public void PopulationSpawnsAroundTheFocusAndRemovesFarCars()
    {
        var scene = new PhysicsScene();
        scene.SetGround(new FlatGround(), null);
        var roads = new PathSetRoadSource();
        foreach (var l in SyntheticRoads.RingLanes(Vector3.Zero))
        {
            roads.AddLane(l);
        }

        var focus = new Vector3(0, 0, -300);
        var traffic = new AiTrafficDirector(scene, roads, null, new TrafficSettings { MaxCars = 20 }) { Focus = () => focus };
        for (var i = 0; i < 60 * 20; i++)
        {
            scene.Advance(1.0 / 60);
            traffic.Step(1f / 60);
        }

        var near = traffic.Entries.Count;
        Assert.Equal(20, near);
        Assert.All(traffic.Entries, e => Assert.True(RoadMath.Horizontal(e.Car.Position.ToVector3() - focus) < 400));
        focus = new Vector3(5000, 0, 5000);
        for (var i = 0; i < 60; i++)
        {
            scene.Advance(1.0 / 60);
            traffic.Step(1f / 60);
        }

        output.WriteLine($"spawned {traffic.Spawned}, despawned {traffic.Despawned}, impacts {scene.AllImpacts.Count}");
        Assert.Empty(traffic.Entries);
        Assert.Empty(scene.AllCars);
        Assert.Equal(traffic.Spawned, traffic.Despawned);
    }
}
