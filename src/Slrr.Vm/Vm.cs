using System.Globalization;
using Slrr.Formats.Tufa;

namespace Slrr.Vm;

/// <summary>
/// The script virtual machine: class loading and linking, static/instance field initialization, object and string
/// creation, synchronous invocation and a cooperative thread scheduler driven by a virtual clock.
/// </summary>
public sealed class Vm
{
    private readonly Dictionary<string, VmClass> _classes = new(StringComparer.Ordinal);
    private readonly HashSet<string> _linking = new(StringComparer.Ordinal);
    private readonly List<VmThread> _threads = [];
    private VmClass? _stringClass;
    private VmClass? _objectClass;

    public Vm(VmClassPath classPath, NativeRegistry? natives = null)
    {
        ClassPath = classPath;
        Natives = natives ?? NativeRegistry.CreateDefault();
    }

    public VmClassPath ClassPath { get; }

    public NativeRegistry Natives { get; }

    /// <summary>Where <c>System.log</c> and <c>System.analyze</c> write.</summary>
    public TextWriter Out { get; set; } = TextWriter.Null;

    /// <summary>Lines written by <c>System.log</c>, in order.</summary>
    public List<string> Log { get; } = [];

    /// <summary>
    /// Maps a resource literal <c>archive:0xID</c> (opcode 0E) to the int the engine pushes. The engine resolves it to
    /// a runtime resource id (0x406660); the mapping is not reconstructed yet, so the default returns the local id.
    /// </summary>
    public Func<string, uint, int> ResolveResource { get; set; } = (_, id) => unchecked((int)id);

    /// <summary>Virtual clock in seconds (<c>System.currentTime</c>), advanced by the scheduler.</summary>
    public float Time { get; set; }

    /// <summary>Simulation clock in seconds (<c>System.simTime</c>); follows <see cref="Time"/> until an engine drives it.</summary>
    public float SimTime { get; set; }

    public Random Random { get; set; } = new(12345);

    /// <summary>Instructions executed since creation.</summary>
    public long Executed { get; internal set; }

    /// <summary>Hard limit on <see cref="Executed"/> (endless-loop guard); <see cref="VmBudgetExceededException"/> beyond it.</summary>
    public long InstructionBudget { get; set; } = 200_000_000;

    /// <summary>Instructions a scheduled thread may run before the scheduler switches to the next one.</summary>
    public int TimeSlice { get; set; } = 20_000;

    /// <summary>Trace every executed instruction to this writer (null = off).</summary>
    public TextWriter? Trace { get; set; }

    public IReadOnlyList<VmThread> Threads => _threads;

    /// <summary>The thread executing script code right now (null between runs).</summary>
    public VmThread? CurrentThread { get; internal set; }

    /// <summary>Errors that terminated scheduled threads (the engine logs them and stops the thread).</summary>
    public List<(VmThread Thread, VmException Error)> ThreadErrors { get; } = [];

    public IReadOnlyCollection<VmClass> LoadedClasses => _classes.Values;

    public VmClass ObjectClass => _objectClass ??= LoadClass(VmTypes.ObjectClass);

    public VmClass StringClass => _stringClass ??= LoadClass(VmTypes.StringClass);

    // ---------------------------------------------------------------- classes

    public VmClass? TryLoadClass(string name)
    {
        try
        {
            return LoadClass(name);
        }
        catch (VmClassNotFoundException)
        {
            return null;
        }
    }

    /// <summary>Loads and links a class and its superclasses/interfaces. Does not run static initializers.</summary>
    public VmClass LoadClass(string name)
    {
        if (_classes.TryGetValue(name, out var c))
        {
            return c;
        }

        if (!_linking.Add(name))
        {
            throw new VmException($"circular class hierarchy at {name}");
        }

        try
        {
            var src = ClassPath.Find(name);
            var raw = src?.Class ?? Synthetic(name) ?? throw new VmClassNotFoundException(name);
            var superName = raw.ClassName(raw.SuperClass);
            var super = superName is null || superName == name ? null : LoadClass(superName);
            var interfaces = raw.Interfaces.Select(i => raw.ClassName(i)).Where(n => n is not null).Select(n => LoadClass(n!)).ToList();
            c = new VmClass(raw, src?.File, super, interfaces);
            _classes[name] = c;
            return c;
        }
        finally
        {
            _linking.Remove(name);
        }
    }

