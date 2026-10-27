using Xunit;

namespace Slrr.Vm.Tests;

/// <summary>Interpreter semantics on hand-assembled classes (no game data needed).</summary>
public class InterpreterTests
{
    private const string T = "test.T";

    private static Vm NewVm(params ClassBuilder[] classes)
    {
        var path = new VmClassPath();
        foreach (var c in classes)
        {
            path.AddClass(c.Build());
        }

        return new Vm(path);
    }

    private static Value CallStatic(Vm vm, string cls, string name, params Value[] args)
    {
        var c = vm.LoadClass(cls);
        return vm.Invoke(c.Methods.Single(m => m.Name == name), null, args);
    }

    [Fact]
    public void ArgumentsOccupySlotsInReverseOrder()
    {
        // static int f(int a, int b) { return (a - b) * 2; }  -> a is slot 1, b is slot 0
        var b = new ClassBuilder(T).Method("f", "(II)I", a => a.Local(1).Local(0).O(Operator.Sub).Int(2).O(Operator.Mul).ReturnValue(), isStatic: true);
        var vm = NewVm(b);
        Assert.Equal(14, CallStatic(vm, T, "f", Value.Int(10), Value.Int(3)).AsInt);
    }

    [Fact]
    public void StringConversionUsesPercentDAndThreeDecimals()
    {
        var b = new ClassBuilder(T).Method("f", "()Ljava.lang.String;", a => a.Str("x").Int(1).O(Operator.Add).Float(2.5f).O(Operator.Add).Null().O(Operator.Add).ReturnValue(), isStatic: true);
        var vm = NewVm(b);
        Assert.Equal("x12.500", vm.GetString(CallStatic(vm, T, "f").AsObject));
    }

    [Fact]
    public void IntFloatPromotionAndStoreCoercion()
    {
        // int i = 7 / 2 (int division); float f = i; f = f / 2;  int j = f * 3 (store truncates) -> 4
        var b = new ClassBuilder(T).Method("f", "()I", a =>
        {
            a.DeclLocal(0, "I", use: true).Int(7).Int(2).O(Operator.Div).Ins(Op.AssignInit);
            a.DeclLocal(1, "F", use: true).Local(0).Ins(Op.AssignInit);
            a.Local(1).Int(2).O(Operator.Div).Local(1).D(Operator.Assign);
            a.DeclLocal(2, "I", use: true).Local(1).Int(3).O(Operator.Mul).Ins(Op.AssignInit);
            a.Local(2).ReturnValue();
        }, isStatic: true);
        Assert.Equal(4, CallStatic(NewVm(b), T, "f").AsInt);
    }

    [Fact]
    public void AndAndShortCircuitsThroughSkipIfTrue()
    {
        // static int f(int a) { return a != 0 && 10 / a > 1; }
        var b = new ClassBuilder(T).Method("f", "(I)I", a =>
        {
            a.Local(0).Int(0).O(Operator.Ne);
            a.Ins(Op.SkipIfTrue);
            var jmp = a.Here;
            a.Ins(Op.Jmp, 0);
            a.Int(10).Local(0).O(Operator.Div).Int(1).O(Operator.Gt);
            a.O(Operator.AndAnd);
            a.Patch(jmp, a.Here);
            a.ReturnValue();
        }, isStatic: true);
        var vm = NewVm(b);
        Assert.Equal(0, CallStatic(vm, T, "f", Value.Int(0)).AsInt);
        Assert.Equal(1, CallStatic(vm, T, "f", Value.Int(3)).AsInt);
        Assert.Equal(0, CallStatic(vm, T, "f", Value.Int(20)).AsInt);
    }

    [Fact]
    public void SwitchUsesCaseAndPop()
    {
        // switch (n) { case 1: return 10; case 2: return 20; default: return 99; }
        var b = new ClassBuilder(T).Method("f", "(I)I", a =>
        {
            a.Local(0);
            a.Int(1);
            var c1 = a.Here;
            a.Ins(Op.Case, 0);
            a.Int(2);
            var c2 = a.Here;
            a.Ins(Op.Case, 0);
            a.Ins(Op.Pop);
            var def = a.Here;
            a.Ins(Op.Jmp, 0);
            a.Patch(c1, a.Here);
            a.Int(10).ReturnValue();
            a.Patch(c2, a.Here);
            a.Int(20).ReturnValue();
            a.Patch(def, a.Here);
            a.Int(99).ReturnValue();
        }, isStatic: true);
        var vm = NewVm(b);
        Assert.Equal(10, CallStatic(vm, T, "f", Value.Int(1)).AsInt);
        Assert.Equal(20, CallStatic(vm, T, "f", Value.Int(2)).AsInt);
        Assert.Equal(99, CallStatic(vm, T, "f", Value.Int(7)).AsInt);
    }

