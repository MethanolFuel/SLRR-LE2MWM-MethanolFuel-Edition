using Slrr.Core;
using Slrr.Formats.Cfg;
using Slrr.Formats.CfgSchema;

namespace Slrr.Cli;

/// <summary>
/// <c>cfg-schema [gameRoot] [--list]</c>: with --list prints the embedded per-keyword schema; otherwise runs it over
/// every .cfg under gameRoot and prints CFG300/CFG301 violations grouped by keyword. Exit code 0 always.
/// </summary>
public static class CmdCfgSchema
{
    public static int Run(string[] args, TextWriter o, TextWriter e)
    {
        var schemas = CfgArgSchemas.Default;
        if (args.Contains("--list"))
        {
            foreach (var s in schemas.Keywords.OrderBy(k => k.Keyword, StringComparer.Ordinal))
            {
                var types = string.Join(" ", s.Args.Select(a => string.Join("/", a)));
                o.WriteLine($"{s.Keyword,-14} {s.Lines,5} lines {s.Files,4} files  args {s.Min}..{s.Max}  {types}");
            }

            return 0;
        }

        var root = args.FirstOrDefault(a => !a.StartsWith("--", StringComparison.Ordinal)) ?? ".";
        if (!Directory.Exists(root))
        {
            e.WriteLine($"no such directory: {root}");
            return 2;
        }

        var fs = new GameFileSystem(root);
        var byKeyword = new SortedDictionary<string, List<Diagnostic>>(StringComparer.Ordinal);
        int files = 0, lines = 0;
        foreach (var file in fs.Enumerate(".cfg"))
        {
            files++;
            var rel = Path.GetRelativePath(fs.Root, file);
            foreach (var l in CfgDocument.Load(file, rel).Directives)
            {
                lines++;
                foreach (var d in schemas.Check(l, rel))
                {
                    if (!byKeyword.TryGetValue(l.Keyword!, out var list))
                    {
                        byKeyword[l.Keyword!] = list = [];
                    }

                    list.Add(d);
                }
            }
        }

        foreach (var (k, list) in byKeyword)
        {
            o.WriteLine($"{k}: {list.Count}");
            foreach (var d in list.Take(5))
            {
                o.WriteLine($"  {d}");
            }
        }

        o.WriteLine($"{files} cfg files, {lines} directive lines, {byKeyword.Values.Sum(v => v.Count)} violations in {byKeyword.Count} keywords");
        return 0;
    }
}
