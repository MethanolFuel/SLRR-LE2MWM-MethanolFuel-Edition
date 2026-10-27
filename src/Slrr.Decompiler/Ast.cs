using System.Text;

namespace Slrr.Decompiler;

/// <summary>Expression tree node. <see cref="Precedence"/> drives minimal parenthesisation.</summary>
public abstract record Expr
{
    public const int PrecAssign = 10;
    public const int PrecConditional = 20;
    public const int PrecUnary = 85;
    public const int PrecPostfix = 90;
    public const int PrecPrimary = 100;

    public abstract int Precedence { get; }

    public abstract void Write(StringBuilder sb);

    public sealed override string ToString()
    {
        var sb = new StringBuilder();
        Write(sb);
        return sb.ToString();
    }

    /// <summary>Writes <paramref name="e"/>, in parentheses when it binds looser than <paramref name="min"/>.</summary>
    protected static void Sub(StringBuilder sb, Expr e, int min)
    {
        if (e.Precedence < min)
        {
            sb.Append('(');
            e.Write(sb);
            sb.Append(')');
        }
        else
        {
            e.Write(sb);
        }
    }

    protected static void Args(StringBuilder sb, IReadOnlyList<Expr> args)
    {
        sb.Append('(');
        for (var i = 0; i < args.Count; i++)
        {
            if (i > 0)
            {
                sb.Append(", ");
            }

            Sub(sb, args[i], PrecAssign);
        }

        sb.Append(')');
    }
}

/// <summary>Literal, name, <c>this</c>, class name: anything printed verbatim.</summary>
public sealed record AtomExpr(string Text) : Expr
{
    public override int Precedence => PrecPrimary;

    public override void Write(StringBuilder sb) => sb.Append(Text);
}

public sealed record MemberExpr(Expr Target, string Name) : Expr
{
    public override int Precedence => PrecPrimary;

    public override void Write(StringBuilder sb)
    {
        Sub(sb, Target, PrecPostfix + 1);
        sb.Append('.').Append(Name);
    }
}

public sealed record IndexExpr(Expr Array, Expr Index) : Expr
{
    public override int Precedence => PrecPrimary;

    public override void Write(StringBuilder sb)
    {
        Sub(sb, Array, PrecPostfix + 1);
        sb.Append('[');
        Index.Write(sb);
        sb.Append(']');
    }
}

/// <summary>Method call; <see cref="Target"/> null = unqualified.</summary>
public sealed record CallExpr(Expr? Target, string Name, IReadOnlyList<Expr> Arguments) : Expr
{
    public override int Precedence => PrecPrimary;

    public override void Write(StringBuilder sb)
    {
        if (Target is not null)
        {
            Sub(sb, Target, PrecPostfix + 1);
            sb.Append('.');
        }

        sb.Append(Name);
        Args(sb, Arguments);
    }
}

/// <summary><c>super(...)</c> / <c>this(...)</c> constructor call.</summary>
public sealed record CtorCallExpr(string Keyword, IReadOnlyList<Expr> Arguments) : Expr
{
    public override int Precedence => PrecPrimary;

    public override void Write(StringBuilder sb)
    {
        sb.Append(Keyword);
        Args(sb, Arguments);
    }
}

public sealed record NewExpr(string Type, IReadOnlyList<Expr> Arguments) : Expr
{
    // "new X(...).m()" is valid and is how the shipped sources write it
    public override int Precedence => PrecPrimary;

    public override void Write(StringBuilder sb)
    {
        sb.Append("new ").Append(Type);
        Args(sb, Arguments);
    }
}

public sealed record NewArrayExpr(string ElementType, Expr Length) : Expr
{
    public override int Precedence => PrecPostfix;

    public override void Write(StringBuilder sb)
    {
        sb.Append("new ").Append(ElementType).Append('[');
        Length.Write(sb);
        sb.Append(']');
    }
}

public sealed record CastExpr(string Type, Expr Operand) : Expr
{
    public override int Precedence => PrecUnary;

    public override void Write(StringBuilder sb)
    {
        sb.Append('(').Append(Type).Append(')');
        Sub(sb, Operand, PrecUnary);
    }
}

public sealed record UnaryExpr(string Operator, Expr Operand, bool Postfix) : Expr
{
    public override int Precedence => Postfix ? PrecPostfix : PrecUnary;

    public override void Write(StringBuilder sb)
    {
        if (Postfix)
        {
            Sub(sb, Operand, PrecPostfix);
            sb.Append(Operator);
            return;
        }

        sb.Append(Operator);
        var start = sb.Length;
        Sub(sb, Operand, PrecUnary);

        // "- -x" and "- --x" must not merge into "--".
        if (start < sb.Length && (Operator == "-" || Operator == "+") && sb[start] == Operator[0])
        {
            sb.Insert(start, ' ');
        }
    }
}

public sealed record BinaryExpr(string Operator, Expr Left, Expr Right) : Expr
{
    public override int Precedence => Of(Operator);