    [Fact]
    public void ArraysLoopsAndLength()
    {
        // int[] a = new int[4]; for (i = 0; i < a.length; i++) a[i] = i * i; return a[3] + a.length;
        var b = new ClassBuilder(T).Method("f", "()I", a =>
        {
            a.DeclLocal(0, "[I", use: true).Int(4).O(Operator.NewArray).Type("[I").Ins(Op.AssignInit);
            a.DeclLocal(1, "I", use: true).Int(0).Ins(Op.AssignInit);
            var top = a.Here;
            a.Local(1).Local(0).FieldByName("length");
            a.O(Operator.Lt);
            var exit = a.Here;
            a.Ins(Op.JmpF, 0);
            a.Local(1).Local(1).O(Operator.Mul).Local(1).Local(0).Ins(Op.Index).D(Operator.Assign);
            a.Local(1).D(Operator.PostInc);
            var back = a.Here;
            a.Ins(Op.Jmp, 0);
            a.Patch(back, top);
            a.Patch(exit, a.Here);
            a.Int(3).Local(0).Ins(Op.Index).Local(0).FieldByName("length").O(Operator.Add).ReturnValue();
        }, isStatic: true);
        var vm = NewVm(b);
        Assert.Equal(13, CallStatic(vm, T, "f").AsInt);
    }

    [Fact]
    public void FieldInitializersConstructorsAndSuperCalls()
    {
        const string Base = "test.Base";
        var baseCls = new ClassBuilder(Base)
            .Field("counter", "I", isStatic: true, init: a => a.Int(100))
            .Field("x", "I", init: a => a.Int(5))
            .Method("<init>", "(I)", a =>
            {
                a.ArgC(0).Ins(Op.SuperInit).D(Operator.VoidMarker);
                a.Local(1).Field(Base, "x", "I").D(Operator.Add + 0x17);      // x += arg  (0x27)
                a.Field(Base, "counter", "I").D(Operator.PostInc);
                a.Ins(Op.Return);
            })
            .Method("describe", "()Ljava.lang.String;", a => a.Str("base").ReturnValue());
        var derived = new ClassBuilder(T, Base)
            .Field("y", "F", init: a => a.Float(1.5f))
            .Method("<init>", "()", a =>
            {
                a.Int(7).ArgC(1).Ins(Op.SuperInit).D(Operator.VoidMarker);
                a.Ins(Op.Return);
            })
            .Method("describe", "()Ljava.lang.String;", a =>
                a.SuperCall(0, "describe").Str("+derived").O(Operator.Add).ReturnValue())
            .Method("make", "()Ljava.lang.String;", a =>
            {
                a.DeclLocal(0, "L" + T + ";", use: true).New(T).ArgC(0).Ins(Op.CallInit).Ins(Op.AssignInit);
                a.CallOnLocal(0, 0, "describe").Ins(Op.RValue);
                a.Ins(Op.RetVal);
            }, isStatic: true);

        var cls = derived.Build();
        var path = new VmClassPath();
        path.AddClass(baseCls.Build());
        path.AddClass(cls);
        var vm = new Vm(path);
        var result = CallStatic(vm, T, "make");
        Assert.Equal("base+derived", vm.GetString(result.AsObject));

        var c = vm.LoadClass(T);
        var obj = vm.Construct(c);
        Assert.Equal(12, obj.Fields[c.FindField("x")!.Slot].AsInt);
        Assert.Equal(1.5f, obj.Fields[c.FindField("y")!.Slot].AsFloat);
        Assert.Equal(102, vm.LoadClass(Base).StaticValues[0].AsInt);
    }

