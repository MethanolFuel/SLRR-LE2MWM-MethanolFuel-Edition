#if SLRR_PHYSICS
// Integration (lead): add <ProjectReference Include="..\..\src\Slrr.Physics\Slrr.Physics.csproj" /> and
// <DefineConstants>$(DefineConstants);SLRR_PHYSICS</DefineConstants> to Slrr.Cli.csproj, then
// "drive" => CmdDrive.Run(args[1..], o, e) in Commands.cs. Until then this file compiles to nothing in Slrr.Cli.
using System.Globalization;
using Slrr.Engine.Parts.RawEdit;
using Slrr.Physics;
using Slrr.Physics.Loading;

namespace Slrr.Cli;

/// <summary>
/// <c>drive</c>: loads a car from the shipped data through the resource + parts layers (the car's own scripts build
/// it), turns it into a <see cref="VehicleSpec"/> and runs scripted scenarios on flat ground (src/Slrr.Physics,
/// docs/phase4/physics.md). Parity with the original game is UNVERIFIED.
/// <code>
/// drive --list [--root dir]                       car gametypes found under cars\
/// drive &lt;car&gt; [--root dir] [--gi] [--spec] [--dyno] [--scenario all|0-100|top|brake|skidpad]
/// </code>
/// </summary>
public static class CmdDrive
{
    public const string Usage = """
        drive --list [--root dir]                         list the car gametypes (native car entries under cars\)
        drive <car> [--root dir] [--gi] [--spec] [--dyno] [--scenario all|0-100|top|brake|skidpad]
                                                          build <car> from its stock parts and run test drives
        """;

    public static int Run(string[] args, TextWriter o, TextWriter e)
    {
        var root = Option(args, "--root") ?? Directory.GetCurrentDirectory();
        if (!File.Exists(Path.Combine(root, "sl.rpk")))
        {
            e.WriteLine($"drive: no game data in {root} (sl.rpk missing); use --root");
            return 2;
        }

        if (args.Contains("--list"))
        {
            foreach (var c in CarLoader.FindCars(root))
            {
                o.WriteLine($"{c.Name,-40} {c.Archive,-36} {c.Cfg}");
            }

            return 0;
        }

        var query = Positional(args).FirstOrDefault();
        if (query is null)
        {
            e.WriteLine(Usage);
            return 2;
        }

        var type = CarLoader.FindCar(root, query);
        if (type is null)
        {
            e.WriteLine($"drive: no car matches '{query}' (try --list)");
            return 2;
        }

        o.WriteLine($"car        {type.Name} ({type.Archive}, {type.Cfg})");
        var car = CarLoader.Load(root, type);
        o.WriteLine($"parts      {car.Parts.Count} installed by the scripts; Chassis.updatevariables {(car.ScriptsCompleted ? "completed" : "FAILED (see log)")}");
        foreach (var line in car.Log.Where(l => !l.StartsWith("parts: install", StringComparison.Ordinal)).Take(12))
        {
            o.WriteLine($"  log: {line}");
        }

        if (car.EngineCompletedInCSharp)
        {
            o.WriteLine("  WARNING: engine numbers come from a C# replay after a VM error, NOT from the game's scripts");
        }

        var options = new SpecOptions { Settings = args.Contains("--gi") ? EngineSettings.MethanolFuel() : null };
        var spec = SpecBuilder.Build(car.Chassis, car.Dyno, options);
        PrintSpec(spec, o, args.Contains("--spec"), args.Contains("--dyno"));

        var which = Option(args, "--scenario") ?? "all";
        var results = new List<ScenarioResult>();
        if (which is "all" or "0-100")
        {
            results.Add(Scenarios.ZeroTo100(spec));
        }

        if (which is "all" or "top")
        {
            results.Add(Scenarios.TopSpeed(spec));
        }

        if (which is "all" or "brake")
        {
            results.Add(Scenarios.Braking100(spec));
        }

        if (which is "all" or "skidpad")
        {
            results.Add(Scenarios.Skidpad(spec));
        }

        o.WriteLine();
        o.WriteLine("scenario results (flat ground, 120 Hz, GUESSED driver; parity with the original game UNVERIFIED)");
        foreach (var r in results)
        {
            o.WriteLine(F($"  {r.Name,-14} {(double.IsNaN(r.Value) ? "n/a" : r.Value.ToString("F2", CultureInfo.InvariantCulture)),8} {r.Unit,-5} {(r.Completed ? "" : "(incomplete) ")}{r.Detail}"));
        }

        Plausibility(spec, results, o);
        return 0;
    }

