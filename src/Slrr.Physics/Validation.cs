namespace Slrr.Physics;

/// <summary>One regression check: a measured value of this model and the range first principles allow for this car.</summary>
/// <param name="Basis">How the range was derived (from the car's own data; NOT from the original game).</param>
public sealed record CheckResult(string Name, double Value, string Unit, double Min, double Max, bool Completed, string Basis, string Detail)
{
    public bool Pass => Completed && !double.IsNaN(Value) && Value >= Min && Value <= Max;
}

/// <summary>
/// First-principles numbers of a car from its own data (mass, CoM, wheels, tyres, engine table, gearing, drag). They
/// bound what any sane vehicle model must produce; they are NOT the original game's numbers (parity unverified).
/// </summary>
public sealed class CarEstimates
{
    public double PeakPowerW { get; init; }

    public double PeakPowerRpm { get; init; }

    /// <summary>Mean tyre peak coefficient D*sin(C*pi/2) (the magic formula's maximum at p1 = 0).</summary>
    public double Mu { get; init; }

    public double MuDriven { get; init; }

    /// <summary>Static share of the weight on the driven wheels.</summary>
    public double DrivenShare { get; init; }

    /// <summary>CoM height above the ground at the static stance [m].</summary>
    public double CgHeight { get; init; }

    public double Track { get; init; }

    public double Wheelbase { get; init; }

    /// <summary>Deceleration the brakes alone can give (sum of brake torque / radius / mass) [m/s^2].</summary>
    public double BrakeDecel { get; init; }

    /// <summary>Speed where drive power = drag + rolling resistance [m/s].</summary>
    public double DragLimitedTop { get; init; }

    /// <summary>Speed at RPM_limit in the top gear (driven wheels' mean radius) [m/s].</summary>
    public double GearLimitedTop { get; init; }

    /// <summary>Steady lateral acceleration at which the inner wheels unload (rigid body statics, t / 2h) [g].</summary>
    public double WheelLiftG => CgHeight > 0 ? Track / (2 * CgHeight) : double.PositiveInfinity;

    public static CarEstimates Of(VehicleSpec s)
    {
        double bestP = 0, bestRpm = 0;
        var eng = s.Engine;
        for (var i = 0; i < eng.Torque.Length; i++)
        {
            var rpm = i * (double)eng.TableStep;
            if (rpm > eng.RpmLimit)
            {
                break;
            }

            var p = (eng.Torque[i] - EngineSpec.DynoFrictionTorque(rpm)) * rpm * Math.PI / 30;
            if (p > bestP)
            {
                bestP = p;
                bestRpm = rpm;
            }
        }

        double Peak(WheelSpec w) => Math.Abs(w.Tyre.P[2] * Math.Sin(w.Tyre.P[0] * Math.PI / 2));
        var wheels = s.Wheels;
        var mu = wheels.Count > 0 ? wheels.Average(Peak) : 1;
        var driven = wheels.Where(w => w.Drive > 0).ToList();
        var muDriven = driven.Count > 0 ? driven.Average(Peak) : mu;
        var loads = s.StaticWheelLoads();
        var total = loads.Sum();
        var share = total > 0 ? wheels.Select((w, i) => w.Drive > 0 ? loads[i] : 0).Sum() / total : 0;
        var ground = wheels.Count > 0 ? wheels.Average(w => w.Position.Y - w.Radius) : s.CenterOfMass.Y - 0.5;
        var track = wheels.Count > 1 ? wheels.Max(w => w.Position.X) - wheels.Min(w => w.Position.X) : 1.5;
        var wheelbase = wheels.Count > 1 ? wheels.Max(w => w.Position.Z) - wheels.Min(w => w.Position.Z) : 2.5;
        var brake = wheels.Sum(w => w.BrakeTorque / Math.Max(0.1, w.Radius)) / s.Mass;
        var eta = s.Drivetrain.Efficiency;
        var drag = 0.5 * s.AirDensity * s.DragCoefficient * s.FrontalArea;
        var roll = Vehicle.RollingResistanceScale * (wheels.Count > 0 ? wheels.Average(w => w.Tyre.RollResistance) : 0) + SurfaceDefaults.RollingResistance * Vehicle.RollingResistanceScale;
        double vDrag = 0;
        if (bestP > 0)
        {
            // P*eta = drag v^3 + roll m g v, solved by bisection
            double lo = 0, hi = 200;
            for (var k = 0; k < 60; k++)
            {
                var v = (lo + hi) / 2;
                if (drag * v * v * v + roll * s.Mass * s.Gravity * v < bestP * eta)
                {
                    lo = v;
                }
                else
                {
                    hi = v;
                }
            }

            vDrag = lo;
        }

        var dt = s.Drivetrain;
        var top = Math.Min(dt.Gears, dt.Ratios.Length - 2);
        var topRatio = top >= 1 && top < dt.Ratios.Length ? dt.Ratios[top] * dt.FinalDrive : 0;
        var rDriven = driven.Count > 0 ? driven.Average(w => w.Radius) : 0.3;
        var vGear = topRatio > 0 ? eng.RpmLimit * Math.PI / 30 / topRatio * rDriven : double.PositiveInfinity;
        return new CarEstimates
        {
            PeakPowerW = bestP,
            PeakPowerRpm = bestRpm,
            Mu = mu,
            MuDriven = muDriven,
            DrivenShare = share,
            CgHeight = s.CenterOfMass.Y - ground,
            Track = track,
            Wheelbase = wheelbase,
            BrakeDecel = brake,
            DragLimitedTop = vDrag,
            GearLimitedTop = vGear,
        };
    }
}

