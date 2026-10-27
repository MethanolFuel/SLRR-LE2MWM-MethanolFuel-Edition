using System.Numerics;
using Slrr.Core;

namespace Slrr.Formats.Scx;

/// <summary>A raw v4 chunk: directory type + payload (chunk header type/size already validated).</summary>
public sealed record ScxChunk(uint Type, int Offset, ReadOnlyMemory<byte> Payload);

/// <summary>Decoded vertex buffer. Only position (and normal when present) are interpreted; the rest stays raw.</summary>
public sealed record ScxVertexBuffer(uint Flags, int Stride, int Count, ReadOnlyMemory<byte> Data)
{
    /// <summary>v4: flag 0x40. v3 (Flags == 0): always, at byte 12.</summary>
    public bool HasNormal => Flags == 0 ? Stride >= 24 : (Flags & 0x40) != 0;

    /// <summary>
    /// Normal follows position and the blend data (D3D FVF order). Measured on the corpus: normals are unit
    /// length at byte 16 for flags 0x245/0x263 and at byte 20 for 0x265, i.e. bits 0x4 and 0x20 take 4 bytes each.
    /// </summary>
    public int NormalOffset =>
        12 + ((Flags & 0x4) != 0 ? 4 : 0) + ((Flags & 0x8) != 0 ? 8 : 0) + ((Flags & 0x10) != 0 ? 12 : 0) + ((Flags & 0x20) != 0 ? 4 : 0);

    public Vector3 Position(int i)
    {
        var r = new ByteReader(Data, i * Stride);
        return new Vector3(r.F32(), r.F32(), r.F32());
    }

    public Vector3? Normal(int i)
    {
        if (!HasNormal)
        {
            return null;
        }

        var r = new ByteReader(Data, (i * Stride) + NormalOffset);
        return new Vector3(r.F32(), r.F32(), r.F32());
    }
}

/// <summary>One drawable mesh: material bytes (raw), vertices and triangle indices.</summary>
public sealed record ScxMesh(ReadOnlyMemory<byte> Material, ScxVertexBuffer Vertices, IReadOnlyList<int> Indices, IReadOnlyList<ScxChunk> OtherChunks);

/// <summary>
/// SCX mesh container (magic <c>INVO</c>). Two layouts exist in the shipped content:
/// <list type="bullet">
/// <item>v4 (2,682 files): u32 chunkCount, directory of (u32 type, u32 absOffset), each chunk = u32 type, u32 size
/// (including its 8-byte header), payload. Types 0 = material, 1 = u32, 2 = 12 bytes, 3 = variable (skinning?),
/// 4 = vertices (u32 count, u32 flags, count*stride bytes), 5 = indices (u32 count, u16 each). Meshes are
/// sequences 0/2 [1] [3] 4 5.</item>
/// <item>v3 (542 files): repeated { u32 materialSize (incl. itself), material; u32 vertexCount, vertices;
/// u32 triangleCount, triangles (3 x u32) } until EOF, optionally followed by one u32 0. Vertex stride is
/// 44 bytes for 56-byte materials, else 64.</item>
/// </list>
/// See docs/formats/scx.md for the evidence and open questions.
/// </summary>
public sealed class ScxFile
{
    public static ReadOnlySpan<byte> Magic => "INVO"u8;

    public uint Version { get; init; }
    public IReadOnlyList<ScxChunk> Chunks { get; init; } = [];
    public IReadOnlyList<ScxMesh> Meshes { get; init; } = [];

    /// <summary>Bytes accounted for by the parser (header + directory + chunks, or v3 records + terminator).</summary>
    public int CoveredBytes { get; init; }
    public int TotalBytes { get; init; }

    public static ScxFile Read(string path) => Read(File.ReadAllBytes(path));

    public static ScxFile Read(ReadOnlyMemory<byte> bytes)
    {
        var r = new ByteReader(bytes);
        r.Expect(Magic, "SCX");
        var version = r.U32();
        return version switch
        {
            4 => ReadV4(r, bytes.Length),
            3 => ReadV3(r, bytes.Length),
            _ => throw new SlrrFormatException($"unsupported SCX version {version}"),
        };
    }

    /// <summary>Per-flag-bit byte sizes inferred from 2,682 files (hypothesis; stride is always taken from the chunk size).</summary>
    public static int StrideFromFlags(uint flags)
    {
        var size = 0;
        if ((flags & 0x001) != 0) size += 12;  // position
        if ((flags & 0x004) != 0) size += 4;   // 1 blend weight?
        if ((flags & 0x008) != 0) size += 8;   // 2 blend weights?
        if ((flags & 0x010) != 0) size += 12;  // 3 blend weights?
        if ((flags & 0x020) != 0) size += 4;   // blend indices?
        if ((flags & 0x040) != 0) size += 12;  // normal
        if ((flags & 0x080) != 0) size += 4;   // diffuse colour?
        if ((flags & 0x100) != 0) size += 4;   // specular colour?
        if ((flags & 0x200) != 0) size += 8;   // uv0
        if ((flags & 0x400) != 0) size += 8;   // uv1
        if ((flags & 0x800) != 0) size += 8;   // uv2
        return size;
    }

