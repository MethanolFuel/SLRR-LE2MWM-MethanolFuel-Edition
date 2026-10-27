using System.Numerics;
using Slrr.Vm;

namespace Slrr.Engine.MapNatives;

/// <summary>Small helpers for reading and writing script objects (java.lang.Vector3 / Ypr fields, arrays).</summary>
public static class ScriptValues
{
    public const string Vector3Class = "java.lang.Vector3";
    public const string YprClass = "java.lang.Ypr";

    public static Value Get(VmObject obj, string field)
    {
        var f = obj.Class.FindField(field) ?? throw new VmException($"{obj.Class.Name} has no field {field}");
        return f.IsStatic ? f.Owner.StaticValues[f.Slot] : obj.Fields[f.Slot];
    }

    public static void Set(VmObject obj, string field, Value value)
    {
        var f = obj.Class.FindField(field) ?? throw new VmException($"{obj.Class.Name} has no field {field}");
        if (f.IsStatic)
        {
            f.Owner.StaticValues[f.Slot] = value;
        }
        else
        {
            obj.Fields[f.Slot] = value;
        }
    }

    /// <summary>x, y, z of a java.lang.Vector3 (null → null).</summary>
    public static Vector3? ReadVector(VmObject? v) =>
        v is null ? null : new Vector3(Get(v, "x").AsFloat, Get(v, "y").AsFloat, Get(v, "z").AsFloat);

    /// <summary>y (yaw), p (pitch), r (roll) of a java.lang.Ypr (null → null).</summary>
    public static Vector3? ReadYpr(VmObject? v) =>
        v is null ? null : new Vector3(Get(v, "y").AsFloat, Get(v, "p").AsFloat, Get(v, "r").AsFloat);

    /// <summary>A new java.lang.Vector3 with the fields set directly (no constructor runs, like the engine's natives).</summary>
    public static VmObject NewVector(Slrr.Vm.Vm vm, Vector3 p)
    {
        var v = vm.NewObject(vm.LoadClass(Vector3Class));
        Set(v, "x", Value.Float(p.X));
        Set(v, "y", Value.Float(p.Y));
        Set(v, "z", Value.Float(p.Z));
        return v;
    }

    public static VmArray NewVectorArray(Slrr.Vm.Vm vm, params Vector3[] items)
    {
        var a = vm.NewArray("L" + Vector3Class + ";", items.Length);
        for (var i = 0; i < items.Length; i++)
        {
            a.Items[i] = Value.Obj(NewVector(vm, items[i]));
        }

        return a;
    }
}
