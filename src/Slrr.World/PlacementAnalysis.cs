using System.Numerics;

namespace Slrr.World;

/// <summary>Fits placed-object orientation conventions against the record boxes (see <c>world angles</c>).</summary>
public static class PlacementAnalysis
{
    public sealed record Fit(Placement.Convention Convention, int Objects, double MedianError, double P90Error, int Within5Cm, int Within50Cm);

    /// <summary>Every (axis, sign) assignment of the three angles and every multiplication order.</summary>
    public static IEnumerable<Placement.Convention> AllConventions()
    {
        int[][] perms = [[0, 1, 2], [0, 2, 1], [1, 0, 2], [1, 2, 0], [2, 0, 1], [2, 1, 0]];
        foreach (var p in perms)
        {
            for (var s = 0; s < 8; s++)
            {
                for (var order = 0; order < 6; order++)
                {
                    yield return new Placement.Convention(p[0], (s & 1) == 0 ? 1 : -1, p[1], (s & 2) == 0 ? 1 : -1, p[2], (s & 4) == 0 ? 1 : -1, order);
                }
            }
        }
    }

    /// <summary>World-space AABB of the type's local bounds under a convention; error = largest face distance to the record box.</summary>
    public static double BoxError(PlacedObject o, Placement.Convention conv)
    {
        var (mn, mx) = o.Type!.Bounds!.Value;
        var rot = Placement.Rotation(o.Angles.X, o.Angles.Y, o.Angles.Z, conv);
        var lo = new Vector3(float.MaxValue);
        var hi = new Vector3(float.MinValue);
        for (var i = 0; i < 8; i++)
        {
            var c = new Vector3((i & 1) == 0 ? mn.X : mx.X, (i & 2) == 0 ? mn.Y : mx.Y, (i & 4) == 0 ? mn.Z : mx.Z);
            var w = Vector3.Transform(c, rot) + o.Position;
            lo = Vector3.Min(lo, w);
            hi = Vector3.Max(hi, w);
        }

        var b = o.RecordBox;
        var rlo = new Vector3(b.CenterX - b.HalfX, b.CenterY - b.HalfY, b.CenterZ - b.HalfZ);
        var rhi = new Vector3(b.CenterX + b.HalfX, b.CenterY + b.HalfY, b.CenterZ + b.HalfZ);
        var d = Vector3.Max(Vector3.Abs(lo - rlo), Vector3.Abs(hi - rhi));
        return Math.Max(d.X, Math.Max(d.Y, d.Z));
    }

    public static List<Fit> Rank(IReadOnlyList<PlacedObject> objects, Func<PlacedObject, bool>? filter = null)
    {
        var sel = objects.Where(o => o.Type?.Bounds is not null && (filter?.Invoke(o) ?? true)).ToList();
        var fits = new List<Fit>();
        foreach (var conv in AllConventions())
        {
            var errs = sel.Select(o => BoxError(o, conv)).Order().ToArray();
            if (errs.Length == 0)
            {
                continue;
            }

            fits.Add(new Fit(conv, errs.Length, errs[errs.Length / 2], errs[(int)(errs.Length * 0.9)], errs.Count(e => e < 0.05), errs.Count(e => e < 0.5)));
        }

        return fits.OrderByDescending(f => f.Within50Cm).ThenBy(f => f.MedianError).ToList();
    }
}
