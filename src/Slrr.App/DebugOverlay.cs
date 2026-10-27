using System.Numerics;
using Silk.NET.OpenGL;

namespace Slrr.App;

/// <summary>A 5x7 bitmap font for the FPS overlay (no game data needed, so it also shows "LOADING" and errors before boot).</summary>
public static class BitmapFont
{
    public const int GlyphW = 5;
    public const int GlyphH = 7;

    // Rows top to bottom, bit 4 = leftmost column (classic HD44780-style 5x7 shapes).
    private static readonly Dictionary<char, byte[]> Glyphs = new()
    {
        ['0'] = [0x0E, 0x11, 0x13, 0x15, 0x19, 0x11, 0x0E], ['1'] = [0x04, 0x0C, 0x04, 0x04, 0x04, 0x04, 0x0E],
        ['2'] = [0x0E, 0x11, 0x01, 0x02, 0x04, 0x08, 0x1F], ['3'] = [0x1F, 0x02, 0x04, 0x02, 0x01, 0x11, 0x0E],
        ['4'] = [0x02, 0x06, 0x0A, 0x12, 0x1F, 0x02, 0x02], ['5'] = [0x1F, 0x10, 0x1E, 0x01, 0x01, 0x11, 0x0E],
        ['6'] = [0x06, 0x08, 0x10, 0x1E, 0x11, 0x11, 0x0E], ['7'] = [0x1F, 0x01, 0x02, 0x04, 0x08, 0x08, 0x08],
        ['8'] = [0x0E, 0x11, 0x11, 0x0E, 0x11, 0x11, 0x0E], ['9'] = [0x0E, 0x11, 0x11, 0x0F, 0x01, 0x02, 0x0C],
        ['A'] = [0x0E, 0x11, 0x11, 0x11, 0x1F, 0x11, 0x11], ['B'] = [0x1E, 0x11, 0x11, 0x1E, 0x11, 0x11, 0x1E],
        ['C'] = [0x0E, 0x11, 0x10, 0x10, 0x10, 0x11, 0x0E], ['D'] = [0x1C, 0x12, 0x11, 0x11, 0x11, 0x12, 0x1C],
        ['E'] = [0x1F, 0x10, 0x10, 0x1E, 0x10, 0x10, 0x1F], ['F'] = [0x1F, 0x10, 0x10, 0x1E, 0x10, 0x10, 0x10],
        ['G'] = [0x0E, 0x11, 0x10, 0x17, 0x11, 0x11, 0x0F], ['H'] = [0x11, 0x11, 0x11, 0x1F, 0x11, 0x11, 0x11],
        ['I'] = [0x0E, 0x04, 0x04, 0x04, 0x04, 0x04, 0x0E], ['J'] = [0x07, 0x02, 0x02, 0x02, 0x02, 0x12, 0x0C],
        ['K'] = [0x11, 0x12, 0x14, 0x18, 0x14, 0x12, 0x11], ['L'] = [0x10, 0x10, 0x10, 0x10, 0x10, 0x10, 0x1F],
        ['M'] = [0x11, 0x1B, 0x15, 0x15, 0x11, 0x11, 0x11], ['N'] = [0x11, 0x11, 0x19, 0x15, 0x13, 0x11, 0x11],
        ['O'] = [0x0E, 0x11, 0x11, 0x11, 0x11, 0x11, 0x0E], ['P'] = [0x1E, 0x11, 0x11, 0x1E, 0x10, 0x10, 0x10],
        ['Q'] = [0x0E, 0x11, 0x11, 0x11, 0x15, 0x12, 0x0D], ['R'] = [0x1E, 0x11, 0x11, 0x1E, 0x14, 0x12, 0x11],
        ['S'] = [0x0F, 0x10, 0x10, 0x0E, 0x01, 0x01, 0x1E], ['T'] = [0x1F, 0x04, 0x04, 0x04, 0x04, 0x04, 0x04],
        ['U'] = [0x11, 0x11, 0x11, 0x11, 0x11, 0x11, 0x0E], ['V'] = [0x11, 0x11, 0x11, 0x11, 0x11, 0x0A, 0x04],
        ['W'] = [0x11, 0x11, 0x11, 0x15, 0x15, 0x15, 0x0A], ['X'] = [0x11, 0x11, 0x0A, 0x04, 0x0A, 0x11, 0x11],
        ['Y'] = [0x11, 0x11, 0x11, 0x0A, 0x04, 0x04, 0x04], ['Z'] = [0x1F, 0x01, 0x02, 0x04, 0x08, 0x10, 0x1F],
        ['.'] = [0x00, 0x00, 0x00, 0x00, 0x00, 0x0C, 0x0C], [','] = [0x00, 0x00, 0x00, 0x00, 0x0C, 0x04, 0x08],
        [':'] = [0x00, 0x0C, 0x0C, 0x00, 0x0C, 0x0C, 0x00], ['/'] = [0x00, 0x01, 0x02, 0x04, 0x08, 0x10, 0x00],
        ['-'] = [0x00, 0x00, 0x00, 0x1F, 0x00, 0x00, 0x00], ['+'] = [0x00, 0x04, 0x04, 0x1F, 0x04, 0x04, 0x00],
        ['='] = [0x00, 0x00, 0x1F, 0x00, 0x1F, 0x00, 0x00], ['('] = [0x02, 0x04, 0x08, 0x08, 0x08, 0x04, 0x02],
        [')'] = [0x08, 0x04, 0x02, 0x02, 0x02, 0x04, 0x08], ['%'] = [0x18, 0x19, 0x02, 0x04, 0x08, 0x13, 0x03],
        ['_'] = [0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x1F], ['|'] = [0x04, 0x04, 0x04, 0x04, 0x04, 0x04, 0x04],
        ['['] = [0x0E, 0x08, 0x08, 0x08, 0x08, 0x08, 0x0E], [']'] = [0x0E, 0x02, 0x02, 0x02, 0x02, 0x02, 0x0E],
        ['\''] = [0x04, 0x04, 0x08, 0x00, 0x00, 0x00, 0x00], ['!'] = [0x04, 0x04, 0x04, 0x04, 0x04, 0x00, 0x04],
        ['?'] = [0x0E, 0x11, 0x01, 0x02, 0x04, 0x00, 0x04], ['>'] = [0x08, 0x04, 0x02, 0x01, 0x02, 0x04, 0x08],
        ['<'] = [0x02, 0x04, 0x08, 0x10, 0x08, 0x04, 0x02], ['#'] = [0x0A, 0x0A, 0x1F, 0x0A, 0x1F, 0x0A, 0x0A],
    };

