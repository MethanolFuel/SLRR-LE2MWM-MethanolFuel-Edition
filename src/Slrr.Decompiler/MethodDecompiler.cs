using System.Globalization;
using System.Text;
using Slrr.Formats.Tufa;

namespace Slrr.Decompiler;

/// <summary>Problems found while decompiling one class.</summary>
public sealed class DecompileDiagnostics
{
    public List<string> Errors { get; } = [];

    /// <summary>Jumps that could not be expressed as structured control flow.</summary>
    public int Gotos { get; set; }

    /// <summary>Opcodes / operator codes the decompiler has no rule for (e.g. "op 1E", "07 0C").</summary>
    public SortedSet<string> Unresolved { get; } = new(StringComparer.Ordinal);

    public bool Clean => Errors.Count == 0 && Gotos == 0;
}

/// <summary>Thrown for a body whose stack code does not match the expected shapes.</summary>
public sealed class DecompileException(string message) : Exception(message);

/// <summary>
/// Turns one TREE body (postfix stack code with prefix markers) into statements: expression trees are
/// rebuilt with a symbolic operand stack; if/else, loops and switch are recovered from the relative jumps
/// (offsets count instructions from the jump itself).
/// </summary>
internal sealed class MethodDecompiler
{
    private readonly TufaClass _cls;
    private readonly IReadOnlyList<TufaInstruction> _ins;
    private readonly DecompileDiagnostics _diag;
    private readonly Dictionary<int, LocalVar> _locals = [];
    private readonly Dictionary<int, List<int>> _backEdges = [];
    private readonly List<Breakable> _ctx = [];
    private readonly HashSet<string> _reserved;

    public MethodDecompiler(TufaClass cls, TufaMethodBody body, DecompileDiagnostics diag, HashSet<string> reservedNames)
    {
        _cls = cls;
        _ins = body.Instructions;
        _diag = diag;
        _reserved = reservedNames;
        for (var i = 0; i < _ins.Count; i++)
        {
            var op = _ins[i].Opcode;
            if ((op == Op.Jmp || op == Op.JmpTrue || op == Op.JmpFalse) && _ins[i].Operand <= 0)
            {
                var t = i + _ins[i].Operand;
                if (!_backEdges.TryGetValue(t, out var list))
                {
                    _backEdges[t] = list = [];
                }

                list.Add(i);
            }
        }
    }

    private sealed record Breakable(int Break, int Continue, bool IsLoop);

    /// <summary>Binds parameter slots (slot 0 = this for instance methods).</summary>
    public List<LocalVar> BindParameters(IReadOnlyList<string> types, bool isStatic)
    {
        var result = new List<LocalVar>();
        // Arguments occupy the slots in reverse order: the last parameter is slot 1 (0 in static methods).
        var first = isStatic ? 0 : 1;
        for (var p = 0; p < types.Count; p++)
        {
            var slot = first + types.Count - 1 - p;
            var v = new LocalVar(slot, types[p], MakeName(types[p], slot));
            _locals[slot] = v;
            result.Add(v);
        }

        return result;
    }

    public bool IsStatic { get; set; }

    /// <summary>Decompiles a method body.</summary>
    public List<Stmt> DecompileMethod()
    {
        var stmts = Block(0, _ins.Count, out var left);
        if (left.Count != 0)
        {
            Error($"{left.Count} value(s) left on the stack at method end");
        }

        // The compiler appends "PUSH0, drops, RETVAL" (or RETURN in constructors) to every body.
        if (stmts.Count > 0 && stmts[^1] is ReturnStmt { Value: null })
        {
            stmts.RemoveAt(stmts.Count - 1);
        }

        return stmts;
    }

    /// <summary>Decompiles a field-initializer body (ends with ENDINIT, value on the stack).</summary>
    public Expr DecompileInitializer()
    {
        var stmts = Block(0, _ins.Count, out var left);
        if (stmts.Count != 0 || left.Count != 1)
        {
            Error($"field initializer: {stmts.Count} statements, {left.Count} stack values");
        }

        return left.Count > 0 ? left.Pop() : new AtomExpr("/*?*/");
    }

