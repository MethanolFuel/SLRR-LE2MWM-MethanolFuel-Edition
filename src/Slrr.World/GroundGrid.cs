namespace Slrr.World;

/// <summary>
/// Uniform XZ grid over the upward-facing triangles (stored normal Y &gt; 0.05): each cell lists every triangle whose XZ
/// footprint touches it (exact scan-line coverage, conservative at the edges). A ground query is then one cell lookup plus a
/// handful of point-in-triangle tests, independent of map size. Memory: 4 bytes per cell plus 4 bytes per (triangle, cell) pair.
/// </summary>
internal sealed class GroundGrid
{
    private readonly float _minX;
    private readonly float _minZ;
    private readonly float _invCell;
    private readonly int _w;
    private readonly int _h;
    private readonly int[] _start;
    private int[] _items = [];

    public GroundGrid(float[] v, float[] n, int triCount, float cellHint)
    {
        float minX = float.MaxValue, minZ = float.MaxValue, maxX = float.MinValue, maxZ = float.MinValue;
        var any = false;
        for (var t = 0; t < triCount; t++)
        {
            if (n[(t * 3) + 1] <= 0.05f)
            {
                continue;
            }

            any = true;
            for (var k = 0; k < 3; k++)
            {
                var x = v[(t * 9) + (k * 3)];
                var z = v[(t * 9) + (k * 3) + 2];
                minX = MathF.Min(minX, x);
                maxX = MathF.Max(maxX, x);
                minZ = MathF.Min(minZ, z);
                maxZ = MathF.Max(maxZ, z);
            }
        }

        if (!any)
        {
            _start = [0, 0];
            _items = [];
            _w = _h = 1;
            _invCell = 1;
            return;
        }

        // keep the cell count under ~4M
        var cell = cellHint;
        while (((maxX - minX) / cell * ((maxZ - minZ) / cell)) > 4_000_000)
        {
            cell *= 1.5f;
        }

        _minX = minX;
        _minZ = minZ;
        _invCell = 1f / cell;
        _w = (int)((maxX - minX) * _invCell) + 1;
        _h = (int)((maxZ - minZ) * _invCell) + 1;
        _start = new int[(_w * _h) + 1];

        // pass 1: count, pass 2: fill
        for (var pass = 0; pass < 2; pass++)
        {
            if (pass == 1)
            {
                var sum = 0;
                for (var i = 0; i < _start.Length; i++)
                {
                    var c = _start[i];
                    _start[i] = sum;
                    sum += c;
                }

                _items = new int[sum];
            }

            var fill = pass == 1 ? (int[])_start.Clone() : null;
            for (var t = 0; t < triCount; t++)
            {
                if (n[(t * 3) + 1] <= 0.05f)
                {
                    continue;
                }

                Rasterize(v, t, pass, fill);
            }
        }
    }

    public int CellCount => _w * _h;

    public int ItemCount => _items.Length;

    public int Width => _w;

    public int Height => _h;

    /// <summary>Candidate triangles of the cell containing (x, z); empty when outside the grid.</summary>
    public ReadOnlySpan<int> Candidates(float x, float z)
    {
        var cx = (int)MathF.Floor((x - _minX) * _invCell);
        var cz = (int)MathF.Floor((z - _minZ) * _invCell);
        if ((uint)cx >= (uint)_w || (uint)cz >= (uint)_h)
        {
            return default;
        }

        var c = (cz * _w) + cx;
        return _items.AsSpan(_start[c], _start[c + 1] - _start[c]);
    }

    private void Rasterize(float[] v, int t, int pass, int[]? fill)
    {
        var o = t * 9;
        float x0 = v[o], z0 = v[o + 2], x1 = v[o + 3], z1 = v[o + 5], x2 = v[o + 6], z2 = v[o + 8];
        var zmin = MathF.Min(z0, MathF.Min(z1, z2));
        var zmax = MathF.Max(z0, MathF.Max(z1, z2));
        var r0 = Math.Max(0, (int)MathF.Floor((zmin - 1e-3f - _minZ) * _invCell));
        var r1 = Math.Min(_h - 1, (int)MathF.Floor((zmax + 1e-3f - _minZ) * _invCell));
        var inv = 1f / _invCell;
        for (var r = r0; r <= r1; r++)
        {
            var sz0 = _minZ + (r * inv);
            var sz1 = sz0 + inv;
            var lo = float.MaxValue;
            var hi = float.MinValue;
            Slab(x0, z0, x1, z1, sz0, sz1, ref lo, ref hi);
            Slab(x1, z1, x2, z2, sz0, sz1, ref lo, ref hi);
            Slab(x2, z2, x0, z0, sz0, sz1, ref lo, ref hi);
            if (lo > hi)
            {
                continue;
            }

            var c0 = Math.Max(0, (int)MathF.Floor((lo - 1e-3f - _minX) * _invCell));
            var c1 = Math.Min(_w - 1, (int)MathF.Floor((hi + 1e-3f - _minX) * _invCell));
            for (var c = c0; c <= c1; c++)
            {
                var idx = (r * _w) + c;
                if (pass == 0)
                {
                    _start[idx]++;
                }
                else
                {
                    _items[fill![idx]++] = t;
                }
            }
        }
    }

    /// <summary>Extends [lo, hi] by the X range of segment (a, b) clipped to the z slab [z0, z1].</summary>
    private static void Slab(float ax, float az, float bx, float bz, float z0, float z1, ref float lo, ref float hi)
    {
        if (az > bz)
        {
            (ax, bx) = (bx, ax);
            (az, bz) = (bz, az);
        }

        if (bz < z0 || az > z1)
        {
            return;
        }

        var dz = bz - az;
        if (dz < 1e-12f)
        {
            lo = MathF.Min(lo, MathF.Min(ax, bx));
            hi = MathF.Max(hi, MathF.Max(ax, bx));
            return;
        }

        var ta = MathF.Max(0, (z0 - az) / dz);
        var tb = MathF.Min(1, (z1 - az) / dz);
        var xa = ax + ((bx - ax) * ta);
        var xb = ax + ((bx - ax) * tb);
        lo = MathF.Min(lo, MathF.Min(xa, xb));
        hi = MathF.Max(hi, MathF.Max(xa, xb));
    }
}
