namespace Slrr.Physics;

/// <summary>Driver controls, all in [0, 1] except <see cref="Steer"/> ([-1, 1], +1 = full left) and <see cref="Gear"/>.</summary>
public sealed class DriverInput
{
    public double Throttle { get; set; }

    public double Brake { get; set; }

    public double HandBrake { get; set; }

    /// <summary>1 = clutch engaged (pedal released), 0 = disengaged.</summary>
    public double Clutch { get; set; } = 1;

    public double Steer { get; set; }

    /// <summary>-1 reverse (ratio[7]), 0 neutral, 1..gears.</summary>
    public int Gear { get; set; } = 1;

    public bool Nitro { get; set; }
}

/// <summary>Per-wheel simulation state (read-only for callers).</summary>
public sealed class WheelSim
{
    public double Spin { get; internal set; }

    /// <summary>Damage d of this corner (0 = intact). MEASURED scaling: tyre friction and brake torque times
    /// <see cref="TyreModel.DamageFactor"/> (1 - 0.6 d^2, 0.1 from d = 1; setFriction 0x440fcc, setBrake 0x4415ae).</summary>
    public double Damage { get; set; }

    /// <summary>Slip state carried between steps (the exe's wheel +0x14c): the longitudinal slip fed to the formula.</summary>
    public double SlipState { get; internal set; }

    /// <summary>Slip-angle state carried between steps (the exe's wheel +0x150), radians.</summary>
    public double AngleState { get; internal set; }

    /// <summary>Suspension travel from the static position, + = compressed [m].</summary>
    public double Travel { get; internal set; }

    public double TravelVelocity { get; internal set; }

    public double Load { get; internal set; }

    public double SlipRatio { get; internal set; }

    public double SlipAngle { get; internal set; }

    public double ForceLong { get; internal set; }

    public double ForceLat { get; internal set; }

    public bool Grounded { get; internal set; }

    /// <summary>Spring length at zero travel (chosen so the static corner load sits at the cfg wheel position).</summary>
    public double StaticLength { get; internal set; }

    /// <summary>Spring potential energy relative to zero force, 0.5 K (c + u)^2 for the grounded spring (tests).</summary>
    public double SpringEnergy { get; internal set; }
}

/// <summary>
/// Deterministic fixed-step vehicle dynamics: a 6-DOF rigid body on four ray-cast suspension corners, magic-formula
/// tyres (MEASURED formula 0x43ba80, GUESSED slip definitions), wheel spin, an open differential, gearbox, friction
/// clutch and an engine driven by the dyno torque table. <see cref="Step"/> advances 1/120 s in
/// <see cref="Substeps"/> equal sub-steps of the whole system (body and wheels), plain double arithmetic in a fixed
/// order: the same inputs give bit-identical states on the same runtime. Everything not labelled MEASURED in the
/// docs (docs/phase4/physics.md) is GUESSED; parity with the original game is unverified.
/// </summary>
public sealed class Vehicle
{
    public const double StepRate = 120.0;
    public const double BumpStopRate = 10.0; // x spring rate past the min length (GUESSED)

    /// <summary>
    /// MEASURED (0x456492..0x456498): the wheel step multiplies the smoothed load by Pacejka[13] (surface rollres + tyre
    /// rollres, 0x45629b) and 0.01. Applied here as a rolling-resistance torque against the spin (application GUESSED).
    /// </summary>
    public const double RollingResistanceScale = 0.01;

    private readonly WheelSim[] _wheels;
    private readonly double[] _staticLoads;
    private double _prevEngineRpm = -1;

    public Vehicle(VehicleSpec spec, IGroundQuery ground, int substeps = 8)
    {
        Spec = spec;
        Ground = ground;
        Substeps = Math.Max(1, substeps);
        _staticLoads = spec.StaticWheelLoads();
        _wheels = spec.Wheels.Select(_ => new WheelSim()).ToArray();
        for (var i = 0; i < _wheels.Length; i++)
        {
            var w = spec.Wheels[i];
            var l0 = w.RestLength - _staticLoads[i] / Math.Max(1, w.SpringRate);
            _wheels[i].StaticLength = Math.Clamp(l0, w.MinLength + 0.005, Math.Max(w.MaxLength, w.MinLength + 0.01));
        }

        Reset(Vec3.Zero, 0);
    }

