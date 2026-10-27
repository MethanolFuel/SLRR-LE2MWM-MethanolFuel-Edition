namespace Slrr.Core;

/// <summary>
/// Resolves game-relative paths (Windows style, case-insensitive, e.g. <c>cars\racers\X.scx</c>)
/// against a game root directory on any OS. Built once from a directory scan.
/// </summary>
public sealed class GameFileSystem
{
    private readonly Dictionary<string, string> _files = new(StringComparer.OrdinalIgnoreCase);

    public GameFileSystem(string root)
    {
        Root = Path.GetFullPath(root);
        foreach (var file in Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(Root, file);
            if (rel.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Any(seg => seg.StartsWith('.')))
            {
                continue;
            }

            _files.TryAdd(Normalize(rel), file);
        }
    }

    public string Root { get; }

    public int Count => _files.Count;

    public static string Normalize(string gamePath)
    {
        var p = gamePath.Trim().Trim('"').Replace('/', '\\');
        while (p.StartsWith(".\\", StringComparison.Ordinal))
        {
            p = p[2..];
        }

        return p.TrimStart('\\');
    }

    public bool Exists(string gamePath) => _files.ContainsKey(Normalize(gamePath));

    public string? Resolve(string gamePath) => _files.TryGetValue(Normalize(gamePath), out var f) ? f : null;

    public IEnumerable<string> Enumerate(params string[] extensions) =>
        _files.Values.Where(f => extensions.Any(e => f.EndsWith(e, StringComparison.OrdinalIgnoreCase))).Order(StringComparer.Ordinal);
}
