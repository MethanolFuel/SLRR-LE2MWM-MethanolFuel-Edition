using Slrr.Engine.Ui;
using Slrr.Physics;
using Slrr.Vm;

namespace Slrr.Game.Physics;

/// <summary>What a driver (keyboard/gamepad through the host's virtual axes, a bot brain, a test script) asks for.</summary>
public sealed class CarControls
{
    /// <summary>0..1.</summary>
    public double Throttle { get; set; }

    /// <summary>0..1.</summary>
    public double Brake { get; set; }

    /// <summary>-1..1, +1 = full LEFT. MEASURED convention: <c>ControlSet</c> maps RCDIK_LEFT to AXIS_TURN_LEFTRIGHT
    /// +1 and RCDIK_RIGHT to -1; <see cref="DriverInput.Steer"/> uses the same sign.</summary>
    public double Steer { get; set; }

    /// <summary>Clutch PEDAL 0..1 (1 = pressed = disengaged; ControlSet maps RCDIK_C to AXIS_CLUTCH +1, M).</summary>
    public double ClutchPedal { get; set; }

    public double Handbrake { get; set; }

    public bool Nitro { get; set; }

    public bool Horn { get; set; }

    /// <summary>AXIS_GEAR_UPDOWN: +1 shift up (RCDIK_A), -1 shift down (RCDIK_Z) (M, ControlSet). Edges trigger shifts.</summary>
    public int GearUpDown { get; set; }

    public void CopyFrom(CarControls o)
    {
        Throttle = o.Throttle;
        Brake = o.Brake;
        Steer = o.Steer;
        ClutchPedal = o.ClutchPedal;
        Handbrake = o.Handbrake;
        Nitro = o.Nitro;
        Horn = o.Horn;
        GearUpDown = o.GearUpDown;
    }

    public void Clear() => CopyFrom(new CarControls());
}

/// <summary>Source of driver controls, read once per fixed physics step.</summary>
public interface IVehicleInputSource
{
    void Read(double time, CarControls into);
}

/// <summary>
/// The player's controls from the host's input layer: <see cref="UiEngine.VirtualAxis"/> of the Input.AXIS_* ids the
/// shipped <c>ControlSet</c> maps (numbers M, java.io.Input constants). Smoothing (<c>setAxisSmooth</c>) belongs to the
/// input layer and is not repeated here.
/// </summary>
public sealed class VirtualAxisInput(UiEngine ui, Func<VmObject?>? controller = null) : IVehicleInputSource
{
    public const int AxisTurnLeftRight = 1;
    public const int AxisThrottle = 28;
    public const int AxisBrake = 29;
    public const int AxisNitro = 31;
    public const int AxisHandbrake = 48;
    public const int AxisClutch = 49;
    public const int AxisGearUpDown = 50;
    public const int AxisHorn = 54;

    public void Read(double time, CarControls into)
    {
        var c = controller?.Invoke();
        float A(int axis) => ui.VirtualAxis(axis, c);
        into.Throttle = Math.Clamp(A(AxisThrottle), 0, 1);
        into.Brake = Math.Clamp(A(AxisBrake), 0, 1);
        into.Steer = Math.Clamp(A(AxisTurnLeftRight), -1, 1);
        into.ClutchPedal = Math.Clamp(A(AxisClutch), 0, 1);
        into.Handbrake = Math.Clamp(A(AxisHandbrake), 0, 1);
        into.Nitro = A(AxisNitro) > 0.5f;
        into.Horn = A(AxisHorn) > 0.5f;
        var g = A(AxisGearUpDown);
        into.GearUpDown = g > 0.5f ? 1 : g < -0.5f ? -1 : 0;
    }
}

/// <summary>Piecewise-constant scripted controls (tests, CLI): each key holds from its time until the next key.</summary>
public sealed class ScriptedInput : IVehicleInputSource
{
    private readonly List<(double Time, CarControls Controls)> _keys = [];

