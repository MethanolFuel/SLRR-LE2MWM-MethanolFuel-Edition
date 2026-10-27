namespace Slrr.Decompiler;

/// <summary>A class member as normalised tokens: key "m:name/argc" (methods, constructors) or "f:name" (fields).</summary>
public sealed record MemberTokens(string Key, List<string> Tokens);

/// <summary>A class split into members.</summary>
public sealed record ClassTokens(string Name, List<MemberTokens> Members);

/// <summary>
/// Splits Java-dialect source into classes and members and normalises each member so that a decompiled class
/// and its original source can be compared token by token. Normalisation tolerates the known lossy details:
/// local variable names (renamed by declaration order), modifier order, <c>T a, b;</c> multi-declarations,
/// optional braces around single statements, empty statements, a trailing <c>return;</c> and an implicit
/// <c>super();</c>.
/// </summary>
public static class SourceStructure
{
    public static List<ClassTokens> Parse(string source)
    {
        var t = JavaTokens.Tokenize(source);
        var classes = new List<ClassTokens>();
        var i = 0;
        ParseTop(t, ref i, t.Count, classes);
        return classes;
    }

    private static void ParseTop(List<string> t, ref int i, int end, List<ClassTokens> classes)
    {
        while (i < end)
        {
            if (t[i] is "package" or "import")
            {
                while (i < end && t[i] != ";")
                {
                    i++;
                }

                i++;
                continue;
            }

            if (t[i] is "class" or "interface" && i + 1 < end)
            {
                var name = t[i + 1];
                while (i < end && t[i] != "{")
                {
                    i++;
                }

                var close = Match(t, i, end);
                classes.Add(new ClassTokens(name, ParseMembers(t, i + 1, close, classes)));
                i = close + 1;
                continue;
            }

            i++;
        }
    }

    private static List<MemberTokens> ParseMembers(List<string> t, int start, int end, List<ClassTokens> nested)
    {
        var members = new List<MemberTokens>();
        var i = start;
        while (i < end)
        {
            if (t[i] == ";")
            {
                i++;
                continue;
            }

            // nested class: register it as a class of its own
            var k = i;
            while (k < end && JavaTokens.ModifierWords.Contains(t[k]))
            {
                k++;
            }

            if (k < end && t[k] is "class" or "interface")
            {
                var j = k;
                ParseTop(t, ref j, end, nested);
                i = j;
                continue;
            }

            if (k < end && t[k] == "{")
            {
                // static/instance initializer block
                var close = Match(t, k, end);
                members.Add(new MemberTokens("init:" + members.Count, NormalizeMethod(t.GetRange(i, close + 1 - i))));
                i = close + 1;
                continue;
            }

            // scan to the end of the member
            var depth = 0;
            var isMethod = false;
            var decided = false;
            var e = i;
            for (; e < end; e++)
            {
                var tok = t[e];
                if (!decided && depth == 0 && tok == "=")
                {
                    decided = true;
                }
                else if (!decided && depth == 0 && tok == "(")
                {
                    decided = true;
                    isMethod = true;
                }

                if (tok is "(" or "[")
                {
                    depth++;
                }
                else if (tok is ")" or "]")
                {
                    depth--;
                }
                else if (tok == "{" && depth == 0)
                {
                    if (isMethod)
                    {
                        e = Match(t, e, end);
                        break;
                    }

                    depth++;
                }
                else if (tok == "}" && depth > 0)
                {
                    depth--;
                }
                else if (tok == ";" && depth == 0)
                {
                    break;
                }
            }

            var tokens = t.GetRange(i, Math.Min(e, end - 1) + 1 - i);
            if (isMethod)
            {
                var open = tokens.IndexOf("(");
                var name = open > 0 ? tokens[open - 1] : "?";
                members.Add(new MemberTokens($"m:{name}({ParamTypes(tokens, open)})", NormalizeMethod(tokens)));
            }
            else
            {
                foreach (var f in SplitDeclaration(SortModifiers(tokens)))
                {
                    var nameIdx = f.IndexOf("=");
                    var name = (nameIdx < 0 ? f[^2] : f[nameIdx - 1]);
                    members.Add(new MemberTokens("f:" + name, f));
                }
            }

            i = e + 1;
        }

        return members;
    }

