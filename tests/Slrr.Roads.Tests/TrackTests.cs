using System.Numerics;
using Slrr.Engine.MapNatives;
using Slrr.Engine.Resources;
using Slrr.Game;
using Slrr.Roads;
using Slrr.Vm;
using Xunit;
using static Slrr.Roads.Tests.Approx;
using Xunit.Abstractions;

// the city world and a booted game host are large: run the classes one after another (shared CI boxes ran out of memory)
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace Slrr.Roads.Tests;

/// <summary>The shipped scripts enter Valo City (Valocity extends Track) on a GameHost with the map natives and this area's
/// road natives on top: the road queries answer from city.trc and the start position has ground.</summary>
public class TrackTests(ITestOutputHelper output)
{
    private const int CmdOk = 1;
    private const int CmdDown = 4;
    private const int CmdMap0 = 10;

    [Fact]
    public void ValoCityEntersWithRoadNativesAndGroundUnderTheStart()
    {
        var root = GameRoot.Find();
        if (root is null)
        {
            return;
        }

        GC.Collect(); // the corpus fixture's city world may still be around
        GameHost? host = null;
        var maps = new MapNativesModule(null, new MapWorldOptions { GameRoot = root, BuildRenderScene = false, IncludeObjectCollision = false },
            new MapNativeOptions { FogSink = (id, c, n, f) => host!.Natives.Fog[id] = (c, n, f) });
        var roads = new RoadNativesModule(maps.Maps, new RoadGraphCatalog(root));
        host = new GameHost(new GameHostOptions { Root = root, ExtraModules = [maps, roads] });
        using var _ = host;
        maps.Attach(host.Resources);
        Assert.True(host.Boot());
        Assert.True(host.RunUntil(() => Section(host) == "MainMenu", 30));
        host.Input.Click("SELECT TRACK");
        Assert.True(host.RunUntil(() => Section(host) == "TrackSelect", 30));
        host.RunFor(0.5f);

        var boxes = Probe.Vector(host.Vm, Probe.Static(host.Vm, "java.game.GameLogic", "MapBox").AsObject).Select(v => v.AsObject).OfType<VmObject>().ToList();
        var index = boxes.FindIndex(b => b.Class.Name.Contains("Valo", StringComparison.OrdinalIgnoreCase));
        Assert.True(index >= 0, string.Join(", ", boxes.Select(b => b.Class.Name)));
        for (var page = 0; page < index / 10; page++)
        {
            host.Input.ClickCommand(CmdDown);
            host.RunFor(0.3f);
        }

        host.Input.ClickCommand(CmdMap0 + (index % 10));
        host.RunFor(0.3f);
        host.Input.ClickCommand(CmdOk);
        Assert.True(host.RunUntil(() => Entered(host), 90), host.Describe());
        host.RunFor(3f);

        var track = host.ActiveSection!;
        var map = Probe.Field(track, "map").AsObject!;
        var posStart = ScriptValues.ReadVector(Probe.Field(track, "posStart").AsObject)!.Value;
        var groundId = ResourceEngine.IdOf(map);
        var state = maps.Maps.State(groundId);
        Assert.NotNull(state.World);
        Assert.NotNull(roads.GraphOf(groundId));

        // script-visible answers, run in the VM through the registered natives
        var arr = (VmArray)host.Vm.InvokeVirtual(map, "alignToRoad", Value.Obj(ScriptValues.NewVector(host.Vm, posStart))).AsObject!;
        var aligned = ScriptValues.ReadVector(arr.Items[0].AsObject)!.Value;
        var dir = ScriptValues.ReadVector(arr.Items[1].AsObject)!.Value;
        var cross = ScriptValues.ReadVector(host.Vm.InvokeVirtual(map, "getNearestCross", Value.Obj(ScriptValues.NewVector(host.Vm, posStart)), Value.Float(500)).AsObject)!.Value;
        var startDir = host.Vm.InvokeVirtual(map, "getStartDirection", Value.Obj(ScriptValues.NewVector(host.Vm, posStart)), Value.Obj(ScriptValues.NewVector(host.Vm, cross))).AsObject;
        var routeLen = host.Vm.InvokeVirtual(map, "getRouteLength", Value.Obj(ScriptValues.NewVector(host.Vm, posStart)), Value.Obj(ScriptValues.NewVector(host.Vm, cross))).AsFloat;
        var hasGround = state.Ground!.TryGetGround(posStart.X, posStart.Z, posStart.Y + 2, out var hit);

        output.WriteLine($"posStart {posStart}; ground {(hasGround ? hit.Position.Y.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture) : "none")}; extensions {string.Join(",", state.World!.Extensions.Select(x => x.Name))}");
        output.WriteLine($"alignToRoad {aligned} dir {dir}; getNearestCross(p, 500) {cross} (|d| {Vector3.Distance(cross, posStart):0.0}); getStartDirection {ScriptValues.ReadVector(startDir)}; getRouteLength {routeLen:0.0}");
        output.WriteLine("road natives (graph, fallback): " + string.Join(", ", roads.Calls.Select(k => $"{k.Key} {k.Value}")));
        output.WriteLine($"navigator updates {roads.Navigator.Updates}, car {roads.Navigator.CarPosition}, lane {roads.Navigator.Lane?.Lane.Id}");
        output.WriteLine($"missing natives: {string.Join("; ", host.Missing.Select(m => m.Key))}; thread errors {host.ThreadErrors.Count}");

        Assert.True(hasGround, $"no ground under posStart {posStart}");
        Near(1f, dir.Length(), 1e-3f);
        Assert.True(Vector3.Distance(aligned, posStart) < 60, $"aligned {aligned}");
        Assert.NotNull(startDir);
        Assert.True(routeLen > 0);
        Assert.Empty(host.ThreadErrors);
        Assert.True(roads.Calls.Values.Sum(v => v.Graph) >= 4);
    }

    private static string Section(GameHost host) => host.ActiveSection is { } s ? ScreenTracker.Short(s.Class.Name) : "-";

    private static bool Entered(GameHost host) =>
        host.ActiveSection is { } s && s.Class.IsSubclassOf("java.game.Track") && host.Resources.NodeOf(s)?.Callbacks.Contains(HostNatives.GiiAnimate) == true;
}
