using Slrr.Core;
using Slrr.Vm;

namespace Slrr.Engine.Ui;

/// <summary>
/// Before/after numbers for the UI natives: (1) the static runnable-class count of docs/research/native-usage.md
/// (tier B closure of all methods, <c>native-usage-classes.tsv</c>), (2) the VM survey of <c>run-class --survey</c>
/// (fresh VM per class: static init, then every no-argument constructor/static method), without stubs.
/// </summary>
public static class UiSurvey
{
    public sealed record StaticResult(int Classes, int Runnable, IReadOnlyList<string> NewlyRunnable);

    public sealed record DynamicResult(int Classes, int InitOk, int MethodsRun, int MethodsOk, int ClassesAllOk, IReadOnlyDictionary<string, int> MissingNatives);

    /// <summary>Short "Class.method" names (as in native-usage-classes.tsv) of a registry.</summary>
    public static HashSet<string> ShortNames(IEnumerable<NativeKey> keys) =>
        keys.Select(k => k.ClassName[(k.ClassName.LastIndexOf('.') + 1)..] + "." + k.Name).ToHashSet(StringComparer.Ordinal);

    /// <summary>Classes whose tier-B closure (all methods) is covered by <paramref name="implemented"/>.</summary>
    public static (int Classes, List<string> Runnable) StaticRunnable(string tsv, HashSet<string> implemented)
    {
        var runnable = new List<string>();
        var n = 0;
        foreach (var line in File.ReadLines(tsv).Skip(1))
        {
            var p = line.Split('\t');
            if (p.Length < 8)
            {
                continue;
            }

            n++;
            var natives = p[7].Split(',', StringSplitOptions.RemoveEmptyEntries);
            if (natives.All(implemented.Contains))
            {
                runnable.Add(p[0]);
            }
        }

        return (n, runnable);
    }

    public static (StaticResult Before, StaticResult After) CompareStatic(string tsv)
    {
        var lang = ShortNames(NativeRegistry.CreateDefault().Keys);
        var withUi = new HashSet<string>(lang, StringComparer.Ordinal);
        withUi.UnionWith(ShortNames(UiNatives.Keys));
        var (n, before) = StaticRunnable(tsv, lang);
        var (_, after) = StaticRunnable(tsv, withUi);
        var newly = after.Except(before, StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
        return (new StaticResult(n, before.Count, []), new StaticResult(n, after.Count, newly));
    }

    /// <summary>The <c>run-class --survey</c> procedure with or without the UI natives (no stubs, 2M-instruction budget).</summary>
    public static DynamicResult Dynamic(string gameRoot, bool withUi)
    {
        var path = new VmClassPath(new GameFileSystem(gameRoot));
        var names = path.AllScannedClassNames().Order(StringComparer.Ordinal).ToList();
        int initOk = 0, run = 0, ok = 0, allOk = 0;
        var missing = new Dictionary<string, int>(StringComparer.Ordinal);
        NativeRegistry Registry()
        {
            var r = NativeRegistry.CreateDefault();
            if (withUi)
            {
                r.Add(new UiNatives(new UiEngine()));
            }

            return r;
        }

        void Miss(VmException ex)
        {
            if (ex is VmNativeMissingException nm)
            {
                var k = $"{nm.ClassName}.{nm.Name}{nm.Descriptor}";
                missing[k] = missing.GetValueOrDefault(k) + 1;
            }
        }

        foreach (var name in names)
        {
            VmClass cls;
            try
            {
                var vm = new Vm.Vm(path, Registry()) { InstructionBudget = 2_000_000 };
                cls = vm.LoadClass(name);
                vm.EnsureInitialized(cls);
                initOk++;
            }
            catch (VmException ex)
            {
                Miss(ex);
                continue;
            }

            var all = true;
            foreach (var m in cls.Methods.Where(m => m.ArgCount == 0 && (m.IsStatic || m.IsConstructor) && m.Code is not null))
            {
                run++;
                try
                {
                    var vm = new Vm.Vm(path, Registry()) { InstructionBudget = 2_000_000 };
                    var c = vm.LoadClass(name);
                    var target = c.Methods.First(x => x.Descriptor == m.Descriptor && x.Name == m.Name && x.IsStatic == m.IsStatic);
                    if (target.IsConstructor)
                    {
                        vm.InvokeVoid(target, vm.NewObject(c));
                    }
                    else
                    {
                        vm.Invoke(target, null);
                    }

                    ok++;
                }
                catch (VmException ex)
                {
                    all = false;
                    Miss(ex);
                }
            }

            if (all)
            {
                allOk++;
            }
        }

        return new DynamicResult(names.Count, initOk, run, ok, allOk, missing);
    }
}
