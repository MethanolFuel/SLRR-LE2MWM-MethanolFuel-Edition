using System.Numerics;
using Slrr.Formats.Graphics;
using Slrr.Formats.Instances;
using Slrr.Formats.Scx;

namespace Slrr.Render.Geometry;

/// <summary>A draw range with its material.</summary>
public sealed record CpuSubMesh(int FirstIndex, int IndexCount, MaterialDesc Material, Vector3 Min, Vector3 Max);

/// <summary>
/// GPU-ready geometry (no GL): interleaved vertices of <see cref="FloatsPerVertex"/> floats =
/// position 3, normal 3, uvA 2 (base stage channel), uvB 2 (decal / second channel), colour RGBA 4; 32-bit indices
/// rebased to the shared vertex array; one <see cref="CpuSubMesh"/> per source mesh.
/// </summary>
public sealed class CpuMesh
{
    public const int FloatsPerVertex = 14;
    public const int StrideBytes = FloatsPerVertex * sizeof(float);

    public required float[] Vertices { get; init; }
    public required uint[] Indices { get; init; }
    public required IReadOnlyList<CpuSubMesh> SubMeshes { get; init; }
    public Vector3 Min { get; init; }
    public Vector3 Max { get; init; }
    public int SkippedMeshes { get; init; }
    public int GeneratedNormals { get; init; }

    public int VertexCount => Vertices.Length / FloatsPerVertex;
    public int TriangleCount => Indices.Length / 3;
    public Vector3 Center => (Min + Max) * 0.5f;
    public float Radius => VertexCount == 0 ? 0 : (Max - Min).Length() * 0.5f;
}

/// <summary>One source mesh in builder-neutral form.</summary>
internal sealed record SourceMesh(Vector3[] Positions, Vector3[]? Normals, Vector2[] UvA, Vector2[] UvB, Vector4[]? Colors, IReadOnlyList<int> Indices, MaterialDesc Material);

/// <summary>Builds <see cref="CpuMesh"/> from SCX files (cars, objects) or POLY chunks (map geometry). Untrusted input: bad meshes are skipped and counted.</summary>
public static class MeshBuilder
{
    /// <param name="scx">Mesh file.</param>
    /// <param name="texturePaths">Texture list of the render entry drawing it (may be empty).</param>
    /// <param name="hasAlpha">Texture alpha probe (path -> any texel with alpha &lt; 255).</param>
    public static CpuMesh FromScx(ScxFile scx, IReadOnlyList<string?> texturePaths, Func<string, bool> hasAlpha)
    {
        ArgumentNullException.ThrowIfNull(scx);
        var list = new List<SourceMesh>();
        foreach (var mesh in scx.Meshes)
        {
            var vb = mesh.Vertices;
            if (vb.Count == 0 || vb.Stride < 12 || vb.Data.Length < (long)vb.Count * vb.Stride)
            {
                list.Add(new SourceMesh([], null, [], [], null, [], MaterialDesc.Default));
                continue;
            }

            var mat = ScxMaterial.Decode((int)scx.Version, mesh.Material);
            var layout = ScxVertexLayout.For(vb);

            // Channel of the base stage -> attribute A, decal (slot 1) channel -> attribute B (or the other channel).
            var baseStage = mat.Stages.Where(s => !s.IsGenerated && s.Slot != 1).OrderBy(s => s.Slot).FirstOrDefault()
                ?? mat.Stages.Where(s => !s.IsGenerated).OrderBy(s => s.Slot).FirstOrDefault();
            var decal = mat.Stages.FirstOrDefault(s => s.Slot == 1 && !s.IsGenerated);
            var chA = Channel(baseStage?.UvSource ?? 0, layout);
            var chB = Channel(decal?.UvSource ?? (chA == 0 ? 1 : 0), layout);
            var material = MaterialFactory.Create(mat, texturePaths, ch => ch == chA ? 0 : ch == chB ? 1 : (ch == 0 ? 0 : -1), hasAlpha);

            var n = vb.Count;
            var pos = new Vector3[n];
            var nrm = vb.HasNormal ? new Vector3[n] : null;
            var uvA = new Vector2[n];
            var uvB = new Vector2[n];
            for (var i = 0; i < n; i++)
            {
                pos[i] = vb.Position(i);
                if (nrm is not null)
                {
                    nrm[i] = vb.Normal(i) ?? Vector3.Zero;
                }

                uvA[i] = chA >= 0 ? layout.ReadUv(vb, i, chA) : Vector2.Zero;
                uvB[i] = chB >= 0 ? layout.ReadUv(vb, i, chB) : uvA[i];
            }

            list.Add(new SourceMesh(pos, nrm, uvA, uvB, null, mesh.Indices, material));
        }

        return Build(list);
    }

