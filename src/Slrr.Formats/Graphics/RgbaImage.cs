using Slrr.Core;

namespace Slrr.Formats.Graphics;

/// <summary>A decoded texture: 8-bit RGBA, rows top to bottom, no padding.</summary>
public sealed class RgbaImage
{
    /// <summary>Upper bound for decoded images (64 M pixels) so hostile headers cannot force huge allocations.</summary>
    public const long MaxPixels = 64L * 1024 * 1024;

    public RgbaImage(int width, int height)
    {
        Check(width, height);
        Width = width;
        Height = height;
        Pixels = new byte[width * height * 4];
    }

    public int Width { get; }
    public int Height { get; }

    /// <summary>RGBA bytes, <c>Width * Height * 4</c>.</summary>
    public byte[] Pixels { get; }

    /// <summary>Short description of the source encoding (e.g. <c>DXT5</c>, <c>PNG rgba8</c>).</summary>
    public string Format { get; set; } = "";

    public bool HasTransparency()
    {
        for (var i = 3; i < Pixels.Length; i += 4)
        {
            if (Pixels[i] != 255)
            {
                return true;
            }
        }

        return false;
    }

    public void Set(int x, int y, byte r, byte g, byte b, byte a)
    {
        var o = ((y * Width) + x) * 4;
        Pixels[o] = r;
        Pixels[o + 1] = g;
        Pixels[o + 2] = b;
        Pixels[o + 3] = a;
    }

    public static void Check(long width, long height)
    {
        if (width <= 0 || height <= 0 || width * height > MaxPixels)
        {
            throw new SlrrFormatException($"image size {width}x{height} is invalid or too large");
        }
    }
}
