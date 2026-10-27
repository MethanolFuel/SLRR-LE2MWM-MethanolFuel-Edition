using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Slrr.World;

/// <summary>Accumulates world-space triangles for <see cref="CollisionWorld"/>.</summary>
public sealed class TriangleSoupBuilder
{
    private float[] _v = new float[9 * 1024];
    private float[] _n = new float[3 * 1024];
    private ushort[] _s = new ushort[1024];
    private int[] _src = new int[1024];

    public int Count { get; private set; }

    /// <summary>Triangles dropped because they have zero area (their stored normal is then not meaningful).</summary>
    public int DegenerateDropped { get; private set; }

    /// <summary>Adds a triangle. <paramref name="normal"/> is the stored (PHYS) normal; when it is not unit length the
    /// geometric normal <c>(b-a)x(c-a)</c> is used instead (MEASURED: they agree for every non-degenerate shipped triangle).</summary>
    public void Add(Vector3 a, Vector3 b, Vector3 c, Vector3 normal, int surface, int source)
    {
        var geo = Vector3.Cross(b - a, c - a);
        var len2 = geo.LengthSquared();
        if (len2 < 1e-14f || float.IsNaN(len2))
        {
            DegenerateDropped++;
            return;
        }

        var nl = normal.LengthSquared();
        var n = nl is > 0.9f and < 1.1f ? normal : geo / MathF.Sqrt(len2);
        if (Count == _s.Length)
        {
            var cap = Count * 2;
            Array.Resize(ref _v, cap * 9);
            Array.Resize(ref _n, cap * 3);
            Array.Resize(ref _s, cap);
            Array.Resize(ref _src, cap);
        }

        var o = Count * 9;
        _v[o] = a.X;
        _v[o + 1] = a.Y;
        _v[o + 2] = a.Z;
        _v[o + 3] = b.X;
        _v[o + 4] = b.Y;
        _v[o + 5] = b.Z;
        _v[o + 6] = c.X;
        _v[o + 7] = c.Y;
        _v[o + 8] = c.Z;
        _n[Count * 3] = n.X;
        _n[(Count * 3) + 1] = n.Y;
        _n[(Count * 3) + 2] = n.Z;
        _s[Count] = (ushort)surface;
        _src[Count] = source;
        Count++;
    }

    internal (float[] V, float[] N, ushort[] S, int[] Src) Take() => (_v, _n, _s, _src);
}

/// <summary>
/// Static triangle soup with a BVH (binned SAH, 4 triangles per leaf). Immutable after construction, so all queries are
/// safe from many threads. Performance is MEASURED by <c>world bench</c> (see docs/phase4/world.md).
/// </summary>
public sealed class CollisionWorld : IGroundQuery
{
    /// <summary>Preferred ground-grid cell edge in metres (GUESSED; widened automatically above 4M cells).</summary>
    public const float GroundCellSize = 6f;

    /// <summary>Triangles with a bounding-box edge above this (metres) go into the separate large-triangle tree (GUESSED, tuned on city.rpk).</summary>
    public const float BigTriangleExtent = 64f;

    private const int LeafSize = 4;

    private readonly float[] _v;
    private readonly float[] _n;
    private readonly ushort[] _surf;
    private readonly int[] _src;
    private readonly Node[] _nodes;
    private readonly GroundGrid _grid;
    private readonly int[] _roots;
    private readonly int _stackSize;
    private int _nodeCount;
    private int _maxDepth;

