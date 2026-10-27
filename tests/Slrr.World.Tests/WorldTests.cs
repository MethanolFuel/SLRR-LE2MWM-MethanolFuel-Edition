using System.Numerics;
using Slrr.World;
using Xunit;

namespace Slrr.World.Tests;

internal static class Approx
{
    public static void Eq(float expected, float actual, int digits = 3) =>
        Assert.True(MathF.Abs(expected - actual) <= MathF.Pow(10, -digits), $"expected {expected}, got {actual}");
}

public class SyntheticWorldTests
{
    private static CollisionWorld Build(Action<TriangleSoupBuilder> fill, int surfaces = 4)
    {
        var soup = new TriangleSoupBuilder();
        fill(soup);
        return new CollisionWorld(soup, SurfaceTable.Anonymous(surfaces));
    }

    private static void Quad(TriangleSoupBuilder s, Vector3 a, Vector3 b, Vector3 c, Vector3 d, int surface, int source = 1)
    {
        // a,b,c,d counter-clockwise seen from +Y => upward normal when a->b->c turns left in XZ
        s.Add(a, b, c, Vector3.Cross(b - a, c - a), surface, source);
        s.Add(a, c, d, Vector3.Cross(c - a, d - a), surface, source);
    }

    private static void Floor(TriangleSoupBuilder s, float y, float half, int surface) =>
        Quad(s, new(-half, y, half), new(half, y, half), new(half, y, -half), new(-half, y, -half), surface);

    [Fact]
    public void FlatFloorReturnsHeightNormalAndSurface()
    {
        var w = Build(s => Floor(s, 2f, 50f, 3));
        Assert.True(w.TryGetGround(10, -20, float.PositiveInfinity, out var h));
        Approx.Eq(2f, h.Position.Y, 4);
        Approx.Eq(1f, h.Normal.Y, 4);
        Assert.Equal(3, h.Surface);
        Assert.False(w.TryGetGround(60, 0, float.PositiveInfinity, out _));
        Assert.False(w.TryGetGround(0, 0, 1.0f, out _)); // start below the floor
    }

    [Fact]
    public void RampInterpolatesHeight()
    {
        // plane y = 0.1 * x over x in [0, 100]
        var w = Build(s => Quad(s, new(0, 0, 10), new(100, 10, 10), new(100, 10, -10), new(0, 0, -10), 0));
        foreach (var x in new[] { 1f, 25f, 50f, 99f })
        {
            Assert.True(w.TryGetGround(x, 3, float.PositiveInfinity, out var h));
            Approx.Eq(0.1f * x, h.Position.Y, 3);
        }
    }

    [Fact]
    public void StackedLayersRespectStartHeight()
    {
        var w = Build(s =>
        {
            Floor(s, 0f, 20f, 0);
            Floor(s, 8f, 5f, 1); // bridge deck
        });
        Assert.True(w.TryGetGround(0, 0, float.PositiveInfinity, out var top));
        Approx.Eq(8f, top.Position.Y, 4);
        Assert.Equal(1, top.Surface);
        Assert.True(w.TryGetGround(0, 0, 7f, out var under));
        Approx.Eq(0f, under.Position.Y, 4);
        Assert.True(w.TryGetGround(0, 0, 8.5f, out var onDeck));
        Approx.Eq(8f, onDeck.Position.Y, 4);
        Approx.Eq(0.5f, onDeck.Distance, 3);
    }

    [Fact]
    public void CeilingsAndWallsAreNotGround()
    {
        var w = Build(s =>
        {
            // downward-facing triangle (clockwise seen from above); the stored normal is replaced by the geometric one
            s.Add(new(-5, 3, -5), new(5, 3, -5), new(5, 3, 5), Vector3.Zero, 0, 1);
        });
        Assert.False(w.TryGetGround(0, 0, float.PositiveInfinity, out _));
    }

    [Fact]
    public void SharedEdgesLeaveNoCracks()
    {
        var w = Build(s => Floor(s, 0f, 10f, 0));
        var rng = new Random(1);
        for (var i = 0; i < 2000; i++)
        {
            // the diagonal is x == z; sample on and next to it
            var t = (float)((rng.NextDouble() * 18) - 9);
            Assert.True(w.TryGetGround(t, t, float.PositiveInfinity, out _), $"crack at {t}");
        }
    }

