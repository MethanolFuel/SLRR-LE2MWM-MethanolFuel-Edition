using System.Numerics;
using Slrr.Engine.Resources;
using Slrr.Vm;
using Slrr.World;

namespace Slrr.Engine.MapNatives;

/// <summary>
/// <c>java.util.resource.GroundRef</c>: the 21 natives declared in the shipped class (signatures MEASURED from
/// system/Scripts/util/resource/GroundRef.class) plus the 5 the exe registers without a declaration (names MEASURED,
/// docs/engine-api/java.util.md). Behaviour is GUESSED unless a comment says otherwise: no road graph (<c>.trc</c>),
/// traffic or pedestrian system exists yet, so road queries fall back to the collision ground of the map and traffic
/// calls are recorded only.
/// </summary>
public sealed class GroundNatives(MapWorlds maps, MapNativeOptions options)
{
    public const string Cls = "java.util.resource.GroundRef";
    private const string LV = "Ljava.lang.Vector3;";
    private const string LGR = "Ljava.util.resource.GameRef;";

    /// <summary>Height margin above a query point when looking for the ground below it (GUESSED, see <see cref="MapWorlds.TryGround"/>).</summary>
    public float GroundMargin { get; set; } = 2.0f;

    /// <summary>Calls per native (for reports).</summary>
    public Dictionary<string, int> Calls { get; } = new(StringComparer.Ordinal);

    public void Register(NativeRegistry r, Action<string, string, string, NativeMethod> add)
    {
        void Add(string name, string desc, NativeMethod fn) => add(Cls, name, desc, c =>
        {
            Calls[name] = Calls.GetValueOrDefault(name) + 1;
            return fn(c);
        });

        // ---- water / fog: stored for the renderer (IMapWorldListener)
        Add("setWater", "(FFF)V", c =>
        {
            Map(c).WaterCompact = (c.Float(0), c.Float(1), c.Float(2));
            return Value.Void;
        });
        Add("setWater", $"({LV}{LV}FF)V", c =>
        {
            Map(c).Water = new WaterPlane(Vec(c, 0), Vec(c, 1), c.Float(2), c.Float(3));
            return Value.Void;
        });
        Add("addWaterLimit", $"({LV}{LV})V", c =>
        {
            Map(c).WaterLimits.Add(new WaterPlane(Vec(c, 0), Vec(c, 1), 0, 0));
            return Value.Void;
        });
        Add("setFog", "(IFF)V", c =>
        {
            var m = Map(c);
            m.Fog = new FogSetting(c.Int(0), c.Float(1), c.Float(2));
            options.FogSink?.Invoke(m.GroundId, c.Int(0), c.Float(1), c.Float(2));
            maps.RaiseFog(m);
            return Value.Void;
        });

        // ---- traffic: recorded, never simulated (GUESSED return values: ids count from 1, spawners return 0 cars)
        Add("addTrafficCar", $"({LGR}{LV})I", c =>
        {
            var m = Map(c);
            var id = NextTrafficId(m);
            m.Traffic[id] = new TrafficEntry(id, ResourceEngine.IdOf(c.Object(0)), "car", ReadV(c, 1), 1, 0, 0, 0);
            return Value.Int(id);
        });
        Add("remTrafficCar", "(I)V", c =>
        {
            Map(c).Traffic.Remove(c.Int(0));
            return Value.Void;
        });
        Add("notifyTrafficCar", "(II)I", c =>
        {
            if (Map(c).Traffic.TryGetValue(c.Int(0), out var t))
            {
                t.Notify = c.Int(1);
            }

            return Value.Zero;
        });
        Add("addTrafficN", $"({LGR}IFFF)I", c =>
        {
            var m = Map(c);
            var id = NextTrafficId(m);
            m.Traffic[id] = new TrafficEntry(id, ResourceEngine.IdOf(c.Object(0)), "spawnN", null, c.Int(1), c.Float(2), c.Float(3), c.Float(4));
            return Value.Zero;
        });
        Add("addTrafficP", $"({LGR}{LV}IFFF)I", c =>
        {
            var m = Map(c);
            var id = NextTrafficId(m);
            m.Traffic[id] = new TrafficEntry(id, ResourceEngine.IdOf(c.Object(0)), "spawnP", ReadV(c, 1), c.Int(2), c.Float(3), c.Float(4), c.Float(5));
            return Value.Zero;
        });
        Add("delTraffic", "()V", c =>
        {
            var m = Map(c, load: false);
            m.Traffic.Clear();
            m.TrafficHalts.Clear();
            return Value.Void;
        });
        Add("setTrafficCarBehaviour", "(II)V", c =>
        {
            if (Map(c).Traffic.TryGetValue(c.Int(0), out var t))
            {
                t.Behaviour = c.Int(1);
            }

            return Value.Void;
        });
        Add("haltTrafficCross", $"({LV}F)V", c =>
        {
            Map(c).TrafficHalts.Add((Vec(c, 0), Vec(c, 0), c.Float(1), "cross"));
            return Value.Void;
        });
        Add("haltTrafficPath", $"({LV}{LV})V", c =>
        {
            Map(c).TrafficHalts.Add((Vec(c, 0), Vec(c, 1), 0, "path"));
            return Value.Void;
        });

        // ---- road queries: the collision ground stands in for the road graph
        Add("getNearestCross", $"({LV}F)" + LV, c => Value.Obj(ScriptValues.NewVector(c.Vm, OnGround(Map(c), Vec(c, 0)))));
        Add("getStartDirection", $"({LV}{LV})" + LV, c => Value.Obj(ScriptValues.NewVector(c.Vm, Direction(Vec(c, 0), Vec(c, 1)))));
        Add("getRouteLength", $"({LV}{LV})F", c => Value.Float(Vector3.Distance(Vec(c, 0), Vec(c, 1))));
        Add("alignToRoad", $"({LV})[" + LV, c =>
        {
            var p = OnGround(Map(c), Vec(c, 0));
            return Value.Obj(ScriptValues.NewVectorArray(c.Vm, p, options.DefaultRoadDirection));
        });

        // ---- pedestrians: recorded
        Add("setPedestrianDensityN", "(F)V", c =>
        {
            Map(c, load: false).PedestrianDensity = c.Float(0);
            return Value.Void;
        });
        Add("addPedestrianType", $"({LGR})V", c =>
        {
            Map(c).PedestrianTypes.Add(ResourceEngine.IdOf(c.Object(0)));
            return Value.Void;
        });
        Add("remPedestrianType", $"({LGR})V", c =>
        {
            Map(c).PedestrianTypes.Remove(ResourceEngine.IdOf(c.Object(0)));
            return Value.Void;
        });
        Add("pedestrianDistance", $"({LV}I)F", _ => Value.Float(-1f));

        // ---- registered by the exe, not declared by the shipped class (no call site; kept for completeness)
        Add("removePedestrianType", $"({LGR})V", c =>
        {
            Map(c).PedestrianTypes.Remove(ResourceEngine.IdOf(c.Object(0)));
            return Value.Void;
        });
        Add("findRoute", $"({LV}{LV})F", c =>
        {
            var m = Map(c);
            m.Route = (Vec(c, 0), Vec(c, 1));
            return Value.Float(Vector3.Distance(Vec(c, 0), Vec(c, 1)));
        });
        Add("getRouteLength", "()F", c => Value.Float(Map(c).Route is { } r ? Vector3.Distance(r.From, r.To) : -1f));
        Add("getRouteDist", $"({LV})F", c => Value.Float(Map(c).Route is { } r ? Vector3.Distance(r.From, Vec(c, 0)) : -1f));
        Add("getRoutePos", $"(F){LV}", c =>
        {
            var r = Map(c).Route;
            if (r is null)
            {
                return Value.Null;
            }

            var len = Vector3.Distance(r.Value.From, r.Value.To);
            var t = len <= 0 ? 0 : Math.Clamp(c.Float(0) / len, 0, 1);
            return Value.Obj(ScriptValues.NewVector(c.Vm, Vector3.Lerp(r.Value.From, r.Value.To, t)));
        });
    }

