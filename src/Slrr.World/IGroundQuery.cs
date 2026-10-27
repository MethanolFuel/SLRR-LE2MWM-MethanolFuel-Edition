using System.Numerics;

namespace Slrr.World;

/// <summary>Result of a ground / ray query against the static world.</summary>
/// <param name="Position">Hit point.</param>
/// <param name="Normal">Unit normal of the hit triangle (the stored PHYS normal), oriented to face the query origin.</param>
/// <param name="Surface">Surface index (PHYS <c>surface</c>, index into <see cref="SurfaceTable"/>; <see cref="SurfaceTable.ObjectSurfaceId"/> for placed-object shapes).</param>
/// <param name="Distance">Ray parameter t (world units, direction is unit length) or, for ground queries, <c>fromY - height</c>.</param>
/// <param name="Triangle">Index into <see cref="CollisionWorld"/>'s triangle arrays.</param>
/// <param name="Source">Id of the instance node (spatial node record id) or placed object that owns the triangle.</param>
public readonly record struct SurfaceHit(Vector3 Position, Vector3 Normal, int Surface, float Distance, int Triangle, int Source)
{
    public float Height => Position.Y;
}

/// <summary>
/// Ground lookup used by the physics engine: "drop a ray from above at (x, z)". Y is up (MEASURED: placed lamp bases sit
/// at y about 1..2.5 on ground triangles of the same y; PHYS vertex boxes are tall in Y).
/// Implementations must be thread safe for concurrent reads. This is the only world interface the vehicle code needs;
/// it deliberately has no dependency on <see cref="CollisionWorld"/>.
/// </summary>
public interface IGroundQuery
{
    /// <summary>Highest upward-facing surface at or below <paramref name="fromY"/> at horizontal position (x, z).
    /// Pass <see cref="float.PositiveInfinity"/> for the topmost surface (ignores bridges' undersides: only
    /// upward-facing triangles count). Returns false when there is no ground below.</summary>
    bool TryGetGround(float x, float z, float fromY, out SurfaceHit hit);

    /// <summary>Closest hit of a ray (any triangle, double sided) up to <paramref name="maxDistance"/>.
    /// <paramref name="direction"/> need not be normalized.</summary>
    bool Raycast(Vector3 origin, Vector3 direction, float maxDistance, out SurfaceHit hit);

    /// <summary>Surface table for interpreting <see cref="SurfaceHit.Surface"/> (grip, skid classes).</summary>
    SurfaceTable Surfaces { get; }
}

/// <summary>A penetration contact between a query shape and a triangle.</summary>
/// <param name="Point">Closest point on the triangle.</param>
/// <param name="Normal">Direction to push the shape out (from the triangle toward the shape's axis/centre).</param>
/// <param name="Depth">Penetration depth (radius minus distance), &gt; 0.</param>
public readonly record struct Contact(Vector3 Point, Vector3 Normal, float Depth, int Surface, int Triangle, int Source);
