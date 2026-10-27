using System.Globalization;
using Slrr.Core;
using Slrr.Engine.Parts;
using Slrr.Engine.Resources;
using Slrr.Engine.SystemLib;
using Slrr.Engine.Ui;
using Slrr.Vm;

namespace Slrr.Game;

/// <summary>Options of a <see cref="GameHost"/>.</summary>
public sealed class GameHostOptions
{
    /// <summary>Game root (directory with system.rpk). Never written.</summary>
    public required string Root { get; init; }

    /// <summary>Writable directory that backs the script path <c>save/...</c>. Default: a fresh temp directory owned by the host.</summary>
    public string? SaveRoot { get; init; }

    /// <summary>Missing natives become no-ops returning 0/null (recorded in <see cref="GameHost.Missing"/>) instead of stopping the thread.</summary>
    public bool StubMissingNatives { get; init; } = true;

    public float FrameTime { get; init; } = 1f / 30f;

    public long InstructionBudget { get; init; } = 2_000_000_000;

    public int RandomSeed { get; init; } = 12345;

    public IPhysicsWorld? Physics { get; init; }

    public IRenderer? Renderer { get; init; }

    public IAudioSink? Audio { get; init; }

    /// <summary>Echo of System.log / engine messages.</summary>
    public TextWriter? ScriptLog { get; init; }

    /// <summary>Extra native modules registered after the host's own (replace semantics apply only if they say so).</summary>
    public IReadOnlyList<INativeModule> ExtraModules { get; init; } = [];

    /// <summary>
    /// Native modules that need the host's own objects (e.g. <see cref="GameHost.Resources"/>, <see cref="GameHost.Natives"/>):
    /// each factory runs inside the constructor once those exist, and its module is registered after
    /// <see cref="ExtraModules"/>, in list order (so the last factory's module wraps everything before it).
    /// </summary>
    public IReadOnlyList<Func<GameHost, INativeModule>> ModuleFactories { get; init; } = [];

    /// <summary>Gap-closing host natives (<see cref="HostNatives"/>); off only to measure what the engine areas alone do.</summary>
    public bool HostNatives { get; init; } = true;

    /// <summary>Engine model behind DynoData (Slrr.Physics). Null: <see cref="PlaceholderDynoCalculator"/> unless
    /// <see cref="PlaceholderDyno"/> is false (then the parts area's NullDynoCalculator: no engine ever works).</summary>
    public Slrr.Engine.Parts.IDynoCalculator? Dyno { get; init; }

    public bool PlaceholderDyno { get; init; } = true;

    /// <summary>In-memory script shims (<see cref="Game.ScriptShims"/>) for VM behaviours the scripts need.</summary>
    public bool ScriptShims { get; init; } = true;
}

/// <summary>A native that was called but is registered by no module.</summary>
public sealed class MissingNativeRecord
{
    public required NativeKey Key { get; init; }

    public int Calls { get; set; }

    public required IReadOnlyList<string> FirstTrace { get; init; }

    /// <summary>The screen (active GameState / top dialog) when it was first hit.</summary>
    public required string Screen { get; init; }
}

/// <summary>
/// The composition root of the C# engine: one VM with every engine native area (Resources, System, Parts, Ui) plus the
/// host's own gap-closing natives, booted exactly like the original start-up (<c>engine-boot</c>: system.rpk, Config,
/// Config.init = <c>java.game.Init</c>, GameLogic, SplashScreen), then a frame loop that advances script time, input,
/// UI, events/timers, per-object callbacks and (through <see cref="IPhysicsWorld"/>, <see cref="IRenderer"/>,
/// <see cref="IAudioSink"/>) the simulation and presentation areas.
/// </summary>
public sealed class GameHost : IGameWorld, IDisposable
{
    private readonly bool _ownsSaveRoot;
    private readonly List<string> _log = [];
    private long _frame;

