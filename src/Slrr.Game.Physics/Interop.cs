using System.Numerics;
using Slrr.Physics;
using Slrr.World;
using PhysGround = Slrr.Physics.IGroundQuery;
using WorldGroundQuery = Slrr.World.IGroundQuery;

namespace Slrr.Game.Physics;

/// <summary>Yaw/pitch/roll as the engine's <c>Ypr</c> (radians).</summary>
public readonly record struct YawPitchRoll(float Yaw, float Pitch, float Roll);

/// <summary>Conversions between the physics doubles (<see cref="Vec3"/>/<see cref="Quat"/>) and System.Numerics.</summary>
public static class Conv
{
    public static Vector3 ToVector3(this Vec3 v) => new((float)v.X, (float)v.Y, (float)v.Z);

    public static Vec3 ToVec3(this Vector3 v) => new(v.X, v.Y, v.Z);

    /// <summary>Both are the standard Hamilton body-to-world rotation (<c>q v q*</c>), so the components map 1:1.</summary>
    public static Quaternion ToQuaternion(this Quat q) => new((float)q.X, (float)q.Y, (float)q.Z, (float)q.W);

    public static Quat ToQuat(this Quaternion q) => new Quat(q.W, q.X, q.Y, q.Z).Normalized();

    /// <summary>
    /// Orientation from a <c>Ypr</c>. MEASURED for placed map objects (docs/phase4/world.md: <c>params x,y,z,a,b,c</c> is
    /// exactly <see cref="Matrix4x4.CreateFromYawPitchRoll"/>); GUESSED to hold for <c>GameRef.setMatrix</c> on cars too.
    /// </summary>
    public static Quat FromYpr(double yaw, double pitch, double roll) =>
        Quaternion.CreateFromYawPitchRoll((float)yaw, (float)pitch, (float)roll).ToQuat();

    /// <summary>Inverse of <see cref="FromYpr"/> (pitch clamped to ±90°). Computed in double from the rotated axes.</summary>
    public static YawPitchRoll ToYpr(Quat q)
    {
        // CreateFromYawPitchRoll = roll about Z, then pitch about X, then yaw about Y. The rotated +Z axis is
        // (sin y cos p, -sin p, cos y cos p); the Y components of the rotated X and Y axes are (sin r cos p, cos r cos p).
        var z = q.Rotate(Vec3.UnitZ);
        var x = q.Rotate(Vec3.UnitX);
        var y = q.Rotate(Vec3.UnitY);
        var pitch = Math.Asin(Math.Clamp(-z.Y, -1, 1));
        var yaw = Math.Atan2(z.X, z.Z);
        var roll = Math.Atan2(x.Y, y.Y);
        return new YawPitchRoll((float)yaw, (float)pitch, (float)roll);
    }
}

/// <summary>
/// How a map surface (<see cref="SurfaceInfo"/>) becomes the physics contact's friction / rolling resistance /
/// collision coefficient. MEASURED (exe .scm handlers 0x4627f0..0x462c5e, docs/phase4/physics.md): every material starts
/// with friction = ground_friction, rollres = 0.001, collcoeff = 0.2; a <c>friction f</c> line stores f * ground_friction,
/// <c>rollres r</c> and <c>collcoeff c</c> store the raw values. Slrr.World fills absent keys with its own neutral 1.0
/// (SurfaceInfo.Default*), so a value equal to that default is read as "absent" (GUESSED; no shipped material writes
/// rollres 1.0 or collcoeff 1.0, MEASURED on the five text .scm files).
/// </summary>
public sealed class SurfaceMapping
{
    /// <summary>Config.ground_friction (script default 1.0, MEASURED).</summary>
    public double GroundFriction { get; init; } = SurfaceDefaults.GroundFriction;

    /// <summary>Friction of placed-object shapes and unknown indices (= the exe's material default, ground_friction).</summary>
    public double DefaultFriction { get; init; } = SurfaceDefaults.GroundFriction;

    public GroundHit Apply(GroundHit hit, SurfaceTable? table, int surface)
    {
        var (f, rr, cc) = Map(table, surface);
        return hit with { Friction = f, RollingResistance = rr, CollisionCoefficient = cc, Surface = surface };
    }

