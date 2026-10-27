using System.Numerics;
using Slrr.Core;
using Slrr.Formats.Graphics;
using Slrr.Formats.Instances;
using Slrr.Formats.Rpak;
using Slrr.Render.Content;
using Slrr.Render.Geometry;
using Slrr.Render.Scene;

namespace Slrr.Render.Maps;

/// <summary>Area of a map to load (XZ circle in map units).</summary>
public readonly record struct MapRegion(float X, float Z, float Radius)
{
    public bool Touches(Vector3 min, Vector3 max)
    {
        var cx = Math.Clamp(X, min.X, max.X);
        var cz = Math.Clamp(Z, min.Z, max.Z);
        return ((cx - X) * (cx - X)) + ((cz - Z) * (cz - Z)) <= Radius * Radius;
    }
}

/// <summary>Counts reported by <see cref="MapLoader.Load"/>.</summary>
public sealed class MapStats
{
    public int PolyChunks { get; set; }
    public int PolyChunksLoaded { get; set; }
    public int Objects { get; set; }
    public int ObjectsLoaded { get; set; }
    public int ObjectTypes { get; set; }
    public int Materials { get; set; }
    public int TexturedMaterials { get; set; }
    public Vector3 Min { get; set; }
    public Vector3 Max { get; set; }

    /// <summary>POLY triangles whose right-handed face normal agrees / disagrees with the stored vertex normals.</summary>
    public long WindingAgree { get; set; }

    public long WindingDisagree { get; set; }
}

/// <summary>
/// Builds a <see cref="SceneDescription"/> for a map archive:
/// <list type="bullet">
/// <item>static geometry = every POLY chunk of the instance tree (docs/formats/instances.md), one <see cref="StaticChunk"/> per
/// node with the node's box for culling;</item>
/// <item>POLY <c>material</c> = index into the map's <c>.scy</c> (v3 SCX with materials only; the root RSD names the <c>.scm</c>, whose
/// text lists the same count, e.g. city 108 = POLY range 0..107, MEASURED) and the texture list of the render entry whose mesh is
/// that <c>.scy</c> (Drag_Track render 0x4, 20 textures, MEASURED);</item>
/// <item>placed objects = RSD <c>gametype</c> + <c>params x,y,z,a,b,c</c> -> the gametype's inline cfg <c>render</c>/<c>lod</c>/<c>lods</c> lines,
/// placed at (x,y,z) with (a,b,c) as yaw/pitch/roll (GUESSED, see instances.md);</item>
/// <item>sky = one render entry of <c>maps\skydome.rpk</c>.</item>
/// </list>
/// </summary>
public sealed class MapLoader(GameContent content)
{
    public const string SkyArchive = "maps\\skydome.rpk";

    public GameContent Content { get; } = content;

    public MapStats Stats { get; } = new();

