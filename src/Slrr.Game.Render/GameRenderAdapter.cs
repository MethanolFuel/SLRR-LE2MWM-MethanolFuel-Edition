using System.Diagnostics;
using System.Numerics;
using Slrr.Engine.Parts;
using Slrr.Engine.Resources;
using Slrr.Formats.Graphics;
using Slrr.Game.Render.Cameras;
using Slrr.Game.Render.Cars;
using Slrr.Game.Render.Gl;
using Slrr.Game.Render.Maps;
using Slrr.Game.Render.Scene;
using Slrr.Render.Content;
using Slrr.Render.Gl;
using Slrr.Render.Maps;
using Slrr.Render.Overlay;
using Slrr.Render.Scene;
using Slrr.Render.UiBridge;
using SceneCamera = Slrr.Render.Scene.Camera;

namespace Slrr.Game.Render;

/// <summary>Settings of the adapter.</summary>
public sealed class GameRenderOptions
{
    /// <summary>Game root (directory with the .rpk files).</summary>
    public required string Root { get; init; }

    public int Width { get; init; } = 800;

    public int Height { get; init; } = 600;

    /// <summary>Directory for frame captures (PNG); null = no capture.</summary>
    public string? CaptureDir { get; init; }

    /// <summary>Write a PNG every n host frames (0 = never). With a capture directory set and 0, only <see cref="GameRenderAdapter.CaptureNow"/> writes.</summary>
    public int CaptureEvery { get; init; }

    /// <summary>Draw every n-th host frame (1 = every frame). Frames that are neither drawn nor captured cost nothing.</summary>
    public int RenderEvery { get; init; } = 1;

    /// <summary>When true the scene is only drawn on capture frames (fast headless runs).</summary>
    public bool DrawOnlyOnCapture { get; init; }

    public CullMode Cull { get; init; } = CullMode.Off;

    /// <summary>Radius in metres of the map window kept in memory around the camera.</summary>
    public float MapRadius { get; init; } = 280f;

    /// <summary>Draw headlight / taillight glows even though the car state says they are off.</summary>
    public bool ForceLights { get; init; }

    /// <summary>See <see cref="GameRenderAdapter.SettleCarsOnGround"/>.</summary>
    public bool SettleCarsOnGround { get; init; }

    public Vector4 ClearColor { get; init; } = new(0.20f, 0.23f, 0.28f, 1f);

    public long TextureBudget { get; init; } = SceneRenderer.DefaultTextureBudget;
}

/// <summary>One drawn frame.</summary>
public sealed record FrameRecord(long HostFrame, float Time, FrameStats Stats, string? CapturePath);

/// <summary>
/// The <see cref="Slrr.Game.IRenderer"/> of the C# engine: per host frame it draws what the scripts describe into an offscreen target.
/// <list type="bullet">
/// <item>3D views from <see cref="ICameraSource"/> (script cameras and camera game objects), each with its scene: render instances,
/// cars (the live part tree, paint, wheel animation, light glows), the sun from the scene's light instance, map geometry from
/// <see cref="IMapSource"/> streamed around the eye, sky domes;</item>
/// <item>the 2D overlay of every active Osd (rectangles and texts) from the headless UI models via <c>Slrr.Render</c>'s UiOverlaySource;</item>
/// <item>PNG capture every n frames.</item>
/// </list>
/// Wiring (see docs/race/game-render.md): create the adapter, pass <see cref="Hooks"/> in <c>GameHostOptions.ExtraModules</c> and the adapter as
/// <c>GameHostOptions.Renderer</c>. The OpenGL context lives on the thread that created the adapter, so run the host loop on that thread.
/// </summary>
public sealed class GameRenderAdapter : Slrr.Game.IRenderer, IDisposable
{
    private readonly GameRenderOptions _options;
    private readonly bool _ownsHost;
    private readonly GlHost _host;
    private readonly GameContent _content;
    private readonly SceneRenderer _renderer;
    private readonly OffscreenTarget _target;
    private readonly MapStreamer _streamer;
    private readonly HashSet<StaticChunk> _resident = new(ReferenceEqualityComparer.Instance);
    private IGameWorld? _world;
    private CarVisualBuilder? _cars;
    private UiOverlaySource? _overlay;
    private ICameraSource? _cameraSource;
    private long _drawn;
    private int _frameCounter;
    private float _lastRenderTime;
    private float _lastTickTime;

