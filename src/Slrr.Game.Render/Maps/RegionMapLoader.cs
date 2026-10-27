using System.Numerics;
using Slrr.Core;
using Slrr.Formats.Graphics;
using Slrr.Formats.Instances;
using Slrr.Formats.Rpak;
using Slrr.Render.Content;
using Slrr.Render.Geometry;
using Slrr.Render.Maps;
using Slrr.Render.Scene;

namespace Slrr.Game.Render.Maps;

/// <summary>What to show of a map: archive, optionally one instance root (the garage archive holds four garages), a circle around the focus.</summary>
public sealed record MapRequest(string Archive, uint? RootId, Vector2 Center, float Radius, string Sky = "Skydome_cloudy_day01")
{
    public bool SameMap(MapRequest o) =>
        string.Equals(GameFileSystem.Normalize(Archive), GameFileSystem.Normalize(o.Archive), StringComparison.OrdinalIgnoreCase) && RootId == o.RootId;
}

/// <summary>A loaded part of a map: the scene description (POLY chunks as static geometry, placed objects as items, sky) and what was loaded.</summary>
public sealed class LoadedRegion
{
    public required MapRequest Request { get; init; }

    public required SceneDescription Scene { get; init; }

    public MapStats Stats { get; } = new();

    public int ChunksSkippedByBudget { get; set; }

    public double LoadMs { get; set; }

    public long CpuVertexBytes => Scene.Static.Sum(c => (long)c.Mesh.Vertices.Length * sizeof(float) + ((long)c.Mesh.Indices.Length * sizeof(uint)));
}

/// <summary>
/// Region-limited map loader. It follows <c>Slrr.Render.Maps.MapLoader</c> (same POLY / SCY material / placed object rules, so the
/// MEASURED and GUESSED labels of docs/phase4/render.md apply) and adds what the host needs: filtering by instance root (garage1 vs
/// garage2..4 live in one archive), a circle around the focus for both POLY chunks and objects, and a chunk budget so a city never
/// has to fit into memory at once (the whole-city render ran out of memory).
/// </summary>
public sealed class RegionMapLoader(GameContent content)
{
    /// <summary>Upper bound for POLY chunks held at once (GUESSED; keeps the 624-chunk city well below the OOM point).</summary>
    public int MaxChunks { get; set; } = 220;

    public GameContent Content { get; } = content;

    private readonly Dictionary<ResRef, CfgRenderInfo?> _objects = [];
    private (RpakArchive Archive, InstanceForest Forest)? _forest;

