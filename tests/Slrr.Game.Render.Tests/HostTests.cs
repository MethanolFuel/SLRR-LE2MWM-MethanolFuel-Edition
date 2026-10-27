using System.Numerics;
using Slrr.Engine.Parts;
using Slrr.Engine.Resources;
using Slrr.Game.Render.Cameras;
using Slrr.Game.Render.Cars;
using Slrr.Game.Render.Maps;
using Slrr.Game.Render.Scene;
using Slrr.Render.Content;
using Xunit;

namespace Slrr.Game.Render.Tests;

/// <summary>The renderer's reading of the live host (no GL needed): part tree to draw items, paint, wheels, cameras, map choice.</summary>
public class HostTests
{
    private static (ResourceNode Node, PartState Chassis) BackgroundCar(GameHost host)
    {
        var node = host.Resources.Tree.Instances.ToList().Single(n => n.NativeKind == "car" && n.Script is not null && n.ParentId != host.Resources.RootId);
        var chassis = host.Parts.Find(node.Script);
        Assert.NotNull(chassis);
        return (node, chassis!);
    }

    [Fact]
    public void TheMainMenuBackgroundCarIsBuiltFromItsLivePartTreeWithPaintAndWheels()
    {
        if (GameRoot.Find() is not { } root)
        {
            return;
        }

        using var menu = new MenuHost(root);
        var host = menu.Host;
        var (node, chassis) = BackgroundCar(host);
        var content = new GameContent(root);
        var builder = new CarVisualBuilder(content, host.Resources);
        var frame = builder.Build(chassis, Matrix4x4.Identity, null);

        Assert.True(frame.Parts > 40, $"{frame.Parts} parts");
        Assert.True(frame.Items.Count > 60, $"{frame.Items.Count} items");
        Assert.True(frame.Lights >= 8, $"{frame.Lights} lights counted");
        // Paint: MEASURED substitution of the cfg default texture (default_white.png) by a factory colour of GameLogic.CARCOLORS.
        Assert.True(frame.PaintedParts >= 8, $"{frame.PaintedParts} painted parts");
        var subs = builder.PaintSubstitutions.ToList();
        Assert.NotEmpty(subs);
        Assert.All(subs, s =>
        {
            Assert.EndsWith("default_white.png", s.From, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("factory_colors", s.To, StringComparison.OrdinalIgnoreCase);
        });
        Assert.Contains(frame.Items, i => i.PaintFrom is not null && i.PaintTo is not null);
        Assert.True(node.Id != 0);
    }

    [Fact]
    public void WheelAnimationMovesOnlyRimsAndTyresAndSteersOnlyTheFrontWheels()
    {
        if (GameRoot.Find() is not { } root)
        {
            return;
        }

        using var menu = new MenuHost(root);
        var (_, chassis) = BackgroundCar(menu.Host);
        var builder = new CarVisualBuilder(new GameContent(root), menu.Host.Resources) { CullOpaque = false };
        var rest = builder.Build(chassis, Matrix4x4.Identity, new CarState());
        var spun = new CarState { Wheels = [new(1f, 0, 0), new(1f, 0, 0), new(1f, 0, 0), new(1f, 0, 0)] };
        var rolled = builder.Build(chassis, Matrix4x4.Identity, spun);
        var steered = builder.Build(chassis, Matrix4x4.Identity, new CarState { Wheels = [new(0, 0.5f, 0), new(0, 0.5f, 0), new(0, 0, 0), new(0, 0, 0)] });
        var lifted = builder.Build(chassis, Matrix4x4.Identity, new CarState { Wheels = [new(0, 0, 0.1f), new(0, 0, 0.1f), new(0, 0, 0.1f), new(0, 0, 0.1f)] });
        Assert.Equal(rest.Items.Count, rolled.Items.Count);

        var changedBySpin = Changed(rest, rolled);
        Assert.InRange(changedBySpin.Count, 8, 80);
        Assert.All(changedBySpin.Select(k => rest.Items[k].Item.Group).Distinct(), g => Assert.True(g.Contains("rim", StringComparison.OrdinalIgnoreCase) || g.Contains("tyre", StringComparison.OrdinalIgnoreCase), g));
        var changedBySteer = Changed(rest, steered);
        Assert.NotEmpty(changedBySteer);
        Assert.True(changedBySteer.Count < changedBySpin.Count + 20 && changedBySteer.Count > 0, "steering moves wheels (and brakes) of the front axle");
        Assert.True(changedBySteer.All(k => changedBySpin.Contains(k) || rest.Items[k].Item.Group.Contains("brake", StringComparison.OrdinalIgnoreCase)));
        var changedByLift = Changed(rest, lifted);
        Assert.True(changedByLift.Count >= changedBySpin.Count, "travel moves brakes too");
        // Suspension travel is a pure vertical offset of 0.1 m.
        var i = Enumerable.Range(0, rest.Items.Count).First(k => rest.Items[k].Item.World != lifted.Items[k].Item.World);
        Assert.True(MathF.Abs(lifted.Items[i].Item.World.Translation.Y - rest.Items[i].Item.World.Translation.Y - 0.1f) < 1e-3f);
    }

    private static List<int> Changed(CarFrame a, CarFrame b) =>
        Enumerable.Range(0, a.Items.Count).Where(k => a.Items[k].Item.World != b.Items[k].Item.World).ToList();

    [Fact]
    public void HeadlightsAndTaillightsBecomeGlowsOnlyWhenSwitchedOn()
    {
        if (GameRoot.Find() is not { } root)
        {
            return;
        }

        using var menu = new MenuHost(root);
        var (_, chassis) = BackgroundCar(menu.Host);
        var builder = new CarVisualBuilder(new GameContent(root), menu.Host.Resources);
        Assert.Empty(builder.Build(chassis, Matrix4x4.Identity, new CarState()).Glows);
        var brake = builder.Build(chassis, Matrix4x4.Identity, new CarState { BrakeLights = true }).Glows;
        var all = builder.Build(chassis, Matrix4x4.Identity, new CarState { Headlights = true }).Glows;
        Assert.NotEmpty(brake);
        Assert.True(all.Count > brake.Count);
        // Taillight glows are red (lf_glow_color 1 0.5 0 0 read as A,R,G,B), headlights white.
        Assert.Contains(brake, g => g.Color.X > 0.9f && g.Color.Y < 0.2f);
        Assert.Contains(all, g => g.Color.Y > 0.8f && g.Color.Z > 0.8f);
    }

    [Fact]
    public void RenderInstancesAreSplitIntoRenderEntryAndSceneWhicheverWayTheResourcesAreaStoresThem()
    {
        if (GameRoot.Find() is not { } root)
        {
            return;
        }

        using var menu = new MenuHost(root);
        var host = menu.Host;
        var tree = host.Resources.Tree;
        var instances = tree.Instances.Where(n => n.Type == ResourceTypes.InstanceRender).ToList();
        Assert.True(instances.Count > 100);
        var split = instances.Select(n => (Node: n, Parts: HostIds.RenderParts(tree, n))).ToList();
        // Every render instance resolves to a render (14) or light (13) entry; on this host the entry is the node's ParentId (the area stores the
        // scene in TypeId), and the bg car's scene hangs below the Dummy created by MainMenuDialog.
        var withEntry = split.Where(x => x.Parts.Render != 0).ToList();
        Assert.True(withEntry.Count > 100);
        Assert.All(withEntry, x => Assert.True(tree.Get(x.Parts.Render) is { Type: 13 or 14 }, $"{x.Node}"));
        Assert.All(withEntry, x => Assert.Equal(x.Node.ParentId, x.Parts.Render));
        Assert.Contains(split, x => ResourceIds.IsInstance(x.Parts.Scene) && tree.Get(x.Parts.Scene)?.Script?.Class.Name == "java.util.resource.Dummy");
        // Overlay rectangles hang below Osd Groups and are not scene content.
        Assert.Contains(split, x => HostIds.InsideOsd(tree, x.Node));
        Assert.Contains(split, x => !HostIds.InsideOsd(tree, x.Node));
    }

    [Fact]
    public void RenderLinesWithAStateFlagAreOnlyDrawnWhileTheirStateIsOn()
    {
        if (GameRoot.Find() is not { } root)
        {
            return;
        }

        using var menu = new MenuHost(root);
        var (_, chassis) = BackgroundCar(menu.Host);
        var builder = new CarVisualBuilder(new GameContent(root), menu.Host.Resources);
        var off = builder.Build(chassis, Matrix4x4.Identity, new CarState());
        var lit = builder.Build(chassis, Matrix4x4.Identity, new CarState { Headlights = true });
        var braking = builder.Build(chassis, Matrix4x4.Identity, new CarState { BrakeLights = true });
        var reversing = builder.Build(chassis, Matrix4x4.Identity, new CarState { Reversing = true });
        Assert.True(off.StateVariantsHidden >= 10, $"{off.StateVariantsHidden} lamp/flame variants hidden at rest");
        Assert.True(lit.Items.Count > off.Items.Count, "CITY (flags 4) lamp meshes appear with the headlights");
        Assert.True(braking.Items.Count > off.Items.Count, "BRAKE (flags 1) lamp meshes appear when braking");
        Assert.True(reversing.Items.Count >= off.Items.Count);
        // The flags line is measured per cfg: every part cfg keeps one entry per render line.
        var cfgs = PartsWorld.Tree(chassis).Select(builder.Cfg).OfType<PartCfg>().ToList();
        Assert.All(cfgs, c => Assert.True(c.RenderFlags.Count == c.Info.Renders.Count, c.Name));
        Assert.Contains(cfgs, c => c.RenderFlags.Any(f => (f & 4) != 0));
        Assert.Contains(cfgs, c => c.RenderFlags.Any(f => (f & 1) != 0));
    }

    [Fact]
    public void CameraPosesOfScriptCamerasAreRecordedAndTheBackgroundCameraCircles()
    {
        if (GameRoot.Find() is not { } root)
        {
            return;
        }

        var hooks = new RenderHooks();
        using var host = new GameHost(new GameHostOptions { Root = root, ExtraModules = [hooks] });
        host.UiEngine.Video.VideoAvailable = false;
        Assert.True(host.Boot());
        Assert.True(host.RunUntil(() => host.ActiveSection?.Class.Name?.EndsWith("MainMenu", StringComparison.Ordinal) == true, 30));
        host.RunFor(1f);
        var source = new HostCameraSource(hooks);
        var first = source.Cameras(host);
        Assert.Contains(first, c => c.Kind == "script");
        var bg = first.Where(c => c.Kind == "script").OrderByDescending(c => c.FovDegrees).First(); // the 90 degree background camera (the others are Osd/cursor cameras)
        host.RunFor(2f);
        var later = source.Cameras(host).First(c => c.Kind == "script" && c.SceneId == bg.SceneId && c.FovDegrees > 80);
        Assert.True(Vector3.Distance(bg.Pose.Eye, later.Pose.Eye) > 0.03f, "the menu camera moves along its path");
        // The eye stays on the path ellipse around the car (|x| < 1.4, |z| < 3).
        Assert.True(MathF.Abs(later.Pose.Eye.X) < 1.5f && MathF.Abs(later.Pose.Eye.Z) < 3.1f);
        Assert.True(hooks.CameraPoseWrites > 10);
    }

    [Fact]
    public void TheGarageSceneRootIsAnInstanceRootOfItsArchive()
    {
        if (GameRoot.Find() is not { } root)
        {
            return;
        }

        var content = new GameContent(root);
        using var menu = new MenuHost(root);
        var host = menu.Host;
        var id = host.Resources.Archives.ResolveLiteral("misc\\garage.rpk", 0x7A); // garage1
        Assert.NotEqual(0, id);
        var src = new HostMapSource(content, 100f);
        var req = src.Resolve(host, id, new Vector3(1, 2, 3));
        Assert.NotNull(req);
        Assert.Equal(0x7Au, req!.RootId);
        Assert.Equal("misc\\garage.rpk", req.Archive);
        Assert.Equal(new Vector2(1, 3), req.Center);
        Assert.Null(src.Resolve(host, 0, Vector3.Zero));
        Assert.Null(src.Resolve(host, host.Resources.RootId, Vector3.Zero));
    }

    [Fact]
    public void TelemetrySpinIntegratesSpeedOverHostTime()
    {
        if (GameRoot.Find() is not { } root)
        {
            return;
        }

        using var menu = new MenuHost(root);
        var host = menu.Host;
        var (node, chassis) = BackgroundCar(host);
        var t = new HostCarTelemetry();
        node.Vel[2] = -10f; // 10 m/s along the car's heading (-Z, yaw 0)
        t.Get(host, node, chassis, 0f);
        host.RunFor(0.5f);
        var a = t.Get(host, node, chassis, 0f)!;
        var radius = host.Parts.ChassisOf(chassis).Wheels[0].Radius;
        Assert.True(radius > 0.1f);
        var expected = 10f * 0.5f / radius;
        Assert.True(MathF.Abs(a.Wheels[0].Spin - expected) < expected * 0.1f, $"spin {a.Wheels[0].Spin}, expected about {expected}");
        host.Parts.ChassisOf(chassis).Telemetry.SetFloat(ChassisTelemetry.CurrentSteering1, 1f);
        var b = t.Get(host, node, chassis, 0f)!;
        Assert.True(b.Wheels[0].Steer > 0.4f && b.Wheels[2].Steer == 0f);
        node.Vel[2] = 0;
    }
}
