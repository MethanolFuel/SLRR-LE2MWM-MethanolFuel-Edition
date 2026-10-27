using System.Globalization;
using System.Text;

namespace Slrr.Vm;

/// <summary>
/// Small C <c>printf</c> subset for natives that take a format string (e.g. <c>Float.toString(F, "%1.2f bar")</c>):
/// flags <c>-+ 0#</c>, width, precision, conversions <c>d i u x X o c f F e E g G s %</c>.
/// </summary>
public static class Printf
{
    public static string Format(string format, params object?[] args)
    {
        var sb = new StringBuilder();
        var argi = 0;
        for (var i = 0; i < format.Length; i++)
        {
            var ch = format[i];
            if (ch != '%')
            {
                sb.Append(ch);
                continue;
            }

            if (i + 1 < format.Length && format[i + 1] == '%')
            {
                sb.Append('%');
                i++;
                continue;
            }

            var j = i + 1;
            bool left = false, plus = false, space = false, zero = false, alt = false;
            for (; j < format.Length; j++)
            {
                switch (format[j])
                {
                    case '-': left = true; continue;
                    case '+': plus = true; continue;
                    case ' ': space = true; continue;
                    case '0': zero = true; continue;
                    case '#': alt = true; continue;
                }

                break;
            }

            var width = 0;
            while (j < format.Length && char.IsAsciiDigit(format[j]))
            {
                width = (width * 10) + (format[j++] - '0');
            }

            var precision = -1;
            if (j < format.Length && format[j] == '.')
            {
                j++;
                precision = 0;
                while (j < format.Length && char.IsAsciiDigit(format[j]))
                {
                    precision = (precision * 10) + (format[j++] - '0');
                }
            }

            while (j < format.Length && format[j] is 'l' or 'h')
            {
                j++;
            }

            if (j >= format.Length)
            {
                sb.Append(format, i, format.Length - i);
                break;
            }

            var conv = format[j];
            var arg = argi < args.Length ? args[argi] : null;
            argi++;
            string body;
            var numeric = true;
            switch (conv)
            {
                case 'd' or 'i' or 'u':
                    body = Math.Abs((long)ToDouble(arg)).ToString(CultureInfo.InvariantCulture);
                    if (precision >= 0)
                    {
                        body = body.PadLeft(precision, '0');
                    }

                    body = Sign(ToDouble(arg) < 0, plus, space) + body;
                    break;
                case 'x' or 'X' or 'o':
                    var u = unchecked((uint)(long)ToDouble(arg));
                    body = conv == 'o' ? Convert.ToString(u, 8) : u.ToString(conv == 'x' ? "x" : "X", CultureInfo.InvariantCulture);
                    if (alt && u != 0)
                    {
                        body = (conv == 'o' ? "0" : conv == 'x' ? "0x" : "0X") + body;
                    }

                    break;
                case 'f' or 'F':
                {
                    var d = ToDouble(arg);
                    body = Sign(d < 0 || (d == 0 && double.IsNegative(d)), plus, space)
                           + Math.Abs(d).ToString("F" + (precision < 0 ? 6 : precision).ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
                    break;
                }

                case 'e' or 'E':
                {
                    var d = ToDouble(arg);
                    body = Sign(d < 0, plus, space) + Exp(Math.Abs(d), precision < 0 ? 6 : precision, conv);
                    break;
                }

                case 'g' or 'G':
                {
                    var d = ToDouble(arg);
                    body = Sign(d < 0, plus, space) + General(Math.Abs(d), precision < 0 ? 6 : Math.Max(precision, 1), conv, alt);
                    break;
                }

                case 'c':
                    body = ((char)(int)ToDouble(arg)).ToString();
                    numeric = false;
                    break;
                case 's':
                    body = arg?.ToString() ?? "(null)";
                    if (precision >= 0 && body.Length > precision)
                    {
                        body = body[..precision];
                    }

                    numeric = false;
                    break;
                default:
                    body = format[i..(j + 1)];
                    numeric = false;
                    argi--;
                    break;
            }

            if (body.Length < width)
            {
                if (left)
                {
                    body = body.PadRight(width);
                }
                else if (zero && numeric)
                {
                    var signLen = body.Length > 0 && body[0] is '-' or '+' or ' ' ? 1 : 0;
                    body = body[..signLen] + body[signLen..].PadLeft(width - signLen, '0');
                }
                else
                {
                    body = body.PadLeft(width);
                }
            }

            sb.Append(body);
            i = j;
        }

        return sb.ToString();
    }

    private static string Sign(bool negative, bool plus, bool space) => negative ? "-" : plus ? "+" : space ? " " : string.Empty;

    private static double ToDouble(object? o) => o switch
    {
        int i => i,
        float f => f,
        double d => d,
        long l => l,
        string s => double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : 0,
        _ => 0,
    };

    private static string Exp(double d, int precision, char conv)
    {
        var s = d.ToString((conv == 'E' ? "E" : "e") + precision.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);

        // .NET prints three exponent digits (e+001); C prints at least two.
        var e = s.IndexOfAny(['e', 'E']);
        var mant = s[..(e + 2)];
        var exp = s[(e + 2)..].TrimStart('0');
        return mant + exp.PadLeft(2, '0');
    }

    private static string General(double d, int precision, char conv, bool alt)
    {
        if (d == 0)
        {
            return "0";
        }

        // C takes the exponent X from the %e rendering at precision P-1 (i.e. after rounding).
        var e0 = Exp(d, precision - 1, 'e');
        var x = int.Parse(e0[(e0.IndexOf('e') + 1)..], CultureInfo.InvariantCulture);
        string s;
        if (x < -4 || x >= precision)
        {
            s = Exp(d, precision - 1, conv == 'G' ? 'E' : 'e');
            if (!alt)
            {
                var e = s.IndexOfAny(['e', 'E']);
                var m = s[..e];
                if (m.Contains('.'))
                {
                    m = m.TrimEnd('0').TrimEnd('.');
                }

                s = m + s[e..];
            }
        }
        else
        {
            s = d.ToString("F" + Math.Max(precision - 1 - x, 0).ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
            if (!alt && s.Contains('.'))
            {
                s = s.TrimEnd('0').TrimEnd('.');
            }
        }

        return s;
    }
}
