using Slrr.Core;

namespace Slrr.Formats.Graphics;

/// <summary>
/// Minimal managed baseline JPEG decoder (SOF0/SOF1, Huffman, 8-bit, 1 or 3 components, any sampling factors,
/// restart intervals). Progressive and arithmetic-coded JPEGs are rejected. This covers every JPEG embedded in the
/// shipped PTX files (339 colour frames: 3 components, 2x2/1x1/1x1 sampling; 238 alpha frames: 1 component).
/// Three-component images are converted from YCbCr (JFIF) to RGB.
/// </summary>
public sealed class JpegDecoder
{
    private static readonly int[] ZigZag =
    [
        0, 1, 8, 16, 9, 2, 3, 10, 17, 24, 32, 25, 18, 11, 4, 5, 12, 19, 26, 33, 40, 48, 41, 34, 27, 20, 13, 6, 7, 14, 21, 28,
        35, 42, 49, 56, 57, 50, 43, 36, 29, 22, 15, 23, 30, 37, 44, 51, 58, 59, 52, 45, 38, 31, 39, 46, 53, 60, 61, 54, 47, 55, 62, 63,
    ];

    private static readonly float[] IdctTable = BuildIdct();

    private readonly byte[] _data;
    private readonly int[][] _quant = new int[4][];
    private readonly Huffman?[] _dc = new Huffman?[4];
    private readonly Huffman?[] _ac = new Huffman?[4];
    private Component[] _components = [];
    private int _width;
    private int _height;
    private int _hmax = 1;
    private int _vmax = 1;
    private int _mcusX;
    private int _mcusY;
    private int _restartInterval;
    private int _pos;
    private uint _bitBuf;
    private int _bitCount;

    private JpegDecoder(byte[] data) => _data = data;

    public static RgbaImage Decode(ReadOnlySpan<byte> data) => new JpegDecoder(data.ToArray()).Run();

    private RgbaImage Run()
    {
        if (_data.Length < 4 || _data[0] != 0xFF || _data[1] != 0xD8)
        {
            throw new SlrrFormatException("JPEG: missing SOI marker");
        }

        _pos = 2;
        var frameSeen = false;
        var scans = 0;
        while (true)
        {
            var marker = NextMarker();
            if (marker == 0xD9)
            {
                break;
            }

            if (marker is >= 0xD0 and <= 0xD7 or 0x01)
            {
                continue;
            }

            var len = ReadU16();
            if (len < 2 || _pos + len - 2 > _data.Length)
            {
                throw new SlrrFormatException($"JPEG: segment 0x{marker:X2} length {len} exceeds data");
            }

            var segEnd = _pos + len - 2;
            switch (marker)
            {
                case 0xDB:
                    ReadDqt(segEnd);
                    break;
                case 0xC4:
                    ReadDht(segEnd);
                    break;
                case 0xC0:
                case 0xC1:
                    ReadSof();
                    frameSeen = true;
                    break;
                case 0xDD:
                    _restartInterval = ReadU16();
                    break;
                case 0xDA:
                    if (!frameSeen)
                    {
                        throw new SlrrFormatException("JPEG: SOS before SOF");
                    }

                    ReadScanHeaderAndDecode(segEnd);
                    scans++;
                    continue; // _pos already points after entropy-coded data
                default:
                    if (marker is >= 0xC2 and <= 0xCF and not 0xC4 and not 0xC8 and not 0xCC)
                    {
                        throw new SlrrFormatException($"JPEG: unsupported frame type SOF{marker - 0xC0} (only baseline is implemented)");
                    }

                    break; // APPn, COM, DNL...: skip
            }

            _pos = segEnd;
            if (_pos >= _data.Length)
            {
                break;
            }
        }

        if (!frameSeen || scans == 0)
        {
            throw new SlrrFormatException("JPEG: no frame or scan");
        }

        return Output();
    }

    private int NextMarker()
    {
        // Skip fill bytes / garbage until 0xFF xx with xx != 0 and xx != 0xFF.
        while (_pos < _data.Length - 1)
        {
            if (_data[_pos] == 0xFF && _data[_pos + 1] != 0x00 && _data[_pos + 1] != 0xFF)
            {
                var m = _data[_pos + 1];
                _pos += 2;
                return m;
            }

            _pos++;
        }

        return 0xD9; // treat truncated stream as EOI
    }

    private int ReadU16()
    {
        if (_pos + 2 > _data.Length)
        {
            throw new SlrrFormatException("JPEG: truncated");
        }

        var v = (_data[_pos] << 8) | _data[_pos + 1];
        _pos += 2;
        return v;
    }

