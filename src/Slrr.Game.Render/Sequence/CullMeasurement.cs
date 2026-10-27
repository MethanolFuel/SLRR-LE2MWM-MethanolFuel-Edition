using System.Globalization;
using System.Numerics;
using Slrr.Formats.Graphics;
using Slrr.Game.Render.Gl;
using Slrr.Game.Render.Maps;
using Slrr.Render.Cars;
using Slrr.Render.Content;
using Slrr.Render.Geometry;
using Slrr.Render.Gl;
using Slrr.Render.Scene;

namespace Slrr.Game.Render.Sequence;

/// <summary>One scene's culling result.</summary>
public sealed record CullRow(string Scene, CullMode Mode, double Ms, int DrawCalls, long Triangles, long DifferingPixels, double DifferingPercent);

/// <summary>Back-facing triangle census of one scene, by material class.</summary>
public sealed record BackFaceCensus(string Scene, long OpaqueTotal, long OpaqueBack, long TestTotal, long TestBack, long BlendTotal, long BlendBack);

/// <summary>
/// The back-face culling decision. Winding was measured to agree with the stored normals (docs/phase4/render.md: 22,489 / 0 on the Nonus,
/// 198,564 / 10 on the city), but foliage and glass may be authored single-sided. This renders a car (opaque body + glass) and a city
/// region (alpha-tested trees) with every <see cref="CullMode"/>, counts the pixels that change against the double-sided render and the
/// back-facing triangles per material class.
/// </summary>
public static class CullMeasurement
{
    public static int Run(string root, string outDir, int width, int height, TextWriter o, TextWriter e)
    {
        var (rows, census) = Measure(root, outDir, width, height, 4);
        foreach (var r in rows)
        {
            o.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"{r.Scene,-8} {r.Mode,-11} {r.Ms,7:0.0} ms  draw calls {r.DrawCalls,4}  triangles {r.Triangles,9:N0}  pixels differing from Off: {r.DifferingPixels,7:N0} ({r.DifferingPercent:0.000} %)"));
        }

        foreach (var c in census)
        {
            o.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"{c.Scene}: back-facing triangles: opaque {c.OpaqueBack:N0}/{c.OpaqueTotal:N0}, alpha-test {c.TestBack:N0}/{c.TestTotal:N0}, blended {c.BlendBack:N0}/{c.BlendTotal:N0}"));
        }

        if (rows.Count == 0)
        {
            e.WriteLine("nothing measured (game data missing?)");
            return 1;
        }

        return 0;
    }

    public static (List<CullRow> Rows, List<BackFaceCensus> Census) Measure(string root, string? outDir, int width, int height, int repeats)
    {
        var rows = new List<CullRow>();
        var census = new List<BackFaceCensus>();
        using var host = GlHost.TryCreate(out var error) ?? throw new InvalidOperationException($"no OpenGL 3.3 context ({error})");
        var content = new GameContent(root);
        using var renderer = new SceneRenderer(host.Gl, content);
        using var target = new OffscreenTarget(host.Gl, width, height);
        foreach (var (name, view) in Scenes(content, renderer, width / (float)height))
        {
            if (outDir is not null)
            {
                Directory.CreateDirectory(outDir);
            }

            RgbaImage? reference = null;
            foreach (var mode in new[] { CullMode.Off, CullMode.All, CullMode.OpaqueOnly })
            {
                renderer.Cull = mode;
                FrameStats? last = null;
                var total = 0.0;
                for (var i = 0; i < repeats; i++)
                {
                    target.Bind();
                    last = renderer.Draw([view], null, width, height, view.Scene.ClearColor);
                    total += i == 0 ? 0 : last.FrameMs;
                }

                var image = target.Read();
                reference ??= image;
                var diff = Differing(reference, image);
                rows.Add(new CullRow(name, mode, total / Math.Max(repeats - 1, 1), last!.TotalDrawCalls, last.Triangles, diff, 100.0 * diff / (width * height)));
                if (outDir is not null && mode != CullMode.Off)
                {
                    File.WriteAllBytes(Path.Combine(outDir, $"cull-{name}-{mode}.png"), PngCodec.Encode(image));
                    File.WriteAllBytes(Path.Combine(outDir, $"cull-{name}-{mode}-diff.png"), PngCodec.Encode(DiffImage(reference, image)));
                }
                else if (outDir is not null)
                {
                    File.WriteAllBytes(Path.Combine(outDir, $"cull-{name}-Off.png"), PngCodec.Encode(image));
                }
            }

            census.Add(Census(name, view, renderer));
        }

        return (rows, census);
    }

    private static IEnumerable<(string Name, SceneView View)> Scenes(GameContent content, SceneRenderer renderer, float aspect)
    {
        // 1. A car from its cfg: opaque body, blended glass, alpha-tested lights.
        var asm = new CarAssembler(content);
        var archive = "cars\\racers\\Nonus.rpk";
        if (asm.CarTypes(archive).FirstOrDefault() is { Cfg: not null } car)
        {
            var rim = asm.FindGametypeByCfg("parts\\wheels.rpk", "rim_Einvagen_GT_7_5_16_ET_20_LOD_CATALOG_GARAGE.cfg");
            var tyre = asm.FindGametypeByCfg("parts\\wheels.rpk", "tyre_195_50_16_7_5_LOD_CATALOG_GARAGE.cfg");
            var assembly = asm.Assemble(car.Ref, rim, tyre);
            var view = new SceneView { Name = "car" };
            view.Scene.ClearColor = new Vector4(0.30f, 0.33f, 0.37f, 1f);
            var min = new Vector3(float.MaxValue);
            var max = new Vector3(float.MinValue);
            foreach (var item in assembly.Items)
            {
                view.Items.Add(new PaintedItem(item));
                if (renderer.Mesh(item.Def) is { } gm)
                {
                    var (a, b) = Frustum.Transform(gm.Mesh.Min, gm.Mesh.Max, item.World);
                    min = Vector3.Min(min, a);
                    max = Vector3.Max(max, b);
                }
            }

            var cam = new Camera { FovY = 0.7f, Near = 0.05f, Far = 200f };
            OrbitRig.Frame((min + max) * 0.5f, (max - min).Length() * 0.4f, -2.35f, 0.28f, cam.FovY * MathF.Min(1, aspect)).Apply(cam);
            view.Camera = cam;
            yield return ("car", view);
        }

        // 2. City region with trees and fences (alpha-tested), the viewpoint of docs/phase4/render.md.
        var region = new RegionMapLoader(content).Load(new MapRequest("maps\\city.rpk", null, new Vector2(-300, -500), 200));
        if (region.Scene.Static.Count > 0)
        {
            var view = new SceneView { Name = "city", Fog = new Vector2(300, 600) };
            view.Scene.Static.AddRange(region.Scene.Static);
            view.Scene.Sky.AddRange(region.Scene.Sky);
            view.Items.AddRange(region.Scene.Items.Select(i => new PaintedItem(i)));
            view.Scene.ClearColor = new Vector4(0.62f, 0.70f, 0.80f, 1f);
            view.Camera = new Camera { FovY = 0.9f, Near = 0.5f, Far = 800f, Position = new Vector3(-380, 25, -420), Target = new Vector3(-300, 2, -520) };
            yield return ("city", view);
        }
    }

    /// <summary>The reference image dimmed, with differing pixels in red (for looking at where culling changes the picture).</summary>
    private static RgbaImage DiffImage(RgbaImage a, RgbaImage b)
    {
        var d = new RgbaImage(a.Width, a.Height);
        for (var i = 0; i < a.Pixels.Length; i += 4)
        {
            var differs = Math.Abs(a.Pixels[i] - b.Pixels[i]) > 6 || Math.Abs(a.Pixels[i + 1] - b.Pixels[i + 1]) > 6 || Math.Abs(a.Pixels[i + 2] - b.Pixels[i + 2]) > 6;
            d.Pixels[i] = differs ? (byte)255 : (byte)(a.Pixels[i] / 3);
            d.Pixels[i + 1] = differs ? (byte)0 : (byte)(a.Pixels[i + 1] / 3);
            d.Pixels[i + 2] = differs ? (byte)0 : (byte)(a.Pixels[i + 2] / 3);
            d.Pixels[i + 3] = 255;
        }

        return d;
    }

    private static long Differing(RgbaImage a, RgbaImage b)
    {
        long n = 0;
        for (var i = 0; i < a.Pixels.Length; i += 4)
        {
            if (Math.Abs(a.Pixels[i] - b.Pixels[i]) > 6 || Math.Abs(a.Pixels[i + 1] - b.Pixels[i + 1]) > 6 || Math.Abs(a.Pixels[i + 2] - b.Pixels[i + 2]) > 6)
            {
                n++;
            }
        }

        return n;
    }

    /// <summary>Counts triangles whose face normal (RH cross, MEASURED to agree with the stored normals) points away from the eye, per alpha class.</summary>
    private static BackFaceCensus Census(string name, SceneView view, SceneRenderer renderer)
    {
        long[] total = new long[3], back = new long[3];
        var eye = view.Camera.Position;
        var frustum = new Frustum(view.Camera.ViewProjection(1.33f));

        void Count(CpuMesh mesh, Matrix4x4 world)
        {
            foreach (var sm in mesh.SubMeshes)
            {
                var k = (int)sm.Material.Alpha;
                for (var i = sm.FirstIndex; i + 2 < sm.FirstIndex + sm.IndexCount; i += 3)
                {
                    var a = Vector3.Transform(P(mesh, mesh.Indices[i]), world);
                    var b = Vector3.Transform(P(mesh, mesh.Indices[i + 1]), world);
                    var c = Vector3.Transform(P(mesh, mesh.Indices[i + 2]), world);
                    var n = Vector3.Cross(b - a, c - a);
                    total[k]++;
                    if (Vector3.Dot(n, eye - a) < 0)
                    {
                        back[k]++;
                    }
                }
            }
        }

        foreach (var chunk in view.Scene.Static.Where(c => frustum.Intersects(c.Min, c.Max)))
        {
            Count(chunk.Mesh, Matrix4x4.Identity);
        }

        foreach (var item in view.Items)
        {
            if (renderer.Mesh(item.Item.Def) is { } gm)
            {
                var (wmin, wmax) = Frustum.Transform(gm.Mesh.Min, gm.Mesh.Max, item.Item.World);
                if (frustum.Intersects(wmin, wmax))
                {
                    Count(gm.Mesh, item.Item.World);
                }
            }
        }

        return new BackFaceCensus(name, total[(int)AlphaMode.Opaque], back[(int)AlphaMode.Opaque], total[(int)AlphaMode.Test], back[(int)AlphaMode.Test], total[(int)AlphaMode.Blend], back[(int)AlphaMode.Blend]);
    }

    private static Vector3 P(CpuMesh m, uint index)
    {
        var o = (int)index * CpuMesh.FloatsPerVertex;
        return new Vector3(m.Vertices[o], m.Vertices[o + 1], m.Vertices[o + 2]);
    }
}
