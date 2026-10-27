using Slrr.Formats.Tufa;

namespace Slrr.Decompiler.Tests;

/// <summary>Builds small synthetic TUFA classes for unit tests (constant pool, members, bodies).</summary>
internal sealed class ClassBuilder
{
    private readonly Dictionary<string, uint> _utf8 = [];

    public ClassBuilder(string name, string super)
    {
        Class.ClassHeader0 = 0xFFFFFFFF;
        Class.ThisClass = ClassRef(name);
        Class.SuperClass = ClassRef(super);
    }

    public TufaClass Class { get; } = new() { Version = 4 };

    public uint Utf8(string s)
    {
        if (!_utf8.TryGetValue(s, out var i))
        {
            i = Add(new TufaConstant(TufaConstantType.Utf8, s, 0, 0));
            _utf8[s] = i;
        }

        return i;
    }

    public uint ClassRef(string qualified) => Add(new TufaConstant(TufaConstantType.Class, null, Utf8(qualified), 0));

    public uint Member(string cls, string name, string desc)
    {
        var nt = Add(new TufaConstant(TufaConstantType.NameAndType, null, Utf8(name), Utf8(desc)));
        return Add(new TufaConstant(TufaConstantType.MemberRef, null, ClassRef(cls), nt));
    }

    public uint Resource(string archive, uint id) => Add(new TufaConstant(TufaConstantType.Resource, null, Utf8(archive), id));

    /// <summary>Adds a body; each entry is (opcode, operand, source line).</summary>
    public uint Body(params (int Op, long Operand, int Line)[] code)
    {
        var list = code.Select((c, k) =>
        {
            var hasOperand = TufaOpcodes.Get((byte)c.Op)!.OperandSize == 4;
            return new TufaInstruction(k, (byte)c.Op, (ushort)c.Line, hasOperand ? unchecked((int)c.Operand) : 0, hasOperand);
        }).ToList();
        Class.Bodies.Add(new TufaMethodBody(list));
        return (uint)(Class.Bodies.Count - 1);
    }

    public void Method(uint flags, bool isStatic, string name, string desc, uint body) =>
        Class.Methods.Add(new TufaMember(isStatic, flags, Utf8(name), Utf8(desc), body, 0));

    public void Field(uint flags, bool isStatic, string name, string desc, uint init = 0xFFFFFFFF) =>
        Class.Fields.Add(new TufaMember(isStatic, flags, Utf8(name), Utf8(desc), init, 0));

    private uint Add(TufaConstant c)
    {
        Class.Constants.Add(c);
        return (uint)(Class.Constants.Count - 1);
    }
}