    private byte ReadU8() => _pos < _data.Length ? _data[_pos++] : throw new SlrrFormatException("JPEG: truncated");

    private void ReadDqt(int end)
    {
        while (_pos < end)
        {
            var pq = ReadU8();
            var tq = pq & 15;
            var precision16 = (pq >> 4) != 0;
            if (tq > 3)
            {
                throw new SlrrFormatException($"JPEG: quantisation table id {tq}");
            }

            var t = new int[64];
            for (var i = 0; i < 64; i++)
            {
                t[ZigZag[i]] = precision16 ? ReadU16() : ReadU8();
            }

            _quant[tq] = t;
        }
    }

    private void ReadDht(int end)
    {
        while (_pos < end)
        {
            var tc = ReadU8();
            var cls = tc >> 4;
            var id = tc & 15;
            if (id > 3 || cls > 1)
            {
                throw new SlrrFormatException($"JPEG: Huffman table class {cls} id {id}");
            }

            var counts = new int[17];
            var total = 0;
            for (var i = 1; i <= 16; i++)
            {
                counts[i] = ReadU8();
                total += counts[i];
            }

            if (total > 256 || _pos + total > end)
            {
                throw new SlrrFormatException("JPEG: bad Huffman table");
            }

            var symbols = new byte[total];
            Array.Copy(_data, _pos, symbols, 0, total);
            _pos += total;
            var h = new Huffman(counts, symbols);
            if (cls == 0)
            {
                _dc[id] = h;
            }
            else
            {
                _ac[id] = h;
            }
        }
    }

    private void ReadSof()
    {
        var precision = ReadU8();
        if (precision != 8)
        {
            throw new SlrrFormatException($"JPEG: {precision}-bit samples not supported");
        }

        _height = ReadU16();
        _width = ReadU16();
        RgbaImage.Check(_width, _height);
        var n = ReadU8();
        if (n is not (1 or 3))
        {
            throw new SlrrFormatException($"JPEG: {n} components not supported");
        }

        _components = new Component[n];
        for (var i = 0; i < n; i++)
        {
            var id = ReadU8();
            var hv = ReadU8();
            var tq = ReadU8();
            var c = new Component { Id = id, H = hv >> 4, V = hv & 15, Tq = tq & 3 };
            if (c.H is < 1 or > 4 || c.V is < 1 or > 4)
            {
                throw new SlrrFormatException("JPEG: bad sampling factors");
            }

            _components[i] = c;
        }

        _hmax = _components.Max(c => c.H);
        _vmax = _components.Max(c => c.V);
        _mcusX = (_width + (8 * _hmax) - 1) / (8 * _hmax);
        _mcusY = (_height + (8 * _vmax) - 1) / (8 * _vmax);
        foreach (var c in _components)
        {
            c.BlocksX = _mcusX * c.H;
            c.BlocksY = _mcusY * c.V;
            c.Plane = new byte[c.BlocksX * 8 * c.BlocksY * 8];
        }
    }

    private void ReadScanHeaderAndDecode(int headerEnd)
    {
        var ns = ReadU8();
        if (ns < 1 || ns > _components.Length)
        {
            throw new SlrrFormatException($"JPEG: scan with {ns} components");
        }

        var scan = new Component[ns];
        for (var i = 0; i < ns; i++)
        {
            var id = ReadU8();
            var t = ReadU8();
            var c = _components.FirstOrDefault(x => x.Id == id) ?? throw new SlrrFormatException($"JPEG: scan references unknown component {id}");
            c.Td = t >> 4;
            c.Ta = t & 15;
            if (c.Td > 3 || c.Ta > 3 || _dc[c.Td] is null || _ac[c.Ta] is null || _quant[c.Tq] is null)
            {
                throw new SlrrFormatException("JPEG: scan references a missing table");
            }

            scan[i] = c;
        }

        _pos = headerEnd; // skip Ss, Se, Ah/Al (fixed for baseline)
        _bitBuf = 0;
        _bitCount = 0;
        foreach (var c in scan)
        {
            c.Pred = 0;
        }

        var block = new int[64];
        var mcu = 0;
        if (ns == 1)
        {
            var c = scan[0];
            var bw = (((_width * c.H) + _hmax - 1) / _hmax + 7) / 8;
            var bh = (((_height * c.V) + _vmax - 1) / _vmax + 7) / 8;
            for (var by = 0; by < bh; by++)
            {
                for (var bx = 0; bx < bw; bx++)
                {
                    HandleRestart(mcu++, scan);
                    DecodeBlock(c, block);
                    StoreBlock(c, block, bx, by);
                }
            }
        }
        else
        {
            for (var my = 0; my < _mcusY; my++)
            {
                for (var mx = 0; mx < _mcusX; mx++)
                {
                    HandleRestart(mcu++, scan);
                    foreach (var c in scan)
                    {
                        for (var v = 0; v < c.V; v++)
                        {
                            for (var h = 0; h < c.H; h++)
                            {
                                DecodeBlock(c, block);
                                StoreBlock(c, block, (mx * c.H) + h, (my * c.V) + v);
                            }
                        }
                    }
                }
            }
        }

        // Advance to the next marker (not a stuffed byte, not RST).
        while (_pos < _data.Length - 1)
        {
            if (_data[_pos] == 0xFF && _data[_pos + 1] != 0 && _data[_pos + 1] is not (>= 0xD0 and <= 0xD7))
            {
                break;
            }

            _pos++;
        }
    }

