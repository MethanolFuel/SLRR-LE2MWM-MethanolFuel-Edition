using System.Runtime.CompilerServices;
using Slrr.Game.Physics;
using Slrr.Physics;

namespace Slrr.AI.Driving;

/// <summary>
/// What a driver needs to know about its car: geometry for steering, grip and braking limits for speed targets.
/// All values are this project's estimates (labelled per field); nothing is read from the original AI.
/// </summary>
public sealed record VehicleCaps
{
    public const double G = 9.8067;

    /// <summary>Axle distance (m): from the wheel layout (FullCar) or <see cref="SimpleCarSpec.Wheelbase"/>.</summary>
    public double Wheelbase { get; init; } = 2.6;

    /// <summary>Road-wheel angle at full lock (rad).</summary>
    public double MaxSteerAngle { get; init; } = 0.6;

    /// <summary>Lateral acceleration the car can hold (m/s²). GUESSED default 0.8 g; <see cref="Measure"/> runs the
    /// Slrr.Physics skidpad; <c>AI_params a b</c> overrides it (GUESSED meaning, see <see cref="AiLevel"/>).</summary>
    public double LateralAccel { get; init; } = 0.8 * G;

    /// <summary>Deceleration the car can hold under braking (m/s²). GUESSED default 0.8 g.</summary>
    public double BrakeDecel { get; init; } = 0.8 * G;

    /// <summary>Top speed the driver ever asks for (m/s); SimpleCars: their max speed.</summary>
    public double TopSpeed { get; init; } = 90;

    /// <summary>True when the car can drive backwards (FullCar's automatic box: brake at standstill → reverse).</summary>
    public bool CanReverse { get; init; } = true;

    /// <summary>Where the numbers came from (report text).</summary>
    public string Source { get; init; } = "default";

    private static readonly ConditionalWeakTable<VehicleSpec, VehicleCaps> s_measured = new();

    /// <summary>Caps from the car's own data without running anything (grip/brake GUESSED 0.8 g for FullCars).</summary>
    public static VehicleCaps For(SimCar car) => car switch
    {
        FullCar f => FromSpec(f.Spec),
        SimpleCar s => new VehicleCaps
        {
            Wheelbase = s.Spec.Wheelbase,
            MaxSteerAngle = s.Spec.MaxSteer,
            LateralAccel = s.Spec.LateralGrip,
            BrakeDecel = s.Spec.Deceleration,
            TopSpeed = s.Spec.MaxSpeed,
            CanReverse = false,
            Source = "SimpleCarSpec",
        },
        _ => new VehicleCaps(),
    };

    public static VehicleCaps FromSpec(VehicleSpec spec)
    {
        var wb = spec.Wheels.Count > 0 ? spec.Wheels.Max(w => w.Position.Z) - spec.Wheels.Min(w => w.Position.Z) : 2.6;
        var steer = spec.Wheels.Where(w => w.Steers).Select(w => w.MaxSteer).DefaultIfEmpty(0.6).Max();
        var roll = RollLimit(spec);
        return new VehicleCaps
        {
            Wheelbase = wb,
            MaxSteerAngle = steer,
            LateralAccel = Math.Min(0.8 * G, roll),
            Source = FormattableString.Invariant($"spec geometry, grip/brake GUESSED 0.8 g, roll limit {roll / G:F2} g"),
        };
    }

    /// <summary>
    /// Lateral acceleration kept below the static rollover threshold g·track/(2·h_CoM), times 0.6 for transients (GUESSED
    /// margin). Needed because Slrr.Physics cars tip over before they slide (MEASURED on the synthetic car: threshold
    /// 1.07 g against a 0.96 g skidpad; it rolled in the first race runs; docs/race/game-physics.md open question 7 saw
    /// DevilSport lift two wheels at ~1 g).
    /// </summary>
    public static double RollLimit(VehicleSpec spec)
    {
        if (spec.Wheels.Count == 0)
        {
            return 0.8 * G;
        }

        var track = spec.Wheels.Max(w => w.Position.X) - spec.Wheels.Min(w => w.Position.X);
        var ground = spec.Wheels.Average(w => w.Position.Y - w.Radius);
        var h = Math.Max(0.2, spec.CenterOfMass.Y - ground);
        return 0.6 * G * track / (2 * h);
    }