    [Fact]
    public void GridAndBvhAgreeOnRandomTerrain()
    {
        var rng = new Random(7);
        var w = Build(s =>
        {
            for (var i = 0; i < 4000; i++)
            {
                var c = new Vector3((float)(rng.NextDouble() * 400), (float)(rng.NextDouble() * 30), (float)(rng.NextDouble() * 400));
                var a = c + new Vector3((float)rng.NextDouble() * 6, 0, 0);
                var b = c + new Vector3(0, (float)rng.NextDouble(), 7);
                var cc = c + new Vector3(-5, (float)rng.NextDouble(), -3);
                var n = Vector3.Cross(b - a, cc - a);
                if (n.Y < 0)
                {
                    (b, cc) = (cc, b);
                    n = -n;
                }

                s.Add(a, b, cc, Vector3.Normalize(n), i % 4, i);
            }
        });
        var bad = 0;
        for (var i = 0; i < 20000; i++)
        {
            var x = (float)(rng.NextDouble() * 400);
            var z = (float)(rng.NextDouble() * 400);
            var from = (float)(rng.NextDouble() * 40);
            var a = w.TryGetGround(x, z, from, out var ha);
            var b = w.TryGetGroundBvh(x, z, from, out var hb);
            if (a != b || (a && MathF.Abs(ha.Position.Y - hb.Position.Y) > 1e-3f))
            {
                bad++;
            }
        }

        Assert.True(bad <= 2, $"{bad} disagreements"); // edge-tolerance differences only
    }

    [Fact]
    public void RaycastHitsBoxFromOutsideAndInsideWithFacingNormal()
    {
        var w = Build(s => WorldLoader.AddBox(s, new Vector3(1, 2, 3), Matrix4x4.CreateTranslation(10, 0, 0), 5));
        Assert.True(w.Raycast(new Vector3(0, 0.5f, 0.2f), new Vector3(2, 0, 0), 100, out var h));
        Approx.Eq(9f, h.Position.X, 3);
        Approx.Eq(9f, h.Distance, 3);
        Approx.Eq(-1f, h.Normal.X, 3); // faces the ray origin
        Assert.Equal(SurfaceTable.ObjectSurfaceId, w.SurfaceOf(h.Triangle));
        Assert.Equal(5, h.Source);
        Assert.False(w.Raycast(new Vector3(0, 0, 0), new Vector3(-1, 0, 0), 100, out _));
        Assert.False(w.Raycast(new Vector3(0, 0.5f, 0.2f), new Vector3(1, 0, 0), 5, out _)); // too short
        Assert.True(w.Raycast(new Vector3(10, 0, 0), new Vector3(0, 1, 0), 100, out var inside));
        Approx.Eq(2f, inside.Position.Y, 3);
        Assert.True(inside.Normal.Y < 0); // flipped to face the origin inside the box
    }

    [Fact]
    public void BoxTrianglesFaceOutward()
    {
        var soup = new TriangleSoupBuilder();
        WorldLoader.AddBox(soup, new Vector3(1, 2, 3), Matrix4x4.CreateRotationY(0.7f) * Matrix4x4.CreateTranslation(5, 1, -2), 1);
        Assert.Equal(12, soup.Count);
        var w = new CollisionWorld(soup, SurfaceTable.Anonymous(1));
        var center = new Vector3(5, 1, -2);
        for (var t = 0; t < w.TriangleCount; t++)
        {
            var face = (w.Vertex(t, 0) + w.Vertex(t, 1) + w.Vertex(t, 2)) / 3f;
            Assert.True(Vector3.Dot(w.Normal(t), face - center) > 0);
        }
    }