    private void HandleRestart(int mcu, Component[] scan)
    {
        if (_restartInterval == 0 || mcu == 0 || mcu % _restartInterval != 0)
        {
            return;
        }

        _bitBuf = 0;
        _bitCount = 0;
        while (_pos < _data.Length - 1)
        {
            if (_data[_pos] == 0xFF && _data[_pos + 1] is >= 0xD0 and <= 0xD7)
            {
                _pos += 2;
                break;
            }

            _pos++;
        }

        foreach (var c in scan)
        {
            c.Pred = 0;
        }
    }

    private int Bit()
    {
        if (_bitCount == 0)
        {
            byte b = 0;
            if (_pos < _data.Length)
            {
                b = _data[_pos];
                if (b == 0xFF)
                {
                    var next = _pos + 1 < _data.Length ? _data[_pos + 1] : (byte)0;
                    if (next == 0)
                    {
                        _pos += 2;
                    }
                    else
                    {
                        b = 0; // marker: feed zeros, do not consume
                    }
                }
                else
                {
                    _pos++;
                }
            }

            _bitBuf = b;
            _bitCount = 8;
        }

        _bitCount--;
        return (int)((_bitBuf >> _bitCount) & 1);
    }

    private int Bits(int n)
    {
        var v = 0;
        for (var i = 0; i < n; i++)
        {
            v = (v << 1) | Bit();
        }

        return v;
    }

    private static int Extend(int v, int n) => n == 0 ? 0 : v < (1 << (n - 1)) ? v - (1 << n) + 1 : v;

    private int DecodeHuffman(Huffman h)
    {
        var code = 0;
        for (var len = 1; len <= 16; len++)
        {
            code = (code << 1) | Bit();
            if (code <= h.MaxCode[len])
            {
                return h.Symbols[h.ValPtr[len] + code - h.MinCode[len]];
            }
        }

        throw new SlrrFormatException("JPEG: bad Huffman code");
    }

    private void DecodeBlock(Component c, int[] block)
    {
        Array.Clear(block);
        var q = _quant[c.Tq];
        var t = DecodeHuffman(_dc[c.Td]!);
        var diff = t == 0 ? 0 : Extend(Bits(t), t);
        c.Pred += diff;
        block[0] = c.Pred * q[0];
        var k = 1;
        var ac = _ac[c.Ta]!;
        while (k < 64)
        {
            var rs = DecodeHuffman(ac);
            var r = rs >> 4;
            var s = rs & 15;
            if (s == 0)
            {
                if (r == 15)
                {
                    k += 16;
                    continue;
                }

                break;
            }

            k += r;
            if (k > 63)
            {
                break;
            }

            var z = ZigZag[k];
            block[z] = Extend(Bits(s), s) * q[z];
            k++;
        }
    }

    private static void StoreBlock(Component c, int[] block, int bx, int by)
    {
        Span<float> tmp = stackalloc float[64];
        for (var y = 0; y < 8; y++)
        {
            for (var x = 0; x < 8; x++)
            {
                var s = 0f;
                for (var u = 0; u < 8; u++)
                {
                    s += IdctTable[(x * 8) + u] * block[(y * 8) + u];
                }

                tmp[(y * 8) + x] = s;
            }
        }

        var stride = c.BlocksX * 8;
        if (bx >= c.BlocksX || by >= c.BlocksY)
        {
            return;
        }

        for (var x = 0; x < 8; x++)
        {
            for (var y = 0; y < 8; y++)
            {
                var s = 0f;
                for (var v = 0; v < 8; v++)
                {
                    s += IdctTable[(y * 8) + v] * tmp[(v * 8) + x];
                }

                var val = (int)MathF.Round(s + 128f);
                c.Plane[((by * 8) + y) * stride + (bx * 8) + x] = (byte)Math.Clamp(val, 0, 255);
            }
        }
    }

