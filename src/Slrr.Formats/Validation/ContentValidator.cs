using System.Text;
using Slrr.Core;
using Slrr.Formats.Cfg;
using Slrr.Formats.Rpak;

namespace Slrr.Formats.Validation;

/// <summary>
/// Cross-file checks for a game directory:
/// CFG2xx dangling file references and duplicate ids inside cfg files; RPK2xx the same for RPAK archives
/// (duplicate entry ids in one entry table, file paths named in text entries that do not exist on disk).
/// Parse diagnostics from <see cref="CfgDocument"/> are included as-is.
/// </summary>
public sealed class ContentValidator(GameFileSystem fs)
{
    /// <summary>Keys inside RPAK text entries whose argument names a file (seen in the shipped archives).</summary>
    private static readonly string[] RpakFileKeys = ["sourcefile", "shape", "skeleton", "particle", "skidmark", "native", "script", "mesh"];

    public List<Diagnostic> Diagnostics { get; } = [];
    public int CfgFiles { get; private set; }
    public int CfgFilesWithErrors { get; private set; }
    public int RpakFiles { get; private set; }
    public int RpakFilesFailed { get; private set; }
    public int FileRefsChecked { get; private set; }

    public void ValidateAll()
    {
        foreach (var file in fs.Enumerate(".cfg"))
        {
            ValidateCfg(file);
        }

        foreach (var file in fs.Enumerate(".rpk"))
        {
            ValidateRpak(file);
        }
    }

    public CfgDocument ValidateCfg(string file)
    {
        var rel = Rel(file);
        var doc = CfgDocument.Load(file, rel);
        CfgFiles++;
        if (doc.HasErrors)
        {
            CfgFilesWithErrors++;
        }

        Diagnostics.AddRange(doc.Diagnostics);

        var stockparts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var slotIds = new Dictionary<string, int>(StringComparer.Ordinal);
        var compatibles = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var compoundIndex = 0;
        foreach (var line in doc.Lines)
        {
            if (line.Kind == CfgLineKind.CompoundStart)
            {
                compoundIndex++; // slot ids may legitimately repeat in different compound parts
            }

            if (line.Kind != CfgLineKind.Directive)
            {
                continue;
            }

            foreach (var arg in line.Args.Where(CfgKeywords.LooksLikeFileRef))
            {
                FileRefsChecked++;
                if (!fs.Exists(arg))
                {
                    Diagnostics.Add(new(Severity.Warning, "CFG200", $"'{line.Keyword}' references missing file '{arg}'", rel, line.Number));
                }
            }

            switch (line.Keyword)
            {
                case "stockpart" when line.Args.Count > 0:
                    // repeats are normal (four wheels share one id), so this is informational only
                    CheckDuplicate(stockparts, line.Args[0], "CFG210", "stockpart id", rel, line.Number, Severity.Info);
                    break;
                case "slot" when line.Args.Count >= 7:
                    CheckDuplicate(slotIds, $"{compoundIndex}:{line.Args[6]}", "CFG211", $"slot id {line.Args[6]}", rel, line.Number, Severity.Warning);
                    break;
                case "compatible" when line.Args.Count > 0:
                    CheckDuplicate(compatibles, string.Join(' ', line.Args), "CFG212", $"compatible '{string.Join(' ', line.Args)}'", rel, line.Number, Severity.Info);
                    break;
            }
        }

        return doc;
    }

    public RpakArchive? ValidateRpak(string file)
    {
        var rel = Rel(file);
        RpakFiles++;
        RpakArchive archive;
        try
        {
            archive = RpakArchive.Read(file);
        }
        catch (SlrrFormatException ex)
        {
            RpakFilesFailed++;
            Diagnostics.Add(new(Severity.Error, "RPK001", ex.Message, rel));
            return null;
        }

        var ids = new Dictionary<uint, string>();
        foreach (var e in archive.Entries)
        {
            if (!ids.TryAdd(e.Id, e.Alias))
            {
                Diagnostics.Add(new(Severity.Warning, "RPK210", $"id 0x{e.Id:X8} used by both '{ids[e.Id]}' and '{e.Alias}'", rel));
            }

            if (e.HasExtra || e.Size == 0)
            {
                continue;
            }

            var data = archive.GetData(e).Span;
            if (!LooksLikeText(data))
            {
                continue;
            }

            foreach (var raw in Encoding.Latin1.GetString(data).Split('\n'))
            {
                var (code, _) = CfgDocument.SplitComment(raw.TrimEnd('\r'));
                var tokens = CfgDocument.Tokenize(code);
                if (tokens.Count < 2 || !RpakFileKeys.Contains(tokens[0], StringComparer.OrdinalIgnoreCase))
                {
                    continue;
                }

                foreach (var t in tokens.Skip(1).Where(CfgKeywords.LooksLikeFileRef))
                {
                    FileRefsChecked++;
                    if (!fs.Exists(t))
                    {
                        Diagnostics.Add(new(Severity.Warning, "RPK200", $"entry '{e.Alias}' ({tokens[0]}) references missing file '{t}'", rel));
                    }
                }
            }
        }

        return archive;
    }

    private static bool LooksLikeText(ReadOnlySpan<byte> data)
    {
        foreach (var b in data)
        {
            if (b == 0 || (b < 0x20 && b is not (byte)'\r' and not (byte)'\n' and not (byte)'\t'))
            {
                return false;
            }
        }

        return true;
    }

    private void CheckDuplicate(Dictionary<string, int> seen, string key, string code, string what, string file, int line, Severity severity)
    {
        if (seen.TryGetValue(key, out var first))
        {
            Diagnostics.Add(new(severity, code, $"duplicate {what} (first on line {first})", file, line));
        }
        else
        {
            seen[key] = line;
        }
    }

    private string Rel(string file) => Path.GetRelativePath(fs.Root, file).Replace('/', '\\');
}
