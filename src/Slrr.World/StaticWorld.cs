using System.Buffers.Binary;
using System.Diagnostics;
using System.Numerics;
using Slrr.Formats.Instances;
using Slrr.Formats.Rpak;
using Slrr.Formats.Scx;

namespace Slrr.World;

/// <summary>A placed object instance (instance node with <see cref="InstanceNode.ObjectFlag"/>).</summary>
public sealed class PlacedObject
{
    public required uint Id { get; init; }

    public required string Name { get; init; }

    /// <summary>RSD <c>gametype 0xSSSSIIII</c>.</summary>
    public required uint GameType { get; init; }

    public required Vector3 Position { get; init; }

    /// <summary>The three raw angles of <c>params x,y,z,a,b,c</c> (radians).</summary>
    public required Vector3 Angles { get; init; }

    public required Matrix4x4 Transform { get; init; }

    /// <summary>Box stored in the instance record (centre + half extents).</summary>
    public required InstanceBox RecordBox { get; init; }

    public ObjectType? Type { get; init; }
}

/// <summary>A movable (mass &gt; 0) body of a placed object, for the physics engine.</summary>
public sealed record DynamicBody(PlacedObject Owner, ObjectBody Body, Matrix4x4 BodyToWorld);

/// <summary>Build timing and counters (MEASURED by <c>world stats</c>).</summary>
public sealed class WorldStats
{
    public double ReadArchiveMs { get; set; }

    public double ReadForestMs { get; set; }

    public double CollisionExtractMs { get; set; }

    public double BvhBuildMs { get; set; }

    public double RenderSceneMs { get; set; }

    public double ObjectsMs { get; set; }

    public double TotalMs { get; set; }

    public int Nodes { get; set; }

    public int PhysChunks { get; set; }

    public int PolyChunks { get; set; }

    public int MapTriangles { get; set; }

    public int ObjectTriangles { get; set; }

    public int Objects { get; set; }

    public int ObjectsWithBodies { get; set; }

    public int StaticBodies { get; set; }

    public int DynamicBodies { get; set; }

    public int DegenerateDropped { get; set; }

    public int BadPhysIndices { get; set; }
}

/// <summary>Everything the driving code needs from a map archive: collision, render description, surfaces, objects.</summary>
public sealed class StaticWorld
{
    public required string Name { get; init; }

    public required RpakArchive Archive { get; init; }

    public required SurfaceTable Surfaces { get; init; }

    public required CollisionWorld Collision { get; init; }

    public required RenderScene Render { get; init; }

    public required IReadOnlyList<PlacedObject> Objects { get; init; }

    public required IReadOnlyList<DynamicBody> DynamicBodies { get; init; }

    public required WorldStats Stats { get; init; }

    /// <summary>Path of the .scm the root names (<c>params ...,mesh.scm</c>), or null.</summary>
    public string? ScmPath { get; init; }

    public IGroundQuery Ground => Collision;
}

/// <summary>Options for <see cref="WorldLoader"/>.</summary>
public sealed class WorldLoadOptions
{
    /// <summary>Game directory used to find the .scm, referenced archives (objects.rpk) and collision .scx meshes. Null = no objects/materials.</summary>
    public string? GameRoot { get; init; }

    /// <summary>Add static (mass 0) object bodies to the collision world.</summary>
    public bool IncludeObjectCollision { get; init; } = true;

    public bool BuildRenderScene { get; init; } = true;
}

public static class WorldLoader
{
    /// <summary>Collision-mesh .scx files of object bodies are stored x100 (MEASURED: hotel_013 phys_01 y extent +-1075.02 while
    /// its body line puts the centre at y -10.748; kuka_d +-49.11 vs body y 0.506; villanyoszlop +-344.0 vs 3.44 -- in every
    /// case body.y == half height * 0.01, i.e. the mesh sits on y = 0).</summary>
    public const float PhysMeshScale = 0.01f;

    public static StaticWorld Load(string archivePath, WorldLoadOptions? options = null)
    {
        var sw = Stopwatch.StartNew();
        var stats = new WorldStats();
        var archive = RpakArchive.Read(archivePath);
        stats.ReadArchiveMs = sw.Elapsed.TotalMilliseconds;
        var w = Load(archive, Path.GetFileNameWithoutExtension(archivePath), options, stats, sw);
        return w;
    }

