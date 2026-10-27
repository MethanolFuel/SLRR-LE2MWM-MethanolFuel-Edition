using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using Slrr.Race;

namespace Slrr.App;

/// <summary>Entry point shared by <c>Program</c> and the CLI command (<c>tools/Slrr.Cli/CmdApp.cs</c>).</summary>
public static class AppMain
{
    /// <summary>Exit codes: 0 ok, 1 game data missing / boot or script failure, 2 bad arguments, 3 no window or GL context.</summary>
    public static int Run(string[] args, TextWriter o, TextWriter e)
    {
        AppOptions options;
        try
        {
            options = AppOptions.Parse(args);
        }
        catch (ArgumentException ex)
        {
            e.WriteLine(ex.Message);
            e.WriteLine(AppOptions.Usage);
            return 2;
        }

        if (options.ShowHelp)
        {
            o.WriteLine(AppOptions.Usage);
            return 0;
        }

        var env = (Func<string, string?>)Environment.GetEnvironmentVariable;
        var root = AppPaths.FindGameRoot(options.Root, env, [Directory.GetCurrentDirectory(), AppContext.BaseDirectory], out var error, out var searched);
        if (root is null)
        {
            e.WriteLine("slrr-app: game data not found: " + error);
            e.WriteLine("  The C# engine runs the original game's data. Point it at your Street Legal Racing: Redline directory:");
            e.WriteLine("    slrr-app --root \"/path/to/SLRR\"        or set SLRR_GAME_ROOT=/path/to/SLRR");
            e.WriteLine("  Searched: " + string.Join(", ", searched.Take(12)) + (searched.Count > 12 ? ", ..." : ""));
            return 1;
        }

        var saveRoot = AppPaths.SaveRoot(options.SaveRoot, env);
        var userDir = AppPaths.UserDataDir(env);
        using var log = new AppLog(o, options.LogPath ?? Path.Combine(userDir, "logs", options.HeadlessFrames > 0 ? "slrr-app-headless.log" : "slrr-app.log")) { Verbose = options.Verbose };
        try
        {
            Directory.CreateDirectory(saveRoot);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            e.WriteLine($"slrr-app: cannot create the save directory {saveRoot}: {ex.Message} (use --save-root <dir>)");
            return 1;
        }

        log.Info($"slrr-app on {System.Runtime.InteropServices.RuntimeInformation.OSDescription} ({System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture}), .NET {Environment.Version}");
        log.Info($"game root {root}");
        log.Info($"save root {saveRoot}{(AppPaths.IsInside(saveRoot, root) ? " (inside the game directory: explicitly chosen)" : "")}; log {log.Path ?? "console only"}");
        try
        {
            return options.HeadlessFrames > 0 ? RunHeadless(options, root, saveRoot, log) : new WindowRunner(options, root, saveRoot, log).Run();
        }
        catch (FileNotFoundException ex)
        {
            log.Info($"game data incomplete: {ex.Message}");
            return 1;
        }
    }

    /// <summary>
    /// <c>--headless-frames n</c>: the same session (composition root, input router, hit testing, input script) without a window, GL or audio
    /// device, for CI. The router maps a virtual 800x600 window. Runs n host frames, or fewer when the input script ends.
    /// </summary>
    public static int RunHeadless(AppOptions options, string root, string saveRoot, AppLog log)
    {
        using var race = RaceHost.Create(root, new RaceHostOptions
        {
            Render = false,
            Audio = options.WavPath is not null,
            WavPath = options.WavPath,
            SaveRoot = saveRoot,
            FrameTime = 1f / options.HostHz,
        });
        using var session = new GameSession(race, log)
        {
            Mapper = new ViewportMapper(new Vector2(ViewportMapper.VirtualWidth, ViewportMapper.VirtualHeight),
                new Vector2(ViewportMapper.VirtualWidth, ViewportMapper.VirtualHeight), options.Aspect),
        };
        if (options.InputScript is { } spec)
        {
            session.Script = InputScript.Load(spec);
            log.Info($"input script {spec}: {session.Script.Steps.Count} steps");
        }

        if (!session.Boot())
        {
            return 1;
        }

        var steps = new FrameTimes();
        var events = new List<InputEvent>();
        var wall = Stopwatch.StartNew();
        for (var i = 0; i < options.HeadlessFrames; i++)
        {
            session.ProcessInput(events);
            foreach (var c in session.TakeCaptures())
            {
                log.Info($"capture {c}: headless, no image");
            }

            var t0 = wall.Elapsed.TotalMilliseconds;
            session.Step();
            steps.Add(wall.Elapsed.TotalMilliseconds - t0);
            if (session.Script is { } s && (s.Failed || s.QuitRequested || s.Done))
            {
                break;
            }
        }

        log.Info(steps.Summary("host step"));
        log.Info(string.Create(CultureInfo.InvariantCulture, $"headless: {session.HostFrames} host frames, game time {session.Time:F1} s, wall {wall.Elapsed.TotalSeconds:F1} s, clicks {session.Router.Clicks.Count}"));
        log.Info("state: " + session.Describe());
        if (session.Script is { } script)
        {
            log.Info(script.Failed ? $"input script FAILED: {script.Error}" : script.QuitRequested || script.Done ? "input script finished" : "input script did not finish within the frame budget");
            return script.Failed || !(script.QuitRequested || script.Done) ? 1 : 0;
        }

        return 0;
    }
}
