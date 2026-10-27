using System.Numerics;
using Slrr.Formats.Scx;
using Xunit;

namespace Slrr.Viewer.Tests;

public class MeshDataTests
{
    private static Vector3 Pos(MeshData m, int v) => new(m.Vertices[v * 6], m.Vertices[(v * 6) + 1], m.Vertices[(v * 6) + 2]);

    private static Vector3 Nrm(MeshData m, int v) => new(m.Vertices[(v * 6) + 3], m.Vertices[(v * 6) + 4], m.Vertices[(v * 6) + 5]);

    /// <summary>v3 file: one mesh, 56-byte material, 44-byte vertices (pos, normal, uv, 8 bytes, colour).</summary>
    private static byte[] V3Triangle(Vector3 normal)
    {
        var w = new BinaryWriter(new MemoryStream());
        w.Write("INVO"u8);
        w.Write(3u);
        w.Write(56u);
        w.Write(new byte[52]);
        w.Write(3u);
        Vector3[] p = [new(0, 0, 0), new(2, 0, 0), new(0, 2, 0)];
        foreach (var v in p)
        {
            w.Write(v.X); w.Write(v.Y); w.Write(v.Z);
            w.Write(normal.X); w.Write(normal.Y); w.Write(normal.Z);
            w.Write(new byte[16]);
            w.Write(0xFFFFFFFFu);
        }

        w.Write(1u);
        w.Write(0u); w.Write(1u); w.Write(2u);
        w.Write(0u);
        return ((MemoryStream)w.BaseStream).ToArray();
    }

    /// <summary>v4 file: two meshes, position-only vertices (flags 0x1), u16 indices.</summary>
    private static byte[] V4TwoMeshes()
    {
        var chunks = new List<(uint Type, byte[] Payload)>();
        void Mesh(Vector3[] verts, ushort[] idx)
        {
            var vb = new BinaryWriter(new MemoryStream());
            vb.Write((uint)verts.Length);
            vb.Write(0x1u);
            foreach (var v in verts)
            {
                vb.Write(v.X); vb.Write(v.Y); vb.Write(v.Z);
            }

            var ib = new BinaryWriter(new MemoryStream());
            ib.Write((uint)idx.Length);
            foreach (var i in idx)
            {
                ib.Write(i);
            }

            chunks.Add((4, ((MemoryStream)vb.BaseStream).ToArray()));
            chunks.Add((5, ((MemoryStream)ib.BaseStream).ToArray()));
        }

        // Quad in the XZ plane (CCW seen from +Y), then a triangle offset in X.
        Mesh([new(0, 0, 0), new(0, 0, 1), new(1, 0, 1), new(1, 0, 0)], [0, 1, 2, 0, 2, 3]);
        Mesh([new(5, 0, 0), new(5, 0, 1), new(6, 0, 1)], [0, 1, 2]);

        var w = new BinaryWriter(new MemoryStream());
        w.Write("INVO"u8);
        w.Write(4u);
        w.Write((uint)chunks.Count);
        var offset = 12 + (8 * chunks.Count);
        foreach (var (type, payload) in chunks)
        {
            w.Write(type);
            w.Write((uint)offset);
            offset += 8 + payload.Length;
        }

        foreach (var (type, payload) in chunks)
        {
            w.Write(type);
            w.Write((uint)(8 + payload.Length));
            w.Write(payload);
        }

        return ((MemoryStream)w.BaseStream).ToArray();
    }

    [Fact]
    public void V3_interleaves_position_and_normalised_normal()
    {
        var m = MeshData.FromScx(ScxFile.Read(V3Triangle(new Vector3(0, 0, 3))));

        Assert.Equal(3, m.VertexCount);
        Assert.Equal(1, m.TriangleCount);
        Assert.Equal(18, m.Vertices.Length);
        Assert.Equal(new uint[] { 0, 1, 2 }, m.Indices);
        Assert.Equal(new Vector3(2, 0, 0), Pos(m, 1));
        Assert.Equal(Vector3.UnitZ, Nrm(m, 1));
        Assert.Equal(0, m.GeneratedNormals);
        Assert.Equal(Vector3.Zero, m.BoundsMin);
        Assert.Equal(new Vector3(2, 2, 0), m.BoundsMax);
        Assert.Equal(new Vector3(1, 1, 0), m.Center);
    }

