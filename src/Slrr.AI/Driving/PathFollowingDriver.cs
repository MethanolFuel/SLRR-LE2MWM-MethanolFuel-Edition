using System.Numerics;
using Slrr.AI.Roads;
using Slrr.Game.Physics;
using Slrr.Physics;

namespace Slrr.AI.Driving;

/// <summary>What a <see cref="PathFollowingDriver"/> does with its car.</summary>
public enum DriverMode
{
    /// <summary>No input (the brain is suspended: <c>AI_suspend</c>, MEASURED mode 1).</summary>
    Off,

    /// <summary>Full brake, no throttle (<c>AI_stop</c>: MEASURED mode 0 and the brake input set to 127 = full).</summary>
    Hold,

    /// <summary>Brake held and the engine revved at <see cref="PathFollowingDriver.LaunchThrottle"/> (<c>AI_BeginRace f</c>:
    /// MEASURED mode 8 and f clamped to [0, 1]; the scripts send 0.5 on the grid and 1.0 at "1": the revving reading is GUESSED).</summary>
    Launch,

    /// <summary>Follow <see cref="PathFollowingDriver.Path"/>.</summary>
    Drive,
}

/// <summary>What happens at the end of an open path.</summary>
public enum PathEnd
{
    /// <summary>Drive through and keep going along the end tangent (<c>AI_race</c>: the scripts brake the bot themselves
    /// after the finish trigger, City/ROCTrack call <c>Bot.stop()</c>).</summary>
    Continue,

    /// <summary>Plan a stop at the end (<c>AI_GoToTarget</c>, traffic dead ends).</summary>
    Stop,
}

/// <summary>Tunables of <see cref="PathFollowingDriver"/>; all GUESSED (chosen on the synthetic test roads).</summary>
public sealed record DriverSettings
{
    /// <summary>Look-ahead = <see cref="LookBase"/> + <see cref="LookGain"/>·v, clamped to [<see cref="LookMin"/>, <see cref="LookMax"/>] (m, s).</summary>
    public double LookBase { get; init; } = 4.0;

    public double LookGain { get; init; } = 0.42;

    public double LookMin { get; init; } = 6.0;

    public double LookMax { get; init; } = 40.0;

    /// <summary>The speed profile is read this many seconds ahead (actuator lag).</summary>
    public double SpeedPreview { get; init; } = 0.5;

    /// <summary>Stored AI level (<see cref="AiLevel.Stored"/>, 0.25..0.75); scales grip and braking use.</summary>
    public double Level { get; init; } = AiLevel.Default;

    /// <summary>Cars are not avoided (traffic/racers) when false.</summary>
    public bool Avoid { get; init; } = true;

    /// <summary>Pass slower cars on the free side (racers); false = only keep the gap (traffic).</summary>
    public bool Overtake { get; init; } = true;

    /// <summary>Upright check: |up.y| below this for <see cref="RolledTime"/> seconds = rolled (respawn).</summary>
    public double RolledUp { get; init; } = 0.35;

    public double RolledTime { get; init; } = 2.0;

    /// <summary>Stuck: wanting to move but slower than 1 m/s for this long → reverse out (then respawn).</summary>
    public double StuckTime { get; init; } = 2.5;

    /// <summary>Off the road (lateral error beyond half width + this) for <see cref="OffRoadTime"/> s → respawn.</summary>
    public double OffRoadMargin { get; init; } = 20.0;

    public double OffRoadTime { get; init; } = 5.0;

    /// <summary>Put the car back on the path when it rolled/stuck/left the road (GUESSED; the exe posts <c>ai_info_rolled</c>,
    /// MEASURED string at 0x60dac8, what then happens is not traced).</summary>
    public bool AutoRespawn { get; init; } = true;

    /// <summary>Shift a FullCar in manual mode (see <see cref="PathFollowingDriver"/>); false = leave the car's box alone.</summary>
    public bool ShiftItself { get; init; } = true;
}

/// <summary>Recovery events of a driver.</summary>
public enum RecoveryKind
{
    Reverse,
    RespawnRolled,
    RespawnStuck,
    RespawnOffRoad,
}