    /// <summary>
    /// Caps with grip and braking measured by Slrr.Physics' own scenarios on flat ground (skidpad R40, 100-0 km/h);
    /// cached per spec instance. Costs ~1 s of wall time per spec. The numbers are the model's, not the game's.
    /// </summary>
    public static VehicleCaps Measure(VehicleSpec spec)
    {
        lock (s_measured)
        {
            if (s_measured.TryGetValue(spec, out var c))
            {
                return c;
            }

            var geo = FromSpec(spec);
            var skid = Scenarios.Skidpad(spec);
            var brake = Scenarios.Braking100(spec);
            var lat = Math.Min(skid.Completed && skid.Value > 0.2 ? skid.Value * G : 0.8 * G, RollLimit(spec));
            var v = 100 / 3.6;
            // planning uses at most 0.8 g: the scenario brakes without ABS on locked wheels, the AI modulates (GUESSED)
            var dec = Math.Min(brake.Completed && brake.Value > 1 ? v * v / (2 * brake.Value) : geo.BrakeDecel, 0.8 * G);
            c = geo with
            {
                LateralAccel = lat,
                BrakeDecel = dec,
                Source = FormattableString.Invariant($"measured: skidpad {skid.Value:F2} g, braking {dec / G:F2} g (Slrr.Physics scenarios); used lateral {lat / G:F2} g (roll limit {RollLimit(spec) / G:F2} g)"),
            };
            s_measured.Add(spec, c);
            return c;
        }
    }
}

/// <summary>
/// AI skill numbers. MEASURED (exe 0x42eac3, the brain's <c>AI_level %f</c> handler): the value is clamped to [0, 1] and
/// stored as <c>0.5 + (v - 0.5) * 0.5</c> (so 0.25..0.75); the brain's default is 0.5 and <c>AI_NightRace [f]</c> resets it
/// to 0.5 (0x42e705: also stores f, default 100, in another field). <c>AI_params a b</c> stores two floats twice
/// (0x42ec11); shipped values: ROC "8.1 0.36", "4.0 0.4", "5.4 0.36", "7.5 0.4", "7.5 0.42", "8.0 0.4", TestTrack police
/// "50.0 50.35". GUESSED: how the stored level and the params change the driving (below).
/// </summary>
public static class AiLevel
{
    /// <summary>The exe's stored level for an <c>AI_level v</c> verb (MEASURED transform).</summary>
    public static double Stored(double v) => 0.5 + ((Math.Clamp(v, 0, 1) - 0.5) * 0.5);

    /// <summary>Default stored level (MEASURED 0.5).</summary>
    public const double Default = 0.5;

    /// <summary>Fraction of the lateral limit used in corners: 0.80 at the lowest stored level, 0.95 at the highest (GUESSED).</summary>
    public static double GripUse(double stored) => 0.80 + (0.30 * (Math.Clamp(stored, 0.25, 0.75) - 0.25));

    /// <summary>Fraction of the braking limit used when planning (GUESSED 0.75..0.90).</summary>
    public static double BrakeUse(double stored) => 0.75 + (0.30 * (Math.Clamp(stored, 0.25, 0.75) - 0.25));

    /// <summary>
    /// Caps with <c>AI_params a b</c> applied (GUESSED meaning: a = lateral acceleration limit in m/s², b = braking limit in g;
    /// chosen because the shipped ROC values 4.0..8.1 / 0.36..0.42 are plausible as such and the police "50 50.35" reads
    /// as "no limit"). Values ≤ 0 leave the caps unchanged; the params never raise the car's own limits.
    /// </summary>
    public static VehicleCaps WithParams(VehicleCaps caps, float a, float b) => caps with
    {
        LateralAccel = a > 0 ? Math.Min(caps.LateralAccel, a) : caps.LateralAccel,
        BrakeDecel = b > 0 ? Math.Min(caps.BrakeDecel, b * VehicleCaps.G) : caps.BrakeDecel,
        Source = caps.Source + FormattableString.Invariant($"; AI_params {a} {b}"),
    };
}