    /// <summary>Runs the static field initializers (superclass first) once. Re-entrant calls see default values.</summary>
    public void EnsureInitialized(VmClass cls)
    {
        if (cls.InitState != ClassInitState.Uninitialized)
        {
            if (cls.InitState == ClassInitState.Failed)
            {
                throw new VmException($"class {cls.Name} failed to initialize earlier");
            }

            return;
        }

        cls.InitState = ClassInitState.Initializing;
        try
        {
            if (cls.Super is not null)
            {
                EnsureInitialized(cls.Super);
            }

            foreach (var f in cls.StaticFields)
            {
                if (f.Initializer is not null)
                {
                    var v = RunInitializer(cls, f, null);
                    cls.StaticValues[f.Slot] = VmTypes.Coerce(v, f.Descriptor);
                }
            }

            cls.InitState = ClassInitState.Initialized;
        }
        catch
        {
            cls.InitState = ClassInitState.Failed;
            throw;
        }
    }

    // ---------------------------------------------------------------- objects

    /// <summary>Allocates an instance and runs the instance field initializers (base class first). No constructor.</summary>
    public VmObject NewObject(VmClass cls)
    {
        EnsureInitialized(cls);
        var obj = new VmObject(cls);
        if (cls.IsSubclassOf(VmTypes.StringClass))
        {
            obj.Native = string.Empty;
        }

        foreach (var c in cls.Hierarchy().Reverse())
        {
            foreach (var f in c.InstanceFields)
            {
                if (f.Initializer is not null)
                {
                    obj.Fields[f.Slot] = VmTypes.Coerce(RunInitializer(c, f, obj), f.Descriptor);
                }
            }
        }

        return obj;
    }

    /// <summary><c>new C(args)</c>: allocate, initialize fields, run the matching constructor.</summary>
    public VmObject Construct(VmClass cls, params Value[] args)
    {
        var obj = NewObject(cls);
        var ctor = FindConstructor(cls, args);
        if (ctor is not null)
        {
            InvokeVoid(ctor, obj, args);
        }

        return obj;
    }

    public VmObject NewString(string s) => new(StringClass) { Native = s };

    private readonly Dictionary<string, VmObject> _literals = new(StringComparer.Ordinal);

    /// <summary>
    /// String literals are interned, so equal literals compare equal with <c>==</c> (a script compares file strings
    /// against literals and against other interned names; this matches how Java and, most likely, the engine behave).
    /// </summary>
    public VmObject InternLiteral(string s)
    {
        lock (_literals)
        {
            if (!_literals.TryGetValue(s, out var o))
            {
                o = NewString(s);
                _literals[s] = o;
            }

            return o;
        }
    }

    /// <summary>Text of a String object (null for null). Throws for objects that are not strings.</summary>
    public string? GetString(VmObject? o) => o is null ? null : o.Native as string ?? throw new VmException($"{o.Describe()} is not a String");

    public VmArray NewArray(string elementDescriptor, int length)
    {
        if (elementDescriptor.StartsWith('['))
        {
            throw new VmException("multi-dimensional arrays not supported!");
        }

        return new VmArray(ObjectClass, elementDescriptor, length);
    }

    /// <summary>Engine string conversion (JVM::stringConversion): ints with <c>%d</c>, floats with <c>%.3f</c> (measured).</summary>
    public string ToScriptString(Value v) => v.Kind switch
    {
        VmKind.Int => v.AsInt.ToString(CultureInfo.InvariantCulture),
        VmKind.Float => ((double)v.AsFloat).ToString("F3", CultureInfo.InvariantCulture),
        VmKind.Object when v.Ref is null => string.Empty,
        VmKind.Object when v.Ref.Native is string s => s,
        _ => throw new VmException($"JVM::stringConversion: typecast failed ({v.Describe()})"),
    };

    // ---------------------------------------------------------------- invocation

