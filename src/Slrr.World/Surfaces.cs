using System.Globalization;
using System.Text;

namespace Slrr.World;

/// <summary>Coarse surface class. Derived from the material NAME only (GUESSED); the per-material numbers in
/// <see cref="SurfaceInfo"/> are what the engine's own .scm table provides (MEASURED, but sparse).</summary>
public enum SurfaceClass
{
    Unknown,
    Asphalt,
    Concrete,
    Grass,
    Sand,
    Rock,
    Metal,
    Glass,
    Wall,
    Wood,
    Foliage,
    Object,
}

/// <summary>
/// One entry of a map's surface table. A <c>.scm</c> file (text, named by the map root's
/// <c>params ...,mesh.scm</c>) lists <c>material N name</c> lines, each optionally followed by property lines.
/// The index N is what PHYS <c>surface</c> and POLY <c>material</c> hold (MEASURED: city.scm has 108 materials and
/// PHYS surface / POLY material take exactly the values 0..107; city2.scm 75).
/// </summary>
public sealed class SurfaceInfo
{
    /// <summary>Defaults for materials without property lines. GUESSED: the exe reads <c>friction</c>, <c>rollres</c>,
    /// <c>collcoeff</c> (strings at 0x5xxxxx of StreetLegal_Redline.exe, next to <c>ground_friction</c>); the neutral
    /// values below are the multiplicative identity, not measured.</summary>
    public const float DefaultFriction = 1.0f;

    public const float DefaultRollResistance = 1.0f;

    public const float DefaultCollisionCoeff = 1.0f;

    public SurfaceInfo(int index, string name)
    {
        Index = index;
        Name = name;
        Class = Classify(name);
    }

    public int Index { get; }

    public string Name { get; }

    /// <summary>GUESSED from <see cref="Name"/>.</summary>
    public SurfaceClass Class { get; internal set; }

    /// <summary>MEASURED when <see cref="HasExplicitProperties"/> (value of the <c>friction</c> line, e.g. 0.6 sand,
    /// 0.8 road/grass in Dead_Island / Top_Gear); the tyre multiplies it with its own friction (GUESSED).</summary>
    public float Friction { get; internal set; } = DefaultFriction;

    /// <summary><c>rollres</c> line (MEASURED values 2.0 grass, 3.0 sand; unit GUESSED: multiplier of rolling resistance).</summary>
    public float RollResistance { get; internal set; } = DefaultRollResistance;

    /// <summary><c>collcoeff</c> line (MEASURED value 15.0 on Dead_Island sand; meaning GUESSED: body-collision/deformation coefficient).</summary>
    public float CollisionCoeff { get; internal set; } = DefaultCollisionCoeff;

    /// <summary><c>skid_SM</c>: skid-mark resource id (e.g. 0x00020047 = MW_Mod.rpk "Grass Skid", from the trailing comment). 0 = "Any".</summary>
    public uint SkidMarkResource { get; internal set; }

    /// <summary><c>skid_SMcolor</c>: ARGB colour of the skid mark (0xFF737373 grass/sand).</summary>
    public uint SkidMarkColor { get; internal set; }

    /// <summary><c>skid_PS</c>: particle system resource id for tyre smoke (0x00030104 = Particles.rpk "Sand smoke"). 0 = "Any".</summary>
    public uint SkidParticleResource { get; internal set; }

    /// <summary><c>skid_SFX</c>: skid sound resource id (key exists in the exe; no shipped value, so always 0).</summary>
    public uint SkidSoundResource { get; internal set; }

    /// <summary>True when the .scm gave this material at least one property line.</summary>
    public bool HasExplicitProperties { get; internal set; }

    /// <summary>GUESSED name heuristics (Hungarian/Polish/English words seen in the shipped material lists).</summary>
    public static SurfaceClass Classify(string name)
    {
        var n = name.ToLowerInvariant();
        bool Has(params string[] k) => k.Any(n.Contains);
        if (Has("asfalt", "rbegin", "rcenter", "autopalya", "cross", "road", "asphalt", "tarmac"))
        {
            return SurfaceClass.Asphalt;
        }

        if (Has("trawa", "trafa", "trava", "grass", "gbegin", "gcenter", "area", "fu"))
        {
            return SurfaceClass.Grass;
        }

        if (Has("sand", "piasek"))
        {
            return SurfaceClass.Sand;
        }

        if (Has("szikla", "rock", "kamien", "stone"))
        {
            return SurfaceClass.Rock;
        }

        if (Has("sidwalk", "sidewalk", "patka", "beton", "labazat", "concrete", "kerb", "curb"))
        {
            return SurfaceClass.Concrete;
        }

        if (Has("szalagkorlat", "drotkerites", "racs", "lanc", "kerites", "metal", "kosarpalya"))
        {
            return SurfaceClass.Metal;
        }

        if (Has("ablak", "kirakat", "glass", "szyb"))
        {
            return SurfaceClass.Glass;
        }

        if (Has("fal", "sciana", "palank", "wall", "teto", "csatorna"))
        {
            return SurfaceClass.Wall;
        }

        if (Has("ajto", "wood", "drewn"))
        {
            return SurfaceClass.Wood;
        }

        if (Has("tree", "alp", "siatex"))
        {
            return SurfaceClass.Foliage;
        }

        return SurfaceClass.Unknown;
    }

    public override string ToString() => string.Create(CultureInfo.InvariantCulture,
        $"{Index} '{Name}' {Class} friction {Friction} rollres {RollResistance} collcoeff {CollisionCoeff} skidSM 0x{SkidMarkResource:X8}");
}

