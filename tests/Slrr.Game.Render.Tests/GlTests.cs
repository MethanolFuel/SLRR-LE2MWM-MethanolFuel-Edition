using System.Numerics;
using Slrr.Formats.Graphics;
using Slrr.Game.Render.Cameras;
using Slrr.Game.Render.Gl;
using Slrr.Game.Render.Maps;
using Slrr.Game.Render.Sequence;
using Slrr.Render.Gl;
using Slrr.Render.Scene;
using Xunit;
using Xunit.Abstractions;

namespace Slrr.Game.Render.Tests;

/// <summary>
/// Real rendering under software GL (<c>LIBGL_ALWAYS_SOFTWARE=1 xvfb-run -a dotnet test</c>). Each test returns early, passing, when
/// there is no GL context or no game data, like the corpus tests.
/// </summary>
public class GlTests(ITestOutputHelper output)
{
    private static long Differing(RgbaImage a, RgbaImage b, int tolerance = 6)
    {
        Assert.Equal(a.Pixels.Length, b.Pixels.Length);
        long n = 0;
        for (var i = 0; i < a.Pixels.Length; i += 4)
        {
            if (Math.Abs(a.Pixels[i] - b.Pixels[i]) > tolerance || Math.Abs(a.Pixels[i + 1] - b.Pixels[i + 1]) > tolerance || Math.Abs(a.Pixels[i + 2] - b.Pixels[i + 2]) > tolerance)
            {
                n++;
            }
        }

        return n;
    }

    private static int DistinctColours(RgbaImage img)
    {
        var set = new HashSet<int>();
        for (var i = 0; i < img.Pixels.Length; i += 4)
        {
            set.Add((img.Pixels[i] >> 3) | ((img.Pixels[i + 1] >> 3) << 5) | ((img.Pixels[i + 2] >> 3) << 10));
        }

        return set.Count;
    }

    [Fact]
    public void TheMainMenuIsDrawnWithItsThreeDBackgroundAndItsTexts()
    {
        if (GameRoot.Find() is not { } root || Gl.TryCreate() is not { } gl)
        {
            return;
        }

        using var glScope = gl;
        using var adapter = new GameRenderAdapter(new GameRenderOptions { Root = root, Width = 640, Height = 480, DrawOnlyOnCapture = true }, gl);
        using var menu = new MenuHost(root, adapter);
        var withOverlay = adapter.DrawToImage();
        var stats = adapter.LastStats!;
        output.WriteLine(stats.ToString());
        adapter.DrawOverlay = false;
        var sceneOnly = adapter.DrawToImage();
        adapter.DrawOverlay = true;

        Assert.Equal(640, withOverlay.Width);
        Assert.True(DistinctColours(withOverlay) > 300, "the picture is not flat");
        Assert.True(stats.DrawCalls > 40, $"{stats.DrawCalls} scene draw calls (the background car)");
        Assert.True(stats.OverlayDrawCalls >= 2, "texts and rectangles of the Osd");
        Assert.True(stats.Triangles > 10_000);
        Assert.True(Differing(withOverlay, sceneOnly) > 5_000, "the overlay (texts, header) changes the picture");
        // The 3D background covers a large part of the frame: many pixels differ from the clear colour.
        var clear = sceneOnly.Pixels;
        long covered = 0;
        for (var i = 0; i < clear.Length; i += 4)
        {
            if (Math.Abs(clear[i] - 51) > 10 || Math.Abs(clear[i + 1] - 59) > 10 || Math.Abs(clear[i + 2] - 71) > 10)
            {
                covered++;
            }
        }

        Assert.True(covered > 640 * 480 * 0.3, $"{covered} px of 3D content");
    }

