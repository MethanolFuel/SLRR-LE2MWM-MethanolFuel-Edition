using Slrr.Core;
using Slrr.Formats.Ptx;

namespace Slrr.Formats.Graphics;

/// <summary>
/// Decodes any texture file the game references (DDS, PNG, JPEG, TGA, BMP, PTX) to <see cref="RgbaImage"/>.
/// The container is detected from the magic bytes; TGA and PTX (no magic) are recognised by extension.
/// </summary>
public static class TextureDecoder
{
    public static RgbaImage DecodeFile(string path) => Decode(File.ReadAllBytes(path), Path.GetExtension(path));

    public static RgbaImage Decode(byte[] data, string? extension)
    {
        var span = data.AsSpan();
        var ext = (extension ?? "").ToLowerInvariant();
        if (DdsDecoder.IsDds(span))
        {
            return DdsDecoder.Decode(span);
        }

        if (PngCodec.IsPng(span))
        {
            return PngCodec.Decode(span);
        }

        if (span.Length >= 3 && span[0] == 0xFF && span[1] == 0xD8 && span[2] == 0xFF)
        {
            return JpegDecoder.Decode(span);
        }

        if (ext == ".ptx")
        {
            return DecodePtx(PtxFile.Read(data));
        }

        if (BmpDecoder.IsBmp(span))
        {
            return BmpDecoder.Decode(span);
        }

        if (ext == ".tga")
        {
            return TgaDecoder.Decode(span);
        }

        throw new SlrrFormatException($"unrecognised texture format (extension '{ext}')");
    }

    /// <summary>
    /// Builds RGBA for one PTX frame: colour JPEG for RGB, luminance of the alpha JPEG for A (opaque when the frame
    /// has no alpha JPEG). An alpha image of a different size is sampled nearest-neighbour.
    /// </summary>
    public static RgbaImage DecodePtx(PtxFile ptx, int frame = 0)
    {
        if (frame < 0 || frame >= ptx.Frames.Count)
        {
            throw new SlrrFormatException($"PTX has {ptx.Frames.Count} frames, frame {frame} requested");
        }

        var f = ptx.Frames[frame];
        var color = JpegDecoder.Decode(f.ColorJpeg.Span);
        color.Format = "PTX JPEG";
        if (f.AlphaJpeg.Length == 0)
        {
            return color;
        }

        var alpha = JpegDecoder.Decode(f.AlphaJpeg.Span);
        color.Format = "PTX JPEG+alpha";
        for (var y = 0; y < color.Height; y++)
        {
            var ay = y * alpha.Height / color.Height;
            for (var x = 0; x < color.Width; x++)
            {
                var ax = x * alpha.Width / color.Width;
                color.Pixels[(((y * color.Width) + x) * 4) + 3] = alpha.Pixels[((ay * alpha.Width) + ax) * 4];
            }
        }

        return color;
    }
}
