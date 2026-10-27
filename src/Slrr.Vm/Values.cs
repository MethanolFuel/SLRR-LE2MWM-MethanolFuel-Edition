using System.Globalization;

namespace Slrr.Vm;

/// <summary>
/// The four primitive VM types of the original engine (docs/research/script-vm-opcodes.md §1): two int types
/// (booleans and chars are ints), float, object (also <c>null</c>) and void. There is no long, double or byte.
/// </summary>
public enum VmKind : byte
{
    Void,
    Int,
    Float,
    Object,
}

/// <summary>An rvalue on the operand stack, in a local, a field or an array element.</summary>
public readonly struct Value : IEquatable<Value>
{
    private readonly int _i;
    private readonly float _f;

    private Value(VmKind kind, int i, float f, VmObject? r)
    {
        Kind = kind;
        _i = i;
        _f = f;
        Ref = r;
    }

    public VmKind Kind { get; }

    public VmObject? Ref { get; }

    public static Value Void => default;

    public static Value Null => new(VmKind.Object, 0, 0, null);

    public static Value Zero => new(VmKind.Int, 0, 0, null);

    public bool IsNull => Kind == VmKind.Object && Ref is null;

    public static Value Int(int v) => new(VmKind.Int, v, 0, null);

    public static Value Bool(bool v) => new(VmKind.Int, v ? 1 : 0, 0, null);

    public static Value Float(float v) => new(VmKind.Float, 0, v, null);

    public static Value Obj(VmObject? o) => new(VmKind.Object, 0, 0, o);

    /// <summary>Int view: floats truncate toward zero (C cast, as the engine's <c>ftol</c>).</summary>
    public int AsInt => Kind switch
    {
        VmKind.Int => _i,
        VmKind.Float => FloatToInt(_f),
        _ => throw new VmException($"int expected, found {Describe()}"),
    };

    public float AsFloat => Kind switch
    {
        VmKind.Float => _f,
        VmKind.Int => _i,
        _ => throw new VmException($"float expected, found {Describe()}"),
    };

    public VmObject? AsObject => Kind == VmKind.Object ? Ref : throw new VmException($"object expected, found {Describe()}");

    /// <summary>Truth value used by conditional jumps: non-zero numbers, non-null objects.</summary>
    public bool IsTrue => Kind switch
    {
        VmKind.Int => _i != 0,
        VmKind.Float => _f != 0,
        VmKind.Object => Ref is not null,
        _ => throw new VmException("void value used as a condition"),
    };

    public static int FloatToInt(float f)
    {
        if (float.IsNaN(f))
        {
            return int.MinValue;
        }

        return f >= 2147483648f || f < -2147483648f ? int.MinValue : (int)f;
    }

    public string Describe() => Kind switch
    {
        VmKind.Void => "void",
        VmKind.Int => _i.ToString(CultureInfo.InvariantCulture),
        VmKind.Float => _f.ToString("R", CultureInfo.InvariantCulture) + "f",
        _ => Ref is null ? "null" : Ref.Describe(),
    };

    public override string ToString() => Describe();

    public bool Equals(Value other) => Kind == other.Kind && _i == other._i && _f.Equals(other._f) && ReferenceEquals(Ref, other.Ref);

    public override bool Equals(object? obj) => obj is Value v && Equals(v);

    public override int GetHashCode() => HashCode.Combine(Kind, _i, _f, Ref);

    public static bool operator ==(Value a, Value b) => a.Equals(b);

    public static bool operator !=(Value a, Value b) => !a.Equals(b);
}

/// <summary>Script object. <see cref="Native"/> holds the engine-side state of native-backed classes (e.g. the text of a String).</summary>
public class VmObject
{
    private static int s_nextId;

    public VmObject(VmClass cls)
    {
        Class = cls;
        Fields = new Value[cls.InstanceFieldCount];
        for (var i = 0; i < Fields.Length; i++)
        {
            Fields[i] = VmTypes.DefaultValue(cls.InstanceFieldDescriptor(i));
        }

        Id = Interlocked.Increment(ref s_nextId);
    }

    public VmClass Class { get; }

    public Value[] Fields { get; }

    /// <summary>Engine-side payload for native classes (string text, thread state, ...). Natives own its meaning.</summary>
    public object? Native { get; set; }

    public int Id { get; }