    private void Error(string message)
    {
        if (_diag.Errors.Count < 200)
        {
            _diag.Errors.Add(message);
        }
    }

    private List<Stmt> Block(int start, int end, out Stack<Expr> stack)
    {
        var stmts = new List<Stmt>();
        stack = new Stack<Expr>();
        var i = start;
        while (i < end)
        {
            if (stack.Count == 0 && TryLoop(i, end, stmts, out var next))
            {
                i = next;
                continue;
            }

            var before = stmts.Count;
            var at = i;
            i = Step(i, end, stack, stmts);
            for (var k = before; k < stmts.Count; k++)
            {
                if (stmts[k].Line == 0)
                {
                    stmts[k].Line = _ins[at].Line;
                }
            }
        }

        return stmts;
    }

    private List<Stmt> Block(int start, int end)
    {
        var stmts = Block(start, end, out var left);
        if (left.Count != 0)
        {
            Error($"{left.Count} value(s) left on the stack at end of block [{start},{end})");
            foreach (var e in left.Reverse())
            {
                stmts.Add(new ExprStmt(e));
            }
        }

        return stmts;
    }

    private Expr EvalRange(int start, int end)
    {
        var stmts = new List<Stmt>();
        var st = new Stack<Expr>();
        var i = start;
        while (i < end)
        {
            i = Step(i, end, st, stmts);
        }

        if (stmts.Count != 0 || st.Count != 1)
        {
            throw new DecompileException($"expression [{start},{end}) produced {stmts.Count} statements and {st.Count} values");
        }

        return st.Pop();
    }

    // ---------------------------------------------------------------- loops

    private bool TryLoop(int i, int end, List<Stmt> stmts, out int next)
    {
        next = i;
        if (!_backEdges.TryGetValue(i, out var sources))
        {
            return false;
        }

        var b = -1;
        foreach (var s in sources)
        {
            if (s < end && s > b && _ins[s].Opcode != Op.JmpFalse)
            {
                b = s;
            }
        }

        if (b < 0)
        {
            return false;
        }

        var line = _ins[i].Line;
        if (_ins[b].Opcode == Op.JmpTrue)
        {
            _ctx.Add(new Breakable(b + 1, -1, true));
            var body = Block(i, b, out var left);
            _ctx.RemoveAt(_ctx.Count - 1);
            if (left.Count != 1)
            {
                throw new DecompileException($"do-while at {i}: {left.Count} condition values");
            }

            stmts.Add(new DoWhileStmt(body, left.Pop()) { Line = line });
            next = b + 1;
            return true;
        }

        // while/for: the condition is the expression up to the first JMPF that leaves the loop.
        var j = -1;
        Expr? cond = null;
        for (var k = i; k < b; k++)
        {
            if (_ins[k].Opcode == Op.JmpFalse && k + _ins[k].Operand == b + 1)
            {
                j = k;
                break;
            }
        }

        if (j >= 0)
        {
            try
            {
                cond = EvalRange(i, j);
            }
            catch (DecompileException)
            {
                j = -1;
            }
        }

        if (j < 0)
        {
            _ctx.Add(new Breakable(b + 1, i, true));
            var body = Block(i, b);
            _ctx.RemoveAt(_ctx.Count - 1);
            stmts.Add(new WhileStmt(null, body) { Line = line });
            next = b + 1;
            return true;
        }

        // for-update: trailing statements on the condition's line, right before the back jump.
        var u = b;
        while (u - 1 > j && _ins[u - 1].Line == line)
        {
            u--;
        }

        while (u < b && !IsStatementStart(u, j))
        {
            u++;
        }

        List<Stmt> update = [];
        if (u < b)
        {
            _ctx.Add(new Breakable(b + 1, u, true));
            try
            {
                update = Block(u, b);
            }
            catch (DecompileException)
            {
                update = [];
            }

            _ctx.RemoveAt(_ctx.Count - 1);
            if (update.Count == 0 || update.Any(s => s is not ExprStmt))
            {
                update = [];
                u = b;
            }
        }

        _ctx.Add(new Breakable(b + 1, u < b ? u : i, true));
        var loopBody = Block(j + 1, u);
        _ctx.RemoveAt(_ctx.Count - 1);

        List<Stmt> init = [];
        if (stmts.Count > 0 && stmts[^1].Line == line && stmts[^1] is ExprStmt { Expression: AssignExpr } or DeclStmt)
        {
            init.Add(stmts[^1]);
            stmts.RemoveAt(stmts.Count - 1);
        }

        if (init.Count > 0 || update.Count > 0)
        {
            stmts.Add(new ForStmt(init, cond, update, loopBody) { Line = init.Count > 0 ? init[0].Line : line });
        }
        else
        {
            stmts.Add(new WhileStmt(cond, loopBody) { Line = line });
        }

        next = b + 1;
        return true;
    }

