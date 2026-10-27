using System.Numerics;
using Slrr.Physics;
using PhysGround = Slrr.Physics.IGroundQuery;

namespace Slrr.Game.Physics;

/// <summary>A ground that can be swapped at run time (a map loads after the cars were bound); no target = no ground.</summary>
public sealed class SwitchableGround : PhysGround
{
    public PhysGround? Target { get; set; }

    public bool Raycast(Vec3 origin, Vec3 direction, double maxDistance, out GroundHit hit)
    {
        if (Target is { } t)
        {
            return t.Raycast(origin, direction, maxDistance, out hit);
        }

        hit = default;
        return false;
    }
}

/// <summary>Oriented collision box of a car body in the chassis frame (relative to the chassis origin).</summary>
public readonly record struct BodyBox(Vec3 Center, Vec3 HalfExtents)
{
    public double BoundingRadius => HalfExtents.Length;
}

/// <summary>Script-visible run state of a car (command verbs, all GUESSED from their use in the shipped scripts).</summary>
public enum CarRunState
{
    /// <summary>Pose locked where the scripts put it (after <c>reset</c>/<c>stop</c>/<c>idle</c>, showrooms, pre-race grid).</summary>
    Held,

    /// <summary>Simulated and driven by its input source (after <c>start</c>).</summary>
    Driving,
}

/// <summary>Common state of a simulated car: run state, controls, collision shape, telemetry.</summary>
public abstract class SimCar
{
    private Vec3 _prevVelocity;
    private bool _hasPrev;

    protected SimCar(int id, string name)
    {
        Id = id;
        Name = name;
    }

    /// <summary>Chassis instance id (the id the scripts' <c>car.id()</c> and EVENT_COMMAND target carry).</summary>
    public int Id { get; }

    public string Name { get; }

    public CarRunState State { get; set; } = CarRunState.Held;

    /// <summary><c>suspend</c>/<c>wakeup</c> (GUESSED: freezes the simulation without touching the run state).</summary>
    public bool Suspended { get; set; }

    public bool EngineRunning { get; set; } = true;

    /// <summary><c>brake</c>: throttle 0, full brake until the next <c>start</c>/<c>reset</c> (GUESSED).</summary>
    public bool BrakeOverride { get; set; }

    /// <summary>Last <c>setsteer f</c> value; used while no input source is attached (GUESSED).</summary>
    public double ScriptSteer { get; set; }

    /// <summary>Where controls come from (player virtual axes, a bot, a test script); null = idle controls.</summary>
    public IVehicleInputSource? Input { get; set; }

    public CarControls Controls { get; } = new();

    public bool Horn => Controls.Horn;

    /// <summary>Commands and values stored without a simulated effect (abs, asr, difflock, cruise, filter, ...).</summary>
    public Dictionary<string, string> Settings { get; } = new(StringComparer.Ordinal);

    public abstract double Mass { get; }

    /// <summary>Body-frame principal inertia (kg m²).</summary>
    public abstract Vec3 Inertia { get; }

    /// <summary>Centre of mass in the chassis frame.</summary>
    public abstract Vec3 LocalCenterOfMass { get; }

    public abstract Vec3 Position { get; set; }

    public abstract Quat Orientation { get; set; }

    public abstract Vec3 Velocity { get; set; }

    public abstract Vec3 AngularVelocity { get; set; }

    /// <summary>Bounding box of the whole collision shape (car-vs-car spheres, broad phase).</summary>
    public abstract BodyBox Box { get; }

    /// <summary>The boxes that collide with the map (default: just <see cref="Box"/>).</summary>
    public virtual IReadOnlyList<BodyBox> Boxes => [Box];

    /// <summary>Accumulated damage d (0 intact, 1 wrecked; <see cref="DamageModel"/>).</summary>
    public double Damage { get; protected set; }

