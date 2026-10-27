using System.Numerics;
using Slrr.Formats.Scx;
using Slrr.Render.Cars;
using Slrr.Render.Content;
using Slrr.Render.Geometry;
using Slrr.Render.Gl;
using Slrr.Render.Maps;
using Slrr.Render.Overlay;
using Slrr.Render.Scene;
using Xunit;

namespace Slrr.Render.Tests;

/// <summary>Tests on the shipped content; they pass vacuously without game data (see docs/DEVELOPMENT.md).</summary>
public class CorpusTests
{
    private static readonly Lazy<GameContent?> Shared = new(() => GameRoot.Find() is { } r ? new GameContent(r) : null);

    private const string Nonus = "cars\\racers\\Nonus.rpk";

    [Fact]
    public void RenderEntryResolvesMeshAndTexturesAcrossArchives()
    {
        if (Shared.Value is not { } c)
        {
            return;
        }

        var def = c.Render(new ResRef(Nonus, 0x39))!;
        Assert.Equal("cars\\racers\\Nonus_data\\meshes\\chassis.scx", def.MeshFile, ignoreCase: true);
        Assert.NotNull(def.MeshPath);
        Assert.Equal(4, def.TexturePaths.Count);
        Assert.EndsWith("default_white.png", def.TexturePaths[2]!, StringComparison.OrdinalIgnoreCase); // 0x00020025 -> cars.rpk
    }

    [Fact]
    public void ScxMeshesAreCentimetresAndWindingAgreesWithNormals()
    {
        if (Shared.Value is not { } c)
        {
            return;
        }

        var scx = c.Scx(c.Files.Resolve("parts\\wheels\\rims\\meshes\\rim MT_Mescaline 10.0 15 ET -70 LOD CATALOG GARAGE.scx"))!;
        var mesh = MeshBuilder.FromScx(scx, [], _ => false);
        Assert.Equal(19.05f, mesh.Max.Y, 0.01f); // 15" rim: radius 7.5 in = 19.05 cm
        var (agree, disagree) = MeshStats.Winding(c.Scx(c.Files.Resolve("cars\\racers\\Nonus_data\\meshes\\chassis.scx"))!);
        Assert.True(agree > 1000 && disagree == 0, $"{agree}/{disagree}");
    }

    [Fact]
    public void NonusAssemblesWithWheelsBelowTheBodyAndDoorOnItsSlot()
    {
        if (Shared.Value is not { } c)
        {
            return;
        }

        var asm = new CarAssembler(c);
        var car = asm.CarTypes(Nonus).Single(t => t.Cfg.Contains("Street_GT", StringComparison.OrdinalIgnoreCase)).Ref;
        var rim = asm.FindGametypeByCfg("parts\\wheels.rpk", "rim_Einvagen_GT_7_5_16_ET_20_LOD_CATALOG_GARAGE.cfg");
        var tyre = asm.FindGametypeByCfg("parts\\wheels.rpk", "tyre_195_50_16_7_5_LOD_CATALOG_GARAGE.cfg");
        Assert.NotNull(rim);
        Assert.NotNull(tyre);
        var a = asm.Assemble(car, rim, tyre);

        Assert.Equal(4, a.Wheels.Count);
        Assert.All(a.Wheels, w => Assert.True(w.Position.Y < -0.5f));
        Assert.True(a.Parts.Count >= 30, $"{a.Parts.Count} parts");
        Assert.Equal(8, a.Parts.Count(p => p.How == "explicit wheel slot")); // 4 rims + 4 tyres

        // FL_door: own slot 5 at (-0.105, 0, -0.745), car slot 5 at (-0.825, -0.101, -0.832).
        var door = a.Parts.Single(p => p.Name.Equals("FL_door", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(new Vector3(-0.72f, -0.101f, -0.087f), door.World.Translation, new Vec3Comparer(1e-3f));

        // Right wheels are yawed by pi: the rim's +X axis points to -X.
        var rr = a.Parts.Where(p => p.Parent == "wheel3" && p.Name.StartsWith("rim", StringComparison.OrdinalIgnoreCase)).Single();
        Assert.True(Vector3.TransformNormal(Vector3.UnitX, rr.World).X < -0.99f);
    }

    [Fact]
    public void FontMeshIsAGlyphTableIndexedByCharacterCode()
    {
        if (Shared.Value is not { } c)
        {
            return;
        }

        var font = FontData.FromScx(ScxFile.Read(c.Files.Resolve("frontend\\meshes\\Fonts\\sl14.scx")!));
        var a = font.Glyphs['A'];
        Assert.Equal((-20f, 0f, 80f, 220f), (a.X0, a.Y0, a.X1, a.Y1));
        Assert.Equal(0.3555f, a.Uv0.X, 0.001f);
        Assert.True(font.Glyphs['\0'].IsEmpty);
        Assert.Equal(60f, font.Glyphs[' '].Advance);
    }

    [Fact]
    public void DragTrackLoadsPolyGeometryWithItsScyMaterials()
    {
        if (Shared.Value is not { } c)
        {
            return;
        }

        var loader = new MapLoader(c);
        var scene = loader.Load("maps\\Addon_Maps\\Drag_Track.rpk", null);
        Assert.Equal(18, loader.Stats.Materials); // Ground.SCM: "materials 18"
        Assert.True(loader.Stats.TexturedMaterials > 10);
        Assert.True(scene.Static.Count > 0 && scene.Static.Count == loader.Stats.PolyChunks);
        Assert.Single(scene.Sky);
        Assert.True(loader.Stats.WindingAgree > 1000 && loader.Stats.WindingDisagree < loader.Stats.WindingAgree / 100);
    }

    [Fact]
    public void OffscreenFrameDrawsTheCarWhenGlIsAvailable()
    {
        if (Shared.Value is not { } c || GlHost.TryCreate(out _) is not { } host)
        {
            return; // no game data or no display (CI without xvfb): vacuous
        }

        using (host)
        {
            using var renderer = new GameRenderer(host.Gl, c);
            var def = c.Render(new ResRef(Nonus, 0x39))!;
            var scene = new SceneDescription { ClearColor = new Vector4(0, 0, 1, 1) };
            scene.Items.Add(new DrawItem(def, GameUnits.ScxScale, 0, int.MaxValue, null, "chassis"));
            var cam = new Camera { FovY = 0.8f };
            OrbitRig.Frame(Vector3.Zero, 2.3f, -2.3f, 0.3f, cam.FovY).Apply(cam);
            var frame = OffscreenRenderer.Render(host, renderer, scene, cam, null, 96, 72, 2);
            Assert.True(frame.First.DrawCalls > 0);
            var center = ((36 * 96) + 48) * 4;
            var px = frame.Image.Pixels.AsSpan(center, 4).ToArray();
            Assert.False(px[0] == 0 && px[1] == 0 && px[2] == 255, "centre pixel still shows the clear colour");
        }
    }

    private sealed class Vec3Comparer(float eps) : IEqualityComparer<Vector3>
    {
        public bool Equals(Vector3 x, Vector3 y) => Vector3.Distance(x, y) <= eps;

        public int GetHashCode(Vector3 obj) => 0;
    }
}

internal static class GameRoot
{
    public static string? Find()
    {
        var env = Environment.GetEnvironmentVariable("SLRR_GAME_ROOT");
        if (!string.IsNullOrEmpty(env) && File.Exists(Path.Combine(env, "sl.rpk")))
        {
            return env;
        }

        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Slrr.sln")) && File.Exists(Path.Combine(dir.FullName, "sl.rpk")))
            {
                return dir.FullName;
            }
        }

        return null;
    }
}
