namespace Slrr.Decompiler;

/// <summary>TREE opcodes, named after docs/research/script-vm-opcodes.md §2.</summary>
internal static class Op
{
    public const byte Local = 0x01;
    public const byte DeclLocal = 0x02;
    public const byte DropLocal = 0x03;
    public const byte SkipIfTrue = 0x04;
    public const byte SkipIfFalse = 0x05;
    public const byte Type = 0x06;
    public const byte Operator = 0x07;
    public const byte OperatorDiscard = 0x08;
    public const byte Str = 0x09;
    public const byte Float = 0x0A;
    public const byte Int = 0x0B;
    public const byte Null = 0x0C;
    public const byte Bool = 0x0D;
    public const byte Res = 0x0E;
    public const byte EndInit = 0x10;
    public const byte Name = 0x11;
    public const byte MethodName = 0x12;
    public const byte Jmp = 0x14;
    public const byte JmpFalse = 0x15;
    public const byte JmpTrue = 0x16;
    public const byte Case = 0x17;
    public const byte Pop = 0x18;
    public const byte Class = 0x19;
    public const byte Method = 0x1A;
    public const byte Field = 0x1B;
    public const byte FieldByName = 0x1C;
    public const byte Index = 0x20;
    public const byte CallInit = 0x21;
    public const byte ThisInit = 0x22;
    public const byte SuperInit = 0x23;
    public const byte Call = 0x24;
    public const byte CallByName = 0x25;
    public const byte SuperCall = 0x26;
    public const byte ArgCount = 0x27;
    public const byte RValue = 0x28;
    public const byte Push0 = 0x29;
    public const byte Return = 0x2A;
    public const byte RetVal = 0x2B;
    public const byte DeclUse = 0x2C;
    public const byte AssignInit = 0x2D;
    public const byte LocalName = 0x2E;
}

/// <summary>Operator codes of <c>07 OP</c> / <c>08 OPDISCARD</c> (script-vm-opcodes.md §3).</summary>
internal static class OpCode
{
    public const int Conditional = 0x00;
    public const int LogicalOr = 0x01;
    public const int LogicalAnd = 0x02;
    public const int Not = 0x15;
    public const int BitNot = 0x14;
    public const int Negate = 0x16;
    public const int PostInc = 0x17;
    public const int PostDec = 0x18;
    public const int PreInc = 0x19;
    public const int PreDec = 0x1A;
    public const int Expr = 0x1B;
    public const int Literal = 0x1C;
    public const int New = 0x1E;
    public const int NewArray = 0x1F;
    public const int InstanceOf = 0x20;
    public const int Cast = 0x21;
    public const int Void = 0x22;
    public const int Assign = 0x23;

    /// <summary>Binary operator text for codes 0x01..0x13 (null = not a binary operator).</summary>
    public static string? Binary(int code) => code switch
    {
        0x01 => "||",
        0x02 => "&&",
        0x03 => "|",
        0x04 => "^",
        0x05 => "&",
        0x06 => "!=",
        0x07 => "==",
        0x08 => ">=",
        0x09 => "<=",
        0x0A => ">",
        0x0B => "<",
        0x0D => ">>",
        0x0E => "<<",
        0x0F => "-",
        0x10 => "+",
        0x11 => "%",
        0x12 => "/",
        0x13 => "*",
        _ => null,
    };

    /// <summary>Assignment operator text for codes 0x23..0x2E (null = not an assignment).</summary>
    public static string? Assignment(int code) => code switch
    {
        0x23 => "=",
        0x24 => "*=",
        0x25 => "/=",
        0x26 => "%=",
        0x27 => "+=",
        0x28 => "-=",
        0x29 => "<<=",
        0x2A => ">>=",
        0x2B => ">>>=",
        0x2C => "&=",
        0x2D => "^=",
        0x2E => "|=",
        _ => null,
    };
}

/// <summary>Descriptor helpers: <c>I</c>, <c>F</c>, <c>V</c>, <c>Ljava.lang.String;</c>, <c>[I</c>, method <c>(IF)V</c>.</summary>
public static class Descriptors
{
    /// <summary>Java-style type name for a field/type descriptor; class names are reduced to their simple name.</summary>
    public static string TypeName(string descriptor)
    {
        var pos = 0;
        return ParseOne(descriptor, ref pos);
    }

    /// <summary>Fully qualified class names referenced by a descriptor (for imports).</summary>
    public static IEnumerable<string> ClassNames(string descriptor)
    {
        var i = 0;
        while (i < descriptor.Length)
        {
            if (descriptor[i] == 'L')
            {
                var end = descriptor.IndexOf(';', i);
                if (end < 0)
                {
                    yield break;
                }

                yield return descriptor[(i + 1)..end];
                i = end + 1;
            }
            else
            {
                i++;
            }
        }
    }

    /// <summary>Splits a method descriptor into parameter type names and the return type (null for constructors).</summary>
    public static (List<string> Params, string? Return) Method(string descriptor)
    {
        var ps = new List<string>();
        var pos = descriptor.StartsWith('(') ? 1 : 0;
        while (pos < descriptor.Length && descriptor[pos] != ')')
        {
            var before = pos;
            ps.Add(ParseOne(descriptor, ref pos));
            if (pos == before)
            {
                pos++;
            }
        }

        pos++;
        string? ret = pos < descriptor.Length ? ParseOne(descriptor, ref pos) : null;
        return (ps, ret);
    }

    public static string SimpleName(string qualified)
    {
        var i = qualified.LastIndexOf('.');
        return i < 0 ? qualified : qualified[(i + 1)..];
    }

    public static string PackageOf(string qualified)
    {
        var i = qualified.LastIndexOf('.');
        return i < 0 ? string.Empty : qualified[..i];
    }

    private static string ParseOne(string d, ref int pos)
    {
        if (pos >= d.Length)
        {
            return "?";
        }

        var c = d[pos++];
        switch (c)
        {
            case 'I': return "int";
            case 'F': return "float";
            case 'V': return "void";
            case 'Z': return "boolean";
            case 'C': return "char";
            case 'B': return "byte";
            case 'S': return "short";
            case 'J': return "long";
            case 'D': return "double";
            case '[': return ParseOne(d, ref pos) + "[]";
            case 'L':
                {
                    var end = d.IndexOf(';', pos);
                    if (end < 0)
                    {
                        end = d.Length;
                    }

                    var name = d[pos..end];
                    pos = Math.Min(d.Length, end + 1);
                    return name.Length == 0 ? "Object" : SimpleName(name);
                }

            default: return "?" + c;
        }
    }
}