    public CollisionWorld(TriangleSoupBuilder soup, SurfaceTable surfaces)
    {
        Surfaces = surfaces;
        TriangleCount = soup.Count;
        DegenerateDropped = soup.DegenerateDropped;
        var (v, n, s, src) = soup.Take();
        var count = soup.Count;
        var order = new int[count];
        for (var i = 0; i < count; i++)
        {
            order[i] = i;
        }

        var tmin = new float[count * 3];
        var tmax = new float[count * 3];
        var cen = new float[count * 3];
        for (var i = 0; i < count; i++)
        {
            for (var k = 0; k < 3; k++)
            {
                var a = v[(i * 9) + k];
                var b = v[(i * 9) + 3 + k];
                var c = v[(i * 9) + 6 + k];
                tmin[(i * 3) + k] = MathF.Min(a, MathF.Min(b, c));
                tmax[(i * 3) + k] = MathF.Max(a, MathF.Max(b, c));
                cen[(i * 3) + k] = (a + b + c) / 3f;
            }
        }

        // Triangles longer than BigTriangleExtent (terrain sheets, MEASURED 7.3k of 297k in city) would inflate leaf boxes of the
        // small-triangle tree, so they get their own tree: roots 0 (small) and 1 (large).
        var nSmall = 0;
        for (var i = 0; i < count; i++)
        {
            var big = false;
            for (var k = 0; k < 3; k++)
            {
                big |= tmax[(i * 3) + k] - tmin[(i * 3) + k] > BigTriangleExtent;
            }

            if (!big)
            {
                (order[i], order[nSmall]) = (order[nSmall], order[i]);
                nSmall++;
            }
        }

        _nodes = new Node[(2 * count) + 2];
        _nodeCount = 2;
        var roots = new List<int>();
        if (nSmall > 0)
        {
            Build(0, 0, nSmall, order, tmin, tmax, cen, 0);
            roots.Add(0);
        }

        if (nSmall < count)
        {
            Build(1, nSmall, count, order, tmin, tmax, cen, 0);
            roots.Add(1);
        }

        _roots = roots.ToArray();
        _stackSize = _maxDepth + 8; // DFS pushes at most one extra sibling per level

        _v = new float[count * 9];
        _n = new float[count * 3];
        _surf = new ushort[count];
        _src = new int[count];
        for (var i = 0; i < count; i++)
        {
            var o = order[i];
            Array.Copy(v, o * 9, _v, i * 9, 9);
            Array.Copy(n, o * 3, _n, i * 3, 3);
            _surf[i] = s[o];
            _src[i] = src[o];
        }

        _grid = new GroundGrid(_v, _n, count, GroundCellSize);
        if (count > 0)
        {
            var lo = new Vector3(float.MaxValue);
            var hi = new Vector3(float.MinValue);
            foreach (var ri in _roots)
            {
                ref var r = ref _nodes[ri];
                lo = Vector3.Min(lo, new Vector3(r.MinX, r.MinY, r.MinZ));
                hi = Vector3.Max(hi, new Vector3(r.MaxX, r.MaxY, r.MaxZ));
            }

            Bounds = (lo, hi);
        }
    }

    public SurfaceTable Surfaces { get; }

    public int TriangleCount { get; }

    public int DegenerateDropped { get; }

    public int NodeCount => _nodeCount;

    /// <summary>Depth of the deepest BVH leaf (MEASURED about 20-30 on shipped maps).</summary>
    public int MaxDepth => _maxDepth;

    public (Vector3 Min, Vector3 Max) Bounds { get; }

    [StructLayout(LayoutKind.Sequential)]
    private struct Node
    {
        public float MinX;
        public float MinY;
        public float MinZ;
        public float MaxX;
        public float MaxY;
        public float MaxZ;

        /// <summary>Inner: left child. Leaf: first triangle.</summary>
        public int A;

        /// <summary>Inner: right child (&gt; 0). Leaf: -triangle count.</summary>
        public int B;
    }

    public Vector3 Vertex(int tri, int corner) => new(_v[(tri * 9) + (corner * 3)], _v[(tri * 9) + (corner * 3) + 1], _v[(tri * 9) + (corner * 3) + 2]);

    public Vector3 Normal(int tri) => new(_n[tri * 3], _n[(tri * 3) + 1], _n[(tri * 3) + 2]);

    public int SurfaceOf(int tri) => _surf[tri];

    public int SourceOf(int tri) => _src[tri];

    // ---------------------------------------------------------------- build

