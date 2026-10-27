using System.Globalization;
using Slrr.Formats.Cfg;
using Slrr.Formats.Scx;

namespace Slrr.Physics;

/// <summary>An axis-aligned box of the chassis frame (centre and half extents in metres) and where it came from.</summary>
public readonly record struct CollisionBox(Vec3 Center, Vec3 HalfExtents, double Mass, string Source)
{
    public double BoundingRadius => HalfExtents.Length;
}

/// <summary>
/// The chassis collision shape as a set of boxes built from the chassis cfg's <c>body</c> lines
/// (<c>body px py pz ax ay az mass shape</c>). MEASURED data: a stock chassis lists its physics bodies as <c>_phys_*.scx</c>
/// meshes (DevilSport: frame 546 kg, cockpit 93.6 kg, roof 27.4 kg, six engine-bay pieces, load bay), each a small closed
/// mesh (10..20 vertices) stored in centimetres (x100, like the map objects' collision meshes, docs/phase4/world.md) and
/// centred on the body position (the frame's vertices span x +-78.1, y -25.6..22.6, z -154.7..154.9 around body
/// (0, -0.34, 0.54)). GUESSED: each mesh is used as its axis-aligned bounding box (the meshes are boxy hulls), rotated by
/// the body angles when they are not zero; bodies of mass &lt;= 0.01 kg (markers such as the 0.001 kg sphere) are skipped;
/// <c>sphere r</c> / <c>box hx hy hz</c> shapes become boxes of that size. Installed parts' own physics bodies (doors,
/// bumpers) are not added: their placement is not understood yet (see SpecOptions.PartsInCenterOfMass).
/// </summary>
public static class ChassisShape
{
    public const double MeshScale = 0.01;

    public static List<CollisionBox> Read(string cfgPath, ICollection<string>? notes = null)
    {
        var boxes = new List<CollisionBox>();
        if (!File.Exists(cfgPath))
        {
            return boxes;
        }

        var doc = CfgDocument.Load(cfgPath);
        foreach (var line in doc.Directives.Where(l => string.Equals(l.Keyword, "body", StringComparison.OrdinalIgnoreCase)))
        {
            var a = line.Args;
            if (a.Count < 8 || !TryF(a[0], out var px) || !TryF(a[1], out var py) || !TryF(a[2], out var pz)
                || !TryF(a[3], out var ax) || !TryF(a[4], out var ay) || !TryF(a[5], out var az) || !TryF(a[6], out var mass))
            {
                continue;
            }

            if (mass <= 0.01)
            {
                continue;
            }

            var shape = a[7];
            Vec3 min, max;
            if (string.Equals(shape, "sphere", StringComparison.OrdinalIgnoreCase) && a.Count >= 9 && TryF(a[8], out var r))
            {
                min = new Vec3(-r, -r, -r);
                max = new Vec3(r, r, r);
            }
            else if (string.Equals(shape, "box", StringComparison.OrdinalIgnoreCase) && a.Count >= 11
                && TryF(a[8], out var hx) && TryF(a[9], out var hy) && TryF(a[10], out var hz))
            {
                min = new Vec3(-hx, -hy, -hz);
                max = new Vec3(hx, hy, hz);
            }
            else if (shape.EndsWith(".scx", StringComparison.OrdinalIgnoreCase) && Resolve(cfgPath, shape) is { } mesh
                && MeshBounds(mesh, out min, out max))
            {
                min *= MeshScale;
                max *= MeshScale;
            }
            else
            {
                notes?.Add($"chassis body {shape}: no usable shape (skipped)");
                continue;
            }

            if (ax != 0 || ay != 0 || az != 0)
            {
                RotateBounds(ref min, ref max, ax, ay, az);
            }

            var center = new Vec3(px, py, pz) + (min + max) * 0.5;
            var half = (max - min) * 0.5;
            if (half.X > 0.005 && half.Y > 0.005 && half.Z > 0.005)
            {
                boxes.Add(new CollisionBox(center, half, mass, shape));
            }
        }

        return boxes;
    }

    /// <summary>Finds a game-relative path (Windows separators) by walking up from the cfg file's directory.</summary>
    public static string? Resolve(string cfgPath, string gamePath)
    {
        var rel = gamePath.Replace('\\', Path.DirectorySeparatorChar);
        for (var dir = Path.GetDirectoryName(Path.GetFullPath(cfgPath)); dir is not null; dir = Path.GetDirectoryName(dir))
        {
            var candidate = Path.Combine(dir, rel);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static bool MeshBounds(string path, out Vec3 min, out Vec3 max)
    {
        min = new Vec3(double.MaxValue, double.MaxValue, double.MaxValue);
        max = -min;
        ScxFile scx;
        try
        {
            scx = ScxFile.Read(path);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or ArgumentException or IndexOutOfRangeException)
        {
            return false;
        }

        var any = false;
        foreach (var mesh in scx.Meshes)
        {
            for (var i = 0; i < mesh.Vertices.Count; i++)
            {
                var p = mesh.Vertices.Position(i);
                if (!float.IsFinite(p.X) || !float.IsFinite(p.Y) || !float.IsFinite(p.Z))
                {
                    continue;
                }

                any = true;
                min = new Vec3(Math.Min(min.X, p.X), Math.Min(min.Y, p.Y), Math.Min(min.Z, p.Z));
                max = new Vec3(Math.Max(max.X, p.X), Math.Max(max.Y, p.Y), Math.Max(max.Z, p.Z));
            }
        }

        return any;
    }

    private static void RotateBounds(ref Vec3 min, ref Vec3 max, double yaw, double pitch, double roll)
    {
        var q = Quat.FromAxisAngle(Vec3.UnitY, yaw) * Quat.FromAxisAngle(Vec3.UnitX, pitch) * Quat.FromAxisAngle(Vec3.UnitZ, roll);
        var nmin = new Vec3(double.MaxValue, double.MaxValue, double.MaxValue);
        var nmax = -nmin;
        for (var k = 0; k < 8; k++)
        {
            var c = q.Rotate(new Vec3((k & 1) == 0 ? min.X : max.X, (k & 2) == 0 ? min.Y : max.Y, (k & 4) == 0 ? min.Z : max.Z));
            nmin = new Vec3(Math.Min(nmin.X, c.X), Math.Min(nmin.Y, c.Y), Math.Min(nmin.Z, c.Z));
            nmax = new Vec3(Math.Max(nmax.X, c.X), Math.Max(nmax.Y, c.Y), Math.Max(nmax.Z, c.Z));
        }

        min = nmin;
        max = nmax;
    }

    private static bool TryF(string s, out double v) => double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v);
}
