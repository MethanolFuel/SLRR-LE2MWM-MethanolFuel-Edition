using System.Globalization;
using System.IO.Compression;
using System.Text;

namespace Slrr.World;

/// <summary>Top-down sampling of <see cref="IGroundQuery"/> for visual checks. X runs right, Z runs up (north = +Z at the top).</summary>
public sealed class HeightMap
{
    public HeightMap(float minX, float minZ, float cell, int width, int height)
    {
        MinX = minX;
        MinZ = minZ;
        Cell = cell;
        Width = width;
        Height = height;
        Heights = new float[width * height];
        Surfaces = new int[width * height];
    }

    public float MinX { get; }

    public float MinZ { get; }

    public float Cell { get; }

    public int Width { get; }

    public int Height { get; }

    public float[] Heights { get; }

    public int[] Surfaces { get; }

    private float _rampMin;
    private float _rampMax;

    public int Covered { get; private set; }

    public float MinHeight { get; private set; }

    public float MaxHeight { get; private set; }

    public static HeightMap Sample(IGroundQuery ground, float minX, float minZ, float maxX, float maxZ, float cell)
    {
        var w = Math.Max(1, (int)MathF.Ceiling((maxX - minX) / cell));
        var h = Math.Max(1, (int)MathF.Ceiling((maxZ - minZ) / cell));
        var map = new HeightMap(minX, minZ, cell, w, h);
        Parallel.For(0, h, row =>
        {
            for (var col = 0; col < w; col++)
            {
                var x = minX + ((col + 0.5f) * cell);
                var z = minZ + ((row + 0.5f) * cell);
                if (ground.TryGetGround(x, z, float.PositiveInfinity, out var hit))
                {
                    map.Heights[(row * w) + col] = hit.Position.Y;
                    map.Surfaces[(row * w) + col] = hit.Surface;
                }
                else
                {
                    map.Heights[(row * w) + col] = float.NaN;
                    map.Surfaces[(row * w) + col] = -1;
                }
            }
        });

        float lo = float.MaxValue, hi = float.MinValue;
        var covered = 0;
        foreach (var v in map.Heights)
        {
            if (!float.IsNaN(v))
            {
                covered++;
                lo = MathF.Min(lo, v);
                hi = MathF.Max(hi, v);
            }
        }

        map.Covered = covered;
        var finite = map.Heights.Where(v => !float.IsNaN(v)).ToArray();
        if (finite.Length > 0)
        {
            Array.Sort(finite);
            map._rampMin = finite[(int)(finite.Length * 0.02)];
            map._rampMax = finite[Math.Min(finite.Length - 1, (int)(finite.Length * 0.98))];
        }

        map.MinHeight = covered > 0 ? lo : 0;
        map.MaxHeight = covered > 0 ? hi : 0;
        return map;
    }

    /// <summary>RGB for a cell: a terrain-style ramp by height, or a class colour when <paramref name="bySurface"/>.</summary>
    public (byte R, byte G, byte B) Color(int col, int row, bool bySurface, SurfaceTable? table)
    {
        var i = (row * Width) + col;
        var h = Heights[i];
        if (float.IsNaN(h))
        {
            return (16, 16, 24);
        }

        if (bySurface && table is not null)
        {
            return table.Get(Surfaces[i]).Class switch
            {
                SurfaceClass.Asphalt => (90, 90, 100),
                SurfaceClass.Concrete => (170, 170, 160),
                SurfaceClass.Grass => (70, 150, 70),
                SurfaceClass.Sand => (220, 200, 120),
                SurfaceClass.Rock => (130, 110, 90),
                SurfaceClass.Metal => (160, 180, 210),
                SurfaceClass.Object => (220, 80, 80),
                SurfaceClass.Wall => (200, 150, 120),
                _ => (190, 190, 60),
            };
        }

        var t = _rampMax > _rampMin ? (h - _rampMin) / (_rampMax - _rampMin) : 0.5f;
        t = Math.Clamp(t, 0, 1);
        // blue -> green -> yellow -> red-brown
        var r = (byte)(255 * Math.Clamp((t - 0.4f) * 2.5f, 0, 1));
        var g = (byte)(255 * (t < 0.5f ? 0.3f + (t * 1.2f) : 0.9f - ((t - 0.5f) * 1.0f)));
        var b = (byte)(255 * Math.Clamp(1f - (t * 2.2f), 0.05f, 1));
        return (r, g, b);
    }