    /// <summary>Parameter types of a method header, e.g. "int,String[]" (each parameter minus its name).</summary>
    private static string ParamTypes(List<string> tokens, int open)
    {
        if (open < 0)
        {
            return "?";
        }

        var close = Match(tokens, open, tokens.Count);
        var types = new List<string>();
        var current = new List<string>();
        for (var i = open + 1; i <= close; i++)
        {
            if (i == close || tokens[i] == ",")
            {
                if (current.Count > 1)
                {
                    types.Add(string.Concat(current.Take(current.Count - 1)));
                }

                current.Clear();
                continue;
            }

            current.Add(tokens[i]);
        }

        return string.Join(",", types);
    }

    private static int Match(List<string> t, int open, int end)
    {
        var o = t[open];
        var c = o switch { "{" => "}", "(" => ")", _ => "]" };
        var depth = 0;
        for (var i = open; i < end; i++)
        {
            if (t[i] == o)
            {
                depth++;
            }
            else if (t[i] == c && --depth == 0)
            {
                return i;
            }
        }

        return end - 1;
    }

    private static List<string> SortModifiers(List<string> tokens)
    {
        var k = 0;
        while (k < tokens.Count && JavaTokens.ModifierWords.Contains(tokens[k]))
        {
            k++;
        }

        var result = tokens.Take(k).Order(StringComparer.Ordinal).ToList();
        result.AddRange(tokens.Skip(k));
        return result;
    }

    /// <summary>Normalises a method (header + body): modifiers, braces, declarations, local names.</summary>
    public static List<string> NormalizeMethod(List<string> raw)
    {
        var tokens = SortModifiers(raw);
        var bodyStart = tokens.IndexOf("{");
        if (bodyStart < 0)
        {
            return RenameLocals(tokens);
        }

        // header (may contain "(" etc.) + normalised body
        var header = tokens.Take(bodyStart).ToList();
        var outp = new List<string>(header);
        var i = bodyStart;
        Statement(tokens, ref i, tokens.Count, outp, bare: false);

        // drop "super ( ) ;" at the start of the body and a trailing "return ;"
        var b = header.Count;
        if (outp.Count > b + 4 && outp[b + 1] == "super" && outp[b + 2] == "(" && outp[b + 3] == ")" && outp[b + 4] == ";")
        {
            outp.RemoveRange(b + 1, 4);
        }

        if (outp.Count >= b + 3 && outp[^1] == "}" && outp[^2] == ";" && outp[^3] == "return")
        {
            outp.RemoveRange(outp.Count - 3, 2);
        }

        return RenameLocals(outp);
    }

    /// <summary>Copies one statement; a bare nested block (<paramref name="bare"/>) loses its braces.</summary>
    private static void Statement(List<string> t, ref int i, int end, List<string> o, bool bare = true)
    {
        if (i >= end)
        {
            return;
        }

        switch (t[i])
        {
            case "{":
                {
                    if (!bare)
                    {
                        o.Add("{");
                    }

                    var close = Match(t, i, end);
                    i++;
                    while (i < close)
                    {
                        Statement(t, ref i, close, o);
                    }

                    if (!bare)
                    {
                        o.Add("}");
                    }

                    i = close + 1;
                    return;
                }

            case ";":
                i++;
                return;
            case "if":
            case "while":
            case "for":
                {
                    var keyword = t[i];
                    o.Add(t[i++]);
                    CopyParens(t, ref i, end, o);
                    Body(t, ref i, end, o);
                    if (keyword == "if" && i < end && t[i] == "else")
                    {
                        o.Add(t[i++]);
                        Body(t, ref i, end, o);
                    }

                    return;
                }

            case "do":
                o.Add(t[i++]);
                Body(t, ref i, end, o);
                Simple(t, ref i, end, o);
                return;
            case "switch":
                {
                    o.Add(t[i++]);
                    CopyParens(t, ref i, end, o);
                    if (i >= end || t[i] != "{")
                    {
                        return;
                    }

                    var close = Match(t, i, end);
                    o.Add("{");
                    i++;
                    while (i < close)
                    {
                        if (t[i] is "case" or "default")
                        {
                            while (i < close && t[i] != ":")
                            {
                                o.Add(t[i++]);
                            }

                            o.Add(":");
                            i++;
                        }
                        else
                        {
                            Statement(t, ref i, close, o);
                        }
                    }

                    o.Add("}");
                    i = close + 1;
                    return;
                }

            default:
                Simple(t, ref i, end, o);
                return;
        }
    }