    public VehicleSpec Spec { get; }

    public IGroundQuery Ground { get; }

    public int Substeps { get; }

    public IReadOnlyList<WheelSim> Wheels => _wheels;

    /// <summary>Centre of mass, world.</summary>
    public Vec3 Position { get; set; }

    public Quat Orientation { get; set; } = Quat.Identity;

    public Vec3 Velocity { get; set; }

    /// <summary>Angular velocity, world frame.</summary>
    public Vec3 AngularVelocity { get; set; }

    /// <summary>Engine damage d (starts at the spec's wear); torque times <see cref="TyreModel.DamageFactor"/> (MEASURED 0x4486fb).</summary>
    public double EngineDamage { get; set; }

    /// <summary>Engine speed [rad/s].</summary>
    public double EngineSpeed { get; set; }

    public double EngineRpm => EngineSpeed * 60.0 / (2 * Math.PI);

    public double EngineTorque { get; private set; }

    public double ClutchTorque { get; private set; }

    public double Time { get; private set; }

    public long Steps { get; private set; }

    /// <summary>Forward speed [m/s] (car forward is -Z).</summary>
    public double ForwardSpeed => Velocity.Dot(Forward);

    public Vec3 Forward => Orientation.Rotate(new Vec3(0, 0, -1));

    public Vec3 Up => Orientation.Rotate(Vec3.UnitY);

    public Vec3 Left => Orientation.Rotate(new Vec3(-1, 0, 0));

    /// <summary>When false, no aerodynamic drag (tests).</summary>
    public bool Aero { get; set; } = true;

    /// <summary>Places the car at rest on the ground at <paramref name="origin"/> (x, z; y from the static wheel stance).</summary>
    public void Reset(Vec3 origin, double heading)
    {
        Orientation = Quat.FromAxisAngle(Vec3.UnitY, heading);
        Velocity = Vec3.Zero;
        AngularVelocity = Vec3.Zero;
        var cm = Spec.CenterOfMass;
        var lift = Spec.Wheels.Count == 0 ? 0.5 : Spec.Wheels.Average(w => cm.Y - w.Position.Y + w.Radius);
        var groundY = Ground.Raycast(new Vec3(origin.X, 1000, origin.Z), new Vec3(0, -1, 0), 5000, out var hit) ? hit.Point.Y : 0;
        Position = new Vec3(origin.X, groundY + lift, origin.Z);
        EngineSpeed = Spec.Engine.IdleRpm * 2 * Math.PI / 60;
        _prevEngineRpm = -1;
        foreach (var w in _wheels)
        {
            w.Spin = 0;
            w.Travel = 0;
            w.TravelVelocity = 0;
            w.SlipState = 0;
            w.AngleState = 0;
        }

        Time = 0;
        Steps = 0;
        SyncContacts();
    }

