using Slrr.Vm;

namespace Slrr.Engine.Ui;

/// <summary>Small helpers to read and write script objects from C# (fields, statics, calls, Vectors, Vector3).</summary>
public static class Script
{
    public static Value Get(VmObject obj, string field)
    {
        var f = obj.Class.FindField(field) ?? throw new VmException($"{obj.Class.Name} has no field {field}");
        return f.IsStatic ? f.Owner.StaticValues[f.Slot] : obj.Fields[f.Slot];
    }

    public static bool TryGet(VmObject obj, string field, out Value value)
    {
        var f = obj.Class.FindField(field);
        if (f is null)
        {
            value = default;
            return false;
        }

        value = f.IsStatic ? f.Owner.StaticValues[f.Slot] : obj.Fields[f.Slot];
        return true;
    }

    public static void Set(VmObject obj, string field, Value value)
    {
        var f = obj.Class.FindField(field) ?? throw new VmException($"{obj.Class.Name} has no field {field}");
        var v = VmTypes.Coerce(value, f.Descriptor);
        if (f.IsStatic)
        {
            f.Owner.StaticValues[f.Slot] = v;
        }
        else
        {
            obj.Fields[f.Slot] = v;
        }
    }

    public static int GetInt(VmObject obj, string field) => Get(obj, field).AsInt;

    public static float GetFloat(VmObject obj, string field) => Get(obj, field).AsFloat;

    public static VmObject? GetObj(VmObject obj, string field) => Get(obj, field).AsObject;

    /// <summary>Static field of a class (loads and initializes the class).</summary>
    public static Value GetStatic(Vm.Vm vm, string className, string field)
    {
        var cls = vm.LoadClass(className);
        vm.EnsureInitialized(cls);
        var f = StaticField(cls, field);
        return f.Owner.StaticValues[f.Slot];
    }

    public static void SetStatic(Vm.Vm vm, string className, string field, Value value)
    {
        var cls = vm.LoadClass(className);
        vm.EnsureInitialized(cls);
        var f = StaticField(cls, field);
        f.Owner.StaticValues[f.Slot] = VmTypes.Coerce(value, f.Descriptor);
    }

    private static VmField StaticField(VmClass cls, string field) =>
        cls.FindField(field) is { IsStatic: true } f ? f : throw new VmException($"{cls.Name} has no static field {field}");

    /// <summary>Static field only if the class is already initialized (never triggers loading).</summary>
    public static Value? PeekStatic(Vm.Vm vm, string className, string field)
    {
        var cls = vm.LoadedClasses.FirstOrDefault(c => c.Name == className);
        if (cls is null || cls.InitState != ClassInitState.Initialized)
        {
            return null;
        }

        var f = cls.FindField(field);
        return f is { IsStatic: true } ? f.Owner.StaticValues[f.Slot] : null;
    }

    public static Value Call(Vm.Vm vm, VmObject self, string method, params Value[] args) => vm.InvokeVirtual(self, method, args);

    public static Value CallStatic(Vm.Vm vm, string className, string method, string? descriptor, params Value[] args)
    {
        var cls = vm.LoadClass(className);
        vm.EnsureInitialized(cls);
        return vm.Invoke(vm.FindStaticOrAny(cls, method, descriptor, descriptor is null ? args.Length : null), null, args);
    }

    public static string? Str(Vm.Vm vm, Value v) => v.Kind == VmKind.Object ? vm.GetString(v.Ref) : vm.ToScriptString(v);

    /// <summary>Elements of a java.util.Vector (through its script methods, so the representation stays the script's).</summary>
    public static List<Value> VectorItems(Vm.Vm vm, VmObject? vector)
    {
        var list = new List<Value>();
        if (vector is null)
        {
            return list;
        }

        var n = Call(vm, vector, "size").AsInt;
        for (var i = 0; i < n; i++)
        {
            list.Add(Call(vm, vector, "elementAt", Value.Int(i)));
        }

        return list;
    }

    /// <summary>A new java.lang.Vector3 with the given components (fields set directly, no constructor).</summary>
    public static VmObject NewVector3(Vm.Vm vm, float x, float y, float z)
    {
        var v = vm.NewObject(vm.LoadClass("java.lang.Vector3"));
        Set(v, "x", Value.Float(x));
        Set(v, "y", Value.Float(y));
        Set(v, "z", Value.Float(z));
        return v;
    }

    public static bool IsA(VmObject? o, string className) => o is not null && o.Class.IsSubclassOf(className);
}
