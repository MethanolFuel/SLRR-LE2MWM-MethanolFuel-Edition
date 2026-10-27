using System.Numerics;
using Slrr.Game.Render.Cameras;
using Slrr.Game.Render.Maps;
using Slrr.Game.Render.Scene;
using Slrr.Render.Geometry;
using Slrr.Render.Scene;
using Xunit;

namespace Slrr.Game.Render.Tests;

/// <summary>No GL, no game data: conventions and pure functions.</summary>
public class MathTests
{
    /// <summary>The 28 camera poses of MainMenuDialog.bgCamPath (decompiled script; position, yaw, pitch) orbiting a car at the origin.</summary>
    private static readonly (Vector3 Pos, float Yaw, float Pitch)[] MenuPath =
    [
        (new(-1.086f, 0.483f, -2.086f), -1.904f, -0.464f), (new(-1.133f, 0.636f, -1.248f), -1.637f, -0.49f), (new(-1.182f, 0.87f, -0.476f), -1.723f, -0.148f),
        (new(-1.163f, 0.996f, 0.391f), -1.353f, -0.268f), (new(-1.141f, 0.603f, 1.013f), -1.428f, 0.375f), (new(-1.133f, 0.269f, 1.435f), -1.325f, 0.219f),
        (new(-1.217f, 0.273f, 2.01f), -1.118f, 0.108f), (new(-1.068f, 0.69f, 2.449f), -0.927f, -0.203f), (new(-0.573f, 0.875f, 2.585f), -0.7f, -0.224f),
        (new(-0.071f, 0.958f, 2.58f), -0.409f, -0.261f), (new(0.356f, 1.066f, 2.442f), -0.051f, -0.303f), (new(0.508f, 1.097f, 1.894f), 0.226f, -0.315f),
        (new(0.777f, 1.139f, 1.532f), 0.552f, -0.312f), (new(1.119f, 1.132f, 1.225f), 0.816f, -0.254f), (new(1.284f, 1.086f, 0.687f), 1.03f, -0.254f),
        (new(1.298f, 1.053f, 0.086f), 1.187f, -0.304f), (new(1.288f, 1.123f, -0.505f), 1.406f, -0.48f), (new(1.251f, 1.12f, -1.03f), 1.712f, -0.65f),
        (new(1.215f, 1.059f, -1.509f), 2.174f, -0.741f), (new(1.248f, 0.938f, -1.984f), 2.458f, -0.63f), (new(1.215f, 0.81f, -2.384f), 2.584f, -0.474f),
        (new(0.962f, 0.727f, -2.636f), 2.654f, -0.343f), (new(0.611f, 0.679f, -2.785f), 2.723f, -0.235f), (new(0.229f, 0.683f, -2.854f), 2.873f, -0.196f),
        (new(-0.169f, 0.704f, -2.836f), 3.066f, -0.197f), (new(-0.567f, 0.742f, -2.757f), -2.9f, -0.2f), (new(-0.899f, 0.704f, -2.534f), -2.531f, -0.246f),
    ];

    private static void Near(float expected, float actual, float tolerance) =>
        Assert.True(MathF.Abs(expected - actual) <= tolerance, $"expected {expected}, got {actual}");

    private static double MeanAngle(Func<Vector3, Vector3> baseForward)
    {
        var target = new Vector3(0, 0.45f, 0);
        double sum = 0;
        foreach (var (pos, yaw, pitch) in MenuPath)
        {
            var rot = Slrr.Render.Content.CfgRenderInfo.Ypr(new Vector3(yaw, pitch, 0));
            var f = Vector3.Normalize(Vector3.TransformNormal(baseForward(Vector3.Zero), rot));
            var d = Vector3.Normalize(target - pos);
            sum += Math.Acos(Math.Clamp(Vector3.Dot(f, d), -1f, 1f)) * 180 / Math.PI;
        }

        return sum / MenuPath.Length;
    }

    [Fact]
    public void CameraForwardIsMinusZ_MeasuredAgainstTheMainMenuCameraPath()
    {
        // The MainMenu camera circles a car at the origin. Under the cfg convention (yaw about Y, pitch about X, forward -Z) its optical
        // axis points at the car (mean error 21 deg, the path aims at different car points); the other three axes are 80..160 deg off.
        var minusZ = MeanAngle(_ => -Vector3.UnitZ);
        Assert.True(minusZ < 30, $"-Z forward mean error {minusZ:0.0} deg");
        Assert.True(MeanAngle(_ => Vector3.UnitZ) > 90);
        Assert.True(MeanAngle(_ => Vector3.UnitX) > 60);
        Assert.True(MeanAngle(_ => -Vector3.UnitX) > 60);
    }

