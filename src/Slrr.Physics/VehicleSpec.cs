namespace Slrr.Physics;

/// <summary>Engine data used by the simulation (from the dyno tables and the Chassis contract fields).</summary>
public sealed class EngineSpec
{
    /// <summary>Gross torque table [N m] at rpm = i * <see cref="TableStep"/> (the dyno's table 0, entry 0 = 0 rpm).
    /// MEASURED: the chassis field reader (0x448779..0x44894d) copies the DynoData native's tables 0 and 1 (nitro, or
    /// table 0 again) into the vehicle as (rpm, torque) pairs.</summary>
    public float[] Torque { get; set; } = [];

    /// <summary>Nitro table (table 1) or null.</summary>
    public float[]? NitroTorque { get; set; }

    public int TableStep { get; set; } = 250;

    public double IdleRpm { get; set; } = 800;

    public double RpmLimit { get; set; } = 7000;

    public double MaxRpm { get; set; } = 7000;

    /// <summary>Rotating inertia [kg m^2] actually simulated: <c>engine_inertia * InertiaScale</c> (see SpecBuilder).</summary>
    public double Inertia { get; set; } = 0.2;

    /// <summary>Script <c>engine_inertia</c> as read (units unknown; the exe multiplies it by <c>engine_inertia_factor</c>, 0x4558de).</summary>
    public double ScriptInertia { get; set; }

    public double FrictionFwd { get; set; }

    public double FrictionRev { get; set; }

    /// <summary>Engine wear d (telemetry 0x1E38): torque scaled by <see cref="TyreModel.DamageFactor"/> (MEASURED 0x4486fb).</summary>
    public double Wear { get; set; }

    /// <summary>Table lookup (rpm &lt; 0 → 0, linear between entries, 0 past the end), as DynoData.getTorque.</summary>
    public double TableTorque(double rpm, bool nitro = false)
    {
        var t = nitro && NitroTorque is not null ? NitroTorque : Torque;
        return Dyno.EngineCycleModel.Lookup(t, TableStep, (float)rpm);
    }

    /// <summary>The dyno model's own friction loss, 2e-8 rpm^3 W (MEASURED 0x4b7108), as a torque.</summary>
    public static double DynoFrictionTorque(double rpm) => rpm <= 0 ? 0 : 2e-8 * rpm * rpm * rpm / (rpm * 0.10471975803375244);
}

/// <summary>Gearbox, clutch and final drive (Chassis contract fields).</summary>
public sealed class DrivetrainSpec
{
    /// <summary><c>ratio[0..7]</c>: 0 = neutral, 1..gears forward, 7 = reverse (GUESSED from the Chassis fallback).</summary>
    public double[] Ratios { get; set; } = [0, 3.5, 2.7, 1.9, 1.3, 0, 0, -4];

    public int Gears { get; set; } = 4;

    public double FinalDrive { get; set; } = 3.5;

    /// <summary><c>ClutchF</c>: used as the clutch torque capacity [N m] (GUESSED role).</summary>
    public double ClutchCapacity { get; set; } = 500;

    /// <summary>Fraction of power lost in gearbox/diff (GUESSED; the exe value is unknown).</summary>
    public double Efficiency { get; set; } = 0.9;

    /// <summary>
    /// Differential lock coefficient L (Chassis command <c>difflock f</c>, MEASURED 0x4597ea: f &lt;= 0 stores 0.1; the
    /// car's initial value is 0.1, 0x44717d; stock Chassis.java sends <c>difflock 0.0</c>). The vehicle step (0x4554a0..
    /// 0x455602) pulls every driven wheel's spin toward the drive-weighted mean spin by the fraction
    /// <c>k = min(|delta| * 0.1 * dt * 50 * L, 1)</c> each step: a speed-squared viscous coupling on top of the equal-torque
    /// (open) split.
    /// </summary>
    public double DiffLock { get; set; } = 0.1;

    /// <summary>
    /// True: the driven wheels take equal spin increments from the drivetrain (the exe's form, MEASURED 0x455af2..
    /// 0x455b55: <c>w_i += delta * d_i / c_i</c> after the engine/shaft coupling); false: an open differential (equal
    /// torque split, this area's earlier GUESS).
    /// </summary>
    public bool EqualSpinDrive { get; set; } = true;

    /// <summary>Upshift time with the clutch open (GUESSED; Slrr_GI has AutoShiftDeadTime settings, addresses unknown).</summary>
    public double ShiftTime { get; set; } = 0.25;
}

/// <summary>One wheel corner.</summary>
public sealed class WheelSpec
{
    public string Name { get; set; } = "";

    /// <summary>Wheel centre in the car frame at static ride height (cfg <c>wheel</c> line; x right, y up, z back).</summary>
    public Vec3 Position { get; set; }

    public double Radius { get; set; } = 0.3;

    public double Width { get; set; } = 0.2;

    public double Mass { get; set; } = 10;

    public bool Steers { get; set; }

    /// <summary>Max steer angle [rad] at full lock.</summary>
    public double MaxSteer { get; set; }

    /// <summary>Fraction of drive torque (WheelRef.setDrive).</summary>
    public double Drive { get; set; }

