#if SLRR_GAME_PHYSICS
using System.Globalization;
using Slrr.Game.Physics;
using Slrr.Physics;

namespace Slrr.Cli;

/// <summary>
/// <c>carsim [car] [--map maps/x.rpk] [--root dir] [--at x,y,z] [--yaw r] [--seconds s] [--cars n] [--simple]
/// [--parallel] [--every s]</c>: a shipped car on a shipped map driven by the scripted lap segment (Slrr.Game.Physics,
/// no game scripts). Prints the trace, impacts, plausibility checks and the step cost. Exit 0 = all checks pass.
/// </summary>
public static class CmdCarSim
{
    public static int Run(string[] args, TextWriter o, TextWriter e)
    {
        string? Opt(string name) => Array.IndexOf(args, name) is var i and >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        double D(string name, double def) => Opt(name) is { } s && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : def;
        var root = Opt("--root") ?? FindRoot();
        if (root is null)
        {
            e.WriteLine("carsim: game root not found (pass --root)");
            return 2;
        }

        var valued = new HashSet<string> { "--root", "--map", "--at", "--yaw", "--seconds", "--cars", "--every" };
        var car = args.Where((a, i) => !a.StartsWith("--", StringComparison.Ordinal) && (i == 0 || !valued.Contains(args[i - 1]))).FirstOrDefault() ?? "Baiern_DevilSport";
        Vec3? at = null;
        if (Opt("--at") is { } atText)
        {
            var p = atText.Split(',').Select(s => double.Parse(s, CultureInfo.InvariantCulture)).ToArray();
            at = new Vec3(p[0], p[1], p[2]);
        }

        var options = new HeadlessDriveOptions
        {
            Root = root,
            Car = car,
            Map = Opt("--map") ?? "maps/test_track.rpk",
            At = at,
            Yaw = Opt("--yaw") is null ? null : D("--yaw", 0),
            Seconds = D("--seconds", 12),
            Cars = (int)D("--cars", 1),
            SimpleExtras = args.Contains("--simple"),
            Parallel = args.Contains("--parallel"),
            TraceEvery = D("--every", 0.5),
        };

        HeadlessDriveResult r;
        try
        {
            r = HeadlessDrive.Run(options);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or IOException)
        {
            e.WriteLine($"carsim: {ex.Message}");
            return 1;
        }

        var inv = CultureInfo.InvariantCulture;
        o.WriteLine(string.Create(inv, $"car {r.Spec.Name}: {r.Spec.Mass:F0} kg, {r.Spec.Wheels.Count} wheels, {r.Spec.Drivetrain.Gears} gears, rpm limit {r.Spec.Engine.RpmLimit:F0} (loaded in {r.CarLoadMs:F0} ms)"));
        o.WriteLine(string.Create(inv, $"map {r.World.Name}: {r.World.Collision.TriangleCount} triangles (loaded in {r.MapLoadMs:F0} ms)"));
        o.WriteLine(string.Create(inv, $"start {r.Start} yaw {r.StartYaw:F3}; {r.Cars} car(s), {r.Steps} steps of 1/120 s"));
        foreach (var n in r.Notes)
        {
            o.WriteLine("note: " + n);
        }

        o.WriteLine("    t      x        y         z    km/h    rpm gear thr brk steer  gLong  gLat  gVert grounded");
        foreach (var t in r.Trace)
        {
            o.WriteLine(string.Create(inv,
                $"{t.Time,6:F2} {t.Position.X,8:F2} {t.Position.Y,7:F2} {t.Position.Z,9:F2} {t.SpeedKmh,6:F1} {t.Rpm,6:F0} {t.Gear,4} {t.Throttle,3:F1} {t.Brake,3:F1} {t.Steer,5:F2} {t.GLongitudinal,6:F2} {t.GLateral,5:F2} {t.GVertical,6:F2} {t.Wheels.Count(w => w.Grounded)}/{t.Wheels.Count}"));
        }

        foreach (var i in r.Impacts.Take(10))
        {
            o.WriteLine(string.Create(inv, $"impact t={i.Time:F2} car {i.CarId} dv {i.DeltaV:F2} m/s normal {i.Normal} other {i.Other} source 0x{i.Source:X8}"));
        }

        if (r.Impacts.Count > 10)
        {
            o.WriteLine($"... {r.Impacts.Count} impacts");
        }

        o.WriteLine(string.Create(inv, $"max {r.MaxSpeed * 3.6:F1} km/h, max rpm {r.MaxRpm:F0}, top gear used {r.MaxGear}, |gLong| {r.MaxAbsGLong:F2}, |gLat| {r.MaxAbsGLat:F2}, distance {r.Distance:F1} m, all wheels grounded {r.GroundedFraction:P1}"));
        o.WriteLine(string.Create(inv, $"step cost {r.MsPerStep:F4} ms per 120 Hz step ({r.Cars} car(s){(options.Parallel ? ", parallel" : "")}), state hash {r.StateHash:X16}"));
        var checks = new List<(string, bool)>
        {
            ("speed reached 30 km/h", r.MaxSpeed > 30 / 3.6),
            ("speed below 400 km/h", r.MaxSpeed < 400 / 3.6),
            ("rpm within 1.1x limit", r.MaxRpm <= r.Spec.Engine.RpmLimit * 1.1),
            ("|gLong| < 3", r.MaxAbsGLong < 3),
            ("|gLat| < 3", r.MaxAbsGLat < 3),
            ("on the ground most of the time", r.GroundedFraction > 0.8),
            ("finite state", double.IsFinite(r.Final.Position.X) && double.IsFinite(r.Final.Position.Y)),
        };
        foreach (var (name, ok) in checks)
        {
            o.WriteLine($"{(ok ? "ok   " : "CHECK")} {name}");
        }

        return checks.All(c => c.Item2) ? 0 : 1;
    }

    private static string? FindRoot()
    {
        var env = Environment.GetEnvironmentVariable("SLRR_GAME_ROOT");
        if (!string.IsNullOrEmpty(env) && File.Exists(Path.Combine(env, "sl.rpk")))
        {
            return env;
        }

        for (var d = new DirectoryInfo(Directory.GetCurrentDirectory()); d is not null; d = d.Parent)
        {
            if (File.Exists(Path.Combine(d.FullName, "sl.rpk")))
            {
                return d.FullName;
            }
        }

        return null;
    }
}
#endif
