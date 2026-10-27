using System.Diagnostics;
using System.Numerics;
using Slrr.Engine.Resources;
using Slrr.World;

namespace Slrr.Engine.MapNatives;

/// <summary>Options of <see cref="MapWorlds"/>.</summary>
public sealed class MapWorldOptions
{
    /// <summary>Game root (for the .scm surface table, objects.rpk and collision .scx meshes). Null: geometry only.</summary>
    public string? GameRoot { get; init; }

    /// <summary>Static object bodies in the collision world (see Slrr.World).</summary>
    public bool IncludeObjectCollision { get; init; } = true;

    /// <summary>Build the render-side scene description (cheap; the renderer area needs it).</summary>
    public bool BuildRenderScene { get; init; } = true;

    /// <summary>
    /// Create one instance node (type 1) below the ground for every placed object of the map (GUESSED: the exe's
    /// instance tree for map objects is not traced; this makes them visible to <c>getFirstChild/getNextChild</c>).
    /// No object script runs.
    /// </summary>
    public bool InstantiatePlacedObjects { get; init; } = true;

    /// <summary>Keep built worlds per archive after <c>unload()</c> so a second visit does not rebuild (host convenience, not a parity claim).</summary>
    public bool CacheWorlds { get; init; }
}

/// <summary>Water plane set by <c>GroundRef.setWater</c>. Roles GUESSED (point on the plane, plane normal, two floats 300/50 in every call).</summary>
public readonly record struct WaterPlane(Vector3 Point, Vector3 Normal, float A, float B);

/// <summary>A fog setting of <c>GroundRef.setFog(int, float, float)</c>: 24-bit RGB colour, near, far (roles GUESSED, values MEASURED at the 17 call sites).</summary>
public readonly record struct FogSetting(int Color, float Near, float Far);

/// <summary>An AI traffic registration (<c>addTrafficCar/addTrafficN/addTrafficP</c>); nothing drives it here.</summary>
public sealed record TrafficEntry(int Id, int CarOrTypeId, string Kind, Vector3? Position, int Count, float A, float B, float C)
{
    public int Behaviour { get; set; }

    public int Notify { get; set; }
}

/// <summary>Script-visible state of one ground (map root) and the world built from its archive.</summary>
public sealed class MapState
{
    public required int GroundId { get; init; }

    /// <summary>Archive file of the ground node (game-relative as registered).</summary>
    public required string ArchiveName { get; init; }

    /// <summary>Collision world, surfaces, placed objects and render scene (Slrr.World). Null until loaded or when loading failed.</summary>
    public StaticWorld? World { get; internal set; }

    public string? LoadError { get; internal set; }

    /// <summary>Wall time of the last world build (ms).</summary>
    public double LoadMs { get; internal set; }

    /// <summary>Number of times the world was built or taken from the cache for this ground.</summary>
    public int Loads { get; internal set; }

    public FogSetting? Fog { get; internal set; }

    public WaterPlane? Water { get; internal set; }

    /// <summary>The compact <c>setWater(FFF)</c> form (declared and registered, no call site; raw values).</summary>
    public (float A, float B, float C)? WaterCompact { get; internal set; }

    public List<WaterPlane> WaterLimits { get; } = [];

    public Dictionary<int, TrafficEntry> Traffic { get; } = [];

    public List<(Vector3 A, Vector3 B, float Radius, string Kind)> TrafficHalts { get; } = [];

    public float PedestrianDensity { get; internal set; }

    public HashSet<int> PedestrianTypes { get; } = [];

    /// <summary>Instance nodes created for the placed objects (see <see cref="MapWorldOptions.InstantiatePlacedObjects"/>).</summary>
    public List<int> PlacedInstances { get; } = [];

    /// <summary>Placed objects whose gametype entry carries its own <c>script</c> line (not run).</summary>
    public int ScriptedPlacedObjects { get; internal set; }