/// <summary>
/// A deterministic path-following AI driver for one simulated car (<see cref="FullCar"/> or <see cref="SimpleCar"/>),
/// read by the physics scene every fixed step. Everything here is GUESSED (the exe's driving logic was not traced):
/// pure-pursuit steering on the lane centreline (+ lateral offset), a speed target from a curvature/braking profile of
/// the path and the car's grip (scaled by the AI level and <c>AI_params</c>), proportional throttle/brake with simple
/// traction control and ABS, car avoidance (pass on the free side or follow), and recovery (reverse when stuck, respawn
/// on the path when rolled, stuck for good or far off the road). Speed scaling for rubber-banding is off by default.
/// </summary>
public sealed class PathFollowingDriver : IVehicleInputSource
{
    private readonly SimCar _car;
    private IRoadPath? _path;
    private SpeedProfile? _profile;
    private VehicleCaps _caps;
    private double _sMod;
    private double _lastTime = double.NaN;
    private double _clock;
    private double _rolledFor;
    private double _stuckSince = double.NaN;
    private double _offSince = double.NaN;
    private double _reverseUntil = double.NaN;
    private int _reverseTries;
    private double _avoidOffset;
    private bool _profileDirty = true;

    public PathFollowingDriver(SimCar car, VehicleCaps? caps = null, DriverSettings? settings = null)
    {
        _car = car;
        _caps = caps ?? VehicleCaps.For(car);
        Settings = settings ?? new DriverSettings();
    }

    public SimCar Car => _car;

    public DriverSettings Settings { get; private set; }

    public VehicleCaps Caps
    {
        get => _caps;
        set
        {
            _caps = value;
            _profileDirty = true;
        }
    }

    public DriverMode Mode { get; set; } = DriverMode.Drive;

    public PathEnd End { get; set; } = PathEnd.Continue;

    /// <summary>Throttle while in <see cref="DriverMode.Launch"/> (0..1).</summary>
    public double LaunchThrottle { get; set; }

    /// <summary>Lateral offset from the centreline (m, + = left): grid column, traffic lane.</summary>
    public double LaneOffset { get; set; }

    /// <summary>Rubber-band speed factor set by a director (1 = off): scales every speed target, corners included, so a
    /// factor above 1 eats into the grip margin of <see cref="AiLevel.GripUse"/> (keep it small).</summary>
    public double SpeedScale { get; set; } = 1.0;

    /// <summary>Extra speed cap (m/s), e.g. following a script target.</summary>
    public double SpeedCap { get; set; } = double.PositiveInfinity;

    /// <summary>Other cars to avoid (the scene's cars; this car is skipped).</summary>
    public IReadOnlyList<SimCar>? Others { get; set; }

    public IRoadPath? Path => _path;

    /// <summary>Unwrapped distance travelled along the path since it was set (laps × length + s on loops).</summary>
    public double Progress { get; private set; }

    /// <summary>Signed lateral distance from the path centreline at the last step (m, + = left).</summary>
    public double LateralError { get; private set; }

    public double TargetSpeed { get; private set; }

    public CarControls Last { get; } = new();

    public List<(double Time, RecoveryKind Kind)> Recoveries { get; } = [];

    public event Action<PathFollowingDriver, RecoveryKind>? Recovered;

    public void SetLevel(double stored)
    {
        Settings = Settings with { Level = stored };
        _profileDirty = true;
    }

    /// <summary>Follows <paramref name="path"/> from the car's current projection on it.</summary>
    public void SetPath(IRoadPath? path, PathEnd end = PathEnd.Continue)
    {
        _path = path;
        End = end;
        _profileDirty = true;
        Progress = 0;
        // open paths may cross themselves: start the tracking near their beginning unless the car is far along
        _sMod = path is null ? 0 : path.Project(_car.Position.ToVector3(), 0f, path.Closed ? float.PositiveInfinity : 60f);
        if (path is { Closed: false } && PathSetRoadSource.Measure(path, _car.Position.ToVector3(), (float)_sMod).Distance > 30)
        {
            _sMod = path.Project(_car.Position.ToVector3());
        }
        if (path is { Closed: false })
        {
            Progress = _sMod;
        }
    }