    /// <summary><c>damage_multiplier f</c> verb (GUESSED role: scales the damage the model adds).</summary>
    public double DamageMultiplier { get; set; } = 1.0;

    /// <summary>Body up-axis Y below which the car counts as overturned (on its side or roof; GUESSED 0.3 = 72 degrees).</summary>
    public const double OverturnedUpY = 0.3;

    public bool Overturned => Orientation.Rotate(Vec3.UnitY).Y < OverturnedUpY;

    /// <summary>Seconds the car has been overturned and nearly at rest (|v| &lt; 1 m/s); 0 otherwise.</summary>
    public double OverturnedTime { get; internal set; }

    /// <summary>Adds impact damage at a world point (base: the whole car).</summary>
    public virtual void AddDamage(Vec3 point, double amount, double wheelReach) => Damage = Math.Min(1.0, Damage + amount);

    public abstract double Time { get; }

    public double Odometer { get; protected set; }

    public double ImpactTotal { get; internal set; }

    public CarTelemetry? Telemetry { get; protected set; }

    /// <summary>Chassis origin in world (Position is the centre of mass).</summary>
    public Vec3 Origin => Position - Orientation.Rotate(LocalCenterOfMass);

    public bool Simulated => State == CarRunState.Driving && !Suspended;

    /// <summary>Places the chassis origin at (x, z) on the ground under <paramref name="origin"/> (or at its height
    /// when there is no ground), zero velocities. <paramref name="snap"/> false keeps the given height.</summary>
    public abstract void Place(Vec3 origin, Quat orientation, bool snap = true);

    /// <summary>One fixed step of <paramref name="h"/> seconds with the current <see cref="Controls"/>.</summary>
    public abstract void Step(double h);

    /// <summary>Advances the clock and wheel animation of a held car (engine idling, no motion).</summary>
    public abstract void StepHeld(double h);

    /// <summary>World inverse inertia applied to a vector.</summary>
    public Vec3 InverseInertiaWorld(Vec3 v)
    {
        var b = Orientation.InverseRotate(v);
        var i = Inertia;
        return Orientation.Rotate(new Vec3(b.X / i.X, b.Y / i.Y, b.Z / i.Z));
    }

    /// <summary>Applies an impulse at a world point.</summary>
    public void ApplyImpulse(Vec3 point, Vec3 impulse)
    {
        Velocity += impulse / Mass;
        AngularVelocity += InverseInertiaWorld((point - Position).Cross(impulse));
    }

    /// <summary>The 8 world corners of <see cref="Box"/>.</summary>
    public void BoxCorners(Span<Vec3> into) => BoxCorners(Box, into);

    /// <summary>The 8 world corners of a box of this car's chassis frame.</summary>
    public void BoxCorners(BodyBox b, Span<Vec3> into)
    {
        var o = Origin;
        var q = Orientation;
        var k = 0;
        for (var sx = -1; sx <= 1; sx += 2)
        {
            for (var sy = -1; sy <= 1; sy += 2)
            {
                for (var sz = -1; sz <= 1; sz += 2)
                {
                    into[k++] = o + q.Rotate(b.Center + new Vec3(sx * b.HalfExtents.X, sy * b.HalfExtents.Y, sz * b.HalfExtents.Z));
                }
            }
        }
    }

    public Vec3 BoxCenterWorld => Origin + Orientation.Rotate(Box.Center);

    /// <summary>Fills <see cref="Controls"/> from the input source and the script overrides.</summary>
    public void ReadControls()
    {
        if (Input is { } src && State == CarRunState.Driving)
        {
            src.Read(Time, Controls);
        }
        else
        {
            Controls.Clear();
            Controls.Steer = ScriptSteer;
        }

        if (BrakeOverride)
        {
            Controls.Throttle = 0;
            Controls.Brake = 1;
        }

        if (!EngineRunning)
        {
            Controls.Throttle = 0;
        }
    }

