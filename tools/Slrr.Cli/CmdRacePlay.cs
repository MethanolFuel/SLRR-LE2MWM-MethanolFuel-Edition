using System.Diagnostics;
using System.Globalization;
using Slrr.Race;

namespace Slrr.Cli;

/// <summary>
/// <c>race-play</c>: the race-wave composition root (src/Slrr.Race) driven end to end: boot, new career, buy a used car,
/// Garage "Choose Track", a shipped map, TAKE IT, throttle held through the input layer; optional PNG/WAV capture.
/// </summary>
public static class CmdRacePlay
{
    public const string Usage =
        "race-play [--root dir] [--track \"Drag Track\"]... [--seconds 10] [--steer s] [--no-accel] [--render] [--audio]\n" +
        "          [--out dir] [--capture-every n] [--size WxH] [--wav file] | race-play --survey [--seconds 5]";

    /// <summary>TrackSelect labels of the shipped maps (docs/race/mapnatives.md, MEASURED order).</summary>
    public static readonly string[] ShippedMaps =
    [
        "Car Dealer", "Caynon", "Damage Track", "Dead Island", "Drag Track", "Drift Track", "MP1 Drift Track", "Nevada Desert",
        "Physic Lab", "Test Track", "Top Gear Track", "Valo City",
    ];

    public static int Run(string[] args, TextWriter o, TextWriter e)
    {
        string root = ".";
        string? outDir = null, wav = null;
        var tracks = new List<string>();
        float seconds = 10, steer = 0;
        int every = 0, w = 640, h = 480;
        bool render = false, audio = false, accel = true, survey = false;
        for (var i = 0; i < args.Length; i++)
        {
            string Next() => i + 1 < args.Length ? args[++i] : throw new ArgumentException($"{args[i]} needs a value");
            switch (args[i])
            {
                case "--root":
                    root = Next();
                    break;
                case "--track":
                    tracks.Add(Next());
                    break;
                case "--seconds":
                    seconds = float.Parse(Next(), CultureInfo.InvariantCulture);
                    break;
                case "--steer":
                    steer = float.Parse(Next(), CultureInfo.InvariantCulture);
                    break;
                case "--survey":
                    survey = true;
                    break;
                case "--no-accel":
                    accel = false;
                    break;
                case "--render":
                    render = true;
                    break;
                case "--audio":
                    audio = true;
                    break;
                case "--out":
                    outDir = Next();
                    break;
                case "--wav":
                    wav = Next();
                    audio = true;
                    break;
                case "--capture-every":
                    every = int.Parse(Next(), CultureInfo.InvariantCulture);
                    break;
                case "--size":
                    var p = Next().Split('x');
                    w = int.Parse(p[0], CultureInfo.InvariantCulture);
                    h = int.Parse(p[1], CultureInfo.InvariantCulture);
                    break;
                default:
                    e.WriteLine("usage: " + Usage);
                    return 2;
            }
        }

        if (!File.Exists(Path.Combine(root, "sl.rpk")))
        {
            e.WriteLine($"no game data at {Path.GetFullPath(root)} (sl.rpk missing)");
            return 1;
        }

        if (survey)
        {
            return Survey(root, tracks.Count > 0 ? tracks : ShippedMaps, seconds, o);
        }

        if (render && outDir is null)
        {
            outDir = "race-play-out";
        }

        var wall = Stopwatch.StartNew();
        using var race = RaceHost.Create(root, new RaceHostOptions
        {
            Render = render,
            Audio = audio,
            WavPath = wav ?? (audio && outDir is not null ? Path.Combine(outDir, "race-play.wav") : null),
            CaptureDir = render ? outDir : null,
            CaptureEvery = render ? (every > 0 ? every : 30) : 0,
            Width = w,
            Height = h,
        });
        var scenario = new RaceScenario(race, new RaceScenarioOptions
        {
            Tracks = tracks.Count > 0 ? tracks : ["Drag Track", "Test Track"],
            DriveSeconds = seconds,
            Steer = steer,
            AccelerationRound = accel,
        });
        var r = scenario.Run();
        foreach (var s in r.Steps)
        {
            o.WriteLine(s);
        }

        Print(race, r, o);
        o.WriteLine($"wall {wall.Elapsed.TotalSeconds:F1} s");
        return r.EnteredTrack && r.Moved && r.ThreadErrors.Count == 0 ? 0 : 1;
    }

