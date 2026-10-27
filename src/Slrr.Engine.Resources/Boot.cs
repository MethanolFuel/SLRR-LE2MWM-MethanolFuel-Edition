using System.Globalization;
using Slrr.Core;
using Slrr.Vm;

namespace Slrr.Engine.Resources;

/// <summary>Options of a headless boot.</summary>
public sealed class BootOptions
{
    public required string Root { get; init; }

    /// <summary>Make missing natives no-ops returning 0/null (and record them) instead of stopping at the first one.</summary>
    public bool Stub { get; init; }

    /// <summary>Engine frames to run after <c>GameInit</c> (each: advance the clock, run script threads, deliver events, notify <c>Frontend.render</c>).</summary>
    public int Frames { get; init; } = 60;

    public float FrameTime { get; init; } = 1f / 30f;

    public long InstructionBudget { get; init; } = 200_000_000;

    /// <summary>Native modules of other engine areas to add (render, io, sound, parts ...).</summary>
    public IReadOnlyList<INativeModule> Modules { get; init; } = [];

    public TextWriter? ScriptLog { get; init; }
}

/// <summary>One step of the original start-up sequence.</summary>
/// <param name="Name">Step name.</param>
/// <param name="Evidence">Where the step comes from (exe address or script).</param>
/// <param name="Ok">Whether the step completed.</param>
/// <param name="Detail">What happened.</param>
public sealed record BootStep(string Name, string Evidence, bool Ok, string Detail);

/// <summary>A native that was called but not registered.</summary>
public sealed class MissingNative
{
    public required NativeKey Key { get; init; }

    public int Calls { get; set; }

    /// <summary>Script frames at the first call (innermost first).</summary>
    public required IReadOnlyList<string> FirstTrace { get; init; }

    /// <summary>Boot step during which it was first hit.</summary>
    public required string Step { get; init; }
}

public sealed class BootReport
{
    public List<BootStep> Steps { get; } = [];

    /// <summary>Missing natives in first-hit order.</summary>
    public List<MissingNative> Missing { get; } = [];

    /// <summary>The error that stopped the boot (first missing native without --stub, script error, System.exit).</summary>
    public string? StoppedBy { get; set; }

    public IReadOnlyList<string> StopTrace { get; set; } = [];

    public int RegisteredNatives { get; set; }

    public int AreaNatives { get; set; }

    public int FramesRun { get; set; }

    public string? ActiveSection { get; set; }

    public List<string> ThreadErrors { get; } = [];

    public List<string> ScriptLog { get; } = [];

    public List<EngineMessage> EngineMessages { get; } = [];

    public List<(int Index, string Name, ArchiveState State)> Archives { get; } = [];

    public List<NativeCommand> Commands { get; } = [];

    public long Instructions { get; set; }

    public int Instances { get; set; }

    public bool Completed(string step) => Steps.Any(s => s.Name == step && s.Ok);
}

/// <summary>
/// Headless reproduction of the original start-up (docs/engine-api/entry-points.md section 1, measured from the exe):
/// 0x58c76a registers <c>system.rpk</c> (archive 0); VM bootstrap 0x427980 binds system.rpk:0x1/0x4/0x3/0xC/0xB refs,
/// sets WORLDTREEROOT on the root, registers the natives and creates gametype <c>system.rpk:0x47</c> (<c>Config</c>)
/// with the create routine 0x53a5e0 under the root; 0x58c7ae.. reads the Config statics (acceptBuild, version, FILD,
/// video_*, shadows, resource_refreshrate, Sound_*); game init 0x428960 reads <c>Config.init</c> (= sl.rpk:0x40) and
/// creates it as <c>"GameInit"</c> → <c>java.game.Init.&lt;init&gt;</c> → <c>new GameLogic()</c> → SplashScreen. The frame loop
/// (0x428db9) notifies <c>java.render.Frontend.render</c> every frame.
/// </summary>
public static class Boot
{
    public const int ExeBuild = 606;
    public const string ExeVersion = "v2.2.1";

