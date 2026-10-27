using System.Diagnostics;
using System.Numerics;
using Slrr.Engine.Parts;
using Slrr.Engine.Resources;
using Slrr.Game.Render.Cameras;
using Slrr.Game.Render.Cars;
using Slrr.Game.Render.Gl;
using Slrr.Game.Render.Maps;
using Slrr.Game.Render.Scene;
using Slrr.Render.Gl;
using Slrr.Render.Scene;

namespace Slrr.Game.Render.Sequence;

/// <summary>Options of <see cref="DemoSequence"/>.</summary>
public sealed class DemoOptions
{
    public required string Root { get; init; }

    public required string OutDir { get; init; }

    public int Width { get; init; } = 800;

    public int Height { get; init; } = 600;

    public CullMode Cull { get; init; } = CullMode.Off;

    /// <summary>Map archive of the race stage. Its start position is the script's <c>posStart</c> when known (Drag_Track 30,-0.5,-30).</summary>
    public string MapArchive { get; init; } = "maps\\Addon_Maps\\Drag_Track.rpk";

    /// <summary>Start position (XZ) and heading (yaw) of the race stage; defaults to the Drag_Track script values.</summary>
    public Vector3 Start { get; init; } = new(30f, -0.5f, -30f);

    public float StartYaw { get; init; }

    /// <summary>Also run the real host flow (menu, SELECT TRACK, Drag Track, TAKE IT) in a second host.</summary>
    public bool RealTrack { get; init; } = true;
}

/// <summary>Everything a run produced (for the CLI report and the tests).</summary>
public sealed class DemoResult
{
    public List<(string Stage, string Path, FrameStats Stats, IReadOnlyList<string> Views)> Shots { get; } = [];

    public List<string> Problems { get; } = [];

    public int Steps { get; set; }

    public int StepsFailed { get; set; }

    public int RaceCars { get; set; }
}

/// <summary>
/// The scripted proof sequence, all through the real host: boot, main menu (the script builds its 3D background car because the adapter makes
/// <c>openVideo</c> fail), a new career into the garage with a bought car (the play script of Slrr.Game), then a race stage. The race stage is
/// composed by this harness because the host cannot start a race yet (VM/engine gaps in docs/phase4/game.md): it moves the player's
/// car (and the main menu's background car, which the scripts leave alive) into a fresh scene node on the map's start position, writes
/// position, velocity and steering the way Physics will (node state and the chassis telemetry cell), and renders a chase view while the
/// wheels spin.
/// </summary>
public static class DemoSequence
{
    public static int Run(DemoOptions opt, TextWriter o, TextWriter e)
    {
        var result = Execute(opt, o);
        foreach (var p in result.Problems)
        {
            e.WriteLine("problem: " + p);
        }

        return result.Problems.Count == 0 && result.StepsFailed == 0 ? 0 : 1;
    }

    public static DemoResult Execute(DemoOptions opt, TextWriter o)
    {
        ArgumentNullException.ThrowIfNull(opt);
        Directory.CreateDirectory(opt.OutDir);
        var result = new DemoResult();
        using var gl = GlHost.TryCreate(out var glError) ?? throw new InvalidOperationException($"no OpenGL 3.3 context ({glError}); on Linux run under xvfb-run with LIBGL_ALWAYS_SOFTWARE=1");
        var wall = Stopwatch.StartNew();
        RunComposed(opt, gl, o, result);
        if (opt.RealTrack)
        {
            RunRealTrack(opt, gl, o, result);
        }

        o.WriteLine($"done in {wall.Elapsed.TotalSeconds:0.0} s; peak working set {Process.GetCurrentProcess().PeakWorkingSet64 / (1024 * 1024)} MB");
        return result;
    }

    private static GameRenderAdapter NewAdapter(DemoOptions opt, GlHost gl) =>
        new(new GameRenderOptions { Root = opt.Root, Width = opt.Width, Height = opt.Height, DrawOnlyOnCapture = true, Cull = opt.Cull, SettleCarsOnGround = true }, gl);

    private static void TakeShot(GameRenderAdapter adapter, GameHost host, DemoOptions opt, DemoResult result, TextWriter o, string stage)
    {
        var path = Path.Combine(opt.OutDir, $"race-{stage}.png");
        var stats = adapter.CaptureNow(path);
        result.Shots.Add((stage, path, stats, adapter.LastViews));
        o.WriteLine($"[{stage}] {host.Describe()}");
        o.WriteLine($"   {stats}");
        var steady = adapter.Benchmark(8);
        o.WriteLine(string.Create(System.Globalization.CultureInfo.InvariantCulture,
            $"   steady state over {steady.Count} draws: frame {steady.Average(x => x.FrameMs):0.0} ms avg / {steady.Min(x => x.FrameMs):0.0} min, cpu {steady.Average(x => x.CpuMs):0.0} ms, draw calls {steady[^1].TotalDrawCalls}, triangles {steady[^1].Triangles:N0}"));
        foreach (var v in adapter.LastViews)
        {
            o.WriteLine($"   view {v}");
        }

        if (adapter.Region is { } rg)
        {
            o.WriteLine($"   map region {rg.Request.Archive} root {rg.Request.RootId} centre {rg.Request.Center} r {rg.Request.Radius}: chunks {rg.Stats.PolyChunksLoaded}/{rg.Stats.PolyChunks} (budget skipped {rg.ChunksSkippedByBudget}), objects {rg.Stats.ObjectsLoaded}/{rg.Stats.Objects}, load {rg.LoadMs:0} ms, reloads {adapter.MapReloads}");
        }
    }