    private void Build(int nodeIdx, int lo, int hi, int[] order, float[] tmin, float[] tmax, float[] cen, int depth)
    {
        float bx0 = float.MaxValue, by0 = float.MaxValue, bz0 = float.MaxValue, bx1 = float.MinValue, by1 = float.MinValue, bz1 = float.MinValue;
        float cx0 = float.MaxValue, cy0 = float.MaxValue, cz0 = float.MaxValue, cx1 = float.MinValue, cy1 = float.MinValue, cz1 = float.MinValue;
        for (var i = lo; i < hi; i++)
        {
            var t = order[i] * 3;
            bx0 = MathF.Min(bx0, tmin[t]);
            by0 = MathF.Min(by0, tmin[t + 1]);
            bz0 = MathF.Min(bz0, tmin[t + 2]);
            bx1 = MathF.Max(bx1, tmax[t]);
            by1 = MathF.Max(by1, tmax[t + 1]);
            bz1 = MathF.Max(bz1, tmax[t + 2]);
            cx0 = MathF.Min(cx0, cen[t]);
            cy0 = MathF.Min(cy0, cen[t + 1]);
            cz0 = MathF.Min(cz0, cen[t + 2]);
            cx1 = MathF.Max(cx1, cen[t]);
            cy1 = MathF.Max(cy1, cen[t + 1]);
            cz1 = MathF.Max(cz1, cen[t + 2]);
        }

        ref var node = ref _nodes[nodeIdx];
        node.MinX = bx0;
        node.MinY = by0;
        node.MinZ = bz0;
        node.MaxX = bx1;
        node.MaxY = by1;
        node.MaxZ = bz1;
        var n = hi - lo;
        InterlockedMax(ref _maxDepth, depth);
        if (n <= LeafSize)
        {
            node.A = lo;
            node.B = -n;
            return;
        }

        var ex = cx1 - cx0;
        var ey = cy1 - cy0;
        var ez = cz1 - cz0;
        var axis = ex >= ey && ex >= ez ? 0 : ey >= ez ? 1 : 2;
        var cmin = axis == 0 ? cx0 : axis == 1 ? cy0 : cz0;
        var ext = axis == 0 ? ex : axis == 1 ? ey : ez;
        var mid = SplitPoint(lo, hi, axis, cmin, ext, order, tmin, tmax, cen);

        var left = Interlocked.Increment(ref _nodeCount) - 1;
        var right = Interlocked.Increment(ref _nodeCount) - 1;
        node.A = left;
        node.B = right;
        if (n > 65536)
        {
            Parallel.Invoke(
                () => Build(left, lo, mid, order, tmin, tmax, cen, depth + 1),
                () => Build(right, mid, hi, order, tmin, tmax, cen, depth + 1));
        }
        else
        {
            Build(left, lo, mid, order, tmin, tmax, cen, depth + 1);
            Build(right, mid, hi, order, tmin, tmax, cen, depth + 1);
        }
    }

