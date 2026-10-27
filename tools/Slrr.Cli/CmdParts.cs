using System.Globalization;
using Slrr.Engine.Parts;
using Slrr.Engine.Parts.RawEdit;
using Slrr.Vm;

namespace Slrr.Cli;

/// <summary>
/// <c>parts</c>: reports for the parts data model (src/Slrr.Engine.Parts, docs/engine/parts.md).
/// <code>
/// parts --survey [gameRoot] [--list]   run-class --survey --deep with/without the parts natives (4 registries)
/// parts --coverage [order.json]        parts-area natives in native-usage-order.json vs registered
/// parts --rawedit                      the RawEdit address map and both presets
/// </code>
/// </summary>
public static class CmdParts
{
    public const string Usage = """
        parts --survey [gameRoot] [--list]   survey every class: java.lang only / + parts natives / others stubbed with and without parts
        parts --coverage [order.json]        parts-area natives of docs/research/native-usage-order.json vs registered
        parts --rawedit                      RawEdit address -> EngineSettings property map with MethanolFuel/Vanilla values
        """;

    public static int Run(string[] args, TextWriter o, TextWriter e)
    {
        var plain = args.Where(a => !a.StartsWith("--", StringComparison.Ordinal)).ToList();
        if (args.Contains("--survey"))
        {
            return Survey(plain.FirstOrDefault() ?? ".", args.Contains("--list"), o);
        }

        if (args.Contains("--coverage"))
        {
            return Coverage(plain.FirstOrDefault() ?? Path.Combine("docs", "research", "native-usage-order.json"), o, e);
        }

        if (args.Contains("--rawedit"))
        {
            return RawEdit(o);
        }

        e.WriteLine(Usage);
        return 2;
    }

    private static int Survey(string root, bool list, TextWriter o)
    {
        if (!File.Exists(Path.Combine(root, "sl.rpk")))
        {
            o.WriteLine($"no game root at {Path.GetFullPath(root)}");
            return 1;
        }

        var results = new Dictionary<SurveyRegistry, List<SurveyClass>>();
        foreach (var kind in Enum.GetValues<SurveyRegistry>())
        {
            results[kind] = PartsSurvey.Run(root, kind);
            o.WriteLine($"{kind,-20} {PartsSurvey.Summarize(results[kind])}");
        }

        var before = results[SurveyRegistry.OthersStubbed].Where(c => c.NoMissingNative).Select(c => c.Name).ToHashSet(StringComparer.Ordinal);
        var after = results[SurveyRegistry.OthersStubbedParts].Where(c => !c.NoMissingNative).ToList();
        o.WriteLine($"classes that only needed parts natives (others stubbed): {results[SurveyRegistry.OthersStubbedParts].Count(c => c.NoMissingNative) - before.Count}");
        o.WriteLine("parts natives still missing (others stubbed):");
        foreach (var g in after.SelectMany(c => c.Missing).GroupBy(m => m).OrderByDescending(g => g.Count()).Take(15))
        {
            o.WriteLine($"  {g.Count(),5} {g.Key}");
        }

        o.WriteLine("most frequent script errors with parts natives and other areas stubbed (numbers masked):");
        foreach (var g in results[SurveyRegistry.OthersStubbedParts].SelectMany(c => c.Errors)
                     .GroupBy(m => System.Text.RegularExpressions.Regex.Replace(m, @"[0-9a-f]{4,}|\d+", "#")).OrderByDescending(g => g.Count()).Take(list ? 100 : 15))
        {
            o.WriteLine($"  {g.Count(),5} {g.Key}");
        }

        if (list)
        {
            foreach (var c in results[SurveyRegistry.OthersStubbedParts].Where(c => c.NoMissingNative && !before.Contains(c.Name)))
            {
                o.WriteLine("  newly runnable " + c.Name);
            }
        }

        return 0;
    }

    private static int Coverage(string orderJson, TextWriter o, TextWriter e)
    {
        if (!File.Exists(orderJson))
        {
            e.WriteLine($"not found: {orderJson}");
            return 1;
        }

        var module = new PartsNatives();
        var registry = new NativeRegistry();
        registry.Add(module);
        using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(orderJson));
        var area = doc.RootElement.EnumerateArray()
            .Select(x => (Rank: x.GetProperty("rank").GetInt32(), Key: new NativeKey(x.GetProperty("class").GetString()!, x.GetProperty("method").GetString()!, x.GetProperty("descriptor").GetString()!)))
            .Where(x => PartsSurvey.IsPartsNative(x.Key))
            .ToList();
        var done = area.Count(x => registry.Contains(x.Key.ClassName, x.Key.Name, x.Key.Descriptor));
        o.WriteLine($"parts-area natives in the ranking: {area.Count}; registered: {done}; module registers {module.Registered.Count}");
        foreach (var x in area.Where(x => !registry.Contains(x.Key.ClassName, x.Key.Name, x.Key.Descriptor)))
        {
            o.WriteLine($"  missing #{x.Rank} {x.Key}");
        }

        // Static reach (tier B closures of docs/research/native-usage-classes.tsv, names without descriptors).
        var classesTsv = Path.Combine(Path.GetDirectoryName(orderJson) ?? ".", "native-usage-classes.tsv");
        if (File.Exists(classesTsv))
        {
            static string Short(NativeKey k) => k.ClassName[(k.ClassName.LastIndexOf('.') + 1)..] + "." + k.Name;
            var lang = NativeRegistry.CreateDefault().Keys.Select(Short).ToHashSet(StringComparer.Ordinal);
            var parts = module.Registered.Select(Short).ToHashSet(StringComparer.Ordinal);
            var rows = File.ReadLines(classesTsv).Skip(1).Select(l => l.Split('\t'))
                .Select(p => p.Length > 7 && p[7].Length > 0 ? p[7].Split(',') : []).ToList();
            var partsClasses = new HashSet<string>(StringComparer.Ordinal) { "Part", "Chassis", "WheelRef", "SfxTable", "DynoData", "RawEdit" };
            bool IsParts(string n) => partsClasses.Contains(n[..n.IndexOf('.')]);
            int Count(Func<string, bool> ok) => rows.Count(r => r.All(ok));
            o.WriteLine($"static reach over {rows.Count} classes (all methods, tier B): java.lang only {Count(lang.Contains)}, "
                + $"+ parts {Count(n => lang.Contains(n) || parts.Contains(n))}; with every other area assumed done: "
                + $"without parts {Count(n => !IsParts(n))}, with parts {Count(n => !IsParts(n) || parts.Contains(n))}");
        }

        return 0;
    }

    private static int RawEdit(TextWriter o)
    {
        var gi = EngineSettings.MethanolFuel();
        var vanilla = EngineSettings.Vanilla();
        o.WriteLine("address\tname\tproperty\ttype\tconf\tmethanolfuel\tvanilla\twritable");
        foreach (var x in RawAddressMap.Entries)
        {
            o.WriteLine(string.Join('\t', $"0x{x.Address:x8}", x.Name, x.Property, x.Type, x.Confidence,
                x.Get(gi).ToString(CultureInfo.InvariantCulture), x.Get(vanilla).ToString(CultureInfo.InvariantCulture), x.Set is not null));
        }

        return 0;
    }
}