    /// <summary>
    /// The real host flow: main menu -> SELECT TRACK -> Drag Track -> TAKE IT. The Track script builds the scene (map root, the player's car at
    /// its <c>posStart</c>, the "external track cam"); nothing here is composed by the harness except that no physics moves the car.
    /// </summary>
    private static void RunRealTrack(DemoOptions opt, GlHost gl, TextWriter o, DemoResult result)
    {
        using var adapter = NewAdapter(opt, gl);
        using var host = new GameHost(new GameHostOptions { Root = opt.Root, Renderer = adapter, ExtraModules = [adapter.Hooks] });
        adapter.ConsumedCommands = () => host.Natives.PhysicsCommands;
        if (!host.Boot() || !host.RunUntil(() => host.ActiveSection?.Class.Name?.EndsWith("MainMenu", StringComparison.Ordinal) == true, 30))
        {
            o.WriteLine("real track flow: boot/menu failed (skipped)");
            return;
        }

        var script = "click \"SELECT TRACK\"\nuntil section TrackSelect 10\nwait 2\nclick \"Drag Track\"\nwait 1\nclick \"TAKE IT\"\nwait 8\n";
        var runner = new PlayRunner(host);
        runner.Run(PlayScript.Parse(script));
        o.WriteLine($"real track flow: {runner.Results.Count(r => r.Ok)}/{runner.Results.Count} steps ok, screen {host.Screens.Current}");
        TakeShot(adapter, host, opt, result, o, "5-track-real");
    }

    private static void RunComposed(DemoOptions opt, GlHost gl, TextWriter o, DemoResult result)
    {
        using var adapter = NewAdapter(opt, gl);
        using var host = new GameHost(new GameHostOptions { Root = opt.Root, Renderer = adapter, ExtraModules = [adapter.Hooks] });
        adapter.ConsumedCommands = () => host.Natives.PhysicsCommands;
        if (!host.Boot())
        {
            result.Problems.Add("boot failed");
            return;
        }

        void Shot(string stage) => TakeShot(adapter, host, opt, result, o, stage);

        // 1. main menu with its 3D background
        if (!host.RunUntil(() => host.ActiveSection?.Class.Name?.EndsWith("MainMenu", StringComparison.Ordinal) == true, 30))
        {
            result.Problems.Add("MainMenu not reached");
        }

        host.RunFor(2.5f);
        Shot("1-menu");

        // 2. garage with a bought car (first part of the smoke script)
        var steps = PlayScript.Parse(string.Join('\n', PlayCommand.SmokeScript().Split('\n').TakeWhile(l => !l.StartsWith("# --- catalog", StringComparison.Ordinal))));
        var runner = new PlayRunner(host);
        runner.Run(steps);
        result.Steps = runner.Results.Count;
        result.StepsFailed = runner.Results.Count(r => !r.Ok);
        host.RunFor(4f);
        Shot("2-garage");

        // 3. the car on a map
        if (host.Parts.Find(host.View.Chassis) is not { } chassis || host.Resources.NodeOf(host.View.Chassis) is not { } carNode)
        {
            result.Problems.Add("no player car after the garage steps");
            return;
        }

        Race(host, adapter, chassis, carNode, opt, result, Shot);
        foreach (var (from, to) in adapter.Cars?.PaintSubstitutions ?? [])
        {
            o.WriteLine($"   paint {Path.GetFileName(from)} -> {Path.GetFileName(to)}");
        }

        o.WriteLine($"composed run: host frames {host.Frame}; {adapter.Frames.Count} frames drawn; texture {adapter.Renderer.TextureBytes / (1024 * 1024)} MB");
        foreach (var p in adapter.Content.Problems.Take(8))
        {
            o.WriteLine("   content: " + p);
        }
    }

    private sealed class RaceCar(ResourceNode node, PartState chassis, CarVisualBuilder builder, GameHost host, Vector3 start, float yaw, float speed)
    {
        private readonly ChassisState _cs = host.Parts.ChassisOf(chassis);
        private float _phase;
        private Vector3 _pos = start;

        public ResourceNode Node => node;

        public PartCfg? Cfg { get; } = builder.Cfg(chassis);

        public float Yaw { get; private set; } = yaw;

