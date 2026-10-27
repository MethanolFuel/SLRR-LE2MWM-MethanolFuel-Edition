namespace Slrr.Game.Physics.Tests;

/// <summary>Game data root ($SLRR_GAME_ROOT or the repository checkout); corpus tests pass vacuously without it.</summary>
internal static class GameRoot
{
    private static readonly Lazy<string?> s_root = new(() =>
    {
        var env = Environment.GetEnvironmentVariable("SLRR_GAME_ROOT");
        if (!string.IsNullOrEmpty(env) && File.Exists(Path.Combine(env, "sl.rpk")))
        {
            return env;
        }

        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Slrr.sln")) && File.Exists(Path.Combine(dir.FullName, "sl.rpk")))
            {
                return dir.FullName;
            }
        }

        return null;
    });

    public static string? Root => s_root.Value;
}
