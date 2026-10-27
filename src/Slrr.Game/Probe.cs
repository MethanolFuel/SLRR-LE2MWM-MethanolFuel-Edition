using Slrr.Engine.Resources;
using Slrr.Engine.Ui;
using Slrr.Vm;

namespace Slrr.Game;

/// <summary>Read-only access to script state for assertions and transcripts (never initializes a class as a side effect).</summary>
public static class Probe
{
    public static Value Field(VmObject? obj, string name) =>
        obj is not null && obj.Class.FindField(name) is { IsStatic: false } f ? obj.Fields[f.Slot] : Value.Null;

    /// <summary>Static field of an already initialized class; Null when the class is not loaded yet.</summary>
    public static Value Static(Slrr.Vm.Vm vm, string className, string field) => Script.PeekStatic(vm, className, field) ?? Value.Null;

    public static string? Str(Slrr.Vm.Vm vm, Value v) => v.Kind == VmKind.Object && v.Ref?.Native is string s ? s : null;

    public static int Id(VmObject? o) => ResourceEngine.IdOf(o);

    /// <summary>Elements of a <c>java.util.Vector</c>.</summary>
    public static List<Value> Vector(Slrr.Vm.Vm vm, VmObject? v) => v is null ? [] : Script.VectorItems(vm, v);
}

/// <summary>
/// <see cref="Slrr.Engine.Parts.IPartHost"/> on top of the resource layer: a part's id is its GameRef handle id, an id
/// resolves to the script object of its instance node, and a Vehicle (bound with <c>set(chassis)</c> to the chassis
/// instance, measured from Vehicle's constructors) stands for its chassis.
/// </summary>
public sealed class HostPartHost(GameHost host) : Slrr.Engine.Parts.IPartHost
{
    public int IdOf(VmObject obj) => ResourceEngine.IdOf(obj);

    public VmObject? Resolve(int id) => id == 0 ? null : host.Resources.Tree.Get(id)?.Script;

    public void Bind(VmObject gameRef, VmObject? target) => ResourceEngine.EnsureHandle(gameRef).Id = target is null ? 0 : ResourceEngine.IdOf(target);

    public VmObject? PartBehind(VmObject gameRef)
    {
        if (gameRef.Class.IsSubclassOf(Slrr.Engine.Parts.PartsWorld.PartClass))
        {
            return gameRef;
        }

        return Resolve(ResourceEngine.IdOf(gameRef)) is { } s && s.Class.IsSubclassOf(Slrr.Engine.Parts.PartsWorld.PartClass) ? s : null;
    }
}
