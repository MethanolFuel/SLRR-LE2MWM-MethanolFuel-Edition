using System.Numerics;
using Slrr.Physics;
using Slrr.World;

namespace Slrr.Game.Physics;

/// <summary>Static triangle geometry the chassis boxes collide with (cross-area: Slrr.World's <see cref="CollisionWorld"/>).</summary>
public interface IStaticCollision
{
    /// <summary>Appends every triangle that may intersect the sphere (a superset is fine).</summary>
    void Candidates(Vector3 center, float radius, List<int> triangles);

    void Triangle(int tri, out Vector3 a, out Vector3 b, out Vector3 c);

    int SurfaceOf(int tri);

    int SourceOf(int tri);
}

/// <summary><see cref="IStaticCollision"/> over <see cref="CollisionWorld"/> (sphere overlap = BVH query + exact distance).</summary>
public sealed class CollisionWorldShapes(CollisionWorld world) : IStaticCollision
{
    private readonly List<Contact> _contacts = [];

    public CollisionWorld World { get; } = world;

    public void Candidates(Vector3 center, float radius, List<int> triangles)
    {
        _contacts.Clear();
        World.OverlapSphere(center, radius, _contacts);
        foreach (var c in _contacts)
        {
            triangles.Add(c.Triangle);
        }
    }

    public void Triangle(int tri, out Vector3 a, out Vector3 b, out Vector3 c)
    {
        a = World.Vertex(tri, 0);
        b = World.Vertex(tri, 1);
        c = World.Vertex(tri, 2);
    }

    public int SurfaceOf(int tri) => World.SurfaceOf(tri);

    public int SourceOf(int tri) => World.SourceOf(tri);
}

/// <summary>
/// Chassis box collisions: box vs static triangles (separating-axis test, 13 axes, minimum-penetration axis) and car vs
/// car (two spheres per car along its length). Response: position correction plus a sequential impulse with
/// restitution and Coulomb friction at the deepest box corners. MODEL GUESSED (the exe's collision response was not
/// traced); constants are plausible defaults.
/// </summary>
public sealed class CollisionSolver
{
    private readonly List<int> _tris = [];
    private readonly List<(Vec3 Point, Vec3 Normal, double Depth, int Tri)> _contacts = [];

    public double Restitution { get; set; } = 0.2;

    public double Friction { get; set; } = 0.5;

    /// <summary>Penetration allowed without correction [m].</summary>
    public double Slop { get; set; } = 0.005;

    /// <summary>Impacts whose speed change exceeds this raise an <see cref="ImpactEvent"/> [m/s].</summary>
    public double ImpactThreshold { get; set; } = 1.0;

    public int MaxContacts { get; set; } = 4;

    /// <summary>Box-triangle tests run (statistics).</summary>
    public long Tests { get; private set; }

    /// <summary>Turns impact energy into damage (null = no damage).</summary>
    public DamageModel? Damage { get; set; } = new();

