using System.Numerics;
using Slrr.Engine.MapNatives;
using Slrr.Engine.Resources;
using Slrr.Vm;

namespace Slrr.Roads;

/// <summary>What <c>Navigator.updateNavigator</c> last saw (for a minimap renderer): the car, the mode, the car's position
/// and the lane under it.</summary>
public sealed class NavigatorState
{
    public int Updates { get; internal set; }

    public int CarId { get; internal set; }

    public int Mode { get; internal set; }

    public Vector3? CarPosition { get; internal set; }

    public LaneHit? Lane { get; internal set; }
}

/// <summary>
/// Road natives backed by the <c>.trc</c> road graph, registered with replace semantics on top of
/// <see cref="MapNativesModule"/>'s collision-ground stand-ins: <c>GroundRef.alignToRoad</c>, <c>getNearestCross</c>,
/// <c>getStartDirection</c>, <c>getRouteLength(V,V)</c>, <c>findRoute</c>, <c>getRouteLength()</c>, <c>getRoutePos</c>,
/// <c>getRouteDist</c> and <c>java.game.Navigator.updateNavigator(GameRef,I)V</c>. On a ground without a road graph (every
/// track-selector map except the city ones) each native calls the registration it replaced, so the map-natives behaviour is
/// unchanged there. Register AFTER <see cref="MapNativesModule"/> (and before the render hooks).
/// </summary>
public sealed class RoadNativesModule : INativeModule
{
    public const string GroundCls = "java.util.resource.GroundRef";
    public const string NavigatorCls = "java.game.Navigator";
    private const string LV = "Ljava.lang.Vector3;";
    private const string LGR = "Ljava.util.resource.GameRef;";

    private readonly MapWorlds _maps;
    private readonly Dictionary<int, RoadRoute?> _routes = [];

    public RoadNativesModule(MapWorlds maps, RoadGraphCatalog catalog)
    {
        _maps = maps;
        Catalog = catalog;
    }

    public RoadGraphCatalog Catalog { get; }

    public NavigatorState Navigator { get; } = new();

    /// <summary>Calls answered from a road graph / passed to the replaced registration, per native name.</summary>
    public Dictionary<string, (int Graph, int Fallback)> Calls { get; } = new(StringComparer.Ordinal);

    /// <summary>Every key this module registered and whether it replaced an existing registration.</summary>
    public List<(NativeKey Key, bool Replaced)> Registered { get; } = [];

    /// <summary>The road graph of a ground (GroundRef id), building the map world first like the map natives do; null when
    /// the map names no <c>.trc</c>.</summary>
    public RoadGraph? GraphOf(int groundId)
    {
        var m = _maps.EnsureLoaded(groundId);
        return m.World is { } w ? Catalog.ForArchive(w.Archive) : null;
    }

    /// <summary>The route stored by the last <c>findRoute</c> on a ground.</summary>
    public RoadRoute? RouteOf(int groundId) => _routes.GetValueOrDefault(groundId);

    public void Register(NativeRegistry registry)
    {
        void Add(string cls, string name, string desc, Func<NativeCall, RoadGraph, Value> withGraph, Func<NativeCall, Value> fallbackDefault)
        {
            var previous = registry.Find(cls, name, desc);
            var replaced = registry.Contains(cls, name, desc);
            registry.Register(cls, name, desc, c =>
            {
                var g = cls == GroundCls ? GraphOf(ResourceEngine.IdOf(c.Self)) : null;
                var (hit, miss) = Calls.GetValueOrDefault(name);
                if (g is not null)
                {
                    Calls[name] = (hit + 1, miss);
                    return withGraph(c, g);
                }

                Calls[name] = (hit, miss + 1);
                return previous is not null ? previous(c) : fallbackDefault(c);
            }, replace: true);
            Registered.Add((new NativeKey(cls, name, desc), replaced));
        }

        Add(GroundCls, "alignToRoad", $"({LV})[{LV}", (c, g) =>
        {
            var p = Vec(c, 0);
            return g.AlignToRoad(p) is { } a
                ? Value.Obj(ScriptValues.NewVectorArray(c.Vm, a.Position, a.Direction))
                : Value.Obj(ScriptValues.NewVectorArray(c.Vm, p, Vector3.UnitZ)); // GUESSED: no lane within reach
        }, c => Value.Obj(ScriptValues.NewVectorArray(c.Vm, Vec(c, 0), Vector3.UnitZ)));

        Add(GroundCls, "getNearestCross", $"({LV}F){LV}", (c, g) =>
        {
            var p = Vec(c, 0);
            return Value.Obj(ScriptValues.NewVector(c.Vm, g.NearestCross(p, c.Float(1))?.Position ?? p));
        }, c => Value.Obj(ScriptValues.NewVector(c.Vm, Vec(c, 0))));

        Add(GroundCls, "getStartDirection", $"({LV}{LV}){LV}", (c, g) =>
            g.StartDirection(Vec(c, 0), Vec(c, 1)) is { } d ? Value.Obj(ScriptValues.NewVector(c.Vm, d)) : Value.Null,
            _ => Value.Null);

        Add(GroundCls, "getRouteLength", $"({LV}{LV})F", (c, g) => Value.Float(g.RouteLength(Vec(c, 0), Vec(c, 1))),
            c => Value.Float(Vector3.Distance(Vec(c, 0), Vec(c, 1))));

        // exe-only natives (no shipped call site); findRoute shares getRouteLength's handler in the exe (0x483750, MEASURED)
        Add(GroundCls, "findRoute", $"({LV}{LV})F", (c, g) =>
        {
            var r = g.FindRoute(Vec(c, 0), Vec(c, 1));
            _routes[ResourceEngine.IdOf(c.Self)] = r;
            return Value.Float(r?.Length ?? -1f);
        }, c => Value.Float(Vector3.Distance(Vec(c, 0), Vec(c, 1))));
        Add(GroundCls, "getRouteLength", "()F", (c, _) => Value.Float(RouteOf(ResourceEngine.IdOf(c.Self))?.Length ?? -1f), _ => Value.Float(-1f));
        Add(GroundCls, "getRoutePos", $"(F){LV}", (c, _) =>
            RouteOf(ResourceEngine.IdOf(c.Self)) is { } r ? Value.Obj(ScriptValues.NewVector(c.Vm, r.Sample(c.Float(0)).Position)) : Value.Null,
            _ => Value.Null);
        Add(GroundCls, "getRouteDist", $"({LV})F", (c, _) =>
            RouteOf(ResourceEngine.IdOf(c.Self)) is { } r && r.Project(Vec(c, 0), out var s, out float _lateral) ? Value.Float(s) : Value.Float(-1f),
            _ => Value.Float(-1f));

        Add(NavigatorCls, "updateNavigator", $"({LGR}I)V", (_, _) => Value.Void, c =>
        {
            var car = ResourceEngine.IdOf(c.Object(0));
            Navigator.Updates++;
            Navigator.CarId = car;
            Navigator.Mode = c.Int(1);
            if (car != 0 && _maps.Resources.Tree.Get(car) is { } node)
            {
                var pos = new Vector3(node.Pos[0], node.Pos[1], node.Pos[2]);
                Navigator.CarPosition = pos;
                var ground = _maps.MapOf(car);
                Navigator.Lane = ground?.World is { } w && Catalog.ForArchive(w.Archive) is { } g ? g.NearestLane(pos) : null;
            }

            return Value.Void;
        });
    }

    private static Vector3 Vec(NativeCall c, int i) =>
        ScriptValues.ReadVector(c.Object(i)) ?? throw new VmException($"{c.Method.Signature}: null Vector3 argument {i}");
}