    /// <summary>Lit pixels of a glyph (column, row), unknown characters as '?'. Lower case is drawn as upper case.</summary>
    public static IEnumerable<(int X, int Y)> Pixels(char c)
    {
        if (c == ' ')
        {
            yield break;
        }

        var rows = Glyphs.TryGetValue(char.ToUpperInvariant(c), out var g) ? g : Glyphs['?'];
        for (var y = 0; y < GlyphH; y++)
        {
            for (var x = 0; x < GlyphW; x++)
            {
                if ((rows[y] & (0x10 >> x)) != 0)
                {
                    yield return (x, y);
                }
            }
        }
    }
}

/// <summary>
/// Draws text boxes over the presented frame (FPS/frame-time overlay, PAUSED, LOADING, errors) into the window's framebuffer.
/// GLSL 330 core, one VAO/VBO, no textures: legal on macOS core profiles (forward-compatible 3.3+).
/// </summary>
public sealed unsafe class DebugOverlay : IDisposable
{
    private const string Vertex = """
        #version 330 core
        layout(location = 0) in vec2 aPos;
        layout(location = 1) in vec4 aColor;
        uniform vec2 uScreen;
        out vec4 vColor;
        void main()
        {
            vColor = aColor;
            gl_Position = vec4(aPos.x / uScreen.x * 2.0 - 1.0, 1.0 - aPos.y / uScreen.y * 2.0, 0.0, 1.0);
        }
        """;

