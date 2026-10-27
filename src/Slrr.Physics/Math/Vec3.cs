namespace Slrr.Physics;

/// <summary>Double-precision 3-vector (the simulation state is double; arithmetic order is fixed, so runs are deterministic).</summary>
public readonly record struct Vec3(double X, double Y, double Z)
{
    public static readonly Vec3 Zero = new(0, 0, 0);
    public static readonly Vec3 UnitX = new(1, 0, 0);
    public static readonly Vec3 UnitY = new(0, 1, 0);
    public static readonly Vec3 UnitZ = new(0, 0, 1);

    public static Vec3 operator +(Vec3 a, Vec3 b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);

    public static Vec3 operator -(Vec3 a, Vec3 b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);

    public static Vec3 operator -(Vec3 a) => new(-a.X, -a.Y, -a.Z);

    public static Vec3 operator *(Vec3 a, double s) => new(a.X * s, a.Y * s, a.Z * s);

    public static Vec3 operator *(double s, Vec3 a) => new(a.X * s, a.Y * s, a.Z * s);

    public static Vec3 operator /(Vec3 a, double s) => new(a.X / s, a.Y / s, a.Z / s);

    public double Dot(Vec3 b) => X * b.X + Y * b.Y + Z * b.Z;

    public Vec3 Cross(Vec3 b) => new(Y * b.Z - Z * b.Y, Z * b.X - X * b.Z, X * b.Y - Y * b.X);

    public double LengthSquared => X * X + Y * Y + Z * Z;

    public double Length => Math.Sqrt(LengthSquared);

    public Vec3 Normalized()
    {
        var l = Length;
        return l > 0 ? this / l : Zero;
    }

    public override string ToString() => FormattableString.Invariant($"({X:F3}, {Y:F3}, {Z:F3})");
}

/// <summary>Unit quaternion for the body orientation (body → world).</summary>
public readonly record struct Quat(double W, double X, double Y, double Z)
{
    public static readonly Quat Identity = new(1, 0, 0, 0);

    public static Quat operator *(Quat a, Quat b) => new(
        a.W * b.W - a.X * b.X - a.Y * b.Y - a.Z * b.Z,
        a.W * b.X + a.X * b.W + a.Y * b.Z - a.Z * b.Y,
        a.W * b.Y - a.X * b.Z + a.Y * b.W + a.Z * b.X,
        a.W * b.Z + a.X * b.Y - a.Y * b.X + a.Z * b.W);

    public static Quat FromAxisAngle(Vec3 axis, double angle)
    {
        var a = axis.Normalized();
        var s = Math.Sin(angle * 0.5);
        return new Quat(Math.Cos(angle * 0.5), a.X * s, a.Y * s, a.Z * s);
    }

    public Quat Normalized()
    {
        var l = Math.Sqrt(W * W + X * X + Y * Y + Z * Z);
        return new Quat(W / l, X / l, Y / l, Z / l);
    }

    public Quat Conjugate => new(W, -X, -Y, -Z);

    /// <summary>Rotates a body-frame vector into the world frame.</summary>
    public Vec3 Rotate(Vec3 v)
    {
        var u = new Vec3(X, Y, Z);
        var t = 2.0 * u.Cross(v);
        return v + W * t + u.Cross(t);
    }

    /// <summary>World → body.</summary>
    public Vec3 InverseRotate(Vec3 v) => Conjugate.Rotate(v);

    /// <summary>Integrates an angular velocity (world frame) over dt.</summary>
    public Quat Integrate(Vec3 omegaWorld, double dt)
    {
        var dq = new Quat(0, omegaWorld.X, omegaWorld.Y, omegaWorld.Z) * this;
        return new Quat(W + 0.5 * dt * dq.W, X + 0.5 * dt * dq.X, Y + 0.5 * dt * dq.Y, Z + 0.5 * dt * dq.Z).Normalized();
    }
}
