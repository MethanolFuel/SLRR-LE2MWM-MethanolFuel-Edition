using System.Globalization;
using Slrr.Decompiler;
using Slrr.Formats.Tufa;

namespace Slrr.Cli;

/// <summary>
/// <c>decompile &lt;file.class|dir&gt; [--out dir] [--fidelity] [--quiet]</c>: TUFA .class → Java-dialect source.
/// A single file without <c>--out</c> prints to stdout. A directory is scanned recursively and summarised;
/// <c>--fidelity</c> also compares every class with its shipped <c>src/Name.java</c> twin.
/// </summary>
public static class CmdDecompile
{
    private const string Usage = "usage: decompile <file.class|dir> [--out dir] [--fidelity] [--quiet]";

    public static int Run(string[] args, TextWriter o, TextWriter e)
    {
        var positional = new List<string>();
        string? outDir = null;
        var fidelity = false;
        var quiet = false;
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--out" when i + 1 < args.Length:
                    outDir = args[++i];
                    break;
                case "--fidelity":
                    fidelity = true;
                    break;
                case "--quiet":
                    quiet = true;
                    break;
                default:
                    if (args[i].StartsWith("--", StringComparison.Ordinal))
                    {
                        e.WriteLine(Usage);
                        return 2;
                    }

                    positional.Add(args[i]);
                    break;
            }
        }

        if (positional.Count != 1)
        {
            e.WriteLine(Usage);
            return 2;
        }

        var path = positional[0];
        if (File.Exists(path))
        {
            return SingleFile(path, outDir, o, e);
        }

        if (!Directory.Exists(path))
        {
            e.WriteLine($"error: {path} not found");
            return 1;
        }

        var report = CorpusDecompiler.Run(path, outDir);
        if (!quiet)
        {
            foreach (var f in report.Files.Where(f => !f.Clean))
            {
                o.WriteLine($"  {f.RelativePath}: {f.Summary}");
            }
        }

        o.WriteLine(report.Summary());
        if (fidelity)
        {
            var fid = FidelityChecker.Run(path);
            if (!quiet)
            {
                foreach (var p in fid.Pairs)
                {
                    o.WriteLine(string.Create(CultureInfo.InvariantCulture,
                        $"  {p.JavaPath}: {p.ExactMembers}/{p.MatchedMembers} exact, similarity {p.Similarity:P1}, unmatched class/java {p.OnlyInClass}/{p.OnlyInJava}"));
                }
            }

            o.WriteLine(fid.Summary());
        }

        return 0;
    }

    private static int SingleFile(string path, string? outDir, TextWriter o, TextWriter e)
    {
        var source = TufaDecompiler.DecompileFile(TufaFile.Read(path), out var results);
        if (outDir is null)
        {
            o.Write(source);
        }
        else
        {
            Directory.CreateDirectory(outDir);
            var target = Path.Combine(outDir, Path.GetFileNameWithoutExtension(path) + ".java");
            File.WriteAllText(target, source);
            o.WriteLine($"wrote {target}");
        }

        foreach (var r in results)
        {
            foreach (var msg in r.Diagnostics.Errors)
            {
                e.WriteLine($"{r.Name}: {msg}");
            }

            if (r.Diagnostics.Gotos > 0)
            {
                e.WriteLine($"{r.Name}: {r.Diagnostics.Gotos} unstructured jump(s)");
            }
        }

        return results.All(r => r.Diagnostics.Clean) ? 0 : 1;
    }
}
