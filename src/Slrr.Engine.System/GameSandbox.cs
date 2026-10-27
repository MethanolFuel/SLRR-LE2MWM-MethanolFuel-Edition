namespace Slrr.Engine.SystemLib;

/// <summary>A script handed the sandbox a path it may not use (traversal, absolute, drive letter, read-only area...).</summary>
public sealed class GamePathException(string path, string reason) : Exception($"path '{path}' rejected: {reason}")
{
    public string Path { get; } = path;
}

public enum ListKind
{
    FilesAndDirs = 0,
    FilesOnly = 1,
    DirsOnly = 2,
}

/// <summary>
/// File access for scripts, confined to two directories: the game root (read-only) and the save root (read/write).
/// Scripts use game-relative paths with either slash ("save/temp/CopyCar", "parts\\engines\\*.rpk", case-insensitive).
/// The virtual directory <c>save</c> is the save root: reads fall back to <c>&lt;gameRoot&gt;/save</c> when the file does
/// not exist in the save root (so the shipped <c>save/controls/active_control_set</c> loads), writes never do.
/// <c>screenshots</c> is the only other writable area (<see cref="ScreenshotRoot"/>). Everything else is read-only.
/// Rejected: absolute paths (leading slash, UNC), drive letters / any ':' , '..' segments, control characters,
/// <c>&lt; &gt; " |</c>, wildcards outside <see cref="List"/> patterns, and any path crossing a symbolic link.
/// </summary>
public sealed class GameSandbox
{
    public const string SaveMount = "save";
    public const string ScreenshotMount = "screenshots";

    private static readonly char[] Forbidden = ['<', '>', '"', '|', ':'];
    private static readonly char[] Wild = ['*', '?'];

    public GameSandbox(string gameRoot, string saveRoot, string? screenshotRoot = null)
    {
        GameRoot = Path.GetFullPath(gameRoot);
        SaveRoot = Path.GetFullPath(saveRoot);
        ScreenshotRoot = Path.GetFullPath(screenshotRoot ?? Path.Combine(SaveRoot, "_screenshots"));
        Directory.CreateDirectory(SaveRoot);
    }

    public string GameRoot { get; }

    public string SaveRoot { get; }

    public string ScreenshotRoot { get; }