/// <summary>
/// Regression scenarios with expected ranges from first principles and the car's own data (no original-game data
/// exists to compare with: parity with the original game is UNVERIFIED). Every tolerance below is GUESSED and chosen
/// to flag broken cars and broken physics (no torque, no grip, flips, explosions), not to grade realism.
/// </summary>
public static class Validation
{
    private const double Kmh = 1 / 3.6;

    public static IReadOnlyList<CheckResult> Run(VehicleSpec spec)
    {
        var e = CarEstimates.Of(spec);
        return
        [
            ZeroTo100(spec, e), TopSpeed(spec, e), Braking(spec, e), Skidpad(spec, e), Slalom(spec, e), BrakeInTurn(spec, e),
            DriftEntry(spec, e), JumpLanding(spec, e),
        ];
    }

    public static CheckResult ZeroTo100(VehicleSpec spec, CarEstimates e)
    {
        var r = Scenarios.ZeroTo100(spec, maxSeconds: 40);
        var v = 100 * Kmh;
        var tPower = e.PeakPowerW > 0 ? 0.5 * spec.Mass * v * v / (e.PeakPowerW * spec.Drivetrain.Efficiency) : double.PositiveInfinity;
        var tGrip = e.MuDriven * e.DrivenShare > 0 ? v / (e.MuDriven * e.DrivenShare * spec.Gravity) : double.PositiveInfinity;
        var lower = 0.9 * Math.Max(tPower, tGrip);
        var upper = 3.0 * lower + 2.0;
        return new CheckResult("0-100 km/h", r.Value, "s", lower, upper, r.Completed,
            F($"lower = 0.9 max(power m v^2/2P {tPower:F2}, traction v/(mu share g) {tGrip:F2}); upper = 3 lower + 2 s"), r.Detail);
    }

    public static CheckResult TopSpeed(VehicleSpec spec, CarEstimates e)
    {
        var r = Scenarios.TopSpeed(spec, maxSeconds: 150);
        var limit = Math.Min(e.DragLimitedTop, e.GearLimitedTop) / Kmh;
        return new CheckResult("top speed", r.Value, "km/h", 0.75 * limit, 1.05 * limit, r.Completed && r.Value > 1,
            F($"min(drag+rolling-limited {e.DragLimitedTop / Kmh:F0}, gear-limited {e.GearLimitedTop / Kmh:F0}) x [0.75, 1.05]"), r.Detail);
    }

    public static CheckResult Braking(VehicleSpec spec, CarEstimates e)
    {
        var r = Scenarios.Braking100(spec);
        var v = 100 * Kmh;
        var lower = v * v / (2 * spec.Gravity * e.Mu * 1.1);
        var decel = Math.Min(0.8 * e.Mu * spec.Gravity, e.BrakeDecel);
        var upper = v * v / (2 * Math.Max(0.5, decel) * 0.7) + 5;
        return new CheckResult("100-0 km/h", r.Value, "m", lower, upper, r.Completed,
            F($"lower v^2/(2 g 1.1 mu {e.Mu:F2}); upper v^2/(2 * 0.7 * min(0.8 mu g, brakes {e.BrakeDecel:F1} m/s^2)) + 5 m"), r.Detail);
    }

    public static CheckResult Skidpad(VehicleSpec spec, CarEstimates e)
    {
        var r = Scenarios.Skidpad(spec);
        return new CheckResult("skidpad R40", r.Value, "g", 0.55 * e.Mu, 1.15 * e.Mu, r.Completed,
            F($"0.55..1.15 x tyre peak mu {e.Mu:F2}; static inner-wheel lift at {e.WheelLiftG:F2} g (t/2h, h {e.CgHeight:F2} m)"), r.Detail);
    }

