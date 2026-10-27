using Slrr.Engine.Parts;
using Slrr.Engine.Resources;
using Slrr.Engine.SystemLib;
using Slrr.Engine.Ui;
using Slrr.Vm;
using Xunit;

namespace Slrr.Engine.Integration.Tests;

/// <summary>All engine native areas plugged into one boot. Passes vacuously without the game data ($SLRR_GAME_ROOT or the checkout).</summary>
public class FullBootTests
{
    private static string? FindRoot()
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

    [Fact]
    public void BootReachesSplashScreenWithoutStubsOrMissingNatives()
    {
        if (FindRoot() is not { } root)
        {
            return;
        }

        var saveRoot = Path.Combine(Path.GetTempPath(), "slrr-integration-" + Environment.ProcessId);
        Directory.CreateDirectory(saveRoot);
        try
        {
            INativeModule[] modules =
            [
                new EngineSystem(new GameSandbox(root, saveRoot)) { RegisterOpenLib = false, RegisterTimerNatives = false, FixThreadSleep = false },
                new PartsNatives(),
                new UiNatives(new UiEngine()),
            ];
            var report = Boot.Run(new BootOptions { Root = root, Stub = false, Frames = 30, Modules = modules });

            Assert.Null(report.StoppedBy);
            Assert.Empty(report.Missing);
            Assert.All(report.Steps, s => Assert.True(s.Ok, $"{s.Name}: {s.Detail}"));
            Assert.Equal("java.game.SplashScreen", report.ActiveSection);
            Assert.Equal(30, report.FramesRun);
        }
        finally
        {
            Directory.Delete(saveRoot, true);
        }
    }
}
