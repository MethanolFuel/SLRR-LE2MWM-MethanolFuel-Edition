using System.Globalization;
using Slrr.Game;
using Slrr.Game.Render;
using Slrr.Game.Render.Gl;
using Slrr.Game.Render.Sequence;

namespace Slrr.Cli;

/// <summary>
/// <c>game-render</c>: the game host with the Slrr.Game.Render adapter (docs/race/game-render.md). Headless Linux:
/// <c>LIBGL_ALWAYS_SOFTWARE=1 xvfb-run -a slrr game-render ...</c>.
/// Integration (lead): ProjectReference from Slrr.Cli.csproj to src/Slrr.Game.Render and
/// <c>"game-render" =&gt; CmdGameRender.Run(args[1..], o, e)</c> in Commands.cs.
/// </summary>
public static class CmdGameRender
{
    public const string Usage = """
        game-render demo [--root dir] [--out dir] [--size WxH] [--cull off|all|opaque] [--map archive.rpk]
               scripted sequence: main menu (3D background car) -> new career -> garage -> a car on a map; PNG per stage
        game-render play [script.play | --smoke] [--root dir] [--out dir] [--capture-every n] [--size WxH] [--cull off|all|opaque] [--no-settle]
               run a play script (src/Slrr.Game) with the renderer attached; --capture-every n writes frame-NNNNNN.png every n frames;
               cars are drawn resting on the ground (no physics yet) unless --no-settle
        game-render cull [--root dir] [--out dir]
               back-face culling measurement on a car (opaque), foliage and glass
        game-render stream [--root dir] [--map archive.rpk] [--radius r] [--steps n]
               flies a camera over a big map (default maps\\city.rpk) with region streaming; reports reloads, chunks held, memory
        """;

    public static int Run(string[] args, TextWriter o, TextWriter e)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(o);
        ArgumentNullException.ThrowIfNull(e);
        if (args.Length == 0 || args[0] is not ("demo" or "play" or "cull" or "stream"))
        {
            e.WriteLine(Usage);
            return 2;
        }