    public GameHost(GameHostOptions options)
    {
        Options = options;
        Root = Path.GetFullPath(options.Root);
        _ownsSaveRoot = options.SaveRoot is null;
        SaveRoot = options.SaveRoot ?? Path.Combine(Path.GetTempPath(), "slrr-game-" + Environment.ProcessId + "-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(SaveRoot);

        Game = new GameFileSystem(Root);
        Resources = new ResourceEngine(Game);
        SystemArea = new EngineSystem(new GameSandbox(Root, SaveRoot))
        {
            // Same switches as engine-boot / the integration test: the Resources area owns openLib and the timers,
            // and Slrr.Vm's Thread.sleep already takes milliseconds.
            RegisterOpenLib = false,
            RegisterTimerNatives = false,
            FixThreadSleep = false,
        };

        // Resource references in SDAT files with the exe's archive-name trailer (MEASURED, see SdatTrailer), so that the
        // shipped saved cars (cars/racers/ROC_cars) load and this engine's saves carry the same table.
        SystemArea.ArchiveNameOf = i => Resources.Archives[i]?.Name;
        SystemArea.ArchiveIndexOf = name => Resources.Archives.Register(name);
        PartsNatives = new PartsNatives(new PartsWorld(new HostPartHost(this)));
        PartsNatives.World.Dyno = options.Dyno ?? (options.PlaceholderDyno ? new PlaceholderDynoCalculator() : NullDynoCalculator.Instance);
        UiEngine = new UiEngine();
        Physics = options.Physics ?? new NullPhysicsWorld();
        Renderer = options.Renderer ?? new NullRenderer();
        Audio = options.Audio ?? new RecordingAudioSink();
        Natives = new HostNatives(this);

        var registry = NativeRegistry.CreateDefault();
        registry.Add(new ResourceNatives(Resources)).Add(new ArchiveNatives(Resources));
        registry.Add(SystemArea).Add(PartsNatives).Add(new UiNatives(UiEngine));
        if (options.HostNatives)
        {
            registry.Add(Natives);
        }

        foreach (var m in options.ExtraModules)
        {
            registry.Add(m);
        }

        foreach (var factory in options.ModuleFactories)
        {
            registry.Add(factory(this));
        }

        Registry = registry;
        Vm = new Slrr.Vm.Vm(new VmClassPath(Game), registry)
        {
            InstructionBudget = options.InstructionBudget,
            Random = new Random(options.RandomSeed),
        };
        Resources.Attach(Vm);
        if (options.ScriptShims)
        {
            ScriptShims.PatchEmptyObjectReturns(Vm.ClassPath, Vm.ClassPath.AllScannedClassNames().ToList(), ShimmedMethods);
        }

        Vm.Out = new LineWriter(line =>
        {
            _log.Add(line);
            options.ScriptLog?.WriteLine(line);
        });
        Driver = new UiDriver(Vm, UiEngine) { FrameTime = options.FrameTime };
        Input = new InputInjector(this);
        Screens = new ScreenTracker(this);

        registry.Fallback = key =>
        {
            var miss = Missing.Find(m => m.Key == key);
            if (miss is null)
            {
                var trace = Vm.CurrentThread?.Frames.Reverse().Take(6).Select(f => f.Describe()).ToList() ?? [];
                miss = new MissingNativeRecord { Key = key, FirstTrace = trace, Screen = Screens.Current };
                Missing.Add(miss);
            }

            miss.Calls++;
            return options.StubMissingNatives ? NativeRegistry.DefaultStub(key) : null;
        };
    }

    public GameHostOptions Options { get; }

    public string Root { get; }

    public string SaveRoot { get; }

    public GameFileSystem Game { get; }

    public Slrr.Vm.Vm Vm { get; }

    public NativeRegistry Registry { get; }

    public ResourceEngine Resources { get; }

    public EngineSystem SystemArea { get; }

    public PartsNatives PartsNatives { get; }

    public PartsWorld Parts => PartsNatives.World;

    public UiEngine UiEngine { get; }

    UiEngine IGameWorld.Ui => UiEngine;

    public UiDriver Driver { get; }

    public HostNatives Natives { get; }

    public InputInjector Input { get; }

    public ScreenTracker Screens { get; }

    public IPhysicsWorld Physics { get; }

    public IRenderer Renderer { get; }

    public IAudioSink Audio { get; }

    public long Frame => _frame;

    /// <summary>Script methods changed in memory by <see cref="Game.ScriptShims"/>.</summary>
    public List<string> ShimmedMethods { get; } = [];

    /// <summary>Missing natives in first-hit order.</summary>
    public List<MissingNativeRecord> Missing { get; } = [];

    /// <summary>Script thread errors (each ends its thread, as in the engine), in order.</summary>
    public List<string> ThreadErrors { get; } = [];

    /// <summary>System.log output and engine messages.</summary>
    public IReadOnlyList<string> Log => _log;

    public List<BootStep> BootSteps { get; } = [];

    public bool Booted { get; private set; }

    /// <summary>The <c>java.game.Init</c> instance (GameInit).</summary>
    public VmObject? Init { get; private set; }

    // ---------------------------------------------------------------- boot

    /// <summary>
    /// The original start-up (measured order, see Slrr.Engine.Resources.Boot and docs/engine-api/entry-points.md section 1):
    /// mount system.rpk, VM bootstrap refs + WORLDTREEROOT, create gametype system.rpk:0x47 "Config", Config contract,
    /// create Config.init "GameInit" (java.game.Init → GameLogic → SplashScreen). Returns true when GameLogic exists.
    /// </summary>
    public bool Boot()
    {
        var sys = Resources.Archives.Register("system.rpk");
        if (Resources.Archives.EnsureLoaded(sys) is null)
        {
            BootSteps.Add(new BootStep("mount system.rpk", "exe 0x58c765", false, "Data is missing. (distr. v2.2.1 is needed)"));
            return false;
        }

        BootSteps.Add(new BootStep("mount system.rpk", "exe 0x58c765", true, $"archive index {sys}"));
        int[] fixedIds = [0x1, 0x4, 0x3, 0xC, 0xB];
        var resolved = fixedIds.Select(i => Resources.Archives.ResolveLiteral("system.rpk", (uint)i)).ToList();
        if (Resources.Tree.Get(Resources.RootId) is { } root)
        {
            root.Flags |= 0x10;
        }

        BootSteps.Add(new BootStep("VM bootstrap", "exe 0x427980", resolved.All(x => x != 0), $"{Registry.Count} natives registered"));
        Physics.Attach(this);
        Renderer.Attach(this);
        Audio.Attach(this);

        try
        {
            var config = Resources.Create(null, Resources.RootId, Resources.Archives.ResolveLiteral("system.rpk", 0x47), null, "Config", runScript: true);
            CollectThreadErrors();
            BootSteps.Add(new BootStep("create Config", "exe 0x427ad8..0x427b1b", config is not null, config?.Class.Name ?? "no script instance"));
            if (config is null)
            {
                return false;
            }

            var cfg = Vm.LoadClass("java.util.Config");
            var accept = Slrr.Engine.Resources.Boot.StaticInt(Vm, cfg, "acceptBuild");
            var version = Slrr.Engine.Resources.Boot.StaticString(Vm, cfg, "version");
            BootSteps.Add(new BootStep("Config contract", "exe 0x58c7ae..0x58c92c", accept <= Slrr.Engine.Resources.Boot.ExeBuild && version == Slrr.Engine.Resources.Boot.ExeVersion,
                $"acceptBuild={accept} version=\"{version}\""));

            var init = Slrr.Engine.Resources.Boot.StaticInt(Vm, cfg, "init");
            Init = Resources.Create(null, Resources.RootId, init, null, "GameInit", runScript: true);
            CollectThreadErrors();
            BootSteps.Add(new BootStep("GameInit", "exe 0x4289a0..0x4289e5", Init is not null, Init?.Class.Name ?? "null"));
            var logic = Init is null ? null : Probe.Field(Init, "gameLogic").AsObject;
            BootSteps.Add(new BootStep("new GameLogic", "class java.game.Init", logic is not null, logic?.Class.Name ?? "not set"));
        }
        catch (VmException ex)
        {
            ThreadErrors.Add($"boot: {ex.Message}");
            BootSteps.Add(new BootStep("boot", "host", false, ex.Message));
            return false;
        }

        Booted = BootSteps.All(s => s.Ok);
        Screens.Observe();
        return Booted;
    }

    // ---------------------------------------------------------------- frames

    /// <summary>
    /// One host frame (order GUESSED, the exe's per-frame order between event delivery, physics and rendering is not
    /// traced): queued input → engine frame (global hotkeys, <c>Frontend.render</c> wake, script threads for dt) →
    /// GameType callbacks (<c>animate</c>, <c>control(dt)</c>) → event/timer delivery → physics step → audio → render.
    /// </summary>
    public void RunFrame(float? dt = null)
    {
        var step = dt ?? Options.FrameTime;
        Input.BeforeFrame();
        Driver.Frame(step);
        Natives.RunCallbacks(step);
        if (Resources.Pump() > 0)
        {
            Driver.Settle();
        }

        var simDt = step * SystemArea.TimeWarp;
        Physics.Step(simDt);
        Audio.Update(step);
        Renderer.Render(Vm.Time);
        _frame++;
        CollectThreadErrors();
        Screens.Observe();
    }

    public void RunFrames(int n)
    {
        for (var i = 0; i < n; i++)
        {
            RunFrame();
        }
    }

    public void RunFor(float seconds)
    {
        var end = Vm.Time + seconds;
        while (Vm.Time < end - 1e-5f)
        {
            RunFrame(Math.Min(Options.FrameTime, end - Vm.Time));
        }
    }

    /// <summary>Runs frames until <paramref name="done"/> holds or <paramref name="maxSeconds"/> of virtual time pass.</summary>
    public bool RunUntil(Func<bool> done, float maxSeconds = 30f)
    {
        var end = Vm.Time + maxSeconds;
        while (!done())
        {
            if (Vm.Time >= end)
            {
                return false;
            }

            RunFrame();
        }

        return true;
    }

    private void CollectThreadErrors()
    {
        if (Vm.ThreadErrors.Count == 0)
        {
            return;
        }

        foreach (var (t, ex) in Vm.ThreadErrors)
        {
            var where = ex.ScriptTrace.FirstOrDefault();
            ThreadErrors.Add($"{t.Name}: {ex.Message}" + (where is null ? string.Empty : $" at {where}"));
        }

        Vm.ThreadErrors.Clear();
    }

    // ---------------------------------------------------------------- views

    /// <summary><c>GameLogic.actualState</c>.</summary>
    public VmObject? ActiveSection => Probe.Static(Vm, "java.game.GameLogic", "actualState").AsObject;

    /// <summary><c>GameLogic.player</c>.</summary>
    public VmObject? Player => Probe.Static(Vm, "java.game.GameLogic", "player").AsObject;

    /// <summary>The Osd receiving hotkeys (<c>Frontend.inputQueue.lastElement()</c>).</summary>
    public VmObject? TopOsd => Driver.TopOsd();

    /// <summary>Game state views and the play-script path evaluator.</summary>
    public GameView View => _view ??= new GameView(this);

    private GameView? _view;

    /// <summary>
    /// Runs <c>Class.method(args)</c> (static) on its own scheduled script thread, pumping frames until it ends (it may
    /// open dialogs, sleep or wait). Returns the thread's result, or null when it did not finish in time.
    /// </summary>
    public Value? CallStaticOnThread(string className, string method, float maxSeconds, params Value[] args)
    {
        var cls = Vm.LoadClass(className);
        var m = cls.FindMethods(method, args.Length).FirstOrDefault(x => x.IsStatic && x.Code is not null)
                ?? throw new VmException($"{className} has no static {method} with {args.Length} arguments");
        var t = Vm.Spawn(m, null, args, "host:" + method);
        Driver.Settle();
        return RunUntil(() => t.State == VmThreadState.Finished, maxSeconds) ? t.Result : null;
    }

    /// <summary>
    /// Career save as the scripts do it (<c>GameLogic.save(dir + "/")</c>: main file, parts bin, racers, market,
    /// then the current car through <c>CarLot.saveCar</c>/<c>Vehicle.save</c>). <paramref name="dir"/> is a script path
    /// below <c>save/</c>. Returns the script's status (1 ok).
    /// </summary>
    public int SaveCareer(string dir) =>
        CallStaticOnThread("java.game.GameLogic", "save", 30f, Value.Obj(Vm.NewString(dir.TrimEnd('/') + "/")))?.AsInt ?? -1;

    /// <summary><c>GameLogic.load(dir + "/")</c>; 0 = ok (MainMenu treats non-zero as a corrupt save).</summary>
    public int LoadCareer(string dir) =>
        CallStaticOnThread("java.game.GameLogic", "load", 60f, Value.Obj(Vm.NewString(dir.TrimEnd('/') + "/")))?.AsInt ?? -1;

    public string Describe() => string.Create(CultureInfo.InvariantCulture,
        $"t={Vm.Time:F2}s frame={_frame} section={ActiveSection?.Class.Name ?? "-"} screen={Screens.Current}");

    public void Dispose()
    {
        SystemArea.CloseAll();
        if (_ownsSaveRoot)
        {
            try
            {
                Directory.Delete(SaveRoot, true);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    private sealed class LineWriter(Action<string> sink) : TextWriter
    {
        private readonly System.Text.StringBuilder _line = new();

        public override System.Text.Encoding Encoding => System.Text.Encoding.UTF8;

        public override void Write(char value)
        {
            if (value == '\n')
            {
                sink(_line.ToString().TrimEnd('\r'));
                _line.Clear();
            }
            else
            {
                _line.Append(value);
            }
        }
    }
}