    public static int Of(string op) => op switch
    {
        "||" => 30,
        "&&" => 35,
        "|" => 40,
        "^" => 45,
        "&" => 50,
        "==" or "!=" => 55,
        "<" or ">" or "<=" or ">=" or "instanceof" => 60,
        "<<" or ">>" or ">>>" => 65,
        "+" or "-" => 70,
        "*" or "/" or "%" => 75,
        _ => 50,
    };

    public override void Write(StringBuilder sb)
    {
        var p = Precedence;
        Sub(sb, Left, p);
        sb.Append(' ').Append(Operator).Append(' ');
        Sub(sb, Right, p + 1);
    }
}

public sealed record InstanceOfExpr(Expr Operand, string Type) : Expr
{
    public override int Precedence => 60;

    public override void Write(StringBuilder sb)
    {
        Sub(sb, Operand, 60);
        sb.Append(" instanceof ").Append(Type);
    }
}

public sealed record ConditionalExpr(Expr Condition, Expr Then, Expr Else) : Expr
{
    public override int Precedence => PrecConditional;

    public override void Write(StringBuilder sb)
    {
        Sub(sb, Condition, PrecConditional + 1);
        sb.Append(" ? ");
        Sub(sb, Then, PrecConditional + 1);
        sb.Append(" : ");
        Sub(sb, Else, PrecConditional);
    }
}

public sealed record AssignExpr(string Operator, Expr Target, Expr Value) : Expr
{
    public override int Precedence => PrecAssign;

    public override void Write(StringBuilder sb)
    {
        Sub(sb, Target, PrecUnary);
        sb.Append(' ').Append(Operator).Append(' ');
        Sub(sb, Value, PrecAssign);
    }
}

/// <summary>Stack-only marker entries; never printed in valid output.</summary>
internal sealed record ArgCountExpr(int Count) : Expr
{
    public override int Precedence => PrecPrimary;

    public override void Write(StringBuilder sb) => sb.Append("/*argc ").Append(Count).Append("*/");
}

internal sealed record PendingNewExpr(string Type) : Expr
{
    public override int Precedence => PrecPrimary;

    public override void Write(StringBuilder sb) => sb.Append("/*new ").Append(Type).Append("*/");
}

internal sealed record Push0Expr : Expr
{
    public override int Precedence => PrecPrimary;

    public override void Write(StringBuilder sb) => sb.Append('0');
}

internal sealed record DeclTargetExpr(LocalVar Local) : Expr
{
    public override int Precedence => PrecPrimary;

    public override void Write(StringBuilder sb) => sb.Append(Local.Name);
}

/// <summary>A local variable slot binding.</summary>
public sealed class LocalVar(int slot, string type, string name)
{
    public int Slot { get; } = slot;
    public string Type { get; } = type;
    public string Name { get; } = name;
}

/// <summary>Statement node; <see cref="Line"/> is the first source line of its code (0 = unknown).</summary>
public abstract class Stmt
{
    public int Line { get; set; }
}

public sealed class ExprStmt(Expr expression) : Stmt
{
    public Expr Expression { get; } = expression;
}

public sealed class DeclStmt(LocalVar local, Expr? init) : Stmt
{
    public LocalVar Local { get; } = local;
    public Expr? Init { get; } = init;
}

public sealed class ReturnStmt(Expr? value) : Stmt
{
    public Expr? Value { get; } = value;
}

public sealed class IfStmt(Expr condition, List<Stmt> then, List<Stmt>? @else) : Stmt
{
    public Expr Condition { get; } = condition;
    public List<Stmt> Then { get; } = then;
    public List<Stmt>? Else { get; } = @else;
}

public sealed class WhileStmt(Expr? condition, List<Stmt> body) : Stmt
{
    public Expr? Condition { get; } = condition;
    public List<Stmt> Body { get; } = body;
}

public sealed class ForStmt(List<Stmt> init, Expr? condition, List<Stmt> update, List<Stmt> body) : Stmt
{
    public List<Stmt> Init { get; } = init;
    public Expr? Condition { get; } = condition;
    public List<Stmt> Update { get; } = update;
    public List<Stmt> Body { get; } = body;
}

public sealed class DoWhileStmt(List<Stmt> body, Expr condition) : Stmt
{
    public List<Stmt> Body { get; } = body;
    public Expr Condition { get; } = condition;
}

public sealed class SwitchStmt(Expr value, List<SwitchSection> sections) : Stmt
{
    public Expr Value { get; } = value;
    public List<SwitchSection> Sections { get; } = sections;
}

/// <summary>Labels (null entry = <c>default</c>) and the statements under them.</summary>
public sealed class SwitchSection(List<Expr?> labels, List<Stmt> body)
{
    public List<Expr?> Labels { get; } = labels;
    public List<Stmt> Body { get; } = body;
}

public sealed class BreakStmt : Stmt;

public sealed class ContinueStmt : Stmt;

/// <summary>Code that could not be structured: printed as a comment, counted as a failure.</summary>
public sealed class GotoStmt(int target, string reason) : Stmt
{
    public int Target { get; } = target;
    public string Reason { get; } = reason;
}
