namespace Slrr.Engine.MapNatives.Tests;

/// <summary>Game data for corpus tests: $SLRR_GAME_ROOT or the checkout (tests pass vacuously without it).</summary>
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
