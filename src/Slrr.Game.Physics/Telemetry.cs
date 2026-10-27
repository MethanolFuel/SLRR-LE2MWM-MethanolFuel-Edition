using System.Numerics;

namespace Slrr.Game.Physics;

/// <summary>Per-wheel state after a fixed step (for render: wheel placement/rotation; audio: skid; scripts: suspension).</summary>
/// <param name="Center">Wheel centre, world.</param>
/// <param name="LocalCenter">Wheel centre in the chassis frame (relative to the chassis origin, i.e. the cfg <c>wheel</c> position plus travel).</param>
/// <param name="SpinRate">rad/s, positive = rolling forward.</param>
/// <param name="Rotation">Accumulated rolling angle [rad], wrapped to [0, 2π).</param>
/// <param name="SteerAngle">rad, + = left.</param>
/// <param name="Travel">Suspension travel from the static position, + = compressed [m].</param>
/// <param name="Load">Normal load [N].</param>
/// <param name="Skid">Combined slip magnitude √(κ² + tan²α) (GUESSED skid measure for marks/sound).</param>
/// <param name="Surface">Map surface index under the wheel (-1 airborne / unknown).</param>
public readonly record struct WheelTelemetry(
    Vector3 Center,
    Vector3 LocalCenter,
    float Radius,
    float SpinRate,
    float Rotation,
    float SteerAngle,
    float Travel,
    float Load,
    float SlipRatio,
    float SlipAngle,
    float Skid,
    bool Grounded,
    int Surface);

/// <summary>
/// One car after a fixed step. Units SI (m, s, rad, N). <see cref="Position"/> is the chassis ORIGIN (the frame the
/// cfg wheel/body positions are given in), not the centre of mass. G-forces are the accelerometer reading (specific
/// force, gravity included: 1 g vertical at rest) in the body frame.
/// </summary>
public sealed record CarTelemetry
{
    public required int Id { get; init; }

    public required string Name { get; init; }

    public required double Time { get; init; }

    public required long Step { get; init; }

    public required bool Simulated { get; init; }

    public required Vector3 Position { get; init; }

    public required Vector3 CenterOfMass { get; init; }

    public required Quaternion Orientation { get; init; }

    public required YawPitchRoll Ypr { get; init; }

    public required Vector3 Velocity { get; init; }

    public required Vector3 AngularVelocity { get; init; }

    /// <summary>|v| [m/s].</summary>
    public required float Speed { get; init; }

    /// <summary>Signed speed along the car's forward axis [m/s].</summary>
    public required float ForwardSpeed { get; init; }

    public float SpeedSquare => Speed * Speed;

    public float SpeedKmh => Speed * 3.6f;

    public required float Rpm { get; init; }

    public required float IdleRpm { get; init; }

    public required float RpmLimit { get; init; }

    /// <summary>-1 reverse, 0 neutral, 1..n.</summary>
    public required int Gear { get; init; }

    public required bool EngineRunning { get; init; }

    public required float Throttle { get; init; }

    public required float Brake { get; init; }

    /// <summary>Clutch engagement 0..1 (1 = engaged).</summary>
    public required float Clutch { get; init; }

    public required float Handbrake { get; init; }

    /// <summary>Steering -1..1 (+ = left) after the rate limiter.</summary>
    public required float Steer { get; init; }

    public required bool Nitro { get; init; }

    public required bool Horn { get; init; }

    /// <summary>Engine torque [N m] (after friction/pumping), for engine sound load.</summary>
    public required float EngineTorque { get; init; }

    /// <summary>Longitudinal g (+ = accelerating forward).</summary>
    public required float GLongitudinal { get; init; }

    /// <summary>Lateral g (+ = pushed to the left).</summary>
    public required float GLateral { get; init; }

    /// <summary>Vertical g (≈ 1 at rest).</summary>
    public required float GVertical { get; init; }

    /// <summary>Distance driven since the binding [m].</summary>
    public required double Odometer { get; init; }

    /// <summary>Sum of collision impulses / mass since the binding [m/s] (a damage measure for scripts; GUESSED role).</summary>
    public required double ImpactTotal { get; init; }

    public required IReadOnlyList<WheelTelemetry> Wheels { get; init; }
}

/// <summary>A collision of a car's chassis box with the static world or another car.</summary>
/// <param name="DeltaV">Normal impulse / car mass [m/s] (the speed change the hit caused).</param>
/// <param name="Other">Other car id, or 0 for the static world.</param>
/// <param name="Source">Owner of the world triangle (spatial node record id / placed object id), 0 for cars.</param>
public readonly record struct ImpactEvent(int CarId, double Time, Vector3 Point, Vector3 Normal, float DeltaV, int Other, int Surface, int Source);

/// <summary>What the render and audio adapters read (cross-area interface, docs/race/game-physics.md).</summary>
public interface ICarTelemetrySource
{
    /// <summary>Every simulated or bound car, latest fixed step.</summary>
    IReadOnlyList<CarTelemetry> Cars { get; }

    /// <summary>Telemetry of the car whose chassis instance id is <paramref name="id"/>, or null.</summary>
    CarTelemetry? Get(int id);

    /// <summary>Fraction of a fixed step accumulated but not yet simulated (0..1): render may interpolate.</summary>
    double Alpha { get; }

    /// <summary>Impacts raised during the last host frame.</summary>
    IReadOnlyList<ImpactEvent> Impacts { get; }
}
