using Slrr.Formats.Graphics;
using Slrr.Render.Overlay;
using Slrr.Render.Scene;

namespace Slrr.Render.Gl;

/// <summary>Frames rendered into an offscreen target.</summary>
public sealed class FrameResult
{
    public required RgbaImage Image { get; init; }

    /// <summary>The first frame (includes mesh/texture uploads).</summary>
    public required RenderStats First { get; init; }

    /// <summary>The following frames (steady state).</summary>
    public required IReadOnlyList<RenderStats> Steady { get; init; }

    public double SteadyAverageMs => Steady.Count == 0 ? First.FrameMs : Steady.Average(s => s.FrameMs);

    public double SteadyMinMs => Steady.Count == 0 ? First.FrameMs : Steady.Min(s => s.FrameMs);
}

public static class OffscreenRenderer
{
    /// <summary>Renders <paramref name="frames"/> frames (at least 1) of the same scene and returns the last image.</summary>
    public static FrameResult Render(GlHost host, GameRenderer renderer, SceneDescription scene, Camera camera, OverlayFrame? overlay, int width, int height, int frames)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(renderer);
        using var target = new OffscreenTarget(host.Gl, width, height);
        target.Bind();
        var first = renderer.Draw(scene, camera, width, height, overlay);
        var steady = new List<RenderStats>();
        for (var i = 1; i < Math.Max(frames, 1); i++)
        {
            target.Bind();
            steady.Add(renderer.Draw(scene, camera, width, height, overlay));
        }

        return new FrameResult { Image = target.Read(), First = first, Steady = steady };
    }
}