    private static int SplitPoint(int lo, int hi, int axis, float cmin, float ext, int[] order, float[] tmin, float[] tmax, float[] cen)
    {
        var n = hi - lo;
        if (ext <= 1e-9f)
        {
            return lo + (n / 2);
        }

        const int Bins = 16;
        Span<int> cnt = stackalloc int[Bins];
        Span<float> bb = stackalloc float[Bins * 6];
        for (var b = 0; b < Bins; b++)
        {
            bb[(b * 6) + 0] = bb[(b * 6) + 1] = bb[(b * 6) + 2] = float.MaxValue;
            bb[(b * 6) + 3] = bb[(b * 6) + 4] = bb[(b * 6) + 5] = float.MinValue;
        }

        var scale = Bins / ext;
        for (var i = lo; i < hi; i++)
        {
            var t = order[i] * 3;
            var bi = Math.Min(Bins - 1, (int)((cen[t + axis] - cmin) * scale));
            cnt[bi]++;
            for (var k = 0; k < 3; k++)
            {
                bb[(bi * 6) + k] = MathF.Min(bb[(bi * 6) + k], tmin[t + k]);
                bb[(bi * 6) + 3 + k] = MathF.Max(bb[(bi * 6) + 3 + k], tmax[t + k]);
            }
        }

        Span<float> leftArea = stackalloc float[Bins];
        Span<int> leftCnt = stackalloc int[Bins];
        Span<float> mn = stackalloc float[3];
        Span<float> mx = stackalloc float[3];
        Reset(mn, mx);
        var acc = 0;
        for (var b = 0; b < Bins - 1; b++)
        {
            Grow(mn, mx, bb, b, cnt[b]);
            acc += cnt[b];
            leftCnt[b] = acc;
            leftArea[b] = Area(mn, mx, acc);
        }

        Reset(mn, mx);
        acc = 0;
        var bestCost = float.MaxValue;
        var bestSplit = -1;
        for (var b = Bins - 1; b >= 1; b--)
        {
            Grow(mn, mx, bb, b, cnt[b]);
            acc += cnt[b];
            var rightArea = Area(mn, mx, acc);
            var lc = leftCnt[b - 1];
            if (lc == 0 || acc == 0)
            {
                continue;
            }

            var cost = (leftArea[b - 1] * lc) + (rightArea * acc);
            if (cost < bestCost)
            {
                bestCost = cost;
                bestSplit = b;
            }
        }

        if (bestSplit < 0)
        {
            return lo + (n / 2);
        }

        var p = lo;
        var q = hi - 1;
        while (p <= q)
        {
            var t = order[p] * 3;
            var bi = Math.Min(Bins - 1, (int)((cen[t + axis] - cmin) * scale));
            if (bi < bestSplit)
            {
                p++;
            }
            else
            {
                (order[p], order[q]) = (order[q], order[p]);
                q--;
            }
        }

        return p == lo || p == hi ? lo + (n / 2) : p;
    }

    private static void InterlockedMax(ref int target, int value)
    {
        int cur;
        while (value > (cur = Volatile.Read(ref target)) && Interlocked.CompareExchange(ref target, value, cur) != cur)
        {
        }
    }

    private static void Reset(Span<float> mn, Span<float> mx)
    {
        mn.Fill(float.MaxValue);
        mx.Fill(float.MinValue);
    }

    private static void Grow(Span<float> mn, Span<float> mx, Span<float> bb, int b, int count)
    {
        if (count == 0)
        {
            return;
        }

        for (var k = 0; k < 3; k++)
        {
            mn[k] = MathF.Min(mn[k], bb[(b * 6) + k]);
            mx[k] = MathF.Max(mx[k], bb[(b * 6) + 3 + k]);
        }
    }

    private static float Area(Span<float> mn, Span<float> mx, int count)
    {
        if (count == 0)
        {
            return 0;
        }

        var dx = mx[0] - mn[0];
        var dy = mx[1] - mn[1];
        var dz = mx[2] - mn[2];
        return (dx * dy) + (dy * dz) + (dz * dx);
    }

    // ---------------------------------------------------------------- queries

