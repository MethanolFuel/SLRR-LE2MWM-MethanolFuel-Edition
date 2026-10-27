using System.Numerics;
using Slrr.Engine.Parts;
using Slrr.Engine.Resources;

namespace Slrr.Game.Render.Cars;

/// <summary>
/// Per-wheel animation state. <see cref="Spin"/> is the accumulated forward rolling angle in radians (positive = the car rolls forward),
/// <see cref="Steer"/> the steering angle in radians (positive = turn left, i.e. towards -X: GUESSED sign), <see cref="Travel"/> the
/// suspension compression in metres relative to the cfg wheel position (positive = the wheel moves up relative to the body).
/// </summary>
public readonly record struct WheelPose(float Spin, float Steer, float Travel);

/// <summary>What the renderer needs to know about one car beyond its part tree.</summary>
public sealed class CarState
{
    /// <summary>Wheels by cfg wheel index (0..3 = FL, FR, RL, RR; missing entries are rest poses).</summary>
    public WheelPose[] Wheels { get; set; } = new WheelPose[4];

    /// <summary>Overrides the world matrix of the car (otherwise the chassis node's position and yaw/pitch/roll are used).</summary>
    public Matrix4x4? World { get; set; }

    public bool Headlights { get; set; }

    public bool BrakeLights { get; set; }

    public bool Reversing { get; set; }

    public bool IndicatorLeft { get; set; }

    public bool IndicatorRight { get; set; }
}

/// <summary>
/// The seam to Slrr.Physics (which is integrated by the lead): per frame the adapter asks for the car's animation state.
/// An implementation may read its own simulation; <see cref="HostCarTelemetry"/> derives what the host already stores.
/// </summary>
public interface ICarTelemetry
{
    /// <summary>State of the car whose chassis part is <paramref name="chassis"/> (node = its resource instance). Null = rest pose.</summary>
    CarState? Get(IGameWorld world, ResourceNode node, PartState chassis, float dt);
}

/// <summary>
/// Default telemetry from the host's own data, no physics needed:
/// <list type="bullet">
/// <item>spin = integral of (speed along the car's forward axis / wheel radius), speed from <see cref="ResourceNode.Vel"/> (GameRef.setState writes it), radius from <see cref="WheelState.Radius"/> (G: that Physics keeps writing Vel);</item>
/// <item>steer = <c>ChassisTelemetry.CurrentSteering1</c> (M: cell identity from Chassis.java; GUESSED range -1..1) times 0.5 rad on the two front wheels (G);</item>
/// <item>travel = 0 (no suspension state exists in the host; Physics should provide an ICarTelemetry);</item>
/// <item>lights = off.</item>
/// </list>
/// </summary>
public sealed class HostCarTelemetry : ICarTelemetry
{
    /// <summary>Steering lock in radians at full deflection (GUESSED).</summary>
    public float SteerLock { get; set; } = 0.5f;

    private readonly Dictionary<int, float[]> _spin = [];
    private readonly Dictionary<int, float> _lastTime = [];

    public CarState? Get(IGameWorld world, ResourceNode node, PartState chassis, float dt)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(node);
        ArgumentNullException.ThrowIfNull(chassis);
        var cs = world.Parts.ChassisOf(chassis);
        var state = new CarState();
        var forward = Vector3.TransformNormal(-Vector3.UnitZ, Slrr.Game.Render.Scene.HostIds.Local(node));
        var vel = new Vector3(node.Vel[0], node.Vel[1], node.Vel[2]);
        var speed = Vector3.Dot(vel, forward);
        if (!_spin.TryGetValue(node.Id, out var spin))
        {
            _spin[node.Id] = spin = new float[4];
        }

        // Integrate over host time (not draw-to-draw time) so skipped draws do not lose rotation; a gap above 1 s is a teleport.
        var now = world.Vm.Time;
        var step = _lastTime.TryGetValue(node.Id, out var last) ? now - last : 0f;
        _lastTime[node.Id] = now;
        step = step is > 0f and <= 1f ? step : 0f;

        var steerCell = cs.Telemetry.GetFloat(ChassisTelemetry.CurrentSteering1);
        var steer = float.IsFinite(steerCell) ? Math.Clamp(steerCell, -1f, 1f) * SteerLock : 0f;
        for (var i = 0; i < 4; i++)
        {
            var radius = cs.Wheels.Count > i && cs.Wheels[i].Radius > 0.05f ? cs.Wheels[i].Radius : 0.3f;
            spin[i] += speed / radius * step;
            state.Wheels[i] = new WheelPose(spin[i], i < 2 ? steer : 0f, 0f);
        }

        return state;
    }
}
