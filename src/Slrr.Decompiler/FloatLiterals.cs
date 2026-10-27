using System.Globalization;

namespace Slrr.Decompiler;

/// <summary>
/// Float literals as the game's in-engine compiler produces them. Its decimal parser is not correctly rounded;
/// the shipped corpus is matched by: integer part accumulated in a wrapping uint32 and converted to float, then
/// each fraction digit added in single precision as <c>f += d * scale; scale /= 10</c> (scale starts at 0.1f).
/// Examples: "3.141" → 3.1409998f (one ulp low), "0.0035" → 0.0034999996f, "50000000000.0" → 2755359744f.
/// </summary>
public static class FloatLiterals
{
    /// <summary>Emulates the game compiler on a plain decimal literal (digits, optional '.', optional digits, optional f).</summary>
    public static float? GameParse(string text)
    {
        var t = text.TrimEnd('f', 'F', 'd', 'D');
        if (t.Length == 0 || t.Contains('e', StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        uint ip = 0;
        var i = 0;
        for (; i < t.Length && t[i] != '.'; i++)
        {
            if (!char.IsAsciiDigit(t[i]))
            {
                return null;
            }

            ip = unchecked((ip * 10) + (uint)(t[i] - '0'));
        }

        var f = (float)ip;
        var scale = 0.1f;
        for (i++; i < t.Length; i++)
        {
            if (!char.IsAsciiDigit(t[i]))
            {
                return null;
            }

            f += (t[i] - '0') * scale;
            scale /= 10;
        }

        return f;
    }

    /// <summary>
    /// Shortest plain decimal (always with a '.') that the game compiler turns into exactly <paramref name="f"/>;
    /// falls back to the shortest correctly rounded representation.
    /// </summary>
    public static string Format(float f)
    {
        if (float.IsNaN(f) || float.IsInfinity(f))
        {
            return "0.0/*" + f.ToString(CultureInfo.InvariantCulture) + "*/";
        }

        var neg = f < 0 || (f == 0 && float.IsNegative(f));
        var a = Math.Abs(f);
        var bits = BitConverter.SingleToInt32Bits(a);
        string? best = null;
        for (var p = 1; p <= 9 && best is null; p++)
        {
            var rounded = Plain(a.ToString("G" + p.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture));
            foreach (var candidate in Neighbours(rounded))
            {
                if (GameParse(candidate) is { } g && BitConverter.SingleToInt32Bits(g) == bits)
                {
                    best = candidate;
                    break;
                }
            }
        }

        best ??= Plain(a.ToString("R", CultureInfo.InvariantCulture));
        if (!best.Contains('.', StringComparison.Ordinal))
        {
            best += ".0";
        }

        return (neg ? "-" : string.Empty) + best;
    }

    /// <summary>The rounded string and the strings one unit higher/lower in its last digit.</summary>
    private static IEnumerable<string> Neighbours(string s)
    {
        yield return s;
        if (!decimal.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
        {
            yield break;
        }

        var dot = s.IndexOf('.');
        var decimals = dot < 0 ? 0 : s.Length - dot - 1;
        var unit = 1m;
        for (var k = 0; k < decimals; k++)
        {
            unit /= 10;
        }

        foreach (var n in new[] { d + unit, d - unit })
        {
            if (n > 0)
            {
                yield return n.ToString("F" + decimals.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
            }
        }
    }

    /// <summary>Expands exponent notation ("1.5E-05") into a plain decimal.</summary>
    private static string Plain(string s)
    {
        var e = s.IndexOfAny(['E', 'e']);
        if (e < 0)
        {
            return s;
        }

        var mant = s[..e];
        var exp = int.Parse(s[(e + 1)..], CultureInfo.InvariantCulture);
        var dot = mant.IndexOf('.');
        var digits = mant.Replace(".", string.Empty, StringComparison.Ordinal);
        var pointPos = (dot < 0 ? mant.Length : dot) + exp;
        if (pointPos <= 0)
        {
            return "0." + new string('0', -pointPos) + digits;
        }

        return pointPos >= digits.Length
            ? digits + new string('0', pointPos - digits.Length)
            : digits[..pointPos] + "." + digits[pointPos..];
    }
}
