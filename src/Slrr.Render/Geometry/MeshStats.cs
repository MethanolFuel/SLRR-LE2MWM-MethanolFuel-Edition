using System.Numerics;
using Slrr.Formats.Instances;
using Slrr.Formats.Scx;

namespace Slrr.Render.Geometry;

/// <summary>Measurements on raw meshes used to settle conventions (winding vs stored normals).</summary>
public static class MeshStats
{
    /// <summary>
    /// Counts triangles whose right-handed face normal <c>(b - a) x (c - a)</c> points the same way as the mean of the
    /// three stored vertex normals (agree) or the opposite way (disagree). Meshes without normals are ignored.
    /// agree >> disagree means counter-clockwise front faces in a right-handed frame (GL default <c>glFrontFace(GL_CCW)</c>).
    /// </summary>
    public static (long Agree, long Disagree) Winding(ScxFile scx)
    {
        ArgumentNullException.ThrowIfNull(scx);
        long agree = 0, disagree = 0;
        foreach (var m in scx.Meshes)
        {
            var vb = m.Vertices;
            if (!vb.HasNormal || vb.Data.Length < (long)vb.Count * vb.Stride)
            {
                continue;
            }

            for (var t = 0; t + 2 < m.Indices.Count; t += 3)
            {
                int a = m.Indices[t], b = m.Indices[t + 1], c = m.Indices[t + 2];
                if (a < 0 || b < 0 || c < 0 || a >= vb.Count || b >= vb.Count || c >= vb.Count)
                {
                    continue;
                }

                var face = Vector3.Cross(vb.Position(b) - vb.Position(a), vb.Position(c) - vb.Position(a));
                var n = (vb.Normal(a) ?? Vector3.Zero) + (vb.Normal(b) ?? Vector3.Zero) + (vb.Normal(c) ?? Vector3.Zero);
                var d = Vector3.Dot(face, n);
                if (!float.IsFinite(d) || d == 0)
                {
                    continue;
                }

                if (d > 0)
                {
                    agree++;
                }
                else
                {
                    disagree++;
                }
            }
        }

        return (agree, disagree);
    }

    /// <summary>Same measurement for map POLY sub-meshes.</summary>
    public static (long Agree, long Disagree) Winding(IEnumerable<PolySubmesh> submeshes)
    {
        ArgumentNullException.ThrowIfNull(submeshes);
        long agree = 0, disagree = 0;
        foreach (var sm in submeshes)
        {
            var v = sm.Vertices;
            for (var t = 0; t + 2 < sm.Indices.Count; t += 3)
            {
                uint a = sm.Indices[t], b = sm.Indices[t + 1], c = sm.Indices[t + 2];
                if (a >= v.Count || b >= v.Count || c >= v.Count)
                {
                    continue;
                }

                static Vector3 P(Vec3 p) => new(p.X, p.Y, p.Z);
                var face = Vector3.Cross(P(v[(int)b].Position) - P(v[(int)a].Position), P(v[(int)c].Position) - P(v[(int)a].Position));
                var d = Vector3.Dot(face, P(v[(int)a].Normal) + P(v[(int)b].Normal) + P(v[(int)c].Normal));
                if (!float.IsFinite(d) || d == 0)
                {
                    continue;
                }

                if (d > 0)
                {
                    agree++;
                }
                else
                {
                    disagree++;
                }
            }
        }

        return (agree, disagree);
    }
}