    /// <summary>Map geometry: POLY sub-meshes; <c>material</c> indexes <paramref name="materials"/> (the map's SCY list, MEASURED range 0..107 = 108 city materials).</summary>
    public static CpuMesh FromPoly(IEnumerable<PolySubmesh> submeshes, IReadOnlyList<MaterialDesc> materials)
    {
        var list = new List<SourceMesh>();
        foreach (var sm in submeshes)
        {
            var n = sm.Vertices.Count;
            var pos = new Vector3[n];
            var nrm = new Vector3[n];
            var uvA = new Vector2[n];
            var uvB = new Vector2[n];
            var col = new Vector4[n];
            for (var i = 0; i < n; i++)
            {
                var v = sm.Vertices[i];
                pos[i] = new Vector3(v.Position.X, v.Position.Y, v.Position.Z);
                nrm[i] = new Vector3(v.Normal.X, v.Normal.Y, v.Normal.Z);
                uvA[i] = new Vector2(v.U0, v.V0);
                uvB[i] = new Vector2(v.U1, v.V1);
                col[i] = Bgra(v.Color0);
            }

            var mat = sm.Material < materials.Count ? materials[(int)sm.Material] : MaterialDesc.Default;
            list.Add(new SourceMesh(pos, nrm, uvA, uvB, col, sm.Indices.Select(x => x > int.MaxValue ? -1 : (int)x).ToList(), mat));
        }

        return Build(list);
    }

    /// <summary>D3DCOLOR byte order B,G,R,A (GUESSED for POLY colours, same as the SCX material hypothesis).</summary>
    public static Vector4 Bgra(uint c) => new(((c >> 16) & 0xFF) / 255f, ((c >> 8) & 0xFF) / 255f, (c & 0xFF) / 255f, ((c >> 24) & 0xFF) / 255f);

    private static int Channel(int uvSource, ScxVertexLayout layout)
    {
        var ch = Math.Clamp(uvSource, 0, 2);
        return layout.UvOffset(ch) >= 0 ? ch : layout.UvOffset(0) >= 0 ? 0 : -1;
    }

    internal static CpuMesh Build(IReadOnlyList<SourceMesh> meshes)
    {
        var vertices = new List<float>();
        var indices = new List<uint>();
        var subs = new List<CpuSubMesh>();
        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        int skipped = 0, generated = 0;
        foreach (var m in meshes)
        {
            var triCount = m.Indices.Count - (m.Indices.Count % 3);
            if (m.Positions.Length == 0 || triCount == 0 || !Valid(m, triCount))
            {
                skipped++;
                continue;
            }

            var smooth = SmoothNormals(m.Positions, m.Indices, triCount);
            var baseVertex = vertices.Count / CpuMesh.FloatsPerVertex;
            var smin = new Vector3(float.MaxValue);
            var smax = new Vector3(float.MinValue);
            for (var i = 0; i < m.Positions.Length; i++)
            {
                var p = m.Positions[i];
                smin = Vector3.Min(smin, p);
                smax = Vector3.Max(smax, p);
                var n = m.Normals is { } ns && i < ns.Length ? ns[i] : Vector3.Zero;
                var len = n.Length();
                if (float.IsFinite(len) && len > 1e-6f)
                {
                    n /= len;
                }
                else
                {
                    n = smooth[i];
                    generated++;
                }

                var a = Finite(m.UvA[i]);
                var b = Finite(m.UvB[i]);
                var c = m.Colors?[i] ?? Vector4.One;
                vertices.AddRange([p.X, p.Y, p.Z, n.X, n.Y, n.Z, a.X, a.Y, b.X, b.Y, c.X, c.Y, c.Z, c.W]);
            }

            var first = indices.Count;
            for (var i = 0; i < triCount; i++)
            {
                indices.Add((uint)(baseVertex + m.Indices[i]));
            }

            min = Vector3.Min(min, smin);
            max = Vector3.Max(max, smax);
            subs.Add(new CpuSubMesh(first, triCount, m.Material, smin, smax));
        }

        var empty = vertices.Count == 0;
        return new CpuMesh
        {
            Vertices = [.. vertices],
            Indices = [.. indices],
            SubMeshes = subs,
            Min = empty ? Vector3.Zero : min,
            Max = empty ? Vector3.Zero : max,
            SkippedMeshes = skipped,
            GeneratedNormals = generated,
        };
    }

    private static Vector2 Finite(Vector2 v) => float.IsFinite(v.X) && float.IsFinite(v.Y) ? v : Vector2.Zero;

    private static bool Valid(SourceMesh m, int triCount)
    {
        if (m.UvA.Length != m.Positions.Length || m.UvB.Length != m.Positions.Length || (m.Colors is { } c && c.Length != m.Positions.Length))
        {
            return false;
        }

        for (var i = 0; i < triCount; i++)
        {
            if (m.Indices[i] < 0 || m.Indices[i] >= m.Positions.Length)
            {
                return false;
            }
        }

        foreach (var p in m.Positions)
        {
            if (!float.IsFinite(p.X) || !float.IsFinite(p.Y) || !float.IsFinite(p.Z) || MathF.Abs(p.X) + MathF.Abs(p.Y) + MathF.Abs(p.Z) > 1e7f)
            {
                return false;
            }
        }

        return true;
    }

    private static Vector3[] SmoothNormals(Vector3[] p, IReadOnlyList<int> idx, int count)
    {
        var acc = new Vector3[p.Length];
        for (var t = 0; t < count; t += 3)
        {
            int a = idx[t], b = idx[t + 1], c = idx[t + 2];
            var face = Vector3.Cross(p[b] - p[a], p[c] - p[a]);
            acc[a] += face;
            acc[b] += face;
            acc[c] += face;
        }

        for (var i = 0; i < acc.Length; i++)
        {
            var len = acc[i].Length();
            acc[i] = float.IsFinite(len) && len > 1e-12f ? acc[i] / len : Vector3.UnitY;
        }

        return acc;
    }
}