    /// <summary>True when <paramref name="k"/> follows a complete statement (or the loop's JMPF at <paramref name="j"/>).</summary>
    private bool IsStatementStart(int k, int j)
    {
        if (k - 1 == j)
        {
            return true;
        }

        var prev = _ins[k - 1].Opcode;
        return prev is Op.OperatorDiscard or Op.DropLocal or Op.AssignInit or Op.Jmp or Op.RetVal or Op.Return;
    }

    private bool IsBreakOrContinue(int target)
    {
        if (_ctx.Count == 0)
        {
            return false;
        }

        if (_ctx[^1].Break == target)
        {
            return true;
        }

        for (var k = _ctx.Count - 1; k >= 0; k--)
        {
            if (_ctx[k].IsLoop)
            {
                return _ctx[k].Continue == target;
            }
        }

        return false;
    }

    // ---------------------------------------------------------------- statements and expressions

    private int Step(int i, int end, Stack<Expr> st, List<Stmt> stmts)
    {
        var ins = _ins[i];
        switch (ins.Opcode)
        {
            case Op.Local:
                st.Push(LocalRef(ins.Operand));
                return i + 1;

            case Op.DeclLocal:
                return DeclareLocal(i, st, stmts);

            case Op.DropLocal:
            case Op.RValue:
                return i + 1;

            case Op.AssignInit:
                {
                    var value = Pop(st, i);
                    if (Pop(st, i) is not DeclTargetExpr target)
                    {
                        throw new DecompileException($"ASSIGNINIT at {i} without a declared local");
                    }

                    stmts.Add(new DeclStmt(target.Local, value));
                    return i + 1;
                }

            case Op.Operator:
                return ApplyOperator(i, st);

            case Op.OperatorDiscard:
                {
                    var code = ins.Operand;
                    if (code == OpCode.Expr || code == OpCode.Void)
                    {
                        stmts.Add(new ExprStmt(Pop(st, i)));
                        return i + 1;
                    }

                    if (code == OpCode.Literal)
                    {
                        return i + 2;
                    }

                    var next = ApplyOperator(i, st);
                    stmts.Add(new ExprStmt(Pop(st, i)));
                    return next;
                }

            case Op.Str:
            case Op.Float:
            case Op.Int:
            case Op.Null:
            case Op.Bool:
            case Op.Res:
                st.Push(Literal(ins));
                return i + 1;

            case Op.Name:
                st.Push(Path(i + 1, ins.Operand, out _));
                return i + 1 + ins.Operand;

            case Op.Field:
                st.Push(new AtomExpr(MemberName(ins.Operand)));
                return i + 1;

            case Op.Class:
                st.Push(new AtomExpr(ClassSimpleName(ins.Operand)));
                return i + 1;

            case Op.FieldByName:
                st.Push(new MemberExpr(Pop(st, i), Utf8(ins.Operand)));
                return i + 1;

            case Op.Index:
                {
                    var array = Pop(st, i);
                    var index = Pop(st, i);
                    st.Push(new IndexExpr(array, index));
                    return i + 1;
                }

            case Op.ArgCount:
                st.Push(new ArgCountExpr(ins.Operand));
                return i + 1;

            case Op.Call:
                return Call(i, st);

            case Op.CallByName:
                {
                    var target = Pop(st, i);
                    var args = PopArgs(st, i);
                    st.Push(new CallExpr(target, Utf8(ins.Operand), args));
                    return i + 1;
                }

            case Op.SuperCall:
                st.Push(new CallExpr(new AtomExpr("super"), Utf8(ins.Operand), PopArgs(st, i)));
                return i + 1;

            case Op.CallInit:
                {
                    var args = PopArgs(st, i);
                    if (Pop(st, i) is not PendingNewExpr pending)
                    {
                        throw new DecompileException($"CALLINIT at {i} without 'new'");
                    }

                    st.Push(new NewExpr(pending.Type, args));
                    return i + 1;
                }

            case Op.SuperInit:
                st.Push(new CtorCallExpr("super", PopArgs(st, i)));
                return i + 1;

            case Op.ThisInit:
                st.Push(new CtorCallExpr("this", PopArgs(st, i)));
                return i + 1;

            case Op.Push0:
                st.Push(new Push0Expr());
                return i + 1;

            case Op.RetVal:
                {
                    var v = Pop(st, i);
                    stmts.Add(new ReturnStmt(v is Push0Expr ? null : v));
                    return i + 1;
                }

            case Op.Return:
                stmts.Add(new ReturnStmt(null));
                return i + 1;

            case Op.EndInit:
                return i + 1;

            case Op.SkipIfTrue:
            case Op.SkipIfFalse:
                return ShortCircuit(i, st);

            case Op.JmpFalse:
                return Conditional(i, end, st, stmts);

            case Op.Jmp:
                return Jump(i, stmts);

            case Op.Case:
                return Switch(i, end, st, stmts);

            case Op.Pop:
                Pop(st, i);
                return i + 1;

            case Op.Type:
                Error($"stray TYPE at {i}");
                return i + 1;

            default:
                _diag.Unresolved.Add($"op {ins.Opcode:X2}");
                Error($"unhandled opcode {ins.Opcode:X2} at {i}");
                st.Push(new AtomExpr($"/*op {ins.Opcode:X2} {ins.Operand}*/"));
                return i + 1;
        }
    }

