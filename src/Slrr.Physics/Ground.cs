namespace Slrr.Physics;

/// <summary>Result of a ground ray query.</summary>
/// <param name="Point">World hit point.</param>
/// <param name="Normal">Unit surface normal (world).</param>
/// <param name="Distance">Distance from the ray origin along the ray.</param>
/// <param name="Friction">Surface friction multiplier. MEASURED role: the wheel step 0x456292 sets the tyre's
/// Pacejka[2] (peak friction) to <c>surface[+0x50] * tyre friction</c>; 1.0 = dry tarmac (value GUESSED).</param>
/// <param name="RollingResistance">Surface rolling resistance added to the tyre's (0x45629b: Pacejka[13] =
/// <c>surface[+0x54] + tyre rollres</c>; 0 for tarmac GUESSED).</param>
public readonly record struct GroundHit(Vec3 Point, Vec3 Normal, double Distance, double Friction = 1.0, double RollingResistance = 0.0);

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
