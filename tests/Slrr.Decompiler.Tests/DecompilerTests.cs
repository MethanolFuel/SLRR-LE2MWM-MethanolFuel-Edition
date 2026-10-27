using Slrr.Formats.Tufa;
using Xunit;

namespace Slrr.Decompiler.Tests;

public class DecompilerTests
{
    private const int Local = 0x01, DeclLocal = 0x02, DropLocal = 0x03, SkipT = 0x04, Type = 0x06, Op = 0x07, OpD = 0x08;
    private const int Float = 0x0A, Int = 0x0B, Res = 0x0E, EndInit = 0x10, MName = 0x12, Jmp = 0x14, JmpF = 0x15, JmpT = 0x16;
    private const int Case = 0x17, Pop = 0x18, ClassOp = 0x19, Field = 0x1B, FieldByName = 0x1C, SuperInit = 0x23, Call = 0x24;
    private const int ArgC = 0x27, RValue = 0x28, Push0 = 0x29, Return = 0x2A, RetVal = 0x2B, DeclUse = 0x2C, AssignInit = 0x2D;

    private static string Source(ClassBuilder b, out DecompiledClass result)
    {
        result = TufaDecompiler.Decompile(b.Class);
        Assert.True(result.Diagnostics.Clean, string.Join("; ", result.Diagnostics.Errors) + $" gotos={result.Diagnostics.Gotos}");
        return result.Source.Replace("\t", string.Empty, StringComparison.Ordinal);
    }

    [Fact]
    public void IfElseWithShortCircuitAnd()
    {
        var b = new ClassBuilder("java.game.T", "java.lang.Object");
        var body = b.Body(
            (Local, 1, 1), (Op, 0x1C, 1), (Int, 0, 1), (Op, 0x0A, 1), // a > 0
            (SkipT, 0, 1), (Jmp, 6, 1),                              // && (jump to after the operator)
            (Local, 1, 1), (Op, 0x1C, 1), (Int, 10, 1), (Op, 0x0B, 1), (Op, 0x02, 1),
            (JmpF, 6, 1),
            (Op, 0x1C, 2), (Int, 1, 2), (RValue, 0, 2), (RetVal, 0, 2),
            (Jmp, 5, 3),
            (Op, 0x1C, 4), (Int, 2, 4), (RValue, 0, 4), (RetVal, 0, 4),
            (Push0, 0, 5), (RetVal, 0, 5));
        b.Method(0x1, false, "test", "(I)I", body);

        var src = Source(b, out _);
        Assert.Contains("public int test( int i1 )\n{\nif( i1 > 0 && i1 < 10 )\n{\nreturn 1;\n}\nelse\n{\nreturn 2;\n}\n}", src);
    }

    [Fact]
    public void ForLoopWithSwitchBreakAndDefault()
    {
        var b = new ClassBuilder("java.game.T", "java.lang.Object");
        var body = b.Body(
            (DeclLocal, 2, 10), (Type, b.Utf8("I"), 10), (DeclUse, 0, 10), (Op, 0x1C, 10), (Int, 0, 10), (AssignInit, 0, 10),
            (Local, 2, 10), (Local, 1, 10), (Op, 0x0B, 10), (JmpF, 19, 10),               // 6..9
            (Local, 2, 11), (Op, 0x1C, 11), (Int, 1, 11), (Case, 3, 11), (Pop, 0, 11), (Jmp, 6, 11), // 10..15
            (ArgC, 0, 12), (Call, 0, 12), (MName, b.Utf8("foo"), 12), (OpD, 0x1B, 12), (Jmp, 5, 12), // 16..20
            (ArgC, 0, 13), (Call, 0, 13), (MName, b.Utf8("bar"), 13), (OpD, 0x1B, 13),                 // 21..24
            (Local, 2, 10), (OpD, 0x17, 10), (Jmp, -21, 10),                                            // 25..27
            (Push0, 0, 14), (DropLocal, 2, 14), (RetVal, 0, 14));
        b.Method(0x1, false, "loop", "(I)V", body);

        var src = Source(b, out _);
        Assert.Contains("for( int i2 = 0; i2 < i1; i2++ )\n{\nswitch( i2 )\n{\ncase 1:\nfoo();\nbreak;\ndefault:\nbar();\n}\n}", src);
    }

    [Fact]
    public void DoWhileAndConditionalOperator()
    {
        var b = new ClassBuilder("java.game.T", "java.lang.Object");
        b.Field(0, false, "x", "I");
        var x = b.Member("java.game.T", "x", "I");
        var body = b.Body(
            (Field, x, 1), (OpD, 0x19, 1),                                  // ++x;
            (Field, x, 2), (Op, 0x1C, 2), (Int, 5, 2), (Op, 0x0B, 2), (JmpT, -6, 2), // while (x < 5)
            (Field, x, 3), (Op, 0x1C, 3), (Int, 1, 3), (Op, 0x1C, 3), (Int, 2, 3), (Op, 0x00, 3), (RValue, 0, 3), (RetVal, 0, 3));
        b.Method(0x1, false, "f", "()I", body);

        var src = Source(b, out _);
        Assert.Contains("do\n{\n++x;\n}\nwhile( x < 5 );\nreturn x ? 1 : 2;", src);
    }

