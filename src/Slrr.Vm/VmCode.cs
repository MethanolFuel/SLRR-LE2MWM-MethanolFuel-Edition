using Slrr.Formats.Tufa;

namespace Slrr.Vm;

/// <summary>Runtime opcode numbers (names from docs/research/script-vm-opcodes.md §2).</summary>
public static class Op
{
    public const byte Invalid00 = 0x00;
    public const byte Local = 0x01;
    public const byte DeclLocal = 0x02;
    public const byte DropLocal = 0x03;
    public const byte SkipIfTrue = 0x04;
    public const byte SkipIfFalse = 0x05;
    public const byte Type = 0x06;
    public const byte Oper = 0x07;
    public const byte OperDiscard = 0x08;
    public const byte Str = 0x09;
    public const byte Float = 0x0A;
    public const byte Int = 0x0B;
    public const byte Null = 0x0C;
    public const byte Int2 = 0x0D;
    public const byte Res = 0x0E;
    public const byte Unused0F = 0x0F;
    public const byte EndInit = 0x10;
    public const byte Name = 0x11;
    public const byte MName = 0x12;
    public const byte Invalid13 = 0x13;
    public const byte Jmp = 0x14;
    public const byte JmpF = 0x15;
    public const byte JmpT = 0x16;
    public const byte Case = 0x17;
    public const byte Pop = 0x18;
    public const byte Class = 0x19;
    public const byte Method = 0x1A;
    public const byte Field = 0x1B;
    public const byte FieldByName = 0x1C;
    public const byte SuperField = 0x1D;
    public const byte ArrayInit = 0x1E;
    public const byte PushZeros = 0x1F;
    public const byte Index = 0x20;
    public const byte CallInit = 0x21;
    public const byte ThisInit = 0x22;
    public const byte SuperInit = 0x23;
    public const byte Call = 0x24;
    public const byte CallByName = 0x25;
    public const byte SuperCall = 0x26;
    public const byte ArgC = 0x27;
    public const byte RValue = 0x28;
    public const byte Push0 = 0x29;
    public const byte Return = 0x2A;
    public const byte RetVal = 0x2B;
    public const byte DeclUse = 0x2C;
    public const byte AssignInit = 0x2D;
    public const byte LocalName = 0x2E;

    private static readonly string[] Names =
    [
        "INVALID00", "LOCAL", "DECLLOCAL", "DROPLOCAL", "SKIPIF_T", "SKIPIF_F", "TYPE", "OP", "OPDISCARD", "STR", "FLOAT",
        "INT", "NULL", "INT2", "RES", "UNUSED0F", "ENDINIT", "NAME", "MNAME", "INVALID13", "JMP", "JMPF", "JMPT", "CASE",
        "POP", "CLASS", "METHOD", "FIELD", "FIELDBYNAME", "SUPERFIELD", "ARRAYINIT", "PUSHZEROS", "INDEX", "CALLINIT",
        "THISINIT", "SUPERINIT", "CALL", "CALLBYNAME", "SUPERCALL", "ARGC", "RVALUE", "PUSH0", "RETURN", "RETVAL",
        "DECLUSE", "ASSIGNINIT", "LOCALNAME",
    ];

    public static string Mnemonic(byte op) => op < Names.Length ? Names[op] : $"op{op:X2}";
}

/// <summary>Operator codes of <c>07 OP</c> / <c>08 OPDISCARD</c> (§3 of the opcode notes).</summary>
public static class Operator
{
    public const int Ternary = 0x00;
    public const int OrOr = 0x01;
    public const int AndAnd = 0x02;
    public const int Or = 0x03;
    public const int Xor = 0x04;
    public const int And = 0x05;
    public const int Ne = 0x06;
    public const int Eq = 0x07;
    public const int Ge = 0x08;
    public const int Le = 0x09;
    public const int Gt = 0x0A;
    public const int Lt = 0x0B;
    public const int UShr = 0x0C;
    public const int Shr = 0x0D;
    public const int Shl = 0x0E;
    public const int Sub = 0x0F;
    public const int Add = 0x10;
    public const int Rem = 0x11;
    public const int Div = 0x12;
    public const int Mul = 0x13;
    public const int BitNot = 0x14;
    public const int Not = 0x15;
    public const int Neg = 0x16;
    public const int PostInc = 0x17;
    public const int PostDec = 0x18;
    public const int PreInc = 0x19;
    public const int PreDec = 0x1A;
    public const int Expr = 0x1B;
    public const int Literal = 0x1C;
    public const int Nop1D = 0x1D;
    public const int New = 0x1E;
    public const int NewArray = 0x1F;
    public const int InstanceOf = 0x20;
    public const int Cast = 0x21;
    public const int VoidMarker = 0x22;
    public const int Assign = 0x23;
    public const int AssignLast = 0x2E;

