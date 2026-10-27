using System.Numerics;
using Slrr.Render.Content;

namespace Slrr.Render.Overlay;

/// <summary>A textured rectangle in OSD coordinates (x, y in -1..1, y down, centre + full size; Osd.createBG = (0,0,2,2)).</summary>
public sealed record OverlayQuad(float X, float Y, float Width, float Height, string? TexturePath, Vector4 Color, float Depth, string Label);

/// <summary>A text in OSD coordinates; <see cref="Align"/> = Text.ALIGN_* (RIGHT 0, CENTER 1, LEFT 2).</summary>
public sealed record OverlayText(string Text, float X, float Y, ResRef? Font, Vector4 Color, int Align, float Depth);

/// <summary>What the 2D layer draws in one frame. Produced from the headless UI state by <see cref="UiBridge.UiOverlaySource"/> or by hand.</summary>
public sealed class OverlayFrame
{
    public List<OverlayQuad> Quads { get; } = [];

    public List<OverlayText> Texts { get; } = [];

    /// <summary>Game video mode (Config.video_x/y): fonts are drawn 1 texel = 1 pixel at this resolution (GUESSED) and scaled to the target.</summary>
    public int VideoWidth { get; set; } = 800;

    public int VideoHeight { get; set; } = 600;

    public List<string> Notes { get; } = [];
}

/// <summary>Source of overlay frames: the interface other areas implement to feed the 2D layer.</summary>
public interface IOverlaySource
{
    OverlayFrame Capture();
}

/// <summary>A screen-space sprite (pixels, origin top-left) ready to draw.</summary>
public readonly record struct Sprite(Vector2 P0, Vector2 P1, Vector2 Uv0, Vector2 Uv1, string? Texture, Vector4 Color);

/// <summary>Lays out an <see cref="OverlayFrame"/> into sprites for a target size; pure, testable.</summary>
public static class OverlayLayout
{
    /// <param name="frame">Overlay content.</param>
    /// <param name="width">Target width in pixels.</param>
    /// <param name="height">Target height in pixels.</param>
    /// <param name="font">Font lookup (render ref -> glyphs + atlas path).</param>
    public static List<Sprite> Layout(OverlayFrame frame, int width, int height, Func<ResRef?, (FontData Font, string? Atlas)?> font)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(font);
        var items = new List<(float Depth, int Order, List<Sprite> Sprites)>();
        var order = 0;
        foreach (var q in frame.Quads)
        {
            var c = new Vector2((q.X + 1) * 0.5f * width, (q.Y + 1) * 0.5f * height);
            var half = new Vector2(q.Width * 0.25f * width, q.Height * 0.25f * height);
            items.Add((q.Depth, order++, [new Sprite(c - half, c + half, Vector2.Zero, Vector2.One, q.TexturePath, q.Color)]));
        }

        var scale = height / (float)Math.Max(frame.VideoHeight, 1) / FontData.UnitsPerTexel;
        foreach (var t in frame.Texts)
        {
            if (font(t.Font) is not { } f)
            {
                continue;
            }

            var sprites = new List<Sprite>();
            var w = f.Font.Measure(t.Text) * scale;
            var x = ((t.X + 1) * 0.5f * width) - (t.Align switch { 0 => w, 1 => w * 0.5f, _ => 0 });
            var y = (t.Y + 1) * 0.5f * height;
            foreach (var ch in t.Text)
            {
                var g = f.Font.Glyphs[ch < 256 ? ch : '?'];
                if (!g.IsEmpty && g.Y1 > g.Y0)
                {
                    sprites.Add(new Sprite(new Vector2(x + (g.X0 * scale), y + (g.Y0 * scale)), new Vector2(x + (g.X1 * scale), y + (g.Y1 * scale)), g.Uv0, g.Uv1, f.Atlas, t.Color));
                }

                x += g.Advance * scale;
            }

            items.Add((t.Depth + 1e-4f, order++, sprites));
        }

        return items.OrderBy(i => i.Depth).ThenBy(i => i.Order).SelectMany(i => i.Sprites).ToList();
    }

    /// <summary>Signed ARGB int (Text colours, e.g. Osd.COLOR_SELECTED 0xFFFFCB00) to RGBA.</summary>
    public static Vector4 Argb(int argb)
    {
        var u = unchecked((uint)argb);
        return new Vector4(((u >> 16) & 0xFF) / 255f, ((u >> 8) & 0xFF) / 255f, (u & 0xFF) / 255f, ((u >> 24) & 0xFF) / 255f);
    }
}