    /// <summary>
    /// Places the car at rest with its chassis origin at (x, z) of <paramref name="origin"/>, on the ground found below
    /// <paramref name="origin"/>.Y + <paramref name="probeAbove"/> under each wheel (GUESSED placement, not the exe's):
    /// with <paramref name="align"/> the body is tilted (yaw kept) onto the plane fitted through the wheels' ground
    /// points, then raised so that no wheel sits below its static stance. Without this a car dropped onto a 11 degree
    /// slope by the old origin-only snap started with ~53 kN in the bump stops of the uphill wheels and flipped over
    /// (MEASURED with this model). Returns false (pose kept at <paramref name="origin"/>) when no wheel finds ground.
    /// </summary>
    public bool PlaceOnGround(Vec3 origin, Quat orientation, double probeAbove = 2.0, bool align = true)
    {
        var q = orientation.Normalized();
        var cm = Spec.CenterOfMass;
        var n = Spec.Wheels.Count;
        Velocity = Vec3.Zero;
        AngularVelocity = Vec3.Zero;
        Orientation = q;
        Position = origin + q.Rotate(cm);
        if (n == 0)
        {
            SyncContacts();
            return false;
        }

        var ground = new Vec3?[n];
        for (var pass = 0; pass < (align ? 2 : 1); pass++)
        {
            var hits = 0;
            for (var i = 0; i < n; i++)
            {
                var c = origin + q.Rotate(Spec.Wheels[i].Position);
                ground[i] = Ground.Raycast(new Vec3(c.X, origin.Y + probeAbove, c.Z), new Vec3(0, -1, 0), 200 + probeAbove, out var hit)
                    ? hit.Point : null;
                hits += ground[i] is null ? 0 : 1;
            }

            if (hits == 0)
            {
                SyncContacts();
                return false;
            }

            if (pass == 0 && align && hits >= 3 && FitPlaneNormal(ground) is { } normal && normal.Y > 0.7)
            {
                q = AlignToNormal(q, normal);
            }
        }

        // Raise/lower along world Y so the highest-demanding wheel rests at its static stance (centre = cfg position).
        var lift = double.NegativeInfinity;
        var upAxis = q.Rotate(Vec3.UnitY);
        for (var i = 0; i < n; i++)
        {
            if (ground[i] is { } g)
            {
                var w = Spec.Wheels[i];
                var bottom = origin + q.Rotate(w.Position) - upAxis * w.Radius;
                lift = Math.Max(lift, g.Y - bottom.Y);
            }
        }

        Orientation = q;
        Position = origin + new Vec3(0, lift, 0) + q.Rotate(cm);
        SyncContacts();
        return true;
    }

    /// <summary>
    /// The orientation with the same heading as <paramref name="q"/> whose up axis is <paramref name="normal"/>:
    /// yaw about +Y (kept), then pitch about X and roll about Z (forward is -Z), q = Ry * Rx * Rz.
    /// </summary>
    public static Quat AlignToNormal(Quat q, Vec3 normal)
    {
        var f = q.Rotate(new Vec3(0, 0, -1));
        var yaw = Math.Atan2(-f.X, -f.Z);
        var qy = Quat.FromAxisAngle(Vec3.UnitY, yaw);
        var local = qy.InverseRotate(normal.Normalized());
        var roll = Math.Asin(Math.Clamp(-local.X, -1, 1));
        var pitch = Math.Atan2(local.Z, local.Y);
        return (qy * Quat.FromAxisAngle(Vec3.UnitX, pitch) * Quat.FromAxisAngle(Vec3.UnitZ, roll)).Normalized();
    }

    /// <summary>Unit normal (Y up) of the least-squares plane y = a x + b z + c through the given points (null if degenerate).</summary>
    private static Vec3? FitPlaneNormal(Vec3?[] points)
    {
        double sx = 0, sz = 0, sy = 0, sxx = 0, szz = 0, sxz = 0, sxy = 0, szy = 0, k = 0;
        foreach (var p in points)
        {
            if (p is not { } v)
            {
                continue;
            }

            k++;
            sx += v.X;
            sz += v.Z;
            sy += v.Y;
        }

        if (k < 3)
        {
            return null;
        }

        double mx = sx / k, mz = sz / k, my = sy / k;
        foreach (var p in points)
        {
            if (p is not { } v)
            {
                continue;
            }

            double dx = v.X - mx, dz = v.Z - mz, dy = v.Y - my;
            sxx += dx * dx;
            szz += dz * dz;
            sxz += dx * dz;
            sxy += dx * dy;
            szy += dz * dy;
        }

        var det = sxx * szz - sxz * sxz;
        if (Math.Abs(det) < 1e-9)
        {
            return null;
        }

        var a = (sxy * szz - szy * sxz) / det;
        var b = (szy * sxx - sxy * sxz) / det;
        return new Vec3(-a, 1, -b).Normalized();
    }

