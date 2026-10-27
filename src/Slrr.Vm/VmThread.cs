using System.Globalization;
using Slrr.Formats.Tufa;

namespace Slrr.Vm;

public enum VmThreadState
{
    Ready,
    Sleeping,
    Waiting,
    Suspended,
    Finished,
}

/// <summary>
/// A script thread: a frame stack plus one operand stack, executed by the interpreter in <see cref="Execute"/>.
/// Script-to-script calls push frames (no host recursion); natives, field initializers and <see cref="Vm.Invoke"/>
/// run nested and must not block.
/// </summary>
public sealed partial class VmThread
{
    private readonly List<VmFrame> _frames = [];
    private Slot[] _stack = new Slot[64];
    private int _sp;
    private int _nested;
    private int _slice;

    public VmThread(Vm vm, string name)
    {
        Vm = vm;
        Name = name;
    }

    public Vm Vm { get; }

    public string Name { get; set; }

    /// <summary>The java.lang.Thread instance this thread runs for (null for engine threads).</summary>
    public VmObject? ThreadObject { get; set; }

    public VmThreadState State { get; internal set; } = VmThreadState.Ready;

    public float WakeTime { get; private set; }

    public VmObject? WaitingOn { get; internal set; }

    public int Priority { get; set; }

    public bool Daemon { get; set; }

    /// <summary>Return value of the thread's entry method once finished.</summary>
    public Value Result { get; private set; }

    public VmException? Error { get; private set; }

    public IReadOnlyList<VmFrame> Frames => _frames;

    public int StackDepth => _sp;

    public bool IsAlive => State != VmThreadState.Finished;

    // ---------------------------------------------------------------- blocking (called by natives)

    public void SleepFor(float seconds)
    {
        WakeTime = Vm.Time + Math.Max(0, seconds);
        State = VmThreadState.Sleeping;
    }

    public void WaitOn(VmObject monitor)
    {
        WaitingOn = monitor;
        State = VmThreadState.Waiting;
    }

    public void Suspend()
    {
        if (State != VmThreadState.Finished)
        {
            State = VmThreadState.Suspended;
        }
    }

    public void Wake()
    {
        if (State is VmThreadState.Suspended or VmThreadState.Sleeping or VmThreadState.Waiting)
        {
            WaitingOn = null;
            State = VmThreadState.Ready;
        }
    }

    public void Stop()
    {
        State = VmThreadState.Finished;
        if (_nested == 0 && !ReferenceEquals(Vm.CurrentThread, this))
        {
            _frames.Clear();
            _sp = 0;
        }
    }

    // ---------------------------------------------------------------- entry points

    internal void Start(VmMethod method, VmObject? self, Value[] args)
    {
        Call(method, self, args, pushResult: true);
        if (_frames.Count == 0)
        {
            Result = _sp > 0 ? PopValue() : Value.Void;
            State = VmThreadState.Finished;
        }
    }

    internal void Resume(int slice)
    {
        if (State != VmThreadState.Ready)
        {
            return;
        }

        var prev = Vm.CurrentThread;
        Vm.CurrentThread = this;
        _slice = slice;
        try
        {
            Execute(0);
        }
        catch (VmException ex)
        {
            Unwind(ex, 0, 0);
            Error = ex;
            State = VmThreadState.Finished;
            throw;
        }
        finally
        {
            Vm.CurrentThread = prev;
        }

        if (State == VmThreadState.Finished)
        {
            _frames.Clear();
            _sp = 0;
        }
        else if (_frames.Count == 0)
        {
            Result = _sp > 0 ? PopValue() : Value.Void;
            _sp = 0;
            State = VmThreadState.Finished;
        }
    }

    internal Value InvokeNested(VmMethod method, VmObject? self, Value[] args, bool pushResult) =>
        RunNested(() => Call(method, self, args, pushResult), pushResult, method.Signature);

    internal Value RunInitializer(VmClass cls, VmField field, VmObject? self)
    {
        var code = field.Initializer!;
        return RunNested(
            () => _frames.Add(new VmFrame(cls, null, code, self, code.MaxSlot + 1, pushResult: true, field.Name) { StackBase = _sp }),
            pushResult: true,
            $"initializer of {field}");
    }