    private int DeclareLocal(int i, Stack<Expr> st, List<Stmt> stmts)
    {
        var slot = _ins[i].Operand;
        var p = i + 1;
        string? name = null;
        if (p < _ins.Count && _ins[p].Opcode == Op.LocalName)
        {
            name = Utf8(_ins[p].Operand);
            p++;
        }

        if (p >= _ins.Count || _ins[p].Opcode != Op.Type)
        {
            throw new DecompileException($"DECLLOCAL at {i} without TYPE");
        }

        var type = TypeName(_ins[p].Operand);
        p++;
        var local = new LocalVar(slot, type, name ?? MakeName(type, slot));
        _locals[slot] = local;
        if (p < _ins.Count && _ins[p].Opcode == Op.DeclUse)
        {
            st.Push(new DeclTargetExpr(local));
            return p + 1;
        }

        stmts.Add(new DeclStmt(local, null));
        return p;
    }

    private int ApplyOperator(int i, Stack<Expr> st)
    {
        var code = _ins[i].Operand;
        switch (code)
        {
            case OpCode.Expr:
            case OpCode.Void:
                return i + 1;
            case OpCode.Literal:
                st.Push(Literal(Next(i)));
                return i + 2;
            case OpCode.New:
                st.Push(new PendingNewExpr(TypeName(Expect(i, Op.Type).Operand)));
                return i + 2;
            case OpCode.NewArray:
                {
                    var type = TypeName(Expect(i, Op.Type).Operand);
                    var elem = type.EndsWith("[]", StringComparison.Ordinal) ? type[..^2] : type;
                    st.Push(new NewArrayExpr(elem, Pop(st, i)));
                    return i + 2;
                }

            case OpCode.InstanceOf:
                st.Push(new InstanceOfExpr(Pop(st, i), TypeName(Expect(i, Op.Type).Operand)));
                return i + 2;
            case OpCode.Cast:
                {
                    // object casts name a CLASS; primitive casts ((int), (float)) a TYPE descriptor
                    var n = Next(i);
                    var type = n.Opcode switch
                    {
                        Op.Class => ClassSimpleName(n.Operand),
                        Op.Type => TypeName(n.Operand),
                        _ => throw new DecompileException($"cast at {i} followed by opcode {n.Opcode:X2}"),
                    };
                    st.Push(new CastExpr(type, Pop(st, i)));
                    return i + 2;
                }
            case OpCode.Conditional:
                {
                    var b = Pop(st, i);
                    var a = Pop(st, i);
                    var c = Pop(st, i);
                    st.Push(new ConditionalExpr(c, a, b));
                    return i + 1;
                }

            case OpCode.Not:
                st.Push(new UnaryExpr("!", Pop(st, i), false));
                return i + 1;
            case OpCode.BitNot:
                st.Push(new UnaryExpr("~", Pop(st, i), false));
                return i + 1;
            case OpCode.Negate:
                st.Push(new UnaryExpr("-", Pop(st, i), false));
                return i + 1;
            case OpCode.PostInc:
                st.Push(new UnaryExpr("++", Pop(st, i), true));
                return i + 1;
            case OpCode.PostDec:
                st.Push(new UnaryExpr("--", Pop(st, i), true));
                return i + 1;
            case OpCode.PreInc:
                st.Push(new UnaryExpr("++", Pop(st, i), false));
                return i + 1;
            case OpCode.PreDec:
                st.Push(new UnaryExpr("--", Pop(st, i), false));
                return i + 1;
        }

        if (OpCode.Binary(code) is { } bin)
        {
            var r = Pop(st, i);
            var l = Pop(st, i);
            st.Push(new BinaryExpr(bin, l, r));
            return i + 1;
        }

        if (OpCode.Assignment(code) is { } asg)
        {
            var target = Pop(st, i);
            var value = Pop(st, i);
            st.Push(new AssignExpr(asg, target, value));
            return i + 1;
        }

        _diag.Unresolved.Add($"{_ins[i].Opcode:X2} {code:X2}");
        Error($"unknown operator code 0x{code:X2} at {i}");
        st.Push(new AtomExpr($"/*operator 0x{code:X2}*/"));
        return i + 1;
    }

