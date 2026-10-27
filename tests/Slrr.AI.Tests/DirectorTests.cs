using System.Globalization;
using System.Numerics;
using Slrr.AI.Driving;
using Slrr.AI.Race;
using Slrr.AI.Roads;
using Slrr.Engine.Commands;
using Slrr.Game.Physics;
using Slrr.Physics;
using Xunit;
using Xunit.Abstractions;

namespace Slrr.AI.Tests;

/// <summary>The brain verbs as the shipped scripts send them, driving physics cars through <see cref="AiRaceDirector"/>.</summary>
public sealed class DirectorTests(ITestOutputHelper output)
{
    private const int Brain = 50;
    private const int CarId = 100;
    private const int Opponent = 101;

    private static (PhysicsScene Scene, FakeWorld World, AiRaceDirector Director, ObjectInfo Brain, FullCar Car, SampledPath Circuit) Setup()
    {
        var scene = new PhysicsScene();
        scene.SetGround(new FlatGround(), null);
        var roads = SyntheticRoads.World(out var circuit);
        var world = new FakeWorld(scene);
        var inner = new RaceDirector(world, world, world, world);
        var director = new AiRaceDirector(inner, scene, roads);
        var brain = world.Add(Brain, "bot");
        world.Add(CarId, "car");
        var car = scene.AddCar(CarId, "bot car", SyntheticCars.Default());
        var start = circuit.Sample(0);
        car.Place(new Vec3(start.Position.X, 0.5, start.Position.Z), Conv.FromYpr(RoadMath.YawOf(start.Tangent), 0, 0));
        return (scene, world, director, brain, car, circuit);
    }

    private static void Send(AiRaceDirector d, ObjectInfo brain, string text) =>
        d.Execute(brain, ParsedCommand.Parse(text, CommandEngine.GroupOf(brain, ParsedCommand.VerbOf(text)) ?? VerbGroup.Brain));

    [Fact]
    public void BotEnterCarThenBeginRaceThenRaceDrivesTheRouteToTheFinish()
    {
        var (scene, world, d, brain, car, circuit) = Setup();
        // Bot.enterCar: AI_level, controllable, AI_suspend (MEASURED order)
        Send(d, brain, "AI_level 1.0");
        Send(d, brain, $"controllable {CarId}");
        Send(d, brain, "AI_suspend");
        Assert.False(d.Owns(CarId)); // suspended: the commands layer's director keeps it (no input)

        // City.startRace: car held, AI_BeginRace 0.5; at "1": AI_BeginRace 1.0
        Send(d, brain, "AI_BeginRace 0.5");
        var b = d.Bindings[Brain];
        Assert.Equal(DriverMode.Launch, b.Driver.Mode);
        Assert.Equal(0.5, b.Driver.LaunchThrottle);
        Send(d, brain, "AI_BeginRace 7");
        Assert.Equal(1.0, b.Driver.LaunchThrottle); // MEASURED clamp to [0, 1]
        Assert.Same(b.Driver, car.Input);

        // GO: start + AI_race finish opponent (Bot.startRace)
        var finish = circuit.Sample(600).Position;
        car.State = CarRunState.Driving;
        Send(d, brain, string.Create(CultureInfo.InvariantCulture, $"AI_race {finish.X},{finish.Y},{finish.Z} {Opponent}"));
        Assert.True(b.Routed, b.Note);
        Assert.Equal(DriverMode.Drive, b.Driver.Mode);
        Assert.Equal(AiLevel.Stored(1.0), b.Driver.Settings.Level);

        // the gate drops the commands layer's straight-line inputs for the car this director drives
        var gate = d.Gate(world);
        gate.SetInput(CarId, DriveInput.FullBrake);
        gate.SetInput(999, DriveInput.FullBrake);
        Assert.Single(world.Inputs);

        var maxLat = 0.0;
        double? arrived = null;
        while (scene.Time < 90 && arrived is null)
        {
            scene.Advance(1.0 / 60);
            d.Step(1f / 60);
            maxLat = Math.Max(maxLat, Math.Abs(b.Driver.LateralError));
            if (Vector3.Distance(car.Position.ToVector3() with { Y = 0 }, finish with { Y = 0 }) < 8)
            {
                arrived = scene.Time;
            }
        }

        output.WriteLine($"{b.Note}; arrived after {arrived:F2} s, max |lateral| {maxLat:F2} m");
        Assert.NotNull(arrived);
        Assert.True(maxLat < 7, $"left the road: {maxLat:F2} m");

        // City.event_handlerRaceFinish: raceBot.stop() = AI_stop → full brake (MEASURED mode 0 + brake 127)
        Send(d, brain, "AI_stop");
        Assert.Equal(DriverMode.Hold, b.Driver.Mode);
        var t0 = scene.Time;
        while (scene.Time < t0 + 8)
        {
            scene.Advance(1.0 / 60);
        }

        Assert.True(car.Speed < 0.5, $"still {car.Speed:F2} m/s");

        // AI_follow goes back to the commands layer's director
        Send(d, brain, $"AI_follow 0,0,-2 {Opponent}");
        Assert.False(d.Owns(CarId));
        Assert.Null(car.Input);
    }