    private Value RunNested(Action enter, bool pushResult, string what)
    {
        var prev = Vm.CurrentThread;
        Vm.CurrentThread = this;
        var depth = _frames.Count;
        var sp0 = _sp;
        _nested++;
        try
        {
            enter();
            Execute(depth);
            var r = pushResult ? (_sp > sp0 ? PopValue() : Value.Void) : Value.Void;
            if (_sp != sp0)
            {
                throw new VmException($"operand stack imbalance of {_sp - sp0} after {what}");
            }

            return r;
        }
        catch (VmException ex)
        {
            Unwind(ex, depth, sp0);
            throw;
        }
        finally
        {
            _nested--;
            Vm.CurrentThread = prev;
        }
    }

    private void Unwind(VmException ex, int depth, int sp)
    {
        if (ex.ScriptTrace.Count == 0)
        {
            for (var i = _frames.Count - 1; i >= 0; i--)
            {
                ex.ScriptTrace.Add(_frames[i].Describe());
            }
        }

        if (_frames.Count > depth)
        {
            _frames.RemoveRange(depth, _frames.Count - depth);
        }

        _sp = Math.Min(_sp, sp);
    }

    // ---------------------------------------------------------------- calls

    private void Call(VmMethod m, VmObject? self, Value[] args, bool pushResult)
    {
        if (m.IsStatic)
        {
            Vm.EnsureInitialized(m.Owner);
            self = null;
        }
        else if (self is null)
        {
            throw new VmException($"instance missing for {m.Signature}");
        }

        if (args.Length != m.ArgCount)
        {
            throw new VmException($"{m.Signature} called with {args.Length} arguments");
        }

        if (m.IsNative)
        {
            var fn = Vm.Natives.Find(m.Owner.Name, m.Name, m.Descriptor)
                ?? throw new VmNativeMissingException(m.Owner.Name, m.Name, m.Descriptor);
            var r = fn(new NativeCall(Vm, this, m, self, args));
            if (pushResult)
            {
                Push(r);
            }

            return;
        }

        if (m.Code is null)
        {
            throw new VmException($"abstract method {m.Signature} has no code");
        }

        var f = new VmFrame(m.Owner, m, m.Code, self, m.FrameSize, pushResult, null) { StackBase = _sp };
        var slot = 0;
        if (!m.IsStatic)
        {
            f.Locals[0] = Value.Obj(self);
            f.LocalTypes[0] = "L" + m.Owner.Name + ";";
            slot = 1;
        }

        // Arguments occupy the slots after `this` in REVERSE order: the last argument is slot 1 (slot 0 in a static
        // method), the first argument the highest slot. Measured: String.timeToString(FI) reads the int flags from
        // slot 0 and the float time from slot 1; Tyre.SetInflation(FI) reads `I` from slot 2, `forced` from slot 1.
        for (var i = 0; i < args.Length; i++)
        {
            var a = args.Length - 1 - i;
            f.Locals[slot + i] = VmTypes.Coerce(args[a], m.Params[a]);
            f.LocalTypes[slot + i] = m.Params[a];
        }

        _frames.Add(f);
    }

    /// <summary>A number stored into a String variable becomes its text (the engine converts on assignment).</summary>
    private Value ToStringStorage(VmVariable target, Value v) =>
        v.Kind is VmKind.Int or VmKind.Float && target.Descriptor == "Ljava.lang.String;"
            ? Value.Obj(Vm.NewString(Vm.ToScriptString(v)))
            : v;

    private void ReturnFrom(VmFrame f, Value v)
    {
        _sp = f.StackBase;
        _frames.RemoveAt(_frames.Count - 1);
        if (f.PushResult)
        {
            // A non-void method that ends without `return` yields the type's default (0, 0.0, null), not void.
            if (v.Kind == VmKind.Void && f.Method?.Return is { } ret && VmTypes.KindOf(ret) != VmKind.Void)
            {
                v = VmTypes.KindOf(ret) switch
                {
                    VmKind.Float => Value.Float(0f),
                    VmKind.Object => Value.Null,
                    _ => Value.Int(0),
                };
            }

            Push(v);
        }
    }

    // ---------------------------------------------------------------- main loop