    private int ShortCircuit(int p, Stack<Expr> st)
    {
        var isAnd = _ins[p].Opcode == Op.SkipIfTrue;
        if (p + 1 >= _ins.Count || _ins[p + 1].Opcode != Op.Jmp)
        {
            throw new DecompileException($"SKIPIF at {p} not followed by JMP");
        }

        var t = p + 1 + _ins[p + 1].Operand;
        if (t - 1 <= p + 1 || t > _ins.Count || _ins[t - 1].Opcode != Op.Operator
            || _ins[t - 1].Operand != (isAnd ? OpCode.LogicalAnd : OpCode.LogicalOr))
        {
            throw new DecompileException($"short-circuit at {p}: jump target {t} is not the && / || operator");
        }

        var right = EvalRange(p + 2, t - 1);
        var left = Pop(st, p);
        st.Push(new BinaryExpr(isAnd ? "&&" : "||", left, right));
        return t;
    }

    private int Conditional(int j, int end, Stack<Expr> st, List<Stmt> stmts)
    {
        var cond = Pop(st, j);
        var t = j + _ins[j].Operand;
        if (t <= j || t > end)
        {
            _diag.Gotos++;
            stmts.Add(new GotoStmt(t, "conditional jump out of block") { Line = _ins[j].Line });
            stmts.Add(new ExprStmt(cond));
            return j + 1;
        }

        if (t - 1 > j && _ins[t - 1].Opcode == Op.Jmp)
        {
            var u = t - 1 + _ins[t - 1].Operand;
            var looksLikeBreak = IsBreakOrContinue(u) && _ins[t - 1].Line != _ins[j].Line;
            if (u > t && u <= end && !looksLikeBreak)
            {
                var then = Block(j + 1, t - 1);
                var @else = Block(t, u);
                stmts.Add(new IfStmt(cond, then, @else));
                return u;
            }
        }

        stmts.Add(new IfStmt(cond, Block(j + 1, t), null));
        return t;
    }