    private void EnsureProfile()
    {
        if (!_profileDirty || _path is null)
        {
            return;
        }

        var lvl = Settings.Level;
        _profile = new SpeedProfile(_path, _caps.LateralAccel * AiLevel.GripUse(lvl), _caps.BrakeDecel * AiLevel.BrakeUse(lvl),
            _caps.TopSpeed, End == PathEnd.Stop, laneOffset: (float)LaneOffset);
        _profileDirty = false;
    }

    private static long s_readTicks;

    /// <summary>Wall time spent in <see cref="Read"/> by all drivers of the process (diagnostics, seconds).</summary>
    public static double TotalReadSeconds => Interlocked.Read(ref s_readTicks) / (double)System.Diagnostics.Stopwatch.Frequency;

    public void Read(double time, CarControls into)
    {
        var t0 = System.Diagnostics.Stopwatch.GetTimestamp();
        ReadCore(time, into);
        Interlocked.Add(ref s_readTicks, System.Diagnostics.Stopwatch.GetTimestamp() - t0);
    }

    private void ReadCore(double time, CarControls into)
    {
        into.Clear();
        // own clock: the car's clock restarts when it is placed (respawn, script setMatrix)
        var dt = double.IsNaN(_lastTime) ? 0 : time > _lastTime ? time - _lastTime : PhysicsScene.FixedStep;
        _lastTime = time;
        _clock += dt;
        time = _clock;
        switch (Mode)
        {
            case DriverMode.Off:
                Last.CopyFrom(into);
                return;
            case DriverMode.Hold:
                into.Brake = 1;
                Last.CopyFrom(into);
                return;
            case DriverMode.Launch:
                into.Brake = 1;
                into.Handbrake = 1;
                into.Throttle = Math.Clamp(LaunchThrottle, 0, 1);
                Last.CopyFrom(into);
                return;
        }

        if (_path is not { } path)
        {
            into.Brake = 1;
            Last.CopyFrom(into);
            return;
        }

        EnsureProfile();
        var q = _car.Orientation;
        var pos = _car.Position;
        var fwd = q.Rotate(new Vec3(0, 0, -1));
        var left = q.Rotate(new Vec3(-1, 0, 0));
        var up = q.Rotate(Vec3.UnitY);
        var vel = _car.Velocity;
        var vf = vel.Dot(fwd);
        var speed = Math.Sqrt((vel.X * vel.X) + (vel.Z * vel.Z));

        // ---- progress along the path (continuity window)
        var p3 = pos.ToVector3();
        var window = (float)(15 + (speed * dt * 2));
        var s = path.Project(p3, (float)_sMod, window);
        var dsAlong = s - _sMod;
        if (path.Closed)
        {
            if (dsAlong > path.Length / 2)
            {
                dsAlong -= path.Length;
            }
            else if (dsAlong < -path.Length / 2)
            {
                dsAlong += path.Length;
            }
        }

        _sMod = s;
        Progress += dsAlong;
        var here = path.Sample(s);
        var lat = Vector3.Dot(p3 - here.Position, RoadMath.LeftOf(here.Tangent));
        LateralError = lat;

        if (Recover(time, dt, up, vf, speed, here, lat, into))
        {
            Last.CopyFrom(into);
            return;
        }

        if (up.Y < 0.6)
        {
            Last.CopyFrom(into); // on its side or roof: no throttle (Slrr.Physics keeps driving tumbling cars), wait for the respawn
            return;
        }

        // ---- avoidance: lateral offset / follow speed
        var follow = double.PositiveInfinity;
        if (Settings.Avoid && Others is { } others)
        {
            follow = Avoid(others, pos, fwd, vf, s, here, dt);
        }
        else
        {
            _avoidOffset = 0;
        }

        // ---- steering: pure pursuit from the rear axle to a point on the (offset) centreline
        var wb = _caps.Wheelbase;
        var rear = pos - (fwd * (wb / 2));
        var look = Math.Clamp(Settings.LookBase + (Settings.LookGain * Math.Abs(vf)), Settings.LookMin, Settings.LookMax);
        var ahead = path.Sample((float)(s + look));
        var offset = LaneOffset + _avoidOffset;
        var aim = ahead.Position + (RoadMath.LeftOf(ahead.Tangent) * (float)offset);
        var to = new Vec3(aim.X - rear.X, 0, aim.Z - rear.Z);
        var ld = Math.Max(1.0, to.Length);
        var alpha = Math.Atan2(to.Dot(left), to.Dot(fwd));
        var delta = Math.Atan(2 * wb * Math.Sin(alpha) / ld);
        var steer = delta / _caps.MaxSteerAngle;
        if (_car is FullCar fc && fc.Drivetrain.SteerHelp > 0)
        {
            steer *= 1 + (fc.Drivetrain.SteerHelp * Math.Abs(vf) / 20.0);
        }

        // never ask for more steering than the lateral limit allows at this speed: the tyres of a Slrr.Physics car
        // grip past the rollover threshold, so a sudden swerve tips it over (seen in the first race runs)
        if (vf > 3)
        {
            // (combined with braking the tyres have less to give: the limit shrinks with the last brake input)
            var aMax = _caps.LateralAccel * AiLevel.GripUse(Settings.Level) * (1 - (0.5 * Last.Brake));
            var lim = Math.Atan(aMax * wb / (vf * vf)) / _caps.MaxSteerAngle;
            // ... but never below what the road itself needs here (+30 %), or the car runs wide out of its lane
            var need = Math.Atan(wb * Math.Max(Math.Abs(here.Curvature), Math.Abs(ahead.Curvature))) / _caps.MaxSteerAngle;
            lim = Math.Max(lim, (1.3 * need) + 0.03);
            steer = Math.Clamp(steer, -lim, lim);
        }

        into.Steer = Math.Clamp(steer, -1, 1);

        // ---- speed target
        var profile = _profile!.At(s + (Math.Max(0, vf) * Settings.SpeedPreview));
        var vt = Math.Min(Math.Min(profile, _caps.TopSpeed), SpeedCap) * SpeedScale;
        vt = Math.Min(vt, follow);
        if (Math.Abs(left.Y) > 0.31 && up.Y > 0)
        {
            vt = Math.Min(vt, 0.85 * vf); // body rolled > 18°: lift and brake gently (GUESSED roll guard)
        }

        if (Math.Abs(alpha) > Math.PI / 2)
        {
            vt = Math.Min(vt, 5); // pointing away from the path: slow U-turn
        }

        TargetSpeed = vt;
        var err = vt - vf;
        if (err >= -0.5)
        {
            into.Throttle = Math.Clamp(0.3 + (0.35 * err), 0, 1);
        }
        else if (err < -1.0)
        {
            into.Brake = Math.Clamp(0.1 + (0.3 * (-err - 1.0)), 0, 1);
        }

        if (vt < 0.3 && Math.Abs(vf) < 0.5)
        {
            into.Throttle = 0;
            into.Brake = 1;
        }

        if (Math.Abs(steer) > 1.1 && vf > 12)
        {
            into.Throttle *= 0.5; // understeer: lift
        }

        Assists(into);
        if (_car is FullCar { Drivetrain.Gear: -1 } && Settings.ShiftItself && vf < -1.0)
        {
            into.Throttle = 0; // still rolling backwards in reverse: stop before selecting first
            into.Brake = 1;
        }

        Shift(time, vf, wantReverse: false, into);
        Last.CopyFrom(into);
    }