    /// <summary>
    /// Highest upward-facing (stored normal Y &gt; 0.05, GUESSED threshold of about 87 degrees) triangle at or below
    /// <paramref name="fromY"/> containing (x, z). Edge hits are inclusive with a 1e-5 relative tolerance so shared edges
    /// leave no cracks.
    /// </summary>
    public bool TryGetGroundBvh(float x, float z, float fromY, out SurfaceHit hit)
    {
        hit = default;
        if (TriangleCount == 0)
        {
            return false;
        }

        Span<int> stack = stackalloc int[_stackSize];
        var sp = 0;
        var bestY = float.NegativeInfinity;
        var bestTri = -1;
        var nodes = _nodes;
        var v = _v;
        var nrm = _n;
        foreach (var ri in _roots)
        {
            if (GroundBox(ref nodes[ri], x, z, fromY))
            {
                stack[sp++] = ri;
            }
        }

        while (sp > 0)
        {
            ref var nd = ref nodes[stack[--sp]];
            if (nd.MaxY < bestY)
            {
                continue;
            }

            if (nd.B >= 0)
            {
                ref var l = ref nodes[nd.A];
                ref var r = ref nodes[nd.B];
                var hl = GroundBox(ref l, x, z, fromY);
                var hr = GroundBox(ref r, x, z, fromY);
                if (hl && hr)
                {
                    // visit the box reaching higher first: finds the top surface early and prunes the rest
                    if (l.MaxY >= r.MaxY)
                    {
                        stack[sp++] = nd.B;
                        stack[sp++] = nd.A;
                    }
                    else
                    {
                        stack[sp++] = nd.A;
                        stack[sp++] = nd.B;
                    }
                }
                else if (hl)
                {
                    stack[sp++] = nd.A;
                }
                else if (hr)
                {
                    stack[sp++] = nd.B;
                }

                continue;
            }

            var end = nd.A - nd.B;
            for (var t = nd.A; t < end; t++)
            {
                var ny = nrm[(t * 3) + 1];
                if (ny <= 0.05f)
                {
                    continue;
                }

                var o = t * 9;
                float ax = v[o], ay = v[o + 1], az = v[o + 2];
                float bx = v[o + 3], by = v[o + 4], bz = v[o + 5];
                float cx = v[o + 6], cy = v[o + 7], cz = v[o + 8];

                var e0 = ((bx - ax) * (z - az)) - ((bz - az) * (x - ax));
                var e1 = ((cx - bx) * (z - bz)) - ((cz - bz) * (x - bx));
                var e2 = ((ax - cx) * (z - cz)) - ((az - cz) * (x - cx));
                var area = ((bx - ax) * (cz - az)) - ((bz - az) * (cx - ax));
                var eps = MathF.Abs(area) * 1e-5f;
                if (area > 0 ? (e0 < -eps || e1 < -eps || e2 < -eps) : (e0 > eps || e1 > eps || e2 > eps))
                {
                    continue;
                }

                var y = ay - (((nrm[t * 3] * (x - ax)) + (nrm[(t * 3) + 2] * (z - az))) / ny);
                if (y > fromY + 1e-4f || y <= bestY)
                {
                    continue;
                }

                var lo = MathF.Min(ay, MathF.Min(by, cy)) - 1e-3f;
                var hi = MathF.Max(ay, MathF.Max(by, cy)) + 1e-3f;
                if (y < lo || y > hi)
                {
                    continue;
                }

                bestY = y;
                bestTri = t;
            }
        }

        if (bestTri < 0)
        {
            return false;
        }

        hit = new SurfaceHit(new Vector3(x, bestY, z), Normal(bestTri), _surf[bestTri], float.IsInfinity(fromY) ? 0 : fromY - bestY, bestTri, _src[bestTri]);
        return true;
    }

    public int GroundGridCells => _grid.CellCount;

    public int GroundGridItems => _grid.ItemCount;