    private void Execute(int stopDepth)
    {
        var vm = Vm;
        while (_frames.Count > stopDepth)
        {
            if (State != VmThreadState.Ready)
            {
                if (_nested > 0)
                {
                    throw new VmException($"thread '{Name}' {State.ToString().ToLowerInvariant()} inside a synchronous call; blocking natives need a scheduled thread");
                }

                return;
            }

            if (_nested == 0 && --_slice < 0)
            {
                return;
            }

            if (++vm.Executed > vm.InstructionBudget)
            {
                throw new VmBudgetExceededException(vm.InstructionBudget);
            }

            var f = _frames[^1];
            if (f.Ip >= f.Code.Length)
            {
                ReturnFrom(f, Value.Void);
                continue;
            }

            var ip = f.Ip;
            var ins = f.Code.Ins[ip];
            f.CurrentIp = ip;
            f.Ip = ip + 1 + f.Code.Consumed[ip];
            vm.Trace?.WriteLine($"{f.Class.Name}.{f.Method?.Name ?? "<init:" + f.InitializerOf + ">"} {ip,4} L{ins.Line,-5} {Op.Mnemonic(ins.Op)} {ins.Arg} sp={_sp}");
            try
            {
                Step(f, ip, ins);
            }
            catch (Exception ex) when (ex is not VmException)
            {
                throw new VmException($"internal error ({ex.GetType().Name}): {ex.Message}", ex);
            }
        }
    }

    private void Step(VmFrame f, int ip, VmIns ins)
    {
        switch (ins.Op)
        {
            case Op.Local:
                f.EnsureSlot(ins.Arg);
                PushVar(new LocalVariable(f, ins.Arg));
                break;
            case Op.DeclLocal:
                DeclLocal(f, ip, ins.Arg);
                break;
            case Op.DropLocal:
            case Op.Type:
            case Op.Class:
            case Op.Method:
            case Op.MName:
            case Op.DeclUse:
            case Op.LocalName:
            case Op.Unused0F:
                break;
            case Op.SkipIfTrue:
            case Op.SkipIfFalse:
                if (PeekValue().IsTrue == (ins.Op == Op.SkipIfTrue))
                {
                    f.Ip = ip + 2;
                }

                break;
            case Op.Oper:
                Operate(f, ip, ins.Arg, discard: false);
                break;
            case Op.OperDiscard:
                Operate(f, ip, ins.Arg, discard: true);
                break;
            case Op.Str:
            case Op.Float:
            case Op.Int:
            case Op.Null:
            case Op.Int2:
            case Op.Res:
                Push(Literal(f, ip));
                break;
            case Op.EndInit:
                ReturnFrom(f, PopValue());
                break;
            case Op.Name:
                PushVar(EvalNameVariable(f, ip + 1, ins.Arg));
                break;
            case Op.Jmp:
                f.Ip = ip + ins.Arg;
                break;
            case Op.JmpF:
                if (!PopValue().IsTrue)
                {
                    f.Ip = ip + ins.Arg;
                }

                break;
            case Op.JmpT:
                if (PopValue().IsTrue)
                {
                    f.Ip = ip + ins.Arg;
                }

                break;
            case Op.Case:
            {
                var c = PopValue();
                if (Equal(PeekValue(), c, "case"))
                {
                    PopSlot();
                    f.Ip = ip + ins.Arg;
                }

                break;
            }

            case Op.Pop:
                PopSlot();
                break;
            case Op.Field:
                PushVar(FieldVariable(f, ResolveField(f.Class, ins.Arg), Base.Implicit));
                break;
            case Op.FieldByName:
                PushVar(FieldByName(f, Base.Of(PopValue()), f.Class.Utf8((uint)ins.Arg, "field name")));
                break;
            case Op.SuperField:
                throw new VmOpcodeException("SUPERFIELD (0x1D) has no operand in the type table and is unused in the shipped corpus; its name source is unknown");
            case Op.ArrayInit:
                ArrayInit(ins.Arg);
                break;
            case Op.PushZeros:
                for (var i = 0; i < ins.Arg; i++)
                {
                    Push(Value.Zero);
                }

                break;
            case Op.Index:
                Index();
                break;
            case Op.CallInit:
                CallInit();
                break;
            case Op.ThisInit:
            case Op.SuperInit:
                ThisOrSuperInit(f, ins.Op == Op.SuperInit);
                break;
            case Op.Call:
            {
                var args = PopArgs();
                var (m, self) = ResolveCall(f, ip + 1, args);
                Call(m, self, args, pushResult: true);
                break;
            }

            case Op.CallByName:
            {
                var target = PopValue();
                var args = PopArgs();
                var (m, self) = DispatchByName(f, Base.Of(target), f.Class.Utf8((uint)ins.Arg, "method name"), args);
                Call(m, self, args, pushResult: true);
                break;
            }

            case Op.SuperCall:
            {
                var args = PopArgs();
                var name = f.Class.Utf8((uint)ins.Arg, "method name");
                var sup = f.Class.Super ?? throw new VmException($"super.{name}() in {f.Class.Name}, which has no superclass");
                var m = Vm.SelectOverload(sup.FindMethods(name, args.Length), args)
                    ?? throw new VmException($"unknown method super.{name}() with {args.Length} arguments in {sup.Name}");
                Call(m, m.IsStatic ? null : f.This ?? throw new VmException($"super.{name}() from a static method"), args, pushResult: true);
                break;
            }

            case Op.ArgC:
                Push(Value.Int(ins.Arg));
                break;
            case Op.RValue:
                Push(PopValue());
                break;
            case Op.Push0:
                Push(Value.Zero);
                break;
            case Op.Return:
                ReturnFrom(f, Value.Void);
                break;
            case Op.RetVal:
                ReturnFrom(f, PopValue());
                break;
            case Op.AssignInit:
            {
                var v = PopValue();
                var target = PopVar("ASSIGNINIT");
                target.Set(ToStringStorage(target, v));
                break;
            }

            case Op.Invalid00:
            case Op.Invalid13:
                throw new VmOpcodeException($"fatal script error: opcode 0x{ins.Op:X2} is invalid at runtime");
            default:
                throw new VmOpcodeException($"unknown opcode 0x{ins.Op:X2}");
        }
    }