    private int Jump(int j, List<Stmt> stmts)
    {
        var t = j + _ins[j].Operand;
        if (_ctx.Count > 0 && _ctx[^1].Break == t)
        {
            stmts.Add(new BreakStmt());
            return j + 1;
        }

        for (var k = _ctx.Count - 1; k >= 0; k--)
        {
            if (_ctx[k].IsLoop)
            {
                if (_ctx[k].Continue == t)
                {
                    stmts.Add(new ContinueStmt());
                    return j + 1;
                }

                break;
            }
        }

        if (t == j + 1)
        {
            // "JMP +1" outside any loop/switch context: jumps to the next instruction (e.g. a switch's
            // final break placed after the enclosing if's end); a no-op
            return j + 1;
        }

        _diag.Gotos++;
        stmts.Add(new GotoStmt(t, "unstructured jump"));
        return j + 1;
    }

    private int Switch(int k, int end, Stack<Expr> st, List<Stmt> stmts)
    {
        var cases = new List<(Expr Value, int Target)>();
        var p = k;
        var scratch = new List<Stmt>();
        while (true)
        {
            if (p >= end)
            {
                throw new DecompileException($"switch at {k}: no POP before block end");
            }

            if (_ins[p].Opcode == Op.Case)
            {
                cases.Add((Pop(st, p), p + _ins[p].Operand));
                p++;
            }
            else if (_ins[p].Opcode == Op.Pop)
            {
                p++;
                break;
            }
            else
            {
                p = Step(p, end, st, scratch);
                if (scratch.Count != 0)
                {
                    throw new DecompileException($"switch at {k}: statement between cases");
                }
            }
        }

        var value = Pop(st, p);
        if (p >= end || _ins[p].Opcode != Op.Jmp)
        {
            throw new DecompileException($"switch at {k}: POP not followed by JMP");
        }

        var dflt = p + _ins[p].Operand;
        var bodyStart = p + 1;
        var e = Math.Max(dflt, cases.Count > 0 ? cases.Max(c => c.Target) : dflt);
        for (var changed = true; changed;)
        {
            changed = false;
            for (var q = bodyStart; q < e; q++)
            {
                if (_ins[q].Opcode == Op.Jmp)
                {
                    var t = q + _ins[q].Operand;
                    if (t > e && t <= end)
                    {
                        e = t;
                        changed = true;
                    }
                }
            }
        }

        if (e > end || cases.Any(c => c.Target < bodyStart))
        {
            throw new DecompileException($"switch at {k}: labels outside [{bodyStart},{end})");
        }

        var labels = cases.Select(c => (Label: (Expr?)c.Value, c.Target)).ToList();
        if (dflt < e)
        {
            labels.Add((null, dflt));
        }

        var positions = labels.Select(l => l.Target).Distinct().Order().ToList();
        var sections = new List<SwitchSection>();
        _ctx.Add(new Breakable(e, -1, false));
        for (var s = 0; s < positions.Count; s++)
        {
            var from = positions[s];
            var to = s + 1 < positions.Count ? positions[s + 1] : e;
            var group = labels.Where(l => l.Target == from).Select(l => l.Label).ToList();
            sections.Add(new SwitchSection(group, Block(from, to)));
        }

        _ctx.RemoveAt(_ctx.Count - 1);
        if (positions.Count > 0 && positions[0] != bodyStart)
        {
            Error($"switch at {k}: unreachable code before the first label");
        }

        stmts.Add(new SwitchStmt(value, sections) { Line = _ins[k].Line });
        return e;
    }