    /// <summary>Slalom: 6 gates 18 m apart, +-1.5 m, entered at 50 km/h with a pure-pursuit driver; value = peak sideslip.</summary>
    public static CheckResult Slalom(VehicleSpec spec, CarEstimates e)
    {
        var car = new Vehicle(spec, new FlatGround());
        var shifter = new Scenarios.AutoShifter(spec);
        const double spacing = 18, amp = 1.5, entry = 50 * Kmh;
        var maxSteer = MaxSteer(spec);
        double peakSlip = 0, maxErr = 0;
        var done = false;
        var flipped = false;
        while (car.Time < 40)
        {
            var s = -car.Position.Z;
            var v = car.ForwardSpeed;
            var throttle = Math.Clamp((entry - v) * 0.5 + 0.2, 0, 1);
            var input = shifter.Control(car, throttle);
            double PathX(double sz) => sz < 20 ? 0 : amp * Math.Sin(Math.PI * (sz - 20) / spacing);
            var look = Math.Max(6, 0.6 * Math.Abs(v));
            var aim = new Vec3(PathX(s + look), car.Position.Y, -(s + look)) - car.Position;
            var alpha = Math.Atan2(aim.Dot(car.Left), aim.Dot(car.Forward));
            input.Steer = Math.Clamp(Math.Atan(2 * e.Wheelbase * Math.Sin(alpha) / look) / maxSteer, -1, 1);
            car.Step(input);
            if (s > 20)
            {
                peakSlip = Math.Max(peakSlip, Sideslip(car));
                maxErr = Math.Max(maxErr, Math.Abs(car.Position.X - PathX(s)));
            }

            flipped |= car.Up.Y < 0.5;
            if (s > 20 + 6 * spacing)
            {
                done = true;
                break;
            }
        }

        return new CheckResult("slalom 18 m @50", peakSlip, "deg", 0, 12, done && !flipped && maxErr < 2.5,
            "peak |sideslip| 0..12 deg, path error < 2.5 m, upright (GUESSED driver and limits)", F($"max path error {maxErr:F2} m{(flipped ? ", ROLLED" : "")}"));
    }

    /// <summary>Deceleration the brake-in-turn check asks the brakes for (0.3 g; the pedal is scaled per car, no ABS).</summary>
    public const double BrakeInTurnDecelG = 0.3;

    /// <summary>Brake in a turn: R 50 m circle at 60 % of the tyre-limited speed, then the pedal that gives 0.3 g on a
    /// straight; value = peak sideslip above 5 m/s.</summary>
    public static CheckResult BrakeInTurn(VehicleSpec spec, CarEstimates e)
    {
        const double radius = 50;
        var car = new Vehicle(spec, new FlatGround());
        var shifter = new Scenarios.AutoShifter(spec);
        var target = Math.Sqrt(0.6 * e.Mu * spec.Gravity * radius);
        var center = new Vec3(-radius, 0, 0);
        var maxSteer = MaxSteer(spec);
        var pedal = e.BrakeDecel > 0 ? Math.Clamp(BrakeInTurnDecelG * spec.Gravity / e.BrakeDecel, 0.05, 1) : 1;
        double peak = 0, brakeStart = -1;
        var flipped = false;
        var stopped = false;
        while (car.Time < 60)
        {
            var v = car.ForwardSpeed;
            DriverInput input;
            var settled = car.Time > 8 && Math.Abs(v - target) < 0.5;
            if (brakeStart < 0 && settled)
            {
                brakeStart = car.Time;
            }

            if (brakeStart < 0)
            {
                input = shifter.Control(car, Math.Clamp((target - v) * 0.6 + 0.25, 0, 1));
            }
            else
            {
                input = new DriverInput { Brake = pedal, Clutch = 0, Gear = shifter.Gear };
            }

            var rel = car.Position - center;
            var look = Math.Max(6.0, 0.8 * Math.Abs(v));
            var theta = Math.Atan2(rel.Z, rel.X) - look / radius;
            var aim = new Vec3(center.X + radius * Math.Cos(theta), car.Position.Y, center.Z + radius * Math.Sin(theta)) - car.Position;
            var alpha = Math.Atan2(aim.Dot(car.Left), aim.Dot(car.Forward));
            input.Steer = Math.Clamp(Math.Atan(2 * e.Wheelbase * Math.Sin(alpha) / look) / maxSteer, -1, 1);
            car.Step(input);
            flipped |= car.Up.Y < 0.5;
            if (brakeStart >= 0)
            {
                peak = Math.Max(peak, Sideslip(car));
                if (car.Velocity.Length < 0.3)
                {
                    stopped = true;
                    break;
                }
            }
        }

        return new CheckResult("brake in turn R50", peak, "deg", 0, 25, stopped && !flipped,
            F($"brake pedal {pedal:F2} (0.3 g on a straight) from {target / Kmh:F0} km/h on the circle, stops without spinning: peak |sideslip| 0..25 deg (GUESSED)"),
            brakeStart < 0 ? "never settled on the circle" : F($"brake at {brakeStart:F1} s, {(stopped ? $"stopped at {car.Time:F1} s" : "did not stop")}{(flipped ? ", ROLLED" : "")}"));
    }