    /// <summary>Ground query through the XZ grid (see <see cref="GroundGrid"/>); same result as <see cref="TryGetGroundBvh"/>.</summary>
    public bool TryGetGround(float x, float z, float fromY, out SurfaceHit hit)
    {
        var v = _v;
        var nrm = _n;
        var bestY = float.NegativeInfinity;
        var bestTri = -1;
        foreach (var t in _grid.Candidates(x, z))
        {
            var ny = nrm[(t * 3) + 1];
            var o = t * 9;
            float ax = v[o], ay = v[o + 1], az = v[o + 2];
            float bx = v[o + 3], by = v[o + 4], bz = v[o + 5];
            float cx = v[o + 6], cy = v[o + 7], cz = v[o + 8];
            var e0 = ((bx - ax) * (z - az)) - ((bz - az) * (x - ax));
            var e1 = ((cx - bx) * (z - bz)) - ((cz - bz) * (x - bx));
            var e2 = ((ax - cx) * (z - cz)) - ((az - cz) * (x - cx));
            var area = ((bx - ax) * (cz - az)) - ((bz - az) * (cx - ax));
            var eps = MathF.Abs(area) * 1e-5f;
            if (area > 0 ? (e0 < -eps || e1 < -eps || e2 < -eps) : (e0 > eps || e1 > eps || e2 > eps))
            {
                continue;
            }

            var y = ay - (((nrm[t * 3] * (x - ax)) + (nrm[(t * 3) + 2] * (z - az))) / ny);
            if (y > fromY + 1e-4f || y <= bestY)
            {
                continue;
            }

            if (y < MathF.Min(ay, MathF.Min(by, cy)) - 1e-3f || y > MathF.Max(ay, MathF.Max(by, cy)) + 1e-3f)
            {
                continue;
            }

            bestY = y;
            bestTri = t;
        }

        if (bestTri < 0)
        {
            hit = default;
            return false;
        }

        hit = new SurfaceHit(new Vector3(x, bestY, z), Normal(bestTri), _surf[bestTri], float.IsInfinity(fromY) ? 0 : fromY - bestY, bestTri, _src[bestTri]);
        return true;
    }

    /// <summary>Ground height convenience wrapper; <see cref="float.NaN"/> when there is no ground.</summary>
    public float HeightAt(float x, float z, float fromY = float.PositiveInfinity) =>
        TryGetGround(x, z, fromY, out var h) ? h.Position.Y : float.NaN;

    public bool Raycast(Vector3 origin, Vector3 direction, float maxDistance, out SurfaceHit hit)
    {
        hit = default;
        var len = direction.Length();
        if (TriangleCount == 0 || len < 1e-20f)
        {
            return false;
        }

        var dir = direction / len;
        var inv = new Vector3(SafeInv(dir.X), SafeInv(dir.Y), SafeInv(dir.Z));
        Span<int> stack = stackalloc int[_stackSize];
        Span<float> entry = stackalloc float[_stackSize];
        var sp = 0;
        foreach (var ri in _roots)
        {
            stack[sp] = ri;
            entry[sp++] = 0;
        }

        var best = maxDistance;
        var bestTri = -1;
        var nodes = _nodes;
        while (sp > 0)
        {
            sp--;
            if (entry[sp] > best)
            {
                continue;
            }

            ref var nd = ref nodes[stack[sp]];
            if (nd.B >= 0)
            {
                var tl = RayBoxT(origin, inv, ref nodes[nd.A], best);
                var tr = RayBoxT(origin, inv, ref nodes[nd.B], best);
                if (tl >= 0 && tr >= 0)
                {
                    // nearer child is popped first
                    if (tl <= tr)
                    {
                        stack[sp] = nd.B;
                        entry[sp++] = tr;
                        stack[sp] = nd.A;
                        entry[sp++] = tl;
                    }
                    else
                    {
                        stack[sp] = nd.A;
                        entry[sp++] = tl;
                        stack[sp] = nd.B;
                        entry[sp++] = tr;
                    }
                }
                else if (tl >= 0)
                {
                    stack[sp] = nd.A;
                    entry[sp++] = tl;
                }
                else if (tr >= 0)
                {
                    stack[sp] = nd.B;
                    entry[sp++] = tr;
                }

                continue;
            }

            var end = nd.A - nd.B;
            for (var t = nd.A; t < end; t++)
            {
                if (Geometry.RayTriangle(origin, dir, Vertex(t, 0), Vertex(t, 1), Vertex(t, 2), out var tt) && tt >= 0 && tt <= best)
                {
                    best = tt;
                    bestTri = t;
                }
            }
        }

        if (bestTri < 0)
        {
            return false;
        }

        var n = Normal(bestTri);
        if (Vector3.Dot(n, dir) > 0)
        {
            n = -n;
        }

        hit = new SurfaceHit(origin + (dir * best), n, _surf[bestTri], best, bestTri, _src[bestTri]);
        return true;
    }

