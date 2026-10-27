using Slrr.Vm;

namespace Slrr.Engine.Parts;

/// <summary>
/// What the parts layer needs from the resource/instance layer (owned by another area; see docs/engine/parts.md).
/// The default <see cref="DefaultPartHost"/> is self-contained so the parts natives run headless and in tests.
/// </summary>
public interface IPartHost
{
    /// <summary>Instance id of a game object (what <c>GameRef.id()</c> returns for it).</summary>
    int IdOf(VmObject obj);

    /// <summary>Script object of a live instance id, or null.</summary>
    VmObject? Resolve(int id);

    /// <summary>Makes a script-side GameRef handle point at <paramref name="target"/> (install_OK's out parameter).</summary>
    void Bind(VmObject gameRef, VmObject? target);

    /// <summary>
    /// The part script object behind a GameRef-like argument (e.g. a <c>Vehicle</c> or a plain GameRef handle passed
    /// to install_OK), or null. The default returns the object itself when it is a Part.
    /// </summary>
    VmObject? PartBehind(VmObject gameRef);
}

/// <summary>Self-contained host: ids are <see cref="VmObject.Id"/>, bindings are kept in a table.</summary>
public class DefaultPartHost : IPartHost
{
    private readonly Dictionary<int, WeakReference<VmObject>> _objects = [];
    private readonly System.Runtime.CompilerServices.ConditionalWeakTable<VmObject, VmObject?> _bindings = [];

    public virtual int IdOf(VmObject obj)
    {
        _objects[obj.Id] = new WeakReference<VmObject>(obj);
        return obj.Id;
    }

    public virtual VmObject? Resolve(int id) =>
        id != 0 && _objects.TryGetValue(id, out var w) && w.TryGetTarget(out var o) ? o : null;

    public virtual void Bind(VmObject gameRef, VmObject? target) => _bindings.AddOrUpdate(gameRef, target);

    /// <summary>What <see cref="Bind"/> stored for a handle (a GameRef owner can serve <c>getScriptInstance</c> from it).</summary>
    public VmObject? BoundTarget(VmObject gameRef) => _bindings.TryGetValue(gameRef, out var t) ? t : null;

    public virtual VmObject? PartBehind(VmObject gameRef)
    {
        if (gameRef.Class.IsSubclassOf(PartsWorld.PartClass))
        {
            return gameRef;
        }

        if (BoundTarget(gameRef) is { } bound)
        {
            return bound;
        }

        // java.game.Vehicle keeps its chassis in a field named `chassis` (Vehicle.java): follow it.
        return ScriptObjects.GetObject(gameRef, "chassis") is { } c && c.Class.IsSubclassOf(PartsWorld.PartClass) ? c : null;
    }
}

/// <summary>Output of the dyno computation (the fields calcDyno writes back into the DynoData object).</summary>
public sealed record DynoCurve(float Torque, float Torque2, float MaxRpm, float MaxHp, float MaxTorque, float RpmMaxTorque, float RpmMaxHp);

/// <summary>
/// Engine model behind <c>DynoData.calcDyno/getTorque/getHP</c> and <c>Chassis.getTorque</c>. This is physics
/// (Phase 4), not data model: the parts layer only routes the calls. <see cref="NullDynoCalculator"/> is the
/// placeholder default.
/// </summary>
public interface IDynoCalculator
{
    /// <summary>Computes the curve for <paramref name="dyno"/> with <paramref name="steps"/> rpm steps; null = leave the fields unchanged.</summary>
    DynoCurve? Calculate(DynoParams dyno, float steps);

    float Torque(DynoParams dyno, float rpm, float boost);

    float Horsepower(DynoParams dyno, float rpm, float boost);
}

/// <summary>Placeholder: no curve (DynoData fields keep their script values), torque and power 0. Not the original model.</summary>
public sealed class NullDynoCalculator : IDynoCalculator
{
    public static readonly NullDynoCalculator Instance = new();

    public DynoCurve? Calculate(DynoParams dyno, float steps) => null;

    public float Torque(DynoParams dyno, float rpm, float boost) => 0f;

    public float Horsepower(DynoParams dyno, float rpm, float boost) => 0f;
}
