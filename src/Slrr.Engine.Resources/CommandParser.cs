using System.Globalization;

namespace Slrr.Engine.Resources;

/// <summary>
/// The subset of C <c>sscanf</c> the engine's command handlers use (exe 0x551240; formats such as
/// <c>%*s %d %d %d %d %d %f %f %f</c>): <c>%d</c>, <c>%f</c>, <c>%s</c>, <c>%Ns</c>, <c>%*…</c> (assignment suppressed),
/// whitespace (matches any amount, also none) and literal characters (must match exactly, e.g. the commas in
/// <c>%f,%f,%f</c>). Returns the number of assigned fields like sscanf; scanning stops at the first mismatch.
/// Measured: the format strings (docs/engine-api/entry-points.md section 5). Number syntax follows the C library
/// (strtol base 10 / strtod) — standard behaviour, not traced in the exe.
/// </summary>
public static class CScanf
{
    public static int Scan(string input, string format, List<object> values)
    {
        var i = 0;
        var assigned = 0;
        for (var f = 0; f < format.Length; f++)
        {
            var fc = format[f];
            if (char.IsWhiteSpace(fc))
            {
                while (i < input.Length && char.IsWhiteSpace(input[i]))
                {
                    i++;
                }

                continue;
            }

            if (fc != '%')
            {
                if (i >= input.Length || input[i] != fc)
                {
                    return assigned;
                }

                i++;
                continue;
            }

            f++;
            if (f >= format.Length)
            {
                return assigned;
            }

            var suppress = false;
            if (format[f] == '*')
            {
                suppress = true;
                f++;
            }

            var width = 0;
            while (f < format.Length && char.IsAsciiDigit(format[f]))
            {
                width = (width * 10) + (format[f] - '0');
                f++;
            }

            if (f >= format.Length)
            {
                return assigned;
            }

            var conv = format[f];
            if (conv == '%')
            {
                if (i >= input.Length || input[i] != '%')
                {
                    return assigned;
                }

                i++;
                continue;
            }

            while (i < input.Length && char.IsWhiteSpace(input[i]))
            {
                i++;
            }

            if (i >= input.Length)
            {
                return assigned;
            }

            var max = width > 0 ? width : int.MaxValue;
            object? v;
            switch (conv)
            {
                case 'd':
                    {
                        var start = i;
                        if (i < input.Length && (input[i] == '-' || input[i] == '+') && i - start < max)
                        {
                            i++;
                        }

                        var digits = i;
                        while (i < input.Length && char.IsAsciiDigit(input[i]) && i - start < max)
                        {
                            i++;
                        }

                        if (i == digits)
                        {
                            return assigned;
                        }

                        v = unchecked((int)long.Clamp(long.Parse(input.AsSpan(start, Math.Min(i - start, 19)), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture), int.MinValue, int.MaxValue));
                        break;
                    }

                case 'f':
                    {
                        var start = i;
                        if (input[i] == '-' || input[i] == '+')
                        {
                            i++;
                        }

                        var mant = i;
                        while (i < input.Length && (char.IsAsciiDigit(input[i]) || input[i] == '.') && i - start < max)
                        {
                            i++;
                        }

                        if (i == mant || input.AsSpan(mant, i - mant).Count('.') > 1 || input.AsSpan(mant, i - mant) is ".")
                        {
                            return assigned;
                        }

                        if (i < input.Length && (input[i] == 'e' || input[i] == 'E') && i - start < max)
                        {
                            var save = i;
                            i++;
                            if (i < input.Length && (input[i] == '-' || input[i] == '+'))
                            {
                                i++;
                            }

                            var ed = i;
                            while (i < input.Length && char.IsAsciiDigit(input[i]))
                            {
                                i++;
                            }

                            if (i == ed)
                            {
                                i = save;
                            }
                        }

                        v = float.Parse(input.AsSpan(start, i - start), NumberStyles.Float, CultureInfo.InvariantCulture);
                        break;
                    }

                case 's':
                    {
                        var start = i;
                        while (i < input.Length && !char.IsWhiteSpace(input[i]) && i - start < max)
                        {
                            i++;
                        }

                        v = input[start..i];
                        break;
                    }

                default:
                    throw new ArgumentException($"unsupported conversion %{conv} in \"{format}\"", nameof(format));
            }

            if (!suppress)
            {
                values.Add(v);
                assigned++;
            }
        }

        return assigned;
    }
}

/// <summary>A parsed part <c>install</c> command.</summary>
/// <param name="Fields">sscanf return value (8 with a position, 5 without).</param>
/// <param name="I1">Parsed, unused by the part handler (scripts pass 0).</param>
/// <param name="CarId">i2: the car / target instance (stored at part+0x60).</param>
/// <param name="I3">Parsed, unused by the handler (Part.load passes the child's own slot id).</param>
/// <param name="TargetPartId">i4: the part the new part is attached to.</param>
/// <param name="SlotId">i5: slot id on the target part, 0 = let the install check (0x473b10) choose.</param>
/// <param name="Position">x, y, z when all 8 fields were parsed, otherwise null (position ignored).</param>
public sealed record InstallCommand(int Fields, int I1, int CarId, int I3, int TargetPartId, int SlotId, (float X, float Y, float Z)? Position);

/// <summary>A parsed part <c>remove</c> command (<c>%*s %d %d</c>, the second int is where the part goes, e.g. the map).</summary>
public sealed record RemoveCommand(int Fields, int First, int DestinationId);

/// <summary>
/// Parsers for the part command strings (exe 0x475f6d.., measured format strings and field use:
/// docs/engine-api/entry-points.md, "Part commands in detail").
/// </summary>
public static class PartCommands
{
    /// <summary>Part handler format (VA 0x6124b4).</summary>
    public const string InstallFormat = "%*s %d %d %d %d %d %f %f %f";

    /// <summary>Car handler format (VA 0x6106b0).</summary>
    public const string CarInstallFormat = "%*s %d %d %d %d %d";

    /// <summary>Part/car <c>remove</c> format (VA 0x6124d8 / 0x6106cc).</summary>
    public const string RemoveFormat = "%*s %d %d";

    /// <summary>The verb (first whitespace-separated token) of a command string, or empty.</summary>
    public static string Verb(string command)
    {
        var t = command.TrimStart();
        var sp = t.IndexOfAny([' ', '\t', '\r', '\n']);
        return sp < 0 ? t : t[..sp];
    }

    /// <summary>
    /// Parses <c>install i1 i2 i3 i4 i5 [x y z]</c>. Returns null when fewer than the five ints are present (the
    /// handler then fails; hypothesis: it requires all five, the exact threshold was not read).
    /// </summary>
    public static InstallCommand? ParseInstall(string command)
    {
        var v = new List<object>();
        var n = CScanf.Scan(command, InstallFormat, v);
        if (n < 5)
        {
            return null;
        }

        (float, float, float)? pos = n >= 8 ? ((float)v[5], (float)v[6], (float)v[7]) : null;
        return new InstallCommand(n, (int)v[0], (int)v[1], (int)v[2], (int)v[3], (int)v[4], pos);
    }

    public static RemoveCommand? ParseRemove(string command)
    {
        var v = new List<object>();
        var n = CScanf.Scan(command, RemoveFormat, v);
        return n < 2 ? null : new RemoveCommand(n, (int)v[0], (int)v[1]);
    }
}