    protected void PublishCommon(long step, Func<CarTelemetry, CarTelemetry> fill, double h)
    {
        var v = Velocity;
        var acc = _hasPrev && h > 0 ? (v - _prevVelocity) / h : Vec3.Zero;
        _prevVelocity = v;
        _hasPrev = Simulated;
        const double g = 9.8067;
        var f = Orientation.InverseRotate(acc + new Vec3(0, g, 0)) / g; // body frame: x right, y up, -z forward
        var q = Orientation;
        var t = new CarTelemetry
        {
            Id = Id,
            Name = Name,
            Time = Time,
            Step = step,
            Simulated = Simulated,
            Position = Origin.ToVector3(),
            CenterOfMass = Position.ToVector3(),
            Orientation = q.ToQuaternion(),
            Ypr = Conv.ToYpr(q),
            Velocity = v.ToVector3(),
            AngularVelocity = AngularVelocity.ToVector3(),
            Speed = (float)v.Length,
            ForwardSpeed = (float)v.Dot(q.Rotate(new Vec3(0, 0, -1))),
            Rpm = 0,
            IdleRpm = 0,
            RpmLimit = 0,
            Gear = 0,
            EngineRunning = EngineRunning,
            Throttle = (float)Controls.Throttle,
            Brake = (float)Controls.Brake,
            Clutch = 1,
            Handbrake = (float)Controls.Handbrake,
            Steer = (float)Controls.Steer,
            Nitro = Controls.Nitro,
            Horn = Controls.Horn,
            EngineTorque = 0,
            GLongitudinal = (float)-f.Z,
            GLateral = (float)-f.X,
            GVertical = (float)f.Y,
            Odometer = Odometer,
            ImpactTotal = ImpactTotal,
            Damage = Damage,
            Overturned = Overturned,
            Wheels = [],
        };
        Telemetry = fill(t);
    }

    internal abstract void Publish(long step, double h);

    /// <summary>Speed of the body, m/s.</summary>
    public double Speed => Velocity.Length;

    public override string ToString() => $"{Name} #{Id} {State}{(Suspended ? " suspended" : "")}";
}

/// <summary>The player's (and any fully built) car: a <see cref="Vehicle"/> from <see cref="SpecBuilder"/> data.</summary>
public sealed class FullCar : SimCar
{
    private readonly DriverInput _input = new();
    private readonly double[] _rotation;
    private BodyBox _box;
    private BodyBox[] _boxes;

    public FullCar(int id, string name, VehicleSpec spec, PhysGround ground, int substeps = 8)
        : base(id, name)
    {
        Vehicle = new Vehicle(spec, ground, substeps);
        Drivetrain = new Drivetrain(spec);
        _rotation = new double[spec.Wheels.Count];
        _boxes = spec.Body.Count > 0 ? spec.Body.Select(b => new BodyBox(b.Center, b.HalfExtents)).ToArray() : [DefaultBox(spec)];
        _box = Union(_boxes);
    }

    public override IReadOnlyList<BodyBox> Boxes => _boxes;

    /// <summary>Engine damage follows the car's damage; the corner nearest the impact in the car's x/z plane (within the
    /// reach) takes twice the
    /// amount on its tyre friction and brake (GUESSED split; the scaling itself is MEASURED, see <see cref="DamageModel"/>).</summary>
    public override void AddDamage(Vec3 point, double amount, double wheelReach)
    {
        base.AddDamage(point, amount, wheelReach);
        Vehicle.EngineDamage = Damage;
        var spec = Spec;
        var best = -1;
        var bestD = wheelReach;
        var local = Vehicle.Orientation.InverseRotate(point - Vehicle.Position) + spec.CenterOfMass;
        for (var i = 0; i < spec.Wheels.Count; i++)
        {
            // distance in the car's horizontal plane (x, z): a bumper hit above the wheel line still reaches the corner
            var wp = spec.Wheels[i].Position;
            var d = Math.Sqrt((wp.X - local.X) * (wp.X - local.X) + (wp.Z - local.Z) * (wp.Z - local.Z));
            if (d < bestD)
            {
                bestD = d;
                best = i;
            }
        }

        if (best >= 0)
        {
            var w = Vehicle.Wheels[best];
            w.Damage = Math.Min(1.0, w.Damage + 2 * amount);
        }
    }

