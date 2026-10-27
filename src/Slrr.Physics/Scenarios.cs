namespace Slrr.Physics;

/// <summary>Outcome of one scripted run.</summary>
public sealed record ScenarioResult(string Name, double Value, string Unit, bool Completed, string Detail);

/// <summary>
/// Scripted test drives on a <see cref="IGroundQuery"/> (normally <see cref="FlatGround"/>). The driver logic
/// (shift points, shift time, no ABS/traction control, skidpad controller) is GUESSED and only meant to produce
/// comparable numbers; it is not a model of the game's AI or automatic gearbox.
/// </summary>
public static class Scenarios
{
    public const double Kmh = 1 / 3.6;

    /// <summary>Full-throttle launch from rest; seconds to 100 km/h.</summary>
    public static ScenarioResult ZeroTo100(VehicleSpec spec, IGroundQuery? ground = null, double maxSeconds = 60)
    {
        var car = new Vehicle(spec, ground ?? new FlatGround());
        var driver = new AutoShifter(spec);
        while (car.Time < maxSeconds)
        {
            car.Step(driver.Control(car, throttle: 1));
            if (car.ForwardSpeed >= 100 * Kmh)
            {
                return new ScenarioResult("0-100 km/h", car.Time, "s", true, Fmt($"{driver.Shifts} shifts, {Distance(car):F0} m"));
            }
        }

        return new ScenarioResult("0-100 km/h", double.NaN, "s", false, Fmt($"reached {car.ForwardSpeed / Kmh:F1} km/h in {maxSeconds} s"));
    }

    /// <summary>Full throttle until the speed stops rising (gain &lt; 0.2 km/h over 5 s) or the time runs out.</summary>
    public static ScenarioResult TopSpeed(VehicleSpec spec, IGroundQuery? ground = null, double maxSeconds = 180)
    {
        var car = new Vehicle(spec, ground ?? new FlatGround());
        var driver = new AutoShifter(spec);
        double best = 0, mark = 0, markTime = 0;
        while (car.Time < maxSeconds)
        {
            car.Step(driver.Control(car, throttle: 1));
            best = Math.Max(best, car.ForwardSpeed);
            if (car.Time - markTime >= 5)
            {
                if (best - mark < 0.2 * Kmh && car.Time > 10)
                {
                    return new ScenarioResult("top speed", best / Kmh, "km/h", true, Fmt($"gear {driver.Gear}, {car.EngineRpm:F0} rpm after {car.Time:F0} s"));
                }

                mark = best;
                markTime = car.Time;
            }
        }

        return new ScenarioResult("top speed", best / Kmh, "km/h", false, Fmt($"still accelerating after {maxSeconds} s (gear {driver.Gear})"));
    }

    /// <summary>Accelerate to 100 km/h, then full brake with the clutch open (no ABS); metres to standstill.</summary>
    public static ScenarioResult Braking100(VehicleSpec spec, IGroundQuery? ground = null)
    {
        var car = new Vehicle(spec, ground ?? new FlatGround());
        var driver = new AutoShifter(spec);
        while (car.ForwardSpeed < 100 * Kmh)
        {
            car.Step(driver.Control(car, throttle: 1));
            if (car.Time > 90)
            {
                return new ScenarioResult("100-0 km/h", double.NaN, "m", false, "never reached 100 km/h");
            }
        }

        var start = car.Position;
        var t0 = car.Time;
        var input = new DriverInput { Throttle = 0, Brake = 1, Clutch = 0, Gear = driver.Gear };
        while (car.ForwardSpeed > 0.05 && car.Time - t0 < 30)
        {
            car.Step(input);
        }

        var d = Horizontal(car.Position - start);
        var locked = car.Wheels.Count(w => w.Spin == 0);
        return new ScenarioResult("100-0 km/h", d, "m", car.ForwardSpeed <= 0.05, Fmt($"{car.Time - t0:F2} s, mean {100 * Kmh / (car.Time - t0) / spec.Gravity:F2} g, {locked} wheels locked at the end"));
    }

