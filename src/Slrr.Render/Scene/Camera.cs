using System.Numerics;

namespace Slrr.Render.Scene;

/// <summary>
/// Perspective camera in game units (metres). Game axes, MEASURED from car cfgs (docs/phase4/render.md): +Y up
/// (wheels at y = -0.6 below the chassis origin), front = -Z (F_bumper slot z = -2.04, R_bumper +2.05), left = -X
/// (L_* slots, the LHD driver's seat and steering wheel at x = -0.38). Up = +Y with front -Z and left -X is a
/// right-handed frame, so the data is drawn unmirrored in GL's right-handed clip setup.
/// System.Numerics matrices are row-vector (v * M) and upload untransposed to GLSL (M * v).
/// </summary>
public sealed class Camera
{
    public Vector3 Position { get; set; } = new(0, 2, 6);

    public Vector3 Target { get; set; }

    public Vector3 Up { get; set; } = Vector3.UnitY;

    /// <summary>Vertical field of view in radians.</summary>
    public float FovY { get; set; } = MathF.PI / 4;

    public float Near { get; set; } = 0.1f;

    public float Far { get; set; } = 2000f;

    public Matrix4x4 View => Matrix4x4.CreateLookAt(Position, Target, Up);

    public Matrix4x4 Projection(float aspect) => Matrix4x4.CreatePerspectiveFieldOfView(FovY, Math.Max(aspect, 1e-3f), Near, Far);

    public Matrix4x4 ViewProjection(float aspect) => View * Projection(aspect);

    public Vector3 Forward => Vector3.Normalize(Target - Position);

    /// <summary>Projected size of a sphere as a fraction of the viewport height (used for LOD).</summary>
    public float ScreenFraction(Vector3 center, float radius)
    {
        var d = Vector3.Distance(center, Position);
        if (d <= radius)
        {
            return 10f;
        }

        return radius / (d * MathF.Tan(FovY * 0.5f));
    }
}

/// <summary>Orbit rig: yaw/pitch/distance around a target (car showroom, debug views).</summary>
public sealed class OrbitRig
{
    public Vector3 Target { get; set; }

    /// <summary>Yaw around +Y; 0 looks from +Z towards -Z (from behind the car, MEASURED front = -Z).</summary>
    public float Yaw { get; set; }

    public float Pitch { get; set; } = 0.35f;

    public float Distance { get; set; } = 6f;

    public void Apply(Camera cam)
    {
        ArgumentNullException.ThrowIfNull(cam);
        var p = Math.Clamp(Pitch, -1.5f, 1.5f);
        var dir = new Vector3(MathF.Sin(Yaw) * MathF.Cos(p), MathF.Sin(p), MathF.Cos(Yaw) * MathF.Cos(p));
        cam.Target = Target;
        cam.Position = Target + (dir * Distance);
        cam.Up = Vector3.UnitY;
    }

    public static OrbitRig Frame(Vector3 center, float radius, float yaw, float pitch, float fovY)
    {
        var dist = Math.Max(radius, 0.01f) / MathF.Sin(fovY * 0.5f) * 1.05f;
        return new OrbitRig { Target = center, Yaw = yaw, Pitch = pitch, Distance = dist };
    }
}

/// <summary>Chase rig: behind and above a moving body (heading = yaw of the body, GUESSED parameters like the cfg ext_camera).</summary>
public sealed class ChaseRig
{
    public float Height { get; set; } = 1.8f;

    public float Distance { get; set; } = 5f;

    public float LookAhead { get; set; } = 2f;

    public void Apply(Camera cam, Vector3 bodyPos, float heading)
    {
        ArgumentNullException.ThrowIfNull(cam);
        var fwd = new Vector3(-MathF.Sin(heading), 0, -MathF.Cos(heading)); // heading 0 = facing -Z
        cam.Position = bodyPos - (fwd * Distance) + new Vector3(0, Height, 0);
        cam.Target = bodyPos + (fwd * LookAhead);
        cam.Up = Vector3.UnitY;
    }
}
