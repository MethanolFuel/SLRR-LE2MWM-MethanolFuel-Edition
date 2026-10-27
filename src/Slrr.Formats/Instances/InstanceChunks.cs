using System.Globalization;
using System.Text;
using Slrr.Core;

namespace Slrr.Formats.Instances;

public readonly record struct Vec3(float X, float Y, float Z)
{
    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"({X:0.###}, {Y:0.###}, {Z:0.###})");
}

/// <summary>PHYS triangle: <c>u32 surface | u32 a, b, c | f32 normal[3]</c> (28 bytes).</summary>
/// <remarks>Measured: the normal equals normalize((b-a) x (c-a)) for all 693,433 non-degenerate shipped triangles.
/// <see cref="Surface"/> meaning (material / surface class) is a hypothesis.</remarks>
public readonly record struct PhysTriangle(uint Surface, uint A, uint B, uint C, Vec3 Normal);

/// <summary>PHYS chunk: collision mesh of a spatial leaf. <c>u32 triCount | tris | u32 vertCount | vec3 verts</c>.</summary>
public sealed class PhysMesh
{
    public List<PhysTriangle> Triangles { get; } = [];
    public List<Vec3> Vertices { get; } = [];

    public static PhysMesh Parse(ReadOnlyMemory<byte> payload)
    {
        var r = new ByteReader(payload);
        var m = new PhysMesh();
        var nt = r.CheckedCount(r.U32(), 28, "PHYS triangle");
        for (var i = 0; i < nt; i++)
        {
            m.Triangles.Add(new PhysTriangle(r.U32(), r.U32(), r.U32(), r.U32(), new Vec3(r.F32(), r.F32(), r.F32())));
        }

        var nv = r.CheckedCount(r.U32(), 12, "PHYS vertex");
        for (var i = 0; i < nv; i++)
        {
            m.Vertices.Add(new Vec3(r.F32(), r.F32(), r.F32()));
        }

        if (r.Remaining != 0)
        {
            throw new SlrrFormatException($"PHYS chunk has {r.Remaining} trailing bytes");
        }

        foreach (var t in m.Triangles)
        {
            if (t.A >= nv || t.B >= nv || t.C >= nv)
            {
                throw new SlrrFormatException($"PHYS triangle index out of range ({t.A}, {t.B}, {t.C}) >= {nv}");
            }
        }

        return m;
    }

    public byte[] ToBytes()
    {
        var w = new ByteWriter();
        w.U32((uint)Triangles.Count);
        foreach (var t in Triangles)
        {
            w.U32(t.Surface);
            w.U32(t.A);
            w.U32(t.B);
            w.U32(t.C);
            WriteVec(w, t.Normal);
        }

        w.U32((uint)Vertices.Count);
        foreach (var v in Vertices)
        {
            WriteVec(w, v);
        }

        return w.ToArray();
    }

    internal static void WriteVec(ByteWriter w, Vec3 v)
    {
        w.F32(v.X);
        w.F32(v.Y);
        w.F32(v.Z);
    }
}

/// <summary>POLY vertex, 56 bytes: position, normal, two u32 colors, then three UV pairs (names are hypotheses;
/// UV0 looks like tiled texture coordinates, UV1 like 0..1 lightmap coordinates; UV2 is non-zero in 711,958 of
/// 1,100,565 shipped vertices, meaning unknown).</summary>
public readonly record struct PolyVertex(Vec3 Position, Vec3 Normal, uint Color0, uint Color1, float U0, float V0, float U1, float V1, float U2, float V2);

/// <summary>One POLY sub-mesh: <c>u32 material | u32 vertCount | u32 triCount | vertices | u32 indices[3*triCount]</c>.</summary>
public sealed class PolySubmesh
{
    /// <summary>First u32; small integers 0..n (hypothesis: material/texture slot of the map mesh).</summary>
    public uint Material { get; set; }

    public List<PolyVertex> Vertices { get; } = [];

    public List<uint> Indices { get; } = [];
}

/// <summary>POLY chunk: render geometry of a spatial leaf. <c>u32 unknown | u32 submeshCount | submeshes</c>.</summary>
public sealed class PolyMesh
{
    public const int VertexSize = 56;

    /// <summary>First u32; 1 in 2,857 of 2,883 shipped chunks, else 6/47/122/199 (meaning unknown).</summary>
    public uint Unknown { get; set; }

    public List<PolySubmesh> Submeshes { get; } = [];

