using System.Buffers.Binary;
using System.IO.Compression;

namespace Slrr.Viewer;

/// <summary>Minimal dependency-free PNG encoder (8-bit RGBA, filter 0, zlib via System.IO.Compression).</summary>
public static class PngWriter
{
    private static readonly uint[] CrcTable = BuildCrcTable();

    /// <summary>Encodes tightly packed RGBA rows. <paramref name="flipVertically"/> converts glReadPixels' bottom-up order.</summary>
    public static byte[] Encode(ReadOnlySpan<byte> rgba, int width, int height, bool flipVertically = false)
    {
        if (width <= 0 || height <= 0 || rgba.Length != (long)width * height * 4)
        {
            throw new ArgumentException($"expected {width}x{height}x4 bytes, got {rgba.Length}");
        }

        var raw = new byte[(long)height * ((width * 4) + 1)];
        for (var y = 0; y < height; y++)
        {
            var src = flipVertically ? height - 1 - y : y;
            var dst = y * ((width * 4) + 1);
            raw[dst] = 0;
            rgba.Slice(src * width * 4, width * 4).CopyTo(raw.AsSpan(dst + 1));
        }

        byte[] compressed;
        using (var ms = new MemoryStream())
        {
            using (var z = new ZLibStream(ms, CompressionLevel.Optimal, leaveOpen: true))
            {
                z.Write(raw);
            }

            compressed = ms.ToArray();
        }

        using var output = new MemoryStream();
        output.Write([0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A]);
        var ihdr = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(ihdr, width);
        BinaryPrimitives.WriteInt32BigEndian(ihdr.AsSpan(4), height);
        ihdr[8] = 8;  // bit depth
        ihdr[9] = 6;  // colour type RGBA
        WriteChunk(output, "IHDR", ihdr);
        WriteChunk(output, "IDAT", compressed);
        WriteChunk(output, "IEND", []);
        return output.ToArray();
    }

    private static void WriteChunk(Stream s, string type, ReadOnlySpan<byte> data)
    {
        Span<byte> buf = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(buf, data.Length);
        s.Write(buf);
        Span<byte> typeBytes = [(byte)type[0], (byte)type[1], (byte)type[2], (byte)type[3]];
        s.Write(typeBytes);
        s.Write(data);
        var crc = Crc(Crc(0xFFFFFFFFu, typeBytes), data) ^ 0xFFFFFFFFu;
        BinaryPrimitives.WriteUInt32BigEndian(buf, crc);
        s.Write(buf);
    }

    internal static uint Crc(uint crc, ReadOnlySpan<byte> data)
    {
        foreach (var b in data)
        {
            crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
        }

        return crc;
    }

    private static uint[] BuildCrcTable()
    {
        var table = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            var c = n;
            for (var k = 0; k < 8; k++)
            {
                c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            }

            table[n] = c;
        }

        return table;
    }
}
