using System.Text;
using Slrr.Core;
using Slrr.Formats.Rpak;

namespace Slrr.Formats.Graphics;

/// <summary>A render (type 14) RPAK entry: <c>mesh 0x..</c> plus an ordered <c>texture 0x..</c> list.</summary>
public sealed record RenderBinding(string Archive, uint RenderId, string Alias, uint MeshId, string MeshFile, IReadOnlyList<uint> TextureIds);

/// <summary>Result of resolving one texture id of a render entry.</summary>
/// <param name="Id">Id as written in the render entry.</param>
/// <param name="Archive">Archive that holds the texture entry (after following an external reference).</param>
/// <param name="Alias">Entry alias, e.g. <c>Nonus_chassis.ptx</c>.</param>
/// <param name="SourceFile">The <c>sourcefile</c> value of the entry (game-relative).</param>
/// <param name="Path">Absolute path of the file on disk, or null.</param>
/// <param name="Problem">Why resolution failed, or null.</param>
public sealed record TextureRef(uint Id, string? Archive, string? Alias, string? SourceFile, string? Path, string? Problem)
{
    public bool Resolved => Path is not null;
}

/// <summary>
/// Game-wide index from SCX files to the render entries that draw them and from texture ids to files.
/// Id resolution rule (measured): an id whose high 16 bits are zero is looked up in the same archive; otherwise the
/// high 16 bits select the archive's external reference slot (1-based <c>Index</c>) and the low 16 bits are the id
/// inside that archive (e.g. Nonus.rpk <c>texture 0x00020025</c> = cars.rpk id 0x25 <c>white</c> =
/// <c>cars\textures\default_white.png</c>). Entries are <c>key value</c> text lines; <c>;</c> starts a comment.
/// </summary>
public sealed class RenderIndex
{
    private readonly Dictionary<string, ArchiveInfo> _archives = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<RenderBinding>> _byMesh = new(StringComparer.OrdinalIgnoreCase);

    private RenderIndex(GameFileSystem fs) => Files = fs;

    public GameFileSystem Files { get; }

    public int ArchiveCount => _archives.Count;
    public int RenderCount { get; private set; }
    public List<string> Errors { get; } = [];

    public IEnumerable<string> MeshFiles => _byMesh.Keys;

    public static RenderIndex Build(GameFileSystem fs)
    {
        var index = new RenderIndex(fs);
        foreach (var file in fs.Enumerate(".rpk"))
        {
            var rel = GameFileSystem.Normalize(Path.GetRelativePath(fs.Root, file));
            try
            {
                var a = RpakArchive.Read(file);
                var info = new ArchiveInfo(rel, a);
                foreach (var e in a.Entries)
                {
                    info.ById.TryAdd(e.Id, e);
                }

                index._archives[rel] = info;
            }
            catch (SlrrFormatException ex)
            {
                index.Errors.Add($"{rel}: {ex.Message}");
            }
        }

        foreach (var info in index._archives.Values)
        {
            foreach (var e in info.Archive.Entries)
            {
                if (e.TypeOfEntry != 14)
                {
                    continue;
                }

                var kv = ParseText(info.Archive.GetData(e).Span);
                var meshTok = kv.FirstOrDefault(p => p.Key == "mesh").Value;
                if (meshTok is null || !TryHex(meshTok, out var meshId))
                {
                    continue;
                }

                var textures = kv.Where(p => p.Key == "texture").Select(p => TryHex(p.Value, out var t) ? t : uint.MaxValue).ToList();
                var meshEntry = index.Lookup(info, meshId, out _);
                if (meshEntry is null)
                {
                    continue;
                }

                var src = ParseText(meshEntry.Value.Archive.Archive.GetData(meshEntry.Value.Entry).Span).FirstOrDefault(p => p.Key == "sourcefile").Value;
                if (src is null)
                {
                    continue;
                }

                var norm = GameFileSystem.Normalize(src);
                var binding = new RenderBinding(info.Path, e.Id, e.Alias, meshId, norm, textures);
                if (!index._byMesh.TryGetValue(norm, out var list))
                {
                    index._byMesh[norm] = list = [];
                }

                list.Add(binding);
                index.RenderCount++;
            }
        }

        return index;
    }

    /// <summary>Render entries whose mesh entry has <c>sourcefile</c> equal to this game-relative SCX path.</summary>
    public IReadOnlyList<RenderBinding> FindRenders(string scxGamePath) =>
        _byMesh.TryGetValue(GameFileSystem.Normalize(scxGamePath), out var l) ? l : [];

    public TextureRef ResolveTexture(RenderBinding render, uint id)
    {
        if (!_archives.TryGetValue(render.Archive, out var info))
        {
            return new TextureRef(id, null, null, null, null, "archive not indexed");
        }

        var hit = Lookup(info, id, out var problem);
        if (hit is null)
        {
            return new TextureRef(id, null, null, null, null, problem);
        }

        var (arch, entry) = hit.Value;
        var src = ParseText(arch.Archive.GetData(entry).Span).FirstOrDefault(p => p.Key == "sourcefile").Value;
        if (src is null)
        {
            return new TextureRef(id, arch.Path, entry.Alias, null, null, "entry has no sourcefile");
        }

        var path = Files.Resolve(src);
        return new TextureRef(id, arch.Path, entry.Alias, GameFileSystem.Normalize(src), path, path is null ? "source file not shipped" : null);
    }

    private (ArchiveInfo Archive, RpakEntry Entry)? Lookup(ArchiveInfo info, uint id, out string? problem)
    {
        problem = null;
        if (info.ById.TryGetValue(id, out var local))
        {
            return (info, local);
        }

        var slot = id >> 16;
        if (slot == 0)
        {
            problem = $"id 0x{id:X8} not in {info.Path}";
            return null;
        }

        var reference = info.Archive.References.FirstOrDefault(r => r.Index == slot);
        if (reference is null)
        {
            problem = $"id 0x{id:X8}: {info.Path} has no reference slot {slot}";
            return null;
        }

        if (!_archives.TryGetValue(GameFileSystem.Normalize(reference.Name), out var other))
        {
            problem = $"id 0x{id:X8}: referenced archive {reference.Name} not found";
            return null;
        }

        if (other.ById.TryGetValue(id & 0xFFFF, out var ext))
        {
            return (other, ext);
        }

        problem = $"id 0x{id & 0xFFFF:X8} not in {other.Path}";
        return null;
    }

    public static List<KeyValuePair<string, string>> ParseText(ReadOnlySpan<byte> data)
    {
        var list = new List<KeyValuePair<string, string>>();
        foreach (var raw in Encoding.Latin1.GetString(data).Split('\n'))
        {
            var line = raw;
            var c = line.IndexOf(';', StringComparison.Ordinal);
            if (c >= 0)
            {
                line = line[..c];
            }

            line = line.Trim();
            if (line.Length == 0)
            {
                continue;
            }

            var sp = line.IndexOfAny([' ', '\t']);
            list.Add(sp < 0 ? new(line, "") : new(line[..sp], line[(sp + 1)..].Trim()));
        }

        return list;
    }

    private static bool TryHex(string token, out uint value)
    {
        var t = token.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
        if (t.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            t = t[2..];
        }

        return uint.TryParse(t, System.Globalization.NumberStyles.HexNumber, null, out value);
    }

    private sealed class ArchiveInfo(string path, RpakArchive archive)
    {
        public string Path { get; } = path;
        public RpakArchive Archive { get; } = archive;
        public Dictionary<uint, RpakEntry> ById { get; } = [];
    }
}