    public static PolyMesh Parse(ReadOnlyMemory<byte> payload)
    {
        var r = new ByteReader(payload);
        var m = new PolyMesh { Unknown = r.U32() };
        var count = r.CheckedCount(r.U32(), 12, "POLY submesh");
        for (var s = 0; s < count; s++)
        {
            var sm = new PolySubmesh { Material = r.U32() };
            var nv = r.U32();
            var nt = r.U32();
            var verts = r.CheckedCount(nv, VertexSize, "POLY vertex");
            for (var i = 0; i < verts; i++)
            {
                sm.Vertices.Add(new PolyVertex(
                    new Vec3(r.F32(), r.F32(), r.F32()), new Vec3(r.F32(), r.F32(), r.F32()), r.U32(), r.U32(),
                    r.F32(), r.F32(), r.F32(), r.F32(), r.F32(), r.F32()));
            }

            var tris = r.CheckedCount(nt, 12, "POLY triangle");
            for (var i = 0; i < tris * 3; i++)
            {
                var idx = r.U32();
                if (idx >= nv)
                {
                    throw new SlrrFormatException($"POLY index {idx} >= vertex count {nv}");
                }

                sm.Indices.Add(idx);
            }

            m.Submeshes.Add(sm);
        }

        if (r.Remaining != 0)
        {
            throw new SlrrFormatException($"POLY chunk has {r.Remaining} trailing bytes");
        }

        return m;
    }

    public byte[] ToBytes()
    {
        var w = new ByteWriter();
        w.U32(Unknown);
        w.U32((uint)Submeshes.Count);
        foreach (var sm in Submeshes)
        {
            if (sm.Indices.Count % 3 != 0)
            {
                throw new InvalidOperationException("POLY index count must be a multiple of 3");
            }

            w.U32(sm.Material);
            w.U32((uint)sm.Vertices.Count);
            w.U32((uint)(sm.Indices.Count / 3));
            foreach (var v in sm.Vertices)
            {
                PhysMesh.WriteVec(w, v.Position);
                PhysMesh.WriteVec(w, v.Normal);
                w.U32(v.Color0);
                w.U32(v.Color1);
                w.F32(v.U0);
                w.F32(v.V0);
                w.F32(v.U1);
                w.F32(v.V1);
                w.F32(v.U2);
                w.F32(v.V2);
            }

            foreach (var i in sm.Indices)
            {
                w.U32(i);
            }
        }

        return w.ToArray();
    }
}

/// <summary>Placement decoded from an object instance's <c>params x,y,z,a,b,c</c> line.</summary>
/// <remarks>The three angles are radians; the game's script API calls orientations <c>Ypr</c> (yaw, pitch, roll),
/// and in shipped maps only the first angle is usually non-zero, consistent with yaw about the vertical axis (hypothesis).</remarks>
public readonly record struct InstancePlacement(Vec3 Position, float Yaw, float Pitch, float Roll);

/// <summary>RSD chunk: CRLF text, normally <c>gametype 0xRRRRIIII</c> and <c>params ...</c>.</summary>
public sealed class RsdText
{
    public RsdText(byte[] payload)
    {
        Text = Encoding.Latin1.GetString(payload);
    }

    public string Text { get; }

    public IEnumerable<(string Key, string Value)> Lines()
    {
        foreach (var raw in Text.Split('\n'))
        {
            var line = raw.TrimEnd('\r', '\0');
            if (line.Length == 0)
            {
                continue;
            }

            var sp = line.IndexOf(' ', StringComparison.Ordinal);
            yield return sp < 0 ? (line, string.Empty) : (line[..sp], line[(sp + 1)..]);
        }
    }

    public string? Get(string key) => Lines().Where(l => l.Key == key).Select(l => l.Value).FirstOrDefault();

    /// <summary>Resource id of the instance's type: high 16 bits = 1-based slot in the archive's reference table
    /// (e.g. 0x0002018E = slot 2 objects.rpk, id 0x18E lamp_base), low 16 bits = entry id in that archive.</summary>
    public uint? GameType
    {
        get
        {
            var v = Get("gametype");
            if (v is null)
            {
                return null;
            }

            var s = v.Trim();
            if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                s = s[2..];
            }

            return uint.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var id) ? id : null;
        }
    }

    public string? Params => Get("params");

    /// <summary>Parses <c>params</c> as six decimal numbers (object instances). Returns null for other forms
    /// (map roots use <c>0xFLAGS,0,0,0,0,0,0,mesh.scm[,ai.trc]</c>).</summary>
    public InstancePlacement? Placement
    {
        get
        {
            var p = Params;
            if (p is null)
            {
                return null;
            }

            var cut = p.IndexOf("//", StringComparison.Ordinal);
            var parts = (cut >= 0 ? p[..cut] : p).Split(',');
            if (parts.Length != 6)
            {
                return null;
            }

            var f = new float[6];
            for (var i = 0; i < 6; i++)
            {
                if (!float.TryParse(parts[i].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out f[i]))
                {
                    return null;
                }
            }

            return new InstancePlacement(new Vec3(f[0], f[1], f[2]), f[3], f[4], f[5]);
        }
    }
}
