using System.Numerics;
using Slrr.Game.Render.Cars;
using Slrr.Game.Render.Scene;

namespace Slrr.Game.Render.Cameras;

/// <summary>Result of a rig: where the eye is and what it looks at.</summary>
public readonly record struct RigPose(Vector3 Eye, Vector3 Target, Vector3 Up);

/// <summary>
/// The three camera rigs of the game as pure functions of a car's world matrix. All parameters are GUESSED from the car cfgs
/// (<c>camera</c> lines = cockpit / hood / bumper views, MEASURED positions; <c>ext_camera 120 0,1.8,0 0.0,1.0,-2.0 0,0.0,0.0 5</c>) and the
/// camera verbs of the scripts (<c>dist 5.5 6.5</c>, <c>height 1.9</c>, <c>angle ...</c>); the original rigs smooth their motion
/// (<c>smooth</c>, <c>force</c>, <c>torque</c> verbs), these do not.
/// </summary>
public static class CameraRigs
{
    /// <summary>Free camera: a position and a yaw/pitch/roll.</summary>
    public static RigPose Free(Vector3 position, Vector3 ypr)
    {
        var (fwd, up) = HostIds.Basis(ypr);
        return new RigPose(position, position + fwd, up);
    }

    /// <summary>Looks from <paramref name="eye"/> at <paramref name="target"/>.</summary>
    public static RigPose LookAt(Vector3 eye, Vector3 target) => new(eye, target, Vector3.UnitY);

    /// <summary>
    /// Chase rig: <paramref name="distance"/> metres behind the body (heading only, the body's pitch and roll are ignored so the view
    /// stays level), <paramref name="height"/> above it, looking <paramref name="lookAhead"/> metres in front of it.
    /// </summary>
    public static RigPose Chase(Matrix4x4 body, float distance, float height, float lookAhead, float yawOffset = 0)
    {
        var pos = body.Translation;
        var f = Vector3.TransformNormal(-Vector3.UnitZ, body);
        var flat = new Vector3(f.X, 0, f.Z);
        flat = flat.LengthSquared() < 1e-6f ? -Vector3.UnitZ : Vector3.Normalize(flat);
        flat = Vector3.TransformNormal(flat, Matrix4x4.CreateRotationY(yawOffset));
        return new RigPose(pos - (flat * distance) + new Vector3(0, height, 0), pos + (flat * lookAhead) + new Vector3(0, height * 0.35f, 0), Vector3.UnitY);
    }

    /// <summary>Chase rig with the cfg's <c>ext_camera</c> (distance, height of the eye, look-ahead) when present.</summary>
    public static RigPose Chase(Matrix4x4 body, CfgExtCamera? ext)
    {
        var distance = ext?.Distance ?? 5f;
        var height = ext is null ? 1.8f : Math.Max(ext.A.Y, 0.8f);
        var ahead = ext is null ? 2f : Math.Abs(ext.B.Z);
        return Chase(body, distance, height, ahead);
    }

    /// <summary>Cockpit (or hood / bumper) rig: an eye fixed in the car's frame, from a cfg <c>camera</c> line.</summary>
    public static RigPose Cockpit(Matrix4x4 body, CfgCamera cam)
    {
        var (fwd, up) = HostIds.Basis(cam.Ypr);
        var eye = Vector3.Transform(cam.Position, body);
        return new RigPose(eye, eye + Vector3.TransformNormal(fwd, body), Vector3.TransformNormal(up, body));
    }

    /// <summary>Orbit around <paramref name="anchor"/> (garage / dealer view): azimuth 0 is behind the anchor looking towards -Z.</summary>
    public static RigPose Orbit(Vector3 anchor, float azimuth, float pitch, float distance)
    {
        var p = Math.Clamp(pitch, -1.4f, 1.4f);
        var dir = new Vector3(MathF.Sin(azimuth) * MathF.Cos(p), MathF.Sin(p), MathF.Cos(azimuth) * MathF.Cos(p));
        return new RigPose(anchor + (dir * Math.Max(distance, 0.1f)), anchor, Vector3.UnitY);
    }

    /// <summary>Vertical field of view in radians for a horizontal angle of view and an aspect ratio.</summary>
    public static float VerticalFov(float horizontalDegrees, float aspect)
    {
        var h = Math.Clamp(horizontalDegrees, 5f, 170f) * MathF.PI / 180f;
        return 2f * MathF.Atan(MathF.Tan(h * 0.5f) / Math.Max(aspect, 0.1f));
    }
}