    private int Call(int i, Stack<Expr> st)
    {
        var args = PopArgs(st, i);
        var p = i + 1;
        Expr? target = null;
        if (p < _ins.Count && _ins[p].Opcode == Op.Name)
        {
            var k = _ins[p].Operand;
            if (k < 1)
            {
                throw new DecompileException($"CALL at {i}: empty name path");
            }

            target = k > 1 ? Path(p + 1, k - 1, out _) : null;
            p += k;
        }

        if (p >= _ins.Count)
        {
            throw new DecompileException($"CALL at {i}: missing method");
        }

        var m = _ins[p];
        var name = m.Opcode switch
        {
            Op.Method => MemberName(m.Operand),
            Op.MethodName => Utf8(m.Operand),
            _ => throw new DecompileException($"CALL at {i}: path ends with opcode {m.Opcode:X2}"),
        };
        st.Push(new CallExpr(target, name, args));
        return p + 1;
    }

    /// <summary>Evaluates an n-component name path starting at <paramref name="start"/>.</summary>
    private Expr Path(int start, int count, out int next)
    {
        Expr? e = null;
        next = start + count;
        for (var q = start; q < start + count; q++)
        {
            if (q >= _ins.Count)
            {
                throw new DecompileException("name path runs past the body");
            }

            var c = _ins[q];
            string? name = c.Opcode switch
            {
                Op.Field => MemberName(c.Operand),
                Op.FieldByName or Op.MethodName => Utf8(c.Operand),
                Op.Method => MemberName(c.Operand),
                _ => null,
            };
            if (e is null)
            {
                e = c.Opcode switch
                {
                    Op.Class => new AtomExpr(ClassSimpleName(c.Operand)),
                    Op.Local => LocalRef(c.Operand),
                    _ when name is not null => new AtomExpr(name),
                    _ => throw new DecompileException($"name path component opcode {c.Opcode:X2}"),
                };
            }
            else
            {
                if (name is null)
                {
                    _diag.Unresolved.Add($"path component {c.Opcode:X2}");
                    name = c.Opcode == Op.Class ? ClassSimpleName(c.Operand) : $"/*{c.Opcode:X2}*/";
                }

                e = new MemberExpr(e, name);
            }
        }

        return e ?? throw new DecompileException("empty name path");
    }

    private List<Expr> PopArgs(Stack<Expr> st, int at)
    {
        if (Pop(st, at) is not ArgCountExpr argc)
        {
            throw new DecompileException($"call at {at} without ARGC");
        }

        var args = new Expr[argc.Count];
        for (var k = argc.Count - 1; k >= 0; k--)
        {
            args[k] = Pop(st, at);
        }

        return [.. args];
    }

    private static Expr Pop(Stack<Expr> st, int at) =>
        st.Count > 0 ? st.Pop() : throw new DecompileException($"stack underflow at {at}");

    private TufaInstruction Next(int i) =>
        i + 1 < _ins.Count ? _ins[i + 1] : throw new DecompileException($"operator at {i} needs an operand instruction");

    private TufaInstruction Expect(int i, byte opcode)
    {
        var n = Next(i);
        return n.Opcode == opcode ? n : throw new DecompileException($"expected opcode {opcode:X2} after {i}, found {n.Opcode:X2}");
    }