    /// <summary>True when the segment from..to is unobstructed.</summary>
    public bool LineOfSight(Vector3 from, Vector3 to) => !Raycast(from, to - from, Vector3.Distance(from, to), out _);

    /// <summary>All triangles penetrated by the sphere. Contacts are appended to <paramref name="contacts"/>.</summary>
    public int OverlapSphere(Vector3 center, float radius, List<Contact> contacts) =>
        OverlapCapsule(center, center, radius, contacts);

    /// <summary>All triangles penetrated by the capsule (segment a..b swept by <paramref name="radius"/>).</summary>
    public int OverlapCapsule(Vector3 a, Vector3 b, float radius, List<Contact> contacts)
    {
        if (TriangleCount == 0)
        {
            return 0;
        }

        var found = 0;
        var min = Vector3.Min(a, b) - new Vector3(radius);
        var max = Vector3.Max(a, b) + new Vector3(radius);
        var r2 = radius * radius;
        Span<int> stack = stackalloc int[_stackSize];
        var sp = 0;
        foreach (var ri in _roots)
        {
            stack[sp++] = ri;
        }

        var nodes = _nodes;
        while (sp > 0)
        {
            ref var nd = ref nodes[stack[--sp]];
            if (nd.MaxX < min.X || nd.MinX > max.X || nd.MaxY < min.Y || nd.MinY > max.Y || nd.MaxZ < min.Z || nd.MinZ > max.Z)
            {
                continue;
            }

            if (nd.B >= 0)
            {
                stack[sp++] = nd.A;
                stack[sp++] = nd.B;
                continue;
            }

            var end = nd.A - nd.B;
            for (var t = nd.A; t < end; t++)
            {
                var d2 = Geometry.ClosestSegmentTriangle(a, b, Vertex(t, 0), Vertex(t, 1), Vertex(t, 2), out var onSeg, out var onTri);
                if (d2 >= r2)
                {
                    continue;
                }

                var d = MathF.Sqrt(d2);
                var push = onSeg - onTri;
                var n = Normal(t);
                if (d > 1e-6f)
                {
                    push /= d;
                }
                else
                {
                    push = Vector3.Dot(n, ((a + b) * 0.5f) - onTri) >= 0 ? n : -n;
                }

                contacts.Add(new Contact(onTri, push, radius - d, _surf[t], t, _src[t]));
                found++;
            }
        }

        return found;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool GroundBox(ref Node nd, float x, float z, float fromY) =>
        x >= nd.MinX && x <= nd.MaxX && z >= nd.MinZ && z <= nd.MaxZ && nd.MinY <= fromY;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float SafeInv(float d) => MathF.Abs(d) < 1e-20f ? (d < 0 ? -1e20f : 1e20f) : 1f / d;

    /// <summary>Entry distance (&gt;= 0) when the ray hits the box within maxT, else -1.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float RayBoxT(Vector3 o, Vector3 inv, ref Node nd, float maxT)
    {
        var t1 = (nd.MinX - o.X) * inv.X;
        var t2 = (nd.MaxX - o.X) * inv.X;
        var tmin = MathF.Min(t1, t2);
        var tmax = MathF.Max(t1, t2);
        t1 = (nd.MinY - o.Y) * inv.Y;
        t2 = (nd.MaxY - o.Y) * inv.Y;
        tmin = MathF.Max(tmin, MathF.Min(t1, t2));
        tmax = MathF.Min(tmax, MathF.Max(t1, t2));
        t1 = (nd.MinZ - o.Z) * inv.Z;
        t2 = (nd.MaxZ - o.Z) * inv.Z;
        tmin = MathF.Max(tmin, MathF.Min(t1, t2));
        tmax = MathF.Min(tmax, MathF.Max(t1, t2));
        var entry = MathF.Max(tmin, 0);
        return tmax >= entry && entry <= maxT ? entry : -1f;
    }
}