    /// <summary>Every map on a fresh host: career, used car, Choose Track, the map, TAKE IT, throttle for <paramref name="seconds"/>.</summary>
    public static int Survey(string root, IReadOnlyList<string> maps, float seconds, TextWriter o)
    {
        var ci = CultureInfo.InvariantCulture;
        var driven = 0;
        o.WriteLine("map | entered | track class | moved m | max km/h | grounded | thread errors | enter ms | drive ms/frame | physics ms/step");
        foreach (var map in maps)
        {
            using var race = RaceHost.Create(root);
            var r = new RaceScenario(race, new RaceScenarioOptions { Tracks = [map], DriveSeconds = seconds, AccelerationRound = false }).Run();
            var enter = race.Costs.GetValueOrDefault("enter track");
            var drive = race.Costs.GetValueOrDefault("drive");
            var sc = race.Physics.Scene;
            driven += r.Moved ? 1 : 0;
            o.WriteLine(string.Create(ci,
                $"{map} | {r.EnteredTrack} | {r.TrackClass ?? "-"} | {r.Distance:F1} | {r.MaxSpeed * 3.6f:F1} | {r.GroundedFraction:P0} | {r.ThreadErrors.Count} | {enter.Ms:F0} | {(drive.Frames > 0 ? drive.Ms / drive.Frames : 0):F2} | {(sc.Steps > 0 ? sc.StepWallMs / sc.Steps : 0):F3}"));
            foreach (var s in r.Steps.Where(s => s.Contains("NOT", StringComparison.Ordinal) || s.Contains("FAILED", StringComparison.Ordinal) || s.Contains("not ", StringComparison.Ordinal)))
            {
                o.WriteLine("    " + s);
            }

            foreach (var t in r.ThreadErrors.Take(3))
            {
                o.WriteLine("    thread error: " + t);
            }
        }

        o.WriteLine($"driven {driven}/{maps.Count}");
        return 0;
    }