    [Fact]
    public void SphereAndCapsuleOverlapReportDepthAndDirection()
    {
        var w = Build(s => Floor(s, 0f, 50f, 2));
        var list = new List<Contact>();
        Assert.Equal(0, w.OverlapSphere(new Vector3(0, 1.0f, 0), 0.5f, list));
        Assert.True(w.OverlapSphere(new Vector3(0, 0.3f, 0), 0.5f, list) > 0);
        var c = list[0];
        Approx.Eq(0.2f, c.Depth, 3);
        Approx.Eq(1f, c.Normal.Y, 3);
        Assert.Equal(2, c.Surface);

        list.Clear();
        // upright capsule standing on the floor with its lower sphere penetrating by 0.1
        Assert.True(w.OverlapCapsule(new Vector3(0, 0.4f, 0), new Vector3(0, 1.5f, 0), 0.5f, list) > 0);
        Approx.Eq(0.1f, list.Max(x => x.Depth), 3);
        list.Clear();
        // horizontal capsule lying inside the floor plane: zero distance, pushed along the normal
        Assert.True(w.OverlapCapsule(new Vector3(-3, 0f, 0), new Vector3(3, 0f, 0), 0.25f, list) > 0);
        Approx.Eq(0.25f, list.Max(x => x.Depth), 3);
    }

    [Fact]
    public void DegenerateTrianglesAreDropped()
    {
        var soup = new TriangleSoupBuilder();
        soup.Add(Vector3.Zero, Vector3.Zero, Vector3.UnitX, Vector3.UnitY, 0, 0);
        soup.Add(Vector3.Zero, Vector3.UnitX, Vector3.UnitZ, Vector3.UnitY, 0, 0);
        Assert.Equal(1, soup.Count);
        Assert.Equal(1, soup.DegenerateDropped);
    }

    [Fact]
    public void EmptyWorldAnswersNoHits()
    {
        var w = new CollisionWorld(new TriangleSoupBuilder(), SurfaceTable.Anonymous(1));
        Assert.False(w.TryGetGround(0, 0, 10, out _));
        Assert.False(w.Raycast(Vector3.Zero, Vector3.UnitX, 10, out _));
        Assert.Equal(0, w.OverlapSphere(Vector3.Zero, 1, []));
    }

    [Fact]
    public void LargeTrianglesAreHandledByTheSeparateTree()
    {
        var w = Build(s =>
        {
            Floor(s, 0f, 5000f, 1); // two 10 km triangles
            for (var i = 0; i < 50; i++)
            {
                WorldLoader.AddBox(s, new Vector3(1, 1, 1), Matrix4x4.CreateTranslation(i * 5, 1, 0), 9);
            }
        });
        Assert.True(w.Raycast(new Vector3(0, 10, 100), -Vector3.UnitY, 100, out var h));
        Approx.Eq(10f, h.Distance, 3);
        Assert.True(w.TryGetGround(4000, 4000, float.PositiveInfinity, out var g));
        Approx.Eq(0f, g.Position.Y, 3);
        Assert.True(w.TryGetGround(10, 0, float.PositiveInfinity, out var onBox));
        Approx.Eq(2f, onBox.Position.Y, 3);
    }
}

public class SurfaceAndPlacementTests
{
    [Fact]
    public void TextScmParsesMaterialsAndProperties()
    {
        const string Scm = "materials 4\r\nmaterial 0 1 - Default\r\nmaterial 1 asfalt\r\nfriction\t0.8\r\nrollres\t\t2.0\r\nskid_SM\t\t0x00020047 //MW_Mod.rpk\\Grass Skid\r\nskid_SMcolor\t0xFF737373\r\nskid_PS\t\t0x00030104 //Particles.rpk\r\ncollcoeff\t15.0\r\n\r\nmaterial 2 Sand2\r\nmaterial 3 Trees alp\r\n// comment\r\n";
        var t = SurfaceTable.TryParse(System.Text.Encoding.Latin1.GetBytes(Scm));
        Assert.NotNull(t);
        Assert.Equal(4, t!.Count);
        var s = t.Get(1);
        Assert.Equal("asfalt", s.Name);
        Assert.Equal(SurfaceClass.Asphalt, s.Class);
        Assert.Equal(0.8f, s.Friction);
        Assert.Equal(2.0f, s.RollResistance);
        Assert.Equal(15.0f, s.CollisionCoeff);
        Assert.Equal(0x00020047u, s.SkidMarkResource);
        Assert.Equal(0xFF737373u, s.SkidMarkColor);
        Assert.Equal(0x00030104u, s.SkidParticleResource);
        Assert.True(s.HasExplicitProperties);
        Assert.False(t.Get(0).HasExplicitProperties);
        Assert.Equal(SurfaceInfo.DefaultFriction, t.Get(2).Friction);
        Assert.Equal(SurfaceClass.Foliage, t.Get(3).Class);
        Assert.Equal(SurfaceClass.Object, t.Get(SurfaceTable.ObjectSurfaceId).Class);
        Assert.Equal("<unknown 99>", t.Get(99).Name);
    }

