using Slrr.Core;
using Slrr.Engine.Ui;
using Slrr.Vm;

namespace Slrr.Cli;

/// <summary>
/// <c>ui</c>: the headless java.render/java.sound/input area (src/Slrr.Engine.Ui).
/// <code>
/// ui --natives                      list the 64 UI natives this area registers
/// ui --survey [gameRoot] [--dynamic] runnable classes before/after the UI natives (static closure; --dynamic adds the VM survey)
/// </code>
/// Needs a ProjectReference from Slrr.Cli.csproj to src/Slrr.Engine.Ui (added at integration).
/// </summary>
public static class CmdUi
{
    public const string Usage = """
        ui --natives                        list the UI natives (java.render, java.sound, Input/Controller/MouseCursor)
        ui --survey [gameRoot] [--dynamic]  runnable classes before/after the UI natives (static tier-B closure from
                                            docs/research/native-usage-classes.tsv; --dynamic: run-class survey too)
        """;

    public static int Run(string[] args, TextWriter o, TextWriter e)
    {
        try
        {
            if (args.Contains("--natives"))
            {
                foreach (var k in UiNatives.Keys.OrderBy(k => k.ToString(), StringComparer.Ordinal))
                {
                    o.WriteLine(k);
                }

                o.WriteLine($"{UiNatives.Keys.Count} natives");
                return 0;
            }

            if (args.Contains("--survey"))
            {
                var root = args.FirstOrDefault(a => !a.StartsWith("--", StringComparison.Ordinal)) ?? ".";
                var tsv = Path.Combine(root, "docs", "research", "native-usage-classes.tsv");
                var (before, after) = UiSurvey.CompareStatic(tsv);
                o.WriteLine($"static (tier B, all methods): {before.Runnable}/{before.Classes} runnable with java.lang natives only; {after.Runnable}/{after.Classes} with the UI natives (+{after.Runnable - before.Runnable})");
                foreach (var c in after.NewlyRunnable)
                {
                    o.WriteLine("  newly runnable: " + c);
                }

                if (args.Contains("--dynamic"))
                {
                    foreach (var withUi in new[] { false, true })
                    {
                        var r = UiSurvey.Dynamic(root, withUi);
                        o.WriteLine($"VM survey {(withUi ? "with" : "without")} UI natives: {r.Classes} classes, static init ok {r.InitOk}, no-arg ctors/static methods ok {r.MethodsOk}/{r.MethodsRun}, classes with all of them ok {r.ClassesAllOk}");
                        foreach (var (k, v) in r.MissingNatives.OrderByDescending(x => x.Value).Take(8))
                        {
                            o.WriteLine($"    {v,5} missing {k}");
                        }
                    }
                }

                return 0;
            }

            e.WriteLine(Usage);
            return 2;
        }
        catch (Exception ex) when (ex is VmException or IOException or SlrrFormatException or UnauthorizedAccessException)
        {
            e.WriteLine("error: " + ex.Message);
            return 1;
        }
    }
}