    private int _gearAxisLast;
    private double _lastShiftTime = double.NegativeInfinity;

    /// <summary>
    /// The driver shifts a FullCar itself in manual mode (the exe's brain sends its car <c>transmission 0</c> on AI_race and
    /// AI_spline, MEASURED 0x433752/0x433a40, i.e. the AI shifts; our automatic box also downshifts on locked wheels).
    /// Shift points GUESSED: up above 92 % of the rev limit, down when the lower gear stays under 75 % and this gear is
    /// under 55 %; rpm from the road speed (not the wheel spin).
    /// </summary>
    private void Shift(double time, double vf, bool wantReverse, CarControls into)
    {
        if (_car is not FullCar f || !Settings.ShiftItself)
        {
            return;
        }

        var dt = f.Drivetrain;
        if (dt.Mode != TransmissionMode.Manual)
        {
            dt.Mode = TransmissionMode.Manual;
        }

        if (_gearAxisLast != 0 || dt.Shifting)
        {
            into.GearUpDown = 0;
            _gearAxisLast = 0;
            return;
        }

        var spec = f.Spec;
        var gear = dt.Gear;
        int target;
        if (wantReverse)
        {
            target = Math.Abs(vf) < 1.0 ? -1 : gear;
        }
        else if (gear <= 0)
        {
            target = vf > -1.0 ? 1 : gear;
        }
        else
        {
            var limit = spec.Engine.RpmLimit;
            target = gear;
            if (time - _lastShiftTime >= 0.5)
            {
                if (gear < dt.TopGear && Rpm(spec, gear, vf) > 0.92 * limit && spec.Drivetrain.Ratios[gear + 1] > 0)
                {
                    target = gear + 1;
                }
                else if (gear > 1 && Rpm(spec, gear - 1, vf) < 0.75 * limit && Rpm(spec, gear, vf) < 0.55 * limit)
                {
                    target = gear - 1;
                }
            }
        }

        if (target != gear)
        {
            into.GearUpDown = Math.Sign(target - gear);
            _gearAxisLast = into.GearUpDown;
            _lastShiftTime = time;
        }
    }