    private static void PrintSpec(VehicleSpec s, TextWriter o, bool full, bool dyno)
    {
        var eng = s.Engine;
        var peak = PeakPower(eng);
        o.WriteLine(F($"mass       {s.Mass:F1} kg, CM {s.CenterOfMass}, Cd {s.DragCoefficient:F3}, A {s.FrontalArea:F2} m^2"));
        o.WriteLine(F($"engine     idle {eng.IdleRpm:F0} rpm, limit {eng.RpmLimit:F0}, max {eng.MaxRpm:F0}, inertia {eng.Inertia:F3} kg m^2 (script {eng.ScriptInertia:F3}), table {eng.Torque.Length} x {eng.TableStep} rpm"));
        o.WriteLine(F($"dyno       maxTorque {s.DynoMaxTorque:F1} N m @ {s.DynoRpmMaxTorque:F0}, script maxHP {s.ScriptMaxHp:F1} @ {s.DynoRpmMaxHp:F0} rpm; table torque*omega peak {peak.Kw:F1} kW ({peak.Kw * 1.341:F1} hp) @ {peak.Rpm:F0} rpm"));
        var dt = s.Drivetrain;
        o.WriteLine(F($"gearbox    {dt.Gears} gears [{string.Join(" ", dt.Ratios.Select(r => r.ToString("F2", CultureInfo.InvariantCulture)))}], final {dt.FinalDrive:F2}, clutch {dt.ClutchCapacity:F0} N m"));
        foreach (var w in s.Wheels)
        {
            var p = w.Tyre.P;
            o.WriteLine(F($"wheel {w.Name}   pos {w.Position} r {w.Radius:F3} drive {w.Drive:F2} brake {w.BrakeTorque:F0} N m, k {w.SpringRate:F0} N/m rest {w.RestLength:F3} [{w.MinLength:F3}..{w.MaxLength:F3}] damp {w.DampingBump:F0}/{w.DampingRebound:F0}, tyre C {p[0]:F3} D {p[2]:F3} p4 {p[4]:F2} E {p[8]:F2} maxL {p[11]:F0} fx {p[12]:F2}"));
        }

        foreach (var b in s.Rollbars)
        {
            o.WriteLine(F($"rollbar    {b.Wheel1}-{b.Wheel2} {b.Rate:F0} N/m damping {b.Damping:F0}"));
        }

        if (full)
        {
            foreach (var n in s.Notes)
            {
                o.WriteLine($"note       {n}");
            }
        }

        if (dyno)
        {
            for (var i = 0; i < eng.Torque.Length; i++)
            {
                var rpm = i * eng.TableStep;
                o.WriteLine(F($"  {rpm,6} rpm  {eng.Torque[i],8:F1} N m  {eng.Torque[i] * rpm * 0.10471975803375244 / 1000,7:F1} kW"));
            }
        }
    }

    private static (double Kw, double Rpm) PeakPower(EngineSpec eng)
    {
        var best = (Kw: 0.0, Rpm: 0.0);
        for (var i = 0; i < eng.Torque.Length; i++)
        {
            var rpm = i * eng.TableStep;
            var kw = (eng.Torque[i] - EngineSpec.DynoFrictionTorque(rpm)) * rpm * 0.10471975803375244 / 1000;
            if (kw > best.Kw && rpm <= eng.RpmLimit)
            {
                best = (kw, rpm);
            }
        }

        return best;
    }

    /// <summary>Order-of-magnitude checks against the car's own numbers (not against the game).</summary>
    private static void Plausibility(VehicleSpec s, List<ScenarioResult> results, TextWriter o)
    {
        var p = PeakPower(s.Engine).Kw * 1000;
        o.WriteLine();
        o.WriteLine("plausibility (simple physics estimates from the car's own mass/power/drag; not game data)");
        if (p <= 0)
        {
            o.WriteLine("  no engine power: skipped");
            return;
        }

        var drag = 0.5 * s.AirDensity * s.DragCoefficient * s.FrontalArea;
        var vMax = Math.Cbrt(p * s.Drivetrain.Efficiency / drag);
        var v100 = 100 / 3.6;
        var tMin = 0.5 * s.Mass * v100 * v100 / (p * s.Drivetrain.Efficiency);
        var mu = s.Wheels.Count > 0 ? s.Wheels.Average(w => w.Tyre.P[2] * Math.Sin(w.Tyre.P[0] * Math.PI / 2)) : 1;
        o.WriteLine(F($"  power/weight {p / 1000 / s.Mass * 1000:F0} W/kg; 0-100 lower bound (all power, no losses) {tMin:F2} s; drag-limited top speed {vMax * 3.6:F0} km/h; tyre D*sin(C*pi/2) ~ {mu:F2}"));
        foreach (var r in results.Where(r => r.Completed))
        {
            var verdict = r.Name switch
            {
                "0-100 km/h" => r.Value >= tMin && r.Value <= tMin * 4 ? "plausible" : "CHECK",
                "top speed" => r.Value <= vMax * 3.6 * 1.05 && r.Value > vMax * 3.6 * 0.4 ? "plausible (drag or gear limited)" : "CHECK",
                "100-0 km/h" => r.Value >= v100 * v100 / (2 * 2.0 * s.Gravity) && r.Value <= v100 * v100 / (2 * 0.4 * s.Gravity) ? "plausible (0.4..2 g)" : "CHECK",
                _ => r.Value is > 0.3 and < 2.5 ? "plausible (0.3..2.5 g)" : "CHECK",
            };
            o.WriteLine(F($"  {r.Name,-14} {verdict}"));
        }
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
            if (args[i] is "--root" or "--scenario")
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