    private static readonly string[] Names =
    [
        "?:", "||", "&&", "|", "^", "&", "!=", "==", ">=", "<=", ">", "<", ">>>", ">>", "<<", "-", "+", "%", "/", "*", "~",
        "!", "neg", "x++", "x--", "++x", "--x", "expr", "lit", "nop1D", "new", "newarray", "instanceof", "cast", "void",
        "=", "*=", "/=", "%=", "+=", "-=", "<<=", ">>=", ">>>=", "&=", "^=", "|=",
    ];

    public static string Name(int code) => code >= 0 && code < Names.Length ? Names[code] : $"op#{code}";

    public static bool IsBinary(int code) => code is >= OrOr and <= Mul;

    public static bool IsUnary(int code) => code is BitNot or Not or Neg;

    public static bool IsIncDec(int code) => code is >= PostInc and <= PreDec;

    public static bool IsAssign(int code) => code is >= Assign and <= AssignLast;

    /// <summary>Instructions that follow this operator and are consumed by it (literal, TYPE or CLASS).</summary>
    public static int Consumes(int code) => code is Literal or New or NewArray or InstanceOf or Cast ? 1 : 0;
}

/// <summary>One decoded instruction: opcode, source line and the 32-bit operand (0 when absent).</summary>
public readonly record struct VmIns(byte Op, ushort Line, int Arg);

/// <summary>
/// A TREE body prepared for execution. <see cref="Consumed"/>[i] is the number of instructions after i that the
/// instruction at i reads itself (a literal after <c>07 1C</c>, a TYPE after <c>new</c>, a name path after
/// <c>CALL</c>/<c>NAME</c>, ...); the interpreter continues at i + 1 + Consumed[i].
/// </summary>
public sealed class VmCode
{
    public VmCode(int bodyIndex, IReadOnlyList<TufaInstruction> source)
    {
        BodyIndex = bodyIndex;
        Ins = new VmIns[source.Count];
        for (var i = 0; i < source.Count; i++)
        {
            Ins[i] = new VmIns(source[i].Opcode, source[i].Line, source[i].Operand);
        }

        Consumed = new int[Ins.Length];
        var maxSlot = -1;
        for (var i = 0; i < Ins.Length; i++)
        {
            var ins = Ins[i];
            Consumed[i] = ComputeConsumed(i);
            if (ins.Op is Op.Local or Op.DeclLocal or Op.DropLocal)
            {
                maxSlot = Math.Max(maxSlot, ins.Arg);
            }
        }

        MaxSlot = maxSlot;
    }

    public int BodyIndex { get; }

    public VmIns[] Ins { get; }

    public int[] Consumed { get; }

    /// <summary>Highest local slot named by LOCAL/DECLLOCAL/DROPLOCAL (-1 when none).</summary>
    public int MaxSlot { get; }

    public int Length => Ins.Length;

    private int ComputeConsumed(int i)
    {
        var ins = Ins[i];
        switch (ins.Op)
        {
            case Op.Oper:
            case Op.OperDiscard:
                return Math.Min(Operator.Consumes(ins.Arg), Ins.Length - i - 1);
            case Op.DeclLocal:
            {
                var j = i + 1;
                if (j < Ins.Length && Ins[j].Op == Op.LocalName)
                {
                    j++;
                }

                if (j < Ins.Length && Ins[j].Op == Op.Type)
                {
                    j++;
                }

                if (j < Ins.Length && Ins[j].Op == Op.DeclUse)
                {
                    j++;
                }

                return j - i - 1;
            }

            case Op.Name:
                return Math.Clamp(ins.Arg, 0, Ins.Length - i - 1);
            case Op.Call:
                if (i + 1 < Ins.Length && Ins[i + 1].Op == Op.Name)
                {
                    return 1 + Math.Clamp(Ins[i + 1].Arg, 0, Ins.Length - i - 2);
                }

                return i + 1 < Ins.Length && Ins[i + 1].Op is Op.Method or Op.MName ? 1 : 0;
            default:
                return 0;
        }
    }
}
