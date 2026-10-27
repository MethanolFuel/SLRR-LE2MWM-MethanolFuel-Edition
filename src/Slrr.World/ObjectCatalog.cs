using System.Globalization;
using System.Numerics;
using System.Text;
using Slrr.Formats.Rpak;

namespace Slrr.World;

public enum BodyShapeKind
{
    Box,
    Sphere,
    Mesh,
}

/// <summary>One <c>body px py pz ax ay az mass shape...</c> line of an object type's ICFG text.
/// Shape forms MEASURED in objects.rpk (450 body lines): <c>box hx hy hz</c> (339), <c>sphere r</c> (1) and an
/// <c>.scx</c> path (collision mesh, e.g. <c>objects\meshes\kuka_d\phys_01.scx</c>). Box extents are HALF sizes
/// (GUESSED, supported by the lamp pole: <c>body 0 4 0 ... box 0.1 4.0 0.1</c> against <c>object bounds ... 0 ... 8.02</c>;
/// <c>world verify</c> measures how many boxes lie inside the type's bounds).
/// <c>mass</c> 0 = static, &gt; 0 = movable (GUESSED; static boxes are all 0, the kuka_d mesh body has 50.0).</summary>
public sealed record ObjectBody(Vector3 Position, Vector3 Angles, float Mass, BodyShapeKind Kind, Vector3 Size, string? MeshPath);

/// <summary>A <c>render 0xID x y z rx ry rz</c> line: render resource and its local offset (renderer-side data).</summary>
public sealed record ObjectRenderRef(uint ResourceId, Vector3 Position, Vector3 Angles);

/// <summary>Collision / bounds data of one placed-object gametype, from its ICFG entry.</summary>
public sealed class ObjectType
{
    public required string Name { get; init; }

    public required uint EntryId { get; init; }

    /// <summary>Local-space bounds (<c>object bounds min.xyz max.xyz flag</c>), MEASURED present on the types that carry ICFG data.</summary>
    public (Vector3 Min, Vector3 Max)? Bounds { get; init; }

    public List<ObjectBody> Bodies { get; } = [];

    public List<ObjectRenderRef> Renders { get; } = [];

    /// <summary>Parses ICFG entry data: <c>"ICFG" u32 textLength, text</c> (MEASURED: length == entry size - 8;
    /// a NUL separates the words of the first line in some entries, treated as a space).</summary>
    public static ObjectType? Parse(RpakEntry entry, ReadOnlySpan<byte> data)
    {
        if (data.Length < 8 || data[0] != (byte)'I' || data[1] != (byte)'C' || data[2] != (byte)'F' || data[3] != (byte)'G')
        {
            return null;
        }

        var text = Encoding.Latin1.GetString(data[8..]).Replace('\0', ' ');
        var type = new ObjectType { Name = entry.Alias, EntryId = entry.Id, Bounds = ParseBounds(text) };
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            var cut = line.IndexOf(';', StringComparison.Ordinal);
            if (cut >= 0)
            {
                line = line[..cut];
            }

            var p = line.Split(new[] { ' ', '\t', '\r' }, StringSplitOptions.RemoveEmptyEntries);
            if (p.Length == 0)
            {
                continue;
            }

            if (p[0] == "body" && p.Length >= 9)
            {
                var pos = V(p, 1);
                var ang = V(p, 4);
                var mass = F(p[7]);
                if (p[8] == "box" && p.Length >= 12)
                {
                    type.Bodies.Add(new ObjectBody(pos, ang, mass, BodyShapeKind.Box, V(p, 9), null));
                }
                else if (p[8] == "sphere" && p.Length >= 10)
                {
                    type.Bodies.Add(new ObjectBody(pos, ang, mass, BodyShapeKind.Sphere, new Vector3(F(p[9])), null));
                }
                else if (p[8].EndsWith(".scx", StringComparison.OrdinalIgnoreCase))
                {
                    type.Bodies.Add(new ObjectBody(pos, ang, mass, BodyShapeKind.Mesh, Vector3.One, p[8]));
                }
            }
            else if (p[0] == "render" && p.Length >= 2 && p[1].StartsWith("0x", StringComparison.OrdinalIgnoreCase)
                && uint.TryParse(p[1].AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rid))
            {
                type.Renders.Add(new ObjectRenderRef(rid, p.Length >= 5 ? V(p, 2) : Vector3.Zero, p.Length >= 8 ? V(p, 5) : Vector3.Zero));
            }
        }

