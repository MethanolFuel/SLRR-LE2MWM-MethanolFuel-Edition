using Slrr.Formats.Tufa;

namespace Slrr.Vm;

/// <summary>
/// MTHD access flags. NOT the Java numbering: the decompiler agent's comparison of 136 source/class pairs gives
/// 0x2 protected, 0x4 private, 0x10 abstract, 0x20 final, 0x80 synchronized; 0x40 is native (measured: natives have
/// no code and are registered in the exe).
/// </summary>
[Flags]
public enum VmAccess : uint
{
    None = 0,
    Public = 0x1,
    Protected = 0x2,
    Private = 0x4,
    Static = 0x8,
    Abstract = 0x10,
    Final = 0x20,
    Native = 0x40,
    Synchronized = 0x80,
}

public sealed class VmField
{
    public required VmClass Owner { get; init; }

    public required string Name { get; init; }

    public required string Descriptor { get; init; }

    public required VmAccess Flags { get; init; }

    public required bool IsStatic { get; init; }

    /// <summary>Static: index into <see cref="VmClass.StaticValues"/>. Instance: index into <see cref="VmObject.Fields"/>.</summary>
    public required int Slot { get; init; }

    /// <summary>Field-initializer body (FILD value column), or null. The body ends with ENDINIT.</summary>
    public VmCode? Initializer { get; init; }

    public override string ToString() => $"{Owner.Name}.{Name}:{Descriptor}";
}

public sealed class VmMethod
{
    public required VmClass Owner { get; init; }

    public required string Name { get; init; }

    public required string Descriptor { get; init; }

    public required VmAccess Flags { get; init; }

    public required bool IsStatic { get; init; }

    /// <summary>MTHD slots column: this + arguments + locals (measured to cover every LOCAL slot used by the body).</summary>
    public required uint Slots { get; init; }

    public VmCode? Code { get; init; }

    public required IReadOnlyList<string> Params { get; init; }

    /// <summary>Return descriptor; null for constructors (their descriptors have no return type).</summary>
    public string? Return { get; init; }

    public bool IsNative => (Flags & VmAccess.Native) != 0;

    public bool IsConstructor => Name == "<init>";

    public int ArgCount => Params.Count;

    /// <summary>Local array size: the larger of the MTHD slot count and the highest slot the body names.</summary>
    public int FrameSize => Math.Max((int)Math.Min(Slots, 4096), Math.Max((Code?.MaxSlot ?? -1) + 1, Params.Count + (IsStatic ? 0 : 1)));

    public string Signature => $"{Owner.Name}.{Name}{Descriptor}";

    public override string ToString() => Signature;
}

/// <summary>Runtime class built from one TUFA class (constant pool, fields, methods, initializer bodies, hierarchy).</summary>
public sealed class VmClass
{
    private readonly Dictionary<string, VmField> _declaredFieldsByName = new(StringComparer.Ordinal);
    private readonly Dictionary<(string, string), VmMethod> _declaredMethods = [];
    private readonly object?[] _resolved;
    private string[]? _instanceFieldDescs;

    internal VmClass(TufaClass raw, string? source, VmClass? super, IReadOnlyList<VmClass> interfaces)
    {
        Raw = raw;
        Name = raw.Name;
        Source = source;
        Super = super;
        Interfaces = interfaces;
        _resolved = new object?[raw.Constants.Count];
        Bodies = raw.Bodies.Select((b, i) => new VmCode(i, b.Instructions)).ToArray();

        var statics = new List<VmField>();
        var instance = new List<VmField>();
        var instanceBase = super?.InstanceFieldCount ?? 0;
        foreach (var f in raw.Fields)
        {
            var name = Utf8(f.NameIndex, "field name");
            var field = new VmField
            {
                Owner = this,
                Name = name,
                Descriptor = Utf8(f.DescriptorIndex, "field descriptor"),
                Flags = (VmAccess)f.Flags,
                IsStatic = f.IsStatic,
                Slot = f.IsStatic ? statics.Count : instanceBase + instance.Count,
                Initializer = f.Value == uint.MaxValue ? null : Body(f.Value, $"initializer of {name}"),
            };
            (f.IsStatic ? statics : instance).Add(field);
            _declaredFieldsByName.TryAdd(name, field);
        }

        StaticFields = statics;
        InstanceFields = instance;
        InstanceFieldCount = instanceBase + instance.Count;
        StaticValues = statics.Select(f => VmTypes.DefaultValue(f.Descriptor)).ToArray();

        var methods = new List<VmMethod>();
        foreach (var m in raw.Methods)
        {
            var name = Utf8(m.NameIndex, "method name");
            var desc = Utf8(m.DescriptorIndex, "method descriptor");
            var (ps, ret) = VmTypes.ParseMethod(desc);
            var method = new VmMethod
            {
                Owner = this,
                Name = name,
                Descriptor = desc,
                Flags = (VmAccess)m.Flags,
                IsStatic = m.IsStatic,
                Slots = m.Slots,
                Code = m.Value == uint.MaxValue ? null : Body(m.Value, $"method {name}{desc}"),
                Params = ps,
                Return = ret,
            };
            methods.Add(method);
            _declaredMethods.TryAdd((name, desc), method);
        }

        Methods = methods;
    }

    public string Name { get; }

    public TufaClass Raw { get; }

    /// <summary>File the class was loaded from (null for in-memory classes).</summary>
    public string? Source { get; }

    public VmClass? Super { get; }

