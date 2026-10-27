using System.Numerics;

namespace Slrr.GameAudio;

public enum ImpactKind
{
    /// <summary>Body/object collision (sound.rpk "collision").</summary>
    Body,

    /// <summary>Hard tyre/kerb hit (same sample, G).</summary>
    Tyre,
}

/// <summary>A collision reported by the physics side: closing speed along the contact normal in m/s and the world contact point.</summary>
public readonly record struct ImpactEvent(float ClosingSpeed, Vector3 Position, ImpactKind Kind = ImpactKind.Body);

/// <summary>
/// What the physics adapter feeds the audio per frame for one car. Every member is a plain value so a headless script can drive it.
/// The field roles map onto the exe's per-car sound update (0x4518c0): <see cref="EngineAngularSpeed"/> = vehicle +0x1e1c,
/// <see cref="Throttle"/> = +0x1db4, <see cref="StarterEngaged"/> = +0x1dc8, <see cref="DriveShaftAngularSpeed"/> = +0x1e14,
/// <see cref="DriveShaftTorque"/> = +0x1e24 (offsets MEASURED, the physical meaning of the last three GUESSED).
/// </summary>
public interface IVehicleAudioTelemetry
{
    /// <summary>False while the car does not exist / is not simulated (all its sounds fall silent).</summary>
    bool Active { get; }

    /// <summary>World position of the car (engine/exhaust emitter).</summary>
    Vector3 Position { get; }

    /// <summary>Crankshaft angular speed in rad/s (the exe converts with 9.5493 and adds 1 to get the table rpm).</summary>
    float EngineAngularSpeed { get; }

    /// <summary>Pedal 0..1 (negative values are treated by magnitude, as the exe takes fabs).</summary>
    float Throttle { get; }

    /// <summary>True while the starter motor cranks the engine.</summary>
    bool StarterEngaged { get; }

    /// <summary>Transmission output angular speed in rad/s (signed: negative in reverse).</summary>
    float DriveShaftAngularSpeed { get; }

    /// <summary>Torque through the driveline in N m (magnitude drives the whine volume: |T| x 1e-4 clamped to 1).</summary>
    float DriveShaftTorque { get; }

    /// <summary>Body speed in m/s (skid pitch).</summary>
    float Speed { get; }

    int WheelCount { get; }

    /// <summary>Tyre slip ratio 0 (gripping) .. 1 (sliding) of wheel <paramref name="index"/>.</summary>
    float WheelSlip(int index);

    /// <summary>World contact position of wheel <paramref name="index"/>.</summary>
    Vector3 WheelPosition(int index);

    /// <summary>Moves the collisions since the last call into <paramref name="into"/> (and forgets them).</summary>
    void DrainImpacts(ICollection<ImpactEvent> into);
}

/// <summary>Mutable plain-data implementation (tests, scripted scenarios, and a convenient base for the physics adapter).</summary>
public sealed class ManualTelemetry : IVehicleAudioTelemetry
{
    private readonly List<ImpactEvent> _impacts = [];

    public bool Active { get; set; } = true;

    public Vector3 Position { get; set; }

    public float EngineAngularSpeed { get; set; }

    public float Throttle { get; set; }

    public bool StarterEngaged { get; set; }

    public float DriveShaftAngularSpeed { get; set; }

    public float DriveShaftTorque { get; set; }

    public float Speed { get; set; }

    public float[] Slip { get; set; } = [0f, 0f, 0f, 0f];

    public Vector3[] WheelPositions { get; set; } = [];

    public int WheelCount => Slip.Length;

    public float WheelSlip(int index) => index >= 0 && index < Slip.Length ? Slip[index] : 0f;

    public Vector3 WheelPosition(int index) => index >= 0 && index < WheelPositions.Length ? WheelPositions[index] : Position;

    public void AddImpact(ImpactEvent e) => _impacts.Add(e);

    public void DrainImpacts(ICollection<ImpactEvent> into)
    {
        foreach (var e in _impacts)
        {
            into.Add(e);
        }

        _impacts.Clear();
    }

    /// <summary>Engine speed helper: rpm to rad/s.</summary>
    public static float RpmToRad(float rpm) => rpm / 9.549297f;
}
