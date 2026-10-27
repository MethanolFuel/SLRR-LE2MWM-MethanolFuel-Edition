using Slrr.Formats.Tufa;

namespace Slrr.Vm;

/// <summary>Operators, name paths, member resolution and dispatch.</summary>
public sealed partial class VmThread
{
    /// <summary>The thing a name-path component is applied to.</summary>
    private readonly record struct Base(int Kind, VmClass? Cls, VmObject? Obj)
    {
        public const int ImplicitKind = 0;
        public const int ClassKind = 1;
        public const int ObjectKind = 2;

        public static Base Implicit => new(ImplicitKind, null, null);

        public static Base OfClass(VmClass c) => new(ClassKind, c, null);

        public static Base Of(Value v) =>
            v.Kind == VmKind.Object ? new(ObjectKind, null, v.Ref) : throw new VmException($"field access on primitive value {v.Describe()}");
    }

    // ---------------------------------------------------------------- operators

    private void Operate(VmFrame f, int ip, int code, bool discard)
    {
        Value res;
        switch (code)
        {
            case Operator.Expr:
                if (discard)
                {
                    PopSlot();
                }

                return;
            case Operator.Nop1D:
            case Operator.VoidMarker:
                return;
            case Operator.Literal:
                if (!discard)
                {
                    Push(Literal(f, ip + 1));
                }

                return;
            case Operator.New:
            {
                var desc = TypeAt(f, ip + 1, "new");
                var name = VmTypes.ClassOf(desc) ?? throw new VmException($"new of non-class type {desc}");
                res = Value.Obj(Vm.NewObject(Vm.LoadClass(name)));
                break;
            }

            case Operator.NewArray:
            {
                var desc = TypeAt(f, ip + 1, "newarray");
                var n = PopValue();
                if (n.Kind != VmKind.Int)
                {
                    throw new VmException($"array size {n.Describe()} is not an int");
                }

                res = Value.Obj(Vm.NewArray(desc.StartsWith('[') ? desc[1..] : desc, n.AsInt));
                break;
            }

            case Operator.InstanceOf:
            {
                var desc = TypeAt(f, ip + 1, "instanceof");
                // `x instanceof T` on a number is false rather than an error (TrackSelect.exit).
                var subject = PopValue();
                res = Value.Bool(subject.Kind == VmKind.Object && IsInstance(subject.AsObject, desc));
                break;
            }

            case Operator.Cast:
                res = Cast(f, ip + 1, PopValue());
                break;
            case Operator.Ternary:
            {
                var e = PopValue();
                var t = PopValue();
                res = PopValue().IsTrue ? t : e;
                break;
            }

            case >= Operator.OrOr and <= Operator.Mul:
            {
                var r = PopValue();
                var l = PopValue();
                res = Binary(code, l, r);
                break;
            }

            case Operator.BitNot:
            case Operator.Not:
            case Operator.Neg:
                res = Unary(code, PopValue());
                break;
            case >= Operator.PostInc and <= Operator.PreDec:
            {
                var v = PopVar(Operator.Name(code));
                var old = v.Get();
                var delta = code is Operator.PostInc or Operator.PreInc ? 1 : -1;
                v.Set(old.Kind switch
                {
                    VmKind.Int => Value.Int(unchecked(old.AsInt + delta)),
                    VmKind.Float => Value.Float(old.AsFloat + delta),
                    _ => throw new VmException($"non-numeric operand ({Operator.Name(code)}): {old.Describe()}"),
                });
                res = code is Operator.PostInc or Operator.PostDec ? old : v.Get();
                break;
            }

            case >= Operator.Assign and <= Operator.AssignLast:
            {
                var target = PopVar(Operator.Name(code));
                var v = PopValue();
                if (code != Operator.Assign)
                {
                    v = Binary(CompoundOperator(code), target.Get(), v);
                }

                target.Set(ToStringStorage(target, v));
                res = target.Get();
                break;
            }

            default:
                throw new VmOpcodeException($"operator 0x{code:X2} has no handler");
        }

        if (!discard)
        {
            Push(res);
        }
    }

