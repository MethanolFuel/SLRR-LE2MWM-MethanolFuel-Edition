using System.Text;
using Slrr.Core;

namespace Slrr.Formats.Tufa;

public enum TufaConstantType : uint
{
    Utf8 = 0,
    Resource = 3,
    Class = 4,
    MemberRef = 5,
    NameAndType = 7,
}

/// <summary>Constant-pool entry. A and B are the index/value operands (B unused for Class).</summary>
public sealed record TufaConstant(TufaConstantType Type, string? Text, uint A, uint B);

/// <summary>Field (16 bytes) or method (20 bytes) table row.</summary>
public sealed record TufaMember(bool IsStatic, uint Flags, uint NameIndex, uint DescriptorIndex, uint Value, uint Slots);

public sealed record TufaInstruction(int Offset, byte Opcode, ushort Line, int Operand, bool HasOperand);

public sealed record TufaMethodBody(IReadOnlyList<TufaInstruction> Instructions);

/// <summary>One compiled class (a .class file may hold several, each starting with a TUFA header).</summary>
public sealed class TufaClass
{
    public uint Version { get; init; }

    /// <summary>u32 after the version (e.g. 0x00011AE1); purpose unknown (checksum / timestamp?).</summary>
    public uint Stamp { get; init; }

    public List<TufaConstant> Constants { get; } = [];
    public List<TufaMember> Fields { get; } = [];
    public List<TufaMember> Methods { get; } = [];
    public List<TufaMethodBody> Bodies { get; } = [];
    public List<string> ChunkOrder { get; } = [];

    /// <summary>CLSS chunk: u32 (always 0xFFFFFFFF), u32 (0/1), this-class idx, super-class idx, interface count, interfaces.</summary>
    public uint ClassHeader0 { get; set; }
    public uint ClassHeader1 { get; set; }
    public uint ThisClass { get; set; }
    public uint SuperClass { get; set; }
    public List<uint> Interfaces { get; } = [];

    public string? Utf8(uint index) =>
        index < Constants.Count && Constants[(int)index].Type == TufaConstantType.Utf8 ? Constants[(int)index].Text : null;

    public string? ClassName(uint classIndex) =>
        classIndex < Constants.Count && Constants[(int)classIndex].Type == TufaConstantType.Class ? Utf8(Constants[(int)classIndex].A) : null;

    public string Name => ClassName(ThisClass) ?? "?";
    public string SuperName => ClassName(SuperClass) ?? "?";

    /// <summary>Human-readable rendering of a constant, e.g. <c>java.game.Part.name:Ljava.lang.String;</c>.</summary>
    public string Describe(uint index)
    {
        if (index >= Constants.Count)
        {
            return $"#{index}?";
        }

        var c = Constants[(int)index];
        return c.Type switch
        {
            TufaConstantType.Utf8 => "\"" + c.Text + "\"",
            TufaConstantType.Class => ClassName(index) ?? "?",
            TufaConstantType.Resource => $"{Utf8(c.A)}:0x{c.B:X}",
            TufaConstantType.NameAndType => $"{Utf8(c.A)}:{Utf8(c.B)}",
            TufaConstantType.MemberRef => $"{ClassName(c.A)}.{Describe(c.B)}",
            _ => $"#{index}",
        };
    }
}

/// <summary>
/// Reader for the game's compiled script format. File = one or more classes, each:
/// "TUFA" u32 version(4) u32 stamp, then chunks (4-byte tag, u32 size, payload):
/// CONS (constant pool), FILD (fields, optional), MTHD (methods, optional), CLSS (class header), TREE (code).
/// </summary>
public static class TufaFile
{
    public static IReadOnlyList<TufaClass> Read(string path) => Read(File.ReadAllBytes(path));

    public static IReadOnlyList<TufaClass> Read(ReadOnlyMemory<byte> bytes)
    {
        var r = new ByteReader(bytes);
        var classes = new List<TufaClass>();
        TufaClass? current = null;
        while (r.Remaining > 0)
        {
            var tag = r.Latin1(4);
            if (tag == "TUFA")
            {
                current = new TufaClass { Version = r.U32(), Stamp = r.U32() };
                if (current.Version != 4)
                {
                    throw new SlrrFormatException($"unsupported TUFA version {current.Version}");
                }

                classes.Add(current);
                continue;
            }

            if (current is null)
            {
                throw new SlrrFormatException("file does not start with TUFA");
            }

            var size = r.CheckedCount(r.U32(), 1, $"{tag} chunk size");
            var payload = new ByteReader(r.Slice(size));
            current.ChunkOrder.Add(tag);
            switch (tag)
            {
                case "CONS": ReadConstants(payload, current); break;
                case "FILD": ReadMembers(payload, current.Fields, 4); break;
                case "MTHD": ReadMembers(payload, current.Methods, 5); break;
                case "CLSS": ReadClassHeader(payload, current); break;
                case "TREE": ReadCode(payload, current); break;
                default: throw new SlrrFormatException($"unknown chunk '{tag}'");
            }

            if (payload.Remaining != 0)
            {
                throw new SlrrFormatException($"{tag} chunk has {payload.Remaining} unparsed bytes");
            }
        }

        return classes;
    }

