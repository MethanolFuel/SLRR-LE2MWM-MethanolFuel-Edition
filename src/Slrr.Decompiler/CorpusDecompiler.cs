using System.Globalization;
using Slrr.Core;
using Slrr.Formats.Tufa;

namespace Slrr.Decompiler;

/// <summary>Result for one .class file of a corpus run.</summary>
public sealed record FileReport(string RelativePath, int Classes, int CleanClasses, int Errors, int Gotos, string? ReadError, string? FirstError)
{
    public bool Clean => ReadError is null && Errors == 0 && Gotos == 0;

    public string Summary => ReadError is not null
        ? "read error: " + ReadError
        : $"{Errors} error(s), {Gotos} goto(s){(FirstError is null ? string.Empty : "; first: " + FirstError)}";
}

/// <summary>Decompiles every .class under a directory (hidden directories skipped) and aggregates the diagnostics.</summary>
public sealed class CorpusReport
{
    public List<FileReport> Files { get; } = [];

    public int Classes { get; set; }
    public int CleanClasses { get; set; }
    public int Methods { get; set; }
    public int Bodies { get; set; }
    public int TotalBodies { get; set; }
    public int NativeBodies { get; set; }
    public int Gotos { get; set; }
    public int ClassesWithGotos { get; set; }
    public int ClassesWithErrors { get; set; }
    public SortedDictionary<string, int> Unresolved { get; } = new(StringComparer.Ordinal);

    public int CleanFiles => Files.Count(f => f.Clean);

    public string Summary() =>
        string.Create(CultureInfo.InvariantCulture, $"decompiled {Files.Count} files / {Classes} classes: {CleanFiles} files and {CleanClasses} classes clean ")
        + string.Create(CultureInfo.InvariantCulture, $"(no errors, no unstructured jumps); {ClassesWithErrors} classes with errors, {ClassesWithGotos} with {Gotos} unstructured jumps; ")
        + string.Create(CultureInfo.InvariantCulture, $"bodies decompiled {Bodies} + {NativeBodies} empty native = {Bodies + NativeBodies}/{TotalBodies}; methods {Methods}; unresolved: ")
        + (Unresolved.Count == 0 ? "none" : string.Join(", ", Unresolved.Select(kv => string.Create(CultureInfo.InvariantCulture, $"{kv.Key} x{kv.Value}"))));

    public static CorpusReport Run(string root, string? outDir = null)
    {
        var report = new CorpusReport();
        var fs = new GameFileSystem(root);
        foreach (var path in fs.Enumerate(".class"))
        {
            var rel = Path.GetRelativePath(fs.Root, path);
            IReadOnlyList<TufaClass> classes;
            try
            {
                classes = TufaFile.Read(path);
            }
            catch (SlrrFormatException ex)
            {
                report.Files.Add(new FileReport(rel, 0, 0, 0, 0, ex.Message, null));
                continue;
            }

            var source = TufaDecompiler.DecompileFile(classes, out var results);
            var errors = 0;
            var gotos = 0;
            var clean = 0;
            string? first = null;
            foreach (var r in results)
            {
                var d = r.Diagnostics;
                report.Classes++;
                report.Methods += r.MethodCount;
                report.Bodies += r.DecompiledBodies;
                report.NativeBodies += r.NativeBodies;
                errors += d.Errors.Count;
                gotos += d.Gotos;
                first ??= d.Errors.FirstOrDefault();
                report.Gotos += d.Gotos;
                report.ClassesWithGotos += d.Gotos > 0 ? 1 : 0;
                report.ClassesWithErrors += d.Errors.Count > 0 ? 1 : 0;
                if (d.Clean)
                {
                    clean++;
                    report.CleanClasses++;
                }

                foreach (var u in d.Unresolved)
                {
                    report.Unresolved[u] = report.Unresolved.GetValueOrDefault(u) + 1;
                }
            }

            report.TotalBodies += classes.Sum(c => c.Bodies.Count);
            report.Files.Add(new FileReport(rel, results.Count, clean, errors, gotos, null, first));
            if (outDir is not null)
            {
                var target = Path.Combine(outDir, Path.ChangeExtension(rel, ".java"));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.WriteAllText(target, source);
            }
        }

        return report;
    }
}

/// <summary>Convenience entry point used by the CLI.</summary>
public static class CorpusDecompiler
{
    public static CorpusReport Run(string root, string? outDir = null) => CorpusReport.Run(root, outDir);
}