    public GameRenderAdapter(GameRenderOptions options, GlHost? host = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
        ForceLights = options.ForceLights;
        SettleCarsOnGround = options.SettleCarsOnGround;
        if (host is null)
        {
            host = GlHost.TryCreate(out var error) ?? throw new InvalidOperationException(
                $"no OpenGL 3.3 context ({error}); on Linux run under xvfb-run with LIBGL_ALWAYS_SOFTWARE=1");
            _ownsHost = true;
        }

        _host = host;
        _content = new GameContent(options.Root);
        _renderer = new SceneRenderer(host.Gl, _content) { Cull = options.Cull, TextureBudget = options.TextureBudget };
        _target = new OffscreenTarget(host.Gl, options.Width, options.Height);
        _streamer = new MapStreamer(new RegionMapLoader(_content));
        MapSource = new HostMapSource(_content, options.MapRadius);
        Telemetry = new HostCarTelemetry();
        if (options.CaptureDir is { } dir)
        {
            Directory.CreateDirectory(dir);
        }
    }

    /// <summary>Register as the <b>last</b> native module of the host (<c>GameHostOptions.ExtraModules</c>).</summary>
    public RenderHooks Hooks { get; } = new();

    public GameContent Content => _content;

    public CarVisualBuilder? Cars => _cars;

    public SceneRenderer Renderer => _renderer;

    public IMapSource MapSource { get; set; }

    public ICarTelemetry Telemetry { get; set; }

    /// <summary>Replaces the default <see cref="HostCameraSource"/>.</summary>
    public ICameraSource? CameraSource
    {
        get => _cameraSource;
        set => _cameraSource = value;
    }

    /// <summary>Commands the physics layer consumed (<c>GameHost.Natives.PhysicsCommands</c>); lets the default camera source see camera verbs that Physics handled.</summary>
    public Func<IEnumerable<(int Target, string Text)>>? ConsumedCommands { get; set; }

    /// <summary>Linear fog start/end in metres for map views (0/0 = off).</summary>
    public Vector2 Fog { get; set; } = new(220f, 420f);

    /// <summary>
    /// Draw a car whose tyres would be below the map's ground resting on it instead (never lowers a car). For hosts without a physics engine, which
    /// never lifts a car out of the ground (the Track script places it at y -0.5).
    /// </summary>
    public bool SettleCarsOnGround { get; set; }

    public bool DrawOverlay { get; set; } = true;

    /// <summary>Draw headlight / taillight glows regardless of the car state (the host has no light state yet).</summary>
    public bool ForceLights { get; set; }

    public List<FrameRecord> Frames { get; } = [];

    public FrameStats? LastStats { get; private set; }

    public List<string> Notes { get; } = [];

    public int MapReloads => _streamer.Reloads;

    public LoadedRegion? Region => _streamer.Current;

    /// <summary>Last 3D views built (diagnostics).</summary>
    public IReadOnlyList<string> LastViews { get; private set; } = [];

    public void Attach(IGameWorld world)
    {
        _world = world;
        _cars = new CarVisualBuilder(_content, world.Resources);
        // MainMenu / TrackSelect / CarSelect build their 3D background only when openVideo fails (docs/engine/ui.md, M); there is no FMV here.
        world.Ui.Video.VideoAvailable = false;
    }

    public void Render(float time)
    {
        if (_world is null)
        {
            return;
        }

        var frame = _world.Frame;
        var capture = _options.CaptureDir is not null && _options.CaptureEvery > 0 && _frameCounter % _options.CaptureEvery == 0;
        var draw = capture || (!_options.DrawOnlyOnCapture && _frameCounter % Math.Max(_options.RenderEvery, 1) == 0);
        _frameCounter++;
        if (!draw)
        {
            Tick(_world, time);
            return;
        }

        var stats = DrawFrame(time);
        string? path = null;
        if (capture)
        {
            path = Path.Combine(_options.CaptureDir!, $"frame-{frame:D6}.png");
            WritePng(path);
        }

        Frames.Add(new FrameRecord(frame, time, stats, path));
    }

    /// <summary>Lets the telemetry see every host frame (wheel spin integrates over host time) even when nothing is drawn.</summary>
    private void Tick(IGameWorld world, float time)
    {
        var dt = Math.Max(0f, time - _lastTickTime);
        _lastTickTime = time;
        var rootId = world.Resources.RootId;
        foreach (var n in world.Resources.Tree.Instances.Where(n => n.NativeKind == "car" && n.Script is not null && n.ParentId != rootId && n.ParentId != 0).ToList())
        {
            if (world.Parts.Find(n.Script) is { IsChassis: true } chassis)
            {
                Telemetry.Get(world, n, chassis, dt);
            }
        }
    }

