using System.Numerics;
using Slrr.Render.Content;
using Slrr.Render.Geometry;

namespace Slrr.Render.Scene;

/// <summary>
/// cfg <c>lods n t1 .. tn</c>: the LOD level is the number of thresholds below the object's projected size
/// (radius / (distance * tan(fovY/2))), clamped to the highest level present. GUESSED semantics, chosen because it
/// is consistent with the shipped data: rims (<c>lods 3 0.005 0.025 2.0</c>, renders at levels 0/1/2 marked lores/midres/hires)
/// switch to hires below ~29 m, lamp_base (<c>lods 2 0.01 0.025</c>, levels 0-1 and 2) below ~780 m.
/// </summary>
public sealed record LodSet(IReadOnlyList<float> Thresholds)
{
    public int Level(float screenFraction, int maxLevel)
    {
        var level = Thresholds.Count(t => screenFraction > t);
        return Math.Clamp(level, 0, Math.Max(maxLevel, 0));
    }
}

/// <summary>One placed render entry (a car part, an object, a wheel).</summary>
/// <param name="Def">The render entry.</param>
/// <param name="World">Local-to-world transform (row-vector convention).</param>
/// <param name="LodMin">cfg <c>lod a b</c> range of the render line (all levels when absent).</param>
/// <param name="Lods">The owning gametype's <c>lods</c> line, or null.</param>
/// <param name="Group">Name of the part / object the render belongs to (for reports).</param>
public sealed record DrawItem(RenderDef Def, Matrix4x4 World, int LodMin, int LodMax, LodSet? Lods, string Group, int LodLevels = 0);

/// <summary>Unit conventions.</summary>
public static class GameUnits
{
    /// <summary>
    /// SCX vertex positions are centimetres while cfg positions, instance boxes and object params are metres (MEASURED: the
    /// 15" MT_Mescaline rim mesh has radius 19.05 = 7.5 in x 2.54; Nonus chassis.scx is 410.4 long against wheel lines at
    /// z -1.351 / +1.260 m; lamp_base.scx is 802 tall and its cfg render offset is y 4.01 m).
    /// </summary>
    public const float ScxToMetres = 0.01f;

    public static System.Numerics.Matrix4x4 ScxScale => System.Numerics.Matrix4x4.CreateScale(ScxToMetres);
}

/// <summary>Map geometry that is not an RPK render entry (POLY chunks of the instance tree) with its node box.</summary>
public sealed record StaticChunk(CpuMesh Mesh, Vector3 Min, Vector3 Max, string Name);

/// <summary>Everything a frame draws besides the overlay.</summary>
public sealed class SceneDescription
{
    public List<DrawItem> Items { get; } = [];

    public List<StaticChunk> Static { get; } = [];

    /// <summary>Sky dome render entries, drawn centred on the camera without depth writes.</summary>
    public List<RenderDef> Sky { get; } = [];

    /// <summary>Direction towards the sun (frontend.rpk light "sun" direction is (0,-0.707,0.707) = light travelling down and +Z; MEASURED values).</summary>
    public Vector3 LightDirection { get; set; } = Vector3.Normalize(new Vector3(0.3f, 0.707f, -0.707f));

    public Vector3 Ambient { get; set; } = new(0.16f, 0.22f, 0.31f);

    public Vector3 LightColor { get; set; } = new(1f, 1f, 0.95f);

    public Vector4 ClearColor { get; set; } = new(0.42f, 0.52f, 0.62f, 1f);

    public List<string> Notes { get; } = [];
}
