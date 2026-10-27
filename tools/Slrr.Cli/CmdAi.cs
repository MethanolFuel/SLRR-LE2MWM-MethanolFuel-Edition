#if SLRR_AI
using System.Globalization;
using Slrr.AI.Race;
using Slrr.AI.Roads;

namespace Slrr.Cli;

/// <summary>
/// <c>ai-race</c>: a headless race of AI full cars on the synthetic circuit (or a shipped AI spline on its map) with a
/// traffic population, printing results, determinism hash and step cost. See docs/gaps/ai.md.
/// </summary>
public static class CmdAi
{
    public const string Usage =
        "ai-race [--cars n] [--traffic n] [--laps n] [--rubber] [--no-measure] [--frame s] [--finish-timeout s] [--spl file --root dir --map maps/x.rpk --cut metres]";

    public static int Run(string[] args, TextWriter o, TextWriter e)
    {
        var ci = CultureInfo.InvariantCulture;
        int cars = 8, traffic = 30, laps = 2;
        var rubber = false;
        var measure = true;
        var frame = 1.0 / 60;
        var finishTimeout = 120.0;
        string? spl = null, root = null, map = null;
        float? cut = null;
        for (var i = 0; i < args.Length; i++)
        {
            string Next() => i + 1 < args.Length ? args[++i] : throw new ArgumentException($"{args[i]} needs a value");
            switch (args[i])
            {
                case "--cars": cars = int.Parse(Next(), ci); break;
                case "--traffic": traffic = int.Parse(Next(), ci); break;
                case "--laps": laps = int.Parse(Next(), ci); break;
                case "--rubber": rubber = true; break;
                case "--no-measure": measure = false; break;
                case "--frame": frame = double.Parse(Next(), ci); break;
                case "--finish-timeout": finishTimeout = double.Parse(Next(), ci); break;
                case "--spl": spl = Next(); break;
                case "--root": root = Next(); break;
                case "--map": map = Next(); break;
                case "--cut": cut = float.Parse(Next(), ci); break;
                case "-h" or "--help":
                    o.WriteLine(Usage);
                    return 0;
                default:
                    e.WriteLine($"unknown option {args[i]}\n{Usage}");
                    return 2;
            }
        }

        var options = new HeadlessRaceOptions
        {
            AiCars = cars,
            TrafficCars = traffic,
            Rules = new RaceRules { Laps = laps, RubberBand = rubber, FinishTimeout = finishTimeout },
            MeasureCaps = measure,
            FrameTime = frame,
        };
        if (spl is not null)
        {
            var mapRun = MapRace.Prepare(root ?? ".", spl, map, cutAt: cut);
            o.WriteLine(mapRun.Describe);
            options = options with { Roads = mapRun.Roads, Route = mapRun.Route, Ground = mapRun.Ground, Collision = mapRun.Collision, TrafficCars = 0 };
        }

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var r = HeadlessRace.Run(options);
        o.Write(r.Results.Format());
        o.WriteLine(string.Create(ci, $"sim {r.SimSeconds:F1} s, {r.Steps} steps, {r.MsPerStep:F3} ms/step (physics + AI, {r.MaxSceneCars} cars max), traffic director {r.TrafficMsPerFrame:F3} ms/frame, wall {sw.Elapsed.TotalSeconds:F1} s"));
        o.WriteLine(string.Create(ci, $"traffic spawned {r.TrafficSpawned}, despawned {r.TrafficDespawned}, peak {r.TrafficPeak}, min same-lane gap {r.MinTrafficGap:F1} m; car/car impacts {r.CarImpacts}"));
        foreach (var en in r.Entrants)
        {
            o.WriteLine(string.Create(ci, $"  {en.Name}: max |lateral| {en.MaxLateral:F2} m, off-road {100.0 * en.OffRoadSteps / Math.Max(1, en.RacingSteps):F2} % of steps"));
        }

        foreach (var n in r.Notes)
        {
            o.WriteLine($"note: {n}");
        }

        o.WriteLine($"state hash {r.StateHash:X16}");
        return r.Results.Rows.All(x => x.Status == EntrantStatus.Finished) ? 0 : 1;
    }
}
#endif
