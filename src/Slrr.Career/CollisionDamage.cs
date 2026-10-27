using System.Globalization;
using Slrr.Engine.Parts;
using Slrr.Engine.Resources;
using Slrr.Game;
using Slrr.Game.Physics;
using Slrr.Vm;

namespace Slrr.Career;

/// <summary>Switches and coefficients of <see cref="CollisionDamageBridge"/>.</summary>
public sealed class CollisionDamageOptions
{
    /// <summary>Post <c>EVENT_COLLISION</c> for impacts at or above this speed change [m/s]. GUESSED (the exe compares
    /// the contact's impulse at record+0xA0 with a per-body threshold at body+0x18, values not decoded).</summary>
    public float EventThreshold { get; set; } = 1.0f;

    /// <summary>Speed change below which an impact does no damage [m/s]. GUESSED.</summary>
    public float DamageThreshold { get; set; } = 2.0f;

    /// <summary>Speed change that removes <c>Config.external_damage</c> of tear from a body part [m/s]. GUESSED.</summary>
    public float DamageScale { get; set; } = 20.0f;

    /// <summary>Share of the body-part damage the chassis takes. GUESSED.</summary>
    public float ChassisShare { get; set; } = 0.5f;

    /// <summary>Minimum simulated time between two events (and damage applications) of one (car, other) pair [s]. GUESSED.</summary>
    public double MinInterval { get; set; } = 0.1;

    /// <summary>Apply damage to the parts' tear (off: events only).</summary>
    public bool Damage { get; set; } = true;
}

/// <summary>One event the bridge posted / one damage application, for transcripts and tests.</summary>
public readonly record struct CollisionRecord(float Time, int CarId, int OtherId, float DeltaV, int PartsDamaged, float TearRemoved);

/// <summary>
/// Connects the physics impacts (<see cref="PhysicsScene.Impact"/>) to what the shipped scripts handle:
/// <list type="bullet">
/// <item><b>EVENT_COLLISION (1)</b> posted to the car's instance, delivered to the car's script (when its mask accepts it)
/// and to every <c>addNotification(car, EVENT_COLLISION)</c> subscriber (City: police alert, crash count, camera; Garage:
/// jack). Payload MEASURED from the exe: the collision dispatcher (0x426800 callers 0x4a36a4..0x4a3739) posts code 1 to
/// both bodies of a contact record and to their root objects; the event translator 0x425c60 (case code 1, 0x425d6b)
/// formats the text with <c>"%d"</c> (0x60c9c8) as the instance id of the <b>other</b> body's root object. So the
/// text is one token, the other object's id; City reads <c>token(0).intValue()</c> and asks <c>getInfo(GII_CATEGORY)</c>.
/// For a map impact the "other object" here is the map's instance (GUESSED: the exe would name the hit map object).</item>
/// <item><b>Damage</b>: the exe has no damage event for scripts (no 0x426800 caller posts EVENT_DAMAGE 8, MEASURED); damage
/// is native part state the scripts read with <c>Part.getWear/getTear</c> (Mechanic's repair screen: <c>getCondition()
/// = wear·tear·children</c>, <c>repairCost()</c>). The model here is GUESSED: every body part (<c>java.game.parts.BodyPart</c>
/// subclasses) of the car loses <c>external_damage · damage_multiplier · (dv − threshold) / scale</c> of tear, the chassis
/// <see cref="CollisionDamageOptions.ChassisShare"/> of it, clamped at 0; then the part's script <c>updateDamage()</c>
/// callback runs (exe string 0x60e4b0, when the exe calls it is not traced).</item>
/// </list>
/// </summary>
public sealed class CollisionDamageBridge
{
    public const int EventCollision = 1;
    public const string BodyPartClass = "java.game.parts.BodyPart";

    private readonly GameHost _host;
    private readonly GamePhysicsWorld _physics;
    private readonly Func<int> _mapId;
    private readonly List<ImpactEvent> _pending = [];
    private readonly Dictionary<(int, int), double> _lastPost = [];

    public CollisionDamageBridge(GameHost host, GamePhysicsWorld physics, Func<int> mapId, CollisionDamageOptions? options = null)
    {
        _host = host;
        _physics = physics;
        _mapId = mapId;
        Options = options ?? new CollisionDamageOptions();
        physics.Scene.Impact += i => _pending.Add(i);
        physics.Scene.Stepped += _ => Flush();
    }

    public CollisionDamageOptions Options { get; }

    public List<CollisionRecord> Records { get; } = [];

