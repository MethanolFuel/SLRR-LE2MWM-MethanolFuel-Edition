using Slrr.Formats.Tufa;

namespace Slrr.Vm;

/// <summary>Outcome of <see cref="VmVerifier"/> over a set of classes.</summary>
public sealed class VmVerifyReport
{
    public int Bodies { get; set; }

    public int MethodBodies { get; set; }

    public int InitializerBodies { get; set; }

    public int UnreferencedBodies { get; set; }

    public int EmptyBodies { get; set; }

    public int Ok { get; set; }

    public long ReachableInstructions { get; set; }

    /// <summary>Instructions read by a preceding instruction (literal, TYPE, CLASS, name-path components, ...).</summary>
    public long ConsumedInstructions { get; set; }

    /// <summary>Instructions neither reachable nor consumed (dead code such as the block-end drops after a return).</summary>
    public long UnreachableInstructions { get; set; }

    /// <summary>Literal instructions (09..0E) reached by the main loop instead of being consumed by <c>07 1C</c>.</summary>
    public long StandaloneLiterals { get; set; }

    public Dictionary<string, int> Terminators { get; } = [];

    public List<string> Failures { get; } = [];
}

/// <summary>
/// Static stack-effect check of TREE bodies under the interpreter's opcode semantics: every path must keep a
/// consistent operand-stack depth at join points, pass exactly the pushed ARGC count to CALL/CALLINIT/..., hand a
/// Variable to assignments, and end with depth 1 at RETVAL/ENDINIT and 0 at RETURN. A corpus that passes this check
/// confirms the stack-shape part of the opcode table (not the arithmetic).
/// </summary>
public static class VmVerifier
{
    private const int V = -2; // value
    private const int L = -1; // variable (lvalue)

    public static void Verify(TufaClass cls, VmVerifyReport report, string? file = null)
    {
        var initBodies = cls.Fields.Where(f => f.Value != uint.MaxValue).Select(f => f.Value).ToHashSet();
        var methodBodies = cls.Methods.Where(m => m.Value != uint.MaxValue).ToDictionary(m => m.Value, m => m);
        for (var b = 0; b < cls.Bodies.Count; b++)
        {
            report.Bodies++;
            var code = new VmCode(b, cls.Bodies[b].Instructions);
            var kind = initBodies.Contains((uint)b) ? "init" : methodBodies.ContainsKey((uint)b) ? "method" : "unreferenced";
            switch (kind)
            {
                case "init": report.InitializerBodies++; break;
                case "method": report.MethodBodies++; break;
                default: report.UnreferencedBodies++; break;
            }

            if (code.Length == 0)
            {
                report.EmptyBodies++;
                report.Ok++;
                continue;
            }

            var err = VerifyBody(cls, code, report);
            if (err is null)
            {
                report.Ok++;
            }
            else
            {
                var what = methodBodies.TryGetValue((uint)b, out var m) ? $"{cls.Utf8(m.NameIndex)}{cls.Utf8(m.DescriptorIndex)}" : $"body {b} ({kind})";
                report.Failures.Add($"{file ?? cls.Name}: {cls.Name}.{what}: {err}");
            }
        }
    }

