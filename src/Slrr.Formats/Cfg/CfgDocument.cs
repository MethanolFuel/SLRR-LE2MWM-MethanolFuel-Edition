using System.Text;
using Slrr.Core;

namespace Slrr.Formats.Cfg;

public enum CfgLineKind
{
    Blank,
    Comment,

    /// <summary>A directive commented out with a leading <c>#</c> (e.g. <c>#render 0x229</c>) or a <c>#</c> banner line.</summary>
    Disabled,
    Section,
    Directive,
    CompoundStart,
    CompoundEnd,
    Eof,

    /// <summary>Any line after <c>eof</c>; the engine is assumed to ignore it.</summary>
    AfterEof,
}

/// <summary>One physical line of a cfg file.</summary>
public sealed record CfgLine(int Number, string Raw, CfgLineKind Kind, string? Keyword, IReadOnlyList<string> Args, string? Comment, int Depth);

/// <summary>
/// Parsed cfg document: a flat list of lines (lossless: <see cref="CfgLine.Raw"/> keeps the original text)
/// plus structural diagnostics. Grammar (derived from all 1,182 shipped files, see docs/formats/cfg.md):
/// <code>
/// file      := line* ('eof' line*)?
/// line      := ws* (directive | section | '#' text | ) ws* comment? EOL
/// comment   := (';' | '//') text
/// directive := keyword (ws+ arg)*        ; arg = any run of non-whitespace
/// section   := '[' name ']'              ; particle cfgs only ([STATE], [ACTION])
/// compound  := 'compound' line* 'endcompound' arg?   ; may nest (depth 2 seen)
/// </code>
/// </summary>
public sealed class CfgDocument
{
    private static readonly char[] Separators = [' ', '\t'];

    public required string Path { get; init; }
    public required IReadOnlyList<CfgLine> Lines { get; init; }
    public required IReadOnlyList<Diagnostic> Diagnostics { get; init; }
    public bool HasEof { get; init; }

    public IEnumerable<CfgLine> Directives => Lines.Where(l => l.Kind == CfgLineKind.Directive);

    public bool HasErrors => Diagnostics.Any(d => d.Severity == Severity.Error);

    public static CfgDocument Load(string path, string? displayPath = null) =>
        Parse(File.ReadAllBytes(path), displayPath ?? path);

    public static CfgDocument Parse(ReadOnlySpan<byte> bytes, string path)
    {
        var diags = new List<Diagnostic>();
        var text = Encoding.Latin1.GetString(bytes);
        if (text.Length > 0 && text[0] == '﻿')
        {
            text = text[1..];
        }

        var lines = new List<CfgLine>();
        var depth = 0;
        var sawEof = false;
        var openLines = new Stack<int>();
        var raw = text.Split('\n');
        var count = raw.Length;
        if (count > 0 && raw[^1].Length == 0)
        {
            count--; // trailing newline does not start a new line
        }

        for (var i = 0; i < count; i++)
        {
            var number = i + 1;
            var line = raw[i].TrimEnd('\r');
            var (code, comment) = SplitComment(line);
            var trimmed = code.Trim(' ', '\t');

            if (trimmed.Any(c => c < ' ' && c != '\t'))
            {
                diags.Add(new(Severity.Error, "CFG001", "control/binary character in line", path, number));
            }

            if (sawEof)
            {
                if (trimmed.Length > 0)
                {
                    diags.Add(new(Severity.Info, "CFG012", "text after eof is ignored", path, number));
                }

                lines.Add(new(number, line, CfgLineKind.AfterEof, null, [], comment, depth));
                continue;
            }

            if (trimmed.Length == 0)
            {
                lines.Add(new(number, line, comment is null ? CfgLineKind.Blank : CfgLineKind.Comment, null, [], comment, depth));
                continue;
            }

            if (trimmed[0] == '#')
            {
                var rest = Tokenize(trimmed[1..]);
                lines.Add(new(number, line, CfgLineKind.Disabled, rest.Count > 0 ? rest[0] : null, rest.Skip(1).ToArray(), comment, depth));
                continue;
            }

            if (trimmed[0] == '[')
            {
                if (trimmed[^1] != ']' || trimmed.Length < 3)
                {
                    diags.Add(new(Severity.Error, "CFG002", $"malformed section header '{trimmed}'", path, number));
                }

                lines.Add(new(number, line, CfgLineKind.Section, trimmed.Trim('[', ']'), [], comment, depth));
                continue;
            }

            var tokens = Tokenize(trimmed);
            var keyword = tokens[0];
            var args = tokens.Skip(1).ToArray();
            switch (keyword)
            {
                case "compound":
                    lines.Add(new(number, line, CfgLineKind.CompoundStart, keyword, args, comment, depth));
                    openLines.Push(number);
                    depth++;
                    break;
                case "endcompound":
                    if (depth == 0)
                    {
                        diags.Add(new(Severity.Error, "CFG010", "endcompound without matching compound", path, number));
                    }
                    else
                    {
                        depth--;
                        openLines.Pop();
                    }

                    lines.Add(new(number, line, CfgLineKind.CompoundEnd, keyword, args, comment, depth));
                    break;
                case "eof":
                    sawEof = true;
                    lines.Add(new(number, line, CfgLineKind.Eof, keyword, args, comment, depth));
                    break;
                default:
                    if (!CfgKeywords.Known.Contains(keyword))
                    {
                        diags.Add(new(Severity.Warning, "CFG100", $"unknown keyword '{keyword}'", path, number));
                    }

                    foreach (var a in args)
                    {
                        if (a.StartsWith("0x", StringComparison.OrdinalIgnoreCase) && !ResourceId.TryParse(a, out _))
                        {
                            diags.Add(new(Severity.Warning, "CFG101", $"'{a}' looks like a hex id but does not parse", path, number));
                        }
                    }

                    lines.Add(new(number, line, CfgLineKind.Directive, keyword, args, comment, depth));
                    break;
            }
        }

        while (openLines.Count > 0)
        {
            diags.Add(new(Severity.Error, "CFG011", "compound is never closed", path, openLines.Pop()));
        }

        if (!sawEof)
        {
            diags.Add(new(Severity.Warning, "CFG013", "missing 'eof' terminator", path));
        }

        return new CfgDocument { Path = path, Lines = lines, Diagnostics = diags, HasEof = sawEof };
    }

    /// <summary>Splits off the first <c>;</c> or <c>//</c> comment. Quotes have no special meaning (only seen inside comments).</summary>
    public static (string Code, string? Comment) SplitComment(string line)
    {
        var semi = line.IndexOf(';');
        var slashes = line.IndexOf("//", StringComparison.Ordinal);
        var cut = semi < 0 ? slashes : slashes < 0 ? semi : Math.Min(semi, slashes);
        return cut < 0 ? (line, null) : (line[..cut], line[cut..]);
    }

    public static List<string> Tokenize(string code) =>
        code.Split(Separators, StringSplitOptions.RemoveEmptyEntries).ToList();
}