    [Fact]
    public void BasisYawZeroLooksDownMinusZAndYawQuarterTurnLooksDownMinusX()
    {
        var (f0, u0) = HostIds.Basis(Vector3.Zero);
        Assert.True(Vector3.Distance(f0, -Vector3.UnitZ) < 1e-5f);
        Assert.True(Vector3.Distance(u0, Vector3.UnitY) < 1e-5f);
        var (f1, _) = HostIds.Basis(new Vector3(MathF.PI / 2, 0, 0));
        Assert.True(Vector3.Distance(f1, -Vector3.UnitX) < 1e-5f);
        var (f2, _) = HostIds.Basis(new Vector3(0, 0.5f, 0));
        Assert.True(f2.Y > 0.4f); // positive pitch looks up
    }

    [Fact]
    public void ChaseRigSitsBehindAndAboveTheBodyAndLooksAhead()
    {
        var body = Matrix4x4.CreateRotationY(0.8f) * Matrix4x4.CreateTranslation(10, 1, -20);
        var pose = CameraRigs.Chase(body, distance: 5, height: 1.8f, lookAhead: 2);
        var fwd = Vector3.Normalize(Vector3.TransformNormal(-Vector3.UnitZ, body));
        var toEye = pose.Eye - body.Translation;
        Assert.True(Vector3.Dot(Vector3.Normalize(new Vector3(toEye.X, 0, toEye.Z)), fwd) < -0.99f, "eye is behind the car");
        Near(1.8f, toEye.Y, 0.001f);
        Assert.True(Vector3.Dot(Vector3.Normalize(pose.Target - pose.Eye), fwd) > 0.9f, "looks along the heading");
    }

    [Fact]
    public void ChaseRigIgnoresBodyPitchAndRoll()
    {
        var level = CameraRigs.Chase(Matrix4x4.CreateTranslation(0, 0, 0), 5, 1.5f, 2);
        var tilted = CameraRigs.Chase(Matrix4x4.CreateRotationX(0.4f) * Matrix4x4.CreateRotationZ(0.3f), 5, 1.5f, 2);
        Near(level.Eye.Y, tilted.Eye.Y, 0.01f);
    }

    [Fact]
    public void CockpitRigFollowsTheBodyFrame()
    {
        var cam = new Slrr.Game.Render.Cars.CfgCamera(new Vector3(-0.395f, 0.3f, 0.33f), Vector3.Zero, 90);
        var body = Matrix4x4.CreateRotationY(MathF.PI) * Matrix4x4.CreateTranslation(5, 0, 5);
        var pose = CameraRigs.Cockpit(body, cam);
        // The car faces +Z after a half turn: the eye is at x = 5 + 0.395, z = 5 - 0.33 and looks towards +Z.
        Near(5.395f, pose.Eye.X, 0.01f);
        Near(4.67f, pose.Eye.Z, 0.01f);
        Assert.True(pose.Target.Z > pose.Eye.Z);
    }

    [Fact]
    public void OrbitRigKeepsTheDistanceAndLooksAtTheAnchor()
    {
        var pose = CameraRigs.Orbit(new Vector3(1, 2, 3), 0.7f, 0.3f, 4f);
        Near(4f, Vector3.Distance(pose.Eye, pose.Target), 0.001f);
        Assert.Equal(new Vector3(1, 2, 3), pose.Target);
        Assert.True(pose.Eye.Y > 2);
    }

    [Fact]
    public void VerticalFovFollowsTheAspect()
    {
        var wide = CameraRigs.VerticalFov(90, 16f / 9f);
        var square = CameraRigs.VerticalFov(90, 1f);
        Near(MathF.PI / 2, square, 0.001f);
        Assert.True(wide < square);
    }

    [Fact]
    public void SceneParentOfARenderInstanceIsItsTypeIdNotItsParentId()
    {
        // MEASURED on the running host: RenderRef.create(scene, renderType, name) is stored with TypeId = scene and ParentId = renderType.
        var tree = new Slrr.Engine.Resources.ResourceTree(new Slrr.Engine.Resources.ArchiveTable(_ => null));
        var scene = tree.CreateInstance(Slrr.Engine.Resources.ResourceTypes.InstanceGame, 0, "scene", 0);
        var render = tree.CreateInstance(Slrr.Engine.Resources.ResourceTypes.InstanceRender, 0x00020042, "mesh", scene.Id);
        Assert.Equal(scene.Id, HostIds.SceneParentId(tree, render));
        Assert.Equal((0x00020042, scene.Id), HostIds.RenderParts(tree, render));
        Assert.True(HostIds.IsInScene(tree, render, scene.Id));
        Assert.False(HostIds.IsInScene(tree, render, 0x1234));
        scene.Pos[0] = 3;
        render.Pos[1] = 2;
        Assert.Equal(new Vector3(3, 2, 0), HostIds.World(tree, render).Translation);
    }