    /// <summary>Advances one fixed 1/120 s step.</summary>
    public void Step(DriverInput input)
    {
        var h = 1.0 / StepRate / Substeps;
        for (var k = 0; k < Substeps; k++)
        {
            SubStep(input, h);
        }

        // Differential coupling (MEASURED 0x4554a0..0x455602, see DrivetrainSpec.DiffLock): each driven wheel's spin is
        // pulled toward the drive-weighted mean spin, once per vehicle step with the step's dt as the exe does (an
        // explicit fraction, so its strength depends on the step: 1/120 s here, the exe's rate is not known).
        CoupleDrivenWheels(1.0 / StepRate);

        Steps++;
        Time = Steps / StepRate;
    }

    /// <summary>Total mechanical energy (tests): body KE + rotational KE + gravity PE + grounded spring PE + wheel spin KE.</summary>
    public double MechanicalEnergy()
    {
        var m = Spec.Mass;
        var wb = Orientation.InverseRotate(AngularVelocity);
        var i = Spec.Inertia;
        var e = 0.5 * m * Velocity.LengthSquared + 0.5 * (i.X * wb.X * wb.X + i.Y * wb.Y * wb.Y + i.Z * wb.Z * wb.Z)
            + m * Spec.Gravity * Position.Y;
        for (var n = 0; n < _wheels.Length; n++)
        {
            e += _wheels[n].SpringEnergy + 0.5 * Spec.Wheels[n].SpinInertia * _wheels[n].Spin * _wheels[n].Spin;
        }

        return e;
    }

    /// <summary>Recomputes every wheel's travel, contact and spring energy from the current pose (travel velocity 0);
    /// call after moving the body by hand.</summary>
    public void SyncContacts()
    {
        var up = Up;
        for (var i = 0; i < _wheels.Length; i++)
        {
            var u = Probe(i, up, out _, out var grounded, out _, out _);
            var w = Spec.Wheels[i];
            var c = w.RestLength - _wheels[i].StaticLength;
            _wheels[i].Travel = u;
            _wheels[i].TravelVelocity = 0;
            _wheels[i].Grounded = grounded;
            _wheels[i].SpringEnergy = grounded ? 0.5 * w.SpringRate * (c + u) * (c + u) : 0;
        }
    }

    /// <summary>Ray cast of one corner along the body down axis: travel (+ = compressed), contact, bump-stop force.</summary>
    private double Probe(int i, Vec3 up, out GroundHit hit, out bool grounded, out double bump, out Vec3 center)
    {
        var w = Spec.Wheels[i];
        var st = _wheels[i];
        var uMin = st.StaticLength - w.MaxLength;
        var uMax = st.StaticLength - w.MinLength;
        var attach = Position + Orientation.Rotate(w.Position - Spec.CenterOfMass);
        var origin = attach + up * uMax;
        var u = uMin;
        grounded = false;
        bump = 0;
        if (Ground.Raycast(origin, -up, uMax - uMin + w.Radius, out hit))
        {
            var uContact = uMax - (hit.Distance - w.Radius);
            if (uContact >= uMin)
            {
                grounded = true;
                u = Math.Min(uContact, uMax);
                bump = uContact > uMax ? (uContact - uMax) * w.SpringRate * BumpStopRate : 0;
            }
        }

        center = attach + up * u;
        return u;
    }