    public static StaticWorld Load(RpakArchive archive, string name, WorldLoadOptions? options = null, WorldStats? stats = null, Stopwatch? total = null)
    {
        options ??= new WorldLoadOptions();
        stats ??= new WorldStats();
        var sw = total ?? Stopwatch.StartNew();
        var mark = sw.Elapsed.TotalMilliseconds;

        var forest = InstanceForest.Read(archive);
        stats.ReadForestMs = sw.Elapsed.TotalMilliseconds - mark;
        mark = sw.Elapsed.TotalMilliseconds;

        var catalog = options.GameRoot is { } gr ? new ObjectCatalog(gr) : null;

        // surface table from the .scm named by the root
        string? scmPath = null;
        SurfaceTable? table = null;
        foreach (var root in forest.Roots)
        {
            var p = root.Rsd?.Params;
            if (p is null)
            {
                continue;
            }

            var scm = p.Split(',').Select(s => s.Trim()).Select(s => { var c = s.IndexOf("//", StringComparison.Ordinal); return (c >= 0 ? s[..c] : s).Trim(); })
                .FirstOrDefault(s => s.EndsWith(".scm", StringComparison.OrdinalIgnoreCase));
            if (scm is not null && catalog?.ResolvePath(scm) is { } path)
            {
                scmPath = path;
                table = SurfaceTable.TryParse(File.ReadAllBytes(path));
                break;
            }
        }

        // PHYS: world-space triangles (already placed in the tree; MEASURED vertex boxes lie inside the node boxes in map coordinates)
        var soup = new TriangleSoupBuilder();
        var maxSurface = -1;
        foreach (var node in forest.AllNodes())
        {
            stats.Nodes++;
            foreach (var chunk in node.Chunks)
            {
                if (chunk.Tag == InstanceChunk.TagPhys)
                {
                    stats.PhysChunks++;
                    maxSurface = Math.Max(maxSurface, AddPhys(chunk.Payload, (int)node.Record.Id, soup, stats));
                }
                else if (chunk.Tag == InstanceChunk.TagPoly)
                {
                    stats.PolyChunks++;
                }
            }
        }

        stats.MapTriangles = soup.Count;
        stats.CollisionExtractMs = sw.Elapsed.TotalMilliseconds - mark;
        mark = sw.Elapsed.TotalMilliseconds;

        table ??= SurfaceTable.Anonymous(maxSurface + 1);
        if (table.Count <= maxSurface)
        {
            // the .scm lists fewer materials than the PHYS data uses: pad with anonymous entries so every id resolves
            table = new SurfaceTable(table.Items.Concat(Enumerable.Range(table.Count, maxSurface + 1 - table.Count).Select(i => new SurfaceInfo(i, "material " + i))));
        }

        // placed objects
        var objects = new List<PlacedObject>();
        var dynamics = new List<DynamicBody>();
        var scxCache = new Dictionary<string, (Vector3[] V, int[] I)?>(StringComparer.OrdinalIgnoreCase);
        foreach (var n in forest.AllNodes().Where(x => x.IsObject))
        {
            var rsd = n.Rsd;
            if (rsd?.GameType is not { } gt || rsd.Placement is not { } pl)
            {
                continue;
            }

            var ang = new Vector3(pl.Yaw, pl.Pitch, pl.Roll);
            var pos = new Vector3(pl.Position.X, pl.Position.Y, pl.Position.Z);
            var obj = new PlacedObject
            {
                Id = n.Record.Id,
                Name = n.Record.Alias,
                GameType = gt,
                Position = pos,
                Angles = ang,
                Transform = Placement.Transform(pos, pl.Yaw, pl.Pitch, pl.Roll),
                RecordBox = n.Box,
                Type = catalog?.Resolve(archive, gt),
            };
            objects.Add(obj);
            if (obj.Type is not { Bodies.Count: > 0 } t)
            {
                continue;
            }

            stats.ObjectsWithBodies++;
            foreach (var body in t.Bodies)
            {
                var toWorld = BodyMatrix(body) * obj.Transform;
                if (body.Mass > 0)
                {
                    dynamics.Add(new DynamicBody(obj, body, toWorld));
                    stats.DynamicBodies++;
                }
                else
                {
                    stats.StaticBodies++;
                    if (options.IncludeObjectCollision)
                    {
                        var before = soup.Count;
                        AddBody(soup, body, toWorld, (int)obj.Id, catalog, scxCache);
                        stats.ObjectTriangles += soup.Count - before;
                    }
                }
            }
        }

        stats.Objects = objects.Count;
        stats.ObjectsMs = sw.Elapsed.TotalMilliseconds - mark;
        mark = sw.Elapsed.TotalMilliseconds;

        var collision = new CollisionWorld(soup, table);
        stats.DegenerateDropped = collision.DegenerateDropped;
        stats.BvhBuildMs = sw.Elapsed.TotalMilliseconds - mark;
        mark = sw.Elapsed.TotalMilliseconds;

        var render = options.BuildRenderScene ? RenderScene.Build(forest, table) : RenderScene.Empty(table);
        stats.RenderSceneMs = sw.Elapsed.TotalMilliseconds - mark;
        stats.TotalMs = sw.Elapsed.TotalMilliseconds;

        return new StaticWorld
        {
            Name = name,
            Archive = archive,
            Surfaces = table,
            Collision = collision,
            Render = render,
            Objects = objects,
            DynamicBodies = dynamics,
            Stats = stats,
            ScmPath = scmPath,
        };
    }

