using System.Numerics;
using System.Text;
using Slrr.Formats.Cfg;
using Slrr.Render.Content;
using Slrr.Render.Geometry;
using Slrr.Render.Overlay;
using Slrr.Render.Scene;
using Xunit;

namespace Slrr.Render.Tests;

public class PureTests
{
    [Theory]
    [InlineData(0.001f, 0)]
    [InlineData(0.01f, 1)]
    [InlineData(0.1f, 2)]
    [InlineData(5f, 2)]
    public void LodLevelCountsThresholdsBelowTheProjectedSize(float fraction, int level)
    {
        var rim = new LodSet([0.005f, 0.025f, 2.0f]);
        Assert.Equal(level, rim.Level(fraction, 2));
    }

    [Fact]
    public void FrustumKeepsBoxesInFrontAndCullsBoxesBehind()
    {
        var cam = new Camera { Position = new Vector3(0, 0, 10), Target = Vector3.Zero, Near = 0.1f, Far = 100 };
        var f = new Frustum(cam.ViewProjection(1));
        Assert.True(f.Intersects(new Vector3(-1), new Vector3(1)));
        Assert.False(f.Intersects(new Vector3(-1, -1, 20), new Vector3(1, 1, 22)));
        Assert.False(f.Intersects(new Vector3(50, -1, -1), new Vector3(52, 1, 1)));
        Assert.False(f.Intersects(new Vector3(-1, -1, -200), new Vector3(1, 1, -150)));
        Assert.True(f.Intersects(new Vector3(0, 0, -50), 1f));
    }

    [Fact]
    public void TransformedBoxContainsTheRotatedCorners()
    {
        var m = Matrix4x4.CreateRotationY(0.7f) * Matrix4x4.CreateTranslation(3, 0, 0);
        var (min, max) = Frustum.Transform(new Vector3(-1, -2, -3), new Vector3(1, 2, 3), m);
        foreach (var x in new[] { -1f, 1f })
        {
            foreach (var z in new[] { -3f, 3f })
            {
                var p = Vector3.Transform(new Vector3(x, 2, z), m);
                Assert.True(p.X >= min.X - 1e-4 && p.X <= max.X + 1e-4 && p.Z >= min.Z - 1e-4 && p.Z <= max.Z + 1e-4);
            }
        }
    }

    [Fact]
    public void CfgRenderLinesLodsSlotsAndWheelsAreParsed()
    {
        const string text = """
            render		0x0000008E	0.000 0.000 0.000	0.000 0.000 -3.142
            lod	2 2 //hires
            render		0x000004d4	0.000 0.000 0.000	0.000 0.000 -3.142
            lod	1 1
            lods 3 0.005 0.025 2.000
            # FL wheel #
            type		10.000 sphere	0.315
            wheel		-0.698 -0.616 -1.351	0.000 0.000 0.000	1.000 0.000 1.000 0.000
            slot		0.000 0.000 0.000	3.142 0.000 0.000	101	; FL wheel
            attach		0x00040200 1
            slot		-0.825 -0.101 -0.832	0.000 0.000 0.000	5	; FL_door
            stockpart 0x000000A1 // "L headlights" //
            eof
            """;
        var info = CfgRenderInfo.Parse(CfgDocument.Parse(Encoding.Latin1.GetBytes(text), "t.cfg"));
        Assert.Equal(2, info.Renders.Count);
        Assert.Equal((2, 2), (info.Renders[0].LodMin, info.Renders[0].LodMax));
        Assert.Equal(0x4D4u, info.Renders[1].Id);
        Assert.Equal([0.005f, 0.025f, 2.0f], info.Lods!.Thresholds);
        var wheel = Assert.Single(info.Wheels);
        Assert.Equal(0.315f, wheel.Radius);
        Assert.Equal(0, info.Slots.Single(s => s.SlotId == 101).WheelIndex);
        Assert.Equal((0x00040200u, 1), info.Slots.Single(s => s.SlotId == 101).Attach.Single());
        Assert.Null(info.Slots.Single(s => s.SlotId == 5).WheelIndex);
        Assert.Equal(0xA1u, info.StockParts.Single());

        // Yaw pi (right-wheel slots) turns +X into -X; roll -pi (rim render lines) turns +Y into -Y.
        var yaw = CfgRenderInfo.Ypr(new Vector3(MathF.PI, 0, 0));
        Assert.True(Vector3.Transform(Vector3.UnitX, yaw).X < -0.99f);
        var roll = CfgRenderInfo.Ypr(new Vector3(0, 0, -MathF.PI));
        Assert.True(Vector3.Transform(Vector3.UnitY, roll).Y < -0.99f);
    }