    public void Static(SimCar car, IStaticCollision world, double time, List<ImpactEvent> impacts)
    {
        var bound = car.Box;
        _tris.Clear();
        world.Candidates(car.BoxCenterWorld.ToVector3(), (float)(bound.BoundingRadius + 0.05), _tris);
        if (_tris.Count == 0)
        {
            return;
        }

        var q = car.Orientation;
        Span<Vec3> axes = [q.Rotate(Vec3.UnitX), q.Rotate(Vec3.UnitY), q.Rotate(Vec3.UnitZ)];
        Span<Vec3> corners = stackalloc Vec3[8];
        _contacts.Clear();
        _tris.Sort();
        var origin = car.Origin;
        foreach (var box in car.Boxes)
        {
            var center = origin + q.Rotate(box.Center);
            car.BoxCorners(box, corners);
            var r2 = (box.BoundingRadius + 0.05) * (box.BoundingRadius + 0.05);
            var last = -1;
            foreach (var t in _tris)
            {
                if (t == last)
                {
                    continue;
                }

                last = t;
                world.Triangle(t, out var a, out var b, out var c);
                var av = a.ToVec3();
                var bv = b.ToVec3();
                var cv = c.ToVec3();
                if (car.Boxes.Count > 1 && !NearTriangle(center, r2, av, bv, cv))
                {
                    continue;
                }

                Tests++;
                if (BoxTriangle(center, axes, box.HalfExtents, av, bv, cv, out var n, out var depth))
                {
                    _contacts.Add((DeepestPoint(corners, n), n, depth, t));
                }
            }
        }

        if (_contacts.Count == 0)
        {
            return;
        }

        _contacts.Sort((x, y) =>
        {
            var k = y.Depth.CompareTo(x.Depth);
            return k != 0 ? k : x.Tri.CompareTo(y.Tri);
        });
        var count = Math.Min(MaxContacts, _contacts.Count);

        // Position: accumulate the push needed for each contact along its normal.
        var push = Vec3.Zero;
        for (var i = 0; i < count; i++)
        {
            var (_, n, d, _) = _contacts[i];
            var need = d - Slop - push.Dot(n);
            if (need > 0)
            {
                push += n * need;
            }
        }

        car.Position += push;

        // Velocity: two passes of sequential impulses.
        var maxDv = 0.0;
        var at = 0;
        double impulse = 0, closing = 0, energy = 0;
        for (var pass = 0; pass < 2; pass++)
        {
            for (var i = 0; i < count; i++)
            {
                var (p, n, _, _) = _contacts[i];
                var r = Resolve(car, null, p, n);
                impulse += r.Impulse;
                if (pass == 0)
                {
                    closing = Math.Max(closing, r.Closing);
                    energy = Math.Max(energy, r.Energy);
                }

                if (r.DeltaV > maxDv)
                {
                    maxDv = r.DeltaV;
                    at = i;
                }
            }
        }

        car.ImpactTotal += maxDv;
        if (maxDv >= ImpactThreshold)
        {
            var (p, n, _, tri) = _contacts[at];
            var damage = Damage?.Apply(car, p, energy) ?? 0;
            impacts.Add(new ImpactEvent(car.Id, time, p.ToVector3(), n.ToVector3(), (float)maxDv, 0, world.SurfaceOf(tri), world.SourceOf(tri),
                (float)impulse, (float)closing, (float)energy, (float)damage));
        }
    }

    private static bool NearTriangle(Vec3 center, double r2, Vec3 a, Vec3 b, Vec3 c)
    {
        var min = new Vec3(Math.Min(a.X, Math.Min(b.X, c.X)), Math.Min(a.Y, Math.Min(b.Y, c.Y)), Math.Min(a.Z, Math.Min(b.Z, c.Z)));
        var max = new Vec3(Math.Max(a.X, Math.Max(b.X, c.X)), Math.Max(a.Y, Math.Max(b.Y, c.Y)), Math.Max(a.Z, Math.Max(b.Z, c.Z)));
        var dx = Math.Max(0, Math.Max(min.X - center.X, center.X - max.X));
        var dy = Math.Max(0, Math.Max(min.Y - center.Y, center.Y - max.Y));
        var dz = Math.Max(0, Math.Max(min.Z - center.Z, center.Z - max.Z));
        return dx * dx + dy * dy + dz * dz <= r2;
    }