    private static int CompoundOperator(int code) => code switch
    {
        0x24 => Operator.Mul,
        0x25 => Operator.Div,
        0x26 => Operator.Rem,
        0x27 => Operator.Add,
        0x28 => Operator.Sub,
        0x2C => Operator.And,
        0x2D => Operator.Xor,
        0x2E => Operator.Or,
        _ => throw new VmOpcodeException($"unsupported assignmenttype ({Operator.Name(code)})"),
    };

    private Value Binary(int code, Value l, Value r)
    {
        // String concatenation. The engine's values carry their static type, so a null held in a String variable
        // still concatenates (String.timeToString does `s += h` with s == null). Our values are untyped: a null
        // operand of + is taken to be a String (no other + accepts an object).
        if (code == Operator.Add && (IsString(l) || IsString(r) || l.IsNull || r.IsNull))
        {
            return Value.Obj(Vm.NewString(Vm.ToScriptString(l) + Vm.ToScriptString(r)));
        }

        switch (code)
        {
            case Operator.Eq:
                return Value.Bool(Equal(l, r, "=="));
            case Operator.Ne:
                return Value.Bool(!Equal(l, r, "!="));
            case Operator.OrOr:
            case Operator.AndAnd:
                // Objects count as "non-null" (String.timeToAlphaString evaluates `(flags & X) && s` with a String s).
                // Floats are conditions too (`f1 && f2`: Block_Inline.updatevariables); only void is rejected.
                if (l.Kind is VmKind.Void || r.Kind is VmKind.Void)
                {
                    throw new VmException($"void operand ({Operator.Name(code)})");
                }

                return Value.Bool(code == Operator.OrOr ? l.IsTrue || r.IsTrue : l.IsTrue && r.IsTrue);
        }

        if (l.Kind == VmKind.Int && r.Kind == VmKind.Int)
        {
            int a = l.AsInt, b = r.AsInt;
            return code switch
            {
                Operator.Or => Value.Int(a | b),
                Operator.Xor => Value.Int(a ^ b),
                Operator.And => Value.Int(a & b),
                Operator.Ge => Value.Bool(a >= b),
                Operator.Le => Value.Bool(a <= b),
                Operator.Gt => Value.Bool(a > b),
                Operator.Lt => Value.Bool(a < b),
                Operator.Shr => Value.Int(a >> b),
                Operator.Shl => Value.Int(a << b),
                Operator.Sub => Value.Int(unchecked(a - b)),
                Operator.Add => Value.Int(unchecked(a + b)),
                Operator.Rem => b == 0 ? throw new VmException("division by zero (%)") : Value.Int(b == -1 ? 0 : a % b),
                Operator.Div => b == 0 ? throw new VmException("division by zero (/)") : Value.Int(b == -1 ? unchecked(-a) : a / b),
                Operator.Mul => Value.Int(unchecked(a * b)),
                _ => throw new VmOpcodeException($"operator {Operator.Name(code)} has no handler"),
            };
        }

        if (l.Kind is VmKind.Int or VmKind.Float && r.Kind is VmKind.Int or VmKind.Float)
        {
            float a = l.AsFloat, b = r.AsFloat;
            return code switch
            {
                Operator.Ge => Value.Bool(a >= b),
                Operator.Le => Value.Bool(a <= b),
                Operator.Gt => Value.Bool(a > b),
                Operator.Lt => Value.Bool(a < b),
                Operator.Sub => Value.Float(a - b),
                Operator.Add => Value.Float(a + b),
                Operator.Rem => Value.Float(a % b),
                Operator.Div => Value.Float(a / b),
                Operator.Mul => Value.Float(a * b),
                _ => throw new VmException($"non-integral operands ({Operator.Name(code)})"),
            };
        }

        throw new VmException($"unsupported type ({Operator.Name(code)}): {l.Describe()}, {r.Describe()}");
    }

    private static Value Unary(int code, Value v) => code switch
    {
        Operator.Neg when v.Kind == VmKind.Int => Value.Int(unchecked(-v.AsInt)),
        Operator.Neg when v.Kind == VmKind.Float => Value.Float(-v.AsFloat),
        Operator.Not when v.Kind == VmKind.Int => Value.Bool(v.AsInt == 0),

        // `!f` on a float is `f == 0` (OptionsDialog.osdCommand(CMD_REFRESH_TEXT): `if (!aspectX)`).
        Operator.Not when v.Kind == VmKind.Float => Value.Bool(v.AsFloat == 0f),

        // `!obj` is a null test in shipped code (InventoryItem_Part.getPart: `if (!partXXX)`).
        Operator.Not when v.Kind == VmKind.Object => Value.Bool(v.Ref is null),
        Operator.BitNot when v.Kind == VmKind.Int => Value.Int(~v.AsInt),
        _ => throw new VmException($"non-integral operand ({Operator.Name(code)}): {v.Describe()}"),
    };