    [Fact]
    public void UnusedAndInvalidOpcodesFailClearly()
    {
        var bad = new ClassBuilder(T)
            .Method("a", "()I", a => a.Ins(0x13).ReturnValue(), isStatic: true)
            .Method("b", "()I", a => a.Ins(0x30).ReturnValue(), isStatic: true)
            .Method("c", "()I", a => a.Int(8).Int(1).O(Operator.UShr).ReturnValue(), isStatic: true)
            .Method("d", "()I", a => a.Ins(Op.SuperField).ReturnValue(), isStatic: true)
            .Method("e", "()I", a => a.Int(1).Ins(Op.Local, 0).D(0x29).ReturnVoid(), isStatic: true);
        var vm = NewVm(bad);
        Assert.Contains("invalid", Assert.Throws<VmOpcodeException>(() => CallStatic(vm, T, "a")).Message);
        Assert.Contains("unknown opcode 0x30", Assert.Throws<VmOpcodeException>(() => CallStatic(vm, T, "b")).Message);
        Assert.Contains(">>>", Assert.Throws<VmOpcodeException>(() => CallStatic(vm, T, "c")).Message);
        Assert.Throws<VmOpcodeException>(() => CallStatic(vm, T, "d"));
        Assert.Contains("unsupported assignmenttype", Assert.Throws<VmOpcodeException>(() => CallStatic(vm, T, "e")).Message);
    }

    [Fact]
    public void ErrorsCarryTheScriptStack()
    {
        var b = new ClassBuilder(T)
            .Method("inner", "(I)I", a =>
            {
                a.Line = 42;
                a.Int(1).Local(0).O(Operator.Div).ReturnValue();
            }, isStatic: true)
            .Method("outer", "()I", a =>
            {
                a.Line = 7;
                a.Int(0).Call(1, T, "inner", "(I)I").ReturnValue();
            }, isStatic: true);
        var ex = Assert.Throws<VmException>(() => CallStatic(NewVm(b), T, "outer"));
        Assert.Contains("division by zero", ex.Message);
        Assert.Equal(["test.T.inner(I)I line 42", "test.T.outer()I line 7"], ex.ScriptTrace);
    }

    [Fact]
    public void MissingNativeIsReportedAndRegisteredNativeIsCalled()
    {
        var b = new ClassBuilder(T)
            .Native("twice", "(I)I", isStatic: true)
            .Native("absent", "()V", isStatic: true)
            .Method("f", "()I", a => a.Int(21).Call(1, T, "twice", "(I)I").ReturnValue(), isStatic: true);
        var path = new VmClassPath();
        path.AddClass(b.Build());
        var natives = NativeRegistry.CreateDefault();
        natives.Register(T, "twice", "(I)I", c => Value.Int(c.Int(0) * 2));
        var vm = new Vm(path, natives);
        Assert.Equal(42, CallStatic(vm, T, "f").AsInt);
        var ex = Assert.Throws<VmNativeMissingException>(() => CallStatic(vm, T, "absent"));
        Assert.Equal("native implementation missing for \"test.T.absent()V\"", ex.Message);
        Assert.Throws<InvalidOperationException>(() => natives.Register(T, "twice", "(I)I", _ => Value.Void));
    }

    [Fact]
    public void ScheduledThreadsSleepOnTheVirtualClock()
    {
        var b = new ClassBuilder(T)
            .Field("done", "I", isStatic: true)
            .Native("nap", "(F)V", isStatic: true)
            .Method("worker", "()V", a =>
            {
                a.Float(2.5f).Call(1, T, "nap", "(F)V").D(Operator.Expr);
                a.Int(1).Field(T, "done", "I").D(Operator.Assign);
                a.ReturnVoid();
            }, isStatic: true);
        var path = new VmClassPath();
        path.AddClass(b.Build());
        var natives = NativeRegistry.CreateDefault();
        natives.Register(T, "nap", "(F)V", c =>
        {
            c.Thread.SleepFor(c.Float(0));
            return Value.Void;
        });
        var vm = new Vm(path, natives);
        var cls = vm.LoadClass(T);
        var t = vm.Spawn(cls.Methods.Single(m => m.Name == "worker"), null, [], "w");
        vm.RunFrame(1f);
        Assert.Equal(VmThreadState.Sleeping, t.State);
        Assert.Equal(0, cls.StaticValues[0].AsInt);
        Assert.Equal(0, vm.RunUntilIdle());
        Assert.Equal(1, cls.StaticValues[0].AsInt);
        Assert.Equal(3.5f, vm.Time); // started in the frame at t=1, slept 2.5 s

        // The same native cannot block inside a synchronous call.
        var vm2 = new Vm(path, natives);
        Assert.Contains("synchronous", Assert.Throws<VmException>(() => CallStatic(vm2, T, "worker")).Message);
    }

    [Fact]
    public void BudgetStopsEndlessLoops()
    {
        var b = new ClassBuilder(T).Method("spin", "()V", a => a.Ins(Op.Jmp, 0), isStatic: true);
        var vm = NewVm(b);
        vm.InstructionBudget = 1000;
        Assert.Throws<VmBudgetExceededException>(() => CallStatic(vm, T, "spin"));
    }

