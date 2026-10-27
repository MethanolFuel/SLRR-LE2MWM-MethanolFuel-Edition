using System.Diagnostics;
using Slrr.Audio;
using Slrr.Core;
using Slrr.Engine.Commands;
using Slrr.Engine.MapNatives;
using Slrr.Engine.Resources;
using Slrr.Game;
using Slrr.Game.Physics;
using Slrr.Game.Render;
using Slrr.GameAudio;
using Slrr.Physics.Dyno;
using Slrr.Vm;

namespace Slrr.Race;

/// <summary>Switches of a <see cref="RaceHost"/>.</summary>
public sealed class RaceHostOptions
{
    /// <summary>Draw through <see cref="GameRenderAdapter"/> (needs an OpenGL 3.3 context; on Linux xvfb + llvmpipe).</summary>
    public bool Render { get; init; }

    /// <summary>Mix sound through <see cref="GameAudioSink"/>; with <see cref="WavPath"/> the mix is written there.</summary>
    public bool Audio { get; init; }

    public string? WavPath { get; init; }

    public int SampleRate { get; init; } = 44100;

    /// <summary>PNG directory (render only).</summary>
    public string? CaptureDir { get; init; }

    /// <summary>Capture a PNG every n host frames (0 = never). With <see cref="DrawOnlyOnCapture"/> the frames in between cost nothing.</summary>
    public int CaptureEvery { get; init; }

    public bool DrawOnlyOnCapture { get; init; } = true;

    public int Width { get; init; } = 640;

    public int Height { get; init; } = 480;

    public float FrameTime { get; init; } = 1f / 30f;

    public TextWriter? ScriptLog { get; init; }

    /// <summary>Engine model behind DynoData: the exe-measured cycle model of Slrr.Physics (default) or the host placeholder.</summary>
    public bool ExeDyno { get; init; } = true;

    /// <summary>Map natives (GroundRef, Animation, ParticleSystem, Vector3/Ypr math).</summary>
    public bool MapNatives { get; init; } = true;

    /// <summary>Build the map's render scene in MapWorlds (only the renderer would use it; off saves load time headless).</summary>
    public bool MapRenderScene { get; init; }
}

/// <summary>
/// The race-wave composition root: one <see cref="GameHost"/> with every race adapter wired in the order their areas
/// require (docs/race/integration.md):
/// <list type="number">
/// <item>host natives (Resources, System, Parts, Ui, <see cref="HostNatives"/>);</item>
/// <item><see cref="MapNativesModule"/> (needs <see cref="GameHost.Resources"/>; replace semantics take over <c>setFog</c>);</item>
/// <item><see cref="PhysicsNatives"/> (Vehicle.getSpeedSquare/getHorn/hasCrime from the simulation);</item>
/// <item><see cref="CommandNatives"/> (must follow HostNatives: it chains getInfo; its Vehicle natives only fill gaps);</item>
/// <item>the renderer's <see cref="GameRenderAdapter.Hooks"/> (must be last).</item>
/// </list>
/// Physics = <see cref="CommandsPhysicsWorld"/>(<see cref="GamePhysicsWorld"/>), whose command engine sees the physics cars
/// through <see cref="PhysicsVehicleBridge"/>; the physics map comes from MapNatives via <see cref="MapWorldsSource"/>.
/// </summary>
public sealed class RaceHost : IDisposable
{
    private RaceHost(GameHost host, GamePhysicsWorld physics, CommandsPhysicsWorld commands, MapNativesModule? maps, MapWorldsSource mapSource,
        RaceAudioSink? audio, GameRenderAdapter? renderer)
    {
        Host = host;
        Physics = physics;
        Commands = commands;
        Maps = maps;
        MapSource = mapSource;
        Audio = audio;
        Renderer = renderer;
    }

    public GameHost Host { get; }

    public GamePhysicsWorld Physics { get; }

    public CommandsPhysicsWorld Commands { get; }

    /// <summary>The scene pruning between the command engine and the physics adapter.</summary>
    public MapScopedPhysics? Scoped { get; private init; }

    public CommandEngine CommandEngine => Commands.Engine ?? throw new InvalidOperationException("not booted");

    public MapNativesModule? Maps { get; }

    public MapWorldsSource MapSource { get; }

    public RaceAudioSink? Audio { get; }

    public GameRenderAdapter? Renderer { get; }

    /// <summary>Wall time spent in <see cref="GameHost.RunFrame"/> per stage (filled by <see cref="Measure"/>).</summary>
    public Dictionary<string, (int Frames, double Ms)> Costs { get; } = [];