    private MapState Map(NativeCall c, bool load = true)
    {
        var id = ResourceEngine.IdOf(c.Self);
        return load && options.LoadOnFirstGroundCall ? maps.EnsureLoaded(id) : maps.State(id);
    }

    private static Vector3 Vec(NativeCall c, int i) => ScriptValues.ReadVector(c.Object(i)) ?? throw new VmException($"{c.Method.Signature}: null Vector3 argument {i}");

    private static Vector3? ReadV(NativeCall c, int i) => ScriptValues.ReadVector(c.Object(i));

    private static int NextTrafficId(MapState m) => m.Traffic.Count == 0 ? 1 : m.Traffic.Keys.Max() + 1;

    /// <summary>The ground point under <paramref name="p"/> (y replaced), or <paramref name="p"/> when the map has no ground there.</summary>
    public Vector3 OnGround(MapState m, Vector3 p) =>
        m.Ground is { } g && MapWorlds.TryGround(g, p, GroundMargin, out SurfaceHit hit) ? new Vector3(p.X, hit.Position.Y, p.Z) : p;

    /// <summary>Unit horizontal direction from <paramref name="a"/> to <paramref name="b"/> (+Z when they coincide). GUESSED.</summary>
    public static Vector3 Direction(Vector3 a, Vector3 b)
    {
        var d = new Vector3(b.X - a.X, 0, b.Z - a.Z);
        return d.LengthSquared() < 1e-12f ? Vector3.UnitZ : Vector3.Normalize(d);
    }
}
