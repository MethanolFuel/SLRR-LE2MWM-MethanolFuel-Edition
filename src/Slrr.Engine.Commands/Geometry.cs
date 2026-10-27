using System.Numerics;

namespace Slrr.Engine.Commands;

/// <summary>
/// Orientation as the scripts' <c>java.lang.Ypr</c>: yaw (about +Y), pitch (about X), roll (about Z), radians.
/// Convention GUESSED but consistent with the host's <c>Vector3.rotate(Ypr)</c> (Slrr.Game HostNatives: roll, then pitch,
/// then yaw; <c>x' = x cos y + z sin y</c>, <c>z' = -x sin y + z cos y</c>) and MEASURED against the scripts in one point:
/// TestTrack's acceleration round starts with <c>startdir(3.14)</c> and its checkpoints lie at increasing +Z, and
/// Track.handleMessage (CMD_LOOKBACK) puts the rear camera at (0, 1.5, -d) "in front", so a car with yaw 0 faces -Z.
/// </summary>
public readonly record struct Ypr(float Yaw, float Pitch, float Roll)
{
    public static Ypr Zero => default;

    /// <summary>Unit vector the object faces (local -Z rotated).</summary>
    public Vector3 Forward => Rotate(new Vector3(0, 0, -1));

    public Vector3 Rotate(Vector3 v)
    {
        float x = v.X, y = v.Y, z = v.Z;
        float cr = MathF.Cos(Roll), sr = MathF.Sin(Roll);
        (x, y) = ((x * cr) - (y * sr), (x * sr) + (y * cr));
        float cp = MathF.Cos(Pitch), sp = MathF.Sin(Pitch);
        (y, z) = ((y * cp) - (z * sp), (y * sp) + (z * cp));
        float cy = MathF.Cos(Yaw), sy = MathF.Sin(Yaw);
        (x, z) = ((x * cy) + (z * sy), (-x * sy) + (z * cy));
        return new Vector3(x, y, z);
    }

    /// <summary>Yaw/pitch that face along <paramref name="dir"/> (roll 0); inverse of <see cref="Forward"/>.</summary>
    public static Ypr FromDirection(Vector3 dir)
    {
        var h = MathF.Sqrt((dir.X * dir.X) + (dir.Z * dir.Z));
        if (h < 1e-6f && MathF.Abs(dir.Y) < 1e-6f)
        {
            return default;
        }

        // forward(yaw) = (-sin yaw, 0, -cos yaw)
        var yaw = MathF.Atan2(-dir.X, -dir.Z);
        var pitch = MathF.Atan2(dir.Y, h);
        return new Ypr(yaw, pitch, 0f);
    }

    /// <summary>Signed difference a - b wrapped to (-pi, pi].</summary>
    public static float AngleDiff(float a, float b)
    {
        var d = (a - b) % (2 * MathF.PI);
        if (d > MathF.PI)
        {
            d -= 2 * MathF.PI;
        }
        else if (d <= -MathF.PI)
        {
            d += 2 * MathF.PI;
        }

        return d;
    }
}

/// <summary>Position, orientation and velocity of an engine object (world frame, metres, radians, m/s).</summary>
public readonly record struct Pose(Vector3 Position, Ypr Orientation, Vector3 Velocity)
{
    public float Speed => Velocity.Length();

    /// <summary>Transforms an offset given in this object's frame to world coordinates.</summary>
    public Vector3 ToWorld(Vector3 local) => Position + Orientation.Rotate(local);
}