    private static void ReadConstants(ByteReader p, TufaClass cls)
    {
        var count = p.CheckedCount(p.U32(), 8, "constant");
        for (var i = 0; i < count; i++)
        {
            var type = (TufaConstantType)p.U32();
            switch (type)
            {
                case TufaConstantType.Utf8:
                {
                    var len = p.CheckedCount(p.U32(), 1, "string length");
                    var text = p.Latin1(len);
                    if (p.U8() != 0)
                    {
                        throw new SlrrFormatException($"constant {i}: string is not NUL-terminated");
                    }

                    cls.Constants.Add(new(type, text, 0, 0));
                    break;
                }

                case TufaConstantType.Class:
                    cls.Constants.Add(new(type, null, p.U32(), 0));
                    break;
                case TufaConstantType.Resource:
                case TufaConstantType.MemberRef:
                case TufaConstantType.NameAndType:
                    cls.Constants.Add(new(type, null, p.U32(), p.U32()));
                    break;
                default:
                    throw new SlrrFormatException($"constant {i}: unknown type {(uint)type}");
            }
        }
    }

    private static void ReadMembers(ByteReader p, List<TufaMember> into, int words)
    {
        foreach (var isStatic in new[] { true, false })
        {
            var count = p.CheckedCount(p.U32(), words * 4, "member");
            for (var i = 0; i < count; i++)
            {
                var flags = p.U32();
                var name = p.U32();
                var desc = p.U32();
                var value = p.U32();
                var slots = words == 5 ? p.U32() : 0;
                into.Add(new TufaMember(isStatic, flags, name, desc, value, slots));
            }
        }
    }

    private static void ReadClassHeader(ByteReader p, TufaClass cls)
    {
        cls.ClassHeader0 = p.U32();
        cls.ClassHeader1 = p.U32();
        cls.ThisClass = p.U32();
        cls.SuperClass = p.U32();
        var n = p.CheckedCount(p.U32(), 4, "interface");
        for (var i = 0; i < n; i++)
        {
            cls.Interfaces.Add(p.U32());
        }
    }

    private static void ReadCode(ByteReader p, TufaClass cls)
    {
        var methods = p.CheckedCount(p.U32(), 4, "method body");
        for (var m = 0; m < methods; m++)
        {
            var count = p.CheckedCount(p.U32(), 3, "instruction");
            var list = new List<TufaInstruction>(count);
            for (var i = 0; i < count; i++)
            {
                var at = p.Position;
                var code = p.U8();
                var line = p.U16();
                var op = TufaOpcodes.Get(code)
                    ?? throw new SlrrFormatException($"unknown opcode 0x{code:X2} at TREE offset {at}");
                var hasOperand = op.OperandSize == 4;
                list.Add(new TufaInstruction(at, code, line, hasOperand ? p.I32() : 0, hasOperand));
            }

            cls.Bodies.Add(new TufaMethodBody(list));
        }
    }

    /// <summary>Text dump used by <c>slrr dump-class</c>.</summary>
    public static string Dump(TufaClass c, bool withCode = true)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"class {c.Name} extends {c.SuperName}  (TUFA v{c.Version}, stamp 0x{c.Stamp:X8}, chunks {string.Join(' ', c.ChunkOrder)}, {c.Methods.Count} methods, {c.Bodies.Count} bodies)");
        foreach (var i in c.Interfaces)
        {
            sb.AppendLine($"  implements {c.ClassName(i)}");
        }

        sb.AppendLine($"  constants: {c.Constants.Count}");
        for (var i = 0; i < c.Constants.Count; i++)
        {
            sb.AppendLine($"    #{i,-4} {c.Constants[i].Type,-11} {c.Describe((uint)i)}");
        }

        foreach (var f in c.Fields)
        {
            var init = f.Value == uint.MaxValue ? string.Empty : $" = {f.Value}";
            sb.AppendLine($"  field {(f.IsStatic ? "static " : string.Empty)}flags=0x{f.Flags:X} {c.Utf8(f.NameIndex)} {c.Utf8(f.DescriptorIndex)}{init}");
        }

        foreach (var m in c.Methods)
        {
            sb.AppendLine($"  method {(m.IsStatic ? "static " : string.Empty)}flags=0x{m.Flags:X} {c.Utf8(m.NameIndex)}{c.Utf8(m.DescriptorIndex)} body={m.Value} slots={m.Slots}");
            if (withCode && m.Value < c.Bodies.Count)
            {
                AppendBody(sb, c, c.Bodies[(int)m.Value]);
            }
        }

        // Bodies no MTHD row points at (2,660 of 7,033 in the shipped corpus; purpose still open, see docs/formats/tufa.md).
        var referenced = c.Methods.Select(m => m.Value).ToHashSet();
        for (var i = 0; i < c.Bodies.Count; i++)
        {
            if (!referenced.Contains((uint)i))
            {
                sb.AppendLine($"  body {i} (not referenced by MTHD, {c.Bodies[i].Instructions.Count} instructions)");
                if (withCode)
                {
                    AppendBody(sb, c, c.Bodies[i]);
                }
            }
        }

        return sb.ToString();
    }

    private static void AppendBody(StringBuilder sb, TufaClass c, TufaMethodBody body)
    {
        foreach (var ins in body.Instructions)
        {
            var op = TufaOpcodes.Get(ins.Opcode)!;
            var operand = !ins.HasOperand ? string.Empty : op.Kind switch
            {
                TufaOperandKind.Float => $" {ins.Operand} ({BitConverter.Int32BitsToSingle(ins.Operand)}f)",
                TufaOperandKind.StringConst or TufaOperandKind.ClassConst or TufaOperandKind.FieldRef
                    or TufaOperandKind.MethodRef or TufaOperandKind.ResourceConst => $" #{ins.Operand} {c.Describe(unchecked((uint)ins.Operand))}",
                _ => $" {ins.Operand}",
            };
            sb.AppendLine($"      L{ins.Line,-5} {ins.Opcode:X2} {op.Mnemonic}{operand}");
        }
    }
}