    // ---------------------------------------------------------------- names and literals

    private Expr LocalRef(int slot)
    {
        if (slot == 0 && !IsStatic)
        {
            return new AtomExpr("this");
        }

        if (_locals.TryGetValue(slot, out var v))
        {
            return new AtomExpr(v.Name);
        }

        Error($"use of undeclared local slot {slot}");
        return new AtomExpr("local" + slot.ToString(CultureInfo.InvariantCulture));
    }

    private string MakeName(string type, int slot)
    {
        var baseType = type.Replace("[]", "s", StringComparison.Ordinal);
        string stem = type switch
        {
            "int" => "i",
            "float" => "f",
            _ => char.ToLowerInvariant(baseType[0]) + baseType[1..],
        };
        var name = stem + slot.ToString(CultureInfo.InvariantCulture);
        while (_reserved.Contains(name))
        {
            name += "_";
        }

        return name;
    }

    private Expr Literal(TufaInstruction lit) => lit.Opcode switch
    {
        // Negative int literals only come from hex constants such as colours (0xFFC0C0C0): "-x" compiles to NEG.
        Op.Int => new AtomExpr(lit.Operand < 0
            ? "0x" + lit.Operand.ToString("X8", CultureInfo.InvariantCulture)
            : lit.Operand.ToString(CultureInfo.InvariantCulture)),
        Op.Float => new AtomExpr(FormatFloat(BitConverter.Int32BitsToSingle(lit.Operand))),
        Op.Str => new AtomExpr(Quote(Utf8(lit.Operand))),
        Op.Null => new AtomExpr("null"),
        Op.Bool => new AtomExpr(lit.Operand != 0 ? "true" : "false"),
        Op.Res => new AtomExpr(Resource(lit.Operand)),
        _ => throw new DecompileException($"opcode {lit.Opcode:X2} is not a literal"),
    };

    private string Resource(int index)
    {
        var c = Constant(index, TufaConstantType.Resource);
        var archive = _cls.Utf8(c.A) ?? "?";
        if (archive.EndsWith(".rpk", StringComparison.OrdinalIgnoreCase))
        {
            archive = archive[..^4];
        }

        return archive.Replace('\\', '.').Replace('/', '.') + ":0x" + c.B.ToString("X4", CultureInfo.InvariantCulture) + "r";
    }

    public static string FormatFloat(float f) => FloatLiterals.Format(f);

    public static string Quote(string s)
    {
        var sb = new StringBuilder("\"");
        foreach (var ch in s)
        {
            switch (ch)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (ch < 0x20)
                    {
                        sb.Append("\\u").Append(((int)ch).ToString("x4", CultureInfo.InvariantCulture));
                    }
                    else
                    {
                        sb.Append(ch);
                    }

                    break;
            }
        }

        return sb.Append('"').ToString();
    }

    private TufaConstant Constant(int index, TufaConstantType type)
    {
        if (index < 0 || index >= _cls.Constants.Count || _cls.Constants[index].Type != type)
        {
            throw new DecompileException($"constant #{index} is not {type}");
        }

        return _cls.Constants[index];
    }

    private string Utf8(int index) =>
        _cls.Utf8(unchecked((uint)index)) ?? throw new DecompileException($"constant #{index} is not a string");

    private string TypeName(int index) => Descriptors.TypeName(Utf8(index));

    private string ClassSimpleName(int index) =>
        Descriptors.SimpleName(_cls.ClassName(unchecked((uint)index)) ?? throw new DecompileException($"constant #{index} is not a class"));

    /// <summary>Name of a field/method reference (CONS type 5 → name-and-type → name).</summary>
    private string MemberName(int index)
    {
        var mr = Constant(index, TufaConstantType.MemberRef);
        var nt = Constant((int)mr.B, TufaConstantType.NameAndType);
        return Utf8((int)nt.A);
    }
}
