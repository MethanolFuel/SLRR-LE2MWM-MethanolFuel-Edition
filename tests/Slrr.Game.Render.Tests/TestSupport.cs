using Slrr.Game;
using Slrr.Game.Render;
using Slrr.Render.Gl;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace Slrr.Game.Render.Tests;

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

    public static string TempDir(string tag)
    {
        var d = Path.Combine(Path.GetTempPath(), $"slrr-game-render-test-{tag}-{Environment.ProcessId}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(d);
        return d;
    }
}

/// <summary>A host that boots to the main menu (video "unavailable" so the scripts build the 3D background) with an optional adapter.</summary>
internal sealed class MenuHost : IDisposable
{
    public MenuHost(string root, GameRenderAdapter? adapter = null)
    {
        Adapter = adapter;
        Host = new GameHost(new GameHostOptions
        {
            Root = root,
            Renderer = adapter,
            ExtraModules = adapter is null ? [] : [adapter.Hooks],
        });
        if (adapter is null)
        {
            Host.UiEngine.Video.VideoAvailable = false;
        }

        Assert.True(Host.Boot());
        Assert.True(Host.RunUntil(() => Host.ActiveSection?.Class.Name?.EndsWith("MainMenu", StringComparison.Ordinal) == true, 30));
        Host.RunFor(2f);
    }

    public GameHost Host { get; }

    public GameRenderAdapter? Adapter { get; }

    public void Dispose() => Host.Dispose();
}

/// <summary>Creates a GL context or null (tests then pass vacuously, like the corpus tests without data).</summary>
internal static class Gl
{
    public static GlHost? TryCreate() => GlHost.TryCreate(out _);
}
