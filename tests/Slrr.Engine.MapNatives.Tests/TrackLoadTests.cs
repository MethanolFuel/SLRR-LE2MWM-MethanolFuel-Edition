using Slrr.Cli;
using Xunit;
using Xunit.Abstractions;

namespace Slrr.Engine.MapNatives.Tests;

public sealed class TrackLoadTests(ITestOutputHelper output)
{
    [Fact]
    public void DragTrackLoadsThroughTheTrackSelector()
    {
        if (GameRoot.Find() is not { } root)
        {
            return;
        }

        var h = new TrackLoadHarness(root);
        var names = h.MapNames();
        var i = names.FindIndex(n => n.Name.Trim() == "Drag Track");
        Assert.True(i >= 0, string.Join(", ", names.Select(n => n.Name)));
        var r = h.RunOne(i);
        var w = new StringWriter();
        TrackLoadHarness.Print(r, w);
        output.WriteLine(w.ToString());
        Assert.True(r.Entered, w.ToString());
        Assert.NotNull(r.World);
        Assert.Equal(r.GroundId, r.CarParent);
        Assert.NotNull(r.ScriptGroundY);
        Assert.Equal(r.WorldGroundY!.Value, r.ScriptGroundY!.Value, 1e-3f);
        Assert.Contains(r.GroundCalls, k => k.Key == "setWater");
        Assert.DoesNotContain(r.Missing, m => m.Contains("GroundRef", StringComparison.Ordinal) || m.Contains("Vector3.length", StringComparison.Ordinal));
        Assert.Equal("TrackSelect", r.ExitSection);
        Assert.True(r.WorldDropped);
        Assert.Empty(r.ThreadErrors);
    }

    [Fact]
    public void EveryTrackSelectorMap()
    {
        if (GameRoot.Find() is not { } root || Environment.GetEnvironmentVariable("SLRR_ALL_MAPS") is null)
        {
            return;
        }

        foreach (var native in new[] { true, false })
        {
            var h = new TrackLoadHarness(root) { UseMapNatives = native };
            var names = h.MapNames();
            var w = new StringWriter();
            var entered = 0;
            for (var i = 0; i < names.Count; i++)
            {
                var r = h.RunOne(i);
                entered += r.Entered ? 1 : 0;
                TrackLoadHarness.Print(r, w);
            }

            output.WriteLine($"map natives {native}: {entered}/{names.Count}");
            output.WriteLine(w.ToString());
        }
    }
}