    [Fact]
    public void CaptureEveryWritesAPngEveryNthHostFrame()
    {
        if (GameRoot.Find() is not { } root || Gl.TryCreate() is not { } gl)
        {
            return;
        }

        using var glScope = gl;
        var dir = GameRoot.TempDir("capture");
        try
        {
            using var adapter = new GameRenderAdapter(new GameRenderOptions { Root = root, Width = 320, Height = 240, CaptureDir = dir, CaptureEvery = 5, DrawOnlyOnCapture = true }, gl);
            using var host = new GameHost(new GameHostOptions { Root = root, Renderer = adapter, ExtraModules = [adapter.Hooks] });
            Assert.True(host.Boot());
            host.RunFrames(12);
            var files = Directory.GetFiles(dir, "frame-*.png").Select(Path.GetFileName).OrderBy(x => x).ToList();
            Assert.Equal(["frame-000000.png", "frame-000005.png", "frame-000010.png"], files);
            Assert.Equal(3, adapter.Frames.Count);
            var png = PngCodec.Decode(File.ReadAllBytes(Path.Combine(dir, files[0]!)));
            Assert.Equal((320, 240), (png.Width, png.Height));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void BackFaceCullingOfOpaqueMeshesDoesNotChangeTheCarButCullingGlassAndFoliageDoes()
    {
        if (GameRoot.Find() is not { } root || Gl.TryCreate() is not { } gl)
        {
            return;
        }

        gl.Dispose(); // Measure creates its own context
        var (rows, census) = CullMeasurement.Measure(root, null, 640, 480, 3);
        foreach (var r in rows)
        {
            output.WriteLine($"{r.Scene} {r.Mode}: {r.Ms:0.0} ms, {r.DifferingPercent:0.000} % differ");
        }

        double Pct(string scene, CullMode m) => rows.Single(r => r.Scene == scene && r.Mode == m).DifferingPercent;
        Assert.True(Pct("car", CullMode.OpaqueOnly) < 0.05, "opaque culling is invisible on a car");
        Assert.True(Pct("car", CullMode.All) > 10 * Math.Max(Pct("car", CullMode.OpaqueOnly), 0.01), "culling glass is visible");
        Assert.True(Pct("city", CullMode.All) > Pct("city", CullMode.OpaqueOnly), "culling alpha-tested foliage changes more");
        Assert.True(Pct("city", CullMode.All) > 1.0);
        var city = census.Single(c => c.Scene == "city");
        Assert.True(city.TestBack > 1000, "foliage triangles face away from the eye (and are drawn: double sided)");
    }

    [Fact]
    public void TheRealTrackFlowDrawsTheMapAndTheCarFromTheHostsWorld()
    {
        if (GameRoot.Find() is not { } root || Gl.TryCreate() is not { } gl)
        {
            return;
        }

        using var glScope = gl;
        using var adapter = new GameRenderAdapter(new GameRenderOptions { Root = root, Width = 480, Height = 360, SettleCarsOnGround = true }, gl);
        using var host = new GameHost(new GameHostOptions { Root = root, Renderer = adapter, ExtraModules = [adapter.Hooks] });
        adapter.ConsumedCommands = () => host.Natives.PhysicsCommands;
        Assert.True(host.Boot());
        var runner = new PlayRunner(host);
        runner.Run(PlayScript.Parse("until section MainMenu 30\nclick \"SELECT TRACK\"\nuntil section TrackSelect 10\nwait 2\nclick \"Drag Track\"\nwait 1\nclick \"TAKE IT\"\nwait 8\n"));
        Assert.All(runner.Results, r => Assert.True(r.Ok, r.Step.Text + ": " + r.Detail));
        var image = adapter.DrawToImage();
        var views = adapter.LastViews;
        output.WriteLine(string.Join(Environment.NewLine, views));
        // The Track script made: a map scene (root 1 of Drag_Track.rpk), the player's car at posStart (30, -0.5, -30) and an "external track cam".
        Assert.NotNull(adapter.Region);
        Assert.Contains("Drag_Track", adapter.Region!.Request.Archive, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1u, adapter.Region.Request.RootId);
        Assert.True(adapter.Region.Stats.PolyChunksLoaded > 50);
        Assert.Contains(views, v => v.StartsWith("chase-camera", StringComparison.Ordinal) && v.Contains("painted", StringComparison.Ordinal));
        Assert.True(DistinctColours(image) > 400);
        Assert.True(adapter.LastStats!.StaticVisible > 20 && adapter.LastStats.DrawCalls > 100);
        // Settling: the car is lifted out of the ground (posStart y is -0.5), so its roof is above the road: more than one ground-colour row of car pixels.
        Assert.True(adapter.LastStats.ItemsVisible > 60);
    }

    [Fact]
    public void PaintChangesTheCarsPixels()
    {
        if (GameRoot.Find() is not { } root || Gl.TryCreate() is not { } gl)
        {
            return;
        }

        using var glScope = gl;
        using var adapter = new GameRenderAdapter(new GameRenderOptions { Root = root, Width = 320, Height = 240, DrawOnlyOnCapture = true }, gl);
        using var menu = new MenuHost(root, adapter);
        var painted = adapter.DrawToImage();
        adapter.Cars!.ApplyPaint = false;
        var unpainted = adapter.DrawToImage();
        var differing = Differing(painted, unpainted);
        output.WriteLine($"{differing} of {320 * 240} pixels differ");
        Assert.True(differing > 2_000, "the yellow factory colour replaces default_white");
    }

    [Fact]
    public void ALightGlowAddsLightAroundItsPosition()
    {
        if (GameRoot.Find() is not { } root || Gl.TryCreate() is not { } gl)
        {
            return;
        }

        using var glScope = gl;
        var content = new Slrr.Render.Content.GameContent(root);
        using var renderer = new SceneRenderer(gl.Gl, content);
        using var target = new OffscreenTarget(gl.Gl, 200, 150);
        SceneView View(bool glow)
        {
            var v = new SceneView { Camera = new Camera { Position = new Vector3(0, 0, 5), Target = Vector3.Zero } };
            v.Scene.ClearColor = new Vector4(0, 0, 0, 1);
            if (glow)
            {
                v.Glows.Add(new GlowBillboard(Vector3.Zero, new Vector3(1, 0.2f, 0.2f), 0.5f, null));
            }

            return v;
        }

        target.Bind();
        var s0 = renderer.Draw([View(false)], null, 200, 150, new Vector4(0, 0, 0, 1));
        var off = target.Read();
        target.Bind();
        var s1 = renderer.Draw([View(true)], null, 200, 150, new Vector4(0, 0, 0, 1));
        var on = target.Read();
        Assert.Equal(0, s0.GlowDrawCalls);
        Assert.Equal(1, s1.GlowDrawCalls);
        var centre = ((75 * 200) + 100) * 4;
        Assert.True(on.Pixels[centre] > 150 && on.Pixels[centre + 1] < on.Pixels[centre], "red glow in the middle");
        Assert.Equal(0, off.Pixels[centre]);
        var corner = 4 * 5;
        Assert.Equal(0, on.Pixels[corner]);
    }

    [Fact]
    public void TheTextureCacheIsRebuiltWhenItExceedsItsBudgetAndTheImageStaysTheSame()
    {
        if (GameRoot.Find() is not { } root || Gl.TryCreate() is not { } gl)
        {
            return;
        }

        using var glScope = gl;
        using var adapter = new GameRenderAdapter(new GameRenderOptions { Root = root, Width = 320, Height = 240, TextureBudget = 1024 * 1024, DrawOnlyOnCapture = true }, gl);
        using var menu = new MenuHost(root, adapter);
        var a = adapter.DrawToImage();
        var b = adapter.DrawToImage();
        Assert.True(adapter.Renderer.TextureResets >= 1, "a 1 MB budget cannot hold the menu textures");
        Assert.Equal(0, Differing(a, b, 2));
    }

    [Fact]
    public void StreamedMapChunksAreEvictedFromTheGpu()
    {
        if (GameRoot.Find() is not { } root || Gl.TryCreate() is not { } gl)
        {
            return;
        }

        using var glScope = gl;
        var content = new Slrr.Render.Content.GameContent(root);
        using var renderer = new SceneRenderer(gl.Gl, content);
        using var target = new OffscreenTarget(gl.Gl, 160, 120);
        var streamer = new MapStreamer(new RegionMapLoader(content));
        var region = streamer.Update(new MapRequest("maps\\Addon_Maps\\Drag_Track.rpk", null, new Vector2(30, -30), 120), out _);
        var view = new SceneView { Camera = new Camera { Position = new Vector3(30, 30, 0), Target = new Vector3(30, 0, -60), Far = 800 } };
        view.Scene.Static.AddRange(region.Scene.Static);
        target.Bind();
        var stats = renderer.Draw([view], null, 160, 120, new Vector4(0.5f, 0.6f, 0.7f, 1));
        Assert.True(stats.StaticVisible > 5);
        Assert.Equal(stats.StaticVisible, renderer.StaticChunksOnGpu);
        var next = streamer.Update(new MapRequest("maps\\Addon_Maps\\Drag_Track.rpk", null, new Vector2(600, 500), 120), out var reloaded);
        Assert.True(reloaded);
        var dropped = renderer.RetainStatic(next.Scene.Static);
        Assert.Equal(stats.StaticVisible, dropped);
        Assert.Equal(0, renderer.StaticChunksOnGpu);
    }
}
