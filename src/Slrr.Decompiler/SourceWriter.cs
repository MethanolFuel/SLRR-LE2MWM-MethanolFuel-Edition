using System.Text;

namespace Slrr.Decompiler;

/// <summary>Prints statements in the shipped sources' style (tabs, braces on their own line).</summary>
internal sealed class SourceWriter
{
    private readonly StringBuilder _sb = new();

    public override string ToString() => _sb.ToString();

    public void Line(int indent, string text)
    {
        _sb.Append('\t', indent).Append(text).Append('\n');
    }

    public void Blank() => _sb.Append('\n');

    public void Block(int indent, IReadOnlyList<Stmt> stmts)
    {
        Line(indent, "{");
        Statements(indent + 1, stmts);
        Line(indent, "}");
    }

    public void Statements(int indent, IReadOnlyList<Stmt> stmts)
    {
        foreach (var s in stmts)
        {
            Statement(indent, s);
        }
    }

    private void Statement(int indent, Stmt s)
    {
        switch (s)
        {
            case ExprStmt e:
                Line(indent, e.Expression + ";");
                break;
            case DeclStmt d:
                Line(indent, Decl(d) + ";");
                break;
            case ReturnStmt r:
                Line(indent, r.Value is null ? "return;" : "return " + r.Value + ";");
                break;
            case BreakStmt:
                Line(indent, "break;");
                break;
            case ContinueStmt:
                Line(indent, "continue;");
                break;
            case GotoStmt g:
                Line(indent, $"/* goto {g.Target}: {g.Reason} */");
                break;
            case IfStmt i:
                If(indent, i, "if");
                break;
            case WhileStmt w:
                Line(indent, "while( " + (w.Condition?.ToString() ?? "true") + " )");
                Block(indent, w.Body);
                break;
            case DoWhileStmt d:
                Line(indent, "do");
                Block(indent, d.Body);
                Line(indent, "while( " + d.Condition + " );");
                break;
            case ForStmt f:
                Line(indent, "for( " + string.Join(", ", f.Init.Select(Inline)) + "; " + f.Condition + "; "
                    + string.Join(", ", f.Update.Select(Inline)) + " )");
                Block(indent, f.Body);
                break;
            case SwitchStmt sw:
                Line(indent, "switch( " + sw.Value + " )");
                Line(indent, "{");
                foreach (var sec in sw.Sections)
                {
                    foreach (var l in sec.Labels)
                    {
                        Line(indent + 1, l is null ? "default:" : "case " + l + ":");
                    }

                    Statements(indent + 2, sec.Body);
                }

                Line(indent, "}");
                break;
            default:
                Line(indent, "/* ? " + s.GetType().Name + " */");
                break;
        }
    }

    private void If(int indent, IfStmt i, string keyword)
    {
        Line(indent, keyword + "( " + i.Condition + " )");
        Block(indent, i.Then);
        if (i.Else is null)
        {
            return;
        }

        if (i.Else.Count == 1 && i.Else[0] is IfStmt chained)
        {
            If(indent, chained, "else if");
            return;
        }

        Line(indent, "else");
        Block(indent, i.Else);
    }

    private static string Decl(DeclStmt d) =>
        d.Local.Type + " " + d.Local.Name + (d.Init is null ? string.Empty : " = " + d.Init);

    private static string Inline(Stmt s) => s switch
    {
        ExprStmt e => e.Expression.ToString(),
        DeclStmt d => Decl(d),
        _ => "/*?*/",
    };
}
