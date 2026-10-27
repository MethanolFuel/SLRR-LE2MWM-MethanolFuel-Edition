using System.Diagnostics;
using System.Globalization;

namespace Slrr.Career;

/// <summary>
/// The <c>career</c> CLI (wrapped by tools/Slrr.Cli/CmdCareer.cs): runs one race-type flow (or all) on the shipped data
/// and prints the transcript, the screens reached, the milestones, the first blocker and the measured numbers.
/// </summary>
public static class CareerCommand
{
    public const string Usage =
        "career <roc|day|night|damage|all> [--root dir] [--roc-entry cheat|test|none] [--roc-runs n] [--shipped-roc-cars]\n" +
        "       [--promotions n] [--no-standin-start] [--quiet] [--render --out dir --capture-every n]";

    public static int Run(string[] args, TextWriter o, TextWriter e)
    {
        if (args.Length == 0)
        {
            e.WriteLine("usage: " + Usage);
            return 2;
        }

        var flow = args[0];
        string root = ".", rocEntry = "cheat";
        string? outDir = null;
        int rocRuns = 2, promotions = 2, every = 0;
        bool shippedCars = false, standin = true, quiet = false, render = false;
        for (var i = 1; i < args.Length; i++)
        {
            string Next() => i + 1 < args.Length ? args[++i] : throw new ArgumentException($"{args[i]} needs a value");
            switch (args[i])
            {
                case "--root":
                    root = Next();
                    break;
                case "--roc-entry":
                    rocEntry = Next();
                    break;
                case "--roc-runs":
                    rocRuns = int.Parse(Next(), CultureInfo.InvariantCulture);
                    break;
                case "--shipped-roc-cars":
                    shippedCars = true;
                    break;
                case "--promotions":
                    promotions = int.Parse(Next(), CultureInfo.InvariantCulture);
                    break;
                case "--no-standin-start":
                    standin = false;
                    break;
                case "--quiet":
                    quiet = true;
                    break;
                case "--render":
                    render = true;
                    break;
                case "--out":
                    outDir = Next();
                    break;
                case "--capture-every":
                    every = int.Parse(Next(), CultureInfo.InvariantCulture);
                    break;
                default:
                    e.WriteLine("usage: " + Usage);
                    return 2;
            }
        }

        var flows = flow == "all" ? new[] { "roc", "day", "night", "damage" } : [flow];
        var city = new CityOptions
        {
            PlayerStart = standin ? new CityOptions().PlayerStart : null,
            NightPromotions = promotions,
        };
        var results = new List<FlowResult>();
        foreach (var f in flows)
        {
            var wall = Stopwatch.StartNew();
            using var s = CareerSession.Create(root, new CareerOptions
            {
                Echo = quiet ? null : o,
                Render = render,
                CaptureDir = outDir,
                CaptureEvery = every,
            });
            FlowResult r = f switch
            {
                "roc" => CareerFlows.Roc(s, rocEntry, rocRuns, shippedCars),
                "day" => CityFlows.Day(s, city),
                "night" => CityFlows.Night(s, city),
                "damage" => DamageFlow.Run(s, city),
                _ => throw new ArgumentException($"unknown flow '{f}' ({Usage})"),
            };
            results.Add(r);
            Report(s, r, wall.Elapsed.TotalSeconds, o);
        }

        return results.All(r => r.Completed) ? 0 : 1;
    }

    public static void Report(CareerSession s, FlowResult r, double wallSeconds, TextWriter o)
    {
        o.WriteLine();
        o.WriteLine($"== career {r.Name}: {(r.Completed ? "COMPLETED" : "STOPPED")} after {r.Milestones.Count} milestones; first unmet dependency: {r.FirstBlocker ?? "none"}");
        foreach (var b in r.Blockers.Skip(1))
        {
            o.WriteLine("   also: " + b);
        }
        o.WriteLine("milestones: " + string.Join(" -> ", r.Milestones));
        o.WriteLine("screens reached:");
        foreach (var sc in s.Screens)
        {
            o.WriteLine("  " + sc.Replace('\n', ' '));
        }

        o.WriteLine("numbers:");
        foreach (var (k, v) in r.Numbers)
        {
            o.WriteLine($"  {k} = {v.Replace('\n', ' ')}");
        }

        o.WriteLine($"collision events posted: {s.Damage.Records.Count}");
        o.WriteLine($"script thread errors ({s.Host.ThreadErrors.Count}): {string.Join(" | ", s.Host.ThreadErrors.Take(5))}");
        o.WriteLine($"missing natives ({s.Host.Missing.Count}): {string.Join(", ", s.Host.Missing.Select(m => $"{m.Key} x{m.Calls}"))}");
        o.WriteLine(string.Create(CultureInfo.InvariantCulture, $"game time {s.Host.Vm.Time:F1} s, {s.Host.Frame} frames, wall {wallSeconds:F1} s"));
    }
}