    private static bool IsString(Value v) => v.Kind == VmKind.Object && v.Ref?.Native is string;

    /// <summary>The engine's <c>==</c> (0x41c5c0): numeric with promotion, objects by reference (no string contents).</summary>
    private static bool Equal(Value l, Value r, string what)
    {
        if (l.Kind == VmKind.Int && r.Kind == VmKind.Int)
        {
            return l.AsInt == r.AsInt;
        }

        if (l.Kind is VmKind.Int or VmKind.Float && r.Kind is VmKind.Int or VmKind.Float)
        {
            return l.AsFloat == r.AsFloat;
        }

        if (l.Kind == VmKind.Object && r.Kind == VmKind.Object)
        {
            return ReferenceEquals(l.Ref, r.Ref);
        }

        throw new VmException($"unsupported type (_eq) in {what}: {l.Describe()}, {r.Describe()}");
    }

    private static bool IsInstance(VmObject? o, string desc)
    {
        if (o is null)
        {
            return false;
        }

        if (desc.StartsWith('['))
        {
            return o is VmArray a && a.ElementDescriptor == desc[1..];
        }

        var name = VmTypes.ClassOf(desc);
        return name is not null && o.Class.IsSubclassOf(name);
    }

    private Value Cast(VmFrame f, int at, Value v)
    {
        if (at >= f.Code.Length)
        {
            throw new VmException("invalid cast structure");
        }

        var next = f.Code.Ins[at];
        string desc;
        if (next.Op == Op.Class)
        {
            desc = "L" + ResolveClassName(f.Class, next.Arg) + ";";
        }
        else if (next.Op == Op.Type)
        {
            desc = f.Class.Utf8((uint)next.Arg, "cast type");
        }
        else
        {
            throw new VmException("invalid cast structure");
        }

        switch (VmTypes.KindOf(desc))
        {
            case VmKind.Int when v.Kind is VmKind.Int or VmKind.Float:
                return Value.Int(v.AsInt);
            case VmKind.Float when v.Kind is VmKind.Int or VmKind.Float:
                return Value.Float(v.AsFloat);
            case VmKind.Object when v.Kind == VmKind.Object:
                if (v.Ref is null || desc.StartsWith('[') || IsInstance(v.Ref, desc))
                {
                    return v;
                }

                throw new VmException($"cannot cast {v.Ref.Class.Name} to {VmTypes.Pretty(desc)}");
            default:
                throw new VmException($"cannot cast {v.Describe()} to {VmTypes.Pretty(desc)}");
        }
    }

    // ---------------------------------------------------------------- name paths

    /// <summary><c>NAME n</c> outside a call: evaluate n components and leave the resulting Variable.</summary>
    private VmVariable EvalNameVariable(VmFrame f, int start, int count)
    {
        var (v, cls) = EvalComponents(f, start, count);
        return v ?? throw new VmException($"classname found alone ({cls?.Name})");
    }

    private (VmVariable? Var, VmClass? Cls) EvalComponents(VmFrame f, int start, int count)
    {
        VmVariable? v = null;
        VmClass? cls = null;
        for (var k = 0; k < count; k++)
        {
            var c = f.Code.Ins[start + k];
            var b = k == 0 ? Base.Implicit : v is not null ? Base.Of(v.Get()) : Base.OfClass(cls!);
            switch (c.Op)
            {
                case Op.Class when k == 0:
                    cls = ResolveClass(f.Class, c.Arg);
                    v = null;
                    break;
                case Op.Local when k == 0:
                    f.EnsureSlot(c.Arg);
                    v = new LocalVariable(f, c.Arg);
                    break;
                case Op.Field when ArrayLengthRef(f.Class, c.Arg):
                    // `arr.length` compiles to a FIELD ref on the pseudo class "[elem" (e.g. "[java.lang.Object".length:I).
                    v = b.Obj is VmArray arr
                        ? new ConstVariable(Value.Int(arr.Items.Length), "length")
                        : throw new VmException(b.Kind == Base.ObjectKind && b.Obj is null ? "null.length" : "length of a non-array");
                    break;
                case Op.Field:
                    v = FieldVariable(f, ResolveField(f.Class, c.Arg), b);
                    break;
                case Op.MName:
                    v = FieldByName(f, b, f.Class.Utf8((uint)c.Arg, "member name"));
                    break;
                default:
                    throw new VmException($"unexpected {Op.Mnemonic(c.Op)} as component {k + 1} of a name path");
            }
        }

        return (v, cls);
    }