    private void SubStep(DriverInput input, double h)
    {
        var spec = Spec;
        var n = _wheels.Length;
        var up = Up;
        var force = new Vec3(0, -spec.Mass * spec.Gravity, 0);
        var torque = Vec3.Zero;

        // ---- suspension: travel from ray casts, forces along the body up axis
        var centers = new Vec3[n];
        var hits = new GroundHit[n];
        var grounded = new bool[n];
        var newTravel = new double[n];
        var travelVel = new double[n];
        var bump = new double[n];
        for (var i = 0; i < n; i++)
        {
            var u = Probe(i, up, out hits[i], out grounded[i], out bump[i], out centers[i]);
            newTravel[i] = u;
            travelVel[i] = (u - _wheels[i].Travel) / h;
        }

        var suspension = new double[n];
        for (var i = 0; i < n; i++)
        {
            var w = spec.Wheels[i];
            var c = w.RestLength - _wheels[i].StaticLength;
            var spring = Math.Max(0, w.SpringRate * (c + newTravel[i]));
            var damp = (travelVel[i] > 0 ? w.DampingBump : w.DampingRebound) * travelVel[i];
            suspension[i] = spring + damp + bump[i];
            _wheels[i].SpringEnergy = grounded[i] ? 0.5 * w.SpringRate * (c + newTravel[i]) * (c + newTravel[i]) : 0;
        }

        foreach (var bar in spec.Rollbars)
        {
            if (bar.Wheel1 < n && bar.Wheel2 < n)
            {
                var f = bar.Rate * (newTravel[bar.Wheel1] - newTravel[bar.Wheel2]) + bar.Damping * (travelVel[bar.Wheel1] - travelVel[bar.Wheel2]);
                suspension[bar.Wheel1] += f;
                suspension[bar.Wheel2] -= f;
            }
        }

        // ---- tyre frames and loads
        var fwd = Forward;
        var fz = new double[n];
        var frameF = new Vec3[n];
        var frameL = new Vec3[n];
        var contact = new Vec3[n];
        var vx = new double[n];
        var vy = new double[n];
        for (var i = 0; i < n; i++)
        {
            var st = _wheels[i];
            st.Travel = newTravel[i];
            st.TravelVelocity = travelVel[i];
            st.Grounded = grounded[i];
            if (!grounded[i])
            {
                st.Load = 0;
                continue;
            }

            var load = Math.Max(0, suspension[i]);
            fz[i] = load;
            st.Load = load;
            var w = spec.Wheels[i];
            var normal = hits[i].Normal;
            var delta = w.Steers ? input.Steer * w.MaxSteer : 0;
            var f = Quat.FromAxisAngle(up, delta).Rotate(fwd);
            f = (f - normal * f.Dot(normal)).Normalized();
            frameF[i] = f;
            frameL[i] = normal.Cross(f);
            contact[i] = centers[i] - normal * w.Radius;
            var r = contact[i] - Position;
            var vc = Velocity + AngularVelocity.Cross(r);
            vx[i] = vc.Dot(f);
            vy[i] = vc.Dot(frameL[i]);

            // The ground pushes along its normal at the contact point with the suspension load (a compressive contact:
            // never pulls). Applying it along the body axis instead would leave its horizontal part unreacted on a
            // pitched body and push the car along (GUESSED model; the exe's suspension geometry was not traced).
            var fn = normal * load;
            force += fn;
            torque += r.Cross(fn);
        }

        // ---- engine
        var eng = spec.Engine;
        var rpm = EngineRpm;
        var throttle = Math.Clamp(input.Throttle, 0, 1);
        var table = eng.TableTorque(Math.Max(rpm, 0), input.Nitro) * TyreModel.DamageFactor(Math.Max(eng.Wear, EngineDamage));
        var friction = EngineSpec.DynoFrictionTorque(rpm);
        var pump = 0.1 * eng.TableTorque(eng.RpmLimit * 0.5) * Math.Max(rpm, 0) / eng.RpmLimit;
        if (rpm < eng.IdleRpm && table > 0.001)
        {
            // Idle governor, MEASURED form (vehicle step 0x454813..0x454864): below the idle speed the throttle is raised
            // by 1.1 x |engine drag| / table torque, capped at 1. The exe's drag is its own friction term (linear in the
            // speed, engine_friction_fwd); here the drag is this model's friction + closed-throttle pumping (GUESSED).
            throttle = Math.Min(1.0, throttle + Math.Abs((friction + pump) * 1.1) / table);
        }
        else if (rpm < eng.IdleRpm)
        {
            throttle = Math.Max(throttle, Math.Clamp((eng.IdleRpm - rpm) / 300.0, 0, 1)); // no table torque here: crank (GUESSED)
        }

        // Rev limiter, MEASURED form (0x454970..0x4549d5): the drive torque is scaled by the fraction that lets the
        // linearly extrapolated engine speed (w + (w - w_prev)) just reach RPM_limit: 1 below, 0 when already above and
        // still rising, (limit - w) / (w_pred - w) when the next step would cross it, (limit - w_pred) / (w - w_pred)
        // when above the limit but falling. Replaces the hard cut, which made the drive bang-bang at the limiter.
        var limiter = RevLimiterFactor(rpm, _prevEngineRpm < 0 ? rpm : _prevEngineRpm, eng.RpmLimit);
        _prevEngineRpm = rpm;
        var te = throttle * table * limiter - friction - (1 - throttle) * pump;
        if (!double.IsFinite(te))
        {
            te = 0;
        }

        EngineTorque = te;

        // ---- wheel spin with implicit tyre slope, clutch coupling, brakes
        var dt = spec.Drivetrain;
        var gearIndex = input.Gear < 0 ? 7 : input.Gear;
        var ratio = gearIndex >= 0 && gearIndex < dt.Ratios.Length && (input.Gear <= dt.Gears) ? dt.Ratios[gearIndex] : 0;
        var g = ratio * dt.FinalDrive;
        var eta = dt.Efficiency;
        var meff = new double[n];
        var bExt = new double[n];
        for (var i = 0; i < n; i++)
        {
            var w = spec.Wheels[i];
            var st = _wheels[i];
            var inertia = w.SpinInertia;
            if (!grounded[i])
            {
                meff[i] = inertia;
                bExt[i] = -h * w.Tyre.Bearing * 1e-3 * st.Spin;
                continue;
            }

            var fx0 = TyreForces(i, st.Spin, vx[i], vy[i], fz[i], hits[i], h, false, out _, out _, out _);
            const double eps = 0.01;
            var fx1 = TyreForces(i, st.Spin + eps, vx[i], vy[i], fz[i], hits[i], h, false, out _, out _, out _);
            var kw = Math.Max(0, (fx1 - fx0) / eps); // dFx/dSpin
            meff[i] = inertia + h * w.Radius * kw;
            var roll = RollingResistanceScale * (w.Tyre.RollResistance + hits[i].RollingResistance) * TyreModel.SmoothedLoad(w.Tyre.P, fz[i]) * w.Radius * Math.Sign(st.Spin);
            bExt[i] = h * (-w.Radius * fx0 - roll - w.Tyre.Bearing * 1e-3 * st.Spin);
        }

        var ie = Math.Max(1e-3, eng.Inertia);
        double tc = 0;
        var capacity = dt.ClutchCapacity * Math.Clamp(input.Clutch, 0, 1);
        // Drive split. Open (equal torque, d_i / meff_i per wheel) or, with DrivetrainSpec.EqualSpinDrive, the exe's form
        // (MEASURED 0x455af2..0x455b55): the drivetrain's velocity change is added to every driven wheel in proportion to
        // its drive fraction, i.e. the driven wheels take equal spin increments (a spool for the drive impulse) and only
        // their tyre forces and the DiffLock coupling make them differ.
        var maxDrive = 0.0;
        for (var i = 0; i < n; i++)
        {
            maxDrive = Math.Max(maxDrive, spec.Wheels[i].Drive);
        }

        var equalSpin = dt.EqualSpinDrive && maxDrive > 0;
        double spoolMass = 0;
        if (equalSpin)
        {
            for (var i = 0; i < n; i++)
            {
                var dh = spec.Wheels[i].Drive / maxDrive;
                spoolMass += meff[i] * dh * dh;
            }
        }

        if (g != 0 && capacity > 0)
        {
            double sumD2 = 0, prop = 0;
            for (var i = 0; i < n; i++)
            {
                var d = spec.Wheels[i].Drive;
                if (d == 0)
                {
                    continue;
                }

                sumD2 += equalSpin ? d * (d / maxDrive) / spoolMass : d * d / meff[i];
                prop += d * (_wheels[i].Spin + bExt[i] / meff[i]);
            }

            var num = EngineSpeed + h * te / ie - g * prop;
            var den = h / ie + g * g * eta * h * sumD2;
            tc = Math.Clamp(num / den, -capacity, capacity);
        }

        ClutchTorque = tc;
        EngineSpeed = Math.Max(0, EngineSpeed + h * (te - tc) / ie);
        for (var i = 0; i < n; i++)
        {
            var w = spec.Wheels[i];
            var st = _wheels[i];
            var drive = equalSpin ? h * tc * g * eta * (w.Drive / maxDrive) / spoolMass : h * tc * g * eta * w.Drive / meff[i];
            var spin = st.Spin + bExt[i] / meff[i] + drive;
            var brake = (w.BrakeTorque * Math.Clamp(input.Brake, 0, 1) + w.HandBrakeTorque * Math.Clamp(input.HandBrake, 0, 1))
                * TyreModel.DamageFactor(st.Damage);
            var dw = h * brake / meff[i];
            spin = Math.Abs(spin) <= dw ? 0 : spin - Math.Sign(spin) * dw;
            st.Spin = spin;

            if (!grounded[i])
            {
                st.ForceLong = st.ForceLat = st.SlipRatio = st.SlipAngle = 0;
                continue;
            }

            var fx = TyreForces(i, spin, vx[i], vy[i], fz[i], hits[i], h, true, out var fy, out var kappa, out var alpha);
            st.ForceLong = fx;
            st.ForceLat = fy;
            st.SlipRatio = kappa;
            st.SlipAngle = alpha;
            var tf = frameF[i] * fx + frameL[i] * fy;
            force += tf;
            if (spec.LateralForceAtHub)
            {
                // Longitudinal force at the contact patch (full pitch transfer), lateral force at the wheel centre.
                var lat = frameL[i] * fy;
                torque += (contact[i] - Position).Cross(tf - lat) + (centers[i] - Position).Cross(lat);
            }
            else
            {
                torque += (contact[i] - Position).Cross(tf);
            }
        }

        // ---- aerodynamic drag (C_drag as Cd; GUESSED formula)
        if (Aero)
        {
            var v = Velocity.Length;
            force -= Velocity * (0.5 * spec.AirDensity * spec.DragCoefficient * spec.FrontalArea * v);
        }

        // ---- rigid body, semi-implicit Euler
        Velocity += force * (h / spec.Mass);
        var wb = Orientation.InverseRotate(AngularVelocity);
        var tb = Orientation.InverseRotate(torque);
        var iv = spec.Inertia;
        var lw = new Vec3(iv.X * wb.X, iv.Y * wb.Y, iv.Z * wb.Z);
        var gyro = wb.Cross(lw);
        wb = new Vec3(wb.X + h * (tb.X - gyro.X) / iv.X, wb.Y + h * (tb.Y - gyro.Y) / iv.Y, wb.Z + h * (tb.Z - gyro.Z) / iv.Z);
        AngularVelocity = Orientation.Rotate(wb);
        Position += Velocity * h;
        Orientation = Orientation.Integrate(AngularVelocity, h);
    }

