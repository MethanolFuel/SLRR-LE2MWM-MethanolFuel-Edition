#if SLRR_PHYSICS
// Integration (lead): "physcheck" => CmdPhysCheck.Run(args[1..], o, e) in Commands.cs, usage line CmdPhysCheck.Usage.
using System.Diagnostics;
using Slrr.Engine.Parts.RawEdit;
using Slrr.Physics;
using Slrr.Physics.Loading;

namespace Slrr.Cli;

/// <summary>
/// <c>physcheck</c>: the regression scenarios of <see cref="Validation"/> (0-100, top speed, braking, skidpad, slalom,
/// brake-in-turn, drift entry, jump landing) for one car or every shipped car, with expected ranges derived from each
/// car's own data. Parity with the original game is UNVERIFIED: the ranges are first-principles bounds, not game data.
/// Exit code 0 = every check of every car passed, 1 = some outlier.
/// </summary>
public static class CmdPhysCheck
{
    public const string Usage = """
        physcheck [car ...|--all] [--root dir] [--gi] [--quiet]
                                                          regression scenarios with first-principles ranges (outliers flagged)
        """;

    public static int Run(string[] args, TextWriter o, TextWriter e)
    {
        var root = Option(args, "--root") ?? Directory.GetCurrentDirectory();
        if (!File.Exists(Path.Combine(root, "sl.rpk")))
        {
            e.WriteLine($"physcheck: no game data in {root} (sl.rpk missing); use --root");
            return 2;
        }

        var names = Positional(args).ToList();
        var cars = args.Contains("--all") || names.Count == 0
            ? CarLoader.FindCars(root).GroupBy(c => c.Name).Select(g => g.First()).ToList()
            : names.Select(n => CarLoader.FindCar(root, n) ?? throw new ArgumentException($"no car matches '{n}'")).ToList();
        var options = new SpecOptions { Settings = args.Contains("--gi") ? EngineSettings.MethanolFuel() : null };
        var quiet = args.Contains("--quiet");
        var outliers = new List<string>();
        var skipped = new List<string>();
        o.WriteLine("physcheck: ranges from each car's own data and first principles; NOT the original game's numbers (parity unverified)");
        foreach (var type in cars)
        {
            var sw = Stopwatch.StartNew();
            VehicleSpec spec;
            LoadedCar loaded;
            try
            {
                loaded = CarLoader.Load(root, type);
                spec = SpecBuilder.Build(loaded.Chassis, loaded.Dyno, options);
            }
            catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or IOException)
            {
                // Traffic/fake-racer gametypes have no Part chassis script (CarLoader: "produced no script object"): skipped.
                if (ex.Message.Contains("produced no script object", StringComparison.Ordinal))
                {
                    skipped.Add(type.Name);
                    continue;
                }

                outliers.Add($"{type.Name}: not built ({ex.Message})");
                o.WriteLine($"== {type.Name}: NOT BUILT: {ex.Message}");
                continue;
            }

            var est = CarEstimates.Of(spec);
            o.WriteLine(F($"== {type.Name}: {spec.Mass:F0} kg, {est.PeakPowerW / 1000:F0} kW @ {est.PeakPowerRpm:F0}, mu {est.Mu:F2}, CoM h {est.CgHeight:F2} m, track {est.Track:F2} m (lift at {est.WheelLiftG:F2} g), {spec.Body.Count} body boxes"));
            if (loaded.EngineCompletedInCSharp)
            {
                o.WriteLine("   WARNING: Chassis.updatevariables failed in the VM; engine/gearbox from a C# replay (see the load log)");
                foreach (var line in loaded.Log.Where(l => l.StartsWith("updatevariables", StringComparison.Ordinal)).Take(1))
                {
                    o.WriteLine("   " + (line.Length > 220 ? line[..220] + "..." : line));
                }
            }

            if (est.PeakPowerW <= 0)
            {
                outliers.Add($"{type.Name}: no engine torque");
                o.WriteLine("   OUTLIER: the engine table has no positive torque below RPM_limit; scenarios skipped");
                continue;
            }

            foreach (var r in Validation.Run(spec))
            {
                var mark = r.Pass ? "ok " : "OUT";
                if (!r.Pass)
                {
                    outliers.Add(F($"{type.Name}: {r.Name} = {r.Value:F2} {r.Unit} (expected {r.Min:F2}..{r.Max:F2}{(r.Completed ? "" : ", incomplete")})"));
                }

                if (!quiet || !r.Pass)
                {
                    o.WriteLine(F($"   {mark} {r.Name,-18} {r.Value,8:F2} {r.Unit,-5} [{r.Min:F2}..{r.Max:F2}]{(r.Completed ? "" : " incomplete")}  {r.Detail}"));
                    if (!quiet)
                    {
                        o.WriteLine($"         basis: {r.Basis}");
                    }
                }
            }

            o.WriteLine(F($"   ({sw.Elapsed.TotalSeconds:F1} s)"));
        }

        o.WriteLine();
        if (skipped.Count > 0)
        {
            o.WriteLine($"skipped (no Part chassis script: traffic / fake racers): {string.Join(", ", skipped)}");
        }

        o.WriteLine($"outliers: {outliers.Count}");
        foreach (var x in outliers)
        {
            o.WriteLine("  " + x);
        }

        return outliers.Count == 0 ? 0 : 1;
    }

    private static string? Option(string[] args, string name)
    {
        var i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }

    private static IEnumerable<string> Positional(string[] args)
    {
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] is "--root")
            {
                i++;
                continue;
            }

            if (!args[i].StartsWith("--", StringComparison.Ordinal))
            {
                yield return args[i];
            }
        }
    }

    private static string F(FormattableString s) => FormattableString.Invariant(s);
}
#endif