        return type;
    }

    private static (Vector3, Vector3)? ParseBounds(string text)
    {
        foreach (var raw in text.Split('\n'))
        {
            var p = raw.Split(new[] { ' ', '\t', '\r' }, StringSplitOptions.RemoveEmptyEntries);
            if (p.Length >= 8 && p[0] == "object" && p[1] == "bounds")
            {
                return (V(p, 2), V(p, 5));
            }
        }

        return null;
    }

    private static Vector3 V(string[] p, int i) => new(F(p[i]), F(p[i + 1]), F(p[i + 2]));

    private static float F(string s) => float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0;
}

/// <summary>Resolves a map archive's reference slots to archive files and caches their object types.</summary>
public sealed class ObjectCatalog
{
    private readonly string _root;
    private readonly Dictionary<string, RpakArchive?> _archives = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<(string, uint), ObjectType?> _types = [];

    public ObjectCatalog(string gameRoot)
    {
        _root = gameRoot;
    }

    public string Root => _root;

    /// <summary>Case-insensitive, segment-wise resolution of a Windows-style game-relative path. Null when absent.</summary>
    public string? ResolvePath(string gamePath)
    {
        var dir = _root;
        var parts = gamePath.Replace('/', '\\').Trim().Trim('"').Split('\\', StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < parts.Length; i++)
        {
            var next = Path.Combine(dir, parts[i]);
            var last = i == parts.Length - 1;
            if (last ? File.Exists(next) : Directory.Exists(next))
            {
                dir = next;
                continue;
            }

            if (!Directory.Exists(dir))
            {
                return null;
            }

            var hit = Directory.EnumerateFileSystemEntries(dir).FirstOrDefault(e => string.Equals(Path.GetFileName(e), parts[i], StringComparison.OrdinalIgnoreCase));
            if (hit is null)
            {
                return null;
            }

            dir = hit;
        }

        return File.Exists(dir) ? dir : null;
    }

    public RpakArchive? Archive(string name)
    {
        if (_archives.TryGetValue(name, out var a))
        {
            return a;
        }

        var path = ResolvePath(name);
        try
        {
            a = path is null ? null : RpakArchive.Read(path);
        }
        catch (Exception ex) when (ex is Slrr.Core.SlrrFormatException or IOException)
        {
            a = null;
        }

        _archives[name] = a;
        return a;
    }

    /// <summary>Object type for a RSD <c>gametype 0xSSSSIIII</c> of <paramref name="mapArchive"/>; null when it is not an
    /// ICFG entry (script-only types, map roots) or the archive is missing. No superId inheritance is applied (entries are used as stored).
    public ObjectType? Resolve(RpakArchive mapArchive, uint gameType)
    {
        var slot = (int)(gameType >> 16);
        var local = gameType & 0xFFFF;
        string name;
        RpakArchive? arch;
        if (slot == 0)
        {
            name = "<self>";
            arch = mapArchive;
        }
        else if (slot <= mapArchive.References.Count)
        {
            name = mapArchive.References[slot - 1].Name;
            arch = Archive(name);
        }
        else
        {
            return null;
        }

        if (arch is null)
        {
            return null;
        }

        var key = (name, local);
        if (_types.TryGetValue(key, out var t))
        {
            return t;
        }

        t = null;
        var entry = arch.Entries.FirstOrDefault(e => e.Id == local);
        if (entry is { Size: > 8 } && entry.TypeOfEntry != RpakArchive.ExtraEntryType)
        {
            t = ObjectType.Parse(entry, arch.GetData(entry).Span);
        }

        _types[key] = t;
        return t;
    }
}
