using System.Numerics;
using Slrr.Render.Geometry;
using Slrr.Render.Scene;

namespace Slrr.Game.Render.Maps;

/// <summary>
/// Finds the ground height under a point in the render meshes of a loaded region (top-most upward-facing opaque triangle below
/// <c>fromY</c>). A brute-force stand-in for the World area's collision queries so a scripted sequence can put a car on a map without
/// referencing that area; physics should use <c>IGroundQuery</c>.
/// </summary>
public static class GroundSampler
{
    public static bool TryGetGround(IReadOnlyList<StaticChunk> chunks, float x, float z, float fromY, out float y)
    {
        ArgumentNullException.ThrowIfNull(chunks);
        y = float.NegativeInfinity;
        foreach (var c in chunks)
        {
            if (x < c.Min.X || x > c.Max.X || z < c.Min.Z || z > c.Max.Z || c.Min.Y > fromY)
            {
                continue;
            }

            var mesh = c.Mesh;
            foreach (var sm in mesh.SubMeshes)
            {
                if (sm.Material.Alpha != AlphaMode.Opaque)
                {
                    continue;
                }

                for (var i = sm.FirstIndex; i + 2 < sm.FirstIndex + sm.IndexCount; i += 3)
                {
                    var a = P(mesh, mesh.Indices[i]);
                    var b = P(mesh, mesh.Indices[i + 1]);
                    var cc = P(mesh, mesh.Indices[i + 2]);
                    if (TryHeight(a, b, cc, x, z, out var h) && h <= fromY && h > y)
                    {
                        y = h;
                    }
                }
            }
        }

        return float.IsFinite(y);
    }

    private static Vector3 P(CpuMesh m, uint index)
    {
        var o = (int)index * CpuMesh.FloatsPerVertex;
        return new Vector3(m.Vertices[o], m.Vertices[o + 1], m.Vertices[o + 2]);
    }

    private static bool TryHeight(Vector3 a, Vector3 b, Vector3 c, float x, float z, out float y)
    {
        y = 0;
        var minX = MathF.Min(a.X, MathF.Min(b.X, c.X));
        var maxX = MathF.Max(a.X, MathF.Max(b.X, c.X));
        var minZ = MathF.Min(a.Z, MathF.Min(b.Z, c.Z));
        var maxZ = MathF.Max(a.Z, MathF.Max(b.Z, c.Z));
        if (x < minX || x > maxX || z < minZ || z > maxZ)
        {
            return false;
        }

        var d = ((b.Z - c.Z) * (a.X - c.X)) + ((c.X - b.X) * (a.Z - c.Z));
        if (MathF.Abs(d) < 1e-9f)
        {
            return false;
        }

        var l1 = (((b.Z - c.Z) * (x - c.X)) + ((c.X - b.X) * (z - c.Z))) / d;
        var l2 = (((c.Z - a.Z) * (x - c.X)) + ((a.X - c.X) * (z - c.Z))) / d;
        var l3 = 1 - l1 - l2;
        if (l1 < -1e-4f || l2 < -1e-4f || l3 < -1e-4f)
        {
            return false;
        }

        y = (l1 * a.Y) + (l2 * b.Y) + (l3 * c.Y);
        return true;
    }
}
