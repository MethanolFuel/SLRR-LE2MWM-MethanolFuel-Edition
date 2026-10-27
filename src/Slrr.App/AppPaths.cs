namespace Slrr.App;

/// <summary>Where the game data and the per-user files are.</summary>
public static class AppPaths
{
    public const string AppFolderName = "SLRR-CSharp";
    public const string RootEnv = "SLRR_GAME_ROOT";
    public const string SaveEnv = "SLRR_SAVE_ROOT";

    /// <summary>The two archives every boot needs (system.rpk: Config/Init scripts; sl.rpk: the engine's base content).</summary>
    public static readonly string[] Markers = ["system.rpk", "sl.rpk"];

    /// <summary>
    /// The game directory: <paramref name="explicitRoot"/> (--root), else <c>SLRR_GAME_ROOT</c>, else the first directory with both
    /// marker archives among the working directory, the executable's directory and their ancestors. Explicit choices are not
    /// second-guessed: when they lack the markers the result is null and <paramref name="error"/> says why.
    /// </summary>
    public static string? FindGameRoot(string? explicitRoot, Func<string, string?> env, IEnumerable<string> searchStarts, out string error, out List<string> searched)
    {
        searched = [];
        if (!string.IsNullOrWhiteSpace(explicitRoot))
        {
            var full = Path.GetFullPath(explicitRoot);
            searched.Add(full);
            return Check(full, "--root", out error);
        }

        if (env(RootEnv) is { Length: > 0 } fromEnv)
        {
            var full = Path.GetFullPath(fromEnv);
            searched.Add(full);
            return Check(full, RootEnv, out error);
        }

        foreach (var start in searchStarts)
        {
            for (var dir = new DirectoryInfo(Path.GetFullPath(start)); dir is not null; dir = dir.Parent)
            {
                if (searched.Contains(dir.FullName))
                {
                    continue;
                }

                searched.Add(dir.FullName);
                if (MissingMarkers(dir.FullName).Count == 0)
                {
                    error = "";
                    return dir.FullName;
                }
            }
        }

        error = "no directory with system.rpk and sl.rpk was found. Pass --root <game dir> or set SLRR_GAME_ROOT.";
        return null;
    }

    private static string? Check(string dir, string source, out string error)
    {
        if (!Directory.Exists(dir))
        {
            error = $"{source} points to '{dir}', which does not exist.";
            return null;
        }

        var missing = MissingMarkers(dir);
        if (missing.Count > 0)
        {
            error = $"{source} points to '{dir}', which has no {string.Join(" and no ", missing)}; it must be the Street Legal Racing: Redline game directory.";
            return null;
        }

        error = "";
        return dir;
    }

    /// <summary>Marker archives not present in <paramref name="dir"/>, matched case-insensitively (Linux file systems are case-sensitive).</summary>
    public static List<string> MissingMarkers(string dir)
    {
        if (!Directory.Exists(dir))
        {
            return [.. Markers];
        }

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var f in Directory.EnumerateFiles(dir))
            {
                names.Add(Path.GetFileName(f));
            }
        }
        catch (Exception e) when (e is UnauthorizedAccessException or IOException)
        {
            return [.. Markers];
        }

        return Markers.Where(m => !names.Contains(m)).ToList();
    }

    /// <summary>
    /// Per-user data folder: Windows <c>%APPDATA%\SLRR-CSharp</c>, macOS <c>~/Library/Application Support/SLRR-CSharp</c>,
    /// Linux <c>$XDG_DATA_HOME/SLRR-CSharp</c> (default <c>~/.local/share/SLRR-CSharp</c>). Never inside the game directory.
    /// </summary>
    public static string UserDataDir(Func<string, string?> env)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (OperatingSystem.IsWindows())
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            return Path.Combine(string.IsNullOrEmpty(appData) ? home : appData, AppFolderName);
        }

        if (OperatingSystem.IsMacOS())
        {
            return Path.Combine(home, "Library", "Application Support", AppFolderName);
        }

        var xdg = env("XDG_DATA_HOME");
        return Path.Combine(string.IsNullOrEmpty(xdg) ? Path.Combine(home, ".local", "share") : xdg, AppFolderName);
    }

    /// <summary>Save root: --save-root, else <c>SLRR_SAVE_ROOT</c>, else <c>&lt;user data&gt;/save</c>.</summary>
    public static string SaveRoot(string? explicitSave, Func<string, string?> env) =>
        Path.GetFullPath(!string.IsNullOrWhiteSpace(explicitSave) ? explicitSave : env(SaveEnv) is { Length: > 0 } e ? e : Path.Combine(UserDataDir(env), "save"));

    /// <summary>True when <paramref name="inner"/> is <paramref name="outer"/> or below it (case-insensitive: the safe answer on every OS).</summary>
    public static bool IsInside(string inner, string outer)
    {
        var a = Path.GetFullPath(inner).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var b = Path.GetFullPath(outer).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return a.StartsWith(b, StringComparison.OrdinalIgnoreCase);
    }
}
