namespace Slrr.Formats.Tests;

/// <summary>
/// Locates the game content for corpus tests: $SLRR_GAME_ROOT, else the first ancestor of the test binary
/// that contains both Slrr.sln and sl.rpk (i.e. this repository). Corpus tests pass vacuously and print a
/// note when no content is found, so the suite still runs on machines without the game data.
/// </summary>
internal static class GameRoot
{
    public static string? Find()
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
    }
}