    [Fact]
    public void MeshBuilderSkipsBadMeshesAndGeneratesNormals()
    {
        Vector3[] p = [Vector3.Zero, Vector3.UnitX, Vector3.UnitZ];
        var good = new SourceMesh(p, null, new Vector2[3], new Vector2[3], null, [0, 2, 1], MaterialDesc.Default);
        var badIndex = new SourceMesh(p, null, new Vector2[3], new Vector2[3], null, [0, 1, 7], MaterialDesc.Default);
        var nan = new SourceMesh([new Vector3(float.NaN), Vector3.UnitX, Vector3.UnitZ], null, new Vector2[3], new Vector2[3], null, [0, 1, 2], MaterialDesc.Default);
        var mesh = MeshBuilder.Build([good, badIndex, nan]);
        Assert.Equal(2, mesh.SkippedMeshes);
        Assert.Equal(3, mesh.VertexCount);
        Assert.Equal(3, mesh.GeneratedNormals);
        Assert.Equal(1f, mesh.Vertices[4], 0.001f); // normal y of vertex 0: (0,0,0),(0,0,1),(1,0,0) is CCW seen from +Y
        Assert.Single(mesh.SubMeshes);
    }

    [Fact]
    public void OverlayQuadsMapOsdCoordinatesToPixelsAndSortByDepth()
    {
        var frame = new OverlayFrame();
        frame.Quads.Add(new OverlayQuad(0, -0.95f, 2, 0.1f, "header", Vector4.One, -0.001f, "header"));
        frame.Quads.Add(new OverlayQuad(0, 0, 2, 2, "bg", Vector4.One, -0.002f, "bg"));
        var sprites = OverlayLayout.Layout(frame, 800, 600, _ => null);
        Assert.Equal(["bg", "header"], sprites.Select(s => s.Texture));
        Assert.Equal(new Vector2(0, 0), sprites[0].P0);
        Assert.Equal(new Vector2(800, 600), sprites[0].P1);
        Assert.Equal(0f, sprites[1].P0.Y, 0.001f);
        Assert.Equal(30f, sprites[1].P1.Y, 0.001f);
    }

    [Fact]
    public void ArgbColoursUnpack()
    {
        Assert.Equal(new Vector4(1, 203 / 255f, 0, 1), OverlayLayout.Argb(unchecked((int)0xFFFFCB00)));
        Assert.Equal(0x70 / 255f, OverlayLayout.Argb(1891680448).W, 0.0001f);
    }

    [Fact]
    public void ResRefComparesArchivesCaseInsensitively()
    {
        Assert.Equal(new ResRef("cars\\racers.rpk", 6), new ResRef("Cars\\Racers.RPK", 6));
        Assert.Equal(new ResRef("a.rpk", 1).GetHashCode(), new ResRef("A.RPK", 1).GetHashCode());
        byte[] icfg = [.. "ICFG"u8, 13, 0, 0, 0, .. "object"u8, 0, .. "bounds"u8];
        Assert.Equal("object\nbounds", Encoding.Latin1.GetString(GameContent.Unwrap(icfg)));
    }
}
