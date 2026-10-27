using System.Numerics;
using Slrr.Formats.Graphics;

namespace Slrr.Render.Geometry;

public enum AlphaMode
{
    Opaque,

    /// <summary>Alpha test (cutout) at 0.5. GUESSED threshold.</summary>
    Test,

    /// <summary>Blended, drawn after opaque geometry, back to front, without depth writes.</summary>
    Blend,
}

/// <summary>
/// One texture stage bound for drawing: the decoded file, which vertex UV attribute carries its channel
/// (0 = <c>aUvA</c>, 1 = <c>aUvB</c>) and the stage's scale/offset (applied as <c>uv * scale + offset</c>; GUESSED order).
/// </summary>
public sealed record StageBinding(string? TexturePath, int Attribute, Vector4 ScaleOffset, int Slot, int UvSource);

/// <summary>Everything the shader needs for one sub-mesh. Built from an <see cref="ScxMaterial"/> + the render entry's texture list.</summary>
public sealed class MaterialDesc
{
    public string Name { get; init; } = "";

    public Vector4 Diffuse { get; init; } = Vector4.One;

    public Vector3 Specular { get; init; } = new(0.2f);

    public float Power { get; init; } = 16f;

    /// <summary>Base colour stage (lowest non-generated slot, normally slot 0).</summary>
    public StageBinding? Base { get; init; }

    /// <summary>Slot 1 layer, blended over the base by its alpha (decal / livery). GUESSED blend.</summary>
    public StageBinding? Decal { get; init; }

    /// <summary>The material has a generated-coordinate slot-3 stage (reflection). The texture is the render's own when assigned,
    /// else null = engine-supplied environment map (hypothesis from docs/formats/graphics.md).</summary>
    public bool Env { get; init; }

    public string? EnvTexturePath { get; init; }

    /// <summary>Property 0x01000001 (hypothesis: reflection strength); 0.25 when absent but an env stage exists (GUESSED).</summary>
    public float EnvStrength { get; init; }

    public AlphaMode Alpha { get; init; }

    public uint Flags { get; init; }

    public uint Flags2 { get; init; }

    /// <summary>Multiply by the vertex colour (map POLY geometry: colour0). GUESSED use of that colour.</summary>
    public bool VertexColor { get; init; }

    public static readonly MaterialDesc Default = new() { Name = "default", Diffuse = new Vector4(0.7f, 0.7f, 0.7f, 1f) };
}

/// <summary>Turns a decoded SCX material into a <see cref="MaterialDesc"/>.</summary>
public static class MaterialFactory
{
    /// <summary>flags2 bit that correlates with glass / glow materials (MEASURED: 118 "window*" + 124 "*glow*"/osd materials; meaning GUESSED = blend).</summary>
    public const uint BlendBit = 0x40000000;

    /// <summary>flags2 bits seen on foliage / fences / "alphatest" materials (MEASURED names; meaning GUESSED = cutout).</summary>
    public const uint CutoutBits = 0x80000000 | 0x20000000;

    /// <param name="mat">Decoded material.</param>
    /// <param name="texturePaths">The render entry's texture list (index = stage TextureIndex); null entries are unresolved.</param>
    /// <param name="channelToAttribute">Maps an SCX UV channel (0..2) to the vertex attribute the builder put it in, or -1.</param>
    /// <param name="hasAlpha">Whether a texture has any non-opaque texel (decides cutout).</param>
    public static MaterialDesc Create(ScxMaterial mat, IReadOnlyList<string?> texturePaths, Func<int, int> channelToAttribute, Func<string, bool> hasAlpha)
    {
        string? Tex(ScxTextureStage s) => s.TextureIndex >= 0 && s.TextureIndex < texturePaths.Count ? texturePaths[s.TextureIndex] : null;

        StageBinding? Bind(ScxTextureStage? s)
        {
            if (s is null)
            {
                return null;
            }

            var attr = channelToAttribute(Math.Clamp(s.UvSource, 0, 2));
            var scale = new Vector4(
                float.IsFinite(s.UScale) && s.UScale != 0 ? s.UScale : 1,
                float.IsFinite(s.VScale) && s.VScale != 0 ? s.VScale : 1,
                float.IsFinite(s.UOffset) ? s.UOffset : 0,
                float.IsFinite(s.VOffset) ? s.VOffset : 0);
            return new StageBinding(Tex(s), Math.Max(attr, 0), scale, s.Slot, s.UvSource);
        }

        var baseStage = mat.Stages.Where(s => !s.IsGenerated && s.Slot != 1).OrderBy(s => s.Slot).FirstOrDefault()
            ?? mat.Stages.Where(s => !s.IsGenerated).OrderBy(s => s.Slot).FirstOrDefault();
        var decalStage = mat.Stages.FirstOrDefault(s => s.Slot == 1 && !s.IsGenerated && !ReferenceEquals(s, baseStage));
        var envStage = mat.Stages.FirstOrDefault(s => s.IsGenerated || s.Slot == 3);
        var b = Bind(baseStage);
        var d = Bind(decalStage);
        if (d?.TexturePath is null)
        {
            d = null;
        }

        float? envProp = null;
        foreach (var p in mat.Properties)
        {
            if (p.Tag == 0x01000001 && p.Payload.Length >= 4)
            {
                envProp = BitConverter.ToSingle(p.Payload.Span);
            }
        }

        var alpha = AlphaMode.Opaque;
        if ((mat.Flags2 & BlendBit) != 0 || mat.Diffuse.W < 0.99f)
        {
            alpha = AlphaMode.Blend;
        }
        else if (b?.TexturePath is { } bp && hasAlpha(bp))
        {
            alpha = AlphaMode.Test;
        }

        var spec = mat.Specular is { } sp ? new Vector3(sp.X, sp.Y, sp.Z) : new Vector3(0.2f);
        return new MaterialDesc
        {
            Name = mat.Name,
            Diffuse = Sane(mat.Diffuse),
            Specular = mat.Version == 3 ? Vector3.Zero : Vector3.Clamp(spec, Vector3.Zero, Vector3.One), // v3 (maps, old parts): no specular, GUESSED
            Power = mat.SpecularPower is { } pw && float.IsFinite(pw) && pw > 0 ? Math.Min(pw, 256) : 16,
            Base = b,
            Decal = d,
            Env = envStage is not null,
            EnvTexturePath = envStage is null ? null : Tex(envStage),
            EnvStrength = envStage is null ? 0 : Math.Clamp(envProp ?? 0.25f, 0, 1),
            Alpha = alpha,
            Flags = mat.Flags,
            Flags2 = mat.Flags2,
        };
    }

    private static Vector4 Sane(Vector4 c) =>
        float.IsFinite(c.X + c.Y + c.Z + c.W) ? Vector4.Clamp(c, Vector4.Zero, Vector4.One) : Vector4.One;
}