    private static ScxFile ReadV4(ByteReader r, int total)
    {
        var count = r.CheckedCount(r.U32(), 8, "chunk directory");
        var dir = new List<(uint Type, uint Offset)>();
        for (var i = 0; i < count; i++)
        {
            dir.Add((r.U32(), r.U32()));
        }

        var covered = r.Position;
        var chunks = new List<ScxChunk>();
        foreach (var (type, offset) in dir)
        {
            if (offset > (uint)total - 8)
            {
                throw new SlrrFormatException($"chunk offset {offset} outside file");
            }

            var c = new ByteReader(r.Data, (int)offset);
            var ctype = c.U32();
            var csize = c.U32();
            if (ctype != type)
            {
                throw new SlrrFormatException($"chunk at {offset} has type {ctype}, directory says {type}");
            }

            if (csize < 8 || csize > (uint)(total - (int)offset))
            {
                throw new SlrrFormatException($"chunk at {offset} has bad size {csize}");
            }

            chunks.Add(new ScxChunk(type, (int)offset, c.Slice((int)csize - 8)));
            covered += (int)csize;
        }

        var meshes = new List<ScxMesh>();
        ReadOnlyMemory<byte> material = ReadOnlyMemory<byte>.Empty;
        var others = new List<ScxChunk>();
        ScxVertexBuffer? vb = null;
        foreach (var ch in chunks)
        {
            switch (ch.Type)
            {
                case 0:
                    material = ch.Payload;
                    others = [];
                    break;
                case 4:
                {
                    var p = new ByteReader(ch.Payload);
                    var n = p.U32();
                    var flags = p.U32();
                    var vertexCount = p.CheckedCount(n, 1, "vertex");
                    var stride = vertexCount == 0 ? 0 : p.Remaining / vertexCount;
                    if (vertexCount != 0 && stride * (long)vertexCount != p.Remaining)
                    {
                        throw new SlrrFormatException($"vertex chunk at {ch.Offset}: {p.Remaining} bytes is not a multiple of {n} vertices");
                    }

                    vb = new ScxVertexBuffer(flags, stride, vertexCount, p.Slice(p.Remaining));
                    break;
                }

                case 5:
                {
                    var p = new ByteReader(ch.Payload);
                    var n = p.CheckedCount(p.U32(), 2, "index");
                    var idx = new int[n];
                    for (var i = 0; i < n; i++)
                    {
                        idx[i] = p.U16();
                    }

                    var vertices = vb ?? new ScxVertexBuffer(0, 0, 0, ReadOnlyMemory<byte>.Empty);
                    if (idx.Length > 0 && idx.Max() >= vertices.Count)
                    {
                        throw new SlrrFormatException($"index chunk at {ch.Offset} references vertex {idx.Max()} of {vertices.Count}");
                    }

                    meshes.Add(new ScxMesh(material, vertices, idx, others));
                    vb = null;
                    break;
                }

                default:
                    others.Add(ch);
                    break;
            }
        }

        return new ScxFile { Version = 4, Chunks = chunks, Meshes = meshes, CoveredBytes = covered, TotalBytes = total };
    }

    private static ScxFile ReadV3(ByteReader r, int total)
    {
        var meshes = new List<ScxMesh>();
        while (r.Remaining > 0)
        {
            if (r.Remaining == 4)
            {
                var terminator = r.U32();
                if (terminator != 0)
                {
                    throw new SlrrFormatException($"unexpected trailing u32 {terminator}");
                }

                break;
            }

            var matStart = r.Position;
            var matSize = r.U32();
            if (matSize < 4 || matSize - 4 > (uint)r.Remaining)
            {
                throw new SlrrFormatException($"material size {matSize} at {matStart} is invalid");
            }

            var material = r.Slice((int)matSize - 4);
            var stride = matSize == 56 ? 44 : 64;
            var vcount = r.CheckedCount(r.U32(), stride, "vertex");
            var vdata = r.Slice(vcount * stride);
            var tcount = r.CheckedCount(r.U32(), 12, "triangle");
            var idx = new int[tcount * 3];
            for (var i = 0; i < idx.Length; i++)
            {
                var v = r.U32();
                idx[i] = v < (uint)vcount ? (int)v : throw new SlrrFormatException($"triangle index {v} out of range for {vcount} vertices");
            }

            meshes.Add(new ScxMesh(material, new ScxVertexBuffer(0, stride, vcount, vdata), idx, []));
        }

        return new ScxFile { Version = 3, Meshes = meshes, CoveredBytes = r.Position, TotalBytes = total };
    }
}