    /// <summary>Last route of <c>findRoute</c> (straight segment, GUESSED: no road graph is loaded).</summary>
    public (Vector3 From, Vector3 To)? Route { get; internal set; }

    public IGroundQuery? Ground => World?.Ground;
}

/// <summary>Callbacks for areas that follow map loading (renderer: scene, fog, water; physics: ground; audio).</summary>
public interface IMapWorldListener
{
    void MapLoaded(MapState map);

    void MapUnloaded(MapState map);

    void FogChanged(MapState map);
}

/// <summary>What a physics/vehicle area needs: the ground under any instance that sits (directly or not) below a loaded map.</summary>
public interface IMapGroundProvider
{
    /// <summary>The loaded map that <paramref name="instanceId"/> belongs to (walks the parents), or null.</summary>
    MapState? MapOf(int instanceId);

    /// <summary>The ground query of the map that <paramref name="instanceId"/> belongs to, or null.</summary>
    IGroundQuery? GroundFor(int instanceId);
}

/// <summary>
/// The maps known to the script layer, keyed by the ground node id (<c>new GroundRef(archive:0x1r)</c>). Builds the
/// <see cref="StaticWorld"/> of the ground's archive lazily on the first GroundRef native call (GUESSED trigger: the exe's
/// load point is not traced; every Track.enter calls <c>setWater</c>/<c>setFog</c> before the car is placed) and drops it on
/// <c>unload()</c> (MEASURED call site: <c>Track.exit</c> ends with <c>map.unload()</c>).
/// </summary>
public sealed class MapWorlds : IMapGroundProvider
{
    private readonly Dictionary<int, MapState> _maps = [];
    private readonly Dictionary<string, StaticWorld> _cache = new(StringComparer.OrdinalIgnoreCase);

    private ResourceEngine? _resources;

    public MapWorlds(ResourceEngine? resources, MapWorldOptions? options = null)
    {
        _resources = resources;
        Options = options ?? new MapWorldOptions();
    }

    /// <summary>The Resources area (set by the constructor or <see cref="Attach"/>).</summary>
    public ResourceEngine Resources => _resources ?? throw new InvalidOperationException("MapWorlds is not attached to a ResourceEngine");

    /// <summary>Late binding for hosts that create the Resources area after the native modules.</summary>
    public void Attach(ResourceEngine resources) => _resources = resources;

    public MapWorldOptions Options { get; }

    public List<IMapWorldListener> Listeners { get; } = [];

    public IReadOnlyDictionary<int, MapState> Maps => _maps;

    /// <summary>Every world build in order: (archive, ms, ok).</summary>
    public List<(string Archive, double Ms, bool Ok)> LoadLog { get; } = [];

    /// <summary>State of a ground without building its world.</summary>
    public MapState State(int groundId)
    {
        if (!_maps.TryGetValue(groundId, out var m))
        {
            var node = Resources.Tree.Get(groundId);
            m = new MapState { GroundId = groundId, ArchiveName = node?.Archive?.Name ?? Resources.Archives.ArchiveName(groundId) };
            _maps[groundId] = m;
        }

        return m;
    }