    /// <summary>Draws the current state and writes a PNG (the next call of <see cref="Render"/> is not needed).</summary>
    public FrameStats CaptureNow(string path)
    {
        var stats = DrawFrame(_world?.Vm.Time ?? 0f);
        WritePng(path);
        Frames.Add(new FrameRecord(_world?.Frame ?? 0, _world?.Vm.Time ?? 0f, stats, path));
        return stats;
    }

    /// <summary>Draws the current state <paramref name="frames"/> times without capturing (steady-state frame times, first-use uploads excluded by the caller's earlier draw).</summary>
    public List<FrameStats> Benchmark(int frames)
    {
        var list = new List<FrameStats>();
        for (var i = 0; i < frames; i++)
        {
            list.Add(DrawFrame(_world?.Vm.Time ?? 0f));
        }

        return list;
    }

    /// <summary>Draws without writing anything and returns the target pixels.</summary>
    public RgbaImage DrawToImage()
    {
        DrawFrame(_world?.Vm.Time ?? 0f);
        return _target.Read();
    }

    private void WritePng(string path)
    {
        _target.Bind();
        File.WriteAllBytes(path, PngCodec.Encode(_target.Read()));
    }

    private FrameStats DrawFrame(float time)
    {
        var world = _world ?? throw new InvalidOperationException("not attached to a host");
        var dt = Math.Max(0f, time - _lastRenderTime);
        _lastRenderTime = time;
        var views = BuildViews(world, dt, out var overlay);
        _target.Bind();
        var stats = _renderer.Draw(views, overlay, _options.Width, _options.Height, _options.ClearColor);
        LastStats = stats;
        _drawn++;
        return stats;
    }

    private IReadOnlyList<SceneView> BuildViews(IGameWorld world, float dt, out OverlayFrame? overlay)
    {
        var tree = world.Resources.Tree;
        var source = _cameraSource ??= new HostCameraSource(Hooks, ConsumedCommands);
        var bindings = source.Cameras(world).OrderBy(c => c.Layer).ToList();
        var views = new List<SceneView>();
        var names = new List<string>();
        var first = true;
        foreach (var cam in bindings)
        {
            var view = BuildView(world, tree, cam, dt);
            view.ClearColor = first;
            first = false;
            views.Add(view);
            names.Add($"{view.Name}: {view.Items.Count} items, {view.Scene.Static.Count} chunks, {view.Glows.Count} glows" + (view.Notes.Count > 0 ? " | " + string.Join("; ", view.Notes) : string.Empty));
        }

        LastViews = names;
        overlay = null;
        if (DrawOverlay)
        {
            _overlay ??= new UiOverlaySource(world.Ui, Hooks.Tap, id => HostIds.ToResRef(world.Resources, id), _content);
            overlay = _overlay.Capture();
        }

        return views;
    }

    private SceneView BuildView(IGameWorld world, ResourceTree tree, CameraBinding cam, float dt)
    {
        var view = new SceneView { Name = cam.Name, Rect = cam.Rect };
        var aspect = cam.Rect.W * _options.Width / Math.Max(cam.Rect.H * _options.Height, 1f);
        view.Camera = new SceneCamera
        {
            Position = cam.Pose.Eye,
            Target = cam.Pose.Target,
            Up = cam.Pose.Up,
            FovY = cam.FovHorizontal ? CameraRigs.VerticalFov(cam.FovDegrees, aspect) : Math.Clamp(cam.FovDegrees, 5f, 170f) * MathF.PI / 180f,
            Near = cam.Near,
            Far = cam.Far,
        };
        view.Scene.ClearColor = _options.ClearColor;

        // Map geometry around the eye.
        LoadedRegion? region = null;
        if (MapSource.Resolve(world, cam.SceneId, cam.Pose.Eye) is { } request)
        {
            region = _streamer.Update(request, out var reloaded);
            if (reloaded)
            {
                _renderer.RetainStatic(region.Scene.Static);
            }

            view.Scene.Static.AddRange(region.Scene.Static);
            view.Items.AddRange(region.Scene.Items.Select(i => new PaintedItem(i)));
            if (view.Scene.Sky.Count == 0)
            {
                view.Scene.Sky.AddRange(region.Scene.Sky);
            }

            view.Fog = Fog;
            view.Scene.ClearColor = new Vector4(0.40f, 0.42f, 0.45f, 1f); // fog + horizon: the grey of the sky dome skirt (GUESSED match)
        }

        // Instances of the scene: render objects, lights, sky, cars.
        var rootId = world.Resources.RootId;
        foreach (var n in tree.Instances.ToList())
        {
            if (n.Type == ResourceTypes.InstanceRender && HostIds.IsInScene(tree, n, cam.SceneId) && !HostIds.InsideOsd(tree, n))
            {
                AddRenderInstance(world, tree, view, n);
            }
            else if (n.Type == ResourceTypes.InstanceGame && n.NativeKind == "car" && n.Script is not null && (cam.SceneId != 0 || n.ParentId != rootId) &&
                     HostIds.IsInScene(tree, n, cam.SceneId) && n.ParentId != 0)
            {
                AddCar(world, tree, view, n, dt, region?.Scene.Static);
            }
        }

        return view;
    }

