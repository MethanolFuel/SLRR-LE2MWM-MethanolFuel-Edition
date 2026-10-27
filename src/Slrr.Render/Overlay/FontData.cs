using System.Numerics;
using Slrr.Formats.Scx;

namespace Slrr.Render.Overlay;

/// <summary>One glyph: quad corners in font units and its texture rectangle.</summary>
public readonly record struct Glyph(float X0, float Y0, float X1, float Y1, Vector2 Uv0, Vector2 Uv1)
{
    public bool IsEmpty => X1 <= X0;

    /// <summary>Pen advance in font units: the quad's right edge (GUESSED; quads start at -20 = 2 px of padding left of the pen).</summary>
    public float Advance => Math.Max(X1, 0);
}

/// <summary>
/// A game font = a render entry whose mesh is a v3 SCX of 256 quads (4 vertices each, quad i = character code i,
/// z = -100 * i) and whose texture is the glyph atlas (MEASURED on frontend\meshes\Fonts\*.scx: 1,024 vertices,
/// 512 triangles; quad 65 'A' = x -20..80, y 0..220, uv (0.355, 0.121)-(0.395, 0.207) of the 256x256 sl14.png).
/// Font units are 10 per texel (MEASURED: 100 units = 0.039 x 256 = 10 texels). Font y grows downwards on screen
/// (y = 0 has the smaller v; UV origin is top-left, graphics.md).
/// <c>frontend\font.dat</c> is the 135-character list <c>!</c>..<c>0xA7</c> the atlases were built from (MEASURED; not
/// needed for drawing); <c>font_pointers.dat</c> = "ABCDEFGHIJKLMN" names the symbol_pointers glyphs.
/// </summary>
public sealed class FontData
{
    public const float UnitsPerTexel = 10f;

    private FontData(Glyph[] glyphs, float lineHeight)
    {
        Glyphs = glyphs;
        LineHeight = lineHeight;
    }

    public IReadOnlyList<Glyph> Glyphs { get; }

    /// <summary>Tallest glyph quad (font units).</summary>
    public float LineHeight { get; }

    public static FontData FromScx(ScxFile scx)
    {
        ArgumentNullException.ThrowIfNull(scx);
        var glyphs = new Glyph[256];
        var mesh = scx.Meshes.FirstOrDefault();
        var height = 0f;
        if (mesh is not null)
        {
            var vb = mesh.Vertices;
            var layout = Slrr.Formats.Graphics.ScxVertexLayout.For(vb);
            for (var q = 0; q < 256 && (q * 4) + 3 < vb.Count; q++)
            {
                var min = new Vector2(float.MaxValue);
                var max = new Vector2(float.MinValue);
                var uvMin = new Vector2(float.MaxValue);
                var uvMax = new Vector2(float.MinValue);
                for (var k = 0; k < 4; k++)
                {
                    var p = vb.Position((q * 4) + k);
                    var uv = layout.ReadUv(vb, (q * 4) + k, 0);
                    min = Vector2.Min(min, new Vector2(p.X, p.Y));
                    max = Vector2.Max(max, new Vector2(p.X, p.Y));
                    uvMin = Vector2.Min(uvMin, uv);
                    uvMax = Vector2.Max(uvMax, uv);
                }

                if (!float.IsFinite(min.X + max.X + min.Y + max.Y))
                {
                    continue;
                }

                glyphs[q] = new Glyph(min.X, min.Y, max.X, max.Y, uvMin, uvMax);
                height = Math.Max(height, max.Y - min.Y);
            }
        }

        return new FontData(glyphs, height > 0 ? height : 200);
    }

    /// <summary>Width of a string in font units.</summary>
    public float Measure(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var w = 0f;
        foreach (var ch in text)
        {
            w += ch < 256 ? Glyphs[ch].Advance : Glyphs['?'].Advance;
        }

        return w;
    }
}