    /// <summary>The exe's predictive rev-limiter factor (MEASURED structure, 0x454970..0x4549d5), speeds in rpm.</summary>
    public static double RevLimiterFactor(double rpm, double previousRpm, double limit)
    {
        var predicted = rpm + (rpm - previousRpm);
        if (predicted <= limit)
        {
            return rpm <= limit ? 1.0 : (limit - predicted) / (rpm - predicted);
        }

        return rpm > limit ? 0.0 : (limit - rpm) / (predicted - rpm);
    }

    private void CoupleDrivenWheels(double h)
    {
        var lockK = Spec.Drivetrain.DiffLock;
        if (!(lockK > 0))
        {
            return;
        }

        double sum = 0, weight = 0;
        for (var i = 0; i < _wheels.Length; i++)
        {
            var d = Spec.Wheels[i].Drive;
            if (d > 0.001)
            {
                sum += d * _wheels[i].Spin;
                weight += d;
            }
        }

        if (weight <= 0)
        {
            return;
        }

        var mean = sum / weight;
        for (var i = 0; i < _wheels.Length; i++)
        {
            if (Spec.Wheels[i].Drive > 0.001)
            {
                var delta = mean - _wheels[i].Spin;
                var k = Math.Min(Math.Abs(delta) * 0.1 * h * 50.0 * lockK, 1.0);
                _wheels[i].Spin += delta * k;
            }
        }
    }

