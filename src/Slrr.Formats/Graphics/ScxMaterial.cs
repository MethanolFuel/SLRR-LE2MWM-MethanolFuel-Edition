using System.Buffers.Binary;
using System.Numerics;
using System.Text;
using Slrr.Core;
using Slrr.Formats.Scx;

namespace Slrr.Formats.Graphics;

/// <summary>One tagged property of a v4 material: <c>u32 tag</c> (high byte = value type) + payload.</summary>
public sealed record ScxMaterialProperty(uint Tag, ReadOnlyMemory<byte> Payload)
{
    public int ValueType => (int)(Tag >> 24);
    public int Id => (int)(Tag & 0xFFFFFF);
}

/// <summary>
/// One texture stage of a material. <see cref="TextureIndex"/> indexes the <c>texture 0x...</c> list of the
/// render (type 14) RPK entry that draws the mesh. v4 stage payload (28 bytes):
/// u32 textureIndex, i32 uvSource, u32 unknown (3 in all but one stage), f32 uScale, f32 vScale, f32 uOffset, f32 vOffset.
/// </summary>
public sealed record ScxTextureStage(int Slot, int TextureIndex, int UvSource, uint Unknown, float UScale, float VScale, float UOffset, float VOffset)
{
    /// <summary>uvSource -2 (705 stages, all in slot 3) is taken to mean generated (environment/reflection) coordinates.</summary>
    public bool IsGenerated => UvSource < 0;
}

/// <summary>Decoded SCX material (both versions). Colours are 0..1 RGBA.</summary>
public sealed class ScxMaterial
{
    public int Version { get; init; }
    public string Name { get; init; } = "";

    /// <summary>v4: first header u32 (values 0,3,4,12,13,15,51,...; meaning unknown). v3: 0.</summary>
    public uint Flags { get; init; }

    /// <summary>v4: second header u32 (only the top bits are used: 0x80000000, 0x40000000, 0x10000000...).</summary>
    public uint Flags2 { get; init; }

    /// <summary>v4 property 0x00000000 (colour), v3 floats 0..15.</summary>
    public Vector4 Diffuse { get; init; } = Vector4.One;

    /// <summary>v4 property 0x00000001 (colour), v3 floats 16..31.</summary>
    public Vector4? Specular { get; init; }

    /// <summary>v4 property 0x00000002 (colour, 229 materials). Hypothesis: emissive / ambient.</summary>
    public Vector4? Color2 { get; init; }

    /// <summary>v4 property 0x01000000 (float) / v3 float at 32. Hypothesis: specular power.</summary>
    public float? SpecularPower { get; init; }

    public IReadOnlyList<ScxTextureStage> Stages { get; init; } = [];
    public IReadOnlyList<ScxMaterialProperty> Properties { get; init; } = [];

    /// <summary>False when the bytes did not follow the expected layout (raw bytes are still available on the mesh).</summary>
    public bool Parsed { get; init; } = true;

    public ScxTextureStage? BaseStage => Stages.Where(s => !s.IsGenerated).OrderBy(s => s.Slot).FirstOrDefault();

    public static ScxMaterial Decode(int version, ReadOnlyMemory<byte> bytes) => version == 3 ? DecodeV3(bytes.Span) : DecodeV4(bytes);