    private VmVariable FieldVariable(VmFrame f, VmField field, Base b)
    {
        if (field.IsStatic)
        {
            Vm.EnsureInitialized(field.Owner);
            return new StaticVariable(field);
        }

        VmObject obj;
        switch (b.Kind)
        {
            case Base.ImplicitKind:
                obj = f.This ?? throw new VmException($"instance field access from static method: {field}");
                break;
            case Base.ClassKind:
                obj = f.This is not null && f.This.Class.IsSubclassOf(b.Cls!)
                    ? f.This
                    : throw new VmException($"instance field access from static context: {field}");
                break;
            default:
                obj = b.Obj ?? throw new VmException($"null.{field.Name}");
                break;
        }

        if (obj.Class.IsSubclassOf(field.Owner))
        {
            return new InstanceVariable(obj, field);
        }

        var byName = obj.Class.FindField(field.Name);
        return byName is null
            ? throw new VmException($"unknown fieldname: {obj.Class.Name}.{field.Name}")
            : FieldVariable(f, byName, Base.Of(Value.Obj(obj)));
    }

    private VmVariable FieldByName(VmFrame f, Base b, string name)
    {
        VmField? field;
        switch (b.Kind)
        {
            case Base.ImplicitKind:
                field = (f.This?.Class ?? f.Class).FindField(name) ?? f.Class.FindField(name);
                if (field is null)
                {
                    throw new VmException($"unknown fieldname: {(f.This?.Class ?? f.Class).Name}.{name}");
                }

                break;
            case Base.ClassKind:
                field = b.Cls!.FindField(name) ?? throw new VmException($"unknown fieldname: {b.Cls.Name}.{name}");
                break;
            default:
                var obj = b.Obj ?? throw new VmException($"null.{name}");
                if (obj is VmArray a && name == "length")
                {
                    return new ConstVariable(Value.Int(a.Items.Length), "length");
                }

                field = obj.Class.FindField(name) ?? throw new VmException($"unknown fieldname: {obj.Class.Name}.{name}");
                break;
        }

        return FieldVariable(f, field, b);
    }

    // ---------------------------------------------------------------- calls

    /// <summary>
    /// Resolves the name path after <c>24 CALL</c>: either a lone <c>1A METHOD</c>/<c>12 MNAME</c> (implicit this or the
    /// current class) or <c>11 NAME k</c> whose first k-1 components give the target and whose last names the method.
    /// </summary>
    private (VmMethod Method, VmObject? Self) ResolveCall(VmFrame f, int at, Value[] args)
    {
        var code = f.Code;
        if (at >= code.Length)
        {
            throw new VmException("script error: CALL without a method");
        }

        Base b;
        VmIns last;
        if (code.Ins[at].Op == Op.Name)
        {
            var k = code.Ins[at].Arg;
            if (k < 1 || at + k >= code.Length)
            {
                throw new VmException("script error: bad CALL name path");
            }

            var (v, cls) = EvalComponents(f, at + 1, k - 1);
            b = k == 1 ? Base.Implicit : v is not null ? Base.Of(v.Get()) : Base.OfClass(cls!);
            last = code.Ins[at + k];
        }
        else
        {
            b = Base.Implicit;
            last = code.Ins[at];
        }

        return last.Op switch
        {
            Op.Method => Dispatch(f, b, ResolveMethod(f.Class, last.Arg), args),
            Op.MName => DispatchByName(f, b, f.Class.Utf8((uint)last.Arg, "method name"), args),
            _ => throw new VmException($"script error: CALL path ends with {Op.Mnemonic(last.Op)}"),
        };
    }