    public ScriptedInput At(double time, double throttle = 0, double brake = 0, double steer = 0, double handbrake = 0, int gear = 0, double clutch = 0)
    {
        _keys.Add((time, new CarControls { Throttle = throttle, Brake = brake, Steer = steer, Handbrake = handbrake, GearUpDown = gear, ClutchPedal = clutch }));
        _keys.Sort((a, b) => a.Time.CompareTo(b.Time));
        return this;
    }

    public void Read(double time, CarControls into)
    {
        CarControls? cur = null;
        foreach (var (t, c) in _keys)
        {
            if (t > time + 1e-9)
            {
                break;
            }

            cur = c;
        }

        if (cur is null)
        {
            into.Clear();
        }
        else
        {
            into.CopyFrom(cur);
        }
    }
}

/// <summary>Transmission modes sent by <c>Vehicle.setTransmission</c> as <c>"transmission n"</c> (values M, Vehicle.class constants).</summary>
public enum TransmissionMode
{
    Manual = 0,
    Auto = 1,
    ManualClutch = 2,
    SemiAuto = 5,
}

/// <summary>
/// Turns <see cref="CarControls"/> into <see cref="DriverInput"/> for one step: gear selection (manual edges or the
/// automatic box), shift timing, the automatic clutch, steering rate and assists. Everything here is GUESSED (the
/// exe's gearbox/clutch logic was not traced): upshift at 95 % of RPM_limit, downshift below 45 %, 0.25 s shifts
/// with the clutch open, launch slip that keeps the engine above 1.5x idle, brake-to-reverse at standstill in
/// <see cref="TransmissionMode.Auto"/>.
/// </summary>
public sealed class Drivetrain
{
    private int _lastGearAxis;
    private double _shiftEnd = -1;
    private double _lastShift = double.NegativeInfinity;

    public Drivetrain(VehicleSpec spec) => Spec = spec;

    public VehicleSpec Spec { get; }

    public TransmissionMode Mode { get; set; } = TransmissionMode.Auto;

    /// <summary>-1 reverse, 0 neutral, 1..Gears.</summary>
    public int Gear { get; set; }

    public bool Shifting => _shiftEnd >= 0;

    /// <summary>Full-lock steering rate [1/s] (GUESSED).</summary>
    public double SteerRate { get; set; } = 4.0;

    /// <summary><c>steerhelp f</c>: speed-sensitive steering reduction, 0..1 (GUESSED role).</summary>
    public double SteerHelp { get; set; }

    public double Steer { get; set; }

    public int Shifts { get; private set; }

    public int TopGear => Math.Min(Spec.Drivetrain.Gears, Spec.Drivetrain.Ratios.Length - 2);

    public void Reset()
    {
        Gear = 0;
        _shiftEnd = -1;
        _lastShift = double.NegativeInfinity;
        _lastGearAxis = 0;
        Steer = 0;
    }