    private static string? VerifyBody(TufaClass cls, VmCode code, VmVerifyReport report)
    {
        var states = new int[]?[code.Length];
        var work = new Stack<int>();
        states[0] = [];
        work.Push(0);
        string? error = null;
        while (work.Count > 0 && error is null)
        {
            var ip = work.Pop();
            var s = new List<int>(states[ip]!);
            var ins = code.Ins[ip];
            var next = ip + 1 + code.Consumed[ip];
            var succ = new List<(int Target, List<int> Stack)>();
            try
            {
                if (ins.Op is >= Op.Str and <= Op.Res)
                {
                    report.StandaloneLiterals++;
                }

                switch (ins.Op)
                {
                    case Op.Local:
                    case Op.Name:
                    case Op.Field:
                        s.Add(L);
                        break;
                    case Op.DeclLocal:
                        if (code.Consumed[ip] > 0 && code.Ins[ip + code.Consumed[ip]].Op == Op.DeclUse)
                        {
                            s.Add(L);
                        }

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
                        Need(s, 1);
                        succ.Add((ip + 2, new List<int>(s)));
                        break;
                    case Op.Oper:
                    case Op.OperDiscard:
                        OperatorEffect(s, ins.Arg, ins.Op == Op.OperDiscard);
                        break;
                    case Op.Str:
                    case Op.Float:
                    case Op.Int:
                    case Op.Null:
                    case Op.Int2:
                    case Op.Res:
                        s.Add(V);
                        break;
                    case Op.EndInit:
                        if (Terminal(report, "ENDINIT", s, 1) is { } e0)
                        {
                            return e0;
                        }

                        continue;
                    case Op.Return:
                        if (Terminal(report, "RETURN", s, 0) is { } e1)
                        {
                            return e1;
                        }

                        continue;
                    case Op.RetVal:
                        if (Terminal(report, "RETVAL", s, 1) is { } e2)
                        {
                            return e2;
                        }

                        continue;
                    case Op.Jmp:
                        succ.Add((ip + ins.Arg, s));
                        next = -1;
                        break;
                    case Op.JmpF:
                    case Op.JmpT:
                        Pop(s);
                        succ.Add((ip + ins.Arg, new List<int>(s)));
                        break;
                    case Op.Case:
                    {
                        Pop(s);
                        Need(s, 1);
                        var taken = new List<int>(s);
                        Pop(taken);
                        succ.Add((ip + ins.Arg, taken));
                        break;
                    }

                    case Op.Pop:
                        Pop(s);
                        break;
                    case Op.FieldByName:
                        Pop(s);
                        s.Add(L);
                        break;
                    case Op.ArrayInit:
                        for (var i = 0; i < ins.Arg; i++)
                        {
                            Pop(s);
                        }

                        s.Add(V);
                        break;
                    case Op.PushZeros:
                        for (var i = 0; i < ins.Arg; i++)
                        {
                            s.Add(V);
                        }

                        break;
                    case Op.Index:
                        Pop(s);
                        Pop(s);
                        s.Add(L);
                        break;
                    case Op.CallInit:
                        PopArgs(s);
                        Need(s, 1);
                        break;
                    case Op.ThisInit:
                    case Op.SuperInit:
                        PopArgs(s);
                        break;
                    case Op.Call:
                    case Op.SuperCall:
                        PopArgs(s);
                        s.Add(V);
                        break;
                    case Op.CallByName:
                        Pop(s);
                        PopArgs(s);
                        s.Add(V);
                        break;
                    case Op.ArgC:
                        s.Add(ins.Arg);
                        break;
                    case Op.RValue:
                        Pop(s);
                        s.Add(V);
                        break;
                    case Op.Push0:
                        s.Add(V);
                        break;
                    case Op.AssignInit:
                        Pop(s);
                        if (Pop(s) != L)
                        {
                            return $"ASSIGNINIT target is not a variable at {ip}";
                        }

                        break;
                    default:
                        return $"opcode {Op.Mnemonic(ins.Op)} at {ip} has no verifiable semantics";
                }
            }
            catch (InvalidOperationException ex)
            {
                return $"{ex.Message} at {ip} ({Op.Mnemonic(ins.Op)} {ins.Arg})";
            }

            if (next >= 0)
            {
                succ.Add((next, s));
            }

            foreach (var (t, st) in succ)
            {
                if (t == code.Length)
                {
                    report.Terminators["fall-off-end"] = report.Terminators.GetValueOrDefault("fall-off-end") + 1;
                    continue;
                }

                if (t < 0 || t > code.Length)
                {
                    return $"jump from {ip} to {t} outside the body";
                }

                if (states[t] is null)
                {
                    states[t] = [.. st];
                    work.Push(t);
                }
                else if (states[t]!.Length != st.Count)
                {
                    error = $"stack depth {st.Count} from {ip} meets depth {states[t]!.Length} at {t}";
                }
            }
        }

        var consumed = new bool[code.Length];
        var reached = 0;
        for (var i = 0; i < code.Length; i++)
        {
            if (states[i] is not null)
            {
                reached++;
                for (var k = 1; k <= code.Consumed[i]; k++)
                {
                    consumed[i + k] = true;
                }
            }
        }

        report.ReachableInstructions += reached;
        report.ConsumedInstructions += consumed.Count(c => c);
        report.UnreachableInstructions += Enumerable.Range(0, code.Length).Count(i => states[i] is null && !consumed[i]);
        return error;
    }

    private static string? Terminal(VmVerifyReport report, string what, List<int> s, int depth)
    {
        report.Terminators[what] = report.Terminators.GetValueOrDefault(what) + 1;
        return s.Count == depth ? null : $"{what} with stack depth {s.Count} (expected {depth})";
    }

    private static void OperatorEffect(List<int> s, int code, bool discard)
    {
        switch (code)
        {
            case Operator.Expr:
                if (discard)
                {
                    Pop(s);
                }

                return;
            case Operator.Literal:
            case Operator.New:
                if (!discard)
                {
                    s.Add(V);
                }

                return;
            case Operator.Nop1D:
            case Operator.VoidMarker:
                return;
            case Operator.NewArray:
            case Operator.InstanceOf:
            case Operator.Cast:
            case Operator.BitNot:
            case Operator.Not:
            case Operator.Neg:
                Pop(s);
                break;
            case Operator.Ternary:
                Pop(s);
                Pop(s);
                Pop(s);
                break;
            case >= Operator.OrOr and <= Operator.Mul:
                Pop(s);
                Pop(s);
                break;
            case >= Operator.PostInc and <= Operator.PreDec:
                if (Pop(s) != L)
                {
                    throw new InvalidOperationException("increment of a value");
                }

                break;
            case >= Operator.Assign and <= Operator.AssignLast:
                if (Pop(s) != L)
                {
                    throw new InvalidOperationException("assignment to a value");
                }

                Pop(s);
                break;
            default:
                throw new InvalidOperationException($"operator 0x{code:X2} has no handler");
        }

        if (!discard)
        {
            s.Add(V);
        }
    }

    private static void PopArgs(List<int> s)
    {
        var n = Pop(s);
        if (n < 0)
        {
            throw new InvalidOperationException("ARGC count expected on top of the stack");
        }

        for (var i = 0; i < n; i++)
        {
            if (Pop(s) >= 0)
            {
                throw new InvalidOperationException("ARGC count used as an argument");
            }
        }
    }

    private static void Need(List<int> s, int n)
    {
        if (s.Count < n)
        {
            throw new InvalidOperationException("stack underflow");
        }
    }

    private static int Pop(List<int> s)
    {
        Need(s, 1);
        var v = s[^1];
        s.RemoveAt(s.Count - 1);
        return v;
    }
}
