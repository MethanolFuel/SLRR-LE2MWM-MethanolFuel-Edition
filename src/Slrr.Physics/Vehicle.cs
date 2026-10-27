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
    public const double MinSlipSpeed = 1.0; // m/s, denominator floor of the slip definitions (GUESSED)
    public const double BumpStopRate = 10.0; // x spring rate past the min length (GUESSED)

    private readonly WheelSim[] _wheels;
    private readonly double[] _staticLoads;

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
        foreach (var w in _wheels)
        {
            w.Spin = 0;
            w.Travel = 0;
            w.TravelVelocity = 0;
        }

        Time = 0;
        Steps = 0;
        SyncContacts();
    }

    /// <summary>Advances one fixed 1/120 s step.</summary>
    public void Step(DriverInput input)
    {
        var h = 1.0 / StepRate / Substeps;
        for (var k = 0; k < Substeps; k++)
        {
            SubStep(input, h);
        }

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
        if (rpm > eng.RpmLimit)
        {
            throttle = 0; // hard rev cut (GUESSED)
        }

        if (rpm < eng.IdleRpm)
        {
            throttle = Math.Max(throttle, Math.Clamp((eng.IdleRpm - rpm) / 300.0, 0, 1)); // idle governor (GUESSED)
        }

        var table = eng.TableTorque(Math.Max(rpm, 0), input.Nitro) * TyreModel.DamageFactor(eng.Wear);
        var pump = 0.1 * eng.TableTorque(eng.RpmLimit * 0.5) * Math.Max(rpm, 0) / eng.RpmLimit;
        var te = throttle * table - EngineSpec.DynoFrictionTorque(rpm) - (1 - throttle) * pump;
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

            var fx0 = TyreForces(i, st.Spin, vx[i], vy[i], fz[i], hits[i], out _, out _, out _);
            const double eps = 0.01;
            var fx1 = TyreForces(i, st.Spin + eps, vx[i], vy[i], fz[i], hits[i], out _, out _, out _);
            var kw = Math.Max(0, (fx1 - fx0) / eps); // dFx/dSpin
            meff[i] = inertia + h * w.Radius * kw;
            var roll = (w.Tyre.RollResistance + hits[i].RollingResistance) * fz[i] * w.Radius * Math.Sign(st.Spin);
            bExt[i] = h * (-w.Radius * fx0 - roll - w.Tyre.Bearing * 1e-3 * st.Spin);
        }

        var ie = Math.Max(1e-3, eng.Inertia);
        double tc = 0;
        var capacity = dt.ClutchCapacity * Math.Clamp(input.Clutch, 0, 1);
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

                sumD2 += d * d / meff[i];
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
            var spin = st.Spin + (h * tc * g * eta * w.Drive + bExt[i]) / meff[i];
            var brake = w.BrakeTorque * Math.Clamp(input.Brake, 0, 1) + w.HandBrakeTorque * Math.Clamp(input.HandBrake, 0, 1);
            var dw = h * brake / meff[i];
            spin = Math.Abs(spin) <= dw ? 0 : spin - Math.Sign(spin) * dw;
            st.Spin = spin;

            if (!grounded[i])
            {
                st.ForceLong = st.ForceLat = st.SlipRatio = st.SlipAngle = 0;
                continue;
            }

            var fx = TyreForces(i, spin, vx[i], vy[i], fz[i], hits[i], out var fy, out var kappa, out var alpha);
            st.ForceLong = fx;
            st.ForceLat = fy;
            st.SlipRatio = kappa;
            st.SlipAngle = alpha;
            var tf = frameF[i] * fx + frameL[i] * fy;
            force += tf;
            torque += (contact[i] - Position).Cross(tf);
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

    /// <summary>Longitudinal tyre force (returned) and lateral force for a spin rate (combined slip, GUESSED).</summary>
    private double TyreForces(int i, double spin, double vx, double vy, double fz, GroundHit hit, out double fy, out double kappa, out double alpha)
    {
        var w = Spec.Wheels[i];
        Span<double> p = stackalloc double[TyreParams.Count];
        w.Tyre.P.CopyTo(p);
        p[2] *= hit.Friction;
        var denom = Math.Max(Math.Abs(vx), MinSlipSpeed);
        kappa = (spin * w.Radius - vx) / denom;
        var tanA = vy / denom;
        alpha = Math.Atan(tanA);
        var load = TyreModel.SmoothedLoad(p, fz);
        p[4] = TyreModel.LoadSensitiveStiffness(p, load);
        var sigma = Math.Sqrt(kappa * kappa + tanA * tanA);
        if (sigma < 1e-12 || load <= 0)
        {
            fy = 0;
            return 0;
        }

        var f = TyreModel.MagicFormula(p, sigma, load);
        fy = -f * tanA / sigma * p[12];
        return f * kappa / sigma;
    }
}