    public byte[] ToPng(bool bySurface = false, SurfaceTable? table = null)
    {
        var raw = new byte[(Width * 3 + 1) * Height];
        for (var row = 0; row < Height; row++)
        {
            var o = row * ((Width * 3) + 1);
            raw[o] = 0;
            var srcRow = Height - 1 - row; // flip: +Z up
            for (var col = 0; col < Width; col++)
            {
                var (r, g, b) = Color(col, srcRow, bySurface, table);
                raw[o + 1 + (col * 3)] = r;
                raw[o + 2 + (col * 3)] = g;
                raw[o + 3 + (col * 3)] = b;
            }
        }

        using var ms = new MemoryStream();
        ms.Write([0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A]);
        var ihdr = new byte[13];
        WriteBe(ihdr, 0, Width);
        WriteBe(ihdr, 4, Height);
        ihdr[8] = 8;
        ihdr[9] = 2;
        Chunk(ms, "IHDR", ihdr);
        using (var z = new MemoryStream())
        {
            using (var zs = new ZLibStream(z, CompressionLevel.Fastest, true))
            {
                zs.Write(raw);
            }

            Chunk(ms, "IDAT", z.ToArray());
        }

        Chunk(ms, "IEND", []);
        return ms.ToArray();
    }

    /// <summary>SVG with run-length merged rects (one per horizontal run of equal colour). Intended for maps up to a few hundred cells wide.</summary>
    public string ToSvg(bool bySurface = false, SurfaceTable? table = null)
    {
        var sb = new StringBuilder();
        var inv = CultureInfo.InvariantCulture;
        sb.Append(inv, $"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{Width}\" height=\"{Height}\" viewBox=\"0 0 {Width} {Height}\" shape-rendering=\"crispEdges\">\n");
        sb.Append(inv, $"<!-- x {MinX}..{MinX + (Width * Cell)} z {MinZ}..{MinZ + (Height * Cell)} cell {Cell} height {MinHeight}..{MaxHeight} covered {Covered}/{Width * Height} -->\n");
        sb.Append("<rect width=\"100%\" height=\"100%\" fill=\"#101018\"/>\n");
        for (var row = 0; row < Height; row++)
        {
            var srcRow = Height - 1 - row;
            var col = 0;
            while (col < Width)
            {
                var c = Color(col, srcRow, bySurface, table);
                var end = col + 1;
                while (end < Width && Color(end, srcRow, bySurface, table) == c)
                {
                    end++;
                }

                if (!float.IsNaN(Heights[(srcRow * Width) + col]))
                {
                    sb.Append(inv, $"<rect x=\"{col}\" y=\"{row}\" width=\"{end - col}\" height=\"1\" fill=\"#{c.R:x2}{c.G:x2}{c.B:x2}\"/>\n");
                }

                col = end;
            }
        }

        sb.Append("</svg>\n");
        return sb.ToString();
    }

    private static void WriteBe(byte[] b, int o, int v)
    {
        b[o] = (byte)(v >> 24);
        b[o + 1] = (byte)(v >> 16);
        b[o + 2] = (byte)(v >> 8);
        b[o + 3] = (byte)v;
    }

    private static void Chunk(Stream s, string type, byte[] data)
    {
        var len = new byte[4];
        WriteBe(len, 0, data.Length);
        s.Write(len);
        var td = new byte[4 + data.Length];
        Encoding.ASCII.GetBytes(type, td);
        data.CopyTo(td, 4);
        s.Write(td);
        var crc = new byte[4];
        WriteBe(crc, 0, unchecked((int)Crc32(td)));
        s.Write(crc);
    }

    private static uint Crc32(byte[] data)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var b in data)
        {
            crc ^= b;
            for (var k = 0; k < 8; k++)
            {
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
            }
        }

        return ~crc;
    }
}