    /// <summary>Clears damage (repairs; scripts' repair paths are not wired).</summary>
    public void Repair()
    {
        Damage = 0;
        Vehicle.EngineDamage = 0;
        foreach (var w in Vehicle.Wheels)
        {
            w.Damage = 0;
        }
    }

    private static BodyBox Union(IReadOnlyList<BodyBox> boxes)
    {
        var min = new Vec3(double.MaxValue, double.MaxValue, double.MaxValue);
        var max = -min;
        foreach (var b in boxes)
        {
            var lo = b.Center - b.HalfExtents;
            var hi = b.Center + b.HalfExtents;
            min = new Vec3(Math.Min(min.X, lo.X), Math.Min(min.Y, lo.Y), Math.Min(min.Z, lo.Z));
            max = new Vec3(Math.Max(max.X, hi.X), Math.Max(max.Y, hi.Y), Math.Max(max.Z, hi.Z));
        }

        return new BodyBox((min + max) * 0.5, (max - min) * 0.5);
    }

    public Vehicle Vehicle { get; }

    public Drivetrain Drivetrain { get; }

    public VehicleSpec Spec => Vehicle.Spec;

    public DriverInput LastInput => _input;

    public override double Mass => Spec.Mass;

    public override Vec3 Inertia => Spec.Inertia;

    public override Vec3 LocalCenterOfMass => Spec.CenterOfMass;

    public override Vec3 Position { get => Vehicle.Position; set => Vehicle.Position = value; }

    public override Quat Orientation { get => Vehicle.Orientation; set => Vehicle.Orientation = value; }

    public override Vec3 Velocity { get => Vehicle.Velocity; set => Vehicle.Velocity = value; }

    public override Vec3 AngularVelocity { get => Vehicle.AngularVelocity; set => Vehicle.AngularVelocity = value; }

    public override BodyBox Box => _box;

    public override double Time => Vehicle.Time;

    /// <summary>Replaces the collision shape by one box (tests, callers with their own shape).</summary>
    public void SetBox(BodyBox box)
    {
        _box = box;
        _boxes = [box];
    }

    /// <summary>
    /// GUESSED body box from the wheel layout (the exe's chassis collision comes from the car's mesh/physics bodies,
    /// not decoded): width track + 0.25 m, length 1.65 x wheelbase (the same proportions Slrr.Physics uses for the
    /// inertia), from 5 cm above the axle line to 1.0 m above it.
    /// </summary>
    public static BodyBox DefaultBox(VehicleSpec spec)
    {
        if (spec.Wheels.Count == 0)
        {
            return new BodyBox(spec.CenterOfMass, new Vec3(0.9, 0.5, 2.2));
        }

        double minX = spec.Wheels.Min(w => w.Position.X), maxX = spec.Wheels.Max(w => w.Position.X);
        double minZ = spec.Wheels.Min(w => w.Position.Z), maxZ = spec.Wheels.Max(w => w.Position.Z);
        var axle = spec.Wheels.Average(w => w.Position.Y);
        var half = new Vec3((maxX - minX + 0.25) / 2, 0.475, (maxZ - minZ) * 1.65 / 2);
        return new BodyBox(new Vec3((minX + maxX) / 2, axle + 0.05 + half.Y, (minZ + maxZ) / 2), half);
    }

