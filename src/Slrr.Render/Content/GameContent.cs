using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using Slrr.Core;
using Slrr.Formats.Cfg;
using Slrr.Formats.Graphics;
using Slrr.Formats.Rpak;
using Slrr.Formats.Scx;

namespace Slrr.Render.Content;

/// <summary>An RPK entry: archive (game-relative path, as the reference tables spell it) + local id.</summary>
public readonly record struct ResRef(string Archive, uint Id)
{
    public bool Equals(ResRef other) => Id == other.Id && string.Equals(Archive, other.Archive, StringComparison.OrdinalIgnoreCase);

    public override int GetHashCode() => HashCode.Combine(Id, StringComparer.OrdinalIgnoreCase.GetHashCode(Archive ?? ""));

    public override string ToString() => $"{Archive}:0x{Id:X8}";
}

/// <summary>
/// A type 14 "render" entry: the mesh it draws and its ordered texture list (index = SCX stage texture index).
/// Measured layout (docs/formats/graphics.md): <c>mesh 0x..</c>, optional <c>flags</c>, <c>texture 0x..</c> lines.
/// </summary>
public sealed record RenderDef(ResRef Ref, string Alias, string? MeshFile, string? MeshPath, IReadOnlyList<uint> TextureIds, IReadOnlyList<string?> TexturePaths, float Flags);

/// <summary>
/// Read-only view of the shipped content for the renderer: archives (loaded lazily), id resolution, entry text,
/// render entries, gametype cfgs, decoded textures and SCX meshes, all cached. Input is untrusted: every failure
/// is recorded in <see cref="Problems"/> and returns null instead of throwing.
/// </summary>
public sealed class GameContent
{
    private readonly Dictionary<string, RpakArchive?> _archives = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Dictionary<uint, RpakEntry>> _byId = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<ResRef, RenderDef?> _renders = [];
    private readonly Dictionary<string, RgbaImage?> _textures = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ScxFile?> _scx = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _problemSet = [];

    public GameContent(GameFileSystem files) => Files = files;

    public GameContent(string root) : this(new GameFileSystem(root))
    {
    }

    public GameFileSystem Files { get; }

    public List<string> Problems { get; } = [];

    public void Problem(string text)
    {
        if (_problemSet.Add(text))
        {
            Problems.Add(text);
        }
    }

    public static string Key(string gamePath) => GameFileSystem.Normalize(gamePath).ToLowerInvariant();

    private static readonly string[] TextureExtensions = [".ptx", ".dds", ".png", ".tga", ".jpg", ".bmp"];

    /// <summary>Texture files resolved through a different extension than the entry names (see <see cref="ResolveTexture"/>).</summary>
    public int ExtensionFallbacks { get; private set; }

    /// <summary>
    /// Resolves a texture <c>sourcefile</c>; when that exact file is not shipped, tries the same name with another texture
    /// extension. GUESSED engine behaviour, evidence: frontend.rpk 0x16 (Osd RID_GENERALBG, the main menu background) names
    /// <c>frontend\textures\GPS_FrameRaceSetup.png</c> and only <c>gps_frameracesetup.ptx</c> is shipped.
    /// </summary>
    public string? ResolveTexture(string? gamePath)
    {
        if (gamePath is null)
        {
            return null;
        }

        if (Files.Resolve(gamePath) is { } exact)
        {
            return exact;
        }

        foreach (var ext in TextureExtensions)
        {
            if (Files.Resolve(Path.ChangeExtension(gamePath, ext)) is { } alt)
            {
                ExtensionFallbacks++;
                return alt;
            }
        }

        return null;
    }