    public virtual string Describe() => Native is string s ? "\"" + s + "\"" : $"{Class.Name}@{Id:x}";

    public override string ToString() => Describe();
}

/// <summary>One-dimensional array (the engine rejects multi-dimensional arrays).</summary>
public sealed class VmArray : VmObject
{
    public VmArray(VmClass objectClass, string elementDescriptor, int length)
        : base(objectClass)
    {
        if (length < 0)
        {
            throw new VmException($"negative array size {length}");
        }

        ElementDescriptor = elementDescriptor;
        Items = new Value[length];
        var d = VmTypes.DefaultValue(elementDescriptor);
        Array.Fill(Items, d);
    }

    public string ElementDescriptor { get; }

    public Value[] Items { get; }

    public override string Describe() => $"{VmTypes.Pretty(ElementDescriptor)}[{Items.Length}]@{Id:x}";
}

/// <summary>Descriptor helpers. Descriptors use dots: <c>I</c>, <c>F</c>, <c>Ljava.lang.String;</c>, <c>[I</c>, <c>V</c>.</summary>
public static class VmTypes
{
    public const string StringClass = "java.lang.String";
    public const string ObjectClass = "java.lang.Object";

    public static Value DefaultValue(string? descriptor) => descriptor switch
    {
        null or "" => Value.Zero,
        "F" => Value.Float(0),
        "V" => Value.Void,
        _ when descriptor[0] is 'L' or '[' => Value.Null,
        _ => Value.Zero,
    };

    public static VmKind KindOf(string descriptor) => descriptor switch
    {
        "F" => VmKind.Float,
        "V" => VmKind.Void,
        _ when descriptor.Length > 0 && descriptor[0] is 'L' or '[' => VmKind.Object,
        _ => VmKind.Int,
    };

    /// <summary>Class name of an <c>L...;</c> descriptor, or null for primitives/arrays.</summary>
    public static string? ClassOf(string descriptor) =>
        descriptor.Length > 2 && descriptor[0] == 'L' && descriptor[^1] == ';' ? descriptor[1..^1] : null;

    /// <summary>Convert a value for storage into a variable of the given declared type (int↔float, as the engine does).</summary>
    public static Value Coerce(Value v, string? descriptor)
    {
        if (descriptor is null)
        {
            return v;
        }

        return KindOf(descriptor) switch
        {
            VmKind.Int when v.Kind == VmKind.Float => Value.Int(v.AsInt),
            VmKind.Float when v.Kind == VmKind.Int => Value.Float(v.AsFloat),
            VmKind.Object when v.Kind != VmKind.Object => throw new VmException($"cannot assign {v.Describe()} to {Pretty(descriptor)}"),
            VmKind.Int or VmKind.Float when v.Kind is VmKind.Object or VmKind.Void => throw new VmException($"cannot assign {v.Describe()} to {Pretty(descriptor)}"),
            _ => v,
        };
    }

    /// <summary>Parameter descriptors and the return descriptor (null for constructors, which have none).</summary>
    public static (List<string> Params, string? Return) ParseMethod(string descriptor)
    {
        if (descriptor.Length == 0 || descriptor[0] != '(')
        {
            throw new VmException($"bad method descriptor '{descriptor}'");
        }

        var ps = new List<string>();
        var i = 1;
        while (i < descriptor.Length && descriptor[i] != ')')
        {
            var start = i;
            while (descriptor[i] == '[')
            {
                i++;
            }

            if (descriptor[i] == 'L')
            {
                var end = descriptor.IndexOf(';', i);
                if (end < 0)
                {
                    throw new VmException($"bad method descriptor '{descriptor}'");
                }

                i = end + 1;
            }
            else
            {
                i++;
            }

            ps.Add(descriptor[start..i]);
        }

        if (i >= descriptor.Length)
        {
            throw new VmException($"bad method descriptor '{descriptor}'");
        }

        var ret = i + 1 < descriptor.Length ? descriptor[(i + 1)..] : null;
        return (ps, ret);
    }

    public static string Pretty(string descriptor) => descriptor switch
    {
        "I" => "int",
        "F" => "float",
        "V" => "void",
        _ when descriptor.StartsWith('[') => Pretty(descriptor[1..]) + "[]",
        _ => ClassOf(descriptor) ?? descriptor,
    };
}