    public override void Place(Vec3 origin, Quat orientation, bool snap = true)
    {
        var spec = Spec;
        Vehicle.Reset(origin, 0);
        var placed = false;
        if (snap && spec.Wheels.Count > 0)
        {
            // Every wheel on the ground below (origin.Y + 2 m, GUESSED margin: a car placed slightly below a road still
            // lands on it while a bridge far above is ignored), body tilted onto the wheels' ground plane.
            placed = Vehicle.PlaceOnGround(origin, orientation, 2.0, align: true);
        }

        if (!placed)
        {
            var cm = spec.CenterOfMass;
            var pos = origin + orientation.Rotate(cm);
            if (snap && GroundBelow(origin, out var gy))
            {
                var lift = spec.Wheels.Count == 0 ? 0.5 : spec.Wheels.Average(w => cm.Y - w.Position.Y + w.Radius);
                pos = new Vec3(pos.X, gy + lift, pos.Z);
            }

            Vehicle.Orientation = orientation.Normalized();
            Vehicle.Position = pos;
            Vehicle.Velocity = Vec3.Zero;
            Vehicle.AngularVelocity = Vec3.Zero;
            Vehicle.SyncContacts();
        }
        Drivetrain.Reset();
        Array.Clear(_rotation);
    }

    private bool GroundBelow(Vec3 origin, out double y)
    {
        y = 0;
        // From 2 m above the requested point (GUESSED margin) so a car placed slightly below a road still lands on it,
        // while a bridge far above is ignored.
        if (Vehicle.Ground.Raycast(new Vec3(origin.X, origin.Y + 2.0, origin.Z), new Vec3(0, -1, 0), 200, out var hit))
        {
            y = hit.Point.Y;
            return true;
        }

        return false;
    }

    public override void Step(double h)
    {
        Drivetrain.Apply(Controls, Vehicle, h, _input);
        if (!EngineRunning)
        {
            _input.Clutch = 0;
            _input.Throttle = 0;
        }

        var before = Origin;
        Vehicle.Step(_input);
        if (!EngineRunning)
        {
            Vehicle.EngineSpeed = 0;
        }

        Odometer += (Origin - before).Length;
        for (var i = 0; i < _rotation.Length; i++)
        {
            _rotation[i] = Wrap(_rotation[i] + Vehicle.Wheels[i].Spin * h);
        }
    }

    public override void StepHeld(double h)
    {
        // Held: no body motion; the engine idles when running (sound), wheels stop.
        var e = Spec.Engine;
        Vehicle.EngineSpeed = EngineRunning ? e.IdleRpm * 2 * Math.PI / 60 : 0;
        Drivetrain.Steer = Controls.Steer;
    }

    private static double Wrap(double a)
    {
        const double tau = 2 * Math.PI;
        a %= tau;
        return a < 0 ? a + tau : a;
    }

    internal override void Publish(long step, double h)
    {
        var v = Vehicle;
        var spec = Spec;
        var up = v.Up;
        var wheels = new WheelTelemetry[spec.Wheels.Count];
        var steer = State == CarRunState.Driving ? _input.Steer : Drivetrain.Steer;
        for (var i = 0; i < wheels.Length; i++)
        {
            var w = spec.Wheels[i];
            var s = v.Wheels[i];
            var local = w.Position + new Vec3(0, s.Travel, 0);
            var attach = v.Position + v.Orientation.Rotate(w.Position - spec.CenterOfMass);
            var center = attach + up * s.Travel;
            var skid = Math.Sqrt(s.SlipRatio * s.SlipRatio + Math.Tan(s.SlipAngle) * Math.Tan(s.SlipAngle));
            wheels[i] = new WheelTelemetry(center.ToVector3(), local.ToVector3(), (float)w.Radius, (float)s.Spin, (float)_rotation[i],
                (float)(w.Steers ? steer * w.MaxSteer : 0), (float)s.Travel, (float)s.Load, (float)s.SlipRatio, (float)s.SlipAngle,
                s.Grounded ? (float)skid : 0f, s.Grounded, s.Grounded ? 0 : -1);
        }

        PublishCommon(step, t => t with
        {
            Rpm = (float)v.EngineRpm,
            IdleRpm = (float)spec.Engine.IdleRpm,
            RpmLimit = (float)spec.Engine.RpmLimit,
            Gear = _input.Gear < 0 ? -1 : Drivetrain.Gear,
            Clutch = (float)_input.Clutch,
            Throttle = (float)_input.Throttle,
            Brake = (float)_input.Brake,
            Steer = (float)steer,
            EngineTorque = (float)v.EngineTorque,
            Wheels = wheels,
        }, h);
    }
}

