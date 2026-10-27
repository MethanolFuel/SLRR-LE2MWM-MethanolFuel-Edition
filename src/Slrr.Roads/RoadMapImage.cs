using System.Globalization;
using System.Numerics;
using System.Text;
using Slrr.Formats.Graphics;
using Slrr.World;

namespace Slrr.Roads;

/// <summary>Top-down pictures of a road graph (+Z up, +X right), optionally over a <see cref="HeightMap"/> of the map's
/// collision ground: lanes (yellow 50, orange 100 speed value), turn paths (cyan), crossings (red, table A) / (magenta,
/// table B), a route (green) and extra paths such as <c>.spl</c> lines (white).</summary>
public sealed class RoadMapImage
{
    public RoadMapImage(RoadGraph? graph, float minX, float minZ, float maxX, float maxZ, float cell)
    {
        Graph = graph;
        Cell = cell;
        MinX = minX;
        MinZ = minZ;
        Width = Math.Max(1, (int)MathF.Ceiling((maxX - minX) / cell));
        Height = Math.Max(1, (int)MathF.Ceiling((maxZ - minZ) / cell));
    }

    public RoadGraph? Graph { get; }

    public float MinX { get; }

    public float MinZ { get; }

    public float Cell { get; }

    public int Width { get; }

    public int Height { get; }

    public HeightMap? Background { get; set; }

    public RoadRoute? Route { get; set; }

    public List<IRoadPath> Extra { get; } = [];

    /// <summary>Bounds of all lane curves plus a margin.</summary>
    public static (float MinX, float MinZ, float MaxX, float MaxZ) BoundsOf(RoadGraph g, float margin = 30)
    {
        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        foreach (var l in g.Lanes)
        {
            var (a, b) = l.Curve.Bounds();
            min = Vector3.Min(min, a);
            max = Vector3.Max(max, b);
        }

        return (min.X - margin, min.Z - margin, max.X + margin, max.Z + margin);
    }

    private (float X, float Y) Px(Vector3 p) => ((p.X - MinX) / Cell, Height - ((p.Z - MinZ) / Cell));

    private IEnumerable<(IRoadPath Path, uint Rgba)> Paths()
    {
        if (Graph is { } g)
        {
            foreach (var n in g.Nodes)
            {
                foreach (var c in n.Connectors)
                {
                    yield return (c.Curve, 0x40C0E0FFu);
                }
            }

            foreach (var l in g.Lanes)
            {
                yield return (l.Curve, l.SpeedLimit > 20 ? 0xFF9020FFu : 0xF0E040FFu);
            }
        }

        foreach (var e in Extra)
        {
            yield return (e, 0xFFFFFFFFu);
        }

        if (Route is { } r)
        {
            yield return (r, 0x30FF50FFu);
        }
    }

    private static IEnumerable<Vector3> Polyline(IRoadPath p, float step)
    {
        var n = Math.Max(2, (int)MathF.Ceiling(p.Length / step) + 1);
        for (var i = 0; i < n; i++)
        {
            yield return p.Sample(p.Length * i / (n - 1)).Position;
        }
    }

    public byte[] ToPng()
    {
        var img = new RgbaImage(Width, Height);
        var px = img.Pixels;
        for (var row = 0; row < Height; row++)
        {
            for (var col = 0; col < Width; col++)
            {
                var o = ((row * Width) + col) * 4;
                (byte R, byte G, byte B) c = (16, 16, 24);
                if (Background is { } bg)
                {
                    var x = MinX + ((col + 0.5f) * Cell);
                    var z = MinZ + ((Height - row - 0.5f) * Cell);
                    var bc = (int)((x - bg.MinX) / bg.Cell);
                    var br = (int)((z - bg.MinZ) / bg.Cell);
                    if (bc >= 0 && br >= 0 && bc < bg.Width && br < bg.Height)
                    {
                        var (r, g, b) = bg.Color(bc, br, false, null);
                        c = ((byte)(r / 2), (byte)(g / 2), (byte)(b / 2));
                    }
                }

                px[o] = c.R;
                px[o + 1] = c.G;
                px[o + 2] = c.B;
                px[o + 3] = 255;
            }
        }

        foreach (var (path, rgba) in Paths())
        {
            Vector3? prev = null;
            foreach (var q in Polyline(path, Cell))
            {
                if (prev is { } a)
                {
                    var w = path is RoadRoute ? 1 : 0; // the route is drawn 3 px wide over the lanes it follows
                    for (var dy = -w; dy <= w; dy++)
                    {
                        for (var dx = -w; dx <= w; dx++)
                        {
                            var (ax, ay) = Px(a);
                            var (bx, by) = Px(q);
                            Line(img, (ax + dx, ay + dy), (bx + dx, by + dy), rgba);
                        }
                    }
                }

                prev = q;
            }
        }

        if (Graph is { } gr)
        {
            foreach (var n in gr.Nodes)
            {
                var (x, y) = Px(n.Position);
                for (var dy = -1; dy <= 1; dy++)
                {
                    for (var dx = -1; dx <= 1; dx++)
                    {
                        Plot(img, (int)x + dx, (int)y + dy, n.Source.Table == 0 ? 0xFF3030FFu : 0xFF30FFFFu);
                    }
                }
            }
        }

        return PngCodec.Encode(img);
    }

