using System.Numerics;

namespace Slrr.Render.Scene;

/// <summary>Six planes extracted from a row-vector view-projection matrix (GL clip space, -w..w on all axes).</summary>
public readonly struct Frustum
{
    private readonly Plane[] _planes;

    public Frustum(Matrix4x4 m)
    {
        // Column j of the row-vector matrix = coefficients of clip component j.
        var c0 = new Vector4(m.M11, m.M21, m.M31, m.M41);
        var c1 = new Vector4(m.M12, m.M22, m.M32, m.M42);
        var c2 = new Vector4(m.M13, m.M23, m.M33, m.M43);
        var c3 = new Vector4(m.M14, m.M24, m.M34, m.M44);
        _planes =
        [
            Make(c3 + c0), Make(c3 - c0), Make(c3 + c1), Make(c3 - c1), Make(c3 + c2), Make(c3 - c2),
        ];
    }

    private static Plane Make(Vector4 v) => Plane.Normalize(new Plane(v.X, v.Y, v.Z, v.W));

    /// <summary>False only when the box is completely outside one plane (conservative).</summary>
    public bool Intersects(Vector3 min, Vector3 max)
    {
        foreach (var p in _planes)
        {
            var v = new Vector3(p.Normal.X >= 0 ? max.X : min.X, p.Normal.Y >= 0 ? max.Y : min.Y, p.Normal.Z >= 0 ? max.Z : min.Z);
            if (Vector3.Dot(p.Normal, v) + p.D < 0)
            {
                return false;
            }
        }

        return true;
    }

    public bool Intersects(Vector3 center, float radius)
    {
        foreach (var p in _planes)
        {
            if (Vector3.Dot(p.Normal, center) + p.D < -radius)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>World AABB of a local box under a transform.</summary>
    public static (Vector3 Min, Vector3 Max) Transform(Vector3 min, Vector3 max, Matrix4x4 m)
    {
        var center = Vector3.Transform((min + max) * 0.5f, m);
        var e = (max - min) * 0.5f;
        var ex = new Vector3(
            (MathF.Abs(m.M11) * e.X) + (MathF.Abs(m.M21) * e.Y) + (MathF.Abs(m.M31) * e.Z),
            (MathF.Abs(m.M12) * e.X) + (MathF.Abs(m.M22) * e.Y) + (MathF.Abs(m.M32) * e.Z),
            (MathF.Abs(m.M13) * e.X) + (MathF.Abs(m.M23) * e.Y) + (MathF.Abs(m.M33) * e.Z));
        return (center - ex, center + ex);
    }
}