    // ---------------------------------------------------------------- operand stack

    private struct Slot
    {
        public Value V;
        public VmVariable? Var;
    }

    private void Push(Value v)
    {
        if (_sp == _stack.Length)
        {
            Array.Resize(ref _stack, _stack.Length * 2);
        }

        _stack[_sp++] = new Slot { V = v };
    }

    private void PushVar(VmVariable v)
    {
        if (_sp == _stack.Length)
        {
            Array.Resize(ref _stack, _stack.Length * 2);
        }

        _stack[_sp++] = new Slot { Var = v };
    }

    private Slot PopSlot()
    {
        var floor = _frames.Count > 0 ? _frames[^1].StackBase : 0;
        if (_sp <= floor)
        {
            throw new VmException("operand stack underflow");
        }

        var s = _stack[--_sp];
        _stack[_sp] = default;
        return s;
    }

    /// <summary>Pop as an rvalue (a Variable is dereferenced, as the engine's 0x423e30 does).</summary>
    private Value PopValue()
    {
        var s = PopSlot();
        return s.Var?.Get() ?? s.V;
    }

    private Value PeekValue()
    {
        var floor = _frames.Count > 0 ? _frames[^1].StackBase : 0;
        if (_sp <= floor)
        {
            throw new VmException("operand stack underflow");
        }

        var s = _stack[_sp - 1];
        return s.Var?.Get() ?? s.V;
    }

    private VmVariable PopVar(string what)
    {
        var s = PopSlot();
        return s.Var ?? throw new VmException($"stack access: value on top (instead of variable) for {what}");
    }

    private Value[] PopArgs()
    {
        var n = PopValue();
        if (n.Kind != VmKind.Int || n.AsInt < 0 || n.AsInt > _sp)
        {
            throw new VmException($"bad argument count {n.Describe()} on the stack");
        }

        var args = new Value[n.AsInt];
        for (var i = args.Length - 1; i >= 0; i--)
        {
            args[i] = PopValue();
        }

        return args;
    }

    // ---------------------------------------------------------------- helpers

    private void DeclLocal(VmFrame f, int ip, int slot)
    {
        var code = f.Code;
        var j = ip + 1;
        if (j < code.Length && code.Ins[j].Op == Op.LocalName)
        {
            j++;
        }

        string? desc = null;
        if (j < code.Length && code.Ins[j].Op == Op.Type)
        {
            desc = f.Class.Utf8((uint)code.Ins[j].Arg, "local type");
            j++;
        }

        f.EnsureSlot(slot);
        f.Locals[slot] = VmTypes.DefaultValue(desc);
        f.LocalTypes[slot] = desc;
        if (j < code.Length && code.Ins[j].Op == Op.DeclUse)
        {
            PushVar(new LocalVariable(f, slot));
        }
    }