    /// <summary>
    /// Constant-radius skidpad (default 40 m): a speed controller raises the target in 0.5 m/s steps every 3 s, a
    /// steering controller tracks the circle; the result is the highest lateral acceleration held for a whole step with
    /// the path error under 1 m.
    /// </summary>
    public static ScenarioResult Skidpad(VehicleSpec spec, double radius = 40, IGroundQuery? ground = null)
    {
        var car = new Vehicle(spec, ground ?? new FlatGround());
        var driver = new AutoShifter(spec);
        var center = new Vec3(-radius, 0, 0); // car starts at the origin heading -Z, turning left around (-R, 0, 0)
        double target = 8, best = 0, levelStart = 0, levelAccSum = 0, levelErrMax = 0;
        var levelSamples = 0;
        var stop = "time limit";
        while (car.Time < 240)
        {
            // speed: accelerate up to the target, then hold
            var v = car.ForwardSpeed;
            var throttle = Math.Clamp((target - v) * 0.6 + 0.25, 0, 1);

            // steering: pure pursuit of a point on the circle a look-ahead distance ahead
            var rel = car.Position - center;
            var dist = Math.Sqrt(rel.X * rel.X + rel.Z * rel.Z);
            var err = dist - radius;
            var wheelbase = spec.Wheels.Count > 0 ? spec.Wheels.Max(w => w.Position.Z) - spec.Wheels.Min(w => w.Position.Z) : 2.5;
            var maxSteer = spec.Wheels.Where(w => w.Steers).Select(w => w.MaxSteer).DefaultIfEmpty(0.6).Max();
            var lookAhead = Math.Max(6.0, 0.8 * Math.Abs(v));
            var theta = Math.Atan2(rel.Z, rel.X) - lookAhead / radius; // travel is clockwise in (x, z)
            var aim = new Vec3(center.X + radius * Math.Cos(theta), car.Position.Y, center.Z + radius * Math.Sin(theta)) - car.Position;
            var alpha = Math.Atan2(aim.Dot(car.Left), aim.Dot(car.Forward));
            var delta = Math.Atan(2 * wheelbase * Math.Sin(alpha) / lookAhead);
            var input = driver.Control(car, throttle);
            input.Steer = Math.Clamp(delta / maxSteer, -1, 1);
            car.Step(input);

            var yawRate = car.AngularVelocity.Y;
            var lat = car.Velocity.Length * yawRate;
            if (car.Time - levelStart > 1.5)
            {
                levelAccSum += lat;
                levelSamples++;
                levelErrMax = Math.Max(levelErrMax, Math.Abs(err));
            }

            if (Math.Abs(err) > 5)
            {
                stop = Fmt($"left the circle (error {err:F1} m)");
                break;
            }

            if (Math.Abs(car.Velocity.Dot(car.Left)) > 0.5 * Math.Max(2, v))
            {
                stop = "spun (sideslip)";
                break;
            }

            if (car.Time - levelStart >= 3)
            {
                if (levelSamples > 0 && levelErrMax < 1.0)
                {
                    best = Math.Max(best, levelAccSum / levelSamples);
                }

                target += 0.5;
                levelStart = car.Time;
                levelAccSum = 0;
                levelSamples = 0;
                levelErrMax = 0;
            }
        }

        return new ScenarioResult(Fmt($"skidpad R{radius:F0}"), best / spec.Gravity, "g", best > 0, Fmt($"{stop} at target {target:F1} m/s after {car.Time:F0} s"));
    }

    public static IReadOnlyList<ScenarioResult> All(VehicleSpec spec) =>
        [ZeroTo100(spec), TopSpeed(spec), Braking100(spec), Skidpad(spec)];

    private static double Distance(Vehicle car) => Horizontal(car.Position);

    private static double Horizontal(Vec3 v) => Math.Sqrt(v.X * v.X + v.Z * v.Z);

    private static string Fmt(FormattableString s) => FormattableString.Invariant(s);

    /// <summary>Upshifts at 97 % of RPM_limit with the clutch open for the shift time (GUESSED driver).</summary>
    private sealed class AutoShifter(VehicleSpec spec)
    {
        private double _shiftEnd = -1;

        public int Gear { get; private set; } = 1;

        public int Shifts { get; private set; }

        public DriverInput Control(Vehicle car, double throttle)
        {
            var dt = spec.Drivetrain;
            if (_shiftEnd >= 0)
            {
                if (car.Time < _shiftEnd)
                {
                    return new DriverInput { Throttle = 0, Clutch = 0, Gear = Gear };
                }

                _shiftEnd = -1;
            }

            var top = Math.Min(dt.Gears, dt.Ratios.Length - 1);
            if (car.EngineRpm > spec.Engine.RpmLimit * 0.97 && Gear < top && dt.Ratios[Gear + 1] > 0)
            {
                Gear++;
                Shifts++;
                _shiftEnd = car.Time + dt.ShiftTime;
                return new DriverInput { Throttle = 0, Clutch = 0, Gear = Gear };
            }

            // Launch: slip the clutch so the engine stays above 1.5x idle (a driver's foot or a centrifugal clutch).
            var idle = spec.Engine.IdleRpm;
            var clutch = Math.Clamp((car.EngineRpm - 1.5 * idle) / (1.5 * idle), 0, 1);
            if (Gear > 1 || car.ForwardSpeed > 8)
            {
                clutch = 1;
            }

            return new DriverInput { Throttle = throttle, Clutch = clutch, Gear = Gear };
        }
    }
}