/// <summary>
/// Parameters of a <see cref="SimpleCar"/> (traffic, background AI). All GUESSED: the exe's traffic model was not traced.
/// </summary>
public sealed class SimpleCarSpec
{
    public double Mass { get; init; } = 1300;

    public double Wheelbase { get; init; } = 2.6;

    public double Track { get; init; } = 1.5;

    public double MaxSteer { get; init; } = 0.6;

    /// <summary>Full-throttle acceleration [m/s²].</summary>
    public double Acceleration { get; init; } = 3.5;

    /// <summary>Full-brake deceleration [m/s²].</summary>
    public double Deceleration { get; init; } = 8.0;

    public double MaxSpeed { get; init; } = 45;

    /// <summary>Lateral grip limit [m/s²].</summary>
    public double LateralGrip { get; init; } = 8.0;

    /// <summary>Centre of mass height above the ground.</summary>
    public double RideHeight { get; init; } = 0.55;

    public double Length { get; init; } = 4.3;

    public double Width { get; init; } = 1.75;

    public double Height { get; init; } = 1.4;

    public static SimpleCarSpec From(VehicleSpec v)
    {
        if (v.Wheels.Count == 0)
        {
            return new SimpleCarSpec { Mass = v.Mass };
        }

        var wb = v.Wheels.Max(w => w.Position.Z) - v.Wheels.Min(w => w.Position.Z);
        var tr = v.Wheels.Max(w => w.Position.X) - v.Wheels.Min(w => w.Position.X);
        return new SimpleCarSpec { Mass = v.Mass, Wheelbase = wb, Track = tr, Length = wb * 1.65, Width = tr + 0.25 };
    }
}

/// <summary>
/// A cheap kinematic car (bicycle model, ground following through two ground probes, lateral grip limit) for traffic
/// and background AI: ~2 ground queries per step instead of 32 ray casts plus the drivetrain. GUESSED model.
/// The chassis origin is on the ground between the axles; forward is -Z like the full model.
/// </summary>
public sealed class SimpleCar : SimCar
{
    private readonly PhysGround _ground;
    private double _yaw;
    private double _speed;
    private double _time;
    private Vec3 _pos;
    private Quat _ori = Quat.Identity;
    private Vec3 _vel;
    private Vec3 _angVel;

    public SimpleCar(int id, string name, SimpleCarSpec spec, PhysGround ground)
        : base(id, name)
    {
        Spec = spec;
        _ground = ground;
    }

    public SimpleCarSpec Spec { get; }

    public override double Mass => Spec.Mass;

    public override Vec3 Inertia => new(Spec.Mass / 12 * (Spec.Height * Spec.Height + Spec.Length * Spec.Length),
        Spec.Mass / 12 * (Spec.Width * Spec.Width + Spec.Length * Spec.Length), Spec.Mass / 12 * (Spec.Width * Spec.Width + Spec.Height * Spec.Height));

    public override Vec3 LocalCenterOfMass => new(0, Spec.RideHeight, 0);

    public override Vec3 Position { get => _pos; set => _pos = value; }

    public override Quat Orientation { get => _ori; set => _ori = value.Normalized(); }

    public override Vec3 Velocity
    {
        get => _vel;
        set
        {
            _vel = value;
            _speed = value.Dot(_ori.Rotate(new Vec3(0, 0, -1)));
        }
    }

