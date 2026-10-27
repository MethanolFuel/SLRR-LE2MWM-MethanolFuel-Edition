using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using Slrr.Game.Render.Gl;
using Slrr.Game.Render.Maps;
using Slrr.Render.Content;
using Slrr.Render.Gl;
using Slrr.Render.Scene;

namespace Slrr.Game.Render.Sequence;

/// <summary>Result of a streaming run.</summary>
public sealed class StreamResult
{
    public int Steps { get; set; }

    public int Reloads { get; set; }

    public int MaxChunksHeld { get; set; }

    public int MaxChunksOnGpu { get; set; }

    public double MaxFrameMs { get; set; }

    public double AvgFrameMs { get; set; }

    public double MaxLoadMs { get; set; }

    public long PeakWorkingSetMb { get; set; }

    public long WorkingSetEndMb { get; set; }

    public long TextureMb { get; set; }

    public int TextureResets { get; set; }

    public double PathMetres { get; set; }
}

/// <summary>
/// Memory-bounded streaming check: a camera flies along a straight line over a big map (the city, whose all-at-once render ran out of memory)
/// while <see cref="MapStreamer"/> keeps only a circle of <c>radius</c> metres, the renderer frees the GPU copy of chunks that left it
/// and the texture cache obeys its budget.
/// </summary>
public static class StreamMeasurement
{
    public static int Run(string root, string archive, Vector2 from, Vector2 to, int steps, float radius, int width, int height, TextWriter o)
    {
        var r = Measure(root, archive, from, to, steps, radius, width, height);
        o.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"stream {archive}: {r.PathMetres:0} m in {r.Steps} steps, radius {radius:0} m: reloads {r.Reloads}, chunks held max {r.MaxChunksHeld} (on GPU max {r.MaxChunksOnGpu}), " +
            $"frame avg {r.AvgFrameMs:0.0} ms max {r.MaxFrameMs:0.0} ms, slowest region load {r.MaxLoadMs:0} ms, textures {r.TextureMb} MB (resets {r.TextureResets}), " +
            $"working set end {r.WorkingSetEndMb} MB, peak {r.PeakWorkingSetMb} MB"));
        return 0;
    }

    public static StreamResult Measure(string root, string archive, Vector2 from, Vector2 to, int steps, float radius, int width, int height)
    {
        using var host = GlHost.TryCreate(out var error) ?? throw new InvalidOperationException($"no OpenGL 3.3 context ({error})");
        var content = new GameContent(root);
        using var renderer = new SceneRenderer(host.Gl, content);
        using var target = new OffscreenTarget(host.Gl, width, height);
        var loader = new RegionMapLoader(content);
        var streamer = new MapStreamer(loader);
        var result = new StreamResult { Steps = steps, PathMetres = Vector2.Distance(from, to) };
        var times = new List<double>();
        var dir = Vector2.Normalize(to - from);
        for (var i = 0; i < steps; i++)
        {
            var p = Vector2.Lerp(from, to, i / (float)Math.Max(steps - 1, 1));
            var region = streamer.Update(new MapRequest(archive, null, p, radius), out var reloaded);
            if (reloaded)
            {
                renderer.RetainStatic(region.Scene.Static);
                result.MaxLoadMs = Math.Max(result.MaxLoadMs, region.LoadMs);
            }

            var ground = GroundSampler.TryGetGround(region.Scene.Static, p.X, p.Y, 2000f, out var gy) ? gy : 0f;
            var view = new SceneView { Fog = new Vector2(radius * 0.6f, radius * 1.1f) };
            view.Scene.Static.AddRange(region.Scene.Static);
            view.Scene.Sky.AddRange(region.Scene.Sky);
            view.Items.AddRange(region.Scene.Items.Select(it => new PaintedItem(it)));
            view.Scene.ClearColor = new Vector4(0.62f, 0.70f, 0.80f, 1f);
            view.Camera = new Camera
            {
                FovY = 0.9f, Near = 0.5f, Far = radius * 1.2f,
                Position = new Vector3(p.X, ground + 12f, p.Y),
                Target = new Vector3(p.X + (dir.X * 60f), ground + 6f, p.Y + (dir.Y * 60f)),
            };
            target.Bind();
            var stats = renderer.Draw([view], null, width, height, view.Scene.ClearColor);
            times.Add(stats.FrameMs);
            result.MaxChunksHeld = Math.Max(result.MaxChunksHeld, region.Scene.Static.Count);
            result.MaxChunksOnGpu = Math.Max(result.MaxChunksOnGpu, renderer.StaticChunksOnGpu);
        }

        result.Reloads = streamer.Reloads;
        result.AvgFrameMs = times.Average();
        result.MaxFrameMs = times.Max();
        result.TextureMb = renderer.TextureBytes / (1024 * 1024);
        result.TextureResets = renderer.TextureResets;
        using var proc = Process.GetCurrentProcess();
        result.PeakWorkingSetMb = proc.PeakWorkingSet64 / (1024 * 1024);
        result.WorkingSetEndMb = proc.WorkingSet64 / (1024 * 1024);
        return result;
    }
}
