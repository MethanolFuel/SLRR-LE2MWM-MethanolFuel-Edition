using System.Globalization;
using System.Reflection;

namespace Slrr.Game;

/// <summary>
/// The <c>play</c> CLI: boots the game headlessly, runs a play script (see <see cref="PlayScript"/>) and prints a
/// transcript: every step with its result, the screens reached, missing natives (first-hit screen and script frame),
/// script thread errors, the host's gap-closing natives and shims. Exit code 0 when every step passed, 1 otherwise,
/// 2 on usage errors.
/// </summary>
public static class PlayCommand
{
    public const string Usage = """
        play [script.play | --smoke] [--root dir] [--save dir] [--strict] [--stop-on-fail] [--no-host-natives] [--log] [--frames n]
          Boots the game headlessly (system.rpk -> Config -> GameInit -> GameLogic) and runs scripted input.
          --smoke            run the built-in smoke path (main menu -> new career -> dealer -> catalog -> install -> main menu)
          --save dir         writable directory for save/... (default: a temporary directory, deleted afterwards)
          --strict           missing natives stop their script thread instead of returning 0/null
          --no-host-natives  measure the engine areas alone (no Slrr.Game gap natives, no script shims, no placeholder dyno)
          --frames n         without a script: run n frames after boot and report the screens
        """;

    public static int Run(string[] args, TextWriter o, TextWriter e)
    {
        string? script = null;
        string? root = null;
        string? save = null;
        var smoke = false;
        var strict = false;
        var stop = false;
        var noHost = false;
        var log = false;
        var frames = 150;
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--smoke": smoke = true; break;
                case "--strict": strict = true; break;
                case "--stop-on-fail": stop = true; break;
                case "--no-host-natives": noHost = true; break;
                case "--log": log = true; break;
                case "--root" when i + 1 < args.Length: root = args[++i]; break;
                case "--save" when i + 1 < args.Length: save = args[++i]; break;
                case "--frames" when i + 1 < args.Length && int.TryParse(args[i + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var n):
                    frames = n;
                    i++;
                    break;
                case "--help" or "-h":
                    o.WriteLine(Usage);
                    return 0;
                default:
                    if (args[i].StartsWith("--", StringComparison.Ordinal) || script is not null)
                    {
                        e.WriteLine(Usage);
                        return 2;
                    }

                    script = args[i];
                    break;
            }
        }

        root = FindRoot(root ?? Directory.GetCurrentDirectory());
        if (root is null)
        {
            e.WriteLine("error: no game root (a directory with system.rpk and sl.rpk) given or found");
            return 2;
        }

        string? text = null;
        if (smoke)
        {
            text = SmokeScript();
        }
        else if (script is not null)
        {
            if (!File.Exists(script))
            {
                e.WriteLine($"error: no such script: {script}");
                return 2;
            }

            text = File.ReadAllText(script);
        }

        using var host = new GameHost(new GameHostOptions
        {
            Root = root,
            SaveRoot = save,
            StubMissingNatives = !strict,
            HostNatives = !noHost,
            ScriptShims = !noHost,
            PlaceholderDyno = !noHost,
            ScriptLog = log ? o : null,
        });
        var started = DateTime.UtcNow;
        var booted = host.Boot();
        o.WriteLine($"boot: {(booted ? "ok" : "FAILED")} ({string.Join(", ", host.BootSteps.Select(s => $"{s.Name}{(s.Ok ? string.Empty : " FAIL")}"))})");
        PlayRunner? runner = null;
        if (booted)
        {
            if (text is null)
            {
                host.RunFrames(frames);
            }
            else
            {
                runner = new PlayRunner(host) { StopOnFailure = stop };
                runner.Run(PlayScript.Parse(text));
            }
        }

        Print(host, runner, o, DateTime.UtcNow - started);
        return !booted || runner?.Failed == true ? 1 : 0;
    }

    /// <summary>The built-in smoke script (embedded resource Scripts/smoke-garage.play).</summary>
    public static string SmokeScript()
    {
        using var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("Slrr.Game.Scripts.smoke-garage.play")
                      ?? throw new InvalidOperationException("embedded smoke script missing");
        using var r = new StreamReader(s);
        return r.ReadToEnd();
    }

    public static void Print(GameHost host, PlayRunner? runner, TextWriter o, TimeSpan wall)
    {
        if (runner is not null)
        {
            o.WriteLine("steps:");
            foreach (var r in runner.Results)
            {
                o.WriteLine(string.Create(CultureInfo.InvariantCulture,
                    $"  [{(r.Ok ? "ok" : "FAIL"),-4}] {r.Time,7:F2}s  L{r.Step.Line,-3} {r.Step.Text}{(r.Detail.Length == 0 ? string.Empty : "  -> " + r.Detail)}"));
            }

            foreach (var line in runner.Output)
            {
                o.WriteLine("  " + line);
            }

            o.WriteLine($"result: {runner.Results.Count(r => r.Ok)}/{runner.Results.Count} steps ok");
        }

        o.WriteLine("screens reached:");
        foreach (var v in host.Screens.Visits)
        {
            o.WriteLine("  " + v);
        }

        o.WriteLine($"natives missing ({host.Missing.Count}, stubbed as 0/null unless --strict):");
        foreach (var m in host.Missing)
        {
            o.WriteLine($"  {m.Calls,6}x {m.Key}  first on [{m.Screen}] {m.FirstTrace.FirstOrDefault()}");
        }

        o.WriteLine($"script thread errors ({host.ThreadErrors.Count}):");
        foreach (var t in host.ThreadErrors.Distinct().Take(30))
        {
            o.WriteLine("  " + t);
        }

        foreach (var t in host.Natives.ChassisUpdateErrors.Distinct().Take(5))
        {
            o.WriteLine("  (caught) " + t);
        }

        o.WriteLine($"host gap natives ({host.Natives.Gaps.Count}): {string.Join(", ", host.Natives.Gaps.Select(g => g.Key))}");
        o.WriteLine($"script shims: {host.ShimmedMethods.Count} empty object-returning methods return null");
        o.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"parts commands {host.Natives.PartCommands.Count} (car-tree fallback {host.Natives.TreeSearchInstalls}, reverse rule {host.Natives.ReverseRuleInstalls}, unlinked {host.Natives.FailedInstalls}); callbacks run {host.Natives.CallbacksRun}"));
        o.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"virtual time {host.Vm.Time:F2}s, {host.Frame} frames, {host.Vm.Executed:N0} instructions, {host.Resources.Tree.Instances.Count()} instances, wall {wall.TotalSeconds:F1}s"));
    }

    public static string? FindRoot(string start)
    {
        for (var d = new DirectoryInfo(Path.GetFullPath(start)); d is not null; d = d.Parent)
        {
            if (File.Exists(Path.Combine(d.FullName, "system.rpk")) && File.Exists(Path.Combine(d.FullName, "sl.rpk")))
            {
                return d.FullName;
            }
        }

        return null;
    }
}
