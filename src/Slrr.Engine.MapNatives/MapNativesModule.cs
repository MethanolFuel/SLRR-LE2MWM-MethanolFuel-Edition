using System.Numerics;
using Slrr.Engine.Resources;
using Slrr.Vm;

namespace Slrr.Engine.MapNatives;

/// <summary>Switches of <see cref="MapNativesModule"/>.</summary>
public sealed class MapNativeOptions
{
    /// <summary>Build the map world on the first GroundRef native call on a ground (GUESSED trigger). Off: only explicit <see cref="MapWorlds.EnsureLoaded"/>.</summary>
    public bool LoadOnFirstGroundCall { get; init; } = true;

    /// <summary>Wrap <c>ResourceRef.unload()V</c> so <c>map.unload()</c> drops the world (the original registration still runs first).</summary>
    public bool WrapUnload { get; init; } = true;

    /// <summary>Second element of <c>alignToRoad</c> (road direction) while no road graph is loaded. GUESSED.</summary>
    public Vector3 DefaultRoadDirection { get; init; } = Vector3.UnitZ;

    /// <summary>Called on every <c>setFog</c> (ground id, rgb, near, far): lets the host keep its own fog table
    /// (Slrr.Game's <c>HostNatives.Fog</c>) in sync after this module replaces its <c>setFog</c>.</summary>
    public Action<int, int, float, float>? FogSink { get; init; }

    /// <summary>Register the Vector3/Ypr math natives the track scripts need (<see cref="MapNatives.MathNatives"/>), only where nobody else did.</summary>
    public bool MathNatives { get; init; } = true;
}

/// <summary>
/// Map natives for the script layer: GroundRef (21 declared + 5 exe-only), Animation (9), ParticleSystem (9), all
/// registered with replace semantics (the game host already stubs <c>GroundRef.setFog</c>). Backed by
/// <see cref="MapWorlds"/> (Slrr.World collision world, surfaces, placed objects) and the Resources tree.
/// </summary>
public sealed class MapNativesModule : INativeModule
{
    /// <param name="resources">The Resources area; null when the host creates it later (then call <see cref="Attach"/> before the first native runs).</param>
    public MapNativesModule(ResourceEngine? resources, MapWorldOptions? worldOptions = null, MapNativeOptions? options = null)
    {
        Options = options ?? new MapNativeOptions();
        Maps = new MapWorlds(resources, worldOptions);
        Ground = new GroundNatives(Maps, Options);
        Animations = new AnimationNatives();
        Particles = new ParticleNatives(Maps);
    }

    public void Attach(ResourceEngine resources) => Maps.Attach(resources);

    public MapNativeOptions Options { get; }

    public MapWorlds Maps { get; }

    public GroundNatives Ground { get; }

    public AnimationNatives Animations { get; }

    public ParticleNatives Particles { get; }

    /// <summary>Every key this module registered, and whether it replaced an existing registration.</summary>
    public List<(NativeKey Key, bool Replaced)> Registered { get; } = [];

    public void Register(NativeRegistry registry)
    {
        void Add(string cls, string name, string desc, NativeMethod fn)
        {
            var replaced = registry.Contains(cls, name, desc);
            registry.Register(cls, name, desc, fn, replace: true);
            Registered.Add((new NativeKey(cls, name, desc), replaced));
        }

        Ground.Register(registry, Add);
        Animations.Register(Add);
        Particles.Register(Add);
        if (Options.MathNatives)
        {
            MathNatives.Register((cls, name, desc, fn) =>
            {
                if (!registry.Contains(cls, name, desc))
                {
                    Add(cls, name, desc, fn);
                }
            });
        }

        if (Options.WrapUnload && registry.Find(ResourceEngine.ResourceRefClass, "unload", "()V") is { } original
            && registry.Contains(ResourceEngine.ResourceRefClass, "unload", "()V"))
        {
            Add(ResourceEngine.ResourceRefClass, "unload", "()V", c =>
            {
                var r = original(c);
                Maps.Unload(ResourceEngine.IdOf(c.This));
                return r;
            });
        }
    }
}