        /// <summary>Car origin above the ground so the tyres touch it: lowest cfg wheel y minus the tyre radius (GUESSED: no suspension sag).</summary>
        public float RideHeight
        {
            get
            {
                var radius = _cs.Wheels.Count > 0 && _cs.Wheels[0].Radius > 0.1f ? _cs.Wheels[0].Radius : 0.33f;
                var wheelY = Cfg is { Info.Wheels.Count: > 0 } ? Cfg.Info.Wheels.Min(w => w.Position.Y) : -0.6f;
                return -(wheelY - radius);
            }
        }

        public void Step(float dt, IReadOnlyList<StaticChunk> ground, float groundFallback, float steerAmplitude)
        {
            _phase += dt * 1.6f;
            var steer = MathF.Sin(_phase) * steerAmplitude;
            Yaw += steer * 0.02f;
            var fwd = new Vector3(-MathF.Sin(Yaw), 0, -MathF.Cos(Yaw));
            _pos += fwd * speed * dt;
            if (!GroundSampler.TryGetGround(ground, _pos.X, _pos.Z, 1000f, out var gy))
            {
                gy = groundFallback;
            }

            _pos.Y = gy + RideHeight;
            node.Pos[0] = _pos.X;
            node.Pos[1] = _pos.Y;
            node.Pos[2] = _pos.Z;
            node.Ori[0] = Yaw;
            node.Vel[0] = fwd.X * speed;
            node.Vel[1] = 0;
            node.Vel[2] = fwd.Z * speed;
            _cs.Telemetry.SetFloat(ChassisTelemetry.CurrentSteering1, steer);
        }
    }

    private static void Race(GameHost host, GameRenderAdapter adapter, PartState chassis, ResourceNode carNode, DemoOptions opt, DemoResult result, Action<string> shot)
    {
        var tree = host.Resources.Tree;
        var scene = tree.CreateInstance(ResourceTypes.InstanceGame, host.Resources.RootId, "race scene", 0);
        var builder = new CarVisualBuilder(adapter.Content, host.Resources);
        var yaw0 = opt.StartYaw;
        var fwd0 = new Vector3(-MathF.Sin(yaw0), 0, -MathF.Cos(yaw0));
        var right0 = new Vector3(-fwd0.Z, 0, fwd0.X);
        var start = new Vector3(opt.Start.X, 0, opt.Start.Z);
        var cars = new List<RaceCar> { new(carNode, chassis, builder, host, start, yaw0, 24f) };

        // Other cars: whatever the scripts still hold in a scene (the main menu's background car survives the screen change).
        var lane = 1;
        foreach (var other in tree.Instances.Where(n => n.NativeKind == "car" && n.Script is not null && n.ParentId != host.Resources.RootId && n.Id != carNode.Id).ToList())
        {
            if (host.Parts.Find(other.Script) is { IsChassis: true } oc && PartsWorld.Tree(oc).Count() > 10)
            {
                cars.Add(new RaceCar(other, oc, builder, host, start + (fwd0 * 14f * lane) + (right0 * 4.5f), yaw0, 21f));
                lane++;
            }
        }

        foreach (var c in cars)
        {
            tree.SetParent(c.Node, scene.Id);
        }

        result.RaceCars = cars.Count;
        adapter.MapSource = new FixedMapSource(opt.MapArchive, null, 260f);
        adapter.CameraSource = new DelegateCameraSource(w =>
        {
            var body = HostIds.World(tree, carNode);
            var pose = CameraRigs.Chase(body, cars[0].Cfg?.ExtCamera);
            return [new CameraBinding("chase", pose, 75f, 0.1f, 900f, ViewRect.Full, 5, scene.Id, "chase") { FollowCarId = carNode.Id }];
        });

        // Warm the region so the ground exists, then drive 90 frames (3 s of virtual time) with a gentle steering swing.
        var region = new MapStreamer(new RegionMapLoader(adapter.Content)).Update(new MapRequest(opt.MapArchive, null, new Vector2(opt.Start.X, opt.Start.Z), 260f), out _);
        for (var i = 0; i < 90; i++)
        {
            foreach (var c in cars)
            {
                c.Step(1f / 30f, region.Scene.Static, opt.Start.Y, c == cars[0] ? 0.5f : 0.15f);
            }

            host.RunFrame(1f / 30f);
        }

        adapter.Fog = new Vector2(300f, 650f);
        adapter.ForceLights = true;
        adapter.DrawOverlay = false; // the garage Osd is still the host's active screen; the race HUD does not exist yet
        shot("3-race");
        if (cars.Count > 1)
        {
            // A view of the other car from the side/front, to check its pose independently of the player's chase camera.
            adapter.CameraSource = new DelegateCameraSource(w =>
            {
                var body = HostIds.World(tree, cars[1].Node);
                var anchor = body.Translation + new Vector3(0, 0.8f, 0);
                return [new CameraBinding("other", CameraRigs.Orbit(anchor, 2.2f + cars[1].Yaw, 0.25f, 9f), 60f, 0.1f, 900f, ViewRect.Full, 5, scene.Id, "orbit") { FollowCarId = cars[1].Node.Id }];
            });
            shot("3b-other");
        }
    }
}