/// <summary>The surface/material table of one map (see <see cref="SurfaceInfo"/>).</summary>
public sealed class SurfaceTable
{
    /// <summary>Surface id given to triangles that come from placed-object collision shapes (not a map material).</summary>
    public const int ObjectSurfaceId = 0xFFFF;

    private static readonly SurfaceInfo ObjectSurface = new(ObjectSurfaceId, "<object>")
    {
        Class = SurfaceClass.Object,
    };

    private readonly List<SurfaceInfo> _items;

    public SurfaceTable(IEnumerable<SurfaceInfo> items)
    {
        _items = items.ToList();
    }

    public int Count => _items.Count;

    public IReadOnlyList<SurfaceInfo> Items => _items;

    /// <summary>Table with <paramref name="count"/> anonymous default materials (when no .scm is available).</summary>
    public static SurfaceTable Anonymous(int count) =>
        new(Enumerable.Range(0, count).Select(i => new SurfaceInfo(i, "material " + i.ToString(CultureInfo.InvariantCulture))));

    /// <summary>Out-of-range ids (and <see cref="ObjectSurfaceId"/>) return a neutral entry, never throw.</summary>
    public SurfaceInfo Get(int id) =>
        id == ObjectSurfaceId ? ObjectSurface : (uint)id < (uint)_items.Count ? _items[id] : new SurfaceInfo(id, "<unknown " + id.ToString(CultureInfo.InvariantCulture) + ">");

    /// <summary>Parses the text of a .scm file. Lenient: unknown lines are ignored, comments (<c>//</c>) are cut.
    /// MEASURED on all 8 shipped text .scm files (city 108 materials, city2 75, Dead_Island 5, Drag_Track 18, Top_Gear 7;
    /// MP1.SCM and Caynon Ground.SCM are binary, not text, and are returned as null by <see cref="TryParse"/>).</summary>
    public static SurfaceTable? TryParse(ReadOnlySpan<byte> bytes)
    {
        var text = Encoding.Latin1.GetString(bytes);
        if (!text.StartsWith("materials", StringComparison.Ordinal))
        {
            return TryParseBinary(bytes);
        }

        var items = new List<SurfaceInfo>();
        SurfaceInfo? cur = null;
        foreach (var raw in text.Split('\n'))
        {
            var comment = raw.IndexOf("//", StringComparison.Ordinal);
            var line = (comment >= 0 ? raw[..comment] : raw).Trim();
            if (line.Length == 0)
            {
                continue;
            }

            var parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts[0] == "material" && parts.Length >= 2 && int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var idx))
            {
                while (items.Count < idx)
                {
                    items.Add(new SurfaceInfo(items.Count, string.Empty));
                }

                cur = new SurfaceInfo(idx, string.Join(' ', parts.Skip(2)));
                if (idx < items.Count)
                {
                    items[idx] = cur;
                }
                else
                {
                    items.Add(cur);
                }

                continue;
            }

            if (cur is null || parts.Length < 2)
            {
                continue;
            }

            var key = parts[0];
            var val = parts[1];
            switch (key)
            {
                case "friction":
                    cur.Friction = F(val, cur.Friction);
                    break;
                case "rollres":
                    cur.RollResistance = F(val, cur.RollResistance);
                    break;
                case "collcoeff":
                    cur.CollisionCoeff = F(val, cur.CollisionCoeff);
                    break;
                case "skid_SM":
                    cur.SkidMarkResource = H(val);
                    break;
                case "skid_SMcolor":
                    cur.SkidMarkColor = H(val);
                    break;
                case "skid_PS":
                    cur.SkidParticleResource = H(val);
                    break;
                case "skid_SFX":
                    cur.SkidSoundResource = H(val);
                    break;
                default:
                    continue;
            }

            cur.HasExplicitProperties = true;
        }

        return new SurfaceTable(items);
    }

    /// <summary>Binary .scm: <c>u32 count | u32 recordSize (36) | count x { u32 tag, char name[32] }</c>. MEASURED on MP1.SCM
    /// (10 records: <c>_p_beton _p_traFa _p_asfalt "Material #288" _p_start ...</c>) and Caynon Ground.SCM (1 record "Groung");
    /// <c>count * 36 + 8 == file length</c> in both. The tag's low byte varies (0x01 0x03 0x0f 0x1f 0x04 0x09 0x08 0x06 0x2f 0x0b), its
    /// upper bytes are 02 03 00; meaning unknown (not used). The <c>_p_</c> prefix of some names looks like "physics material" (GUESSED).
    /// No friction data exists in this form.</summary>
    private static SurfaceTable? TryParseBinary(ReadOnlySpan<byte> b)
    {
        if (b.Length < 8)
        {
            return null;
        }

        var count = (int)BitConverter.ToUInt32(b);
        var size = (int)BitConverter.ToUInt32(b[4..]);
        if (size != 36 || count < 0 || 8L + ((long)count * size) != b.Length)
        {
            return null;
        }

        var items = new List<SurfaceInfo>();
        for (var i = 0; i < count; i++)
        {
            var rec = b.Slice(8 + (i * size) + 4, 32);
            var end = rec.IndexOf((byte)0);
            items.Add(new SurfaceInfo(i, Encoding.Latin1.GetString(end < 0 ? rec : rec[..end])));
        }

        return new SurfaceTable(items);
    }

    private static float F(string s, float fallback) =>
        float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : fallback;

    private static uint H(string s) =>
        uint.TryParse(s.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? s[2..] : s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var v) ? v : 0;
}