    public override Vec3 AngularVelocity { get => _angVel; set => _angVel = value; }

    public override BodyBox Box => new(new Vec3(0, 0.25 + (Spec.Height - 0.25) / 2, 0), new Vec3(Spec.Width / 2, (Spec.Height - 0.25) / 2, Spec.Length / 2));

    public override double Time => _time;

    public override void Place(Vec3 origin, Quat orientation, bool snap = true)
    {
        _yaw = Conv.ToYpr(orientation).Yaw;
        var y = origin.Y;
        if (snap && _ground.Raycast(new Vec3(origin.X, origin.Y + 2, origin.Z), new Vec3(0, -1, 0), 200, out var hit))
        {
            y = hit.Point.Y;
        }

        _ori = Conv.FromYpr(_yaw, 0, 0);
        _pos = new Vec3(origin.X, y, origin.Z) + _ori.Rotate(LocalCenterOfMass);
        _vel = Vec3.Zero;
        _angVel = Vec3.Zero;
        _speed = 0;
    }

    public override void Step(double h)
    {
        var s = Spec;
        var c = Controls;
        var fwdSpeed = _speed;
        var a = Math.Clamp(c.Throttle, 0, 1) * s.Acceleration * (1 - Math.Abs(fwdSpeed) / s.MaxSpeed)
            - Math.Sign(fwdSpeed) * (Math.Clamp(c.Brake, 0, 1) * s.Deceleration + 4e-4 * fwdSpeed * fwdSpeed);
        var ns = fwdSpeed + a * h;
        if (Math.Sign(ns) != Math.Sign(fwdSpeed) && c.Throttle <= 0)
        {
            ns = 0;
        }

        _speed = ns;
        var steer = Math.Clamp(c.Steer, -1, 1) * s.MaxSteer;
        var yawRate = _speed / s.Wheelbase * Math.Tan(steer);
        var maxRate = s.LateralGrip / Math.Max(Math.Abs(_speed), 1);
        yawRate = Math.Clamp(yawRate, -maxRate, maxRate);
        _yaw += yawRate * h;
        var heading = Conv.FromYpr(_yaw, 0, 0);
        var fwd = heading.Rotate(new Vec3(0, 0, -1));
        var origin = Origin + fwd * (_speed * h);

        // ground following: probes under the axles
        var half = s.Wheelbase / 2;
        var front = origin + fwd * half;
        var rear = origin - fwd * half;
        double fy = origin.Y, ry = origin.Y;
        var gotF = _ground.Raycast(new Vec3(front.X, front.Y + 1.5, front.Z), new Vec3(0, -1, 0), 4, out var hf);
        var gotR = _ground.Raycast(new Vec3(rear.X, rear.Y + 1.5, rear.Z), new Vec3(0, -1, 0), 4, out var hr);
        if (gotF)
        {
            fy = hf.Point.Y;
        }

        if (gotR)
        {
            ry = hr.Point.Y;
        }

        if (!gotF && !gotR)
        {
            fy = ry = origin.Y - 9.8067 * h * h * 10; // nothing below: sink slowly (GUESSED)
        }

        var pitch = Math.Atan2(fy - ry, s.Wheelbase);
        var oy = (fy + ry) / 2;
        var newOri = Conv.FromYpr(_yaw, pitch, 0);
        var newOrigin = new Vec3(origin.X, oy, origin.Z);
        var newPos = newOrigin + newOri.Rotate(LocalCenterOfMass);
        _vel = (newPos - _pos) / h;
        _angVel = new Vec3(0, yawRate, 0);
        Odometer += (newPos - _pos).Length;
        _pos = newPos;
        _ori = newOri;
        _time += h;
    }

    public override void StepHeld(double h) => _time += h;

    internal override void Publish(long step, double h) =>
        PublishCommon(step, t => t with { Gear = _speed < -0.1 ? -1 : 1, Clutch = 1 }, h);
}