    /// <summary>Drift entry: 60 km/h, full lock and handbrake for 0.5 s, then counter-steer; value = peak sideslip.</summary>
    public static CheckResult DriftEntry(VehicleSpec spec, CarEstimates e)
    {
        var car = new Vehicle(spec, new FlatGround());
        var shifter = new Scenarios.AutoShifter(spec);
        const double entry = 60 * Kmh;
        double peak = 0, t0 = -1;
        var flipped = false;
        while (car.Time < 40)
        {
            var v = car.ForwardSpeed;
            if (t0 < 0 && car.Time > 3 && v >= entry)
            {
                t0 = car.Time;
            }

            DriverInput input;
            if (t0 < 0)
            {
                input = shifter.Control(car, Math.Clamp((entry + 1 - v) * 0.5 + 0.3, 0, 1));
            }
            else
            {
                var t = car.Time - t0;
                input = shifter.Control(car, t < 0.5 ? 0 : 0.5);
                input.Steer = t < 0.5 ? 1 : -0.3;
                input.HandBrake = t < 0.5 ? 1 : 0;
                peak = Math.Max(peak, Sideslip(car));
                if (t > 3)
                {
                    break;
                }
            }

            car.Step(input);
            flipped |= car.Up.Y < 0.5;
        }

        return new CheckResult("drift entry @60", peak, "deg", 8, 180, t0 >= 0 && !flipped,
            "full lock + handbrake 0.5 s must make the rear slide (peak |sideslip| >= 8 deg) without rolling the car (GUESSED)",
            t0 < 0 ? "never reached 60 km/h" : F($"entry at {t0:F1} s{(flipped ? ", ROLLED" : "")}"));
    }

    /// <summary>Jump landing: dropped level from 1 m above the ground at 15 m/s; value = peak vertical acceleration.</summary>
    public static CheckResult JumpLanding(VehicleSpec spec, CarEstimates e)
    {
        var car = new Vehicle(spec, new FlatGround());
        car.Position += new Vec3(0, 1.0, 0);
        car.Velocity = car.Forward * 15;
        car.SyncContacts();
        double peak = 0, prevVy = car.Velocity.Y;
        var flipped = false;
        var input = new DriverInput { Clutch = 0, Gear = 0 };
        var settledAt = -1.0;
        var minClear = double.MaxValue;
        while (car.Time < 4)
        {
            car.Step(input);
            var ay = (car.Velocity.Y - prevVy) * Vehicle.StepRate / spec.Gravity + 1;
            prevVy = car.Velocity.Y;
            peak = Math.Max(peak, ay);
            flipped |= car.Up.Y < 0.7;
            minClear = Math.Min(minClear, car.Position.Y - (spec.CenterOfMass.Y - spec.Wheels.Select(w => w.Position.Y - w.Radius).DefaultIfEmpty(-0.5).Average()));
            if (settledAt < 0 && car.Time > 1 && Math.Abs(car.Velocity.Y) < 0.1 && car.Wheels.All(w => w.Grounded))
            {
                settledAt = car.Time;
            }
        }

        var fall = Math.Sqrt(2 * spec.Gravity * 1.0);
        var stroke = spec.Wheels.Count > 0 ? spec.Wheels.Average(w => w.MaxLength - w.MinLength) : 0.15;
        var lower = 1 + fall * fall / (2 * spec.Gravity * (stroke + 0.35));
        return new CheckResult("jump landing 1 m", peak, "g", lower, 25, settledAt > 0 && !flipped && minClear > -0.35,
            F($"1 + v^2/(2 g (stroke {stroke:F2} + 0.35)) .. 25 g; settles within 4 s, upright, CoM no more than 0.35 m below its static height"),
            F($"settled at {settledAt:F2} s, CoM lowest {minClear:F2} m from static{(flipped ? ", ROLLED" : "")}"));
    }

    private static double MaxSteer(VehicleSpec spec) => spec.Wheels.Where(w => w.Steers).Select(w => w.MaxSteer).DefaultIfEmpty(0.6).Max();

    private static double Sideslip(Vehicle car)
    {
        var v = car.Velocity;
        var fwd = v.Dot(car.Forward);
        var lat = v.Dot(car.Left);
        return Math.Sqrt(fwd * fwd + lat * lat) < 5 ? 0 : Math.Abs(Math.Atan2(lat, Math.Abs(fwd))) * 180 / Math.PI;
    }

    private static string F(FormattableString s) => FormattableString.Invariant(s);
}
