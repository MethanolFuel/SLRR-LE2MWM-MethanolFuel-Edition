using System.Numerics;

namespace Slrr.Viewer;

/// <summary>
/// Orbit camera around <see cref="Target"/>: yaw about +Y, pitch clamped short of the poles, distance clamped
/// to the mesh radius range. Pure math (System.Numerics, right-handed, row-vector convention); the matrices
/// are uploaded to GLSL untransposed and used as <c>uViewProj * vec4(p, 1)</c>.
/// </summary>
public sealed class OrbitCamera
{
    private const float MaxPitch = 1.55f; // ~89 degrees

    public Vector3 Target { get; set; }
    public float Yaw { get; set; }
    public float Pitch { get; set; }
    public float Distance { get; set; } = 5f;
    public float MinDistance { get; set; } = 0.01f;
    public float MaxDistance { get; set; } = 10_000f;
    public float FieldOfView { get; set; } = MathF.PI / 4f;

    /// <summary>Frames a bounding sphere: target at its centre, distance so it fits the vertical FOV.</summary>
    public static OrbitCamera Frame(Vector3 center, float radius)
    {
        radius = MathF.Max(radius, 1e-4f);
        var cam = new OrbitCamera { Target = center, Yaw = 0.6f, Pitch = 0.35f, MinDistance = radius * 0.05f, MaxDistance = radius * 50f };
        cam.Distance = radius / MathF.Sin(cam.FieldOfView / 2f) * 0.85f; // sphere of the box half-diagonal is loose; slightly tighter looks better
        return cam;
    }

    public Vector3 Eye =>
        Target + (Distance * new Vector3(MathF.Cos(Pitch) * MathF.Sin(Yaw), MathF.Sin(Pitch), MathF.Cos(Pitch) * MathF.Cos(Yaw)));

    public void Rotate(float deltaYaw, float deltaPitch)
    {
        Yaw += deltaYaw;
        Pitch = Math.Clamp(Pitch + deltaPitch, -MaxPitch, MaxPitch);
    }

    /// <summary>Multiplicative zoom; positive steps move closer.</summary>
    public void Zoom(float steps) => Distance = Math.Clamp(Distance * MathF.Pow(0.9f, steps), MinDistance, MaxDistance);

    public Matrix4x4 View => Matrix4x4.CreateLookAt(Eye, Target, Vector3.UnitY);

    public Matrix4x4 Projection(float aspect)
    {
        var near = MathF.Max(Distance * 0.01f, 1e-4f);
        var far = Distance * 10f + 1f;
        return Matrix4x4.CreatePerspectiveFieldOfView(FieldOfView, aspect > 0 ? aspect : 1f, near, far);
    }
}
