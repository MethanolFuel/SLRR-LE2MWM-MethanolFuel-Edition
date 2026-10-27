using Slrr.Formats.Tufa;

namespace Slrr.Vm.Tests;

/// <summary>Builds in-memory TUFA classes (constant pool, fields, methods, bodies) for interpreter unit tests.</summary>
internal sealed class ClassBuilder
{
    private readonly TufaClass _c = new() { Version = 4 };
    private readonly Dictionary<string, uint> _utf8 = new(StringComparer.Ordinal);
    private readonly Dictionary<string, uint> _classes = new(StringComparer.Ordinal);

    public ClassBuilder(string name, string? super = "java.lang.Object")
    {
        _c.ThisClass = Class(name);
        _c.SuperClass = super is null ? uint.MaxValue : Class(super);
        Name = name;
    }

    public string Name { get; }

    public uint Utf8(string s)
    {
        if (!_utf8.TryGetValue(s, out var i))
        {
            i = Add(new TufaConstant(TufaConstantType.Utf8, s, 0, 0));
            _utf8[s] = i;
        }

        return i;
    }

    public uint Class(string name)
    {
        if (!_classes.TryGetValue(name, out var i))
        {
            var n = Utf8(name);
            i = Add(new TufaConstant(TufaConstantType.Class, null, n, 0));
            _classes[name] = i;
        }

        return i;
    }

    public uint Member(string cls, string name, string desc)
    {
        var c = Class(cls);
        var nt = Add(new TufaConstant(TufaConstantType.NameAndType, null, Utf8(name), Utf8(desc)));
        return Add(new TufaConstant(TufaConstantType.MemberRef, null, c, nt));
    }

    public uint Resource(string archive, uint id) => Add(new TufaConstant(TufaConstantType.Resource, null, Utf8(archive), id));

    public ClassBuilder Interface(string name)
    {
        _c.Interfaces.Add(Class(name));
        return this;
    }

    public ClassBuilder Field(string name, string desc, bool isStatic = false, Action<Asm>? init = null)
    {
        var body = uint.MaxValue;
        if (init is not null)
        {
            var a = new Asm(this);
            init(a);
            a.Ins(Op.EndInit);
            body = AddBody(a);
        }

        _c.Fields.Add(new TufaMember(isStatic, isStatic ? 0x8u : 0u, Utf8(name), Utf8(desc), body, 0));
        return this;
    }

    public ClassBuilder Method(string name, string desc, Action<Asm> body, bool isStatic = false, uint flags = 0x1)
    {
        var a = new Asm(this);
        body(a);
        _c.Methods.Add(new TufaMember(isStatic, flags | (isStatic ? 0x8u : 0u), Utf8(name), Utf8(desc), AddBody(a), 0));
        return this;
    }

    public ClassBuilder Native(string name, string desc, bool isStatic = false)
    {
        var a = new Asm(this);
        _c.Methods.Add(new TufaMember(isStatic, 0x41u | (isStatic ? 0x8u : 0u), Utf8(name), Utf8(desc), AddBody(a), 0));
        return this;
    }

    public TufaClass Build() => _c;

    private uint AddBody(Asm a)
    {
        _c.Bodies.Add(new TufaMethodBody(a.Code));
        return (uint)(_c.Bodies.Count - 1);
    }

    private uint Add(TufaConstant c)
    {
        _c.Constants.Add(c);
        return (uint)(_c.Constants.Count - 1);
    }
}

/// <summary>Instruction list writer; helpers emit the compiler's patterns (see docs/research/script-vm-opcodes.md).</summary>
internal sealed class Asm(ClassBuilder b)
{
    private static readonly HashSet<byte> NoOperand = [0x04, 0x05, 0x0C, 0x10, 0x13, 0x18, 0x1D, 0x20, 0x21, 0x22, 0x23, 0x24, 0x28, 0x29, 0x2A, 0x2B, 0x2C, 0x2D, 0x00];

    public List<TufaInstruction> Code { get; } = [];

    public int Here => Code.Count;

    public ushort Line { get; set; } = 1;

    public Asm Ins(byte op, int arg = 0)
    {
        Code.Add(new TufaInstruction(Code.Count, op, Line, arg, !NoOperand.Contains(op)));
        return this;
    }

    /// <summary>Overwrite the operand of a jump at <paramref name="at"/> so it lands on <paramref name="target"/>.</summary>
    public void Patch(int at, int target)
    {
        var i = Code[at];
        Code[at] = i with { Operand = target - at };
    }

    public Asm Int(int v) => Ins(Op.Oper, Operator.Literal).Ins(Op.Int, v);

    public Asm Float(float v) => Ins(Op.Oper, Operator.Literal).Ins(Op.Float, BitConverter.SingleToInt32Bits(v));

    public Asm Str(string s) => Ins(Op.Oper, Operator.Literal).Ins(Op.Str, (int)b.Utf8(s));

    public Asm Null() => Ins(Op.Oper, Operator.Literal).Ins(Op.Null);

    public Asm Local(int slot) => Ins(Op.Local, slot);

    public Asm O(int op) => Ins(Op.Oper, op);

    public Asm D(int op) => Ins(Op.OperDiscard, op);

    public Asm Field(string cls, string name, string desc) => Ins(Op.Field, (int)b.Member(cls, name, desc));

    public Asm Type(string desc) => Ins(Op.Type, (int)b.Utf8(desc));

    public Asm New(string cls) => O(Operator.New).Type("L" + cls + ";");

    public Asm ArgC(int n) => Ins(Op.ArgC, n);

    public Asm FieldByName(string name) => Ins(Op.FieldByName, (int)b.Utf8(name));

    public Asm SuperCall(int argc, string name) => ArgC(argc).Ins(Op.SuperCall, (int)b.Utf8(name));

    /// <summary><c>ARGC n; CALL; METHOD ref</c> (implicit this or static).</summary>
    public Asm Call(int argc, string cls, string name, string desc) => ArgC(argc).Ins(Op.Call).Ins(Op.Method, (int)b.Member(cls, name, desc));

    /// <summary><c>ARGC n; CALL; NAME 2; CLASS c; METHOD ref</c> (static call through a class name).</summary>
    public Asm CallStatic(int argc, string cls, string name, string desc) =>
        ArgC(argc).Ins(Op.Call).Ins(Op.Name, 2).Ins(Op.Class, (int)b.Class(cls)).Ins(Op.Method, (int)b.Member(cls, name, desc));

    /// <summary><c>ARGC n; CALL; NAME 2; LOCAL slot; MNAME name</c> (call by name on a local).</summary>
    public Asm CallOnLocal(int argc, int slot, string name) =>
        ArgC(argc).Ins(Op.Call).Ins(Op.Name, 2).Ins(Op.Local, slot).Ins(Op.MName, (int)b.Utf8(name));

    public Asm DeclLocal(int slot, string desc, bool use) =>
        use ? Ins(Op.DeclLocal, slot).Type(desc).Ins(Op.DeclUse) : Ins(Op.DeclLocal, slot).Type(desc);

    public Asm ReturnValue() => Ins(Op.RValue).Ins(Op.RetVal);

    public Asm ReturnVoid() => Ins(Op.Push0).Ins(Op.RetVal);
}
