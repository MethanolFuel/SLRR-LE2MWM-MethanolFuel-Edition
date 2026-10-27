using System.Globalization;
using System.Numerics;
using Slrr.Core;
using Slrr.Engine.Parts;
using Slrr.Engine.Resources;
using Slrr.Engine.SystemLib;
using Slrr.Engine.Ui;
using Slrr.Formats.Graphics;
using Slrr.Render.Cars;
using Slrr.Render.Content;
using Slrr.Render.Geometry;
using Slrr.Render.Gl;
using Slrr.Render.Maps;
using Slrr.Render.Overlay;
using Slrr.Render.Scene;
using Slrr.Render.UiBridge;
using Slrr.Vm;

namespace Slrr.Cli;

/// <summary>
/// <c>render</c>: offscreen renders with the Phase 4 renderer (src/Slrr.Render) to PNG.
/// Needs ProjectReferences from Slrr.Cli.csproj to src/Slrr.Render (added at integration). Headless Linux:
/// <c>LIBGL_ALWAYS_SOFTWARE=1 xvfb-run -a slrr render ...</c>.
/// </summary>
public static class CmdRender
{
    public const string Usage = """
        render car [car.rpk] [--car alias] [--rim cfg] [--tyre cfg] [--yaw r] [--pitch r] [common]
               assemble a car from its cfg (stock parts + rim/tyre on every wheel) and render it
        render map [map.rpk] [--at x,z] [--radius r] [--eye x,y,z] [--look x,y,z] [--sky alias] [common]
               render the map's POLY geometry + placed objects (instanced, culled, LOD) under a sky dome
        render ui [splash|menu] [--frames n] [common]
               boot the scripts headless (all engine areas), then draw the active Osds' rectangles and texts
        common: [--root dir] [--out file.png] [--size WxH] [--repeat n] [--cull]
        """;

    public static int Run(string[] args, TextWriter o, TextWriter e)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(o);
        ArgumentNullException.ThrowIfNull(e);
        if (args.Length == 0 || args[0] is not ("car" or "map" or "ui"))
        {
            e.WriteLine(Usage);
            return 2;
        }

        var opt = Options.Parse(args.Skip(1).ToArray());
        if (opt is null)
        {
            e.WriteLine(Usage);
            return 2;
        }

        var root = Path.GetFullPath(opt.Get("--root") ?? Directory.GetCurrentDirectory());
        if (!File.Exists(Path.Combine(root, "system.rpk")))
        {
            e.WriteLine($"error: {root} is not a game root (no system.rpk)");
            return 1;
        }

        var content = new GameContent(root);
        var (w, h) = opt.Size;
        var t0 = System.Diagnostics.Stopwatch.StartNew();
        SceneDescription scene;
        Camera cam;
        OverlayFrame? overlay = null;
        var renderer = (GameRenderer?)null;
        using var host = GlHost.TryCreate(out var glError);
        if (host is null)
        {
            e.WriteLine($"error: no OpenGL 3.3 context ({glError}); on Linux run under xvfb-run with LIBGL_ALWAYS_SOFTWARE=1");
            return 3;
        }

