namespace Slrr.Vm;

/// <summary>
/// A native method implementation. Return <see cref="Value.Void"/> for <c>V</c> methods and constructors. A native may
/// block its thread (<see cref="VmThread.SleepFor"/>, <see cref="VmThread.WaitOn"/>, <see cref="VmThread.Suspend"/>);
/// the interpreter then yields after pushing the return value.
/// </summary>
public delegate Value NativeMethod(NativeCall call);

/// <summary>Identity of a native: declaring class, method name and dotted descriptor, exactly as in the MTHD row
/// (and as the engine registers it, see docs/research/natives-redline.tsv).</summary>
public readonly record struct NativeKey(string ClassName, string Name, string Descriptor)
{
    public override string ToString() => $"{ClassName}.{Name}{Descriptor}";
}

/// <summary>Arguments and context of one native invocation.</summary>
public sealed class NativeCall
{
    internal NativeCall(Vm vm, VmThread thread, VmMethod method, VmObject? self, Value[] args)
    {
        Vm = vm;
        Thread = thread;
        Method = method;
        This = self;
        Args = args;
    }

    public Vm Vm { get; }

    public VmThread Thread { get; }

    public VmMethod Method { get; }

    /// <summary>The receiver; null for static natives.</summary>
    public VmObject? This { get; }

    public Value[] Args { get; }

    public VmObject Self => This ?? throw new VmException($"{Method.Signature}: native called without an instance");

    public int Int(int i) => Arg(i).AsInt;

    public float Float(int i) => Arg(i).AsFloat;

    public VmObject? Object(int i) => Arg(i).AsObject;

    /// <summary>Text of a String argument (null for a null reference).</summary>
    public string? String(int i) => Vm.GetString(Arg(i).AsObject);

    public Value Arg(int i) =>
        i < Args.Length ? Args[i] : throw new VmException($"{Method.Signature}: not enough parameters passed to native function");

    public Value Str(string? s) => s is null ? Value.Null : Value.Obj(Vm.NewString(s));
}

/// <summary>A group of natives (e.g. java.lang, java.render) that registers itself; one per source file/area.</summary>
public interface INativeModule
{
    void Register(NativeRegistry registry);
}

/// <summary>
/// Native method table keyed by (class, name, descriptor), like the engine's <c>JVM::registerNative</c>. Other
/// subsystems add their natives in their own <see cref="INativeModule"/>; calling a native that nobody registered
/// raises <see cref="VmNativeMissingException"/>.
/// </summary>
public sealed class NativeRegistry
{
    private readonly Dictionary<NativeKey, NativeMethod> _table = [];

    public int Count => _table.Count;

    public IEnumerable<NativeKey> Keys => _table.Keys;

    /// <summary>A registry with the engine-independent java.lang natives (<see cref="LangNatives"/>).</summary>
    public static NativeRegistry CreateDefault()
    {
        var r = new NativeRegistry();
        r.Add(new LangNatives());
        return r;
    }

    public NativeRegistry Add(INativeModule module)
    {
        module.Register(this);
        return this;
    }

    /// <summary>Registers a native; fails if the key is already taken unless <paramref name="replace"/> is set.</summary>
    public void Register(string className, string name, string descriptor, NativeMethod fn, bool replace = false)
    {
        var key = new NativeKey(className, name, descriptor);
        if (!replace && _table.ContainsKey(key))
        {
            throw new InvalidOperationException($"native {key} registered twice");
        }

        _table[key] = fn;
    }

    public bool Contains(string className, string name, string descriptor) => _table.ContainsKey(new(className, name, descriptor));

    /// <summary>
    /// Optional last resort for natives nobody registered (e.g. permissive stubs during engine bring-up or corpus
    /// surveys). Return null to keep the <see cref="VmNativeMissingException"/>.
    /// </summary>
    public Func<NativeKey, NativeMethod?>? Fallback { get; set; }

    public NativeMethod? Find(string className, string name, string descriptor)
    {
        var key = new NativeKey(className, name, descriptor);
        return _table.GetValueOrDefault(key) ?? Fallback?.Invoke(key);
    }

    /// <summary>A fallback that makes every missing native a no-op returning 0 / 0.0 / null.</summary>
    public static NativeMethod? DefaultStub(NativeKey key)
    {
        var ret = VmTypes.ParseMethod(key.Descriptor).Return;
        var v = ret is null or "V" ? Value.Void : VmTypes.DefaultValue(ret);
        return _ => v;
    }
}