    [Fact]
    public void AiSplineLoadsTheFileCaseInsensitivelyAndFollowsIt()
    {
        var (scene, _, d, brain, car, circuit) = Setup();
        var dir = Path.Combine(Path.GetTempPath(), "slrr-ai-spl-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(dir, "cars", "racers", "ROC_cars"));
        try
        {
            // a spline along the first 500 m of the circuit in the shipped 12-column layout
            var lines = new List<string>();
            for (var s = 0f; s <= 500; s += 25)
            {
                var p = circuit.Sample(s);
                lines.Add(string.Create(CultureInfo.InvariantCulture,
                    $"{p.Position.X:F6} {p.Position.Y:F6} {p.Position.Z:F6} {p.Tangent.X:F6} {p.Tangent.Y:F6} {p.Tangent.Z:F6}\t7.0\t100.00\t0\t10000.0\t10000.0\t0"));
            }

            File.WriteAllLines(Path.Combine(dir, "cars", "racers", "ROC_cars", "ROC_SD.spl"), lines);
            d.ResolveFile = GameFiles.Resolver(dir);

            // ROCTrack: AI_params from Roc.aiParams, then at GO start + followSplineTrack(0.5, name, player id)
            Send(d, brain, $"controllable {CarId}");
            Send(d, brain, "AI_params  8.1 0.36");
            car.State = CarRunState.Driving;
            Send(d, brain, $"AI_spline 0.5 cars/racers/ROC_cars/roc_SD.spl {Opponent}");
            var b = d.Bindings[Brain];
            Assert.NotNull(b.Driver.Path);
            Assert.Contains("AI_params 8.1 0.36", b.Driver.Caps.Source);
            Assert.Equal(0.36 * VehicleCaps.G, b.Driver.Caps.BrakeDecel, 3); // AI_params b → braking limit in g (GUESSED role)
            Assert.True(b.Driver.Caps.LateralAccel <= 8.1);
            while (scene.Time < 80 && b.Driver.Progress < 480)
            {
                scene.Advance(1.0 / 60);
                d.Step(1f / 60);
            }

            output.WriteLine($"{b.Note}: progress {b.Driver.Progress:F0} m after {scene.Time:F1} s");
            Assert.True(b.Driver.Progress >= 480);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void NightRaceResetsTheLevelToTheExeDefault()
    {
        var (_, _, d, brain, _, circuit) = Setup();
        Send(d, brain, $"controllable {CarId}");
        Send(d, brain, "AI_level 0.0");
        var f = circuit.Sample(300).Position;
        Send(d, brain, string.Create(CultureInfo.InvariantCulture, $"AI_race {f.X},{f.Y},{f.Z} {Opponent}"));
        Assert.Equal(0.25, d.Bindings[Brain].Driver.Settings.Level);
        Send(d, brain, "AI_NightRace");
        Assert.Equal(AiLevel.Default, d.Bindings[Brain].Driver.Settings.Level);
    }
}