    public void Apply(CarControls c, Vehicle car, double h, DriverInput into)
    {
        var time = car.Time;
        var spec = Spec;
        var rpm = car.EngineRpm;
        var limit = spec.Engine.RpmLimit;
        var speed = car.ForwardSpeed;
        var throttle = Math.Clamp(c.Throttle, 0, 1);
        var brake = Math.Clamp(c.Brake, 0, 1);

        // ---- steering: rate limited, optionally reduced with speed
        var target = Math.Clamp(c.Steer, -1, 1);
        if (SteerHelp > 0)
        {
            target /= 1 + SteerHelp * Math.Abs(speed) / 20.0;
        }

        var step = SteerRate * h;
        Steer = Math.Abs(target - Steer) <= step ? target : Steer + Math.Sign(target - Steer) * step;

        // ---- gear selection
        if (_shiftEnd >= 0 && time >= _shiftEnd)
        {
            _shiftEnd = -1;
        }

        var axis = c.GearUpDown;
        if (axis != 0 && axis != _lastGearAxis && Mode != TransmissionMode.Auto)
        {
            Shift(Math.Clamp(Gear + axis, -1, TopGear), time);
        }

        _lastGearAxis = axis;
        var reverseRequest = false;
        if (Mode == TransmissionMode.Auto && _shiftEnd < 0)
        {
            if (Gear <= 0)
            {
                if (throttle > 0.05)
                {
                    Shift(1, time, instant: true);
                }
                else if (brake > 0.05 && Math.Abs(speed) < 0.5)
                {
                    Shift(-1, time, instant: true);
                }
            }
            else if (Gear == -1)
            {
                if (throttle > 0.05 && Math.Abs(speed) < 0.5)
                {
                    Shift(1, time, instant: true);
                }
            }
            else if (time - _lastShift >= MinShiftInterval)
            {
                // Decisions on the rpm the driven wheels imply (not the free-revving engine during a shift or wheelspin).
                var wheelRpm = WheelRpm(car, Gear);
                if (wheelRpm > limit * 0.95 && rpm > limit * 0.9 && Gear < TopGear && spec.Drivetrain.Ratios[Gear + 1] > 0)
                {
                    Shift(Gear + 1, time);
                }
                else if (Gear > 1 && wheelRpm < limit * 0.45 && WheelRpm(car, Gear - 1) < limit * 0.85)
                {
                    Shift(Gear - 1, time);
                }
            }

            // reverse: the brake pedal drives backwards, the throttle brakes (GUESSED)
            if (Gear == -1)
            {
                reverseRequest = true;
                (throttle, brake) = (brake, throttle);
            }
        }

        // ---- clutch
        double clutch;
        if (_shiftEnd >= 0)
        {
            clutch = 0;
            throttle = 0;
        }
        else if (Mode == TransmissionMode.ManualClutch)
        {
            clutch = 1 - Math.Clamp(c.ClutchPedal, 0, 1);
        }
        else
        {
            var idle = spec.Engine.IdleRpm;
            clutch = Math.Clamp((rpm - 1.5 * idle) / (1.5 * idle), 0, 1);
            if ((Gear > 1 && rpm > idle * 1.1) || (Math.Abs(speed) > 8 && rpm > idle * 1.1))
            {
                clutch = 1;
            }

            clutch = Math.Min(clutch, 1 - Math.Clamp(c.ClutchPedal, 0, 1));
        }

        into.Throttle = throttle;
        into.Brake = brake;
        into.Clutch = clutch;
        into.Steer = Steer;
        into.HandBrake = Math.Clamp(c.Handbrake, 0, 1);
        into.Gear = reverseRequest ? -1 : Gear;
        into.Nitro = c.Nitro;
    }

    /// <summary>Minimum time between automatic shifts [s] (GUESSED).</summary>
    public double MinShiftInterval { get; set; } = 0.6;

    /// <summary>Engine rpm that the driven wheels' mean spin implies in <paramref name="gear"/>.</summary>
    public double WheelRpm(Vehicle car, int gear)
    {
        var dt = Spec.Drivetrain;
        var index = gear < 0 ? 7 : gear;
        if (index <= 0 || index >= dt.Ratios.Length)
        {
            return 0;
        }

        double spin = 0, weight = 0;
        for (var i = 0; i < Spec.Wheels.Count; i++)
        {
            var d = Spec.Wheels[i].Drive;
            spin += d * car.Wheels[i].Spin;
            weight += d;
        }

        return weight > 0 ? Math.Abs(spin / weight * dt.Ratios[index] * dt.FinalDrive) * 60 / (2 * Math.PI) : 0;
    }

    public void Shift(int gear, double time, bool instant = false)
    {
        if (gear == Gear)
        {
            return;
        }

        Gear = gear;
        Shifts++;
        _lastShift = time;
        _shiftEnd = instant ? -1 : time + Spec.Drivetrain.ShiftTime;
    }
}