    public static BootReport Run(BootOptions o)
    {
        var report = new BootReport();
        var game = new GameFileSystem(o.Root);
        var engine = new ResourceEngine(game);
        var registry = NativeRegistry.CreateDefault();
        registry.Add(new ResourceNatives(engine)).Add(new ArchiveNatives(engine));
        foreach (var m in o.Modules)
        {
            registry.Add(m);
        }

        var vm = new Slrr.Vm.Vm(new VmClassPath(game), registry) { InstructionBudget = o.InstructionBudget };
        engine.Attach(vm);
        var log = new LogWriter(report.ScriptLog, o.ScriptLog);
        vm.Out = log;
        report.RegisteredNatives = registry.Count;
        report.AreaNatives = ResourceNatives.Keys.Count + 1;

        var step = "start";
        registry.Fallback = key =>
        {
            var miss = report.Missing.Find(m => m.Key == key);
            if (miss is null)
            {
                var trace = vm.CurrentThread?.Frames.Reverse().Take(4).Select(f => f.Describe()).ToList() ?? [];
                miss = new MissingNative { Key = key, FirstTrace = trace, Step = step };
                report.Missing.Add(miss);
            }

            miss.Calls++;
            return o.Stub ? NativeRegistry.DefaultStub(key) : null;
        };

        try
        {
            step = "mount system.rpk";
            var sys = engine.Archives.Register("system.rpk");
            var ok = engine.Archives.EnsureLoaded(sys) is not null;
            report.Steps.Add(new BootStep(step, "exe 0x58c765", ok, ok ? $"archive index {sys}" : $"Data is missing. (distr. {ExeVersion} is needed)"));
            if (!ok)
            {
                return Finish(report, engine, vm);
            }

            step = "VM bootstrap";
            int[] fixedIds = [0x1, 0x4, 0x3, 0xC, 0xB];
            var resolved = fixedIds.Select(i => engine.Archives.ResolveLiteral("system.rpk", (uint)i)).ToList();
            if (engine.Tree.Get(engine.RootId) is { } root)
            {
                root.Flags |= 0x10;
            }

            report.Steps.Add(new BootStep(step, "exe 0x427980", resolved.All(x => x != 0),
                $"refs {string.Join(", ", resolved.Select(ResourceIds.Format))}; root {ResourceIds.Format(engine.RootId)} flags WORLDTREEROOT; {registry.Count} natives registered"));

            step = "create Config";
            var config = engine.Create(null, engine.RootId, engine.Archives.ResolveLiteral("system.rpk", 0x47), null, "Config", runScript: true);
            var configOk = config is not null && !CheckThreadErrors(vm, report);
            report.Steps.Add(new BootStep(step, "exe 0x427ad8..0x427b1b (create system.rpk:0x47 \"Config\")", configOk,
                config is null ? "no script instance" : $"{config.Class.Name} instance {ResourceIds.Format(ResourceEngine.IdOf(config))}"));
            if (!configOk)
            {
                return Finish(report, engine, vm);
            }

            step = "Config contract";
            var cfg = vm.LoadClass("java.util.Config");
            var accept = StaticInt(vm, cfg, "acceptBuild");
            var version = StaticString(vm, cfg, "version");
            var contractOk = accept <= ExeBuild && version == ExeVersion;
            var video = string.Join(" ", new[] { "video_x", "video_y", "video_depth", "video_windowed", "shadows", "resource_refreshrate", "Sound_3D_HW", "Sound_Mix_HW", "FILD" }
                .Select(f => $"{f}={StaticInt(vm, cfg, f)}"));
            report.Steps.Add(new BootStep(step, "exe 0x58c7ae..0x58c92c", contractOk,
                (accept > ExeBuild ? $"Incorrect executable build number. ({accept} or greater needed) " : string.Empty)
                + $"acceptBuild={accept} version=\"{version}\" {video}"));

            step = "GameInit";
            var init = StaticInt(vm, cfg, "init");
            var initObj = engine.Create(null, engine.RootId, init, null, "GameInit", runScript: true);
            var initErr = CheckThreadErrors(vm, report);
            report.Steps.Add(new BootStep(step, "exe 0x4289a0..0x4289e5 (Config.init = sl.rpk:0x40r)", initObj is not null && !initErr,
                $"Config.init = {ResourceIds.Format(init)} -> {initObj?.Class.Name ?? "null"}"));

            var logic = initObj is null ? null : GetField(initObj, "gameLogic");
            report.Steps.Add(new BootStep("new GameLogic", "class java.game.Init", logic is not null,
                logic is null ? "Init.gameLogic not set (constructor did not finish)" : $"{logic.Class.Name} instance {ResourceIds.Format(ResourceEngine.IdOf(logic))}"));
            report.ActiveSection = ActiveSection(vm);
            report.Steps.Add(new BootStep("SplashScreen", "class java.game.GameLogic (changeActiveSection(new SplashScreen(frontend.rpk:0x5)))",
                report.ActiveSection == "java.game.SplashScreen", report.ActiveSection is null ? "no active section" : $"GameLogic.actualState = {report.ActiveSection}"));

            if (initObj is not null && !initErr)
            {
                step = "frame loop";
                for (var f = 0; f < o.Frames; f++)
                {
                    NotifyFrontend(vm);
                    vm.RunFrame(o.FrameTime);
                    engine.Pump();
                    report.FramesRun++;
                }

                CheckThreadErrors(vm, report);
                report.ActiveSection = ActiveSection(vm);
                report.Steps.Add(new BootStep(step, "exe 0x428db9 (Frontend.render notifyAll per frame)", report.ThreadErrors.Count == 0,
                    $"{report.FramesRun} frames, {vm.Threads.Count} live threads, active section {report.ActiveSection ?? "none"}"));
            }
        }
        catch (VmExitException ex)
        {
            report.StoppedBy = $"System.exit during {step}: {ex.Message}";
            report.StopTrace = ex.ScriptTrace;
        }
        catch (VmException ex)
        {
            report.StoppedBy = $"{ex.Message} (during {step})";
            report.StopTrace = ex.ScriptTrace;
        }

        return Finish(report, engine, vm);
    }