    /// <summary>Car vs car: two spheres per car (centres at ±(half length − half width) along the body), radius = half width.</summary>
    public void Cars(IReadOnlyList<SimCar> cars, double time, List<ImpactEvent> impacts)
    {
        Span<Vec3> sa = stackalloc Vec3[2];
        Span<Vec3> sb = stackalloc Vec3[2];
        for (var i = 0; i < cars.Count; i++)
        {
            var a = cars[i];
            for (var j = i + 1; j < cars.Count; j++)
            {
                var b = cars[j];
                if (!a.Simulated && !b.Simulated)
                {
                    continue;
                }

                var ra = a.Box.BoundingRadius;
                var rb = b.Box.BoundingRadius;
                var ca = a.BoxCenterWorld;
                var cb = b.BoxCenterWorld;
                if ((ca - cb).LengthSquared > (ra + rb) * (ra + rb))
                {
                    continue;
                }

                var rA = Spheres(a, sa);
                var rB = Spheres(b, sb);
                var best = 0.0;
                Vec3 bn = default, bp = default;
                foreach (var pa in sa)
                {
                    foreach (var pb in sb)
                    {
                        var d = pa - pb;
                        var len = d.Length;
                        var depth = rA + rB - len;
                        if (depth > best && len > 1e-9)
                        {
                            best = depth;
                            bn = d / len; // pushes a away from b
                            bp = pb + bn * rB;
                        }
                    }
                }

                if (best <= 0)
                {
                    continue;
                }

                var wa = a.Simulated ? 1.0 / a.Mass : 0;
                var wb = b.Simulated ? 1.0 / b.Mass : 0;
                var corr = Math.Max(0, best - Slop) / (wa + wb);
                a.Position += bn * (corr * wa);
                b.Position -= bn * (corr * wb);
                var r = Resolve(a, b, bp, bn);
                var dv = r.DeltaV;
                var dvb = b.Simulated ? r.Impulse / b.Mass : 0;
                a.ImpactTotal += dv;
                b.ImpactTotal += dvb;
                if (Math.Max(dv, dvb) >= ImpactThreshold)
                {
                    var da = a.Simulated ? Damage?.Apply(a, bp, r.Energy) ?? 0 : 0;
                    var db = b.Simulated ? Damage?.Apply(b, bp, r.Energy) ?? 0 : 0;
                    impacts.Add(new ImpactEvent(a.Id, time, bp.ToVector3(), bn.ToVector3(), (float)dv, b.Id, -1, 0,
                        (float)r.Impulse, (float)r.Closing, (float)r.Energy, (float)da));
                    impacts.Add(new ImpactEvent(b.Id, time, bp.ToVector3(), (-bn).ToVector3(), (float)dvb, a.Id, -1, 0,
                        (float)r.Impulse, (float)r.Closing, (float)r.Energy, (float)db));
                }
            }
        }
    }

    private static double Spheres(SimCar car, Span<Vec3> into)
    {
        var box = car.Box;
        var r = Math.Min(box.HalfExtents.X, box.HalfExtents.Z);
        var off = Math.Max(0, box.HalfExtents.Z - r);
        var c = car.BoxCenterWorld;
        var axis = car.Orientation.Rotate(Vec3.UnitZ);
        into[0] = c + axis * off;
        into[1] = c - axis * off;
        return r;
    }

    /// <summary>Result of one contact impulse.</summary>
    /// <param name="DeltaV">a's normal speed change [m/s] (0 when a is not simulated).</param>
    /// <param name="Impulse">Normal impulse magnitude [N s].</param>
    /// <param name="Closing">Closing speed along the normal before the impulse [m/s].</param>
    /// <param name="Energy">0.5 * m_eff * closing^2 [J], m_eff = 1 / (inverse effective mass along the normal).</param>
    private readonly record struct ContactResult(double DeltaV, double Impulse, double Closing, double Energy);

    /// <summary>Impulse between a (moving along +n) and b (or the static world).</summary>
    private ContactResult Resolve(SimCar a, SimCar? b, Vec3 p, Vec3 n)
    {
        var aw = a.Simulated;
        var bw = b is { Simulated: true };
        if (!aw && !bw)
        {
            return default;
        }

        var raP = p - a.Position;
        var rbP = b is null ? Vec3.Zero : p - b.Position;
        Vec3 Vel() => (aw ? a.Velocity + a.AngularVelocity.Cross(raP) : Vec3.Zero) - (bw ? b!.Velocity + b.AngularVelocity.Cross(rbP) : Vec3.Zero);
        double K(Vec3 dir) =>
            (aw ? 1 / a.Mass + dir.Dot(a.InverseInertiaWorld(raP.Cross(dir)).Cross(raP)) : 0)
            + (bw ? 1 / b!.Mass + dir.Dot(b.InverseInertiaWorld(rbP.Cross(dir)).Cross(rbP)) : 0);

        var vrel = Vel();
        var vn = vrel.Dot(n);
        if (vn >= 0)
        {
            return default;
        }

        var kn = K(n);
        var j = -(1 + Restitution) * vn / kn;
        var impulse = n * j;
        var vt = vrel - n * vn;
        var vtl = vt.Length;
        if (vtl > 1e-6)
        {
            var t = vt / vtl;
            var jt = Math.Min(vtl / K(t), Friction * j);
            impulse -= t * jt;
        }

        if (aw)
        {
            a.ApplyImpulse(p, impulse);
        }

        if (bw)
        {
            b!.ApplyImpulse(p, -impulse);
        }

        return new ContactResult(aw ? j / a.Mass : 0, j, -vn, 0.5 * vn * vn / kn);
    }