        string? root = null, outDir = null, script = null, map = null;
        var smoke = false;
        var noSettle = false;
        var captureEvery = 0;
        var (w, h) = (800, 600);
        var cull = CullMode.Off;
        float radius = 200f;
        var stepCount = 60;
        for (var i = 1; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--root" when i + 1 < args.Length: root = args[++i]; break;
                case "--out" when i + 1 < args.Length: outDir = args[++i]; break;
                case "--map" when i + 1 < args.Length: map = args[++i]; break;
                case "--smoke": smoke = true; break;
                case "--no-settle": noSettle = true; break;
                case "--radius" when i + 1 < args.Length && float.TryParse(args[i + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out var rr) && rr > 20:
                    radius = rr;
                    i++;
                    break;
                case "--steps" when i + 1 < args.Length && int.TryParse(args[i + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var st) && st > 0:
                    stepCount = st;
                    i++;
                    break;
                case "--capture-every" when i + 1 < args.Length && int.TryParse(args[i + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) && n > 0:
                    captureEvery = n;
                    i++;
                    break;
                case "--size" when i + 1 < args.Length:
                    var p = args[++i].Split('x');
                    if (p.Length != 2 || !int.TryParse(p[0], out w) || !int.TryParse(p[1], out h) || w is < 64 or > 4096 || h is < 64 or > 4096)
                    {
                        e.WriteLine(Usage);
                        return 2;
                    }

                    break;
                case "--cull" when i + 1 < args.Length:
                    cull = args[++i] switch { "all" => CullMode.All, "opaque" => CullMode.OpaqueOnly, "off" => CullMode.Off, _ => (CullMode)(-1) };
                    if ((int)cull < 0)
                    {
                        e.WriteLine(Usage);
                        return 2;
                    }

                    break;
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
            return 1;
        }

        outDir ??= Path.Combine(Directory.GetCurrentDirectory(), "game-render-out");
        try
        {
            switch (args[0])
            {
                case "demo":
                    return DemoSequence.Run(new DemoOptions { Root = root, OutDir = outDir, Width = w, Height = h, Cull = cull, MapArchive = map ?? "maps\\Addon_Maps\\Drag_Track.rpk" }, o, e);
                case "cull":
                    return CullMeasurement.Run(root, outDir, w, h, o, e);
                case "stream":
                    return StreamMeasurement.Run(root, map ?? "maps\\city.rpk", new System.Numerics.Vector2(-900, -900), new System.Numerics.Vector2(700, 400), stepCount, radius, Math.Min(w, 480), Math.Min(h, 360), o);
                default:
                    return RunPlay(root, outDir, script, smoke, captureEvery, w, h, cull, noSettle, o, e);
            }
        }
        catch (InvalidOperationException ex) when (ex.Message.StartsWith("no OpenGL", StringComparison.Ordinal))
        {
            e.WriteLine("error: " + ex.Message);
            return 3;
        }
    }

    private static int RunPlay(string root, string outDir, string? script, bool smoke, int captureEvery, int w, int h, CullMode cull, bool noSettle, TextWriter o, TextWriter e)
    {
        string? text = null;
        if (smoke || script is null)
        {
            text = PlayCommand.SmokeScript();
        }
        else if (File.Exists(script))
        {
            text = File.ReadAllText(script);
        }
        else
        {
            e.WriteLine($"error: no such script: {script}");
            return 2;
        }

        using var adapter = new GameRenderAdapter(new GameRenderOptions
        {
            Root = root, Width = w, Height = h, CaptureDir = outDir, CaptureEvery = captureEvery, Cull = cull, DrawOnlyOnCapture = captureEvery > 0, SettleCarsOnGround = !noSettle,
        });
        using var host = new GameHost(new GameHostOptions { Root = root, Renderer = adapter, ExtraModules = [adapter.Hooks] });
        adapter.ConsumedCommands = () => host.Natives.PhysicsCommands;
        if (!host.Boot())
        {
            e.WriteLine("boot failed");
            return 1;
        }

        var runner = new PlayRunner(host);
        runner.Run(PlayScript.Parse(text));
        foreach (var line in runner.Output)
        {
            o.WriteLine("  | " + line);
        }

        foreach (var r in runner.Results.Where(r => !r.Ok))
        {
            o.WriteLine($"  FAILED line {r.Step.Line}: {r.Step.Text} :: {r.Detail} (t={r.Time:0.0}s, {r.Screen})");
        }

        var failed = runner.Results.Count(r => !r.Ok);
        o.WriteLine($"play: {runner.Results.Count - failed}/{runner.Results.Count} steps ok, {host.Frame} host frames, {adapter.Frames.Count} frames drawn");
        Report.Frames(adapter, o);
        foreach (var v in adapter.LastViews)
        {
            o.WriteLine("  last view: " + v);
        }

        if (Environment.GetEnvironmentVariable("SLRR_GAME_RENDER_DIAG") == "1")
        {
            foreach (var c in host.Resources.Commands.Where(c => c.TargetKind is "camera" or "player").TakeLast(40))
            {
                o.WriteLine($"  cmd {c.TargetId:X8} {c.TargetKind}: {c.Text}");
            }

            foreach (var n in host.Resources.Tree.Instances.Where(n => n.NativeKind is "car" or "camera" || (n.Type is 1 or 2 && n.NativeKind is not ("part" or "button") && (n.ParentId == 0x0002007A || n.Pos.Any(x => MathF.Abs(x) > 0.5f) && n.Script is null && (uint)n.ParentId >> 16 == 2))).Take(60).ToList())
            {
                o.WriteLine($"  node {n.Id:X8} {n.NativeKind} {n.Name} parent {n.ParentId:X8} pos {string.Join(",", n.Pos.Select(x => x.ToString("0.##", CultureInfo.InvariantCulture)))} ori {string.Join(",", n.Ori.Select(x => x.ToString("0.##", CultureInfo.InvariantCulture)))} params {n.Params}");
            }
        }

        return failed == 0 ? 0 : 1;
    }

    private static string? FindRoot(string start)
    {
        for (var d = new DirectoryInfo(start); d is not null; d = d.Parent)
        {
            if (File.Exists(Path.Combine(d.FullName, "system.rpk")) && File.Exists(Path.Combine(d.FullName, "sl.rpk")))
            {
                return d.FullName;
            }
        }

        return null;
    }
}