    private static BootReport Finish(BootReport report, ResourceEngine engine, Slrr.Vm.Vm vm)
    {
        report.Instructions = vm.Executed;
        report.EngineMessages.AddRange(engine.Messages);
        report.Commands.AddRange(engine.Commands);
        report.Instances = engine.Tree.Instances.Count();
        foreach (var s in engine.Archives.Slots)
        {
            report.Archives.Add((s.Index, s.Name, s.State));
        }

        if (report.StoppedBy is null && report.Steps.FirstOrDefault(s => !s.Ok) is { } failed)
        {
            report.StoppedBy ??= report.ThreadErrors.FirstOrDefault() ?? $"step '{failed.Name}' failed";
        }

        return report;
    }

    /// <summary>Moves new thread errors into the report; true if there were any. The first one becomes the stop reason.</summary>
    private static bool CheckThreadErrors(Slrr.Vm.Vm vm, BootReport report)
    {
        if (vm.ThreadErrors.Count == 0)
        {
            return false;
        }

        foreach (var (t, ex) in vm.ThreadErrors)
        {
            report.ThreadErrors.Add($"{t.Name}: {ex.Message}");
            if (report.StoppedBy is null)
            {
                report.StoppedBy = $"{ex.Message} (thread {t.Name})";
                report.StopTrace = ex.ScriptTrace;
            }
        }

        vm.ThreadErrors.Clear();
        return true;
    }

    private static void NotifyFrontend(Slrr.Vm.Vm vm)
    {
        var fe = vm.LoadedClasses.FirstOrDefault(c => c.Name == "java.render.Frontend");
        if (fe is { InitState: ClassInitState.Initialized } && fe.FindField("render") is { IsStatic: true } f
            && StaticValue(fe, f).Ref is { } obj)
        {
            vm.Notify(obj, all: true);
        }
    }

    private static string? ActiveSection(Slrr.Vm.Vm vm)
    {
        var gl = vm.LoadedClasses.FirstOrDefault(c => c.Name == "java.game.GameLogic");
        return gl?.FindField("actualState") is { IsStatic: true } f ? StaticValue(gl, f).Ref?.Class.Name : null;
    }

    private static VmObject? GetField(VmObject o, string name) =>
        o.Class.FindField(name) is { IsStatic: false } f ? o.Fields[f.Slot].Ref : null;

    private static Value StaticValue(VmClass cls, VmField f) => f.Owner.StaticValues[f.Slot];

    public static int StaticInt(Slrr.Vm.Vm vm, VmClass cls, string name)
    {
        vm.EnsureInitialized(cls);
        return cls.FindField(name) is { IsStatic: true } f ? StaticValue(cls, f) is { Kind: VmKind.Int or VmKind.Float } v ? v.AsInt : 0 : 0;
    }

    public static string? StaticString(Slrr.Vm.Vm vm, VmClass cls, string name)
    {
        vm.EnsureInitialized(cls);
        return cls.FindField(name) is { IsStatic: true } f ? vm.GetString(StaticValue(cls, f).Ref) : null;
    }

