using Slrr.Core;
using Slrr.Vm;

namespace Slrr.Engine.Parts;

/// <summary>Which natives a survey VM has.</summary>
public enum SurveyRegistry
{
    /// <summary>java.lang natives of Slrr.Vm only (what <c>run-class --survey</c> uses).</summary>
    Lang,

    /// <summary>java.lang + this area's natives.</summary>
    LangParts,

    /// <summary>java.lang + no-op stubs for every native outside this area (this area's natives stay missing).</summary>
    OthersStubbed,

    /// <summary>java.lang + this area's natives + no-op stubs for everything else.</summary>
    OthersStubbedParts,
}

/// <summary>Per-class outcome of a survey.</summary>
public sealed record SurveyClass(string Name, bool InitOk, int Runs, int Completed, int MissingNative, IReadOnlyList<string> Missing, IReadOnlyList<string> Errors)
{
    /// <summary>Static init and every run reached no unregistered native.</summary>
    public bool NoMissingNative => InitOk && MissingNative == 0;

    /// <summary>Static init and every run completed.</summary>
    public bool AllCompleted => InitOk && Completed == Runs;
}

/// <summary>
/// The <c>run-class --survey --deep</c> procedure (a fresh VM per class and per scripted method, default arguments,
/// 2M-instruction budget) with a choice of native registry, so the effect of this area's natives can be measured.
/// </summary>
public static class PartsSurvey
{
    /// <summary>True for natives this area implements (java.game.parts.* and java.lang.RawEdit).</summary>
    public static bool IsPartsNative(NativeKey k) =>
        k.ClassName.StartsWith("java.game.parts.", StringComparison.Ordinal) || k.ClassName == PartsNatives.RawEdit;

    public static NativeRegistry CreateRegistry(SurveyRegistry kind, PartsWorld? world = null)
    {
        var r = NativeRegistry.CreateDefault();
        if (kind is SurveyRegistry.LangParts or SurveyRegistry.OthersStubbedParts)
        {
            r.Add(new PartsNatives(world));
        }

        if (kind is SurveyRegistry.OthersStubbed or SurveyRegistry.OthersStubbedParts)
        {
            r.Fallback = k => IsPartsNative(k) ? null : NativeRegistry.DefaultStub(k);
        }

        return r;
    }

    public static List<SurveyClass> Run(string gameRoot, SurveyRegistry kind, Func<string, bool>? filter = null)
    {
        var path = new VmClassPath(new GameFileSystem(gameRoot));
        var names = path.AllScannedClassNames().Where(n => filter?.Invoke(n) ?? true).Order(StringComparer.Ordinal).ToList();
        var result = new List<SurveyClass>(names.Count);
        foreach (var name in names)
        {
            var missing = new List<string>();
            var errors = new List<string>();
            var vm = new Vm.Vm(path, CreateRegistry(kind)) { InstructionBudget = 2_000_000 };
            VmClass cls;
            try
            {
                cls = vm.LoadClass(name);
                vm.EnsureInitialized(cls);
            }
            catch (VmException ex)
            {
                if (ex is VmNativeMissingException nm)
                {
                    missing.Add($"{nm.ClassName}.{nm.Name}{nm.Descriptor}");
                }

                result.Add(new SurveyClass(name, false, 0, 0, 0, missing, [ex.Message]));
                continue;
            }

            int runs = 0, completed = 0, nativeMissing = 0;
            foreach (var m in cls.Methods.Where(m => !m.IsNative && m.Code is not null).ToList())
            {
                runs++;
                var run = new Vm.Vm(path, CreateRegistry(kind)) { InstructionBudget = 2_000_000 };
                try
                {
                    var c = run.LoadClass(name);
                    var target = c.Methods.First(x => x.Descriptor == m.Descriptor && x.Name == m.Name && x.IsStatic == m.IsStatic);
                    var args = target.Params.Select(VmTypes.DefaultValue).ToArray();
                    if (target.IsConstructor)
                    {
                        run.InvokeVoid(target, run.NewObject(c), args);
                    }
                    else if (target.IsStatic)
                    {
                        run.Invoke(target, null, args);
                    }
                    else
                    {
                        run.Invoke(target, run.Construct(c), args);
                    }

                    completed++;
                }
                catch (VmNativeMissingException nm)
                {
                    nativeMissing++;
                    missing.Add($"{nm.ClassName}.{nm.Name}{nm.Descriptor}");
                }
                catch (VmException ex)
                {
                    // script errors (null arguments, stub results), System.exit, budget: not a missing native
                    errors.Add(ex.Message);
                }
            }

            result.Add(new SurveyClass(name, true, runs, completed, nativeMissing, missing, errors));
        }

        return result;
    }

    /// <summary>One line per metric, for the CLI and docs.</summary>
    public static string Summarize(IReadOnlyCollection<SurveyClass> r) =>
        $"{r.Count} classes: init ok {r.Count(c => c.InitOk)}, no missing native {r.Count(c => c.NoMissingNative)}, " +
        $"all runs completed {r.Count(c => c.AllCompleted)}; runs {r.Sum(c => c.Runs)}, completed {r.Sum(c => c.Completed)}, " +
        $"stopped at a missing native {r.Sum(c => c.MissingNative)}";
}
