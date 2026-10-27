using System.Numerics;
using Slrr.Formats.Scx;

namespace Slrr.Viewer;

/// <summary>A draw range inside <see cref="MeshData.Indices"/> (one per SCX mesh).</summary>
public readonly record struct SubMesh(int FirstIndex, int IndexCount, int BaseVertex, int VertexCount);

/// <summary>
/// GPU-ready geometry for an SCX file, built without touching OpenGL so it can be unit tested headless.
/// All SCX meshes are concatenated into one interleaved vertex array (position xyz, normal xyz = 6 floats,
/// 24 bytes) and one 32-bit index array whose indices are already rebased to the shared vertex array, so the
/// whole file draws with a single glDrawElements; <see cref="SubMeshes"/> keeps per-mesh ranges for later
/// per-material draws.
/// <para>Input is untrusted game content: meshes with out-of-range indices or non-finite positions are
/// skipped (counted in <see cref="SkippedMeshes"/>), a trailing partial triangle is dropped, and missing or
/// degenerate normals are replaced by area-weighted smooth normals computed from the triangles.</para>
/// </summary>
public sealed class MeshData
{
    public const int FloatsPerVertex = 6;
    public const int StrideBytes = FloatsPerVertex * sizeof(float);
    public const int NormalOffsetBytes = 3 * sizeof(float);

    public required float[] Vertices { get; init; }
    public required uint[] Indices { get; init; }
    public required IReadOnlyList<SubMesh> SubMeshes { get; init; }
    public Vector3 BoundsMin { get; init; }
    public Vector3 BoundsMax { get; init; }
    public int SkippedMeshes { get; init; }

    /// <summary>Number of normals that were computed rather than read from the file.</summary>
    public int GeneratedNormals { get; init; }

    public int VertexCount => Vertices.Length / FloatsPerVertex;
    public int TriangleCount => Indices.Length / 3;
    public Vector3 Center => (BoundsMin + BoundsMax) * 0.5f;

    /// <summary>Half the bounding box diagonal; 1 for an empty mesh so the camera stays sane.</summary>
    public float Radius => VertexCount == 0 ? 1f : MathF.Max((BoundsMax - BoundsMin).Length() * 0.5f, 1e-4f);

    public static MeshData FromScx(ScxFile scx)
    {
        ArgumentNullException.ThrowIfNull(scx);
        var builder = new Builder();
        foreach (var mesh in scx.Meshes)
        {
            var vb = mesh.Vertices;
            var positions = new Vector3[vb.Count];
            Vector3[]? normals = vb.HasNormal ? new Vector3[vb.Count] : null;
            for (var i = 0; i < vb.Count; i++)
            {
                positions[i] = vb.Position(i);
                if (normals is not null)
                {
                    normals[i] = vb.Normal(i)!.Value;
                }
            }

            builder.Add(positions, normals, mesh.Indices);
        }

        return builder.Build();
    }

    /// <summary>Builds from plain arrays (one entry per mesh); the entry point used by tests.</summary>
    public static MeshData FromArrays(IEnumerable<(Vector3[] Positions, Vector3[]? Normals, IReadOnlyList<int> Indices)> meshes)
    {
        ArgumentNullException.ThrowIfNull(meshes);
        var builder = new Builder();
        foreach (var (p, n, i) in meshes)
        {
            builder.Add(p, n, i);
        }

        return builder.Build();
    }

    private sealed class Builder
    {
        private readonly List<float> vertices = [];
        private readonly List<uint> indices = [];
        private readonly List<SubMesh> subMeshes = [];
        private Vector3 min = new(float.MaxValue);
        private Vector3 max = new(float.MinValue);
        private int skipped;
        private int generated;

        public void Add(Vector3[] positions, Vector3[]? normals, IReadOnlyList<int> meshIndices)
        {
            var triIndexCount = meshIndices.Count - (meshIndices.Count % 3);
            if (positions.Length == 0 || triIndexCount == 0)
            {
                skipped++;
                return;
            }

            if (normals is not null && normals.Length != positions.Length)
            {
                normals = null;
            }

            for (var i = 0; i < triIndexCount; i++)
            {
                if (meshIndices[i] < 0 || meshIndices[i] >= positions.Length)
                {
                    skipped++;
                    return;
                }
            }

            foreach (var p in positions)
            {
                if (!float.IsFinite(p.X) || !float.IsFinite(p.Y) || !float.IsFinite(p.Z))
                {
                    skipped++;
                    return;
                }
            }

            var smooth = ComputeSmoothNormals(positions, meshIndices, triIndexCount);
            var baseVertex = vertices.Count / FloatsPerVertex;
            for (var i = 0; i < positions.Length; i++)
            {
                var p = positions[i];
                min = Vector3.Min(min, p);
                max = Vector3.Max(max, p);
                var n = normals is null ? Vector3.Zero : normals[i];
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

                vertices.Add(p.X);
                vertices.Add(p.Y);
                vertices.Add(p.Z);
                vertices.Add(n.X);
                vertices.Add(n.Y);
                vertices.Add(n.Z);
            }

            var first = indices.Count;
            for (var i = 0; i < triIndexCount; i++)
            {
                indices.Add((uint)(baseVertex + meshIndices[i]));
            }

            subMeshes.Add(new SubMesh(first, triIndexCount, baseVertex, positions.Length));
        }

        public MeshData Build()
        {
            var empty = vertices.Count == 0;
            return new MeshData
            {
                Vertices = [.. vertices],
                Indices = [.. indices],
                SubMeshes = subMeshes,
                BoundsMin = empty ? Vector3.Zero : min,
                BoundsMax = empty ? Vector3.Zero : max,
                SkippedMeshes = skipped,
                GeneratedNormals = generated,
            };
        }

        private static Vector3[] ComputeSmoothNormals(Vector3[] positions, IReadOnlyList<int> idx, int count)
        {
            var acc = new Vector3[positions.Length];
            for (var t = 0; t < count; t += 3)
            {
                int a = idx[t], b = idx[t + 1], c = idx[t + 2];

                // Unnormalised cross product = area weighting.
                var face = Vector3.Cross(positions[b] - positions[a], positions[c] - positions[a]);
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
}