    private static Vec3 DeepestPoint(ReadOnlySpan<Vec3> corners, Vec3 n)
    {
        var min = double.MaxValue;
        foreach (var c in corners)
        {
            min = Math.Min(min, c.Dot(n));
        }

        var sum = Vec3.Zero;
        var k = 0;
        foreach (var c in corners)
        {
            if (c.Dot(n) <= min + 0.02)
            {
                sum += c;
                k++;
            }
        }

        return sum / k;
    }

    /// <summary>
    /// Separating-axis box/triangle test. On overlap returns the minimum-translation axis <paramref name="normal"/>
    /// (pointing from the triangle toward the box: moving the box by normal*depth separates them) and the depth.
    /// Face axes win ties (edge axes are penalised 5 %) so a box resting on a plane gets the plane normal.
    /// </summary>
    public static bool BoxTriangle(Vec3 center, ReadOnlySpan<Vec3> boxAxes, Vec3 half, Vec3 a, Vec3 b, Vec3 c, out Vec3 normal, out double depth)
    {
        var bestNormal = default(Vec3);
        var bestDepth = double.MaxValue;
        var ax0 = boxAxes[0];
        var ax1 = boxAxes[1];
        var ax2 = boxAxes[2];
        var v0 = a - center;
        var v1 = b - center;
        var v2 = c - center;
        var f0 = v1 - v0;
        var f1 = v2 - v1;
        var f2 = v0 - v2;
        var triN = f0.Cross(f1);
        var bestScore = double.MaxValue;

        bool Test(Vec3 axis, double bias)
        {
            var l2 = axis.LengthSquared;
            if (l2 < 1e-12)
            {
                return true;
            }

            var l = axis / Math.Sqrt(l2);
            double p0 = v0.Dot(l), p1 = v1.Dot(l), p2 = v2.Dot(l);
            var tmin = Math.Min(p0, Math.Min(p1, p2));
            var tmax = Math.Max(p0, Math.Max(p1, p2));
            var r = half.X * Math.Abs(ax0.Dot(l)) + half.Y * Math.Abs(ax1.Dot(l)) + half.Z * Math.Abs(ax2.Dot(l));
            if (tmin > r || tmax < -r)
            {
                return false;
            }

            var up = tmax + r;    // push the box along +l
            var down = r - tmin;  // push along -l
            var d = Math.Min(up, down);
            var score = d * bias;
            if (score < bestScore)
            {
                bestScore = score;
                bestDepth = d;
                bestNormal = up < down ? l : -l;
            }

            return true;
        }

        normal = default;
        depth = 0;
        if (!Test(triN, 1.0) || !Test(ax0, 1.0) || !Test(ax1, 1.0) || !Test(ax2, 1.0))
        {
            return false;
        }

        foreach (var ax in (ReadOnlySpan<Vec3>)[ax0, ax1, ax2])
        {
            if (!Test(ax.Cross(f0), 1.05) || !Test(ax.Cross(f1), 1.05) || !Test(ax.Cross(f2), 1.05))
            {
                return false;
            }
        }

        normal = bestNormal;
        depth = bestDepth;
        return bestDepth < double.MaxValue;
    }
}