    private static float[] BuildIdct()
    {
        var t = new float[64];
        for (var x = 0; x < 8; x++)
        {
            for (var u = 0; u < 8; u++)
            {
                var cu = u == 0 ? 1f / MathF.Sqrt(2f) : 1f;
                t[(x * 8) + u] = cu * MathF.Cos(((2 * x) + 1) * u * MathF.PI / 16f) / 2f;
            }
        }

        return t;
    }

    private RgbaImage Output()
    {
        var img = new RgbaImage(_width, _height) { Format = _components.Length == 1 ? "JPEG grey" : "JPEG YCbCr" };
        var px = img.Pixels;
        for (var y = 0; y < _height; y++)
        {
            for (var x = 0; x < _width; x++)
            {
                var o = ((y * _width) + x) * 4;
                if (_components.Length == 1)
                {
                    var g = (byte)Sample(_components[0], x, y);
                    px[o] = px[o + 1] = px[o + 2] = g;
                }
                else
                {
                    float yy = Sample(_components[0], x, y);
                    var cb = Sample(_components[1], x, y) - 128f;
                    var cr = Sample(_components[2], x, y) - 128f;
                    px[o] = ClampByte(yy + (1.402f * cr));
                    px[o + 1] = ClampByte(yy - (0.344136f * cb) - (0.714136f * cr));
                    px[o + 2] = ClampByte(yy + (1.772f * cb));
                }

                px[o + 3] = 255;
            }
        }

        return img;
    }

    /// <summary>
    /// Component sample at full-resolution pixel (x, y). Planes subsampled by exactly 2 in a direction use the
    /// libjpeg "fancy" triangle filter (3/4 nearer + 1/4 farther sample); other ratios replicate samples.
    /// </summary>
    private int Sample(Component c, int x, int y)
    {
        var stride = c.BlocksX * 8;
        var compW = ((_width * c.H) + _hmax - 1) / _hmax;
        var compH = ((_height * c.V) + _vmax - 1) / _vmax;
        var fx = _hmax == 2 * c.H;
        var fy = _vmax == 2 * c.V;
        if (!fx && !fy)
        {
            return c.Plane[(y * c.V / _vmax * stride) + (x * c.H / _hmax)];
        }

        int x0, x1, y0, y1;
        if (fx)
        {
            x0 = x >> 1;
            x1 = Math.Clamp((x & 1) == 0 ? x0 - 1 : x0 + 1, 0, compW - 1);
        }
        else
        {
            x0 = x1 = x * c.H / _hmax;
        }

        if (fy)
        {
            y0 = y >> 1;
            y1 = Math.Clamp((y & 1) == 0 ? y0 - 1 : y0 + 1, 0, compH - 1);
        }
        else
        {
            y0 = y1 = y * c.V / _vmax;
        }

        int P(int px, int py) => c.Plane[(py * stride) + px];
        if (fx && fy)
        {
            var v = (9 * P(x0, y0)) + (3 * P(x1, y0)) + (3 * P(x0, y1)) + P(x1, y1);
            return (v + 8) >> 4;
        }

        return ((3 * P(x0, y0)) + P(x1, y1) + 2) >> 2;
    }

    private static byte ClampByte(float v) => (byte)Math.Clamp((int)MathF.Round(v), 0, 255);

    private sealed class Component
    {
        public int Id;
        public int H;
        public int V;
        public int Tq;
        public int Td;
        public int Ta;
        public int BlocksX;
        public int BlocksY;
        public int Pred;
        public byte[] Plane = [];
    }

    private sealed class Huffman
    {
        public Huffman(int[] counts, byte[] symbols)
        {
            Symbols = symbols;
            var code = 0;
            var k = 0;
            for (var len = 1; len <= 16; len++)
            {
                ValPtr[len] = k;
                MinCode[len] = code;
                code += counts[len];
                k += counts[len];
                MaxCode[len] = counts[len] > 0 ? code - 1 : -1;
                code <<= 1;
            }
        }

        public byte[] Symbols { get; }
        public int[] MaxCode { get; } = new int[17];
        public int[] MinCode { get; } = new int[17];
        public int[] ValPtr { get; } = new int[17];
    }
}
