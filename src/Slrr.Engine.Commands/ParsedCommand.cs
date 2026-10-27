using System.Globalization;
using System.Numerics;
using Slrr.Engine.Resources;

namespace Slrr.Engine.Commands;

/// <summary>
/// A command string parsed with the exe's sscanf format for the receiving kind (<see cref="CScanf"/>, the C subset the
/// exe's parser 0x551240 uses). <see cref="Fields"/> is the sscanf return value: callers check it the way the exe's
/// handlers do (fewer fields = optional trailing values absent).
/// </summary>
public sealed class ParsedCommand
{
    private ParsedCommand(string text, string verb, string? format, IReadOnlyList<object> values)
    {
        Text = text;
        Verb = verb;
        Format = format;
        Values = values;
    }

    public string Text { get; }

    public string Verb { get; }

    /// <summary>The format used (null for plain verbs).</summary>
    public string? Format { get; }

    public IReadOnlyList<object> Values { get; }

    public int Fields => Values.Count;

    public static string VerbOf(string text) => PartCommands.Verb(text);

    /// <summary>Parses <paramref name="text"/> with the format <paramref name="group"/> applies to its verb.</summary>
    public static ParsedCommand Parse(string text, VerbGroup group)
    {
        var verb = VerbOf(text);
        var format = VerbCatalog.FormatFor(group, verb);
        var values = new List<object>();
        if (format is not null)
        {
            CScanf.Scan(text, format, values);
        }

        return new ParsedCommand(text, verb, format, values);
    }

    public int Int(int i, int fallback = 0) => i < Values.Count && Values[i] is int v ? v : fallback;

    public float Float(int i, float fallback = 0f) => i < Values.Count && Values[i] is float v ? v : fallback;

    public string? Str(int i) => i < Values.Count ? Values[i] as string : null;

    /// <summary>Three consecutive floats as a vector (missing fields keep <paramref name="fallback"/>'s components).</summary>
    public Vector3 Vec(int i, Vector3 fallback = default) =>
        new(Float(i, fallback.X), Float(i + 1, fallback.Y), Float(i + 2, fallback.Z));

    public bool Has(int i) => i < Values.Count;

    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"{Text} => [{string.Join(", ", Values)}]");
}
