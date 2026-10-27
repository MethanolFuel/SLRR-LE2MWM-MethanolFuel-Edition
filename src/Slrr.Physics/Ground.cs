namespace Slrr.Physics;

/// <summary>Result of a ground ray query.</summary>
/// <param name="Point">World hit point.</param>
/// <param name="Normal">Unit surface normal (world).</param>
/// <param name="Distance">Distance from the ray origin along the ray.</param>
/// <param name="Friction">Surface friction multiplier (exe surface +0x50). MEASURED: the wheel step 0x456292 sets the
/// tyre's Pacejka[2] (peak friction) to <c>surface[+0x50] * tyre friction</c>; the map loader stores
/// <c>.scm friction * ground_friction</c> there (0x462b00) and <c>ground_friction</c> for materials without a friction line
/// (0x46291b); Config.ground_friction = 1.0.</param>
/// <param name="RollingResistance">Surface rolling resistance in the exe's units (+0x54): the <c>.scm rollres</c> value raw
/// (0x462b80), 0.001 when absent (0x462928). The wheel step adds the tyre's rollres (0x45629b) and scales the sum by 0.01
/// times the smoothed load (0x456492..0x456498), see <see cref="Vehicle.RollingResistanceScale"/>.</param>
/// <param name="CollisionCoefficient">Surface <c>collcoeff</c> (+0x58, raw from the .scm, 0.2 when absent: MEASURED
/// 0x462bf0, 0x462914); carried for impacts, its role in the exe's collision response was not traced.</param>
/// <param name="Surface">Surface index of the map's material table (-1 = unknown / not a map surface).</param>
public readonly record struct GroundHit(Vec3 Point, Vec3 Normal, double Distance, double Friction = 1.0, double RollingResistance = 0.0,
    double CollisionCoefficient = SurfaceDefaults.CollisionCoefficient, int Surface = -1);

/// <summary>The exe's per-material defaults (MEASURED: the <c>materials</c> handler 0x4627f0 initialises every material
/// with friction = ground_friction, rollres = 0.001 (0x3a83126f), collcoeff = 0.2 (0x3e4ccccd)).</summary>
public static class SurfaceDefaults
{
    /// <summary>Config.ground_friction (script default 1.0; the exe reads the field at 0x46338b, values &lt;= 0 become 1.0).</summary>
    public const double GroundFriction = 1.0;

    public const double RollingResistance = 0.001;

    public const double CollisionCoefficient = 0.2;
}

/// <summary>
/// What the vehicle needs from the world: a ray cast against the drivable surface. The real implementation (map
/// geometry / collision meshes) belongs to another Phase-4 area; <see cref="FlatGround"/> is the test stand-in.
/// </summary>
public interface IGroundQuery
{
    /// <summary>Casts a ray from <paramref name="origin"/> along unit <paramref name="direction"/>; true with the
    /// nearest hit within <paramref name="maxDistance"/>.</summary>
    bool Raycast(Vec3 origin, Vec3 direction, double maxDistance, out GroundHit hit);
}

/// <summary>Infinite horizontal plane y = <see cref="Height"/> (world up is +Y, the game's convention).</summary>
public sealed class FlatGround(double height = 0.0, double friction = 1.0, double rollingResistance = 0.0) : IGroundQuery
{
    public double Height { get; } = height;

    public bool Raycast(Vec3 origin, Vec3 direction, double maxDistance, out GroundHit hit)
    {
        hit = default;
        if (direction.Y >= -1e-9)
        {
            return false;
        }

        var t = (Height - origin.Y) / direction.Y;
        if (t < -1.0 || t > maxDistance)
        {
            return false;
        }

        hit = new GroundHit(origin + direction * t, Vec3.UnitY, t, friction, rollingResistance);
        return true;
    }
}