    public (double Friction, double RollingResistance, double CollisionCoefficient) Map(SurfaceTable? table, int surface)
    {
        if (table is null || surface < 0 || surface >= table.Count)
        {
            return (DefaultFriction, SurfaceDefaults.RollingResistance, SurfaceDefaults.CollisionCoefficient);
        }

        var s = table.Get(surface);
        if (!s.HasExplicitProperties)
        {
            return (GroundFriction, SurfaceDefaults.RollingResistance, SurfaceDefaults.CollisionCoefficient);
        }

        var friction = s.Friction > 0 ? s.Friction * GroundFriction : GroundFriction;
        var rr = s.RollResistance == SurfaceInfo.DefaultRollResistance ? SurfaceDefaults.RollingResistance : Math.Max(0, s.RollResistance);
        var cc = s.CollisionCoeff == SurfaceInfo.DefaultCollisionCoeff ? SurfaceDefaults.CollisionCoefficient : s.CollisionCoeff;
        return (friction, rr, cc);
    }
}

/// <summary>
/// Slrr.Physics' ray-cast ground on top of Slrr.World's collision world. Downward rays (the suspension probes) use
/// the world's XZ ground grid twice (find the triangle under the ray start, intersect the ray with its plane, re-query
/// at the intersection) because that is several times faster than the BVH; other rays use the BVH. The suspension
/// sees only upward-facing triangles below the probe start (walls are the chassis box's business).
/// </summary>
public sealed class WorldGround : PhysGround
{
    private readonly WorldGroundQuery _world;
    private readonly SurfaceMapping _surfaces;
    private long _rays;
    private long _misses;

    public WorldGround(WorldGroundQuery world, SurfaceMapping? surfaces = null)
    {
        _world = world;
        _surfaces = surfaces ?? new SurfaceMapping();
    }

    public WorldGroundQuery World => _world;

    public long Rays => Interlocked.Read(ref _rays);

    public long Misses => Interlocked.Read(ref _misses);

    /// <summary>Last surface index seen by any probe (diagnostics).</summary>
    public int LastSurface { get; private set; } = -1;

    public bool Raycast(Vec3 origin, Vec3 direction, double maxDistance, out GroundHit hit)
    {
        Interlocked.Increment(ref _rays);
        if (direction.Y < -0.5 && GroundRay(origin, direction, maxDistance, out hit))
        {
            return true;
        }

        if (direction.Y >= -0.5 && _world.Raycast(origin.ToVector3(), direction.ToVector3(), (float)maxDistance, out var h))
        {
            hit = Make(h, h.Distance);
            return true;
        }

        Interlocked.Increment(ref _misses);
        hit = default;
        return false;
    }

    private bool GroundRay(Vec3 o, Vec3 d, double maxDistance, out GroundHit hit)
    {
        hit = default;
        var fromY = (float)o.Y;
        if (!_world.TryGetGround((float)o.X, (float)o.Z, fromY, out var h))
        {
            return false;
        }

        var t = PlaneT(o, d, h);
        if (double.IsNaN(t))
        {
            return false;
        }

        // Second query at the intersection: the ray may have left the first triangle's footprint.
        var p = o + d * t;
        if (_world.TryGetGround((float)p.X, (float)p.Z, fromY, out var h2) && h2.Triangle != h.Triangle)
        {
            var t2 = PlaneT(o, d, h2);
            if (!double.IsNaN(t2))
            {
                t = t2;
                h = h2;
            }
        }

        if (t < -0.05 || t > maxDistance)
        {
            return false;
        }

        hit = Make(h, t, o + d * t);
        return true;
    }

    private static double PlaneT(Vec3 o, Vec3 d, SurfaceHit h)
    {
        var n = h.Normal.ToVec3();
        var den = d.Dot(n);
        return Math.Abs(den) < 1e-6 ? double.NaN : (h.Position.ToVec3() - o).Dot(n) / den;
    }

    private GroundHit Make(SurfaceHit h, double t, Vec3? point = null)
    {
        LastSurface = h.Surface;
        var (f, rr, cc) = _surfaces.Map(_world.Surfaces, h.Surface);
        // Ground-grid triangles face up (Y > 0.05); BVH hits are oriented toward the ray origin by the world.
        return new GroundHit(point ?? h.Position.ToVec3(), h.Normal.ToVec3(), t, f, rr, cc, h.Surface);
    }
}
