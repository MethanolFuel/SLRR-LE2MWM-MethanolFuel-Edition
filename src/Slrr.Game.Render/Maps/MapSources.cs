using System.Numerics;
using Slrr.Engine.Resources;
using Slrr.Formats.Instances;
using Slrr.Formats.Rpak;
using Slrr.Game.Render.Scene;
using Slrr.Render.Content;

namespace Slrr.Game.Render.Maps;

/// <summary>
/// Decides which map geometry belongs to a scene. The host does not place map geometry in its resource tree (the Resources area
/// never instantiates an archive's RSD records: docs/phase4/game.md "Minimum engine surface"), so the renderer has to be told.
/// The World area's loader can sit behind this interface when the lead wires it; <see cref="HostMapSource"/> is the default.
/// </summary>
public interface IMapSource
{
    /// <summary>The map for scene root <paramref name="sceneId"/> (a resource entry id or an instance id), seen from <paramref name="focus"/>; null = no map geometry.</summary>
    MapRequest? Resolve(IGameWorld world, int sceneId, Vector3 focus);
}

/// <summary>Always the same map (scripted sequences, tests).</summary>
public sealed class FixedMapSource(string archive, uint? rootId, float radius, string sky = "Skydome_cloudy_day01") : IMapSource
{
    public string Archive { get; set; } = archive;

    public uint? RootId { get; set; } = rootId;

    public float Radius { get; set; } = radius;

    public string Sky { get; set; } = sky;

    public MapRequest? Resolve(IGameWorld world, int sceneId, Vector3 focus) => new(Archive, RootId, new Vector2(focus.X, focus.Z), Radius, Sky);
}

/// <summary>
/// Default: a scene whose root is an rpk entry (a map gametype such as <c>misc\garage.rpk:0x7A "garage1"</c>, which is what
/// <c>new GroundRef(garageID[i])</c> creates the Garage scene from) uses the instance root of the same archive with that id.
/// MEASURED on the garage: the camera, the car and the render instances all hang below the entry 0x0002007A, and the archive's
/// forest has a root with id 0x7A. Entries that are not a forest root give no map.
/// </summary>
public sealed class HostMapSource(GameContent content, float radius = 280f) : IMapSource
{
    private readonly Dictionary<string, HashSet<uint>> _roots = new(StringComparer.OrdinalIgnoreCase);

    public MapRequest? Resolve(IGameWorld world, int sceneId, Vector3 focus)
    {
        ArgumentNullException.ThrowIfNull(world);
        if (sceneId == 0 || ResourceIds.IsInstance(sceneId) || HostIds.ToResRef(world.Resources, sceneId) is not { } r)
        {
            return null;
        }

        return Roots(r.Archive).Contains(r.Id) ? new MapRequest(r.Archive, r.Id, new Vector2(focus.X, focus.Z), radius) : null;
    }

    private HashSet<uint> Roots(string archive)
    {
        if (_roots.TryGetValue(archive, out var set))
        {
            return set;
        }

        set = [];
        if (content.Archive(archive) is { } a && a.Entries.Any(e => e.TypeOfEntry == RpakArchive.ExtraEntryType))
        {
            try
            {
                // Only roots that carry geometry count (system.rpk has an instance tree too: the world tree root, id 4).
                foreach (var root in InstanceForest.Read(a).Roots.Where(r => r.DescendantsAndSelf().Any(n => n.FindChunk(InstanceChunk.TagPoly) is not null)))
                {
                    set.Add(root.Record.Id);
                }
            }
            catch (Slrr.Core.SlrrFormatException)
            {
                // not an instance archive
            }
        }

        _roots[archive] = set;
        return set;
    }
}