    private static double Rpm(VehicleSpec spec, int gear, double v)
    {
        var r = spec.Wheels.Where(w => w.Drive > 0).Select(w => w.Radius).DefaultIfEmpty(0.3).Average();
        return Math.Abs(v) / r * spec.Drivetrain.Ratios[gear] * spec.Drivetrain.FinalDrive * 60 / (2 * Math.PI);
    }

    private void Assists(CarControls into)
    {
        if (_car is not FullCar f)
        {
            return;
        }

        var spec = f.Spec;
        var wheels = f.Vehicle.Wheels;
        double spin = 0, lockUp = 0;
        for (var i = 0; i < wheels.Count; i++)
        {
            var sr = wheels[i].SlipRatio;
            if (spec.Wheels[i].Drive > 0)
            {
                spin = Math.Max(spin, sr);
            }

            lockUp = Math.Min(lockUp, sr);
        }

        if (spin > 0.2 && into.Throttle > 0)
        {
            into.Throttle *= 0.4; // traction control (GUESSED)
        }

        if (lockUp < -0.2 && into.Brake > 0)
        {
            into.Brake *= 0.75; // ABS (GUESSED)
        }
    }

    private double Avoid(IReadOnlyList<SimCar> others, Vec3 pos, Vec3 fwd, double vf, double s, PathSample here, double dt)
    {
        var path = _path!;
        var follow = double.PositiveInfinity;
        var range = 12 + (1.6 * Math.Max(0, vf));
        var want = 0.0;
        var blocked = false;
        foreach (var o in others)
        {
            if (ReferenceEquals(o, _car))
            {
                continue;
            }

            var rel = o.Position - pos;
            var dAhead = rel.Dot(fwd);
            if (dAhead < 1 || dAhead > range || Math.Abs(rel.Y) > 4)
            {
                continue;
            }

            var at = path.Sample((float)(s + dAhead));
            var otherLat = Vector3.Dot(o.Position.ToVector3() - at.Position, RoadMath.LeftOf(at.Tangent));
            var mine = LaneOffset + _avoidOffset;
            if (Math.Abs(otherLat - mine) > 3.0)
            {
                continue;
            }

            var ov = o.Velocity.Dot(fwd);
            var closing = vf - ov;
            var gap = dAhead - 5.0;
            if (closing > 1.0 || gap < 6)
            {
                // try to pass on the side with more room (stay inside the half width - 1.2 m)
                var room = here.HalfWidth - 1.2;
                var leftSide = otherLat + 3.2;
                var rightSide = otherLat - 3.2;
                double? pick = null;
                if (leftSide <= room && rightSide >= -room)
                {
                    pick = Math.Abs(leftSide - LaneOffset) <= Math.Abs(rightSide - LaneOffset) ? leftSide : rightSide;
                }
                else if (leftSide <= room)
                {
                    pick = leftSide;
                }
                else if (rightSide >= -room)
                {
                    pick = rightSide;
                }

                if (pick is { } p && closing > 1.0 && Settings.Overtake)
                {
                    want = p - LaneOffset;
                    blocked = true;
                }
                else
                {
                    // follow: IDM-like gap keeping (1.2 s time gap + 6 m)
                    var desiredGap = 8 + (1.0 * Math.Max(0, ov));
                    follow = Math.Min(follow, Math.Max(0, ov + ((dAhead - desiredGap) * 0.6)));
                }
            }
        }

        // lateral offset changes at most 2 m/s; back to the lane when clear
        var target = blocked ? want : 0.0;
        var step = 2.0 * dt;
        _avoidOffset = Math.Abs(target - _avoidOffset) <= step ? target : _avoidOffset + (Math.Sign(target - _avoidOffset) * step);
        return follow;
    }