    public string ToSvg()
    {
        var inv = CultureInfo.InvariantCulture;
        var sb = new StringBuilder();
        sb.Append(inv, $"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{Width}\" height=\"{Height}\" viewBox=\"0 0 {Width} {Height}\">\n");
        sb.Append(inv, $"<!-- x {MinX}..{MinX + (Width * Cell)} z {MinZ}..{MinZ + (Height * Cell)} cell {Cell} m; +Z up -->\n");
        sb.Append("<rect width=\"100%\" height=\"100%\" fill=\"#101018\"/>\n");
        if (Background is { } bg)
        {
            // the height map as an embedded PNG, placed in the same world frame
            var x0 = (bg.MinX - MinX) / Cell;
            var y0 = Height - ((bg.MinZ + (bg.Height * bg.Cell) - MinZ) / Cell);
            sb.Append(inv, $"<image x=\"{x0:0.##}\" y=\"{y0:0.##}\" width=\"{bg.Width * bg.Cell / Cell:0.##}\" height=\"{bg.Height * bg.Cell / Cell:0.##}\" opacity=\"0.5\" preserveAspectRatio=\"none\" href=\"data:image/png;base64,");
            sb.Append(Convert.ToBase64String(bg.ToPng()));
            sb.Append("\"/>\n");
        }

        foreach (var (path, rgba) in Paths())
        {
            sb.Append("<polyline fill=\"none\" stroke-width=\"0.8\" stroke=\"#").Append((rgba >> 8).ToString("x6", inv)).Append("\" points=\"");
            foreach (var q in Polyline(path, Cell * 2))
            {
                var (x, y) = Px(q);
                sb.Append(inv, $"{x:0.#},{y:0.#} ");
            }

            sb.Append("\"/>\n");
        }

        if (Graph is { } g)
        {
            foreach (var n in g.Nodes)
            {
                var (x, y) = Px(n.Position);
                sb.Append(inv, $"<circle cx=\"{x:0.#}\" cy=\"{y:0.#}\" r=\"2\" fill=\"{(n.Source.Table == 0 ? "#ff3030" : "#ff30ff")}\"><title>node {n.Id}</title></circle>\n");
            }
        }

        sb.Append("</svg>\n");
        return sb.ToString();
    }

    private static void Line(RgbaImage img, (float X, float Y) a, (float X, float Y) b, uint rgba)
    {
        var steps = (int)MathF.Ceiling(MathF.Max(MathF.Abs(b.X - a.X), MathF.Abs(b.Y - a.Y))) + 1;
        for (var i = 0; i <= steps; i++)
        {
            var t = i / (float)steps;
            Plot(img, (int)(a.X + ((b.X - a.X) * t)), (int)(a.Y + ((b.Y - a.Y) * t)), rgba);
        }
    }

    private static void Plot(RgbaImage img, int x, int y, uint rgba)
    {
        if (x < 0 || y < 0 || x >= img.Width || y >= img.Height)
        {
            return;
        }

        var o = ((y * img.Width) + x) * 4;
        img.Pixels[o] = (byte)(rgba >> 24);
        img.Pixels[o + 1] = (byte)(rgba >> 16);
        img.Pixels[o + 2] = (byte)(rgba >> 8);
        img.Pixels[o + 3] = 255;
    }
}