    /// <summary>
    /// Longitudinal tyre force (returned) and lateral force. MEASURED slip definitions (0x43bbd0, called from the wheel
    /// step at 0x456187 with the wheel surface speed, the contact velocity components, dt and the two stored states):
    /// <code>
    /// steady (|vx| &gt; 0.1):  k = (R w - vx) / |vx|,  a = atan(vy / |vx|)            (0 below 0.1 m/s)
    /// blend b = clamp((|vx| - 0.1) * 0.1, 0, 1)                                       (full steady slip from 10.1 m/s)
    /// low speed, implicit first-order states:  m' = (dv*dt + 0.3 m) / (0.3 (1 + 5 dt)),  t' = (0.5 t + vy*dt) / (0.5 (1 + 10 dt)),
    ///   each clamped to [-1, 1], dv = R w - vx; i.e. dm/dt = (dv - 1.5 m) / 0.3, dt/dt = (vy - 5 t) / 0.5
    /// m = (1 - b) m' + b k,  t = (1 - b) t' + b a       (stored for the next step)
    /// formula input x = sqrt(sin^2 t + (cos t * m * p12)^2);  F = MF(x, smoothed load)
    /// Fx = F cos t m p12 / x,  Fy = -F sin t / x
    /// </code>
    /// p12 (<c>setFrictn_x</c>) scales the LONGITUDINAL slip (0x45619c, 0x4563a4), not the lateral one. Signs mapped to this
    /// model (the exe passes -R w). The sign of the lateral force (against vy) is this model's convention.
    /// </summary>
    private double TyreForces(int i, double spin, double vx, double vy, double fz, GroundHit hit, double h, bool commit,
        out double fy, out double kappa, out double alpha)
    {
        var w = Spec.Wheels[i];
        var st = _wheels[i];
        Span<double> p = stackalloc double[TyreParams.Count];
        w.Tyre.P.CopyTo(p);
        p[2] *= hit.Friction * TyreModel.DamageFactor(st.Damage);
        var speed = Math.Abs(vx);
        var dv = spin * w.Radius - vx;
        double kss = 0, ass = 0;
        if (speed > 0.1)
        {
            kss = dv / speed;
            ass = Math.Atan(vy / speed);
        }

        var b = Math.Clamp((speed - 0.1) * 0.1, 0, 1);
        double m = kss, t = ass;
        if (b < 1)
        {
            var mt = Math.Clamp((dv * h + 0.3 * st.SlipState) / (0.3 * (1 + 5 * h)), -1, 1);
            var tt = Math.Clamp((0.5 * st.AngleState + vy * h) / (0.5 * (1 + 10 * h)), -1, 1);
            m = (1 - b) * mt + b * kss;
            t = (1 - b) * tt + b * ass;
        }

        if (commit)
        {
            st.SlipState = m;
            st.AngleState = t;
        }

        kappa = m;
        alpha = t;
        var load = TyreModel.SmoothedLoad(p, fz);
        p[4] = TyreModel.LoadSensitiveStiffness(p, load);
        var sin = Math.Sin(t);
        var lon = Math.Cos(t) * m * p[12];
        var x = Math.Sqrt(sin * sin + lon * lon);
        if (x < 1e-12 || load <= 0)
        {
            fy = 0;
            return 0;
        }

        var f = TyreModel.MagicFormula(p, x, load);
        fy = -f * sin / x;
        return f * lon / x;
    }
}
