using System.Numerics;
using Slrr.Render.Scene;

namespace Slrr.Game.Render.Gl;

/// <summary>
/// A placed render entry plus an optional texture substitution. Car paint (MEASURED structure, GUESSED application): a part's
/// <c>Part.setTexture(id)</c> value replaces the render's texture-list entry that equals the part cfg's <c>texture</c> line, so
/// <see cref="PaintFrom"/> is the file of the cfg default texture and <see cref="PaintTo"/> the file of the painted one.
/// <see cref="BackfaceCull"/> culls the back faces of this item's opaque sub-meshes (alpha-tested and blended ones stay double sided).
/// </summary>
public sealed record PaintedItem(DrawItem Item, string? PaintFrom = null, string? PaintTo = null, bool BackfaceCull = false);

/// <summary>A light entry (type 13) drawn as a camera-facing additive glow. <see cref="Size"/> is in metres.</summary>
public readonly record struct GlowBillboard(Vector3 Position, Vector3 Color, float Size, string? Texture, Vector4 Uv = default);

/// <summary>Normalised screen rectangle, origin top-left.</summary>
public readonly record struct ViewRect(float X, float Y, float W, float H)
{
    public static ViewRect Full => new(0, 0, 1, 1);
}

/// <summary>One 3D pass: scene objects, camera and the part of the target it fills (a Viewport of the scripts).</summary>
public sealed class SceneView
{
    public string Name { get; set; } = "";

    public SceneDescription Scene { get; } = new();

    public List<PaintedItem> Items { get; } = [];

    public List<GlowBillboard> Glows { get; } = [];

    public Camera Camera { get; set; } = new();

    public ViewRect Rect { get; set; } = ViewRect.Full;

    /// <summary>Clear colour (and depth) first; false keeps what earlier views drew and clears depth only.</summary>
    public bool ClearColor { get; set; } = true;

    /// <summary>Linear fog start/end in metres (0/0 = off).</summary>
    public Vector2 Fog { get; set; }

    public List<string> Notes { get; } = [];
}

/// <summary>How faces are culled. See docs/race/game-render.md for the measurement behind the default.</summary>
public enum CullMode
{
    /// <summary>Draw both sides (the Slrr.Render default).</summary>
    Off,

    /// <summary>Cull back faces of every material (counter-clockwise front faces, MEASURED winding).</summary>
    All,

    /// <summary>Cull back faces of opaque materials only; alpha-tested (foliage, fences) and blended (glass) stay double sided.</summary>
    OpaqueOnly,
}

/// <summary>Counters of one <see cref="SceneRenderer.Draw"/> call.</summary>
public sealed class FrameStats
{
    public int Views { get; set; }
    public int DrawCalls { get; set; }
    public int OverlayDrawCalls { get; set; }
    public int GlowDrawCalls { get; set; }
    public int ItemsVisible { get; set; }
    public int ItemsCulled { get; set; }
    public int ItemsLodRejected { get; set; }
    public int StaticVisible { get; set; }
    public int StaticCulled { get; set; }
    public long Triangles { get; set; }
    public int Glows { get; set; }
    public double CpuMs { get; set; }
    public double FrameMs { get; set; }

    public int TotalDrawCalls => DrawCalls + OverlayDrawCalls + GlowDrawCalls;

    public override string ToString() =>
        $"views {Views}, draw calls {TotalDrawCalls} (scene {DrawCalls}, overlay {OverlayDrawCalls}, glow {GlowDrawCalls}), items visible {ItemsVisible} culled {ItemsCulled} lod-rejected {ItemsLodRejected}, " +
        $"static visible {StaticVisible} culled {StaticCulled}, triangles {Triangles:N0}, cpu {CpuMs:0.0} ms, frame {FrameMs:0.0} ms";
}
