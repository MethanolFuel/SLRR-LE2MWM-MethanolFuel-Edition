using System.Numerics;

namespace Slrr.Engine.Commands;

/// <summary>
/// Stand-in vehicle state for running race scripts without the physics adapter: every instance of native kind
/// <c>car</c> is a point mass on a flat plane at its current height, steered like a bicycle. The resource tree stays the
/// source of truth (scripts' <c>setMatrix</c> teleports are respected; <c>getPos</c>/<c>getVel</c> see the motion).
/// Deterministic (fixed formulas, no randomness). Everything here is GUESSED and only meant for headless runs;
/// Slrr.Physics replaces it through <see cref="IVehicleState"/> / <see cref="IVehicleDriver"/>.
/// </summary>
public sealed class KinematicVehicles(IObjectDirectory world, ICarControl? control = null) : IVehicleState, IVehicleDriver
{
    private readonly Dictionary<int, DriveInput> _inputs = [];
    private readonly Dictionary<int, (int Reset, int Vel)> _seen = [];

    /// <summary>Full-throttle acceleration (m/s²).</summary>
    public float Acceleration { get; set; } = 5f;

    /// <summary>Full-brake deceleration (m/s²).</summary>
    public float BrakeDeceleration { get; set; } = 9f;

    /// <summary>Linear drag (1/s) and rolling resistance (m/s²) when coasting.</summary>
    public float Drag { get; set; } = 0.08f;

    public float Rolling { get; set; } = 0.3f;

    /// <summary>Yaw rate at full steer and speed (rad/s).</summary>
    public float MaxYawRate { get; set; } = 0.9f;

    public float MaxSpeed { get; set; } = 90f;

    public IReadOnlyDictionary<int, DriveInput> Inputs => _inputs;

    public bool TryGetVehicle(int carId, out Pose pose)
    {
        pose = default;
        return world.Get(carId) is { Kind: "car" } && world.TryGetPose(carId, out pose);
    }

    public void SetInput(int carId, DriveInput input) => _inputs[carId] = input;

    public DriveInput InputOf(int carId) => _inputs.GetValueOrDefault(carId);

    public void Step(float dt)
    {
        if (dt <= 0)
        {
            return;
        }

        foreach (var car in world.OfKind("car").ToList())
        {
            if (!world.TryGetPose(car.Id, out var pose))
            {
                continue;
            }

            var ctl = control?.Get(car.Id);
            var vel = pose.Velocity;
            if (ctl is not null)
            {
                var seen = _seen.GetValueOrDefault(car.Id);
                if (ctl.ResetSerial != seen.Reset)
                {
                    vel = default;
                }

                if (ctl.VelocitySerial != seen.Vel && ctl.RequestedVelocity is { } rv)
                {
                    vel = rv.Linear;
                }

                _seen[car.Id] = (ctl.ResetSerial, ctl.VelocitySerial);
            }

            var input = _inputs.GetValueOrDefault(car.Id);
            var hold = ctl?.Hold ?? CarHold.Free;
            if (ctl is { Suspended: true } || hold is CarHold.Stopped or CarHold.Idle)
            {
                if (vel != default)
                {
                    world.SetPose(car.Id, pose with { Velocity = default });
                }

                continue;
            }

            if (hold == CarHold.Braking)
            {
                input = DriveInput.FullBrake;
            }

            var fwd = pose.Orientation.Forward;
            fwd.Y = 0;
            fwd = fwd.LengthSquared() > 1e-8f ? Vector3.Normalize(fwd) : new Vector3(0, 0, -1);
            var speed = Vector3.Dot(vel, fwd);
            if (input == default && MathF.Abs(speed) < 1e-3f)
            {
                if (vel != default)
                {
                    world.SetPose(car.Id, pose with { Velocity = default });
                }

                continue;
            }

            var accel = (input.Throttle * Acceleration) - (Drag * speed);
            var decel = (input.Brake * BrakeDeceleration) + (input.Throttle <= 0 ? Rolling : 0f);
            speed += accel * dt;
            speed = speed > 0 ? Math.Max(0f, speed - (decel * dt)) : Math.Min(0f, speed + (decel * dt));
            speed = Math.Clamp(speed, -MaxSpeed, MaxSpeed);

            var steer = Math.Clamp(input.Steer, -1f, 1f);
            var yawRate = -steer * MaxYawRate * Math.Min(1f, MathF.Abs(speed) / 8f) * MathF.Sign(speed);
            var ori = pose.Orientation with { Yaw = pose.Orientation.Yaw + (yawRate * dt) };
            var nf = ori.Forward;
            nf.Y = 0;
            nf = nf.LengthSquared() > 1e-8f ? Vector3.Normalize(nf) : fwd;
            var nv = nf * speed;
            world.SetPose(car.Id, new Pose(pose.Position + (nv * dt), ori, nv));
        }
    }
}