    public static RaceHost Create(string root, RaceHostOptions? options = null)
    {
        options ??= new RaceHostOptions();
        var mapSource = new MapWorldsSource();
        var physics = new GamePhysicsWorld(new GamePhysicsOptions { Maps = mapSource });
        var bridge = new PhysicsVehicleBridge(physics);
        MapNativesModule? maps = null;
        var scoped = new MapScopedPhysics(physics, () => maps?.Maps, mapSource);
        var commands = new CommandsPhysicsWorld(scoped, res => CommandEngine.ForResources(res, bridge, bridge));

        GameRenderAdapter? renderer = null;
        if (options.Render)
        {
            renderer = new GameRenderAdapter(new GameRenderOptions
            {
                Root = root,
                Width = options.Width,
                Height = options.Height,
                CaptureDir = options.CaptureDir,
                CaptureEvery = options.CaptureEvery,
                DrawOnlyOnCapture = options.DrawOnlyOnCapture,
                SettleCarsOnGround = false, // physics puts the cars on the ground
            })
            {
                Telemetry = new PhysicsRenderTelemetry(physics),
            };
        }

        RaceAudioSink? audio = null;
        if (options.Audio)
        {
            var cat = SoundCatalog.Scan(new GameFileSystem(root));
            var clips = new CatalogClipProvider(cat);
            Slrr.Audio.IAudioSink output = options.WavPath is { } wav ? new WavFileSink(options.SampleRate, wav) : new NullSink(options.SampleRate);
            audio = new RaceAudioSink(new GameAudioSink(new AudioMixer(options.SampleRate), clips, output, VehicleAudioAssets.FromCatalog(cat, clips)), physics);
        }

        var factories = new List<Func<GameHost, INativeModule>>();
        if (options.MapNatives)
        {
            factories.Add(h =>
            {
                maps = new MapNativesModule(h.Resources, new MapWorldOptions { GameRoot = h.Root, BuildRenderScene = options.MapRenderScene },
                    new MapNativeOptions { FogSink = (id, c, n, f) => h.Natives.Fog[id] = (c, n, f) });
                maps.Maps.Listeners.Add(mapSource);
                return maps;
            });
        }

        factories.Add(_ => new PhysicsNatives(physics));
        factories.Add(_ => new CommandNatives(() => commands.Engine));
        if (renderer is not null)
        {
            factories.Add(_ => renderer.Hooks);
        }

        var host = new GameHost(new GameHostOptions
        {
            Root = root,
            FrameTime = options.FrameTime,
            ScriptLog = options.ScriptLog,
            Physics = commands,
            Renderer = renderer,
            Audio = audio,
            Dyno = options.ExeDyno ? new ExeDynoCalculator() : null,
            ModuleFactories = factories,
        });
        if (renderer is not null)
        {
            renderer.ConsumedCommands = () => host.Natives.PhysicsCommands;
        }

        return new RaceHost(host, physics, commands, maps, mapSource, audio, renderer) { Scoped = scoped };
    }

    // ------------------------------------------------------------------ views

    public string Section => Host.ActiveSection is { } s ? ScreenTracker.Short(s.Class.Name) : "";

    public VmObject? PlayerCar => Probe.Field(Host.Player, "car").AsObject;

    public int PlayerCarId => ResourceEngine.IdOf(PlayerCar);

    public CarTelemetry? PlayerTelemetry => Physics.Get(PlayerCarId);

    /// <summary>The map's Track is the active section and <c>Track.enter</c> ran to its end (<c>enableAnimateHook</c> registered GII_ANIMATE).</summary>
    public bool InTrack =>
        Host.ActiveSection is { } s && s.Class.IsSubclassOf("java.game.Track") && Host.Resources.NodeOf(s)?.Callbacks.Contains(HostNatives.GiiAnimate) == true;

    /// <summary>
    /// The player's engine as the scripts see it (<c>player.car.chassis.dynodata</c>, filled by Chassis.updatevariables →
    /// calcDyno): the fields Vehicle.isDriveable and Block.isDynoable test, for diagnostics and the report.
    /// </summary>
    public string EngineReport()
    {
        var chassis = Probe.Field(PlayerCar, "chassis").AsObject;
        var dyno = Probe.Field(chassis, "dynodata").AsObject;
        if (dyno is null)
        {
            return $"no chassis.dynodata (chassis {chassis?.Class.Name ?? "none"})";
        }

        string F(string n) => Probe.Field(dyno, n).AsFloat.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
        return $"maxTorque {F("maxTorque")} N m @ {F("RPM_maxTorque")}, maxHP {F("maxHP")} W @ {F("RPM_maxHP")}, Displacement {F("Displacement")}, " +
               $"Compression {F("Compression")} (min {F("minCompression")}, max {F("maxCompression")}), RPM_limit {F("RPM_limit")}, maxRPM {F("maxRPM")}, " +
               $"starter_torque {Probe.Field(chassis, "starter_torque").AsFloat.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)}";
    }

    /// <summary>Which physical inputs the controllers map to <paramref name="virtualAxis"/> (ControlSet → Controller.define), for diagnostics.</summary>
    public string InputReport(int virtualAxis) => string.Join("; ", Host.UiEngine.Controllers.Select(kv =>
        $"{kv.Key.Class.Name}#{ResourceEngine.IdOf(kv.Key)}: " + string.Join(",", kv.Value.Mappings.Where(m => m.VirtualAxis == virtualAxis)
            .Select(m => $"dev{m.Device}/ax{m.Axis}"))).ToList()) + $" -> value {Host.UiEngine.VirtualAxis(virtualAxis):0.##}";

    /// <summary>Runs <paramref name="action"/> and books its wall time and host frames under <paramref name="stage"/>.</summary>
    public T Measure<T>(string stage, Func<T> action)
    {
        var f0 = Host.Frame;
        var sw = Stopwatch.StartNew();
        var r = action();
        var c = Costs.GetValueOrDefault(stage);
        Costs[stage] = (c.Frames + (int)(Host.Frame - f0), c.Ms + sw.Elapsed.TotalMilliseconds);
        return r;
    }

    public void Dispose()
    {
        Host.Dispose();
        Audio?.Dispose();
        Renderer?.Dispose();
    }
}
