using System.Numerics;
using System.Runtime.CompilerServices;

namespace Slrr.World;

/// <summary>Closest-point helpers (Ericson, Real-Time Collision Detection).</summary>
public static class Geometry
{
    public static Vector3 ClosestPointOnTriangle(Vector3 p, Vector3 a, Vector3 b, Vector3 c)
    {
        var ab = b - a;
        var ac = c - a;
        var ap = p - a;
        var d1 = Vector3.Dot(ab, ap);
        var d2 = Vector3.Dot(ac, ap);
        if (d1 <= 0 && d2 <= 0)
        {
            return a;
        }

        var bp = p - b;
        var d3 = Vector3.Dot(ab, bp);
        var d4 = Vector3.Dot(ac, bp);
        if (d3 >= 0 && d4 <= d3)
        {
            return b;
        }

        var vc = (d1 * d4) - (d3 * d2);
        if (vc <= 0 && d1 >= 0 && d3 <= 0)
        {
            var den = d1 - d3;
            return den == 0 ? a : a + (ab * (d1 / den));
        }

        var cp = p - c;
        var d5 = Vector3.Dot(ab, cp);
        var d6 = Vector3.Dot(ac, cp);
        if (d6 >= 0 && d5 <= d6)
        {
            return c;
        }

        var vb = (d5 * d2) - (d1 * d6);
        if (vb <= 0 && d2 >= 0 && d6 <= 0)
        {
            var den = d2 - d6;
            return den == 0 ? a : a + (ac * (d2 / den));
        }

        var va = (d3 * d6) - (d5 * d4);
        if (va <= 0 && (d4 - d3) >= 0 && (d5 - d6) >= 0)
        {
            var den = (d4 - d3) + (d5 - d6);
            return den == 0 ? b : b + ((c - b) * ((d4 - d3) / den));
        }

        var sum = va + vb + vc;
        if (sum == 0)
        {
            return a;
        }

        var inv = 1f / sum;
        return a + (ab * (vb * inv)) + (ac * (vc * inv));
    }

    /// <summary>Closest points between segments p1-q1 and p2-q2.</summary>
    public static float ClosestSegmentSegment(Vector3 p1, Vector3 q1, Vector3 p2, Vector3 q2, out Vector3 c1, out Vector3 c2)
    {
        var d1 = q1 - p1;
        var d2 = q2 - p2;
        var r = p1 - p2;
        var a = Vector3.Dot(d1, d1);
        var e = Vector3.Dot(d2, d2);
        var f = Vector3.Dot(d2, r);
        float s;
        float t;
        const float Eps = 1e-12f;
        if (a <= Eps && e <= Eps)
        {
            c1 = p1;
            c2 = p2;
            return Vector3.DistanceSquared(c1, c2);
        }

        if (a <= Eps)
        {
            s = 0;
            t = Math.Clamp(f / e, 0, 1);
        }
        else
        {
            var c = Vector3.Dot(d1, r);
            if (e <= Eps)
            {
                t = 0;
                s = Math.Clamp(-c / a, 0, 1);
            }
            else
            {
                var b = Vector3.Dot(d1, d2);
                var denom = (a * e) - (b * b);
                s = denom > Eps ? Math.Clamp(((b * f) - (c * e)) / denom, 0, 1) : 0;
                t = ((b * s) + f) / e;
                if (t < 0)
                {
                    t = 0;
                    s = Math.Clamp(-c / a, 0, 1);
                }
                else if (t > 1)
                {
                    t = 1;
                    s = Math.Clamp((b - c) / a, 0, 1);
                }
            }
        }

        c1 = p1 + (d1 * s);
        c2 = p2 + (d2 * t);
        return Vector3.DistanceSquared(c1, c2);
    }

    /// <summary>Squared distance between a segment and a triangle with the closest points on each.</summary>
    public static float ClosestSegmentTriangle(Vector3 p, Vector3 q, Vector3 a, Vector3 b, Vector3 c, out Vector3 onSegment, out Vector3 onTriangle)
    {
        var best = float.MaxValue;
        onSegment = p;
        onTriangle = a;

        // segment crosses the triangle interior -> distance 0
        var n = Vector3.Cross(b - a, c - a);
        var dir = q - p;
        var denom = Vector3.Dot(n, dir);
        if (MathF.Abs(denom) > 1e-12f)
        {
            var t = Vector3.Dot(n, a - p) / denom;
            if (t is >= 0 and <= 1)
            {
                var hit = p + (dir * t);
                var cp = ClosestPointOnTriangle(hit, a, b, c);
                if (Vector3.DistanceSquared(cp, hit) < 1e-12f)
                {
                    onSegment = hit;
                    onTriangle = hit;
                    return 0;
                }
            }
        }

        Consider(p, ClosestPointOnTriangle(p, a, b, c), ref best, ref onSegment, ref onTriangle);
        Consider(q, ClosestPointOnTriangle(q, a, b, c), ref best, ref onSegment, ref onTriangle);
        ClosestSegmentSegment(p, q, a, b, out var s1, out var t1);
        Consider(s1, t1, ref best, ref onSegment, ref onTriangle);
        ClosestSegmentSegment(p, q, b, c, out s1, out t1);
        Consider(s1, t1, ref best, ref onSegment, ref onTriangle);
        ClosestSegmentSegment(p, q, c, a, out s1, out t1);
        Consider(s1, t1, ref best, ref onSegment, ref onTriangle);
        return best;
    }

    private static void Consider(Vector3 s, Vector3 tr, ref float best, ref Vector3 onSegment, ref Vector3 onTriangle)
    {
        var d = Vector3.DistanceSquared(s, tr);
        if (d < best)
        {
            best = d;
            onSegment = s;
            onTriangle = tr;
        }
    }

    /// <summary>Möller-Trumbore, double sided. <paramref name="t"/> in units of <paramref name="dir"/>.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool RayTriangle(Vector3 o, Vector3 dir, Vector3 a, Vector3 b, Vector3 c, out float t)
    {
        t = 0;
        var e1 = b - a;
        var e2 = c - a;
        var p = Vector3.Cross(dir, e2);
        var det = Vector3.Dot(e1, p);
        if (MathF.Abs(det) < 1e-12f)
        {
            return false;
        }

        var inv = 1f / det;
        var s = o - a;
        var u = Vector3.Dot(s, p) * inv;
        if (u < -1e-6f || u > 1 + 1e-6f)
        {
            return false;
        }

        var qv = Vector3.Cross(s, e1);
        var v = Vector3.Dot(dir, qv) * inv;
        if (v < -1e-6f || u + v > 1 + 1e-6f)
        {
            return false;
        }

        t = Vector3.Dot(e2, qv) * inv;
        return true;
    }
}