    [Fact]
    public void FieldsConstructorNativeCastsAndLiterals()
    {
        var b = new ClassBuilder("java.game.T", "java.game.Base");
        var max = b.Member("java.game.T", "MAX", "I");
        var f = b.Member("java.game.T", "f", "F");
        b.Field(0x28, true, "MAX", "I", b.Body((Op, 0x1C, 1), (Int, 5, 1), (EndInit, 0, 1)));
        b.Field(0, false, "names", "[Ljava.lang.String;", b.Body((Field, max, 2), (Op, 0x1F, 2), (Type, b.Utf8("[Ljava.lang.String;"), 2), (EndInit, 0, 2)));
        b.Field(0, false, "f", "F");
        b.Field(0, false, "id", "I", b.Body((Op, 0x1C, 3), (Res, b.Resource("parts\\engines.rpk", 0x52), 3), (EndInit, 0, 3)));
        b.Method(0x41, false, "nat", "(I)I", b.Body());
        b.Method(0x1, false, "<init>", "(I)", b.Body(
            (Local, 1, 5), (ArgC, 1, 5), (SuperInit, 0, 5), (OpD, 0x22, 5),
            (Op, 0x1C, 6), (Float, BitConverter.SingleToInt32Bits(2.5f), 6), (Op, 0x16, 6), (Field, f, 6), (OpD, 0x23, 6),
            (Return, 0, 7), (DropLocal, 1, 7), (DropLocal, 0, 7)));
        b.Method(0x1, false, "set", "(Ljava.lang.Object;)V", b.Body(
            (Op, 0x1C, 9), (Float, BitConverter.SingleToInt32Bits(1f), 9), (Local, 1, 9), (Op, 0x21, 9), (ClassOp, b.ClassRef("java.game.T"), 9),
            (FieldByName, b.Utf8("f"), 9), (OpD, 0x23, 9), (Push0, 0, 10), (RetVal, 0, 10)));

        var src = Source(b, out var result);
        Assert.Contains("public class T extends Base", src);
        Assert.Contains("final static int MAX = 5;", src);
        Assert.Contains("String[] names = new String[MAX];", src);
        Assert.Contains("int id = parts.engines:0x0052r;", src);
        Assert.Contains("public native int nat( int i1 );", src);
        Assert.Contains("public T( int i1 )\n{\nsuper(i1);\nf = -2.5;\n}", src);
        Assert.Contains("((T)object1).f = 1.0;", src);
        Assert.Equal(1, result.NativeBodies);
        Assert.Equal(5, result.DecompiledBodies);
    }

    [Fact]
    public void ParametersUseReversedSlots()
    {
        // SetAgain(float posX2, ..., int pID): the first parameter is the highest slot.
        var b = new ClassBuilder("java.game.T", "java.lang.Object");
        b.Field(0, false, "a", "F");
        var a = b.Member("java.game.T", "a", "F");
        b.Method(0x1, false, "m", "(FI)V", b.Body((Local, 2, 1), (Field, a, 1), (OpD, 0x23, 1), (Push0, 0, 2), (RetVal, 0, 2)));
        var src = Source(b, out _);
        Assert.Contains("public void m( float f2, int i1 )\n{\na = f2;\n}", src);
    }

    [Fact]
    public void UnstructuredJumpIsReported()
    {
        var b = new ClassBuilder("java.game.T", "java.lang.Object");
        b.Method(0x1, false, "m", "()V", b.Body((Jmp, 3, 1), (Push0, 0, 1), (RetVal, 0, 1), (Push0, 0, 2), (RetVal, 0, 2)));
        var r = TufaDecompiler.Decompile(b.Class);
        Assert.Equal(1, r.Diagnostics.Gotos);
        Assert.False(r.Diagnostics.Clean);
    }

    [Theory]
    [InlineData("3.141", 1078527524)]
    [InlineData("0.0035", 0x3B656040)] // Splitter.drag_reduction
    public void GameCompilerFloatParsing(string text, int bits)
    {
        var parsed = FloatLiterals.GameParse(text);
        Assert.NotNull(parsed);
        Assert.Equal(bits, BitConverter.SingleToInt32Bits(parsed!.Value));
        Assert.NotEqual(bits, BitConverter.SingleToInt32Bits(float.Parse(text, System.Globalization.CultureInfo.InvariantCulture)));
        Assert.Equal(text, FloatLiterals.Format(BitConverter.Int32BitsToSingle(bits)));
    }

    [Fact]
    public void GameCompilerIntegerPartWrapsAt32Bits()
    {
        Assert.Equal(2755359744f, FloatLiterals.GameParse("50000000000.0"));
        Assert.Equal("25.0", FloatLiterals.Format(25f));
        Assert.Equal("0.000000079", FloatLiterals.Format(FloatLiterals.GameParse("0.000000079")!.Value));
    }
}