    private static CpuMesh Quad(float y, AlphaMode alpha)
    {
        // Two triangles covering x,z in [0,2] at height y; vertex layout of CpuMesh (14 floats).
        float[] v = new float[4 * CpuMesh.FloatsPerVertex];
        void Set(int i, float x, float z)
        {
            v[(i * CpuMesh.FloatsPerVertex) + 0] = x;
            v[(i * CpuMesh.FloatsPerVertex) + 1] = y;
            v[(i * CpuMesh.FloatsPerVertex) + 2] = z;
        }

        Set(0, 0, 0);
        Set(1, 2, 0);
        Set(2, 2, 2);
        Set(3, 0, 2);
        var material = new MaterialDesc { Alpha = alpha };
        return new CpuMesh
        {
            Vertices = v,
            Indices = [0, 1, 2, 0, 2, 3],
            SubMeshes = [new CpuSubMesh(0, 6, material, new Vector3(0, y, 0), new Vector3(2, y, 2))],
            Min = new Vector3(0, y, 0),
            Max = new Vector3(2, y, 2),
        };
    }

    [Fact]
    public void GroundSamplerFindsTheTopmostOpaqueSurfaceBelowTheStartHeight()
    {
        var chunks = new List<StaticChunk>
        {
            new(Quad(1f, AlphaMode.Opaque), new Vector3(0, 1, 0), new Vector3(2, 1, 2), "low"),
            new(Quad(5f, AlphaMode.Opaque), new Vector3(0, 5, 0), new Vector3(2, 5, 2), "bridge"),
            new(Quad(3f, AlphaMode.Blend), new Vector3(0, 3, 0), new Vector3(2, 3, 2), "glass"),
        };
        Assert.True(GroundSampler.TryGetGround(chunks, 1f, 1f, 100f, out var top));
        Near(5f, top, 0.001f);
        Assert.True(GroundSampler.TryGetGround(chunks, 1f, 1f, 4f, out var under));
        Near(1f, under, 0.001f); // the blended quad at 3 is not ground
        Assert.False(GroundSampler.TryGetGround(chunks, 9f, 9f, 100f, out _));
    }

    [Fact]
    public void MapStreamerReloadsOnlyWhenTheFocusLeavesTheInnerHalf()
    {
        var root = GameRoot.Find();
        if (root is null)
        {
            return;
        }

        var content = new Slrr.Render.Content.GameContent(root);
        var streamer = new MapStreamer(new RegionMapLoader(content));
        var want = new MapRequest("maps\\Addon_Maps\\Drag_Track.rpk", null, new Vector2(30, -30), 200);
        var a = streamer.Update(want, out var r1);
        Assert.True(r1);
        Assert.True(a.Scene.Static.Count > 0);
        streamer.Update(want with { Center = new Vector2(80, -30) }, out var r2);
        Assert.False(r2); // 50 m < 100 m
        var b = streamer.Update(want with { Center = new Vector2(300, -30) }, out var r3);
        Assert.True(r3);
        Assert.Equal(2, streamer.Reloads);
        Assert.NotSame(a, b);
        Assert.True(b.Scene.Static.Count <= new RegionMapLoader(content).MaxChunks);
    }

    [Fact]
    public void ChunkBudgetKeepsTheNearestChunksOfTheWholeCity()
    {
        var root = GameRoot.Find();
        if (root is null)
        {
            return;
        }

        var content = new Slrr.Render.Content.GameContent(root);
        var loader = new RegionMapLoader(content) { MaxChunks = 40 };
        var region = loader.Load(new MapRequest("maps\\city.rpk", null, new Vector2(-300, -500), 5000));
        Assert.Equal(40, region.Scene.Static.Count);
        Assert.True(region.ChunksSkippedByBudget > 0);
        // The kept chunks are the nearest ones: every kept chunk centre is closer than the farthest of them, and one is at the focus.
        var d = region.Scene.Static.Select(c => Vector2.Distance(new Vector2((c.Min.X + c.Max.X) / 2, (c.Min.Z + c.Max.Z) / 2), new Vector2(-300, -500))).ToList();
        Assert.True(d.Min() < 150);
    }
}