    private (VmMethod, VmObject?) Dispatch(VmFrame f, Base b, VmMethod m, Value[] args)
    {
        if (m.IsStatic)
        {
            return (m, null);
        }

        var self = Receiver(f, b, m.Name, m.Owner);
        var target = self.Class.FindMethod(m.Name, m.Descriptor);
        if (target is null && !self.Class.IsSubclassOf(m.Owner))
        {
            target = Vm.SelectOverload(self.Class.FindMethods(m.Name, args.Length), args);
        }

        return (target ?? m, self);
    }

    private (VmMethod, VmObject?) DispatchByName(VmFrame f, Base b, string name, Value[] args)
    {
        VmClass searchIn = b.Kind switch
        {
            Base.ImplicitKind => f.This?.Class ?? f.Class,
            Base.ClassKind => b.Cls!,
            _ => (b.Obj ?? throw new VmException($"[illegal methodcall] null.{name}()")).Class,
        };
        var m = Vm.SelectOverload(searchIn.FindMethods(name, args.Length), args);
        if (m is null && b.Kind == Base.ImplicitKind && !ReferenceEquals(searchIn, f.Class))
        {
            m = Vm.SelectOverload(f.Class.FindMethods(name, args.Length), args);
        }

        if (m is null)
        {
            throw new VmException($"unknown method {searchIn.Name}.{name}() with {args.Length} arguments");
        }

        return m.IsStatic ? (m, null) : (m, Receiver(f, b, name, m.Owner));
    }

    private static VmObject Receiver(VmFrame f, Base b, string name, VmClass owner) => b.Kind switch
    {
        Base.ImplicitKind => f.This ?? throw new VmException($"instance missing: {owner.Name}.{name}() called from a static method"),
        Base.ClassKind => f.This is not null && f.This.Class.IsSubclassOf(b.Cls!)
            ? f.This
            : throw new VmException($"instance missing: {b.Cls!.Name}.{name}() is not static"),
        _ => b.Obj ?? throw new VmException($"[illegal methodcall] null.{name}()"),
    };

    // ---------------------------------------------------------------- constant pool

    private string ResolveClassName(VmClass cls, int index) =>
        cls.Raw.ClassName((uint)index) ?? throw new VmException($"{cls.Name}: constant #{index} is not a class");

    private VmClass ResolveClass(VmClass cls, int index)
    {
        if (cls.GetResolved((uint)index) is VmClass c)
        {
            return c;
        }

        c = Vm.LoadClass(ResolveClassName(cls, index));
        cls.SetResolved((uint)index, c);
        return c;
    }

    private (string Class, string Name, string Desc) MemberRef(VmClass cls, int index)
    {
        var mr = cls.Constant((uint)index, TufaConstantType.MemberRef);
        var nat = cls.Constant(mr.B, TufaConstantType.NameAndType);
        var owner = cls.Raw.ClassName(mr.A) ?? throw new VmException($"{cls.Name}: member ref #{index} has no class");
        return (owner, cls.Utf8(nat.A, "member name"), cls.Utf8(nat.B, "member descriptor"));
    }

    private bool ArrayLengthRef(VmClass cls, int index)
    {
        if (cls.GetResolved((uint)index) is VmField)
        {
            return false;
        }

        var (owner, name, _) = MemberRef(cls, index);
        return owner.StartsWith('[') && name == "length";
    }

    private VmField ResolveField(VmClass cls, int index)
    {
        if (cls.GetResolved((uint)index) is VmField f)
        {
            return f;
        }

        var (owner, name, _) = MemberRef(cls, index);
        var oc = Vm.LoadClass(owner);
        f = oc.FindField(name) ?? throw new VmException($"unknown fieldname: {owner}.{name}");
        cls.SetResolved((uint)index, f);
        return f;
    }

    private VmMethod ResolveMethod(VmClass cls, int index)
    {
        if (cls.GetResolved((uint)index) is VmMethod m)
        {
            return m;
        }

        var (owner, name, desc) = MemberRef(cls, index);
        var oc = Vm.LoadClass(owner);
        m = oc.FindMethod(name, desc)
            ?? oc.FindMethods(name, VmTypes.ParseMethod(desc).Params.Count).FirstOrDefault()
            ?? throw new VmException($"unknown method {owner}.{name}{desc}");
        cls.SetResolved((uint)index, m);
        return m;
    }
}