    private Value Literal(VmFrame f, int at)
    {
        if (at >= f.Code.Length)
        {
            throw new VmException("literal expected at end of code");
        }

        var ins = f.Code.Ins[at];
        switch (ins.Op)
        {
            case Op.Str:
                return Value.Obj(Vm.InternLiteral(f.Class.Utf8((uint)ins.Arg, "string literal")));
            case Op.Float:
                return Value.Float(BitConverter.Int32BitsToSingle(ins.Arg));
            case Op.Int:
            case Op.Int2:
                return Value.Int(ins.Arg);
            case Op.Null:
                return Value.Null;
            case Op.Res:
            {
                var c = f.Class.Constant((uint)ins.Arg, TufaConstantType.Resource);
                return Value.Int(Vm.ResolveResource(f.Class.Utf8(c.A, "resource archive"), c.B));
            }

            default:
                throw new VmException($"unhandled literal type: {Op.Mnemonic(ins.Op)}");
        }
    }

    private string TypeAt(VmFrame f, int at, string what)
    {
        if (at >= f.Code.Length || f.Code.Ins[at].Op != Op.Type)
        {
            throw new VmException($"invalid {what} structure: TYPE expected");
        }

        return f.Class.Utf8((uint)f.Code.Ins[at].Arg, "type");
    }

    private void Index()
    {
        var arr = PopValue();
        var idx = PopValue();
        if (arr.Kind != VmKind.Object || arr.Ref is not VmArray a)
        {
            throw new VmException(arr.IsNull ? "indexing a null array" : $"indexing a non-array ({arr.Describe()})");
        }

        if (idx.Kind != VmKind.Int)
        {
            throw new VmException($"non-integer array index {idx.Describe()}");
        }

        var i = idx.AsInt;
        if (i < 0 || i >= a.Items.Length)
        {
            throw new VmException($"array index {i} out of bounds (length {a.Items.Length})");
        }

        PushVar(new ArrayElement(a, i));
    }

    private void ArrayInit(int n)
    {
        // Unused in the corpus; semantics from the handler at 0x4232bb (confidence M): [var v1..vn] -> [var arr].
        var values = new Value[n];
        for (var i = n - 1; i >= 0; i--)
        {
            values[i] = PopValue();
        }

        var s = _stack[_sp - 1];
        var desc = s.Var?.Descriptor;
        if (desc is null || !desc.StartsWith('['))
        {
            throw new VmException("ARRAYINIT: target is not an array variable");
        }

        var arr = Vm.NewArray(desc[1..], n);
        for (var i = 0; i < n; i++)
        {
            arr.Items[i] = VmTypes.Coerce(values[i], arr.ElementDescriptor);
        }

        Push(Value.Obj(arr));
    }

    private void CallInit()
    {
        var args = PopArgs();
        var target = PeekValue();
        var obj = target.Kind == VmKind.Object && target.Ref is not null
            ? target.Ref
            : throw new VmException($"CALLINIT on {target.Describe()}");
        var ctor = Vm.FindConstructor(obj.Class, args);
        if (ctor is null)
        {
            if (args.Length == 0)
            {
                return;
            }

            throw new VmException($"no constructor {obj.Class.Name}.<init> for {args.Length} arguments");
        }

        Call(ctor, obj, args, pushResult: false);
    }

    private void ThisOrSuperInit(VmFrame f, bool isSuper)
    {
        var args = PopArgs();
        var self = f.This ?? throw new VmException((isSuper ? "super" : "this") + "(...) outside an instance method");
        VmMethod? ctor;
        if (isSuper)
        {
            if (f.Class.Super is null)
            {
                if (args.Length == 0)
                {
                    return;
                }

                throw new VmException($"super(...) in {f.Class.Name}, which has no superclass");
            }

            ctor = Vm.FindConstructor(f.Class.Super, args);
        }
        else
        {
            // this(...): the lookup starts at the current class and, like super(...), walks up (measured: 32 of 33
            // THISINIT sites resolve in the class itself, InventoryItem_Part's this(inv) only in InventoryItem).
            ctor = Vm.FindConstructor(f.Class, args);
        }

        if (ctor is null)
        {
            if (args.Length == 0)
            {
                return;
            }

            throw new VmException($"no constructor for {(isSuper ? "super" : "this")}(...) with {args.Length} arguments in {f.Class.Name}");
        }

        Call(ctor, self, args, pushResult: false);
    }

    private static string Num(int v) => v.ToString(CultureInfo.InvariantCulture);
}