    public LoadedRegion Load(MapRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var scene = new SceneDescription();
        var result = new LoadedRegion { Request = request, Scene = scene };
        var stats = result.Stats;
        var archive = Content.Archive(request.Archive);
        if (archive is null)
        {
            scene.Notes.Add($"{request.Archive}: not loaded");
            return result;
        }

        InstanceForest forest;
        try
        {
            // The parsed instance tree (chunk payload bytes) is kept for the last map so streaming reloads do not re-parse it.
            if (_forest is { } cached && ReferenceEquals(cached.Archive, archive))
            {
                forest = cached.Forest;
            }
            else
            {
                forest = InstanceForest.Read(archive);
                _forest = (archive, forest);
            }
        }
        catch (SlrrFormatException ex)
        {
            scene.Notes.Add($"{request.Archive}: instance tree: {ex.Message}");
            return result;
        }

        var region = new MapRegion(request.Center.X, request.Center.Y, request.Radius);
        var materials = new Dictionary<string, List<MaterialDesc>>(StringComparer.OrdinalIgnoreCase);
        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        var types = new HashSet<ResRef>();
        var candidates = new List<(InstanceNode Node, List<MaterialDesc> Mats, float Distance)>();
        foreach (var root in forest.Roots)
        {
            if (request.RootId is { } want && root.Record.Id != want)
            {
                continue;
            }

            var scm = root.Rsd?.Params?.Split(',').Select(s => s.Trim()).FirstOrDefault(s => s.EndsWith(".scm", StringComparison.OrdinalIgnoreCase));
            var mats = Materials(request.Archive, archive, scm, materials, stats);
            foreach (var node in root.DescendantsAndSelf())
            {
                var box = node.Box;
                var bmin = new Vector3(box.CenterX - box.HalfX, box.CenterY - box.HalfY, box.CenterZ - box.HalfZ);
                var bmax = new Vector3(box.CenterX + box.HalfX, box.CenterY + box.HalfY, box.CenterZ + box.HalfZ);
                if (node.FindChunk(InstanceChunk.TagPoly) is not null)
                {
                    stats.PolyChunks++;
                    if (region.Touches(bmin, bmax))
                    {
                        var d = Vector2.Distance(new Vector2(box.CenterX, box.CenterZ), request.Center);
                        candidates.Add((node, mats, d));
                    }
                }

                if (node.IsObject && node.Rsd is { } rsd && rsd.GameType is { } gt && rsd.Placement is { } pl)
                {
                    stats.Objects++;
                    var at = new Vector3(pl.Position.X, pl.Position.Y, pl.Position.Z);
                    if (!region.Touches(at, at))
                    {
                        continue;
                    }

                    if (Content.Resolve(request.Archive, gt) is not { } typeRef || ObjectInfo(typeRef) is not { } info)
                    {
                        scene.Notes.Add($"object {node.Record.Alias}: gametype 0x{gt:X8} has no cfg");
                        continue;
                    }

                    types.Add(typeRef);
                    var world = CfgRenderInfo.Ypr(new Vector3(pl.Yaw, pl.Pitch, pl.Roll)) * Matrix4x4.CreateTranslation(at);
                    var levels = info.Renders.Where(x => x.LodMax != int.MaxValue).Select(x => x.LodMax).DefaultIfEmpty(0).Max();
                    foreach (var rl in info.Renders)
                    {
                        if (Content.Resolve(typeRef.Archive, rl.Id) is { } rr && Content.Render(rr) is { } def)
                        {
                            scene.Items.Add(new DrawItem(def, GameUnits.ScxScale * rl.Local * world, rl.LodMin, rl.LodMax, info.Lods, Content.Entry(typeRef)?.Alias ?? "object", levels));
                        }
                    }

                    stats.ObjectsLoaded++;
                    min = Vector3.Min(min, at);
                    max = Vector3.Max(max, at);
                }
            }
        }

        // Nearest chunks first; the budget drops the farthest ones.
        candidates.Sort((a, b) => a.Distance.CompareTo(b.Distance));
        if (candidates.Count > MaxChunks)
        {
            result.ChunksSkippedByBudget = candidates.Count - MaxChunks;
            candidates.RemoveRange(MaxChunks, candidates.Count - MaxChunks);
        }

        foreach (var (node, mats, _) in candidates)
        {
            try
            {
                var parsed = PolyMesh.Parse(node.FindChunk(InstanceChunk.TagPoly)!.Payload);
                var (ag, dis) = MeshStats.Winding(parsed.Submeshes);
                stats.WindingAgree += ag;
                stats.WindingDisagree += dis;
                var mesh = MeshBuilder.FromPoly(parsed.Submeshes, mats);
                if (mesh.VertexCount > 0)
                {
                    scene.Static.Add(new StaticChunk(mesh, mesh.Min, mesh.Max, node.Record.Alias));
                    stats.PolyChunksLoaded++;
                    min = Vector3.Min(min, mesh.Min);
                    max = Vector3.Max(max, mesh.Max);
                }
            }
            catch (SlrrFormatException ex)
            {
                scene.Notes.Add($"POLY {node.Record.Alias}: {ex.Message}");
            }
        }

        stats.ObjectTypes = types.Count;
        stats.Min = min;
        stats.Max = max;
        if (Content.Archive(MapLoader.SkyArchive) is { } sky &&
            sky.Entries.FirstOrDefault(e => e.TypeOfEntry == 14 && e.Alias.Equals(request.Sky, StringComparison.OrdinalIgnoreCase)) is { } skyEntry &&
            Content.Render(new ResRef(MapLoader.SkyArchive, skyEntry.Id)) is { } skyDef)
        {
            scene.Sky.Add(skyDef);
        }

        result.LoadMs = sw.Elapsed.TotalMilliseconds;
        return result;
    }