    [Fact]
    public void VerifierAcceptsCompiledShapesAndRejectsImbalance()
    {
        var good = new ClassBuilder(T).Method("f", "(I)I", a => a.Local(0).Int(1).O(Operator.Add).ReturnValue(), isStatic: true);
        var bad = new ClassBuilder("test.U").Method("f", "(I)I", a => a.Local(0).Int(1).Ins(Op.RetVal), isStatic: true);
        var report = new VmVerifyReport();
        VmVerifier.Verify(good.Build(), report);
        Assert.Empty(report.Failures);
        VmVerifier.Verify(bad.Build(), report);
        Assert.Single(report.Failures);
        Assert.Contains("RETVAL with stack depth 2", report.Failures[0]);
    }

    [Theory]
    [InlineData("%1.2f bar", 1.0f, "1.00 bar")]
    [InlineData("%.0f m", 1249.6f, "1250 m")]
    [InlineData("%5.1f|%-5d|", 3.14159f, "  3.1|0    |")]
    [InlineData("%e", 12345.678f, "1.234568e+04")]
    [InlineData("%g", 0.0001f, "0.0001")]
    [InlineData("%x %X %%", 255f, "ff 0 %")]
    public void PrintfSubset(string format, float arg, string expected) =>
        Assert.Equal(expected, Printf.Format(format, arg, 0));

    [Fact]
    public void AtoiAtof()
    {
        Assert.Equal(-42, LangNatives.Atoi("  -42abc"));
        Assert.Equal(0, LangNatives.Atoi("x1"));
        Assert.Equal(2.5f, LangNatives.Atof("2.5 bar"));
        Assert.Equal(0f, LangNatives.Atof("bar"));
    }

    // The following behaviours are inferred from shipped scripts (not read from the exe); see docs/vm.md.

    [Fact]
    public void LogicalOperatorsAcceptFloatOperands()
    {
        // static int f(float a, float b) { return a && b; }   (Block_Inline.updatevariables)
        var b = new ClassBuilder(T).Method("f", "(FF)I", a => a.Local(1).Local(0).O(Operator.AndAnd).ReturnValue(), isStatic: true);
        var vm = NewVm(b);
        Assert.Equal(1, CallStatic(vm, T, "f", Value.Float(0.5f), Value.Float(2f)).AsInt);
        Assert.Equal(0, CallStatic(vm, T, "f", Value.Float(0f), Value.Float(2f)).AsInt);
    }

    [Fact]
    public void InstanceofOnANumberIsFalse()
    {
        var b = new ClassBuilder(T).Method("f", "()I", a => a.Int(5).O(Operator.InstanceOf).Type("Ljava.lang.String;").ReturnValue(), isStatic: true);
        Assert.Equal(0, CallStatic(NewVm(b), T, "f").AsInt);
    }

    [Fact]
    public void NumberAssignedToAStringVariableBecomesItsText()
    {
        // String s = 42;  return s;   -> "42"
        var b = new ClassBuilder(T).Method("f", "()Ljava.lang.String;", a =>
        {
            a.DeclLocal(0, "Ljava.lang.String;", use: true).Int(42).Ins(Op.AssignInit);
            a.Local(0).ReturnValue();
        }, isStatic: true);
        var vm = NewVm(b);
        Assert.Equal("42", vm.GetString(CallStatic(vm, T, "f").AsObject));
    }

    [Fact]
    public void FallingOffTheEndOfANonVoidMethodReturnsTheDefault()
    {
        var b = new ClassBuilder(T)
            .Method("i", "()I", a => { }, isStatic: true)
            .Method("f", "()F", a => { }, isStatic: true)
            .Method("o", "()Ljava.lang.String;", a => { }, isStatic: true);
        var vm = NewVm(b);
        Assert.Equal(0, CallStatic(vm, T, "i").AsInt);
        Assert.Equal(0f, CallStatic(vm, T, "f").AsFloat);
        Assert.True(CallStatic(vm, T, "o").IsNull);
    }

    [Fact]
    public void EqualStringLiteralsAreTheSameObject()
    {
        var b = new ClassBuilder(T).Method("f", "()I", a => a.Str("abc").Str("abc").O(Operator.Eq).ReturnValue(), isStatic: true);
        Assert.Equal(1, CallStatic(NewVm(b), T, "f").AsInt);
    }
}
