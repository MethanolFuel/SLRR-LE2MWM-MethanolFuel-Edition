namespace Slrr.Formats.Tufa;

/// <summary>What an opcode's 4-byte operand points at, inferred statistically from the shipped corpus.</summary>
public enum TufaOperandKind
{
    None,
    Int,
    Float,
    Register,
    StringConst,
    ClassConst,
    FieldRef,
    MethodRef,
    ResourceConst,
    Constant,
    Branch,
}

/// <summary>
/// One opcode: fixed operand size plus a tentative mnemonic. Operand sizes are MEASURED (every TREE chunk in all
/// 1,692 shipped .class files decodes exactly with this table). Mnemonics and operand kinds are HYPOTHESES from
/// operand statistics (e.g. 0x1B's operand is a FieldRef constant in 49,637 of 49,637 uses); see docs/formats/tufa.md.
/// </summary>
public sealed record TufaOpcode(byte Code, int OperandSize, string Mnemonic, TufaOperandKind Kind);

public static class TufaOpcodes
{
    private static readonly TufaOpcode[] Table =
    [
        new(0x01, 4, "op01_local?", TufaOperandKind.Int),
        new(0x02, 4, "op02_local?", TufaOperandKind.Int),
        new(0x03, 4, "op03_local?", TufaOperandKind.Int),
        new(0x04, 0, "op04", TufaOperandKind.None),
        new(0x05, 0, "op05", TufaOperandKind.None),
        new(0x06, 4, "op06", TufaOperandKind.Int),
        new(0x07, 4, "push_reg?", TufaOperandKind.Register),
        new(0x08, 4, "pop_reg?", TufaOperandKind.Register),
        new(0x09, 4, "ldc_string", TufaOperandKind.StringConst),
        new(0x0A, 4, "ldc_imm32", TufaOperandKind.Float),
        new(0x0B, 4, "op0B", TufaOperandKind.Int),
        new(0x0C, 0, "op0C", TufaOperandKind.None),
        new(0x0D, 4, "op0D", TufaOperandKind.Int),
        new(0x0E, 4, "ldc_resource", TufaOperandKind.ResourceConst),
        new(0x10, 0, "op10", TufaOperandKind.None),
        new(0x11, 4, "op11_argc?", TufaOperandKind.Int),
        new(0x12, 4, "op12_target?", TufaOperandKind.Int),
        new(0x14, 4, "branch14?", TufaOperandKind.Branch),
        new(0x15, 4, "branch15?", TufaOperandKind.Branch),
        new(0x16, 4, "branch16?", TufaOperandKind.Branch),
        new(0x17, 4, "op17", TufaOperandKind.Int),
        new(0x18, 0, "op18", TufaOperandKind.None),
        new(0x19, 4, "class_ref", TufaOperandKind.ClassConst),
        new(0x1A, 4, "invoke", TufaOperandKind.MethodRef),
        new(0x1B, 4, "field", TufaOperandKind.FieldRef),
        new(0x1C, 4, "op1C", TufaOperandKind.Int),
        new(0x20, 0, "op20", TufaOperandKind.None),
        new(0x21, 0, "op21", TufaOperandKind.None),
        new(0x22, 0, "op22", TufaOperandKind.None),
        new(0x23, 0, "op23", TufaOperandKind.None),
        new(0x24, 0, "op24", TufaOperandKind.None),
        new(0x25, 4, "op25", TufaOperandKind.Int),
        new(0x26, 4, "op26", TufaOperandKind.Int),
        new(0x27, 4, "op27_argc?", TufaOperandKind.Int),
        new(0x28, 0, "op28", TufaOperandKind.None),
        new(0x29, 0, "op29", TufaOperandKind.None),
        new(0x2A, 0, "return?", TufaOperandKind.None),
        new(0x2B, 0, "op2B", TufaOperandKind.None),
        new(0x2C, 0, "op2C", TufaOperandKind.None),
        new(0x2D, 0, "op2D", TufaOperandKind.None),
    ];

    private static readonly TufaOpcode?[] ByCode = Build();

    public static IReadOnlyList<TufaOpcode> All => Table;

    public static TufaOpcode? Get(byte code) => ByCode[code];

    private static TufaOpcode?[] Build()
    {
        var a = new TufaOpcode?[256];
        foreach (var op in Table)
        {
            a[op.Code] = op;
        }

        return a;
    }
}