    private void AddRenderInstance(IGameWorld world, ResourceTree tree, SceneView view, ResourceNode n)
    {
        // The render entry is the node's parent id on the current host (MEASURED, see HostIds.RenderParts).
        if (HostIds.ToResRef(world.Resources, HostIds.RenderParts(tree, n).Render) is not { } rr)
        {
            return;
        }

        var entry = _content.Entry(rr);
        if (entry is { TypeOfEntry: 13 })
        {
            // Only the sky dome archive's light is the sun (MainMenu: maps\skydome.rpk:0x24); other lights (the garage neon) are local lamps, not drawn yet.
            if (string.Equals(rr.Archive, MapLoader.SkyArchive, StringComparison.OrdinalIgnoreCase) &&
                n.Properties.TryGetValue("light", out var l) && l is int[] { Length: >= 3 } c)
            {
                // RenderRef.setLight(a, b, c) of the MainMenu sun: 14443620 / 6579300 / 13158600. Read as diffuse, ambient, specular (GUESSED).
                view.Scene.LightColor = Rgb(c[0]);
                view.Scene.Ambient = Rgb(c[1]) * 1.4f;
            }

            return;
        }

        if (_content.Render(rr) is not { } def)
        {
            return;
        }

        if (string.Equals(def.Ref.Archive, MapLoader.SkyArchive, StringComparison.OrdinalIgnoreCase))
        {
            view.Scene.Sky.Add(def);
            return;
        }

        var m = GameUnits.ScxScale * HostIds.World(tree, n);
        view.Items.Add(new PaintedItem(new DrawItem(def, m, 0, int.MaxValue, null, n.Name)));
    }

    private static Vector3 Rgb(int packed) => new(((packed >> 16) & 0xFF) / 255f, ((packed >> 8) & 0xFF) / 255f, (packed & 0xFF) / 255f);

    private void AddCar(IGameWorld world, ResourceTree tree, SceneView view, ResourceNode node, float dt, IReadOnlyList<StaticChunk>? ground)
    {
        if (_cars is null || world.Parts.Find(node.Script) is not { IsChassis: true } chassis)
        {
            return;
        }

        var state = Telemetry.Get(world, node, chassis, dt);
        var carWorld = state?.World ?? HostIds.World(tree, node);
        if (SettleCarsOnGround && state?.World is null && ground is { Count: > 0 } && _cars.RideHeight(chassis) is { } ride &&
            GroundSampler.TryGetGround(ground, carWorld.Translation.X, carWorld.Translation.Z, carWorld.Translation.Y + 3f, out var gy))
        {
            // No physics yet: the scripts place a car at its start position (Track posStart y -0.5) and the engine's simulation lifts or drops it
            // onto the ground. A car whose tyres would be below the ground is drawn resting on it (the node is not touched). A car above the
            // ground stays where it is: in the garage it stands on the jack, which is not map geometry.
            if (carWorld.Translation.Y < gy + ride)
            {
                carWorld.Translation = new Vector3(carWorld.Translation.X, gy + ride, carWorld.Translation.Z);
            }
        }

        var frame = _cars.Build(chassis, carWorld, state, ForceLights);
        view.Items.AddRange(frame.Items);
        view.Glows.AddRange(frame.Glows);
        view.Notes.AddRange(frame.Notes.Take(5));
        view.Notes.Add($"car {node.Id:X8}: {frame.Parts} parts, {frame.PaintedParts} painted, {frame.Lights} lights, {frame.Items.Count} items");
    }

    public void Dispose()
    {
        _target.Dispose();
        _renderer.Dispose();
        if (_ownsHost)
        {
            _host.Dispose();
        }
    }
}