    /// <summary>Plain-text report.</summary>
    public static void Print(BootReport r, TextWriter o, bool verbose)
    {
        o.WriteLine("boot sequence (original order):");
        foreach (var s in r.Steps)
        {
            o.WriteLine($"  [{(s.Ok ? "ok" : "FAIL")}] {s.Name,-18} {s.Detail}");
            if (verbose)
            {
                o.WriteLine($"         evidence: {s.Evidence}");
            }
        }

        o.WriteLine(r.StoppedBy is null ? "result: reached the end of the sequence" : $"stopped: {r.StoppedBy}");
        foreach (var t in r.StopTrace.Take(8))
        {
            o.WriteLine($"    at {t}");
        }

        o.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"natives registered: {r.RegisteredNatives} (resources area {r.AreaNatives}); instructions {r.Instructions:N0}; instances {r.Instances}; frames {r.FramesRun}"));
        o.WriteLine($"missing natives hit ({r.Missing.Count}, first-hit order):");
        foreach (var m in r.Missing.Take(verbose ? int.MaxValue : 40))
        {
            o.WriteLine($"  {m.Calls,6}x  {m.Key}   [{m.Step}] {m.FirstTrace.FirstOrDefault()}");
        }

        if (!verbose && r.Missing.Count > 40)
        {
            o.WriteLine($"  ... {r.Missing.Count - 40} more (--verbose)");
        }

        if (verbose)
        {
            o.WriteLine("archives (global index order):");
            foreach (var (i, n, st) in r.Archives)
            {
                o.WriteLine($"  {i,4}  {n}  {st}");
            }

            foreach (var m in r.EngineMessages)
            {
                o.WriteLine($"engine: {m.Text}");
            }

            foreach (var c in r.Commands)
            {
                o.WriteLine($"command -> {ResourceIds.Format(c.TargetId)} ({c.TargetKind ?? "?"}): {c.Text}");
            }

            foreach (var t in r.ThreadErrors)
            {
                o.WriteLine($"thread error: {t}");
            }

            foreach (var l in r.ScriptLog)
            {
                o.WriteLine($"log: {l}");
            }
        }
    }

    private sealed class LogWriter(List<string> lines, TextWriter? echo) : TextWriter
    {
        private readonly System.Text.StringBuilder _line = new();

        public override System.Text.Encoding Encoding => System.Text.Encoding.UTF8;

        public override void Write(char value)
        {
            if (value == '\n')
            {
                lines.Add(_line.ToString().TrimEnd('\r'));
                echo?.WriteLine(_line.ToString().TrimEnd('\r'));
                _line.Clear();
            }
            else
            {
                _line.Append(value);
            }
        }
    }
}

/// <summary>
/// <c>slrr boot</c>: starts the VM on a game root, runs the original start-up to <c>java.game.Init</c> / GameLogic /
/// SplashScreen and reports how far it gets and which natives are missing next.
/// </summary>
public static class BootCommand
{
    public const string Usage = """
        boot [gameRoot] [--stub] [--frames n] [--verbose] [--log] [--gametypes]
          headless start-up: system.rpk -> Config -> Config.init (sl.rpk:0x40, java.game.Init) -> GameLogic -> SplashScreen.
          Without --stub it stops at the first missing native; with --stub missing natives return 0/null and are listed.
          --gametypes: gametype registry survey (rpk script/native entries) instead of booting.
        """;

    public static int Run(string[] args, TextWriter o, TextWriter e)
    {
        string? root = null;
        var stub = false;
        var verbose = false;
        var echo = false;
        var gametypes = false;
        var frames = 60;
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--stub": stub = true; break;
                case "--verbose": verbose = true; break;
                case "--log": echo = true; break;
                case "--gametypes": gametypes = true; break;
                case "--frames" when i + 1 < args.Length && int.TryParse(args[i + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var n):
                    frames = n;
                    i++;
                    break;
                case "--help" or "-h":
                    o.WriteLine(Usage);
                    return 0;
                default:
                    if (args[i].StartsWith("--", StringComparison.Ordinal))
                    {
                        e.WriteLine(Usage);
                        return 2;
                    }

                    root = args[i];
                    break;
            }
        }

        root ??= FindRoot(Directory.GetCurrentDirectory());
        if (root is null || !File.Exists(Path.Combine(root, "system.rpk")))
        {
            e.WriteLine("error: no game root (a directory with system.rpk) given or found");
            return 1;
        }

        if (gametypes)
        {
            return Gametypes(root, o, verbose);
        }

        var report = Boot.Run(new BootOptions { Root = root, Stub = stub, Frames = frames, ScriptLog = echo ? o : null });
        Boot.Print(report, o, verbose);
        return report.StoppedBy is null ? 0 : 3;
    }

    private static int Gametypes(string root, TextWriter o, bool verbose)
    {
        var s = GametypeRegistry.Survey(new GameFileSystem(root), out _);
        o.WriteLine($"archives loaded {s.Archives}, missing (referenced, no file) {s.MissingArchives}");
        o.WriteLine($"text entries with script/native: {s.TextEntriesWithScriptOrNative}; native entries: {s.NativeEntries}");
        o.WriteLine("native kinds: " + string.Join(", ", s.NativeKinds.OrderByDescending(k => k.Value).Select(k => $"{k.Key} {k.Value}")));
        o.WriteLine("script binding: " + string.Join(", ", s.NativeBindings.OrderBy(k => k.Key).Select(k => $"{k.Key} {k.Value}")));
        foreach (var (k, v) in s.Scripts.OrderByDescending(k => k.Value).Take(verbose ? int.MaxValue : 15))
        {
            o.WriteLine($"  {v,5} {k}");
        }

        if (verbose)
        {
            foreach (var g in s.Unbound)
            {
                o.WriteLine($"  no script: {ResourceIds.Format(g.Id)} {g.Name} native {g.NativeKind} {g.NativeArg}");
            }
        }

        return 0;
    }

    private static string? FindRoot(string start)
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
