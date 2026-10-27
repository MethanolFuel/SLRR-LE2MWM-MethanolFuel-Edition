using System.Buffers.Binary;
using System.Numerics;
using Slrr.Formats.Instances;

namespace Slrr.World;

/// <summary>
/// One POLY submesh, GPU-ready: <see cref="VertexData"/> is the raw 56-byte-stride vertex buffer straight from the file
/// (no copy), <see cref="Indices"/> a triangle list (3 per triangle, CCW/CW unverified).
/// Vertex layout (MEASURED, docs/formats/instances.md): pos f32x3 @0, normal f32x3 @12, color0 u32 @24, color1 u32 @28,
/// uv0 f32x2 @32, uv1 f32x2 @40, uv2 f32x2 @48.
/// </summary>
public sealed class RenderSubmesh
{
    public const int VertexStride = 56;

    public required int Material { get; init; }

    public required int VertexCount { get; init; }

    public required ReadOnlyMemory<byte> VertexData { get; init; }

    public required int[] Indices { get; init; }

    public int TriangleCount => Indices.Length / 3;

    public Vector3 Position(int i)
    {
        var s = VertexData.Span[(i * VertexStride)..];
        return new Vector3(BinaryPrimitives.ReadSingleLittleEndian(s), BinaryPrimitives.ReadSingleLittleEndian(s[4..]), BinaryPrimitives.ReadSingleLittleEndian(s[8..]));
    }

    public Vector3 Normal(int i)
    {
        var s = VertexData.Span[((i * VertexStride) + 12)..];
        return new Vector3(BinaryPrimitives.ReadSingleLittleEndian(s), BinaryPrimitives.ReadSingleLittleEndian(s[4..]), BinaryPrimitives.ReadSingleLittleEndian(s[8..]));
    }

    public Vector2 Uv(int i, int set)
    {
        var s = VertexData.Span[((i * VertexStride) + 32 + (set * 8))..];
        return new Vector2(BinaryPrimitives.ReadSingleLittleEndian(s), BinaryPrimitives.ReadSingleLittleEndian(s[4..]));
    }
}

/// <summary>The POLY chunk(s) of one spatial node: a renderer's culling/draw unit.</summary>
public sealed class RenderChunk
{
    public required uint NodeId { get; init; }

    public required string NodeName { get; init; }

    public required InstanceBox Box { get; init; }

    public required IReadOnlyList<RenderSubmesh> Submeshes { get; init; }

    public Vector3 Center => new(Box.CenterX, Box.CenterY, Box.CenterZ);

    public Vector3 HalfExtents => new(Box.HalfX, Box.HalfY, Box.HalfZ);
}

/// <summary>A material slot of the map as a renderer needs it: index (POLY <c>material</c>), name, surface/grip data.
/// Texture binding is NOT resolved here (open question: material name -> texture, see docs/phase4/world.md).</summary>
public sealed record RenderMaterial(int Index, string Name, SurfaceInfo Surface);

/// <summary>
/// Render-side description of a map: all POLY submeshes grouped per spatial node (with the node's box for culling),
/// the material table, and the placed objects (type + matrix) the renderer must instantiate from the object's own render
/// resources (<see cref="ObjectType.Renders"/>). Pure data: no GPU types.
/// </summary>
public sealed class RenderScene
{
    private RenderScene(SurfaceTable table, List<RenderChunk> chunks)
    {
        Chunks = chunks;
        Materials = table.Items.Select(s => new RenderMaterial(s.Index, s.Name, s)).ToList();
    }

    public IReadOnlyList<RenderChunk> Chunks { get; }

    public IReadOnlyList<RenderMaterial> Materials { get; }

    public int SubmeshCount => Chunks.Sum(c => c.Submeshes.Count);

    public long VertexCount => Chunks.Sum(c => c.Submeshes.Sum(s => (long)s.VertexCount));

    public long TriangleCount => Chunks.Sum(c => c.Submeshes.Sum(s => (long)s.TriangleCount));

    /// <summary>Count of submeshes whose POLY material index is outside the material table (MEASURED 0 on shipped maps with a text .scm).</summary>
    public int MaterialsOutOfRange { get; private set; }

    public static RenderScene Empty(SurfaceTable table) => new(table, []);

    public static RenderScene Build(InstanceForest forest, SurfaceTable table) => Build([(forest, 0)], table);

    /// <summary>Several forests in one scene (a map plus the archives its EXTP chunk attaches); each forest's POLY
    /// material indices are shifted by its offset into the merged <paramref name="table"/>.</summary>
    public static RenderScene Build(IEnumerable<(InstanceForest Forest, int MaterialOffset)> forests, SurfaceTable table)
    {
        var chunks = new List<RenderChunk>();
        var bad = 0;
        foreach (var (forest, offset) in forests)
        {
            foreach (var node in forest.AllNodes())
            {
                foreach (var c in node.Chunks.Where(c => c.Tag == InstanceChunk.TagPoly))
                {
                    var subs = ParsePoly(c.Payload);
                    if (offset != 0)
                    {
                        subs = subs.Select(s => new RenderSubmesh { Material = s.Material + offset, VertexCount = s.VertexCount, VertexData = s.VertexData, Indices = s.Indices }).ToList();
                    }

                    bad += subs.Count(s => s.Material >= table.Count);
                    chunks.Add(new RenderChunk { NodeId = node.Record.Id, NodeName = node.Record.Alias, Box = node.Box, Submeshes = subs });
                }
            }
        }

        return new RenderScene(table, chunks) { MaterialsOutOfRange = bad };
    }

    /// <summary>Zero-copy POLY parse: <c>u32 unknown | u32 submeshCount | { u32 material, u32 vertCount, u32 triCount, vertCount x 56 B, u32 idx[3*triCount] }</c>.</summary>
    public static List<RenderSubmesh> ParsePoly(byte[] payload)
    {
        var list = new List<RenderSubmesh>();
        var mem = payload.AsMemory();
        var s = payload.AsSpan();
        if (s.Length < 8)
        {
            return list;
        }

        var count = (int)BinaryPrimitives.ReadUInt32LittleEndian(s[4..]);
        var pos = 8;
        for (var i = 0; i < count; i++)
        {
            if (pos + 12 > s.Length)
            {
                break;
            }

            var material = (int)BinaryPrimitives.ReadUInt32LittleEndian(s[pos..]);
            var vc = (int)BinaryPrimitives.ReadUInt32LittleEndian(s[(pos + 4)..]);
            var tc = (int)BinaryPrimitives.ReadUInt32LittleEndian(s[(pos + 8)..]);
            pos += 12;
            var vbytes = (long)vc * RenderSubmesh.VertexStride;
            var ibytes = (long)tc * 12;
            if (vc < 0 || tc < 0 || pos + vbytes + ibytes > s.Length)
            {
                break;
            }

            var idx = new int[tc * 3];
            var ip = pos + (int)vbytes;
            for (var k = 0; k < idx.Length; k++)
            {
                idx[k] = (int)BinaryPrimitives.ReadUInt32LittleEndian(s[(ip + (k * 4))..]);
            }

            list.Add(new RenderSubmesh { Material = material, VertexCount = vc, VertexData = mem.Slice(pos, (int)vbytes), Indices = idx });
            pos += (int)(vbytes + ibytes);
        }

        return list;
    }
}