        o.WriteLine($"GL: {host.Version}");
        renderer = new GameRenderer(host.Gl, content) { CullBackFaces = opt.Has("--cull") };
        try
        {
            switch (args[0])
            {
                case "car":
                    (scene, cam) = BuildCar(content, renderer, opt, w / (float)h, o);
                    break;
                case "map":
                    (scene, cam) = BuildMap(content, opt, o);
                    renderer.Fog = new Vector2(cam.Far * 0.5f, cam.Far);
                    break;
                default:
                    (scene, cam, overlay) = BuildUi(content, root, opt, o, e);
                    break;
            }

            var load = t0.Elapsed.TotalMilliseconds;
            var result = OffscreenRenderer.Render(host, renderer, scene, cam, overlay, w, h, opt.Repeat);
            var outPath = opt.Get("--out") ?? $"render-{args[0]}.png";
            File.WriteAllBytes(outPath, PngCodec.Encode(result.Image));
            o.WriteLine($"scene build {load:0} ms; first frame: {result.First}");
            o.WriteLine($"steady frames: {result.Steady.Count}, avg {result.SteadyAverageMs:0.0} ms, min {result.SteadyMinMs:0.0} ms ({w}x{h}); textures {renderer.TextureCount}");
            o.WriteLine($"wrote {outPath}; peak working set {System.Diagnostics.Process.GetCurrentProcess().PeakWorkingSet64 / (1024 * 1024)} MB, texture upload {renderer.TextureBytes / (1024 * 1024)} MB");
            foreach (var n in scene.Notes.Concat(overlay?.Notes ?? []).Take(20))
            {
                o.WriteLine($"  note: {n}");
            }

            o.WriteLine($"content problems: {content.Problems.Count}");
            foreach (var p in content.Problems.Take(10))
            {
                o.WriteLine($"  {p}");
            }

            return 0;
        }
        finally
        {
            renderer.Dispose();
        }
    }

    private static (SceneDescription, Camera) BuildCar(GameContent content, GameRenderer renderer, Options opt, float aspect, TextWriter o)
    {
        var archive = GameFileSystem.Normalize(opt.Positional.FirstOrDefault() ?? "cars\\racers\\Nonus.rpk");
        var asm = new CarAssembler(content);
        var cars = asm.CarTypes(archive).ToList();
        var alias = opt.Get("--car");
        var car = cars.FirstOrDefault(c => alias is null || (content.Entry(c.Ref)?.Alias ?? "").Contains(alias, StringComparison.OrdinalIgnoreCase));
        if (car.Cfg is null)
        {
            throw new InvalidOperationException($"{archive}: no car gametype{(alias is null ? "" : $" matching {alias}")}");
        }

        var rim = asm.FindGametypeByCfg("parts\\wheels.rpk", opt.Get("--rim") ?? "rim_Einvagen_GT_7_5_16_ET_20_LOD_CATALOG_GARAGE.cfg");
        var tyre = asm.FindGametypeByCfg("parts\\wheels.rpk", opt.Get("--tyre") ?? "tyre_195_50_16_7_5_LOD_CATALOG_GARAGE.cfg");
        var a = asm.Assemble(car.Ref, rim, tyre);
        o.WriteLine($"car {content.Entry(car.Ref)?.Alias} ({car.Cfg}): {a.Parts.Count} parts, {a.Items.Count} render items, {a.Wheels.Count} wheels, {a.Lights} lights (not drawn); rim {rim}, tyre {tyre}");
        foreach (var n in a.Notes.Take(10))
        {
            o.WriteLine($"  note: {n}");
        }

        var scene = new SceneDescription { ClearColor = new Vector4(0.30f, 0.33f, 0.37f, 1) };
        scene.Items.AddRange(a.Items);
        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        long agree = 0, disagree = 0;
        foreach (var item in a.Items)
        {
            if (renderer.CpuMeshOf(item.Def) is { } m)
            {
                var (wmin, wmax) = Frustum.Transform(m.Min, m.Max, item.World);
                min = Vector3.Min(min, wmin);
                max = Vector3.Max(max, wmax);
            }

            if (content.Scx(item.Def.MeshPath) is { } scx)
            {
                var (ag, dis) = MeshStats.Winding(scx);
                agree += ag;
                disagree += dis;
            }
        }

        var body = a.Items.Where(i => i.Group == a.Parts[0].Name).Select(i => renderer.CpuMeshOf(i.Def)).OfType<CpuMesh>().ToList();
        var bodyMinY = body.Count > 0 ? body.Min(m => m.Min.Y) * GameUnits.ScxToMetres : float.NaN;
        o.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"bounds {min} .. {max}; chassis mesh bottom y {bodyMinY:0.###} m; wheel centres y {string.Join(", ", a.Wheels.Select(x => x.Position.Y.ToString("0.###", CultureInfo.InvariantCulture)))}; " +
            $"winding vs stored normals (RH cross): agree {agree}, disagree {disagree}"));
        var center = (min + max) * 0.5f;
        var radius = (max - min).Length() * 0.5f;
        var cam = new Camera { FovY = 0.7f, Near = 0.05f, Far = 200f };
        OrbitRig.Frame(center, radius * 0.8f, opt.Float("--yaw", -2.35f), opt.Float("--pitch", 0.28f), cam.FovY * MathF.Min(1, aspect)).Apply(cam);
        return (scene, cam);
    }

    private static (SceneDescription, Camera) BuildMap(GameContent content, Options opt, TextWriter o)
    {
        var archive = GameFileSystem.Normalize(opt.Positional.FirstOrDefault() ?? "maps\\test_track.rpk");
        var at = opt.Vec("--at");
        var radius = opt.Float("--radius", 400f);
        var loader = new MapLoader(content);
        var scene = loader.Load(archive, at is { } p ? new MapRegion(p.X, p.Y, radius) : null, opt.Get("--sky") ?? "Skydome_cloudy_day01");
        var s = loader.Stats;
        o.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"map {archive}: POLY chunks {s.PolyChunksLoaded}/{s.PolyChunks}, objects {s.ObjectsLoaded}/{s.Objects} ({s.ObjectTypes} types, {scene.Items.Count} render items), " +
            $"materials {s.Materials} ({s.TexturedMaterials} textured), sky {scene.Sky.Count}, extent {s.Min} .. {s.Max}; " +
            $"POLY winding vs stored normals (RH cross): agree {s.WindingAgree}, disagree {s.WindingDisagree}"));
        var center = (s.Min + s.Max) * 0.5f;
        if (at is { } c)
        {
            var ground = scene.Static.Where(x => x.Min.X <= c.X && x.Max.X >= c.X && x.Min.Z <= c.Y && x.Max.Z >= c.Y).Select(x => x.Min.Y).DefaultIfEmpty(0).Min();
            center = new Vector3(c.X, ground, c.Y);
        }

        var cam = new Camera { FovY = 0.9f, Near = 0.5f, Far = Math.Max(radius * 2.5f, 300f) };
        cam.Position = opt.Vec3("--eye") ?? center + new Vector3(-radius * 0.35f, radius * 0.18f, radius * 0.35f);
        cam.Target = opt.Vec3("--look") ?? center;
        return (scene, cam);
    }

    private static (SceneDescription, Camera, OverlayFrame) BuildUi(GameContent content, string root, Options opt, TextWriter o, TextWriter e)
    {
        var state = opt.Positional.FirstOrDefault() ?? "menu";
        var frames = (int)opt.Float("--frames", state == "splash" ? 20 : 150);
        var saveRoot = Path.Combine(Path.GetTempPath(), "slrr-render-ui-" + Environment.ProcessId);
        Directory.CreateDirectory(saveRoot);
        try
        {
            var ui = new UiEngine();
            var tap = new RenderTap();
            INativeModule[] modules =
            [
                new EngineSystem(new GameSandbox(root, saveRoot)) { RegisterOpenLib = false, RegisterTimerNatives = false, FixThreadSleep = false },
                new PartsNatives(),
                new UiNatives(ui),
                tap,
            ];
            var report = Boot.Run(new BootOptions { Root = root, Stub = false, Frames = frames, Modules = modules });
            o.WriteLine($"boot: {frames} frames, active section {report.ActiveSection}, stopped by {report.StoppedBy ?? "-"}, missing natives {report.Missing.Count}");
            if (!ui.IsBound)
            {
                throw new InvalidOperationException("the UI engine never ran (boot failed early)");
            }

            var names = report.Archives.ToDictionary(a => a.Index, a => GameFileSystem.Normalize(a.Name));
            ResRef? Map(int id) => id != 0 && names.TryGetValue((id >> 16) & 0xFFFF, out var n) ? new ResRef(n, (uint)(id & 0xFFFF)) : null;
            var overlay = new UiOverlaySource(ui, tap, Map, content).Capture();
            o.WriteLine($"overlay: {tap.Osds.Count} Osds created, {overlay.Quads.Count} rectangles, {overlay.Texts.Count} texts; video {overlay.VideoWidth}x{overlay.VideoHeight}");
            foreach (var t in overlay.Texts.Take(12))
            {
                o.WriteLine(string.Create(CultureInfo.InvariantCulture, $"  text \"{t.Text}\" at ({t.X:0.###}, {t.Y:0.###}) font {t.Font} align {t.Align}"));
            }

            foreach (var q in overlay.Quads.Take(12))
            {
                o.WriteLine(string.Create(CultureInfo.InvariantCulture, $"  rect {q.Label} at ({q.X:0.###}, {q.Y:0.###}) size {q.Width:0.###}x{q.Height:0.###} z {q.Depth:0.###}"));
            }

            var scene = new SceneDescription { ClearColor = new Vector4(0, 0, 0, 1) };
            return (scene, new Camera(), overlay);
        }
        finally
        {
            try
            {
                Directory.Delete(saveRoot, true);
            }
            catch (IOException ex)
            {
                e.WriteLine($"warning: {ex.Message}");
            }
        }
    }

    private sealed class Options
    {
        private readonly Dictionary<string, string?> _named = new(StringComparer.Ordinal);

        public List<string> Positional { get; } = [];

        public (int W, int H) Size { get; private set; } = (800, 600);

        public int Repeat { get; private set; } = 6;

        public string? Get(string name) => _named.TryGetValue(name, out var v) ? v : null;

        public bool Has(string name) => _named.ContainsKey(name);

        public float Float(string name, float fallback) =>
            Get(name) is { } s && float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) && float.IsFinite(v) ? v : fallback;

        public Vector2? Vec(string name)
        {
            var p = Get(name)?.Split(',');
            return p is { Length: 2 } && Parse(p[0], out var x) && Parse(p[1], out var y) ? new Vector2(x, y) : null;
        }

        public Vector3? Vec3(string name)
        {
            var p = Get(name)?.Split(',');
            return p is { Length: 3 } && Parse(p[0], out var x) && Parse(p[1], out var y) && Parse(p[2], out var z) ? new Vector3(x, y, z) : null;
        }

        private static bool Parse(string s, out float v) => float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v) && float.IsFinite(v);

        public static Options? Parse(string[] args)
        {
            var o = new Options();
            for (var i = 0; i < args.Length; i++)
            {
                var a = args[i];
                if (!a.StartsWith("--", StringComparison.Ordinal))
                {
                    o.Positional.Add(a);
                    continue;
                }

                if (a == "--cull")
                {
                    o._named[a] = null;
                    continue;
                }

                if (i + 1 >= args.Length)
                {
                    return null;
                }

                var v = args[++i];
                o._named[a] = v;
                if (a == "--size")
                {
                    var p = v.Split('x');
                    if (p.Length != 2 || !int.TryParse(p[0], out var w) || !int.TryParse(p[1], out var h) || w is < 16 or > 8192 || h is < 16 or > 8192)
                    {
                        return null;
                    }

                    o.Size = (w, h);
                }
                else if (a == "--repeat")
                {
                    if (!int.TryParse(v, out var n) || n is < 1 or > 1000)
                    {
                        return null;
                    }

                    o.Repeat = n;
                }
            }

            return o;
        }
    }
}
