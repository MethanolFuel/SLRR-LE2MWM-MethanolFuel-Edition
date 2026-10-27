using System.Buffers.Binary;
using System.IO.Compression;
using Slrr.Core;

namespace Slrr.Formats.Graphics;

/// <summary>
/// Managed PNG reader (all colour types, bit depths 1..16, Adam7 interlacing, PLTE/tRNS) and a simple RGBA8
/// writer used for glTF export. Inflate/deflate use <see cref="ZLibStream"/> from the BCL.
/// </summary>
public static class PngCodec
{
    private static ReadOnlySpan<byte> Signature => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    private static readonly uint[] CrcTable = BuildCrcTable();

    public static bool IsPng(ReadOnlySpan<byte> data) => data.Length >= 8 && data[..8].SequenceEqual(Signature);

    public static RgbaImage Decode(ReadOnlySpan<byte> data)
    {
        if (!IsPng(data))
        {
            throw new SlrrFormatException("PNG: bad signature");
        }

        var pos = 8;
        int width = 0, height = 0, depth = 0, colorType = 0, interlace = 0;
        byte[]? palette = null;
        byte[]? trns = null;
        var idat = new MemoryStream();
        var seenHeader = false;
        while (pos + 12 <= data.Length)
        {
            var len = BinaryPrimitives.ReadUInt32BigEndian(data[pos..]);
            if (len > (uint)(data.Length - pos - 12))
            {
                throw new SlrrFormatException("PNG: chunk exceeds file");
            }

            var type = data.Slice(pos + 4, 4);
            var body = data.Slice(pos + 8, (int)len);
            pos += 12 + (int)len;
            if (type.SequenceEqual("IHDR"u8))
            {
                if (len < 13)
                {
                    throw new SlrrFormatException("PNG: short IHDR");
                }

                width = (int)Math.Min(BinaryPrimitives.ReadUInt32BigEndian(body), int.MaxValue);
                height = (int)Math.Min(BinaryPrimitives.ReadUInt32BigEndian(body[4..]), int.MaxValue);
                depth = body[8];
                colorType = body[9];
                interlace = body[12];
                RgbaImage.Check(width, height);
                seenHeader = true;
            }
            else if (type.SequenceEqual("PLTE"u8))
            {
                palette = body.ToArray();
            }
            else if (type.SequenceEqual("tRNS"u8))
            {
                trns = body.ToArray();
            }
            else if (type.SequenceEqual("IDAT"u8))
            {
                idat.Write(body);
            }
            else if (type.SequenceEqual("IEND"u8))
            {
                break;
            }
        }

        if (!seenHeader)
        {
            throw new SlrrFormatException("PNG: missing IHDR");
        }

        var channels = colorType switch
        {
            0 => 1,
            2 => 3,
            3 => 1,
            4 => 2,
            6 => 4,
            _ => throw new SlrrFormatException($"PNG: colour type {colorType}"),
        };
        if (depth is not (1 or 2 or 4 or 8 or 16) || (colorType != 0 && colorType != 3 && depth < 8) || (colorType == 3 && depth == 16))
        {
            throw new SlrrFormatException($"PNG: bit depth {depth} invalid for colour type {colorType}");
        }

        if (colorType == 3 && palette is null)
        {
            throw new SlrrFormatException("PNG: palette image without PLTE");
        }

        byte[] raw;
        idat.Position = 0;
        using (var z = new ZLibStream(idat, CompressionMode.Decompress))
        {
            var outBuf = new MemoryStream();
            var limit = (RgbaImage.MaxPixels * 8) + (2L * height) + 1024;
            var chunk = new byte[81920];
            int n;
            while ((n = z.Read(chunk)) > 0)
            {
                outBuf.Write(chunk, 0, n);
                if (outBuf.Length > limit)
                {
                    throw new SlrrFormatException("PNG: decompressed data too large");
                }
            }

            raw = outBuf.ToArray();
        }

        var img = new RgbaImage(width, height) { Format = $"PNG type{colorType} {depth}bit{(interlace == 1 ? " interlaced" : "")}" };
        var bitsPerPixel = channels * depth;
        var bpp = Math.Max(1, bitsPerPixel / 8);
        var src = 0;
        if (interlace == 0)
        {
            Pass(raw, ref src, img, width, height, 0, 0, 1, 1, bitsPerPixel, bpp, colorType, depth, palette, trns);
        }
        else
        {
            int[] x0 = [0, 4, 0, 2, 0, 1, 0], y0 = [0, 0, 4, 0, 2, 0, 1], dx = [8, 8, 4, 4, 2, 2, 1], dy = [8, 8, 8, 4, 4, 2, 2];
            for (var p = 0; p < 7; p++)
            {
                var pw = (width - x0[p] + dx[p] - 1) / dx[p];
                var ph = (height - y0[p] + dy[p] - 1) / dy[p];
                if (pw > 0 && ph > 0)
                {
                    Pass(raw, ref src, img, pw, ph, x0[p], y0[p], dx[p], dy[p], bitsPerPixel, bpp, colorType, depth, palette, trns);
                }
            }
        }

        return img;
    }

