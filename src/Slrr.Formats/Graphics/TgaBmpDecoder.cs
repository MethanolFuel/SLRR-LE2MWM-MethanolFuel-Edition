using System.Buffers.Binary;
using Slrr.Core;

namespace Slrr.Formats.Graphics;

/// <summary>
/// Truevision TGA reader: image types 1/2/3 (colour-mapped, true colour, greyscale) and their RLE variants 9/10/11,
/// 8/15/16/24/32 bits per pixel, both origins. Shipped content: type 10/32bpp (10 files), type 1/8bpp (7),
/// type 3/8bpp (3), type 2/32bpp (2).
/// </summary>
public static class TgaDecoder
{
    public static RgbaImage Decode(ReadOnlySpan<byte> d)
    {
        if (d.Length < 18)
        {
            throw new SlrrFormatException("TGA: short header");
        }

        int idLen = d[0], cmapType = d[1], type = d[2];
        int cmapFirst = BinaryPrimitives.ReadUInt16LittleEndian(d[3..]), cmapLen = BinaryPrimitives.ReadUInt16LittleEndian(d[5..]), cmapBits = d[7];
        int w = BinaryPrimitives.ReadUInt16LittleEndian(d[12..]), h = BinaryPrimitives.ReadUInt16LittleEndian(d[14..]);
        int bpp = d[16], desc = d[17];
        RgbaImage.Check(w, h);
        var baseType = type & 7;
        if (baseType is not (1 or 2 or 3) || (type & ~0xB) != 0)
        {
            throw new SlrrFormatException($"TGA: unsupported image type {type}");
        }

        var pos = 18 + idLen;
        byte[]? cmap = null;
        if (cmapType == 1)
        {
            var entryBytes = (cmapBits + 7) / 8;
            var size = cmapLen * entryBytes;
            if (pos + size > d.Length)
            {
                throw new SlrrFormatException("TGA: colour map truncated");
            }

            cmap = new byte[cmapLen * 4];
            for (var i = 0; i < cmapLen; i++)
            {
                ReadColor(d.Slice(pos + (i * entryBytes), entryBytes), cmapBits, cmap.AsSpan(i * 4, 4));
            }

            pos += size;
        }

        var pixelBytes = (bpp + 7) / 8;
        if (pixelBytes is < 1 or > 4 || (baseType == 1 && cmap is null))
        {
            throw new SlrrFormatException($"TGA: {bpp} bpp / colour map {cmapType} not supported for type {type}");
        }

        var count = w * h;
        var pixels = new byte[count * pixelBytes];
        if ((type & 8) != 0)
        {
            var o = 0;
            while (o < pixels.Length)
            {
                if (pos >= d.Length)
                {
                    throw new SlrrFormatException("TGA: RLE data truncated");
                }

                var hdr = d[pos++];
                var n = (hdr & 0x7F) + 1;
                if (o + (n * pixelBytes) > pixels.Length)
                {
                    throw new SlrrFormatException("TGA: RLE packet overruns image");
                }

                if ((hdr & 0x80) != 0)
                {
                    if (pos + pixelBytes > d.Length)
                    {
                        throw new SlrrFormatException("TGA: RLE data truncated");
                    }

                    for (var k = 0; k < n; k++)
                    {
                        d.Slice(pos, pixelBytes).CopyTo(pixels.AsSpan(o));
                        o += pixelBytes;
                    }

                    pos += pixelBytes;
                }
                else
                {
                    if (pos + (n * pixelBytes) > d.Length)
                    {
                        throw new SlrrFormatException("TGA: RLE data truncated");
                    }

                    d.Slice(pos, n * pixelBytes).CopyTo(pixels.AsSpan(o));
                    o += n * pixelBytes;
                    pos += n * pixelBytes;
                }
            }
        }
        else
        {
            if (pos + pixels.Length > d.Length)
            {
                throw new SlrrFormatException("TGA: pixel data truncated");
            }

            d.Slice(pos, pixels.Length).CopyTo(pixels);
        }

        var img = new RgbaImage(w, h) { Format = $"TGA type{type} {bpp}bpp" };
        var topDown = (desc & 0x20) != 0;
        var rightToLeft = (desc & 0x10) != 0;
        Span<byte> c = stackalloc byte[4];
        for (var i = 0; i < count; i++)
        {
            var px = pixels.AsSpan(i * pixelBytes, pixelBytes);
            if (baseType == 1)
            {
                var idx = (pixelBytes == 1 ? px[0] : px[0] | (px[1] << 8)) - cmapFirst;
                if (idx < 0 || idx >= cmapLen)
                {
                    throw new SlrrFormatException($"TGA: colour index {idx + cmapFirst} outside map");
                }

                cmap.AsSpan(idx * 4, 4).CopyTo(c);
            }
            else if (baseType == 3)
            {
                c[0] = c[1] = c[2] = px[0];
                c[3] = pixelBytes >= 2 ? px[1] : (byte)255;
            }
            else
            {
                ReadColor(px, bpp, c);
            }

            int sx = i % w, sy = i / w;
            var x = rightToLeft ? w - 1 - sx : sx;
            var y = topDown ? sy : h - 1 - sy;
            img.Set(x, y, c[0], c[1], c[2], c[3]);
        }

        return img;
    }