    /// <summary>Splits and validates a script path into segments ("." dropped, empty segments collapsed).</summary>
    public static List<string> Parse(string? path, bool allowWildcards = false)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new GamePathException(path ?? "<null>", "empty");
        }

        var p = path.Trim().Trim('"');
        if (p.StartsWith('/') || p.StartsWith('\\'))
        {
            throw new GamePathException(path, "absolute path");
        }

        var segs = new List<string>();
        foreach (var s in p.Split('/', '\\'))
        {
            if (s.Length == 0 || s == ".")
            {
                continue;
            }

            if (s.Any(char.IsControl) || s.IndexOfAny(Forbidden) >= 0)
            {
                throw new GamePathException(path, "invalid character (drive letters and ':' are not allowed)");
            }

            if (!allowWildcards && s.IndexOfAny(Wild) >= 0)
            {
                throw new GamePathException(path, "wildcard");
            }

            if (s.All(c => c == '.') || s.EndsWith(' ') || s.EndsWith('.'))
            {
                throw new GamePathException(path, "'..' or dot/space-terminated segment");
            }

            segs.Add(s);
        }

        if (segs.Count == 0)
        {
            throw new GamePathException(path, "empty");
        }

        return segs;
    }

    /// <summary>Normalised script form of a path (backslashes, no leading ".\"), for logs and tests.</summary>
    public static string Normalize(string path) => string.Join('\\', Parse(path));

    /// <summary>Real path of an existing file or directory (reads), or null when it does not exist.</summary>
    public string? ResolveExisting(string path)
    {
        var segs = Parse(path);
        if (IsMount(segs[0], SaveMount))
        {
            return Walk(SaveRoot, segs.Skip(1), mustExist: true, path)
                ?? Walk(Path.Combine(GameRoot, SaveMount), segs.Skip(1), mustExist: true, path);
        }

        if (IsMount(segs[0], ScreenshotMount))
        {
            return Walk(ScreenshotRoot, segs.Skip(1), mustExist: true, path)
                ?? Walk(GameRoot, segs, mustExist: true, path);
        }

        return Walk(GameRoot, segs, mustExist: true, path);
    }

    /// <summary>Real path for creating/modifying; throws unless the path is under a writable mount.</summary>
    public string ResolveWritable(string path, bool allowMountRoot = false)
    {
        var segs = Parse(path);
        string baseDir;
        if (IsMount(segs[0], SaveMount))
        {
            baseDir = SaveRoot;
        }
        else if (IsMount(segs[0], ScreenshotMount))
        {
            baseDir = ScreenshotRoot;
        }
        else
        {
            throw new GamePathException(path, "read-only area (only save/ and screenshots/ are writable)");
        }

        if (segs.Count == 1 && !allowMountRoot)
        {
            throw new GamePathException(path, "mount root");
        }

        Directory.CreateDirectory(baseDir);
        return Walk(baseDir, segs.Skip(1), mustExist: false, path)!;
    }

    public bool IsWritable(string path)
    {
        try
        {
            ResolveWritable(path, allowMountRoot: true);
            return true;
        }
        catch (GamePathException)
        {
            return false;
        }
    }

    /// <summary>
    /// Directory listing for a <c>dir\pattern</c> path (wildcard only in the last segment; <c>*.*</c> matches every
    /// name). Names are bare, sorted case-insensitively, without "." and "..". Null if the directory is unreachable.
    /// </summary>
    public List<string>? List(string pattern, ListKind kind)
    {
        var segs = Parse(pattern, allowWildcards: true);
        for (var i = 0; i < segs.Count - 1; i++)
        {
            if (segs[i].IndexOfAny(Wild) >= 0)
            {
                throw new GamePathException(pattern, "wildcard in a directory segment");
            }
        }

        var mask = segs[^1];
        var dirs = segs.Count > 1 ? ExistingDirectories(segs.Take(segs.Count - 1).ToList(), pattern) : [GameRoot];
        if (dirs.Count == 0)
        {
            return null;
        }

        var result = new List<string>();
        foreach (var dir in dirs)
        {
            foreach (var e in new DirectoryInfo(dir).EnumerateFileSystemInfos())
            {
                var isDir = (e.Attributes & FileAttributes.Directory) != 0;
                if ((e.Attributes & FileAttributes.ReparsePoint) != 0 || (kind == ListKind.FilesOnly && isDir) || (kind == ListKind.DirsOnly && !isDir))
                {
                    continue;
                }

                if (Matches(mask, e.Name) && !result.Contains(e.Name, StringComparer.OrdinalIgnoreCase))
                {
                    result.Add(e.Name);
                }
            }
        }

        result.Sort(StringComparer.OrdinalIgnoreCase);
        return result;
    }

    /// <summary>Real directories behind a script directory: for <c>save</c> both the save root and the shipped save dir (save root first).</summary>
    private List<string> ExistingDirectories(List<string> segs, string original)
    {
        var list = new List<string>();
        void Add(string? p)
        {
            if (p is not null && Directory.Exists(p))
            {
                list.Add(p);
            }
        }

        if (IsMount(segs[0], SaveMount))
        {
            Add(Walk(SaveRoot, segs.Skip(1), mustExist: true, original));
            Add(Walk(Path.Combine(GameRoot, SaveMount), segs.Skip(1), mustExist: true, original));
        }
        else
        {
            Add(ResolveExisting(string.Join('\\', segs)));
        }

        return list;
    }

    /// <summary>Windows-style wildcard match (case-insensitive; <c>*</c>, <c>?</c>; <c>*.*</c> matches everything).</summary>
    public static bool Matches(string mask, string name) => mask is "*.*" or "*" || Match(mask, 0, name, 0);

    private static bool Match(string m, int mi, string s, int si)
    {
        while (mi < m.Length)
        {
            if (m[mi] == '*')
            {
                while (mi < m.Length && m[mi] == '*')
                {
                    mi++;
                }

                if (mi == m.Length)
                {
                    return true;
                }

                for (var k = si; k <= s.Length; k++)
                {
                    if (Match(m, mi, s, k))
                    {
                        return true;
                    }
                }

                return false;
            }

            if (si >= s.Length || (m[mi] != '?' && char.ToUpperInvariant(m[mi]) != char.ToUpperInvariant(s[si])))
            {
                return false;
            }

            mi++;
            si++;
        }

        return si == s.Length;
    }

    private static bool IsMount(string seg, string mount) => seg.Equals(mount, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Walks <paramref name="segments"/> below <paramref name="start"/> matching names case-insensitively and refusing
    /// symbolic links. With mustExist the result is null if a segment is missing, otherwise missing segments keep the
    /// script's spelling.
    /// </summary>
    private static string? Walk(string start, IEnumerable<string> segments, bool mustExist, string original)
    {
        var cur = start;
        var missing = false;
        foreach (var seg in segments)
        {
            if (!missing)
            {
                var found = Directory.Exists(cur) ? FindEntry(cur, seg) : null;
                if (found is not null)
                {
                    cur = found;
                    if ((File.GetAttributes(cur) & FileAttributes.ReparsePoint) != 0)
                    {
                        throw new GamePathException(original, "symbolic link");
                    }

                    continue;
                }

                if (mustExist)
                {
                    return null;
                }

                missing = true;
            }

            cur = Path.Combine(cur, seg);
        }

        return mustExist && !File.Exists(cur) && !Directory.Exists(cur) ? null : cur;
    }

    private static string? FindEntry(string dir, string name)
    {
        var exact = Path.Combine(dir, name);
        if (File.Exists(exact) || Directory.Exists(exact))
        {
            return exact;
        }

        foreach (var e in Directory.EnumerateFileSystemEntries(dir))
        {
            if (Path.GetFileName(e).Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                return e;
            }
        }

        return null;
    }
}
