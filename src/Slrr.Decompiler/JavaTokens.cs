using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Slrr.Decompiler;

/// <summary>
/// Tokenizer and token-level normaliser for the game's Java dialect, used by the round-trip fidelity check.
/// Comments and whitespace are dropped; literals are canonicalised (numbers by value, strings by content,
/// resource literals <c>pkg:0xIDr</c> case-insensitively by id).
/// </summary>
public static partial class JavaTokens
{
    private static readonly string[] Operators =
    [
        ">>>=",
        "<<=",
        ">>=",
        ">>>",
        "==",
        "!=",
        "<=",
        ">=",
        "&&",
        "||",
        "++",
        "--",
        "+=",
        "-=",
        "*=",
        "/=",
        "%=",
        "&=",
        "|=",
        "^=",
        "<<",
        ">>",
    ];

    public static readonly HashSet<string> Keywords = new(StringComparer.Ordinal)
    {
        "abstract", "break", "case", "catch", "class", "continue", "default", "do", "else", "extends", "final",
        "finally", "for", "if", "implements", "import", "instanceof", "interface", "native", "new", "package",
        "private", "protected", "public", "return", "static", "super", "switch", "synchronized", "this", "throw",
        "throws", "transient", "try", "volatile", "while", "null", "true", "false",
    };

    public static readonly HashSet<string> ModifierWords = new(StringComparer.Ordinal)
    {
        "public", "protected", "private", "static", "final", "abstract", "native", "synchronized", "transient", "volatile",
    };

    [GeneratedRegex(@"\G[A-Za-z_][A-Za-z0-9_.\\]*:0[xX][0-9A-Fa-f]+[rR]")]
    private static partial Regex ResourceRegex();

    [GeneratedRegex(@"\G(0[xX][0-9A-Fa-f]+|\d+\.?\d*([eE][+-]?\d+)?[fFdDlL]?|\.\d+([eE][+-]?\d+)?[fFdD]?)")]
    private static partial Regex NumberRegex();

    public static List<string> Tokenize(string src)
    {
        var tokens = new List<string>();
        var i = 0;
        while (i < src.Length)
        {
            var c = src[i];
            if (char.IsWhiteSpace(c))
            {
                i++;
                continue;
            }

            if (c == '/' && i + 1 < src.Length && src[i + 1] == '/')
            {
                while (i < src.Length && src[i] != '\n')
                {
                    i++;
                }

                continue;
            }

            if (c == '/' && i + 1 < src.Length && src[i + 1] == '*')
            {
                var close = src.IndexOf("*/", i + 2, StringComparison.Ordinal);
                i = close < 0 ? src.Length : close + 2;
                continue;
            }

            if (c == '"' || c == '\'')
            {
                var sb = new StringBuilder();
                var j = i + 1;
                while (j < src.Length && src[j] != c && src[j] != '\n')
                {
                    if (src[j] == '\\' && j + 1 < src.Length)
                    {
                        var n = src[j + 1];
                        sb.Append(n switch { 'n' => '\n', 't' => '\t', 'r' => '\r', '0' => '\0', _ => n });
                        j += 2;
                        continue;
                    }

                    sb.Append(src[j]);
                    j++;
                }

                tokens.Add(c == '"' ? MethodDecompiler.Quote(sb.ToString()) : "'" + sb + "'");
                i = Math.Min(src.Length, j + 1);
                continue;
            }

            if (char.IsLetter(c) || c == '_' || c == '$')
            {
                var m = ResourceRegex().Match(src, i);
                if (m.Success)
                {
                    tokens.Add(CanonicalResource(m.Value));
                    i += m.Length;
                    continue;
                }

                var j = i + 1;
                while (j < src.Length && (char.IsLetterOrDigit(src[j]) || src[j] == '_' || src[j] == '$'))
                {
                    j++;
                }

                tokens.Add(src[i..j]);
                i = j;
                continue;
            }

            if (char.IsDigit(c) || (c == '.' && i + 1 < src.Length && char.IsDigit(src[i + 1])))
            {
                var m = NumberRegex().Match(src, i);
                var len = m.Success ? m.Length : 1;
                tokens.Add(CanonicalNumber(src.Substring(i, len)));
                i += len;
                continue;
            }

            var op = Operators.FirstOrDefault(o => string.CompareOrdinal(src, i, o, 0, o.Length) == 0);
            if (op is not null)
            {
                tokens.Add(op);
                i += op.Length;
                continue;
            }

            tokens.Add(c.ToString());
            i++;
        }

        return tokens;
    }

    private static string CanonicalResource(string text)
    {
        var colon = text.LastIndexOf(':');
        var pkg = text[..colon].Replace('\\', '.').ToLowerInvariant();
        var id = Convert.ToUInt32(text[(colon + 3)..^1], 16);
        return pkg + ":0x" + id.ToString("X4", CultureInfo.InvariantCulture) + "r";
    }

    private static string CanonicalNumber(string text)
    {
        var t = text.ToLowerInvariant();
        // ints are 32-bit in the VM: 0xFFC0C0C0 and 4290822336 both mean -4144960
        if (t.StartsWith("0x", StringComparison.Ordinal))
        {
            return unchecked((int)Convert.ToInt64(t[2..].TrimEnd('l'), 16)).ToString(CultureInfo.InvariantCulture);
        }

        var isFloat = t.Contains('.', StringComparison.Ordinal) || t.Contains('e', StringComparison.Ordinal) || t.EndsWith('f') || t.EndsWith('d');
        var core = t.TrimEnd('f', 'd', 'l');
        if (!isFloat)
        {
            return long.TryParse(core, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v)
                ? unchecked((int)v).ToString(CultureInfo.InvariantCulture)
                : text;
        }

        // canonical form = what the game compiler would store, printed like the decompiler prints it
        if (FloatLiterals.GameParse(core) is { } g)
        {
            return FloatLiterals.Format(g);
        }

        return float.TryParse(core, NumberStyles.Float, CultureInfo.InvariantCulture, out var f) ? FloatLiterals.Format(f) : text;
    }

    public static bool IsIdentifier(string t) =>
        t.Length > 0 && (char.IsLetter(t[0]) || t[0] == '_' || t[0] == '$') && !t.Contains(':', StringComparison.Ordinal) && !Keywords.Contains(t);
}
