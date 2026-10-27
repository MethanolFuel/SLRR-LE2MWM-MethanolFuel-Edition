using System.Globalization;
using Slrr.Core;
using Slrr.Formats.Tufa;

namespace Slrr.Decompiler;

/// <summary>Comparison of one shipped .java file with the decompiled output of its .class twin.</summary>
public sealed record PairReport(
    string JavaPath,
    string ClassPath,
    int ClassesMatched,
    int MatchedMembers,
    int ExactMembers,
    int ExactMembersNoParens,
    int OnlyInClass,
    int OnlyInJava,
    long CommonTokens,
    long TotalTokens,
    List<string> Mismatches)
{
    /// <summary>Token similarity over matched members: 2·LCS / (|a| + |b|).</summary>
    public double Similarity => TotalTokens == 0 ? 1 : 2.0 * CommonTokens / TotalTokens;
}

/// <summary>
/// Round-trip fidelity check: decompile each shipped .class that has a <c>src/Name.java</c> twin, normalise both
/// (see <see cref="SourceStructure"/>) and compare member by member. Members present on only one side are
/// counted but not scored: the shipped sources were edited after compilation (docs/formats/tufa.md).
/// </summary>
public sealed class FidelityReport
{
    public List<PairReport> Pairs { get; } = [];

    public int JavaFiles { get; set; }

    public int Matched => Pairs.Sum(p => p.MatchedMembers);
    public int Exact => Pairs.Sum(p => p.ExactMembers);
    public int ExactNoParens => Pairs.Sum(p => p.ExactMembersNoParens);
    public double Similarity
    {
        get
        {
            var total = Pairs.Sum(p => p.TotalTokens);
            return total == 0 ? 1 : 2.0 * Pairs.Sum(p => p.CommonTokens) / total;
        }
    }

    public string Summary() =>
        string.Create(CultureInfo.InvariantCulture, $"fidelity: {Pairs.Count}/{JavaFiles} .java files paired with a .class, {Pairs.Sum(p => p.ClassesMatched)} classes matched; ")
        + string.Create(CultureInfo.InvariantCulture, $"members matched {Matched} (only in .class {Pairs.Sum(p => p.OnlyInClass)}, only in .java {Pairs.Sum(p => p.OnlyInJava)}); ")
        + string.Create(CultureInfo.InvariantCulture, $"exact {Exact} ({Pct(Exact, Matched)}), exact ignoring parentheses {ExactNoParens} ({Pct(ExactNoParens, Matched)}); ")
        + string.Create(CultureInfo.InvariantCulture, $"token similarity {Similarity:P2}; files with every matched member exact (ignoring parentheses): ")
        + Pairs.Count(p => p.MatchedMembers > 0 && p.ExactMembersNoParens == p.MatchedMembers).ToString(CultureInfo.InvariantCulture);

    private static string Pct(int a, int b) => b == 0 ? "-" : (100.0 * a / b).ToString("F1", CultureInfo.InvariantCulture) + "%";
}

public static class FidelityChecker
{
    public static FidelityReport Run(string root)
    {
        var fs = new GameFileSystem(root);
        var classFiles = fs.Enumerate(".class").ToList();
        var byName = classFiles.ToLookup(p => Path.GetFileNameWithoutExtension(p), StringComparer.OrdinalIgnoreCase);
        var report = new FidelityReport();
        foreach (var java in fs.Enumerate(".java"))
        {
            report.JavaFiles++;
            var baseName = Path.GetFileNameWithoutExtension(java);
            var dir = Path.GetDirectoryName(java)!;
            var parent = Path.GetFileName(dir).Equals("src", StringComparison.OrdinalIgnoreCase) ? Path.GetDirectoryName(dir)! : dir;
            var candidates = byName[baseName].ToList();
            var cls = candidates.FirstOrDefault(c => string.Equals(Path.GetDirectoryName(c), parent, StringComparison.OrdinalIgnoreCase))
                ?? (candidates.Count == 1 ? candidates[0] : null);
            if (cls is null)
            {
                continue;
            }

            report.Pairs.Add(Compare(File.ReadAllText(java), TufaFile.Read(cls), Path.GetRelativePath(fs.Root, java), Path.GetRelativePath(fs.Root, cls)));
        }

        return report;
    }

    public static PairReport Compare(string javaSource, IReadOnlyList<TufaClass> classes, string javaPath = "", string classPath = "")
    {
        var decompiled = TufaDecompiler.DecompileFile(classes, out _);
        var original = SourceStructure.Parse(javaSource);
        var ours = SourceStructure.Parse(decompiled);
        int classesMatched = 0, matched = 0, exact = 0, exactNp = 0, onlyClass = 0, onlyJava = 0;
        long common = 0, total = 0;
        var mismatches = new List<string>();
        foreach (var oc in ours)
        {
            var jc = original.FirstOrDefault(c => c.Name == oc.Name);
            if (jc is null)
            {
                onlyClass += oc.Members.Count;
                continue;
            }

            classesMatched++;
            var javaMembers = jc.Members.GroupBy(m => m.Key).ToDictionary(g => g.Key, g => g.Last());
            var seen = new HashSet<string>();
            foreach (var m in oc.Members)
            {
                if (!javaMembers.TryGetValue(m.Key, out var jm))
                {
                    onlyClass++;
                    continue;
                }

                seen.Add(m.Key);
                matched++;
                var lcs = Lcs(m.Tokens, jm.Tokens);
                common += lcs;
                total += m.Tokens.Count + jm.Tokens.Count;
                var same = lcs == m.Tokens.Count && lcs == jm.Tokens.Count;
                exact += same ? 1 : 0;
                var a = m.Tokens.Where(t => t is not ("(" or ")")).ToList();
                var b = jm.Tokens.Where(t => t is not ("(" or ")")).ToList();
                var sameNp = a.SequenceEqual(b);
                exactNp += sameNp ? 1 : 0;
                if (!sameNp && mismatches.Count < 50)
                {
                    mismatches.Add($"{oc.Name}.{m.Key}");
                }
            }

            onlyJava += javaMembers.Keys.Count(k => !seen.Contains(k));
        }

        return new PairReport(javaPath, classPath, classesMatched, matched, exact, exactNp, onlyClass, onlyJava, common, total, mismatches);
    }

    /// <summary>Length of the longest common subsequence (Myers O((N+M)·D) without a trace).</summary>
    public static int Lcs(IReadOnlyList<string> a, IReadOnlyList<string> b)
    {
        int n = a.Count, m = b.Count, max = n + m;
        if (max == 0)
        {
            return 0;
        }

        var v = new int[2 * max + 2];
        for (var d = 0; d <= max; d++)
        {
            for (var k = -d; k <= d; k += 2)
            {
                int x = k == -d || (k != d && v[max + k - 1] < v[max + k + 1]) ? v[max + k + 1] : v[max + k - 1] + 1;
                var y = x - k;
                while (x < n && y < m && string.Equals(a[x], b[y], StringComparison.Ordinal))
                {
                    x++;
                    y++;
                }

                v[max + k] = x;
                if (x >= n && y >= m)
                {
                    return (n + m - d) / 2;
                }
            }
        }

        return 0;
    }
}
