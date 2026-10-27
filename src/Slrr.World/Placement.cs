using System.Numerics;

namespace Slrr.World;

/// <summary>
/// Placed-object orientation. The RSD record is <c>params x,y,z,a,b,c</c>; Y is up. The convention below was selected by
/// <c>world angles</c>, which transforms every object type's local bounds by each candidate and compares the resulting
/// world box with the 48-byte box stored in the object's instance record (see docs/phase4/world.md for the numbers).
/// </summary>
public static class Placement
{
    /// <summary>Local-to-world rotation for angles (a, b, c) in radians. MEASURED convention: see <see cref="Describe"/>.</summary>
    public static Matrix4x4 Rotation(float a, float b, float c) => Rotation(a, b, c, Convention.Default);

    public static Matrix4x4 Rotation(float a, float b, float c, Convention conv)
    {
        Matrix4x4 R(int axis, float ang, int sign)
        {
            var t = ang * sign;
            return axis switch
            {
                0 => Matrix4x4.CreateRotationX(t),
                1 => Matrix4x4.CreateRotationY(t),
                _ => Matrix4x4.CreateRotationZ(t),
            };
        }

        var ma = R(conv.AxisA, a, conv.SignA);
        var mb = R(conv.AxisB, b, conv.SignB);
        var mc = R(conv.AxisC, c, conv.SignC);

        // System.Numerics uses row vectors: v' = v * M, so the first matrix in the product is applied first.
        return conv.Order switch
        {
            0 => mc * mb * ma,
            1 => ma * mb * mc,
            2 => mb * ma * mc,
            3 => mc * ma * mb,
            4 => ma * mc * mb,
            _ => mb * mc * ma,
        };
    }

    /// <summary>Full local-to-world matrix (row-vector convention): rotate, then translate.</summary>
    public static Matrix4x4 Transform(Vector3 position, float a, float b, float c) =>
        Rotation(a, b, c) * Matrix4x4.CreateTranslation(position);

    public static string Describe(Convention c) =>
        $"a->{"XYZ"[c.AxisA]}{(c.SignA > 0 ? "+" : "-")} b->{"XYZ"[c.AxisB]}{(c.SignB > 0 ? "+" : "-")} c->{"XYZ"[c.AxisC]}{(c.SignC > 0 ? "+" : "-")} order {c.Order}";

    /// <summary>Axis (0=X,1=Y,2=Z) and sign for each of the three angles and the multiplication order variant 0..5.</summary>
    public readonly record struct Convention(int AxisA, int SignA, int AxisB, int SignB, int AxisC, int SignC, int Order)
    {
        /// <summary>Selected by <c>world angles</c> on city.rpk + ROC_track.rpk (MEASURED: 9708/9708 and 1791/1791 record boxes within 5 cm): a = yaw about +Y,
        /// b = pitch about X, c = roll about Z, applied roll(Z) * pitch(X) * yaw(Y) with row vectors, i.e. identical to Matrix4x4.CreateFromYawPitchRoll(a, b, c).</summary>
        public static Convention Default { get; } = new(1, 1, 0, 1, 2, 1, 0);
    }
}
