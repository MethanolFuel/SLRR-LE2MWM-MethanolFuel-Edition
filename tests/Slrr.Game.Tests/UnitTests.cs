using Slrr.Engine.Parts;
using Xunit;

namespace Slrr.Game.Tests;

/// <summary>Host pieces that need no game data.</summary>
public class UnitTests
{
    [Fact]
    public void PlayScriptParsesQuotesCommentsAndLineNumbers()
    {
        var steps = PlayScript.Parse("""
            # comment
            click "NEW CAREER"   # trailing comment
            until section Garage 30

            expect money == 20000
            type "Bob Smith"
            """);
        Assert.Equal(4, steps.Count);
        Assert.Equal(("click", 2), (steps[0].Verb, steps[0].Line));
        Assert.Equal(["NEW CAREER"], steps[0].Args);
        Assert.Equal(["section", "Garage", "30"], steps[1].Args);
        Assert.Equal(["money", "==", "20000"], steps[2].Args);
        Assert.Equal(["Bob Smith"], steps[3].Args);
    }

    [Fact]
    public void KeyNamesMapToDirectInputScanCodes()
    {
        Assert.Equal(28, InputInjector.KeyCode("Enter"));
        Assert.Equal(28, InputInjector.KeyCode("RETURN"));
        Assert.Equal(1, InputInjector.KeyCode("esc"));
        Assert.Equal(200, InputInjector.KeyCode("Up"));
        Assert.Equal(30, InputInjector.KeyCode("A"));
        Assert.Equal(57, InputInjector.KeyCode("Space"));
        Assert.Equal(42, InputInjector.KeyCode("42"));
        Assert.Throws<ArgumentException>(() => InputInjector.KeyCode("NoSuchKey"));
    }

    [Fact]
    public void PlaceholderDynoGivesAWorkingEngineUnlessInoperable()
    {
        var dyno = new PlaceholderDynoCalculator();
        var d18 = Params(cylinders: 4, bore: 0.081f, stroke: 0.087f, limit: 7600, flags: 0);
        var curve = dyno.Calculate(d18, 30);
        Assert.NotNull(curve);
        Assert.InRange(curve!.MaxTorque, 120f, 160f);          // 1.8 l at 1 MPa BMEP
        Assert.InRange(curve.MaxHp, 100f, 200f);
        Assert.InRange(curve.RpmMaxTorque, 4559f, 4561f);
        Assert.Equal(0f, dyno.Torque(d18, 9000, 0));
        var dead = Params(cylinders: 4, bore: 0.081f, stroke: 0.087f, limit: 7600, flags: unchecked((int)0x80000000));
        Assert.Equal(0f, dyno.Calculate(dead, 30)!.MaxTorque);
    }

    private static DynoParams Params(float cylinders, float bore, float stroke, float limit, int flags) => new()
    {
        Values = new Dictionary<string, float>
        {
            ["cylinders"] = cylinders, ["bore"] = bore, ["stroke"] = stroke, ["RPM_limit"] = limit, ["P_turbo_max"] = 0f,
        },
        Flags = flags,
    };

    [Fact]
    public void RecordingAudioSinkHandsOutHandles()
    {
        var a = new RecordingAudioSink();
        var h1 = a.Play(5, null, 0, 1, 1, 0);
        var h2 = a.Play(6, new System.Numerics.Vector3(1, 2, 3), 0, 1, 1, 1);
        a.Stop(h2);
        Assert.NotEqual(h1, h2);
        Assert.Equal(2, a.Played.Count);
        Assert.Contains(h2, a.Stopped);
        Assert.False(new NullPhysicsWorld().Command(1, "start"));
    }

    [Fact]
    public void ScreenVisitKeyJoinsSectionAndDialog()
    {
        Assert.Equal("Garage > WarningDialog", new ScreenVisit(1, 1, "Garage", "WarningDialog", "WELCOME!").Key);
        Assert.Equal("Catalog", new ScreenVisit(1, 1, "Catalog", null, null).Key);
        Assert.Equal("CarMarket", ScreenTracker.Short("java.game.CarMarket"));
    }

    [Fact]
    public void PlayCommandRejectsBadArguments()
    {
        var o = new StringWriter();
        var e = new StringWriter();
        Assert.Equal(2, PlayCommand.Run(["--bogus"], o, e));
        Assert.Contains("play [script.play", e.ToString(), StringComparison.Ordinal);
        Assert.Equal(0, PlayCommand.Run(["--help"], o, e));
        Assert.Contains("until section MainMenu", PlayCommand.SmokeScript(), StringComparison.Ordinal);
    }
}