    private static void Pass(byte[] raw, ref int src, RgbaImage img, int w, int h, int x0, int y0, int dx, int dy, int bitsPerPixel, int bpp, int colorType, int depth, byte[]? palette, byte[]? trns)
    {
        var rowBytes = (int)(((long)w * bitsPerPixel + 7) / 8);
        var prev = new byte[rowBytes];
        var cur = new byte[rowBytes];
        for (var y = 0; y < h; y++)
        {
            if (src + 1 + rowBytes > raw.Length)
            {
                throw new SlrrFormatException("PNG: image data truncated");
            }

            var filter = raw[src];
            Array.Copy(raw, src + 1, cur, 0, rowBytes);
            src += 1 + rowBytes;
            Unfilter(filter, cur, prev, bpp);
            for (var x = 0; x < w; x++)
            {
                WritePixel(img, x0 + (x * dx), y0 + (y * dy), cur, x, colorType, depth, palette, trns);
            }

            (prev, cur) = (cur, prev);
        }
    }

    private static void Unfilter(byte filter, byte[] cur, byte[] prev, int bpp)
    {
        for (var i = 0; i < cur.Length; i++)
        {
            int a = i >= bpp ? cur[i - bpp] : 0, b = prev[i], c = i >= bpp ? prev[i - bpp] : 0;
            cur[i] = filter switch
            {
                0 => cur[i],
                1 => (byte)(cur[i] + a),
                2 => (byte)(cur[i] + b),
                3 => (byte)(cur[i] + ((a + b) >> 1)),
                4 => (byte)(cur[i] + Paeth(a, b, c)),
                _ => throw new SlrrFormatException($"PNG: filter type {filter}"),
            };
        }
    }

