using System.Numerics;
using Slrr.Vm;

namespace Slrr.Engine.Parts;

/// <summary>Euler angles as the script <c>java.lang.Ypr</c> holds them (yaw, pitch, roll; radians).</summary>
public readonly record struct Ypr(float Y, float P, float R);

/// <summary>Field access by name on script objects and conversion of <c>Vector3</c>/<c>Ypr</c> script values.</summary>
public static class ScriptObjects
{
    public const string Vector3Class = "java.lang.Vector3";
    public const string YprClass = "java.lang.Ypr";

    public static bool HasField(VmObject obj, string name) => obj.Class.FindField(name) is { IsStatic: false };

    public static Value Get(VmObject obj, string name)
    {
        var f = obj.Class.FindField(name);
        return f is null ? Value.Void : f.IsStatic ? f.Owner.StaticValues[f.Slot] : obj.Fields[f.Slot];
    }

    public static float GetFloat(VmObject obj, string name, float fallback = 0)
    {
        var v = Get(obj, name);
        return v.Kind is VmKind.Float or VmKind.Int ? v.AsFloat : fallback;
    }

    public static int GetInt(VmObject obj, string name, int fallback = 0)
    {
        var v = Get(obj, name);
        return v.Kind is VmKind.Float or VmKind.Int ? v.AsInt : fallback;
    }

    public static VmObject? GetObject(VmObject obj, string name)
    {
        var v = Get(obj, name);
        return v.Kind == VmKind.Object ? v.Ref : null;
    }

    /// <summary>Stores a value into a named instance field (converted to the declared type); false when absent.</summary>
    public static bool Set(VmObject obj, string name, Value v)
    {
        var f = obj.Class.FindField(name);
        if (f is null)
        {
            return false;
        }

        var c = VmTypes.Coerce(v, f.Descriptor);
        if (f.IsStatic)
        {
            f.Owner.StaticValues[f.Slot] = c;
        }
        else
        {
            obj.Fields[f.Slot] = c;
        }

        return true;
    }

    public static Vector3? ReadVector(VmObject? v) =>
        v is null ? null : new Vector3(GetFloat(v, "x"), GetFloat(v, "y"), GetFloat(v, "z"));

    public static Ypr? ReadYpr(VmObject? v) =>
        v is null ? null : new Ypr(GetFloat(v, "y"), GetFloat(v, "p"), GetFloat(v, "r"));

    /// <summary>A new script <c>Vector3</c> (fields set directly; the script constructor only copies them).</summary>
    public static Value NewVector(Vm.Vm vm, Vector3 v)
    {
        var o = vm.NewObject(vm.LoadClass(Vector3Class));
        Set(o, "x", Value.Float(v.X));
        Set(o, "y", Value.Float(v.Y));
        Set(o, "z", Value.Float(v.Z));
        return Value.Obj(o);
    }

    public static Value NewYpr(Vm.Vm vm, Ypr v)
    {
        var o = vm.NewObject(vm.LoadClass(YprClass));
        Set(o, "y", Value.Float(v.Y));
        Set(o, "p", Value.Float(v.P));
        Set(o, "r", Value.Float(v.R));
        return Value.Obj(o);
    }
}