    private static void Body(List<string> t, ref int i, int end, List<string> o)
    {
        if (i < end && t[i] == "{")
        {
            Statement(t, ref i, end, o, bare: false);
            return;
        }

        o.Add("{");
        Statement(t, ref i, end, o);
        o.Add("}");
    }

    private static void CopyParens(List<string> t, ref int i, int end, List<string> o)
    {
        if (i >= end || t[i] != "(")
        {
            return;
        }

        var close = Match(t, i, end);
        for (; i <= close; i++)
        {
            o.Add(t[i]);
        }
    }

    /// <summary>Copies a simple statement up to its ';', splitting <c>T a, b = x;</c> into two declarations.</summary>
    private static void Simple(List<string> t, ref int i, int end, List<string> o)
    {
        var start = i;
        var depth = 0;
        for (; i < end; i++)
        {
            if (t[i] is "(" or "[" or "{")
            {
                depth++;
            }
            else if (t[i] is ")" or "]" or "}")
            {
                depth--;
            }
            else if (t[i] == ";" && depth <= 0)
            {
                break;
            }
        }

        var stmt = t.GetRange(start, Math.Min(i + 1, end) - start);
        if (stmt.Count == 0 || stmt[^1] != ";")
        {
            stmt.Add(";");
        }

        i = Math.Min(i + 1, end);
        foreach (var part in SplitDeclaration(stmt))
        {
            o.AddRange(part);
        }
    }

    /// <summary>Splits <c>[mods] T a = 1, b;</c> into <c>[mods] T a = 1;</c> and <c>[mods] T b;</c>; other statements pass through.</summary>
    private static IEnumerable<List<string>> SplitDeclaration(List<string> stmt)
    {
        var k = 0;
        while (k < stmt.Count && JavaTokens.ModifierWords.Contains(stmt[k]))
        {
            k++;
        }

        if (k >= stmt.Count || !JavaTokens.IsIdentifier(stmt[k]))
        {
            yield return stmt;
            yield break;
        }

        k++;
        while (k + 1 < stmt.Count && stmt[k] == "[" && stmt[k + 1] == "]")
        {
            k += 2;
        }

        if (k >= stmt.Count || !JavaTokens.IsIdentifier(stmt[k]))
        {
            yield return stmt;
            yield break;
        }

        var prefix = stmt.Take(k).ToList();
        var depth = 0;
        var part = new List<string>(prefix);
        for (var i = k; i < stmt.Count; i++)
        {
            var tok = stmt[i];
            if (tok is "(" or "[" or "{")
            {
                depth++;
            }
            else if (tok is ")" or "]" or "}")
            {
                depth--;
            }

            if (depth == 0 && tok == ",")
            {
                part.Add(";");
                yield return part;
                part = new List<string>(prefix);
                continue;
            }

            part.Add(tok);
        }

        yield return part;
    }

    /// <summary>Renames parameters and locals to $0, $1, ... in declaration order.</summary>
    private static List<string> RenameLocals(List<string> t)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        var next = 0;
        var o = new List<string>(t.Count);
        for (var i = 0; i < t.Count; i++)
        {
            var tok = t[i];
            var prevDot = i > 0 && t[i - 1] == ".";
            if (!prevDot && JavaTokens.IsIdentifier(tok) && i > 0 && IsDeclaredName(t, i))
            {
                map[tok] = "$" + next++;
                o.Add(map[tok]);
                continue;
            }

            o.Add(!prevDot && map.TryGetValue(tok, out var r) ? r : tok);
        }

        return o;
    }

    /// <summary>True when t[i] is the name in "Type name" / "Type[] name" followed by = ; , or ).</summary>
    private static bool IsDeclaredName(List<string> t, int i)
    {
        if (i + 1 >= t.Count || t[i + 1] is not ("=" or ";" or "," or ")"))
        {
            return false;
        }

        var k = i - 1;
        while (k >= 1 && t[k] == "]" && t[k - 1] == "[")
        {
            k -= 2;
        }

        if (k < 0 || !JavaTokens.IsIdentifier(t[k]) || t[k] == "void")
        {
            return false;
        }

        // the type must not itself be the end of an expression (e.g. "a . b c" cannot occur, but "return x" can)
        return k == 0 || t[k - 1] != ".";
    }
}