    /// <summary>Body-local to object-local matrix (row vectors).</summary>
    public static Matrix4x4 BodyMatrix(ObjectBody b) =>
        Placement.Rotation(b.Angles.X, b.Angles.Y, b.Angles.Z) * Matrix4x4.CreateTranslation(b.Position);

    /// <summary>Parses a PHYS payload straight into the soup (same layout as <c>PhysMesh</c>). Returns the largest surface id seen.</summary>
    private static int AddPhys(byte[] payload, int source, TriangleSoupBuilder soup, WorldStats stats)
    {
        var span = payload.AsSpan();
        if (span.Length < 4)
        {
            return -1;
        }

        var triCount = (int)BinaryPrimitives.ReadUInt32LittleEndian(span);
        var vertOffset = 4 + (triCount * 28);
        if (triCount < 0 || vertOffset + 4 > span.Length)
        {
            return -1;
        }

        var vertCount = (int)BinaryPrimitives.ReadUInt32LittleEndian(span[vertOffset..]);
        if (vertCount < 0 || vertOffset + 4 + (vertCount * 12L) > span.Length)
        {
            return -1;
        }

        var maxSurface = -1;
        var vb = vertOffset + 4;
        Vector3 Vert(uint i) => new(
            BitConverter.ToSingle(payload, vb + ((int)i * 12)),
            BitConverter.ToSingle(payload, vb + ((int)i * 12) + 4),
            BitConverter.ToSingle(payload, vb + ((int)i * 12) + 8));

        for (var t = 0; t < triCount; t++)
        {
            var o = 4 + (t * 28);
            var surf = BinaryPrimitives.ReadUInt32LittleEndian(span[o..]);
            var a = BinaryPrimitives.ReadUInt32LittleEndian(span[(o + 4)..]);
            var b = BinaryPrimitives.ReadUInt32LittleEndian(span[(o + 8)..]);
            var c = BinaryPrimitives.ReadUInt32LittleEndian(span[(o + 12)..]);
            if (a >= vertCount || b >= vertCount || c >= vertCount)
            {
                stats.BadPhysIndices++;
                continue;
            }

            var n = new Vector3(BitConverter.ToSingle(payload, o + 16), BitConverter.ToSingle(payload, o + 20), BitConverter.ToSingle(payload, o + 24));
            maxSurface = Math.Max(maxSurface, (int)surf);
            soup.Add(Vert(a), Vert(b), Vert(c), n, (int)surf, source);
        }

        return maxSurface;
    }

    private static void AddBody(TriangleSoupBuilder soup, ObjectBody body, Matrix4x4 m, int source, ObjectCatalog? catalog, Dictionary<string, (Vector3[] V, int[] I)?> cache)
    {
        const int Surf = SurfaceTable.ObjectSurfaceId;
        switch (body.Kind)
        {
            case BodyShapeKind.Box:
                AddBox(soup, body.Size, m, source);
                break;
            case BodyShapeKind.Sphere:
                AddSphere(soup, body.Size.X, m, source);
                break;
            case BodyShapeKind.Mesh when body.MeshPath is { } mp && catalog is not null:
                if (!cache.TryGetValue(mp, out var mesh))
                {
                    mesh = LoadScx(catalog, mp);
                    cache[mp] = mesh;
                }

                if (mesh is { } mm)
                {
                    for (var i = 0; i + 2 < mm.I.Length; i += 3)
                    {
                        var a = Vector3.Transform(mm.V[mm.I[i]], m);
                        var b = Vector3.Transform(mm.V[mm.I[i + 1]], m);
                        var c = Vector3.Transform(mm.V[mm.I[i + 2]], m);
                        soup.Add(a, b, c, Vector3.Cross(b - a, c - a), Surf, source);
                    }
                }

                break;
            default:
                break;
        }
    }

