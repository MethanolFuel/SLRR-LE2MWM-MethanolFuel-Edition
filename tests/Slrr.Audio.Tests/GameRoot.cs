namespace Slrr.Audio.Tests;

/// <summary>Locates the game content for corpus tests (same rule as Slrr.Formats.Tests): $SLRR_GAME_ROOT, else the repository.</summary>
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

internal static class Check
{
    public static void Near(double expected, double actual, double tolerance) =>
        Xunit.Assert.InRange(actual, expected - tolerance, expected + tolerance);
}