    public SceneDescription Load(string mapArchive, MapRegion? region, string skyAlias = "Skydome_cloudy_day01")
    {
        var scene = new SceneDescription();
        var archive = Content.Archive(mapArchive);
        if (archive is null)
        {
            scene.Notes.Add($"{mapArchive}: not loaded");
            return scene;
        }

        InstanceForest forest;
        try
        {
            forest = InstanceForest.Read(archive);
        }
        catch (SlrrFormatException ex)
        {
            scene.Notes.Add($"{mapArchive}: instance tree: {ex.Message}");
            return scene;
        }

        var materials = new Dictionary<string, List<MaterialDesc>>(StringComparer.OrdinalIgnoreCase);
        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        var types = new HashSet<ResRef>();
        foreach (var root in forest.Roots)
        {
            var scm = root.Rsd?.Params?.Split(',').Select(s => s.Trim()).FirstOrDefault(s => s.EndsWith(".scm", StringComparison.OrdinalIgnoreCase));
            var mats = Materials(mapArchive, archive, scm, materials);
            foreach (var node in root.DescendantsAndSelf())
            {
                var box = node.Box;
                var bmin = new Vector3(box.CenterX - box.HalfX, box.CenterY - box.HalfY, box.CenterZ - box.HalfZ);
                var bmax = new Vector3(box.CenterX + box.HalfX, box.CenterY + box.HalfY, box.CenterZ + box.HalfZ);
                if (node.FindChunk(InstanceChunk.TagPoly) is { } poly)
                {
                    Stats.PolyChunks++;
                    if (region is { } r && !r.Touches(bmin, bmax))
                    {
                        continue;
                    }

                    try
                    {
                        var parsed = PolyMesh.Parse(poly.Payload);
                        var (ag, dis) = MeshStats.Winding(parsed.Submeshes);
                        Stats.WindingAgree += ag;
                        Stats.WindingDisagree += dis;
                        var mesh = MeshBuilder.FromPoly(parsed.Submeshes, mats);
                        if (mesh.VertexCount > 0)
                        {
                            scene.Static.Add(new StaticChunk(mesh, mesh.Min, mesh.Max, node.Record.Alias));
                            Stats.PolyChunksLoaded++;
                            min = Vector3.Min(min, mesh.Min);
                            max = Vector3.Max(max, mesh.Max);
                        }
                    }
                    catch (SlrrFormatException ex)
                    {
                        scene.Notes.Add($"POLY {node.Record.Alias}: {ex.Message}");
                    }
                }

                if (node.IsObject && node.Rsd is { } rsd && rsd.GameType is { } gt && rsd.Placement is { } pl)
                {
                    Stats.Objects++;
                    var at = new Vector3(pl.Position.X, pl.Position.Y, pl.Position.Z);
                    if (region is { } r && !r.Touches(at, at))
                    {
                        continue;
                    }

                    if (Content.Resolve(mapArchive, gt) is not { } typeRef || ObjectInfo(typeRef) is not { } info)
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

                    Stats.ObjectsLoaded++;
                    min = Vector3.Min(min, at);
                    max = Vector3.Max(max, at);
                }
            }
        }

        Stats.ObjectTypes = types.Count;
        Stats.Min = min;
        Stats.Max = max;
        if (Content.Archive(SkyArchive) is { } sky &&
            sky.Entries.FirstOrDefault(e => e.TypeOfEntry == 14 && e.Alias.Equals(skyAlias, StringComparison.OrdinalIgnoreCase)) is { } skyEntry &&
            Content.Render(new ResRef(SkyArchive, skyEntry.Id)) is { } skyDef)
        {
            scene.Sky.Add(skyDef);
        }

        return scene;
    }

    private readonly Dictionary<ResRef, CfgRenderInfo?> _objects = [];

    private CfgRenderInfo? ObjectInfo(ResRef type)
    {
        if (!_objects.TryGetValue(type, out var info))
        {
            info = Content.GametypeCfg(type) is { } cfg ? CfgRenderInfo.Parse(cfg) : null;
            _objects[type] = info;
        }

        return info;
    }

    /// <summary>
    /// Materials of a map root: the <c>.scy</c> next to the root's <c>.scm</c>; roots without an <c>.scm</c> in their params
    /// (test_track: <c>params 0x00000014</c>) use the archive's only render entry whose mesh is a <c>.scy</c> (GUESSED).
    /// </summary>
    private List<MaterialDesc> Materials(string mapArchive, RpakArchive archive, string? scm, Dictionary<string, List<MaterialDesc>> cache)
    {
        var scy = scm is null ? null : GameFileSystem.Normalize(Path.ChangeExtension(scm, ".scy"));
        if (cache.TryGetValue(scy ?? "*", out var hit))
        {
            return hit;
        }

        var list = new List<MaterialDesc>();
        cache[scy ?? "*"] = list;

        // The render entry whose mesh is the .scy gives the texture list.
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

        Stats.Materials += list.Count;
        Stats.TexturedMaterials += list.Count(x => x.Base?.TexturePath is not null);
        return list;
    }
}
