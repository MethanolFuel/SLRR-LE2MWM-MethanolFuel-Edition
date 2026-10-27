namespace Slrr.Game.Render.Sequence;

/// <summary>Text reports of drawn frames.</summary>
public static class Report
{
    public static void Frames(GameRenderAdapter adapter, TextWriter o)
    {
        ArgumentNullException.ThrowIfNull(adapter);
        ArgumentNullException.ThrowIfNull(o);
        var f = adapter.Frames;
        if (f.Count == 0)
        {
            o.WriteLine("no frames drawn");
            return;
        }

        var ms = f.Select(x => x.Stats.FrameMs).OrderBy(x => x).ToList();
        o.WriteLine(string.Create(System.Globalization.CultureInfo.InvariantCulture,
            $"frames drawn {f.Count}: frame ms min {ms[0]:0.0} median {ms[ms.Count / 2]:0.0} max {ms[^1]:0.0}; draw calls avg {f.Average(x => x.Stats.TotalDrawCalls):0}, " +
            $"triangles avg {f.Average(x => x.Stats.Triangles):N0}; textures {adapter.Renderer.TextureCount} ({adapter.Renderer.TextureBytes / (1024 * 1024)} MB), meshes {adapter.Renderer.MeshCount}"));
        foreach (var r in f.Where(x => x.CapturePath is not null).Take(6))
        {
            o.WriteLine($"   {r.CapturePath}: {r.Stats}");
        }
    }
}