    /// <summary>Calls a method synchronously on the current thread (or a temporary one). The callee must not block.</summary>
    public Value Invoke(VmMethod method, VmObject? self, params Value[] args)
    {
        var thread = CurrentThread ?? new VmThread(this, "invoke");
        return thread.InvokeNested(method, self, args, pushResult: true);
    }

    public void InvokeVoid(VmMethod method, VmObject? self, params Value[] args)
    {
        var thread = CurrentThread ?? new VmThread(this, "invoke");
        thread.InvokeNested(method, self, args, pushResult: !method.IsConstructor);
    }

    /// <summary>Virtual call by name and argument count (overloads are chosen by the runtime argument types).</summary>
    public Value InvokeVirtual(VmObject self, string name, params Value[] args)
    {
        var m = SelectOverload(self.Class.FindMethods(name, args.Length), args)
            ?? throw new VmException($"unknown method {self.Class.Name}.{name} with {args.Length} arguments");
        return Invoke(m, self, args);
    }

    /// <summary>Static method by name (and optional descriptor).</summary>
    public VmMethod FindStaticOrAny(VmClass cls, string name, string? descriptor, int? argc)
    {
        var candidates = cls.Methods.Where(m => m.Name == name && (descriptor is null || m.Descriptor == descriptor
                                                     || (descriptor.EndsWith(')') && m.Descriptor.StartsWith(descriptor, StringComparison.Ordinal)))
                                                 && (argc is null || m.ArgCount == argc)).ToList();
        return candidates.Count switch
        {
            1 => candidates[0],
            0 => throw new VmException($"no method {cls.Name}.{name}{descriptor} ({argc?.ToString(CultureInfo.InvariantCulture) ?? "any"} args)"),
            _ => throw new VmException($"{cls.Name}.{name} is overloaded: " + string.Join(", ", candidates.Select(m => m.Descriptor))),
        };
    }

    /// <summary>Starts a scheduled thread that runs <paramref name="method"/>; it runs when the scheduler runs.</summary>
    public VmThread Spawn(VmMethod method, VmObject? self, Value[] args, string name, VmObject? threadObject = null)
    {
        var t = new VmThread(this, name) { ThreadObject = threadObject };
        t.Start(method, self, args);
        _threads.Add(t);
        return t;
    }

    /// <summary>
    /// Runs scheduled threads round-robin until none is runnable. When all remaining threads sleep, the virtual clock
    /// jumps to the earliest wake-up (never past <paramref name="maxTime"/>). Returns the number of live threads left.
    /// </summary>
    public int RunUntilIdle(float maxTime = float.PositiveInfinity)
    {
        while (true)
        {
            WakeSleepers();
            var ready = _threads.Where(t => t.State == VmThreadState.Ready).ToList();
            if (ready.Count == 0)
            {
                var sleepers = _threads.Where(t => t.State == VmThreadState.Sleeping).ToList();
                if (sleepers.Count == 0)
                {
                    break;
                }

                var wake = sleepers.Min(t => t.WakeTime);
                if (wake > maxTime)
                {
                    AdvanceClock(maxTime - Time);
                    break;
                }

                AdvanceClock(Math.Max(0, wake - Time));
                continue;
            }

            foreach (var t in ready)
            {
                RunSlice(t);
            }

            _threads.RemoveAll(t => t.State == VmThreadState.Finished);
        }

        _threads.RemoveAll(t => t.State == VmThreadState.Finished);
        return _threads.Count;
    }

    /// <summary>One engine frame: advance the clocks by <paramref name="dt"/>, wake sleepers, give each runnable thread one slice.</summary>
    public void RunFrame(float dt)
    {
        AdvanceClock(dt);
        WakeSleepers();
        foreach (var t in _threads.Where(t => t.State == VmThreadState.Ready).ToList())
        {
            RunSlice(t);
        }

        _threads.RemoveAll(t => t.State == VmThreadState.Finished);
    }

    public void Notify(VmObject monitor, bool all)
    {
        foreach (var t in _threads)
        {
            if (t.State == VmThreadState.Waiting && ReferenceEquals(t.WaitingOn, monitor))
            {
                t.WaitingOn = null;
                t.State = VmThreadState.Ready;
                if (!all)
                {
                    return;
                }
            }
        }
    }