    public IReadOnlyList<VmClass> Interfaces { get; }

    public IReadOnlyList<VmField> StaticFields { get; }

    /// <summary>Instance fields declared by this class (not inherited).</summary>
    public IReadOnlyList<VmField> InstanceFields { get; }

    /// <summary>Total instance slots, including those of all superclasses.</summary>
    public int InstanceFieldCount { get; }

    public Value[] StaticValues { get; }

    public IReadOnlyList<VmMethod> Methods { get; }

    public VmCode[] Bodies { get; }

    public ClassInitState InitState { get; internal set; }

    public override string ToString() => Name;

    public string InstanceFieldDescriptor(int slot)
    {
        _instanceFieldDescs ??= BuildInstanceDescs();
        return _instanceFieldDescs[slot];
    }

    /// <summary>This class, then its superclasses.</summary>
    public IEnumerable<VmClass> Hierarchy()
    {
        for (var c = this; c is not null; c = c.Super)
        {
            yield return c;
        }
    }

    public bool IsSubclassOf(VmClass other)
    {
        for (var c = this; c is not null; c = c.Super)
        {
            if (ReferenceEquals(c, other) || c.Interfaces.Any(i => i.IsSubclassOf(other)))
            {
                return true;
            }
        }

        return false;
    }

    public bool IsSubclassOf(string className)
    {
        for (var c = this; c is not null; c = c.Super)
        {
            if (c.Name == className || c.Interfaces.Any(i => i.IsSubclassOf(className)))
            {
                return true;
            }
        }

        return false;
    }

    public VmField? DeclaredField(string name) => _declaredFieldsByName.GetValueOrDefault(name);

    /// <summary>Field lookup by name, this class first, then superclasses (a subclass field hides an inherited one).</summary>
    public VmField? FindField(string name)
    {
        for (var c = this; c is not null; c = c.Super)
        {
            if (c._declaredFieldsByName.TryGetValue(name, out var f))
            {
                return f;
            }
        }

        foreach (var i in AllInterfaces())
        {
            if (i._declaredFieldsByName.TryGetValue(name, out var f) && f.IsStatic)
            {
                return f;
            }
        }

        return null;
    }

    public VmMethod? DeclaredMethod(string name, string descriptor) => _declaredMethods.GetValueOrDefault((name, descriptor));

    /// <summary>Exact lookup by name and descriptor, walking up the superclasses (virtual dispatch target).</summary>
    public VmMethod? FindMethod(string name, string descriptor)
    {
        for (var c = this; c is not null; c = c.Super)
        {
            if (c._declaredMethods.TryGetValue((name, descriptor), out var m))
            {
                return m;
            }
        }

        foreach (var i in AllInterfaces())
        {
            if (i._declaredMethods.TryGetValue((name, descriptor), out var m))
            {
                return m;
            }
        }

        return null;
    }

    /// <summary>All methods named <paramref name="name"/> with <paramref name="argc"/> parameters, nearest class first.</summary>
    public IEnumerable<VmMethod> FindMethods(string name, int argc, bool declaredOnly = false)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var c = this; c is not null; c = declaredOnly ? null : c.Super)
        {
            foreach (var m in c.Methods)
            {
                if (m.Name == name && m.ArgCount == argc && seen.Add(m.Descriptor))
                {
                    yield return m;
                }
            }
        }

        if (!declaredOnly)
        {
            foreach (var i in AllInterfaces())
            {
                foreach (var m in i.Methods)
                {
                    if (m.Name == name && m.ArgCount == argc && seen.Add(m.Descriptor))
                    {
                        yield return m;
                    }
                }
            }
        }
    }

    internal object? GetResolved(uint index) => index < _resolved.Length ? _resolved[index] : null;

    internal void SetResolved(uint index, object value)
    {
        if (index < _resolved.Length)
        {
            _resolved[index] = value;
        }
    }

    internal TufaConstant Constant(uint index, TufaConstantType expected)
    {
        if (index >= Raw.Constants.Count || Raw.Constants[(int)index].Type != expected)
        {
            throw new VmException($"{Name}: constant #{index} is not a {expected}");
        }

        return Raw.Constants[(int)index];
    }

    internal string Utf8(uint index, string what) =>
        Raw.Utf8(index) ?? throw new VmException($"{Name}: constant #{index} ({what}) is not a string");

    private IEnumerable<VmClass> AllInterfaces()
    {
        var seen = new HashSet<VmClass>();
        var queue = new Queue<VmClass>();
        for (var c = this; c is not null; c = c.Super)
        {
            foreach (var i in c.Interfaces)
            {
                queue.Enqueue(i);
            }
        }

        while (queue.Count > 0)
        {
            var i = queue.Dequeue();
            if (!seen.Add(i))
            {
                continue;
            }

            yield return i;
            foreach (var j in i.Hierarchy().SelectMany(x => x.Interfaces))
            {
                queue.Enqueue(j);
            }
        }
    }

    private VmCode Body(uint index, string what) =>
        index < Bodies.Length ? Bodies[index] : throw new VmException($"{Name}: {what} names body {index}, but only {Bodies.Length} exist");

    private string[] BuildInstanceDescs()
    {
        var a = new string[InstanceFieldCount];
        foreach (var c in Hierarchy())
        {
            foreach (var f in c.InstanceFields)
            {
                a[f.Slot] = f.Descriptor;
            }
        }

        return a;
    }
}

public enum ClassInitState
{
    Uninitialized,
    Initializing,
    Initialized,
    Failed,
}