    private static (Vector3[] V, int[] I)? LoadScx(ObjectCatalog catalog, string path)
    {
        try
        {
            var file = catalog.ResolvePath(path);
            if (file is null)
            {
                return null;
            }

            var scx = ScxFile.Read(file);
            var verts = new List<Vector3>();
            var idx = new List<int>();
            foreach (var m in scx.Meshes)
            {
                var baseIndex = verts.Count;
                for (var i = 0; i < m.Vertices.Count; i++)
                {
                    verts.Add(m.Vertices.Position(i) * PhysMeshScale);
                }

                idx.AddRange(m.Indices.Select(x => x + baseIndex));
            }

            return (verts.ToArray(), idx.ToArray());
        }
        catch (Exception ex) when (ex is Slrr.Core.SlrrFormatException or IOException)
        {
            return null;
        }
    }

    /// <summary>12 triangles for a box with HALF extents <paramref name="h"/>, outward normals.</summary>
    public static void AddBox(TriangleSoupBuilder soup, Vector3 h, Matrix4x4 m, int source)
    {
        var c = new Vector3[8];
        for (var i = 0; i < 8; i++)
        {
            c[i] = Vector3.Transform(new Vector3((i & 1) == 0 ? -h.X : h.X, (i & 2) == 0 ? -h.Y : h.Y, (i & 4) == 0 ? -h.Z : h.Z), m);
        }

        // faces as corner quads; winding chosen so (b-a)x(c-a) points out (verified by the unit test on a unit box)
        int[][] quads = [[0, 4, 6, 2], [1, 3, 7, 5], [0, 1, 5, 4], [2, 6, 7, 3], [0, 2, 3, 1], [4, 5, 7, 6]];
        var center = Vector3.Transform(Vector3.Zero, m);
        foreach (var q in quads)
        {
            var a = c[q[0]];
            var b = c[q[1]];
            var cc = c[q[2]];
            var d = c[q[3]];
            var n = Vector3.Cross(b - a, cc - a);
            if (Vector3.Dot(n, a - center) < 0)
            {
                (b, d) = (d, b);
            }

            soup.Add(a, b, cc, Vector3.Cross(b - a, cc - a), SurfaceTable.ObjectSurfaceId, source);
            soup.Add(a, cc, d, Vector3.Cross(cc - a, d - a), SurfaceTable.ObjectSurfaceId, source);
        }
    }

    /// <summary>Icosphere (subdivision 1, 80 triangles) approximating a sphere shape. GUESSED tessellation.</summary>
    public static void AddSphere(TriangleSoupBuilder soup, float r, Matrix4x4 m, int source)
    {
        var t = (1f + MathF.Sqrt(5f)) / 2f;
        Vector3[] v =
        [
            new(-1, t, 0), new(1, t, 0), new(-1, -t, 0), new(1, -t, 0),
            new(0, -1, t), new(0, 1, t), new(0, -1, -t), new(0, 1, -t),
            new(t, 0, -1), new(t, 0, 1), new(-t, 0, -1), new(-t, 0, 1),
        ];
        int[] f =
        [
            0, 11, 5, 0, 5, 1, 0, 1, 7, 0, 7, 10, 0, 10, 11, 1, 5, 9, 5, 11, 4, 11, 10, 2, 10, 7, 6, 7, 1, 8,
            3, 9, 4, 3, 4, 2, 3, 2, 6, 3, 6, 8, 3, 8, 9, 4, 9, 5, 2, 4, 11, 6, 2, 10, 8, 6, 7, 9, 8, 1,
        ];
        var center = Vector3.Transform(Vector3.Zero, m);
        for (var i = 0; i < f.Length; i += 3)
        {
            var a = Vector3.Normalize(v[f[i]]);
            var b = Vector3.Normalize(v[f[i + 1]]);
            var c = Vector3.Normalize(v[f[i + 2]]);
            var ab = Vector3.Normalize(a + b);
            var bc = Vector3.Normalize(b + c);
            var ca = Vector3.Normalize(c + a);
            foreach (var (p, q, s) in new[] { (a, ab, ca), (b, bc, ab), (c, ca, bc), (ab, bc, ca) })
            {
                var pa = Vector3.Transform(p * r, m);
                var pb = Vector3.Transform(q * r, m);
                var pc = Vector3.Transform(s * r, m);
                if (Vector3.Dot(Vector3.Cross(pb - pa, pc - pa), pa - center) < 0)
                {
                    (pb, pc) = (pc, pb);
                }

                soup.Add(pa, pb, pc, Vector3.Cross(pb - pa, pc - pa), SurfaceTable.ObjectSurfaceId, source);
            }
        }
    }
}
