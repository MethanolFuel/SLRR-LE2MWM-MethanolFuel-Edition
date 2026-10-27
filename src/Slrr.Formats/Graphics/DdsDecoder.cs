using System.Buffers.Binary;
using System.Numerics;
using Slrr.Core;

namespace Slrr.Formats.Graphics;

/// <summary>Parsed 128-byte DDS header (DX9 layout; the DX10 extension header is not supported).</summary>
public sealed record DdsHeader(
    int Width, int Height, int MipCount, uint Flags, uint PixelFlags, string FourCc, int RgbBitCount,
    uint RMask, uint GMask, uint BMask, uint AMask, uint Caps, uint Caps2)
{
    public bool IsCompressed => (PixelFlags & 0x4) != 0;
    public bool IsCubemap => (Caps2 & 0x200) != 0;

    /// <summary>e.g. <c>DXT1</c>, or <c>RGB32 A8</c> for uncompressed formats.</summary>
    public string Describe() => IsCompressed ? FourCc : $"RGB{RgbBitCount}{(AMask != 0 ? " A" + BitOperations.PopCount(AMask) : "")}";
}

/// <summary>
/// DirectDraw Surface reader: header, DXT1/DXT3/DXT5 block decoding and uncompressed mask-based formats
/// (8/16/24/32 bit, luminance). Only the top mip level of the first face is decoded. The shipped content uses
/// DXT1 (1,156 files), DXT5 (252), DXT3 (150), one 16-bit RGB file and one 32-bit ARGB cubemap.
/// </summary>
public static class DdsDecoder
{
    public static bool IsDds(ReadOnlySpan<byte> data) => data.Length >= 4 && data[..4].SequenceEqual("DDS "u8);

    public static DdsHeader ReadHeader(ReadOnlySpan<byte> data)
    {
        if (data.Length < 128 || !IsDds(data))
        {
            throw new SlrrFormatException("DDS: bad magic or short header");
        }

        if (U(data, 4) != 124 || U(data, 76) != 32)
        {
            throw new SlrrFormatException($"DDS: header size {U(data, 4)} / pixel format size {U(data, 76)}");
        }

        var fourCc = System.Text.Encoding.ASCII.GetString(data.Slice(84, 4)).TrimEnd('\0');
        var h = new DdsHeader((int)Math.Min(U(data, 16), int.MaxValue), (int)Math.Min(U(data, 12), int.MaxValue), (int)Math.Min(U(data, 28), 32), U(data, 8), U(data, 80), fourCc,
            (int)Math.Min(U(data, 88), 64), U(data, 92), U(data, 96), U(data, 100), U(data, 104), U(data, 108), U(data, 112));
        RgbaImage.Check(h.Width, h.Height);
        return h;
    }

    private static uint U(ReadOnlySpan<byte> data, int o) => BinaryPrimitives.ReadUInt32LittleEndian(data[o..]);

    public static RgbaImage Decode(ReadOnlySpan<byte> data)
    {
        var h = ReadHeader(data);
        var body = data[128..];
        var img = new RgbaImage(h.Width, h.Height) { Format = "DDS " + h.Describe() + (h.IsCubemap ? " cube" : "") };
        if (h.IsCompressed)
        {
            var blockSize = h.FourCc switch
            {
                "DXT1" => 8,
                "DXT2" or "DXT3" or "DXT4" or "DXT5" => 16,
                _ => throw new SlrrFormatException($"DDS: unsupported FourCC '{h.FourCc}'"),
            };
            var bw = (h.Width + 3) / 4;
            var bh = (h.Height + 3) / 4;
            if ((long)bw * bh * blockSize > body.Length)
            {
                throw new SlrrFormatException("DDS: compressed data truncated");
            }

            Span<byte> block = stackalloc byte[64];
            for (var by = 0; by < bh; by++)
            {
                for (var bx = 0; bx < bw; bx++)
                {
                    var b = body.Slice(((by * bw) + bx) * blockSize, blockSize);
                    switch (h.FourCc)
                    {
                        case "DXT1":
                            DecodeColorBlock(b, block, true);
                            break;
                        case "DXT2":
                        case "DXT3":
                            DecodeColorBlock(b[8..], block, false);
                            for (var i = 0; i < 16; i++)
                            {
                                var nib = (b[i / 2] >> ((i & 1) * 4)) & 15;
                                block[(i * 4) + 3] = (byte)(nib * 17);
                            }

                            break;
                        default:
                            DecodeColorBlock(b[8..], block, false);
                            DecodeAlphaBlock(b, block);
                            break;
                    }

                    for (var py = 0; py < 4; py++)
                    {
                        for (var px = 0; px < 4; px++)
                        {
                            int x = (bx * 4) + px, y = (by * 4) + py;
                            if (x < h.Width && y < h.Height)
                            {
                                var s = ((py * 4) + px) * 4;
                                img.Set(x, y, block[s], block[s + 1], block[s + 2], block[s + 3]);
                            }
                        }
                    }
                }
            }

            return img;
        }

        DecodeUncompressed(h, body, img);
        return img;
    }

