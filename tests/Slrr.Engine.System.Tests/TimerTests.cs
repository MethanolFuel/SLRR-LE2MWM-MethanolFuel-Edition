using Slrr.Vm;
using Slrr.Vm.Tests;
using Xunit;

namespace Slrr.Engine.SystemLib.Tests;

/// <summary>addTimer / removeAllTimers, Thread.sleep units and the frame driver, with hand-assembled script classes.</summary>
public class TimerTests
{
    private const string Timed = "test.Timed";
    private const string Napper = "test.Napper";
    private const string GameRef = "Ljava.util.resource.GameRef;";

    private static ClassBuilder TimedClass() => new ClassBuilder(Timed, "java.lang.GameType")
        .Field("hits", "I", isStatic: true)
        .Field("lastId", "I", isStatic: true)
        .Field("lastEvent", "I", isStatic: true)
        // handleEvent(GameRef self, int event, int param): slot 0 is this, then the arguments in reverse (param = 1, event = 2)
        .Method("handleEvent", $"({GameRef}II)V", a =>
        {
            a.Field(Timed, "hits", "I").Int(1).O(Operator.Add).Field(Timed, "hits", "I").D(Operator.Assign);
            a.Local(1).Field(Timed, "lastId", "I").D(Operator.Assign);
            a.Local(2).Field(Timed, "lastEvent", "I").D(Operator.Assign);
            a.ReturnVoid();
        })
        .Method("arm", "(FI)V", a =>
        {
            a.Local(2).Local(1).Call(2, "java.lang.GameType", "addTimer", "(FI)V").D(Operator.Expr);
            a.ReturnVoid();
        })
        .Method("clear", "()V", a =>
        {
            a.Call(0, "java.lang.GameType", "removeAllTimers", "()V").D(Operator.Expr);
            a.ReturnVoid();
        });

    private static (VmHarness H, VmObject Obj, VmClass Cls)? Setup()
    {
        var h = VmHarness.Game();
        if (h is null)
        {
            return null;
        }

        h.Vm.ClassPath.AddClass(TimedClass().Build());
        var cls = h.Vm.LoadClass(Timed);
        return (h, h.Vm.NewObject(cls), cls);
    }

    private static int Static(VmClass cls, string name) => cls.StaticValues[cls.StaticFields.Single(f => f.Name == name).Slot].AsInt;

    [Fact]
    public void TimersAreOneShotAndFireOnTheWallClockInOrder()
    {
        if (Setup() is not var (h, obj, cls))
        {
            return;
        }

        using (h)
        {
            h.Call(obj, "arm", Value.Float(2f), Value.Int(7));
            h.Call(obj, "arm", Value.Float(1f), Value.Int(3));
            Assert.Equal(2, h.System.Timers.Count);

            h.System.RunFor(h.Vm, 0.9f);
            Assert.Equal(0, Static(cls, "hits"));
            h.System.RunFor(h.Vm, 0.2f); // t = 1.1
            Assert.Equal(1, Static(cls, "hits"));
            Assert.Equal(3, Static(cls, "lastId"));
            Assert.Equal(TimerService.EventTime(h.Vm), Static(cls, "lastEvent"));
            h.System.RunFor(h.Vm, 1f); // t = 2.1
            Assert.Equal(2, Static(cls, "hits"));
            Assert.Equal(7, Static(cls, "lastId"));
            h.System.RunFor(h.Vm, 10f);
            Assert.Equal(2, Static(cls, "hits")); // never re-armed
            Assert.Equal(0, h.System.Timers.Count);
        }
    }

    [Fact]
    public void HandlersCanReArmAndRemoveAllTimersCancels()
    {
        if (Setup() is not var (h, obj, cls))
        {
            return;
        }

        using (h)
        {
            h.Call(obj, "arm", Value.Float(1f), Value.Int(1));
            h.Call(obj, "arm", Value.Float(5f), Value.Int(2));
            h.Call(obj, "clear");
            Assert.Equal(0, h.System.Timers.Count);
            h.System.RunFor(h.Vm, 6f);
            Assert.Equal(0, Static(cls, "hits"));

            // the event mask check can veto delivery (setEventMask lives in the game-object layer)
            h.System.Timers.AcceptsTimeEvents = _ => false;
            h.Call(obj, "arm", Value.Float(0.5f), Value.Int(9));
            h.System.RunFor(h.Vm, 1f);
            Assert.Equal(0, Static(cls, "hits"));
            h.System.Timers.AcceptsTimeEvents = _ => true;
            h.Call(obj, "arm", Value.Float(0f), Value.Int(9));
            h.System.RunFor(h.Vm, 0.1f);
            Assert.Equal(1, Static(cls, "hits"));
        }
    }

    [Fact]
    public void ThreadSleepTakesMilliseconds()
    {
        var b = new ClassBuilder(Napper)
            .Field("done", "I", isStatic: true)
            .Method("worker", "()V", a =>
            {
                a.Float(1500f).CallStatic(1, "java.lang.Thread", "sleep", "(F)V").D(Operator.Expr);
                a.Int(1).Field(Napper, "done", "I").D(Operator.Assign);
                a.ReturnVoid();
            }, isStatic: true);
        using var h = VmHarness.Game()!;
        if (h is null)
        {
            return;
        }

        h.Vm.ClassPath.AddClass(b.Build());
        var cls = h.Vm.LoadClass(Napper);
        var t = h.Vm.Spawn(cls.Methods.Single(m => m.Name == "worker"), null, [], "w");
        h.System.RunFor(h.Vm, 1f);
        Assert.Equal(VmThreadState.Sleeping, t.State);
        h.System.RunFor(h.Vm, 0.6f);
        Assert.Equal(1, Static(cls, "done"));
    }

    [Fact]
    public void SimulationClockFollowsTimeWarp()
    {
        using var h = VmHarness.Game()!;
        if (h is null)
        {
            return;
        }

        h.System.RunFor(h.Vm, 1f);
        Assert.Equal(1f, h.Vm.SimTime, 0.01f);
        h.Static("java.lang.System", "timeWarp", Value.Float(0f)); // pause
        h.System.RunFor(h.Vm, 2f);
        Assert.Equal(1f, h.Vm.SimTime, 0.01f);
        Assert.Equal(3f, h.Vm.Time, 0.01f); // wall clock keeps running
        h.Static("java.lang.System", "timeWarp", Value.Float(2f));
        h.System.RunFor(h.Vm, 1f);
        Assert.Equal(3f, h.Vm.SimTime, 0.01f);
        Assert.Equal(h.Vm.Time, h.Static("java.lang.System", "currentTime").AsFloat);
        Assert.Equal(h.Vm.SimTime, h.Static("java.lang.System", "simTime").AsFloat);
    }
}