    /// <summary>State of a ground with its world built (once; a failed build is not retried).</summary>
    public MapState EnsureLoaded(int groundId)
    {
        var m = State(groundId);
        if (m.World is not null || m.LoadError is not null)
        {
            return m;
        }

        var sw = Stopwatch.StartNew();
        var node = Resources.Tree.Get(groundId);
        var slot = node?.Archive;
        if (slot?.Archive is not { } rpk)
        {
            m.LoadError = node is null ? $"ground {ResourceIds.Format(groundId)} does not resolve" : $"{node} has no archive";
            LoadLog.Add((m.ArchiveName, 0, false));
            return m;
        }

        var key = slot.File ?? slot.Name;
        try
        {
            if (!(Options.CacheWorlds && _cache.TryGetValue(key, out var world)))
            {
                world = WorldLoader.Load(rpk, Path.GetFileNameWithoutExtension(key), new WorldLoadOptions
                {
                    GameRoot = Options.GameRoot,
                    IncludeObjectCollision = Options.IncludeObjectCollision,
                    BuildRenderScene = Options.BuildRenderScene,
                });
                if (Options.CacheWorlds)
                {
                    _cache[key] = world;
                }
            }

            m.World = world;
        }
        catch (Exception ex) when (ex is Slrr.Core.SlrrFormatException or IOException or InvalidDataException or ArgumentException or InvalidOperationException or IndexOutOfRangeException)
        {
            m.LoadError = $"{ex.GetType().Name}: {ex.Message}";
        }

        if (m.World is { } w && Options.InstantiatePlacedObjects)
        {
            Instantiate(m, w, slot);
        }

        m.LoadMs = sw.Elapsed.TotalMilliseconds;
        m.Loads++;
        LoadLog.Add((m.ArchiveName, m.LoadMs, m.World is not null));
        if (m.World is not null)
        {
            foreach (var l in Listeners)
            {
                l.MapLoaded(m);
            }
        }

        return m;
    }

    private void Instantiate(MapState m, StaticWorld w, ArchiveSlot slot)
    {
        foreach (var o in w.Objects)
        {
            var typeId = Resources.Archives.MapId(slot, o.GameType);
            var n = Resources.Tree.CreateInstance(ResourceTypes.InstanceGame, m.GroundId, o.Name, typeId);
            n.Pos[0] = o.Position.X;
            n.Pos[1] = o.Position.Y;
            n.Pos[2] = o.Position.Z;
            n.Ori[0] = o.Angles.X;
            n.Ori[1] = o.Angles.Y;
            n.Ori[2] = o.Angles.Z;
            n.Properties["map.object"] = o;
            m.PlacedInstances.Add(n.Id);
            if (Resources.Tree.Get(typeId)?.Text?.Get("script") is not null)
            {
                m.ScriptedPlacedObjects++;
            }
        }
    }

    /// <summary><c>unload()</c> on a ground: drop the world and the placed-object instances (fog/water/traffic state is kept).</summary>
    public bool Unload(int groundId)
    {
        if (!_maps.TryGetValue(groundId, out var m) || m.World is null)
        {
            return false;
        }

        foreach (var id in m.PlacedInstances)
        {
            if (Resources.Tree.Get(id) is { } n)
            {
                Resources.Destroy(n);
            }
        }

        m.PlacedInstances.Clear();
        m.ScriptedPlacedObjects = 0;
        foreach (var l in Listeners)
        {
            l.MapUnloaded(m);
        }

        m.World = null;
        return true;
    }

    internal void RaiseFog(MapState m)
    {
        foreach (var l in Listeners)
        {
            l.FogChanged(m);
        }
    }

    public MapState? MapOf(int instanceId)
    {
        for (int id = instanceId, guard = 0; id != 0 && guard < 256; guard++)
        {
            if (_maps.TryGetValue(id, out var m) && m.World is not null)
            {
                return m;
            }

            var n = Resources.Tree.Get(id);
            if (n is null || n.ParentId == id)
            {
                break;
            }

            id = n.ParentId;
        }

        return null;
    }

    public IGroundQuery? GroundFor(int instanceId) => MapOf(instanceId)?.Ground;

    /// <summary>
    /// Ground point under <paramref name="p"/>: first the surface at or below <c>p.y + margin</c>, else the top-most surface
    /// (GUESSED search rule; the exe's road/ground snapping is not traced).
    /// </summary>
    public static bool TryGround(IGroundQuery g, Vector3 p, float margin, out SurfaceHit hit) =>
        g.TryGetGround(p.X, p.Z, p.Y + margin, out hit) || g.TryGetGround(p.X, p.Z, float.PositiveInfinity, out hit);
}