    private static int Paeth(int a, int b, int c)
    {
        var p = a + b - c;
        int pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c);
        return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
    }

    private static int Sample(byte[] row, int index, int depth)
    {
        switch (depth)
        {
            case 8:
                return row[index];
            case 16:
                return row[index * 2]; // high byte
            default:
                var bit = index * depth;
                var v = (row[bit >> 3] >> (8 - depth - (bit & 7))) & ((1 << depth) - 1);
                return v;
        }
    }

    private static int Raw16(byte[] row, int index) => (row[index * 2] << 8) | row[(index * 2) + 1];

    private static void WritePixel(RgbaImage img, int x, int y, byte[] row, int i, int colorType, int depth, byte[]? palette, byte[]? trns)
    {
        byte r, g, b, a = 255;
        switch (colorType)
        {
            case 0:
            {
                var v = Sample(row, i, depth);
                var scaled = depth >= 8 ? v : v * 255 / ((1 << depth) - 1);
                r = g = b = (byte)scaled;
                if (trns is { Length: >= 2 })
                {
                    var key = (trns[0] << 8) | trns[1];
                    var rawV = depth == 16 ? Raw16(row, i) : v;
                    if (rawV == key)
                    {
                        a = 0;
                    }
                }

                break;
            }

            case 2:
                r = (byte)Sample(row, i * 3, depth);
                g = (byte)Sample(row, (i * 3) + 1, depth);
                b = (byte)Sample(row, (i * 3) + 2, depth);
                if (trns is { Length: >= 6 })
                {
                    var kr = (trns[0] << 8) | trns[1];
                    var kg = (trns[2] << 8) | trns[3];
                    var kb = (trns[4] << 8) | trns[5];
                    bool match = depth == 16
                        ? Raw16(row, i * 3) == kr && Raw16(row, (i * 3) + 1) == kg && Raw16(row, (i * 3) + 2) == kb
                        : r == kr && g == kg && b == kb;
                    if (match)
                    {
                        a = 0;
                    }
                }

                break;
            case 3:
            {
                var idx = Sample(row, i, depth);
                if ((idx * 3) + 2 >= palette!.Length)
                {
                    throw new SlrrFormatException($"PNG: palette index {idx} out of range");
                }

                r = palette[idx * 3];
                g = palette[(idx * 3) + 1];
                b = palette[(idx * 3) + 2];
                if (trns is not null && idx < trns.Length)
                {
                    a = trns[idx];
                }

                break;
            }

            case 4:
                r = g = b = (byte)Sample(row, i * 2, depth);
                a = (byte)Sample(row, (i * 2) + 1, depth);
                break;
            default:
                r = (byte)Sample(row, i * 4, depth);
                g = (byte)Sample(row, (i * 4) + 1, depth);
                b = (byte)Sample(row, (i * 4) + 2, depth);
                a = (byte)Sample(row, (i * 4) + 3, depth);
                break;
        }

        img.Set(x, y, r, g, b, a);
    }

    /// <summary>Encodes an image as an RGBA8 (colour type 6) or RGB8 (type 2, when fully opaque) PNG.</summary>
    public static byte[] Encode(RgbaImage img)
    {
        var alpha = img.HasTransparency();
        var channels = alpha ? 4 : 3;
        var rowBytes = img.Width * channels;
        var raw = new byte[(rowBytes + 1) * img.Height];
        var o = 0;
        for (var y = 0; y < img.Height; y++)
        {
            raw[o++] = 0;
            for (var x = 0; x < img.Width; x++)
            {
                var s = ((y * img.Width) + x) * 4;
                raw[o++] = img.Pixels[s];
                raw[o++] = img.Pixels[s + 1];
                raw[o++] = img.Pixels[s + 2];
                if (alpha)
                {
                    raw[o++] = img.Pixels[s + 3];
                }
            }
        }

        var compressed = new MemoryStream();
        using (var z = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
        {
            z.Write(raw);
        }

        var ms = new MemoryStream();
        ms.Write(Signature);
        var ihdr = new byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(ihdr, (uint)img.Width);
        BinaryPrimitives.WriteUInt32BigEndian(ihdr.AsSpan(4), (uint)img.Height);
        ihdr[8] = 8;
        ihdr[9] = (byte)(alpha ? 6 : 2);
        WriteChunk(ms, "IHDR"u8, ihdr);
        WriteChunk(ms, "IDAT"u8, compressed.ToArray());
        WriteChunk(ms, "IEND"u8, []);
        return ms.ToArray();
    }

    private static void WriteChunk(Stream s, ReadOnlySpan<byte> type, ReadOnlySpan<byte> body)
    {
        Span<byte> b4 = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(b4, (uint)body.Length);
        s.Write(b4);
        s.Write(type);
        s.Write(body);
        var crc = Crc(0xFFFFFFFFu, type);
        crc = Crc(crc, body) ^ 0xFFFFFFFFu;
        BinaryPrimitives.WriteUInt32BigEndian(b4, crc);
        s.Write(b4);
    }

    private static uint Crc(uint crc, ReadOnlySpan<byte> data)
    {
        foreach (var b in data)
        {
            crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
        }

        return crc;
    }

    private static uint[] BuildCrcTable()
    {
        var t = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            var c = n;
            for (var k = 0; k < 8; k++)
            {
                c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            }

            t[n] = c;
        }

        return t;
    }
}