    public RpakArchive? Archive(string gamePath)
    {
        var key = Key(gamePath);
        if (_archives.TryGetValue(key, out var a))
        {
            return a;
        }

        a = null;
        var file = Files.Resolve(gamePath);
        if (file is null)
        {
            Problem($"archive {gamePath} not shipped");
        }
        else
        {
            try
            {
                a = RpakArchive.Read(file);
                var map = new Dictionary<uint, RpakEntry>();
                foreach (var e in a.Entries)
                {
                    map.TryAdd(e.Id, e);
                }

                _byId[key] = map;
            }
            catch (Exception ex) when (ex is SlrrFormatException or IOException)
            {
                Problem($"archive {gamePath}: {ex.Message}");
            }
        }

        _archives[key] = a;
        return a;
    }

    public RpakEntry? Entry(ResRef r) =>
        Archive(r.Archive) is not null && _byId.TryGetValue(Key(r.Archive), out var m) && m.TryGetValue(r.Id, out var e) ? e : null;

    /// <summary>
    /// Resolves an id written inside <paramref name="archive"/> (measured rule, see RenderIndex): high 16 bits 0 = this
    /// archive, otherwise the 1-based reference slot; the low 16 bits are the id there.
    /// </summary>
    public ResRef? Resolve(string archive, uint id)
    {
        var a = Archive(archive);
        if (a is null || id == 0)
        {
            return null;
        }

        var local = new ResRef(archive, id);
        if (Entry(local) is not null)
        {
            return local;
        }

        var slot = id >> 16;
        if (slot == 0)
        {
            return null;
        }

        var reference = a.References.FirstOrDefault(r => r.Index == slot);
        if (reference is null)
        {
            return null;
        }

        var target = new ResRef(GameFileSystem.Normalize(reference.Name), id & 0xFFFF);
        return Entry(target) is not null ? target : null;
    }

    public ReadOnlyMemory<byte> Data(ResRef r) =>
        Entry(r) is { } e && Archive(r.Archive) is { } a ? a.GetData(e) : ReadOnlyMemory<byte>.Empty;

    /// <summary>Entry text as key/value lines. Inline gametype cfgs (<c>ICFG</c> + u32 length + NUL-separated text) are unwrapped.</summary>
    public List<KeyValuePair<string, string>> Text(ResRef r) => RenderIndex.ParseText(Unwrap(Data(r).Span));

    /// <summary>Text bytes of an entry: strips the measured <c>ICFG</c> header and turns NULs into line breaks.</summary>
    public static byte[] Unwrap(ReadOnlySpan<byte> data)
    {
        if (data.Length >= 8 && data[..4].SequenceEqual("ICFG"u8))
        {
            var len = (int)Math.Min(BinaryPrimitives.ReadUInt32LittleEndian(data[4..]), (uint)(data.Length - 8));
            data = data.Slice(8, len);
        }

        var copy = data.ToArray();
        for (var i = 0; i < copy.Length; i++)
        {
            if (copy[i] == 0)
            {
                copy[i] = (byte)'\n';
            }
        }

        return copy;
    }

    public string? SourceFile(ResRef r) => Text(r).FirstOrDefault(p => p.Key == "sourcefile").Value is { } s ? GameFileSystem.Normalize(s) : null;