    public static void Print(RaceHost race, RaceScenarioResult r, TextWriter o)
    {
        var ci = CultureInfo.InvariantCulture;
        o.WriteLine(string.Create(ci, $"result: track {r.Track ?? "-"} entered={r.EnteredTrack} bound={r.CarBound} moved={r.Moved} distance {r.Distance:F1} m max {r.MaxSpeed * 3.6f:F1} km/h grounded {r.GroundedFraction:P1}"));
        foreach (var s in r.Samples.Concat(r.AccelSamples))
        {
            o.WriteLine(string.Create(ci, $"  t {s.Time,6:F2}  pos ({s.Position.X,7:F1},{s.Position.Y,6:F2},{s.Position.Z,8:F1})  {s.Speed * 3.6f,6:F1} km/h  {s.Rpm,5:F0} rpm  gear {s.Gear}  thr {s.Throttle:F2}  grounded {s.WheelsGrounded}"));
        }

        foreach (var t in r.AccelTriggerEvents)
        {
            o.WriteLine("  trigger " + t);
        }

        foreach (var (stage, c) in race.Costs)
        {
            o.WriteLine(string.Create(ci, $"cost {stage,-14} {c.Frames,6} frames {c.Ms,9:F0} ms  {(c.Frames > 0 ? c.Ms / c.Frames : 0),7:F2} ms/frame"));
        }

        var scene = race.Physics.Scene;
        o.WriteLine(string.Create(ci, $"physics: {scene.Steps} fixed steps, {scene.AllCars.Count} cars, {scene.StepWallMs:F0} ms in steps = {(scene.Steps > 0 ? scene.StepWallMs / scene.Steps : 0):F3} ms/step, dropped {scene.DroppedTime:F2} s"));
        if (race.Renderer is { } rr)
        {
            var drawn = rr.Frames;
            o.WriteLine(string.Create(ci, $"render: {drawn.Count} frames drawn, captures {drawn.Count(f => f.CapturePath is not null)}"));
            var onTrack = drawn.Where(f => r.Samples.Count > 0 && f.Time >= r.Samples[0].Time).ToList();
            if (onTrack.Count > 0)
            {
                o.WriteLine(string.Create(ci,
                    $"render on track: {onTrack.Count} frames, draw (cpu+gpu, PNG excluded) mean {onTrack.Average(f => f.Stats.FrameMs):F1} ms, max {onTrack.Max(f => f.Stats.FrameMs):F1} ms, draw calls {onTrack.Average(f => f.Stats.TotalDrawCalls):F0}, triangles {onTrack.Average(f => f.Stats.Triangles):N0}"));
            }

            foreach (var f in drawn.Where(f => f.CapturePath is not null).TakeLast(3))
            {
                o.WriteLine($"  {Path.GetFileName(f.CapturePath)} t {f.Time:F2}: {f.Stats}");
            }
        }

        if (race.Audio is { } au)
        {
            o.WriteLine(string.Create(ci, $"audio: {au.Inner.RenderedSeconds:F1} s mixed, cars attached {au.AttachedCars.Count}, sfx played {au.Inner.PlayedCount} failed {au.Inner.FailedPlayCount}, player engine voices {r.AudioEngineVoices}, table rpm max {r.AudioTableRpmMax:F0}"));
        }

        o.WriteLine($"script thread errors ({r.ThreadErrors.Count})");
        foreach (var t in r.ThreadErrors.Take(10))
        {
            o.WriteLine("  " + t);
        }

        o.WriteLine($"natives missing ({r.Missing.Count}): {string.Join(", ", r.Missing.Take(20))}");
        foreach (var l in race.Physics.Log.Where(l => !l.Contains("GUESSED", StringComparison.Ordinal) && !l.StartsWith("car ", StringComparison.Ordinal) || l.Contains("placed", StringComparison.Ordinal)).TakeLast(15))
        {
            o.WriteLine("  physics: " + l);
        }

        foreach (var rem in race.Scoped?.Removed ?? [])
        {
            o.WriteLine("  removed from the physics scene: " + rem);
        }

        foreach (var car in scene.AllCars)
        {
            var node = race.Host.Resources.Tree.Get(car.Id);
            o.WriteLine(string.Create(ci, $"  physics car {car.Id} {car.Name} state {car.State} at ({car.Origin.X:F1},{car.Origin.Y:F1},{car.Origin.Z:F1}) node {(node is null ? "GONE" : $"parent {node.ParentId}")}"));
        }

        var id = race.PlayerCarId;
        var impacts = race.Physics.Scene.AllImpacts.Where(i => i.CarId == id).ToList();
        o.WriteLine($"impacts of the player car: {impacts.Count}");
        foreach (var i in impacts.OrderByDescending(i => i.DeltaV).Take(5))
        {
            o.WriteLine(string.Create(ci, $"  t {i.Time:F2} dv {i.DeltaV:F1} m/s at ({i.Point.X:F1},{i.Point.Y:F1},{i.Point.Z:F1}) normal ({i.Normal.X:F2},{i.Normal.Y:F2},{i.Normal.Z:F2}) other {i.Other} surface {i.Surface} source {i.Source}"));
        }

        if (Environment.GetEnvironmentVariable("SLRR_RACE_DIAG") == "1")
        {
            o.WriteLine($"commands to the player car {id} (last 25 of {race.Physics.CommandLog.Count(c => c.Target == id)}):");
            foreach (var c in race.Physics.CommandLog.Where(c => c.Target == id).TakeLast(25))
            {
                o.WriteLine($"  {(c.Consumed ? "ok" : "--")} {c.Text}");
            }
        }
    }
}
