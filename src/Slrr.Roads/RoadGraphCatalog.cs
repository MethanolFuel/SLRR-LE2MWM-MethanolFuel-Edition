using System.Text;
using System.Text.RegularExpressions;
using Slrr.Formats.Rpak;
using Slrr.World;

namespace Slrr.Roads;

/// <summary>
/// Finds and caches the road graph of a map archive. The graph file is the <c>.trc</c> token of a map root's RSD
/// <c>params</c> line (MEASURED: city.rpk <c>params 0x00000010,0,0,0,0,0,0,maps\city\meshes\city.scm,maps\city\ai\city.trc</c>;
/// no other shipped archive names one, and maps/city/ai/city.trc is the only .trc in the repository).
/// </summary>
public sealed partial class RoadGraphCatalog(string? gameRoot)
{
    private readonly Dictionary<string, RoadGraph?> _byPath = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<RpakArchive, string?> _trcOf = [];

    public string? GameRoot { get; } = gameRoot;

    /// <summary>Load failures (path: message).</summary>
    public List<string> Errors { get; } = [];

    /// <summary>The <c>.trc</c> game path named by a root of <paramref name="archive"/>, or null.</summary>
    public static string? TrcPathOf(RpakArchive archive)
    {
        foreach (var e in archive.Entries.Where(e => e.TypeOfEntry == RpakArchive.ExtraEntryType))
        {
            ReadOnlyMemory<byte> data;
            try
            {
                data = archive.GetData(e);
            }
            catch (ArgumentException)
            {
                continue;
            }

            var text = Encoding.Latin1.GetString(data.Span);
            foreach (Match m in ParamsLine().Matches(text))
            {
                var tok = m.Groups[1].Value.Split(',').Select(s => s.Trim()).FirstOrDefault(s => s.EndsWith(".trc", StringComparison.OrdinalIgnoreCase));
                if (tok is not null)
                {
                    return tok;
                }
            }
        }

        return null;
    }

    public RoadGraph? ForArchive(RpakArchive archive)
    {
        if (!_trcOf.TryGetValue(archive, out var trc))
        {
            trc = TrcPathOf(archive);
            _trcOf[archive] = trc;
        }

        return trc is null ? null : ForGamePath(trc);
    }

    /// <summary>Graph of a game-relative (Windows-style) <c>.trc</c> path, resolved under <see cref="GameRoot"/>.</summary>
    public RoadGraph? ForGamePath(string gamePath)
    {
        if (GameRoot is null)
        {
            return null;
        }

        var path = new ObjectCatalog(GameRoot).ResolvePath(gamePath);
        return path is null ? Fail(gamePath, "not found") : ForFile(path);
    }

    public RoadGraph? ForFile(string path)
    {
        if (_byPath.TryGetValue(path, out var g))
        {
            return g;
        }

        try
        {
            g = new RoadGraph(TrcFile.Read(path), Path.GetFileName(path));
        }
        catch (Exception ex) when (ex is Slrr.Core.SlrrFormatException or IOException)
        {
            Errors.Add($"{path}: {ex.Message}");
            g = null;
        }

        _byPath[path] = g;
        return g;
    }

    private RoadGraph? Fail(string path, string why)
    {
        Errors.Add($"{path}: {why}");
        return null;
    }

    [GeneratedRegex(@"params\s+([^\r\n\0]*)")]
    private static partial Regex ParamsLine();
}