    private const string Fragment = """
        #version 330 core
        in vec4 vColor;
        out vec4 FragColor;
        void main()
        {
            FragColor = vColor;
        }
        """;

    private readonly GL _gl;
    private readonly Slrr.Render.Gl.GlProgram _program;
    private readonly uint _vao;
    private readonly uint _vbo;
    private readonly List<float> _data = [];

    public DebugOverlay(GL gl)
    {
        _gl = gl;
        _program = new Slrr.Render.Gl.GlProgram(gl, Vertex, Fragment);
        _vao = gl.GenVertexArray();
        gl.BindVertexArray(_vao);
        _vbo = gl.GenBuffer();
        gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vbo);
        gl.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, 24, (void*)0);
        gl.EnableVertexAttribArray(0);
        gl.VertexAttribPointer(1, 4, VertexAttribPointerType.Float, false, 24, (void*)8);
        gl.EnableVertexAttribArray(1);
        gl.BindVertexArray(0);
    }

    /// <summary>Queues a block of lines at a framebuffer position (top-left origin) with a translucent background.</summary>
    public void Text(IReadOnlyList<string> lines, Vector2 at, float pixel, Vector4 color, bool center = false)
    {
        if (lines.Count == 0)
        {
            return;
        }

        var advance = (BitmapFont.GlyphW + 1) * pixel;
        var lineH = (BitmapFont.GlyphH + 3) * pixel;
        var width = lines.Max(l => l.Length) * advance;
        var height = lines.Count * lineH;
        if (center)
        {
            at -= new Vector2(width / 2, height / 2);
        }

        Quad(at - new Vector2(pixel * 3), at + new Vector2(width + (pixel * 2), height + pixel), new Vector4(0, 0, 0, 0.6f));
        for (var li = 0; li < lines.Count; li++)
        {
            var x = at.X;
            var y = at.Y + (li * lineH) + pixel;
            foreach (var c in lines[li])
            {
                foreach (var (px, py) in BitmapFont.Pixels(c))
                {
                    var p0 = new Vector2(x + (px * pixel), y + (py * pixel));
                    Quad(p0, p0 + new Vector2(pixel), color);
                }

                x += advance;
            }
        }
    }

    private void Quad(Vector2 a, Vector2 b, Vector4 c)
    {
        void V(float x, float y)
        {
            _data.Add(x);
            _data.Add(y);
            _data.Add(c.X);
            _data.Add(c.Y);
            _data.Add(c.Z);
            _data.Add(c.W);
        }

        V(a.X, a.Y);
        V(b.X, a.Y);
        V(a.X, b.Y);
        V(a.X, b.Y);
        V(b.X, a.Y);
        V(b.X, b.Y);
    }

    /// <summary>Draws everything queued into the bound framebuffer of size <paramref name="width"/> x <paramref name="height"/>, then clears the queue.</summary>
    public void Flush(int width, int height)
    {
        if (_data.Count == 0)
        {
            return;
        }

        _gl.Viewport(0, 0, (uint)width, (uint)height);
        _gl.Disable(EnableCap.DepthTest);
        _gl.Disable(EnableCap.CullFace);
        _gl.Disable(EnableCap.ScissorTest);
        _gl.Enable(EnableCap.Blend);
        _gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
        _program.Use();
        _program.Set("uScreen", new Vector2(width, height));
        _gl.BindVertexArray(_vao);
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vbo);
        var span = System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_data);
        _gl.BufferData<float>(BufferTargetARB.ArrayBuffer, (ReadOnlySpan<float>)span, BufferUsageARB.StreamDraw);
        _gl.DrawArrays(PrimitiveType.Triangles, 0, (uint)(_data.Count / 6));
        _gl.BindVertexArray(0);
        _gl.Disable(EnableCap.Blend);
        _data.Clear();
    }

    public void Dispose()
    {
        _gl.DeleteBuffer(_vbo);
        _gl.DeleteVertexArray(_vao);
        _program.Dispose();
    }
}