    private static void Expand565(int c, out byte r, out byte g, out byte b)
    {
        r = (byte)((((c >> 11) & 31) * 255 + 15) / 31);
        g = (byte)((((c >> 5) & 63) * 255 + 31) / 63);
        b = (byte)(((c & 31) * 255 + 15) / 31);
    }

    /// <summary>Decodes a DXT colour block into 16 RGBA pixels. <paramref name="dxt1"/> enables the 3-colour + transparent mode.</summary>
    public static void DecodeColorBlock(ReadOnlySpan<byte> b, Span<byte> outRgba, bool dxt1)
    {
        var c0 = b[0] | (b[1] << 8);
        var c1 = b[2] | (b[3] << 8);
        Span<byte> pal = stackalloc byte[16];
        Expand565(c0, out pal[0], out pal[1], out pal[2]);
        Expand565(c1, out pal[4], out pal[5], out pal[6]);
        pal[3] = pal[7] = 255;
        if (c0 > c1 || !dxt1)
        {
            for (var k = 0; k < 3; k++)
            {
                pal[8 + k] = (byte)(((2 * pal[k]) + pal[4 + k] + 1) / 3);
                pal[12 + k] = (byte)((pal[k] + (2 * pal[4 + k]) + 1) / 3);
            }

            pal[11] = pal[15] = 255;
        }
        else
        {
            for (var k = 0; k < 3; k++)
            {
                pal[8 + k] = (byte)((pal[k] + pal[4 + k]) / 2);
                pal[12 + k] = 0;
            }

            pal[11] = 255;
            pal[15] = 0;
        }

        var bits = BinaryPrimitives.ReadUInt32LittleEndian(b[4..]);
        for (var i = 0; i < 16; i++)
        {
            var idx = (int)((bits >> (2 * i)) & 3);
            pal.Slice(idx * 4, 4).CopyTo(outRgba.Slice(i * 4, 4));
        }
    }

    /// <summary>Decodes a DXT5 interpolated alpha block (8 bytes) into the alpha bytes of 16 RGBA pixels.</summary>
    public static void DecodeAlphaBlock(ReadOnlySpan<byte> b, Span<byte> outRgba)
    {
        int a0 = b[0], a1 = b[1];
        Span<int> pal = stackalloc int[8];
        pal[0] = a0;
        pal[1] = a1;
        if (a0 > a1)
        {
            for (var i = 1; i < 7; i++)
            {
                pal[i + 1] = (((7 - i) * a0) + (i * a1) + 3) / 7;
            }
        }
        else
        {
            for (var i = 1; i < 5; i++)
            {
                pal[i + 1] = (((5 - i) * a0) + (i * a1) + 2) / 5;
            }

            pal[6] = 0;
            pal[7] = 255;
        }

        ulong bits = 0;
        for (var i = 0; i < 6; i++)
        {
            bits |= (ulong)b[2 + i] << (8 * i);
        }

        for (var i = 0; i < 16; i++)
        {
            outRgba[(i * 4) + 3] = (byte)pal[(int)((bits >> (3 * i)) & 7)];
        }
    }

    private static void DecodeUncompressed(DdsHeader h, ReadOnlySpan<byte> body, RgbaImage img)
    {
        var bytesPer = h.RgbBitCount / 8;
        if (bytesPer is < 1 or > 4)
        {
            throw new SlrrFormatException($"DDS: unsupported bit count {h.RgbBitCount}");
        }

        var pitch = h.Width * bytesPer;
        if ((long)pitch * h.Height > body.Length)
        {
            throw new SlrrFormatException("DDS: pixel data truncated");
        }

        var luminance = (h.PixelFlags & 0x20000) != 0;
        var hasAlpha = (h.PixelFlags & 0x3) != 0 && h.AMask != 0;
        for (var y = 0; y < h.Height; y++)
        {
            for (var x = 0; x < h.Width; x++)
            {
                var o = (y * pitch) + (x * bytesPer);
                uint v = 0;
                for (var k = 0; k < bytesPer; k++)
                {
                    v |= (uint)body[o + k] << (8 * k);
                }

                var a = hasAlpha ? Channel(v, h.AMask) : (byte)255;
                if (luminance)
                {
                    var l = Channel(v, h.RMask);
                    img.Set(x, y, l, l, l, a);
                }
                else
                {
                    img.Set(x, y, Channel(v, h.RMask), Channel(v, h.GMask), Channel(v, h.BMask), a);
                }
            }
        }
    }

    private static byte Channel(uint v, uint mask)
    {
        if (mask == 0)
        {
            return 0;
        }

        var shift = BitOperations.TrailingZeroCount(mask);
        var max = mask >> shift;
        return (byte)(((v & mask) >> shift) * 255 / max);
    }
}