    private bool Recover(double time, double dt, Vec3 up, double vf, double speed, PathSample here, double lat, CarControls into)
    {
        var st = Settings;

        // rolled / on its side
        // (accumulated: a car tumbling over and over is upside down only part of the time)
        _rolledFor = up.Y < st.RolledUp ? _rolledFor + dt : Math.Max(0, _rolledFor - (0.25 * dt));
        if (_rolledFor >= st.RolledTime)
        {
            Respawn(time, RecoveryKind.RespawnRolled);
            return true;
        }

        // far off the road
        if (Math.Abs(lat) > here.HalfWidth + st.OffRoadMargin)
        {
            _offSince = double.IsNaN(_offSince) ? time : _offSince;
            if (time - _offSince >= st.OffRoadTime)
            {
                Respawn(time, RecoveryKind.RespawnOffRoad);
                return true;
            }
        }
        else
        {
            _offSince = double.NaN;
        }

        // reversing out of a jam
        if (!double.IsNaN(_reverseUntil))
        {
            if (time < _reverseUntil)
            {
                ReverseControls(time, vf, lat, into);
                return true;
            }

            _reverseUntil = double.NaN;
            _stuckSince = double.NaN;
        }

        // stuck: the controller asked for speed but the car does not move
        var wants = TargetSpeed > 3 && Last.Throttle > 0.2;
        if (wants && speed < 1.0)
        {
            _stuckSince = double.IsNaN(_stuckSince) ? time : _stuckSince;
            if (time - _stuckSince >= st.StuckTime)
            {
                if (_caps.CanReverse && _reverseTries < 2)
                {
                    _reverseTries++;
                    _reverseUntil = time + 2.0;
                    Note(time, RecoveryKind.Reverse);
                    ReverseControls(time, vf, lat, into);
                    return true;
                }

                Respawn(time, RecoveryKind.RespawnStuck);
                return true;
            }
        }
        else if (speed > 3)
        {
            _stuckSince = double.NaN;
            _reverseTries = 0;
        }

        return false;
    }

    private void ReverseControls(double time, double vf, double lat, CarControls into)
    {
        // turn the nose towards the path while backing (steer away from it)
        into.Steer = vf < -0.5 ? (lat > 0 ? 1 : -1) : 0;
        if (_car is FullCar f && Settings.ShiftItself)
        {
            Shift(time, vf, wantReverse: true, into);
            if (f.Drivetrain.Gear == -1)
            {
                into.Throttle = 0.5;
            }
            else
            {
                into.Brake = 1;
            }
        }
        else
        {
            into.Brake = _caps.CanReverse ? 0.6 : 0; // automatic box: the brake pedal reverses at standstill
        }
    }

    private void Note(double time, RecoveryKind kind)
    {
        Recoveries.Add((time, kind));
        Recovered?.Invoke(this, kind);
    }

    /// <summary>Puts the car upright on the path 10 m behind its projection (lane offset kept), at rest.</summary>
    public void Respawn(double time, RecoveryKind kind)
    {
        Note(time, kind);
        _rolledFor = 0;
        _stuckSince = _offSince = _reverseUntil = double.NaN;
        _reverseTries = 0;
        if (!Settings.AutoRespawn || _path is not { } path)
        {
            return;
        }

        var s = (float)(_sMod - 10);
        var smp = path.Sample(s);
        var at = smp.Position + (RoadMath.LeftOf(smp.Tangent) * (float)LaneOffset);
        _car.Place(new Vec3(at.X, at.Y + 0.5, at.Z), Conv.FromYpr(RoadMath.YawOf(smp.Tangent), 0, 0));
        var back = path.Closed ? s : Math.Max(0, s);
        var d = back - _sMod;
        if (path.Closed && d > path.Length / 2)
        {
            d -= path.Length;
        }

        Progress += d;
        _sMod = path.Closed ? ((back % path.Length) + path.Length) % path.Length : back;
    }
}
