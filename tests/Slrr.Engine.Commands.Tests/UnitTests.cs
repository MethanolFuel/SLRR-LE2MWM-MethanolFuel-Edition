using System.Numerics;
using Xunit;

namespace Slrr.Engine.Commands.Tests;

public class CatalogTests
{
    [Fact]
    public void ScriptSiteCensusAddsUpToTheMeasuredTotal()
    {
        Assert.Equal(VerbCatalog.TotalVerbSites, VerbCatalog.Verbs.Sum(v => v.ScriptSites));
        var (impl, rec, other, total) = VerbCatalog.Coverage();
        Assert.Equal(total, impl + rec + other);
        Assert.Equal(526, total);
        Assert.Equal(VerbCatalog.Verbs.Count, VerbCatalog.Verbs.Select(v => v.Verb).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void EveryFormatUsesOnlyTheScanfSubsetOfTheExe()
    {
        foreach (var v in VerbCatalog.Verbs.Where(v => v.Format is not null))
        {
            var values = new List<object>();
            Slrr.Engine.Resources.CScanf.Scan(v.Verb + " 1", v.Format!, values);
            Assert.StartsWith("%*s", v.Format, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("look -64835 0,0,0 0,0,0 0.2", VerbGroup.Camera, 8)]
    [InlineData("look 3604481 1.5,2,3", VerbGroup.Camera, 4)]
    [InlineData("move -64835 0,1.5,-6.5 0.001 0,-0.2, 0", VerbGroup.Camera, 8)]
    [InlineData("move -64835 0,0,0 3.5", VerbGroup.Camera, 5)]
    [InlineData("render 0 0 0 1 3", VerbGroup.Camera, 5)]
    [InlineData("render 12 -65422 2", VerbGroup.Car, 3)]
    [InlineData("AI_follow 0,0,0.001 -64835", VerbGroup.Brain, 4)]
    [InlineData("AI_race 0.0,0.0,148.21 -64835", VerbGroup.Brain, 4)]
    [InlineData("AI_GoToTarget 1,2,3", VerbGroup.Brain, 3)]
    [InlineData("AI_spline 0.5 track1 2", VerbGroup.Brain, 3)]
    [InlineData("angle 0 4.0 0.7853", VerbGroup.Camera, 3)]
    [InlineData("autozoom 1 0 0.6 0.1", VerbGroup.Camera, 4)]
    [InlineData("move 0, 0.2", VerbGroup.Cursor, 2)]
    [InlineData("mode 0 J 65596", VerbGroup.Cursor, 3)]
    [InlineData("fog 1 0.5 100", VerbGroup.Ground, 1)]
    public void CommandsFromTheScriptsParseWithTheExeFormats(string text, VerbGroup group, int fields)
    {
        var c = ParsedCommand.Parse(text, group);
        Assert.Equal(fields, c.Fields);
    }

    [Fact]
    public void LookParsesTargetAndOffsets()
    {
        var c = ParsedCommand.Parse("look -64835 0,0,-1 -0.3,0,-1", VerbGroup.Camera);
        Assert.Equal(-64835, c.Int(0));
        Assert.Equal(new Vector3(0, 0, -1), c.Vec(1));
        Assert.Equal(new Vector3(-0.3f, 0, -1), c.Vec(4));
    }
}

public class EngineTests
{
    private const int Map = 100;
    private const int Player = 1;
    private const int Car = 10;
    private const int Car2 = 11;
    private const int Cam = 20;
    private const int Brain = 30;
    private const int Trig = 40;

    private static (FakeWorld W, CommandEngine E) Setup()
    {
        var w = new FakeWorld();
        w.Add(Map, null, "map", parent: 0);
        w.Add(Player, "player", "controller");
        w.Add(Car, "car", "Vehicle", "0,0,0,0,0,0", new Vector3(0, 0, 0), MathF.PI, Map);
        w.Add(Cam, "camera", "external track cam", "0,2,10,0,0,0,0x02", new Vector3(0, 2, 10), 0, Map);
        return (w, new CommandEngine(w, w));
    }

    private static void Run(CommandEngine e, float seconds, float dt = 1f / 30f)
    {
        for (var t = 0f; t < seconds; t += dt)
        {
            e.Step(dt);
        }
    }

    [Fact]
    public void ControllerBindsCarAndAnswersOwner()
    {
        var (_, e) = Setup();
        Assert.True(e.Execute(Player, "controllable 10").Consumed);
        Assert.Equal(Player, e.Race.OwnerOf(Car));
        Assert.Equal(Player, e.AnswerInfo(Car, 24));
        Assert.Equal(Car, e.AnswerInfo(Car, 1));
        Assert.Equal(5, e.AnswerInfo(Car, 7));
        Assert.True(e.Execute(Player, "leave 10").Consumed);
        Assert.Equal(0, e.Race.OwnerOf(Car));
        Assert.Null(e.AnswerInfo(Car, 24));
    }

    [Fact]
    public void PartStructureVerbsAndUnknownKindsAreNotConsumed()
    {
        var (w, e) = Setup();
        w.Add(50, "part", "rim");
        Assert.False(e.Execute(Car, "install 0 10 0 0 0").Consumed);
        Assert.False(e.Execute(50, "start").Consumed);
        Assert.False(e.Execute(999, "start").Consumed);
        Assert.True(e.Execute(Map, "obs_add 10").Consumed);
        Assert.Contains(Car, e.Scene.Observers(Map));
    }

    [Fact]
    public void FollowCameraSitsBehindTheControlledCarInsideDist()
    {
        var (w, e) = Setup();
        e.Execute(Player, "controllable 10");
        foreach (var c in new[] { "dist 2.0 10.0", "smooth 0.5 0.5", "force 1.6 0.5 -0.7", "torque 0.1", "roll 0", "render 0 0 0 1 3" })
        {
            Assert.True(e.Execute(Cam, c).Consumed, c);
        }

        e.Execute(Player, "viewport 0");
        e.Execute(Player, "camera 20");
        e.Driver.SetInput(Car, new DriveInput(1, 0, 0));
        Run(e, 5);
        var car = w.PoseOf(Car);
        var cam = e.Cameras.Get(Cam)!;
        var d = Vector3.Distance(cam.Position, car.Position + new Vector3(0, 0.5f, 0));
        Assert.InRange(d, 2f - 1e-3f, 10f + 1e-3f);
        Assert.True(car.Position.Z > 10, "yaw pi drives towards +Z");
        Assert.True(cam.Position.Z < car.Position.Z, "camera trails the car");
        Assert.InRange(Ypr.AngleDiff(cam.Orientation.Yaw, MathF.PI), -0.2f, 0.2f);
        Assert.Equal(Cam, e.Cameras.ViewFor(0)!.Binding.SourceId);
        e.Execute(Cam, "hide 0");
        Assert.Null(e.Cameras.ViewFor(0));
    }

    [Fact]
    public void LookAndMoveFollowTheirTargets()
    {
        var (w, e) = Setup();
        e.Execute(Cam, "simulate 0");
        e.Execute(Cam, "move 10 0,1,0 0.1 0,-0.2,-3");
        e.Execute(Cam, "look 100 0,0,50");
        Run(e, 0.1f);
        var cam = e.Cameras.Get(Cam)!;
        // car yaw pi: its -Z offset points to world +Z
        Assert.Equal(0f, cam.Position.X, 0.001f);
        Assert.Equal(-0.2f, cam.Position.Y, 0.001f);
        Assert.Equal(3f, cam.Position.Z, 0.001f);
        Assert.InRange(Ypr.AngleDiff(cam.Orientation.Yaw, MathF.PI), -0.01f, 0.01f);
        Assert.Equal(cam.Position, w.PoseOf(Cam).Position);
    }

    [Fact]
    public void CarInteriorViewIsBoundThroughTheCarRenderVerb()
    {
        var (_, e) = Setup();
        Assert.True(e.Execute(Car, "render 0 1 2").Consumed);
        var v = e.Cameras.ViewFor(0)!;
        Assert.True(v.Binding.Interior);
        Assert.Equal(2, v.Binding.CameraIndex);
        Assert.True(e.Execute(Car, "hide 0 0").Consumed);
        Assert.Null(e.Cameras.ViewFor(0));
    }

    [Fact]
    public void CarHoldStatesGateTheStandInVehicles()
    {
        var (w, e) = Setup();
        e.Driver.SetInput(Car, new DriveInput(1, 0, 0));
        e.Execute(Car, "stop");
        Run(e, 1);
        Assert.Equal(Vector3.Zero, w.PoseOf(Car).Position);
        e.Execute(Car, "start");
        Run(e, 1);
        Assert.True(w.PoseOf(Car).Velocity.Length() > 4);
        e.Execute(Car, "brake");
        Run(e, 3);
        Assert.Equal(0f, w.PoseOf(Car).Velocity.Length(), 0.001f);
        e.Execute(Car, "sethorn 1");
        Assert.Equal(1, e.Horn(Car));
        e.Execute(Car, "filter 3 2");
        Assert.Equal(2, e.Cars.Get(Car)!.Filters[3]);
        e.Execute(Car, "steerhelp 0.666");
        Assert.Equal(0.666f, e.Cars.Get(Car)!.Settings["steerhelp"], 0.0001f);
    }

    [Fact]
    public void TriggersFireOnAndOffWithTheCarIdAsTokenZero()
    {
        var (w, e) = Setup();
        w.Add(Trig, "trigger", "testrack trigger", "0,0,30,0,0,0,sphere,10", new Vector3(0, 0, 30), 0, Map);
        e.Execute(Car, "start");                 // the car becomes a participant (received a car verb)
        e.Driver.SetInput(Car, new DriveInput(1, 0, 0));
        Run(e, 8);
        var mine = w.Events.Where(x => x.Target == Trig).ToList();
        Assert.Equal(2, mine.Count);
        Assert.Equal((Trig, Car, RaceDirector.EventTriggerOn, "10"), mine[0]);
        Assert.Equal((Trig, Car, RaceDirector.EventTriggerOff, "10"), mine[1]);
    }

    [Fact]
    public void OffEventsAreEmittedBeforeOnEventsInOneStep()
    {
        var (w, e) = Setup();
        w.Add(Trig, "trigger", "a", "0,0,0,0,0,0,sphere,5", Vector3.Zero, 0, Map);
        w.Add(Trig + 1, "trigger", "b", "100,0,0,0,0,0,box,10,10,10", new Vector3(100, 0, 0), 0, Map);
        e.Execute(Car, "start");
        e.Step(0.01f);
        w.SetPose(Car, new Pose(new Vector3(100, 0, 0), default, default));
        e.Step(0.01f);
        var t = w.Events.Select(x => (x.Target, x.Code)).ToList();
        Assert.Equal([(Trig, 32), (Trig, 64), (Trig + 1, 32)], t);
    }

    [Fact]
    public void BrainFollowsItsTargetAndStops()
    {
        var (w, e) = Setup();
        w.Add(Brain, "bot", "BOTBRAIN", parent: Map);
        w.Add(Car2, "car", "Police Bot", null, new Vector3(-60, 0, -40), 0, Map);
        foreach (var c in new[] { "AI_level 20.000", "controllable 11", "AI_suspend", "AI_params 50.0 50.35", "AI_follow 0,0,0.001 10" })
        {
            Assert.True(e.Execute(Brain, c).Consumed, c);
        }

        Run(e, 20);
        var d = Vector3.Distance(w.PoseOf(Car2).Position, w.PoseOf(Car).Position);
        Assert.InRange(d, 0f, 14f);
        Assert.Equal(AiMode.Follow, e.Race.Controller(Brain)!.Mode);
        e.Execute(Brain, "AI_stop");
        Run(e, 5);
        Assert.Equal(0f, w.PoseOf(Car2).Velocity.Length(), 0.01f);
    }

    [Fact]
    public void BrainRacesToTheFinishAfterBeginRace()
    {
        var (w, e) = Setup();
        w.Add(Brain, "bot", "BOTBRAIN", parent: Map);
        w.Add(Car2, "car", "bot car", null, new Vector3(5, 0, 0), MathF.PI, Map);
        e.Execute(Brain, "controllable 11");
        e.Execute(Brain, "AI_BeginRace 0.5");
        e.Execute(Car2, "start");
        e.Execute(Brain, "AI_race 5.0,0.0,400.0 10");
        Run(e, 0.4f);
        Assert.Equal(0f, w.PoseOf(Car2).Velocity.Length(), 0.001f);   // reaction delay (GUESSED role of the float)
        Run(e, 25);
        Assert.True(w.PoseOf(Car2).Position.Z > 380, $"bot at {w.PoseOf(Car2).Position}");
        Assert.InRange(w.PoseOf(Car2).Position.X, 3f, 7f);
    }

    [Fact]
    public void GoToTrafficPostsEnterTrafficAndCarAdd()
    {
        var (w, e) = Setup();
        w.Add(Brain, "bot", "BOTBRAIN", parent: Map);
        w.Add(Car2, "car", "bot car", null, new Vector3(5, 0, 0), MathF.PI, Map);
        e.Execute(Brain, "controllable 11");
        e.Execute(Brain, "AI_GoToTraffic");
        Assert.Contains(w.Events, x => x.Target == Map && x.Text == "car_add 11 1");
        Assert.Contains(w.Events, x => x.Target == Car2 && x.Text == "ai_info_entertraffic 0x1 0xb 0x1e");
        Run(e, 3);
        Assert.InRange(w.PoseOf(Car2).Velocity.Length(), 5f, 13f);
        e.Traffic.Remove(1);
        Assert.Contains(w.Events, x => x.Target == Map && x.Text == "car_rem 11 1");
    }

    [Fact]
    public void RouteTelemetryCountsLapsAndWrongWay()
    {
        var (w, e) = Setup();
        e.Execute(Car, "start");
        var pts = new[] { new Vector3(0, 0, 0), new Vector3(0, 0, 100), new Vector3(0, 0, 200) };
        var r = e.Race.TrackRoute(Car, pts, 5, loop: false);
        e.Driver.SetInput(Car, new DriveInput(1, 0, 0));
        Run(e, 20);
        Assert.True(r.Finished);
        Assert.Equal(2, r.Splits.Count);
        Assert.True(r.Splits[1] > r.Splits[0]);

        var r2 = e.Race.TrackRoute(Car, [new Vector3(0, 0, -1000)], 5, loop: false);
        Run(e, 3);
        Assert.True(r2.WrongWay);
    }

    [Fact]
    public void StepIsDeterministic()
    {
        string Once()
        {
            var (w, e) = Setup();
            w.Add(Brain, "bot", "BOTBRAIN", parent: Map);
            w.Add(Car2, "car", "Police Bot", null, new Vector3(-60, 0, -40), 0.3f, Map);
            e.Execute(Brain, "controllable 11");
            e.Execute(Brain, "AI_follow 0,0,0.001 10");
            e.Execute(Player, "controllable 10");
            e.Execute(Cam, "dist 2.0 10.0");
            e.Execute(Player, "camera 20");
            e.Driver.SetInput(Car, new DriveInput(0.7f, 0, 0.2f));
            Run(e, 10);
            return string.Join("|", w.PoseOf(Car2).Position, w.PoseOf(Car).Position, e.Cameras.Get(Cam)!.Position);
        }

        Assert.Equal(Once(), Once());
    }
}