    [Fact]
    public void BinaryScmParses()
    {
        var bytes = new List<byte>();
        bytes.AddRange(BitConverter.GetBytes(2u));
        bytes.AddRange(BitConverter.GetBytes(36u));
        foreach (var name in new[] { "_p_beton", "_p_asfalt" })
        {
            bytes.AddRange([0x01, 0x02, 0x03, 0x00]);
            var n = new byte[32];
            System.Text.Encoding.ASCII.GetBytes(name, n);
            bytes.AddRange(n);
        }

        var t = SurfaceTable.TryParse(bytes.ToArray());
        Assert.NotNull(t);
        Assert.Equal(2, t!.Count);
        Assert.Equal("_p_asfalt", t.Get(1).Name);
        Assert.Equal(SurfaceClass.Asphalt, t.Get(1).Class);
        Assert.Null(SurfaceTable.TryParse(new byte[] { 1, 2, 3 }));
    }

    [Fact]
    public void DefaultYawRotatesAboutYAndMatchesNumericsYpr()
    {
        var m = Placement.Rotation(MathF.PI / 2, 0, 0);
        var p = Vector3.Transform(Vector3.UnitX, m);
        Approx.Eq(0f, p.X, 4);
        Approx.Eq(0f, p.Y, 4);
        Approx.Eq(-1f, p.Z, 4);
        var abc = Placement.Rotation(0.4f, -0.3f, 0.2f);
        Approx.Eq(Matrix4x4.CreateFromYawPitchRoll(0.4f, -0.3f, 0.2f).M11, abc.M11, 5);
        Approx.Eq(Matrix4x4.CreateFromYawPitchRoll(0.4f, -0.3f, 0.2f).M23, abc.M23, 5);
        var t = Placement.Transform(new Vector3(1, 2, 3), 0, 0, 0);
        Assert.Equal(new Vector3(1, 2, 3), Vector3.Transform(Vector3.Zero, t));
    }

    [Fact]
    public void HeightMapPngHasValidHeader()
    {
        var soup = new TriangleSoupBuilder();
        soup.Add(new(-10, 1, 10), new(10, 1, 10), new(10, 1, -10), Vector3.UnitY, 0, 0);
        soup.Add(new(-10, 1, 10), new(10, 1, -10), new(-10, 1, -10), Vector3.UnitY, 0, 0);
        var w = new CollisionWorld(soup, SurfaceTable.Anonymous(1));
        var map = HeightMap.Sample(w, -20, -20, 20, 20, 1f);
        Assert.Equal(40, map.Width);
        Assert.Equal(400, map.Covered);
        var png = map.ToPng();
        Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, png[..8]);
        Assert.Equal(40, (png[16] << 24) | (png[17] << 16) | (png[18] << 8) | png[19]);
        Assert.Contains("<svg", map.ToSvg());
    }

    [Fact]
    public void ParsePolyIsZeroCopyAndBounded()
    {
        var ms = new MemoryStream();
        var bw = new BinaryWriter(ms);
        bw.Write(1u);
        bw.Write(1u); // one submesh
        bw.Write(7u); // material
        bw.Write(3u); // vertices
        bw.Write(1u); // triangles
        for (var v = 0; v < 3; v++)
        {
            bw.Write((float)v);
            bw.Write(0f);
            bw.Write(0f);
            for (var k = 0; k < 11; k++)
            {
                bw.Write(0u);
            }
        }

        bw.Write(0u);
        bw.Write(1u);
        bw.Write(2u);
        var subs = RenderScene.ParsePoly(ms.ToArray());
        var s = Assert.Single(subs);
        Assert.Equal(7, s.Material);
        Assert.Equal(1, s.TriangleCount);
        Assert.Equal(new Vector3(2, 0, 0), s.Position(2));
        // truncated payload must not throw
        Assert.Empty(RenderScene.ParsePoly(ms.ToArray()[..30]));
    }
}