    private static void ReadColor(ReadOnlySpan<byte> p, int bits, Span<byte> rgba)
    {
        switch (bits)
        {
            case 15:
            case 16:
            {
                var v = p[0] | (p[1] << 8);
                rgba[0] = (byte)(((v >> 10) & 31) * 255 / 31);
                rgba[1] = (byte)(((v >> 5) & 31) * 255 / 31);
                rgba[2] = (byte)((v & 31) * 255 / 31);
                rgba[3] = 255;
                break;
            }

            case 24:
                rgba[0] = p[2];
                rgba[1] = p[1];
                rgba[2] = p[0];
                rgba[3] = 255;
                break;
            case 32:
                rgba[0] = p[2];
                rgba[1] = p[1];
                rgba[2] = p[0];
                rgba[3] = p[3];
                break;
            case 8:
                rgba[0] = rgba[1] = rgba[2] = p[0];
                rgba[3] = 255;
                break;
            default:
                throw new SlrrFormatException($"TGA: {bits}-bit colours not supported");
        }
    }
}

/// <summary>Windows BMP reader for uncompressed 8/24/32-bit images (BI_RGB / BI_BITFIELDS treated as BGRX).</summary>
public static class BmpDecoder
{
    public static bool IsBmp(ReadOnlySpan<byte> d) => d.Length >= 26 && d[0] == (byte)'B' && d[1] == (byte)'M';

    public static RgbaImage Decode(ReadOnlySpan<byte> d)
    {
        if (!IsBmp(d) || d.Length < 54)
        {
            throw new SlrrFormatException("BMP: bad header");
        }

        var dataOffset = (int)Math.Min(BinaryPrimitives.ReadUInt32LittleEndian(d[10..]), int.MaxValue);
        var hdrSize = (int)Math.Min(BinaryPrimitives.ReadUInt32LittleEndian(d[14..]), 1024);
        var w = BinaryPrimitives.ReadInt32LittleEndian(d[18..]);
        var hRaw = BinaryPrimitives.ReadInt32LittleEndian(d[22..]);
        int bpp = BinaryPrimitives.ReadUInt16LittleEndian(d[28..]);
        var compression = BinaryPrimitives.ReadUInt32LittleEndian(d[30..]);
        var colors = (int)Math.Min(BinaryPrimitives.ReadUInt32LittleEndian(d[46..]), 256);
        var topDown = hRaw < 0;
        var h = hRaw == int.MinValue ? 0 : Math.Abs(hRaw);
        RgbaImage.Check(w, h);
        if (compression is not (0 or 3) || bpp is not (8 or 24 or 32))
        {
            throw new SlrrFormatException($"BMP: compression {compression} / {bpp} bpp not supported");
        }

        var palOffset = 14 + hdrSize;
        if (bpp == 8 && colors == 0)
        {
            colors = 256;
        }

        var stride = ((w * bpp) + 31) / 32 * 4;
        if (dataOffset < 0 || (long)dataOffset + ((long)stride * h) > d.Length || (bpp == 8 && palOffset + (colors * 4) > d.Length))
        {
            throw new SlrrFormatException("BMP: data truncated");
        }

        var img = new RgbaImage(w, h) { Format = $"BMP {bpp}bpp" };
        for (var row = 0; row < h; row++)
        {
            var y = topDown ? row : h - 1 - row;
            var line = d.Slice(dataOffset + (row * stride), stride);
            for (var x = 0; x < w; x++)
            {
                switch (bpp)
                {
                    case 8:
                    {
                        var idx = line[x];
                        if (idx >= colors)
                        {
                            throw new SlrrFormatException($"BMP: palette index {idx} out of range");
                        }

                        var p = d.Slice(palOffset + (idx * 4), 4);
                        img.Set(x, y, p[2], p[1], p[0], 255);
                        break;
                    }

                    case 24:
                        img.Set(x, y, line[(x * 3) + 2], line[(x * 3) + 1], line[x * 3], 255);
                        break;
                    default:
                        img.Set(x, y, line[(x * 4) + 2], line[(x * 4) + 1], line[x * 4], 255);
                        break;
                }
            }
        }

        return img;
    }
}