    /// <summary>
    /// v4 layout: u32 flags, u32 flags2, u32 propertyCount, then propertyCount x { u32 tag, payload } where the
    /// payload size depends on the tag's high byte: 0 colour (4 bytes, B,G,R,A order assumed), 1 f32, 5 u32,
    /// 6 texture stage (28 bytes, stage slot = low byte), 8 name (32 bytes, NUL padded).
    /// </summary>
    public static ScxMaterial DecodeV4(ReadOnlyMemory<byte> bytes)
    {
        var span = bytes.Span;
        if (span.Length < 12)
        {
            return new ScxMaterial { Version = 4, Parsed = false };
        }

        var flags = BinaryPrimitives.ReadUInt32LittleEndian(span);
        var flags2 = BinaryPrimitives.ReadUInt32LittleEndian(span[4..]);
        var count = BinaryPrimitives.ReadUInt32LittleEndian(span[8..]);
        var p = 12;
        var props = new List<ScxMaterialProperty>();
        var parsed = true;
        for (var i = 0; i < count; i++)
        {
            if (p + 4 > span.Length)
            {
                parsed = false;
                break;
            }

            var tag = BinaryPrimitives.ReadUInt32LittleEndian(span[p..]);
            var size = PayloadSize((int)(tag >> 24));
            if (size < 0 || p + 4 + size > span.Length)
            {
                parsed = false;
                break;
            }

            props.Add(new ScxMaterialProperty(tag, bytes.Slice(p + 4, size)));
            p += 4 + size;
        }

        parsed &= p == span.Length;
        var name = "";
        Vector4 diffuse = Vector4.One;
        Vector4? spec = null, c2 = null;
        float? power = null;
        var stages = new List<ScxTextureStage>();
        foreach (var prop in props)
        {
            var pl = prop.Payload.Span;
            switch (prop.Tag)
            {
                case 0x08000000:
                    name = Latin1Z(pl);
                    break;
                case 0x00000000:
                    diffuse = Bgra(pl);
                    break;
                case 0x00000001:
                    spec = Bgra(pl);
                    break;
                case 0x00000002:
                    c2 = Bgra(pl);
                    break;
                case 0x01000000:
                    power = BinaryPrimitives.ReadSingleLittleEndian(pl);
                    break;
                default:
                    if (prop.ValueType == 6)
                    {
                        stages.Add(new ScxTextureStage(
                            prop.Id,
                            (int)BinaryPrimitives.ReadUInt32LittleEndian(pl),
                            BinaryPrimitives.ReadInt32LittleEndian(pl[4..]),
                            BinaryPrimitives.ReadUInt32LittleEndian(pl[8..]),
                            BinaryPrimitives.ReadSingleLittleEndian(pl[12..]),
                            BinaryPrimitives.ReadSingleLittleEndian(pl[16..]),
                            BinaryPrimitives.ReadSingleLittleEndian(pl[20..]),
                            BinaryPrimitives.ReadSingleLittleEndian(pl[24..])));
                    }

                    break;
            }
        }

        return new ScxMaterial
        {
            Version = 4, Flags = flags, Flags2 = flags2, Name = name, Diffuse = diffuse, Specular = spec, Color2 = c2,
            SpecularPower = power, Stages = stages, Properties = props, Parsed = parsed,
        };
    }

    /// <summary>Payload size per property value type (measured: types 0, 1, 5, 6, 8 occur).</summary>
    public static int PayloadSize(int valueType) => valueType switch
    {
        0 or 1 or 5 => 4,
        6 => 28,
        8 => 32,
        _ => -1,
    };

    /// <summary>
    /// v3 material (record length 52, 100 or 132 bytes after the size field): f32 RGBA diffuse (0), f32 RGBA
    /// specular (16), f32 power (32), u32 (36), then u16 slots from byte 40 where 0xFFFF = none. The u16 at byte 40
    /// is taken as the diffuse texture index (384 of 406 non-empty values fall inside the render's texture list).
    /// 132-byte records end with a 32-byte name at byte 100.
    /// </summary>
    public static ScxMaterial DecodeV3(ReadOnlySpan<byte> m)
    {
        if (m.Length < 42)
        {
            return new ScxMaterial { Version = 3, Parsed = false };
        }

        var tex = BinaryPrimitives.ReadUInt16LittleEndian(m[40..]);
        var stages = new List<ScxTextureStage>();
        if (tex != 0xFFFF)
        {
            stages.Add(new ScxTextureStage(0, tex, 0, 0, 1, 1, 0, 0));
        }

        return new ScxMaterial
        {
            Version = 3,
            Diffuse = F4(m, 0),
            Specular = F4(m, 16),
            SpecularPower = BinaryPrimitives.ReadSingleLittleEndian(m[32..]),
            Name = m.Length >= 132 ? Latin1Z(m.Slice(100, 32)) : "",
            Stages = stages,
            Parsed = m.Length is 52 or 100 or 132,
        };
    }

    private static Vector4 F4(ReadOnlySpan<byte> m, int o) => new(
        BinaryPrimitives.ReadSingleLittleEndian(m[o..]), BinaryPrimitives.ReadSingleLittleEndian(m[(o + 4)..]),
        BinaryPrimitives.ReadSingleLittleEndian(m[(o + 8)..]), BinaryPrimitives.ReadSingleLittleEndian(m[(o + 12)..]));