    // ---------------------------------------------------------------- overloads (shared with the interpreter)

    internal VmMethod? FindConstructor(VmClass cls, Value[] args)
    {
        for (var c = cls; c is not null; c = c.Super)
        {
            var m = SelectOverload(c.FindMethods("<init>", args.Length, declaredOnly: true), args);
            // Unlike Java, a constructor is looked up through the superclasses when the class itself has none with
            // this argument count: FileRequesterDialog calls super(...) with 5 arguments, which only TextDialog (two
            // levels up) declares; its direct superclass OKCancelDialog has 4- and 6-argument constructors.
            if (m is not null)
            {
                return m;
            }
        }

        return null;
    }

    /// <summary>Best candidate for runtime arguments: exact kinds/classes first, then int↔float, then anything.</summary>
    internal static VmMethod? SelectOverload(IEnumerable<VmMethod> candidates, Value[] args)
    {
        VmMethod? best = null;
        var bestScore = int.MinValue;
        foreach (var m in candidates)
        {
            var score = 0;
            for (var i = 0; i < args.Length && i < m.Params.Count; i++)
            {
                score += Match(m.Params[i], args[i]);
            }

            // A parameter that cannot take the argument at all (object vs number) disqualifies the candidate, so a
            // constructor lookup can continue in the superclass: `new RenderRef(resRef)` matches no RenderRef
            // constructor (its only 1-argument one takes an int) but ResourceRef(ResourceRef).
            if (score > bestScore && score > -100)
            {
                best = m;
                bestScore = score;
            }
        }

        return best;
    }

    private static int Match(string desc, Value v)
    {
        var k = VmTypes.KindOf(desc);
        if (k == v.Kind)
        {
            if (k != VmKind.Object || v.Ref is null)
            {
                return 3;
            }

            var cls = VmTypes.ClassOf(desc);
            if (cls is null)
            {
                return v.Ref is VmArray ? 3 : -10;
            }

            return v.Ref.Class.IsSubclassOf(cls) ? 3 : -10;
        }

        return k is VmKind.Int or VmKind.Float && v.Kind is VmKind.Int or VmKind.Float ? 1 : -100;
    }

    // ---------------------------------------------------------------- internals

    private Value RunInitializer(VmClass cls, VmField f, VmObject? self)
    {
        var thread = CurrentThread ?? new VmThread(this, f.IsStatic ? "THRD-CLASSVAR-INI" : "THRD-INSTVAR-INI");
        return thread.RunInitializer(cls, f, self);
    }

    private void RunSlice(VmThread t)
    {
        try
        {
            t.Resume(TimeSlice);
        }
        catch (VmExitException)
        {
            throw;
        }
        catch (VmBudgetExceededException)
        {
            throw;
        }
        catch (VmException ex)
        {
            ThreadErrors.Add((t, ex));
        }
    }

    private void AdvanceClock(float dt)
    {
        Time += dt;
        SimTime += dt;
    }

    private void WakeSleepers()
    {
        foreach (var t in _threads)
        {
            if (t.State == VmThreadState.Sleeping && t.WakeTime <= Time)
            {
                t.State = VmThreadState.Ready;
            }
        }
    }

    /// <summary>Minimal stand-ins for java.lang.Object / java.lang.String when no game data is available (unit tests).</summary>
    private static TufaClass? Synthetic(string name)
    {
        string? super = name switch
        {
            VmTypes.ObjectClass => null,
            VmTypes.StringClass => VmTypes.ObjectClass,
            _ => "-",
        };
        if (super == "-")
        {
            return null;
        }

        var t = new TufaClass { Version = 4 };
        t.Constants.Add(new TufaConstant(TufaConstantType.Utf8, name, 0, 0));
        t.Constants.Add(new TufaConstant(TufaConstantType.Class, null, 0, 0));
        t.ThisClass = 1;
        t.SuperClass = uint.MaxValue;
        if (super is not null)
        {
            t.Constants.Add(new TufaConstant(TufaConstantType.Utf8, super, 0, 0));
            t.Constants.Add(new TufaConstant(TufaConstantType.Class, null, 2, 0));
            t.SuperClass = 3;
        }

        return t;
    }
}