    private CfgRenderInfo? ObjectInfo(ResRef type)
    {
        if (!_objects.TryGetValue(type, out var info))
        {
            info = Content.GametypeCfg(type) is { } cfg ? CfgRenderInfo.Parse(cfg) : null;
            _objects[type] = info;
        }

        return info;
    }

    private List<MaterialDesc> Materials(string mapArchive, RpakArchive archive, string? scm, Dictionary<string, List<MaterialDesc>> cache, MapStats stats)
    {
        var scy = scm is null ? null : GameFileSystem.Normalize(Path.ChangeExtension(scm, ".scy"));
        if (cache.TryGetValue(scy ?? "*", out var hit))
        {
            return hit;
        }

        var list = new List<MaterialDesc>();
        cache[scy ?? "*"] = list;
        RenderDef? render = null;
        foreach (var e in archive.Entries.Where(e => e.TypeOfEntry == 14))
        {
            var def = Content.Render(new ResRef(mapArchive, e.Id));
            if (def?.MeshFile is { } mf && (scy is null ? mf.EndsWith(".scy", StringComparison.OrdinalIgnoreCase) : mf.Equals(scy, StringComparison.OrdinalIgnoreCase)))
            {
                render = def;
                scy = mf;
                break;
            }
        }

        if (scy is null)
        {
            Content.Problem($"{mapArchive}: no .scm in the root params and no render entry with a .scy mesh");
            return list;
        }

        var scyPath = Content.Files.Resolve(scy);
        if (Content.Scx(scyPath) is not { } file)
        {
            Content.Problem($"{mapArchive}: material file {scy} not shipped");
            return list;
        }

        var textures = render?.TexturePaths ?? [];
        foreach (var m in file.Meshes)
        {
            var mat = ScxMaterial.Decode((int)file.Version, m.Material);
            var desc = MaterialFactory.Create(mat, textures, ch => ch == 0 ? 0 : ch == 1 ? 1 : -1, p => Content.Texture(p, keep: false)?.HasTransparency() ?? false);
            list.Add(new MaterialDesc
            {
                Name = desc.Name, Diffuse = desc.Diffuse, Specular = desc.Specular, Power = desc.Power, Base = desc.Base, Decal = desc.Decal,
                Env = desc.Env, EnvTexturePath = desc.EnvTexturePath, EnvStrength = desc.EnvStrength, Alpha = desc.Alpha, Flags = desc.Flags,
                Flags2 = desc.Flags2, VertexColor = true,
            });
        }

        stats.Materials += list.Count;
        stats.TexturedMaterials += list.Count(x => x.Base?.TexturePath is not null);
        return list;
    }
}

/// <summary>Keeps one <see cref="LoadedRegion"/> around the focus and reloads when the focus leaves the inner half of the loaded circle or the map changes.</summary>
public sealed class MapStreamer(RegionMapLoader loader)
{
    public LoadedRegion? Current { get; private set; }

    public int Reloads { get; private set; }

    /// <summary>The loaded region for <paramref name="want"/> (radius and centre are taken as the desired window around the focus).</summary>
    public LoadedRegion Update(MapRequest want, out bool reloaded)
    {
        ArgumentNullException.ThrowIfNull(want);
        reloaded = false;
        if (Current is { } cur && cur.Request.SameMap(want) &&
            Vector2.Distance(cur.Request.Center, want.Center) <= cur.Request.Radius * 0.5f && Math.Abs(cur.Request.Radius - want.Radius) < 1e-3f)
        {
            return cur;
        }

        Current = loader.Load(want);
        Reloads++;
        reloaded = true;
        return Current;
    }
}