    private static Vector4 Bgra(ReadOnlySpan<byte> p) => new(p[2] / 255f, p[1] / 255f, p[0] / 255f, p[3] / 255f);

    private static string Latin1Z(ReadOnlySpan<byte> b)
    {
        var n = b.IndexOf((byte)0);
        return Encoding.Latin1.GetString(n < 0 ? b : b[..n]);
    }
}

/// <summary>
/// Byte offsets of the vertex attributes. v4 flag bits (measured order): 0x1 position 12; blend data 0x4 4,
/// 0x8 8, 0x10 12, 0x20 4; 0x40 normal 12; 0x80 colour 4; 0x100 colour 4; 0x200 uv0 8; 0x400 uv1 8; 0x800 uv2 8;
/// 0x40000 (one buffer) 12 unknown bytes, assumed to follow everything else.
/// v3: 44 bytes = position, normal, uv0, uv1, u32 colour; 64 bytes = the same + 20 bytes
/// (8 bytes unused/junk, 8 bytes float pair, 4 bytes always zero).
/// </summary>
public sealed record ScxVertexLayout(int Stride, int Normal, int Color0, int Color1, int Uv0, int Uv1, int Uv2, int BlendBytes)
{
    public static ScxVertexLayout For(ScxVertexBuffer vb)
    {
        if (vb.Flags == 0)
        {
            // v3
            return new ScxVertexLayout(vb.Stride, 12, 40, -1, 24, 32, vb.Stride >= 64 ? 52 : -1, 0);
        }

        var f = vb.Flags;
        var o = (f & 0x1) != 0 ? 12 : 0;
        var blend = 0;
        if ((f & 0x4) != 0) blend += 4;
        if ((f & 0x8) != 0) blend += 8;
        if ((f & 0x10) != 0) blend += 12;
        if ((f & 0x20) != 0) blend += 4;
        o += blend;
        int Take(uint bit, int size)
        {
            if ((f & bit) == 0)
            {
                return -1;
            }

            var at = o;
            o += size;
            return at;
        }

        var normal = Take(0x40, 12);
        var c0 = Take(0x80, 4);
        var c1 = Take(0x100, 4);
        var uv0 = Take(0x200, 8);
        var uv1 = Take(0x400, 8);
        var uv2 = Take(0x800, 8);
        return new ScxVertexLayout(vb.Stride, normal, c0, c1, uv0, uv1, uv2, blend);
    }

    public int UvOffset(int channel) => channel switch
    {
        0 => Uv0,
        1 => Uv1,
        2 => Uv2,
        _ => -1,
    };

    public Vector2 ReadUv(ScxVertexBuffer vb, int vertex, int channel)
    {
        var off = UvOffset(channel);
        if (off < 0 || off + 8 > Stride)
        {
            return Vector2.Zero;
        }

        var s = vb.Data.Span.Slice((vertex * vb.Stride) + off, 8);
        return new Vector2(BinaryPrimitives.ReadSingleLittleEndian(s), BinaryPrimitives.ReadSingleLittleEndian(s[4..]));
    }
}

/// <summary>Decoded auxiliary v4 chunks (types 1, 2, 3) that precede a mesh's vertex chunk.</summary>
public static class ScxAuxChunks
{
    /// <summary>Type 3 (55 chunks, skinned meshes in humans/ and MW_Mod/Driver.SCX): u32 0, u32 n, n x u32 bone index.</summary>
    public static IReadOnlyList<int>? BonePalette(ScxChunk c)
    {
        if (c.Type != 3 || c.Payload.Length < 8)
        {
            return null;
        }

        var r = new ByteReader(c.Payload);
        r.U32();
        var n = r.CheckedCount(r.U32(), 4, "bone palette");
        var list = new int[n];
        for (var i = 0; i < n; i++)
        {
            list[i] = (int)r.U32();
        }

        return r.Remaining == 0 ? list : null;
    }

    /// <summary>Type 2 (277 chunks, shocks/springs with .bon files): u32 0, u32 a (0/1), i32 b (0, 1, -1).</summary>
    public static (uint A, uint B, int C)? Type2(ScxChunk c)
    {
        if (c.Type != 2 || c.Payload.Length != 12)
        {
            return null;
        }

        var r = new ByteReader(c.Payload);
        return (r.U32(), r.U32(), r.I32());
    }
}