    public RenderDef? Render(ResRef r)
    {
        if (_renders.TryGetValue(r, out var hit))
        {
            return hit;
        }

        RenderDef? def = null;
        if (Entry(r) is { TypeOfEntry: 14 } e)
        {
            var kv = Text(r);
            string? meshFile = null, meshPath = null;
            if (kv.FirstOrDefault(p => p.Key == "mesh").Value is { } mtok && TryHex(mtok, out var mid) && Resolve(r.Archive, mid) is { } mref)
            {
                meshFile = SourceFile(mref);
                meshPath = meshFile is null ? null : Files.Resolve(meshFile);
                if (meshPath is null)
                {
                    Problem($"render {r}: mesh {meshFile ?? mref.ToString()} not shipped");
                }
            }

            var ids = new List<uint>();
            var paths = new List<string?>();
            foreach (var t in kv.Where(p => p.Key == "texture"))
            {
                var id = TryHex(t.Value, out var v) ? v : 0;
                ids.Add(id);
                string? path = null;
                if (id != 0 && Resolve(r.Archive, id) is { } tref && SourceFile(tref) is { } src)
                {
                    path = ResolveTexture(src);
                    if (path is null)
                    {
                        Problem($"render {r}: texture {src} not shipped");
                    }
                }

                paths.Add(path);
            }

            var flags = kv.FirstOrDefault(p => p.Key == "flags").Value is { } f && float.TryParse(f, NumberStyles.Float, CultureInfo.InvariantCulture, out var fv) ? fv : 0;
            def = new RenderDef(r, e.Alias, meshFile, meshPath, ids, paths, flags);
        }
        else if (Entry(r) is not { TypeOfEntry: 13 })
        {
            // Type 13 = light (cfg render lines also name lights: headlights "type spot ... direction 0 0 -1"); not a mesh.
            Problem($"render {r}: no type 14 entry");
        }

        _renders[r] = def;
        return def;
    }

    /// <summary>
    /// The cfg of a gametype (type 8) entry: <c>native part|car &lt;file.cfg&gt;</c> (car and part archives) or an inline
    /// <c>ICFG</c> block (objects.rpk).
    /// </summary>
    public CfgDocument? GametypeCfg(ResRef r)
    {
        var data = Data(r).Span;
        if (data.Length >= 4 && data[..4].SequenceEqual("ICFG"u8))
        {
            return CfgDocument.Parse(Unwrap(data), r.ToString());
        }

        var native = RenderIndex.ParseText(data).FirstOrDefault(p => p.Key == "native").Value;
        var cfg = native?.Split(' ', StringSplitOptions.RemoveEmptyEntries).LastOrDefault();
        if (cfg is null || !cfg.EndsWith(".cfg", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var path = Files.Resolve(cfg);
        if (path is null)
        {
            Problem($"gametype {r}: {cfg} not shipped");
            return null;
        }

        try
        {
            return CfgDocument.Load(path, cfg);
        }
        catch (IOException ex)
        {
            Problem($"{cfg}: {ex.Message}");
            return null;
        }
    }

    /// <summary>Decodes a texture file. <paramref name="keep"/> = false does not cache the pixels (the GPU keeps its copy).</summary>
    public RgbaImage? Texture(string? path, bool keep = true)
    {
        if (path is null)
        {
            return null;
        }

        if (_textures.TryGetValue(path, out var img))
        {
            return img;
        }

        try
        {
            img = TextureDecoder.DecodeFile(path);
        }
        catch (Exception ex) when (ex is SlrrFormatException or IOException or InvalidDataException)
        {
            Problem($"texture {path}: {ex.Message}");
            img = null;
        }

        if (keep || img is null)
        {
            _textures[path] = img;
        }

        return img;
    }

    public ScxFile? Scx(string? path)
    {
        if (path is null)
        {
            return null;
        }

        if (_scx.TryGetValue(path, out var scx))
        {
            return scx;
        }

        try
        {
            scx = ScxFile.Read(path);
        }
        catch (Exception ex) when (ex is SlrrFormatException or IOException)
        {
            Problem($"mesh {path}: {ex.Message}");
            scx = null;
        }

        _scx[path] = scx;
        return scx;
    }

    private static readonly char[] Separators = [' ', '\t'];

    public static bool TryHex(string token, out uint value)
    {
        var t = token.Split(Separators, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
        if (t.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            t = t[2..];
        }

        return uint.TryParse(t, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value);
    }

    public static float F(IReadOnlyList<string> args, int i, float fallback = 0) =>
        i < args.Count && float.TryParse(args[i], NumberStyles.Float, CultureInfo.InvariantCulture, out var v) && float.IsFinite(v) ? v : fallback;

    public static string Latin1(ReadOnlySpan<byte> b) => Encoding.Latin1.GetString(b);
}