    /// <summary>Service brake torque [N m] at full pedal (WheelRef.setBrake = torque * balance).</summary>
    public double BrakeTorque { get; set; }

    public double HandBrakeTorque { get; set; }

    /// <summary>Spring rate [N/m] (WheelRef.setForce = force * force_mul).</summary>
    public double SpringRate { get; set; } = 30000;

    /// <summary>Zero-force spring length (setRestLen).</summary>
    public double RestLength { get; set; } = 0.3;

    public double MinLength { get; set; } = 0.1;

    public double MaxLength { get; set; } = 0.3;

    /// <summary>Damping [N s/m] compressing / extending. MEASURED: setDamping(d) stores bump = d, rebound = 0.7 d (0x4412e0).</summary>
    public double DampingBump { get; set; } = 2000;

    public double DampingRebound { get; set; } = 1400;

    public TyreParams Tyre { get; set; } = TyreParams.WheelDefaults();

    public double SpinInertia => 0.5 * Mass * Radius * Radius;
}

/// <summary>An anti-roll bar between two wheels (Chassis command <c>add_rollbar w1 w2 force damping</c>).</summary>
public sealed record RollbarSpec(int Wheel1, int Wheel2, double Rate, double Damping);

/// <summary>Everything the simulation needs about one car. Build it with <see cref="SpecBuilder"/> or by hand.</summary>
public sealed class VehicleSpec
{
    public string Name { get; set; } = "car";

    public double Mass { get; set; } = 1200;

    /// <summary>Centre of mass in the car frame.</summary>
    public Vec3 CenterOfMass { get; set; }

    /// <summary>Principal moments of inertia about the CM (car axes). GUESSED from a uniform box of the body bounds.</summary>
    public Vec3 Inertia { get; set; } = new(1500, 1800, 600);

    public List<WheelSpec> Wheels { get; } = [];

    public List<RollbarSpec> Rollbars { get; } = [];

    /// <summary>
    /// Where the tyres' lateral forces act on the body. True (default): at the wheel centre. GUESSED from the exe's
    /// structure (MEASURED): every wheel has its own physics object (wheel +0x30) joined to the chassis by the spring
    /// constraint that setForce/setDamping/setMin/MaxLen configure (0x491f80 calls in 0x441270..0x441540), and the tyre
    /// force is computed on that wheel object (0x456187..0x4563e5), so the chassis receives side forces through the
    /// constraint at the hub rather than at the ground. The inner wheels then unload at t / (2 (h - r)) instead of
    /// t / 2h (DevilSport: 2.3 g instead of 1.09 g). False: at the contact patch (the earlier model). Longitudinal
    /// forces stay at the contact patch either way.
    /// </summary>
    public bool LateralForceAtHub { get; set; } = true;

    /// <summary>Chassis collision boxes in the car frame (<see cref="ChassisShape"/>); empty = callers use their own guess.</summary>
    public List<CollisionBox> Body { get; } = [];

    public EngineSpec Engine { get; set; } = new();

    public DrivetrainSpec Drivetrain { get; set; } = new();

    /// <summary>Chassis <c>C_drag</c> (MEASURED name, read by the exe at 0x448cd6); used as a drag coefficient Cd.</summary>
    public double DragCoefficient { get; set; } = 0.32;

    /// <summary>Frontal area [m^2] (GUESSED: 0.85 * width * height of the body bounds).</summary>
    public double FrontalArea { get; set; } = 2.0;

    public double AirDensity { get; set; } = 1.2928999662399292; // the dyno model's +0x24

    /// <summary>Gravity [m/s^2], positive. Slrr_GI F_Gravity = -9.8067 (MEASURED file value).</summary>
    public double Gravity { get; set; } = 9.8067;

    /// <summary>Displayed numbers for plausibility checks (script maxHP, dyno maxima).</summary>
    public double ScriptMaxHp { get; set; }

    public double DynoMaxTorque { get; set; }

    public double DynoRpmMaxTorque { get; set; }

    public double DynoRpmMaxHp { get; set; }

    /// <summary>Notes on where each number came from (measured / guessed / fallback).</summary>
    public List<string> Notes { get; } = [];

    /// <summary>Static load on each wheel from the CM position (rigid 4-point support, lateral split by x).</summary>
    public double[] StaticWheelLoads()
    {
        var n = Wheels.Count;
        var loads = new double[n];
        if (n == 0)
        {
            return loads;
        }

        var front = Wheels.Where(w => w.Position.Z < CenterOfMass.Z).ToList();
        var rear = Wheels.Where(w => w.Position.Z >= CenterOfMass.Z).ToList();
        if (front.Count == 0 || rear.Count == 0)
        {
            Array.Fill(loads, Mass * Gravity / n);
            return loads;
        }

        var zf = front.Average(w => w.Position.Z);
        var zr = rear.Average(w => w.Position.Z);
        var rearShare = (CenterOfMass.Z - zf) / (zr - zf);
        for (var i = 0; i < n; i++)
        {
            var isRear = Wheels[i].Position.Z >= CenterOfMass.Z;
            loads[i] = Mass * Gravity * (isRear ? rearShare / rear.Count : (1 - rearShare) / front.Count);
        }

        return loads;
    }
}