    [Fact]
    public void Zero_normals_in_file_are_replaced_by_face_normals()
    {
        var m = MeshData.FromScx(ScxFile.Read(V3Triangle(Vector3.Zero)));
        Assert.Equal(3, m.GeneratedNormals);
        for (var v = 0; v < 3; v++)
        {
            Assert.Equal(Vector3.UnitZ, Nrm(m, v));
        }
    }

    [Fact]
    public void V4_meshes_are_concatenated_with_rebased_indices_and_generated_normals()
    {
        var scx = ScxFile.Read(V4TwoMeshes());
        Assert.False(scx.Meshes[0].Vertices.HasNormal);

        var m = MeshData.FromScx(scx);

        Assert.Equal(7, m.VertexCount);
        Assert.Equal(3, m.TriangleCount);
        Assert.Equal(new uint[] { 0, 1, 2, 0, 2, 3, 4, 5, 6 }, m.Indices);
        Assert.Equal([new SubMesh(0, 6, 0, 4), new SubMesh(6, 3, 4, 3)], m.SubMeshes);
        Assert.Equal(7, m.GeneratedNormals);
        for (var v = 0; v < 7; v++)
        {
            Assert.Equal(Vector3.UnitY, Nrm(m, v));
        }

        Assert.Equal(new Vector3(6, 0, 1), m.BoundsMax);
    }

    [Fact]
    public void Untrusted_meshes_are_skipped_not_thrown()
    {
        Vector3[] tri = [Vector3.Zero, Vector3.UnitX, Vector3.UnitY];
        var m = MeshData.FromArrays(
        [
            (tri, null, [0, 1, 7]),                                                   // index out of range
            ([new(float.NaN, 0, 0), Vector3.UnitX, Vector3.UnitY], null, [0, 1, 2]),  // NaN position
            ([], null, []),                                                           // empty
            (tri, [Vector3.UnitZ], [0, 1, 2]),                                        // normal count mismatch -> generated
            (tri, null, [0, 1, 2, 0]),                                                // trailing partial triangle dropped
        ]);

        Assert.Equal(3, m.SkippedMeshes);
        Assert.Equal(2, m.SubMeshes.Count);
        Assert.Equal(new uint[] { 0, 1, 2, 3, 4, 5 }, m.Indices);
        Assert.Equal(6, m.GeneratedNormals);
    }

    [Fact]
    public void Empty_input_gives_safe_bounds()
    {
        var m = MeshData.FromArrays([]);
        Assert.Equal(0, m.VertexCount);
        Assert.Equal(1f, m.Radius);
        Assert.Equal(Vector3.Zero, m.Center);
    }

    [Fact]
    public void Every_loose_corpus_scx_converts()
    {
        var root = GameRoot.Find();
        if (root is null)
        {
            return;
        }

        int files = 0, meshes = 0, skipped = 0, generated = 0, vertices = 0;
        foreach (var path in Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories))
        {
            if (!path.EndsWith(".scx", StringComparison.OrdinalIgnoreCase) || Path.GetRelativePath(root, path).StartsWith(".", StringComparison.Ordinal))
            {
                continue;
            }

            var m = MeshData.FromScx(ScxFile.Read(path));
            files++;
            meshes += m.SubMeshes.Count;
            skipped += m.SkippedMeshes;
            generated += m.GeneratedNormals;
            vertices += m.VertexCount;
            Assert.All(m.Indices, i => Assert.True(i < m.VertexCount, path));
        }

        Console.WriteLine($"MeshData corpus: {files} files, {meshes} meshes, {skipped} skipped, {vertices} vertices, {generated} generated normals");
    }

    [Fact]
    public void Corpus_meshes_convert_with_valid_indices_and_unit_normals()
    {
        var root = GameRoot.Find();
        if (root is null)
        {
            return;
        }

        string[] files = ["cars/racers/Nonus_data/meshes/chassis.scx", "MW_Mod/Meshes/Jack.SCX"];
        foreach (var rel in files)
        {
            var path = Path.Combine(root, rel);
            if (!File.Exists(path))
            {
                continue;
            }

            var m = MeshData.FromScx(ScxFile.Read(path));
            Assert.True(m.TriangleCount > 0, rel);
            Assert.Equal(0, m.SkippedMeshes);
            Assert.All(m.Indices, i => Assert.True(i < m.VertexCount));
            for (var v = 0; v < m.VertexCount; v++)
            {
                Assert.InRange(Nrm(m, v).Length(), 0.999f, 1.001f);
            }
        }
    }
}