    /// <summary>
    /// Turns the impacts collected since the last call into events and damage: at most one event per (car, other) pair
    /// per <see cref="CollisionDamageOptions.MinInterval"/> (GUESSED rate; the exe posts per contact and the scripts
    /// rate-limit themselves, City by 1 s). Runs after every physics step (<see cref="PhysicsScene.Stepped"/>).
    /// </summary>
    public int Flush()
    {
        if (_pending.Count == 0)
        {
            return 0;
        }

        var posted = 0;
        foreach (var g in _pending.GroupBy(i => (i.CarId, i.Other)))
        {
            var strongest = g.MaxBy(i => i.DeltaV);
            if (strongest.DeltaV < Options.EventThreshold || _host.Resources.Tree.Get(strongest.CarId) is null
                || (_lastPost.TryGetValue(g.Key, out var last) && strongest.Time - last < Options.MinInterval))
            {
                continue;
            }

            _lastPost[g.Key] = strongest.Time;

            var other = strongest.Other != 0 ? strongest.Other : _mapId();
            _host.Resources.QueueEvent(strongest.CarId, 0, EventCollision, other.ToString(CultureInfo.InvariantCulture));
            posted++;
            var (parts, tear) = Options.Damage ? ApplyDamage(strongest) : (0, 0f);
            Records.Add(new CollisionRecord(_host.Vm.Time, strongest.CarId, other, strongest.DeltaV, parts, tear));
        }

        _pending.Clear();
        return posted;
    }

    private (int Parts, float Tear) ApplyDamage(ImpactEvent impact)
    {
        var severity = (impact.DeltaV - Options.DamageThreshold) / Options.DamageScale;
        if (severity <= 0 || _host.Resources.Tree.Get(impact.CarId)?.Script is not { } car || _host.Parts.Find(ChassisOf(car)) is not { } chassis)
        {
            return (0, 0f);
        }

        var external = ConfigFloat("external_damage", 0.25f);
        var multiplier = _physics.Scene.Car(impact.CarId) is { } sim && sim.Settings.TryGetValue("damage_multiplier", out var m)
                         && float.TryParse(m, NumberStyles.Float, CultureInfo.InvariantCulture, out var dm) ? dm : 1f;
        var amount = external * multiplier * severity;
        var parts = 0;
        var removed = 0f;
        foreach (var p in PartsWorld.Tree(chassis.Root))
        {
            if (!p.Object.Class.IsSubclassOf(BodyPartClass) && !p.IsChassis)
            {
                continue;
            }

            var d = p.IsChassis ? amount * Options.ChassisShare : amount;
            var before = p.Tear;
            p.Tear = Math.Max(0f, p.Tear - d);
            if (p.Tear < before)
            {
                parts++;
                removed += before - p.Tear;
                RunCallback(p.Object, "updateDamage");
            }
        }

        return (parts, removed);
    }

    private VmObject ChassisOf(VmObject car) => Probe.Field(car, "chassis").AsObject ?? car;

    private float ConfigFloat(string field, float fallback)
    {
        var v = Probe.Static(_host.Vm, "java.util.Config", field);
        return v.Kind == VmKind.Float ? v.AsFloat : fallback;
    }

    private void RunCallback(VmObject obj, string method)
    {
        if (obj.Class.FindMethods(method, 0).FirstOrDefault(x => !x.IsStatic && x.Code is not null) is { } mth)
        {
            _host.Vm.Spawn(mth, obj, [], "THRD-DAMAGE");
        }
    }

    /// <summary>Script-visible condition of the car's chassis (<c>Part.getCondition()</c> evaluated in the VM), NaN when unknown.</summary>
    public static float Condition(GameHost host, VmObject? car) => CallFloat(host, Probe.Field(car, "chassis").AsObject, "getCondition");

    /// <summary>Script-visible repair cost of the car's chassis tree (<c>Part.repairCost()</c>), NaN when unknown.</summary>
    public static float RepairCost(GameHost host, VmObject? car) => CallFloat(host, Probe.Field(car, "chassis").AsObject, "repairCost");

    public static float CallFloat(GameHost host, VmObject? obj, string method)
    {
        if (obj is null || obj.Class.FindMethods(method, 0).FirstOrDefault(m => !m.IsStatic) is not { } m)
        {
            return float.NaN;
        }

        var th = host.Vm.Spawn(m, obj, [], "career:" + method);
        host.Driver.Settle();
        return th.State == VmThreadState.Finished ? (th.Result.Kind == VmKind.Int ? th.Result.AsInt : th.Result.AsFloat) : float.NaN;
    }

    /// <summary>Sum of the tear of the car's body parts (chassis included) and their count, read from the parts layer.</summary>
    public static (int Parts, float TearSum) BodyTear(GameHost host, VmObject? car)
    {
        if (Probe.Field(car, "chassis").AsObject is not { } ch || host.Parts.Find(ch) is not { } chassis)
        {
            return (0, 0f);
        }

        var body = PartsWorld.Tree(chassis.Root).Where(p => p.IsChassis || p.Object.Class.IsSubclassOf(BodyPartClass)).ToList();
        return (body.Count, body.Sum(p => p.Tear));
    }

    internal static int IdOf(VmObject? o) => ResourceEngine.IdOf(o);
}
