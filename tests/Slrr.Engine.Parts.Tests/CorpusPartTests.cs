using Slrr.Vm;
using Xunit;
using Xunit.Abstractions;

namespace Slrr.Engine.Parts.Tests;

/// <summary>Constructs every shipped Part subclass the way the gametype creator would (corpus test).</summary>
public class CorpusPartTests(ITestOutputHelper output)
{
    [Fact]
    public void EveryShippedPartClassConstructsAndUpdatesHeadless()
    {
        if (GameRoot.ClassPath is not { } path)
        {
            return;
        }

        var names = path.AllScannedClassNames().Order(StringComparer.Ordinal).ToList();
        int parts = 0, constructed = 0, updated = 0, chassis = 0, maxWear = 0;
        var failures = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var name in names)
        {
            var h = Harness.Create()!;
            VmClass cls;
            try
            {
                cls = h.Vm.LoadClass(name);
            }
            catch (VmException)
            {
                continue;
            }

            if (!cls.IsSubclassOf(PartsWorld.PartClass) || cls.FindMethods("<init>", 1).FirstOrDefault() is null)
            {
                continue;
            }

            parts++;
            try
            {
                var p = h.Create(name, null, 0x00040000 + parts);
                constructed++;
                chassis += p.IsChassis ? 1 : 0;
                maxWear += p.MaxWear > 0 ? 1 : 0;
                h.Call(p, "updatevariables");
                updated++;
            }
            catch (VmException ex)
            {
                var key = System.Text.RegularExpressions.Regex.Replace(ex.Message, @"\d+", "#");
                failures[key] = failures.GetValueOrDefault(key) + 1;
            }
        }

        output.WriteLine($"{parts} Part classes with <init>(int): constructed {constructed} ({chassis} chassis, {maxWear} set a max wear), loose updatevariables() completed {updated}");
        foreach (var (k, v) in failures.OrderByDescending(x => x.Value).Take(15))
        {
            output.WriteLine($"  {v,4} {k}");
        }

        Assert.True(parts > 1400, $"only {parts} part classes found");
        Assert.True(constructed >= parts * 99 / 100, $"constructed {constructed}/{parts}");
        Assert.True(maxWear > 400);
    }
}
