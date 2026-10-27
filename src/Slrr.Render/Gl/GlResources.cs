using System.Numerics;
using Silk.NET.OpenGL;
using Slrr.Formats.Graphics;
using Slrr.Render.Geometry;

namespace Slrr.Render.Gl;

/// <summary>A <see cref="CpuMesh"/> on the GPU: VAO + vertex/index buffers + a per-mesh instance buffer (mat4 at locations 5..8).</summary>
public sealed unsafe class GpuMesh : IDisposable
{
    private readonly GL _gl;
    private readonly uint _vbo;
    private readonly uint _ebo;
    private readonly uint _instances;
    private int _instanceCapacity;

    public GpuMesh(GL gl, CpuMesh mesh)
    {
        _gl = gl;
        Mesh = mesh;
        Vao = gl.GenVertexArray();
        gl.BindVertexArray(Vao);
        _vbo = gl.GenBuffer();
        gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vbo);
        gl.BufferData<float>(BufferTargetARB.ArrayBuffer, (ReadOnlySpan<float>)mesh.Vertices, BufferUsageARB.StaticDraw);
        _ebo = gl.GenBuffer();
        gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, _ebo);
        gl.BufferData<uint>(BufferTargetARB.ElementArrayBuffer, (ReadOnlySpan<uint>)mesh.Indices, BufferUsageARB.StaticDraw);
        uint stride = CpuMesh.StrideBytes;
        Attr(0, 3, 0);
        Attr(1, 3, 3);
        Attr(2, 2, 6);
        Attr(3, 2, 8);
        Attr(4, 4, 10);

        _instances = gl.GenBuffer();
        gl.BindBuffer(BufferTargetARB.ArrayBuffer, _instances);
        for (uint i = 0; i < 4; i++)
        {
            gl.VertexAttribPointer(5 + i, 4, VertexAttribPointerType.Float, false, 64, (void*)(i * 16));
            gl.EnableVertexAttribArray(5 + i);
            gl.VertexAttribDivisor(5 + i, 1);
        }

        gl.BindVertexArray(0);

        void Attr(uint loc, int size, int offsetFloats)
        {
            gl.VertexAttribPointer(loc, size, VertexAttribPointerType.Float, false, stride, (void*)(offsetFloats * sizeof(float)));
            gl.EnableVertexAttribArray(loc);
        }
    }

    public CpuMesh Mesh { get; }

    public uint Vao { get; }

    /// <summary>Uploads instance matrices (row-vector System.Numerics layout, read as 4 vec4 columns = GLSL M * v).</summary>
    public void SetInstances(ReadOnlySpan<Matrix4x4> worlds)
    {
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _instances);
        fixed (Matrix4x4* p = worlds)
        {
            var bytes = (nuint)(worlds.Length * 64);
            if (worlds.Length > _instanceCapacity)
            {
                _instanceCapacity = Math.Max(worlds.Length, 4);
                _gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(_instanceCapacity * 64), null, BufferUsageARB.DynamicDraw);
            }

            _gl.BufferSubData(BufferTargetARB.ArrayBuffer, 0, bytes, p);
        }
    }

    public void Dispose()
    {
        _gl.DeleteBuffer(_instances);
        _gl.DeleteBuffer(_ebo);
        _gl.DeleteBuffer(_vbo);
        _gl.DeleteVertexArray(Vao);
    }
}

/// <summary>GL textures by file path (mipmapped, repeat), plus 1x1 white and a fallback environment map.</summary>
public sealed unsafe class TextureCache : IDisposable
{
    private readonly GL _gl;
    private readonly Func<string, RgbaImage?> _load;
    private readonly Dictionary<string, uint> _byPath = new(StringComparer.OrdinalIgnoreCase);

    public TextureCache(GL gl, Func<string, RgbaImage?> load)
    {
        _gl = gl;
        _load = load;
        var white = new RgbaImage(1, 1);
        white.Set(0, 0, 255, 255, 255, 255);
        White = Upload(white);
    }

    public uint White { get; }

    public int Count => _byPath.Count;

    public long Bytes { get; private set; }

    /// <summary>GL name for a file; <see cref="White"/> for null or undecodable files.</summary>
    public uint Get(string? path)
    {
        if (path is null)
        {
            return White;
        }

        if (!_byPath.TryGetValue(path, out var id))
        {
            id = _load(path) is { } img ? Upload(img) : White;
            _byPath[path] = id;
        }

        return id;
    }

    public uint Upload(RgbaImage img)
    {
        ArgumentNullException.ThrowIfNull(img);
        var id = _gl.GenTexture();
        _gl.BindTexture(TextureTarget.Texture2D, id);
        _gl.PixelStore(PixelStoreParameter.UnpackAlignment, 1);
        fixed (byte* p = img.Pixels)
        {
            _gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.Rgba8, (uint)img.Width, (uint)img.Height, 0, PixelFormat.Rgba, PixelType.UnsignedByte, p);
        }

        _gl.GenerateMipmap(TextureTarget.Texture2D);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.LinearMipmapLinear);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.Repeat);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.Repeat);
        Bytes += img.Pixels.Length * 4L / 3;
        return id;
    }

    public void Dispose()
    {
        foreach (var id in _byPath.Values.Where(v => v != White).Distinct())
        {
            _gl.DeleteTexture(id);
        }

        _gl.DeleteTexture(White);
    }
}

/// <summary>Colour (RGBA8) + depth framebuffer for offscreen frames.</summary>
public sealed unsafe class OffscreenTarget : IDisposable
{
    private readonly GL _gl;
    private readonly uint _fbo;
    private readonly uint _color;
    private readonly uint _depth;

    public OffscreenTarget(GL gl, int width, int height)
    {
        _gl = gl;
        Width = width;
        Height = height;
        _fbo = gl.GenFramebuffer();
        gl.BindFramebuffer(FramebufferTarget.Framebuffer, _fbo);
        _color = gl.GenRenderbuffer();
        gl.BindRenderbuffer(RenderbufferTarget.Renderbuffer, _color);
        gl.RenderbufferStorage(RenderbufferTarget.Renderbuffer, InternalFormat.Rgba8, (uint)width, (uint)height);
        gl.FramebufferRenderbuffer(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, RenderbufferTarget.Renderbuffer, _color);
        _depth = gl.GenRenderbuffer();
        gl.BindRenderbuffer(RenderbufferTarget.Renderbuffer, _depth);
        gl.RenderbufferStorage(RenderbufferTarget.Renderbuffer, InternalFormat.Depth24Stencil8, (uint)width, (uint)height);
        gl.FramebufferRenderbuffer(FramebufferTarget.Framebuffer, FramebufferAttachment.DepthStencilAttachment, RenderbufferTarget.Renderbuffer, _depth);
        var status = gl.CheckFramebufferStatus(FramebufferTarget.Framebuffer);
        if (status != GLEnum.FramebufferComplete)
        {
            throw new InvalidOperationException($"framebuffer incomplete: {status}");
        }
    }

    public int Width { get; }

    public int Height { get; }

    /// <summary>GL name of the framebuffer object (for <c>glBlitFramebuffer</c> into a window).</summary>
    public uint Framebuffer => _fbo;

    public void Bind()
    {
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _fbo);
        _gl.Viewport(0, 0, (uint)Width, (uint)Height);
    }

    /// <summary>Reads the frame as a top-down <see cref="RgbaImage"/>.</summary>
    public RgbaImage Read()
    {
        var raw = new byte[Width * Height * 4];
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _fbo);
        _gl.PixelStore(PixelStoreParameter.PackAlignment, 1);
        fixed (byte* p = raw)
        {
            _gl.ReadPixels(0, 0, (uint)Width, (uint)Height, PixelFormat.Rgba, PixelType.UnsignedByte, p);
        }

        var img = new RgbaImage(Width, Height);
        var row = Width * 4;
        for (var y = 0; y < Height; y++)
        {
            System.Buffer.BlockCopy(raw, (Height - 1 - y) * row, img.Pixels, y * row, row);
        }

        return img;
    }

    public void Dispose()
    {
        _gl.DeleteRenderbuffer(_depth);
        _gl.DeleteRenderbuffer(_color);
        _gl.DeleteFramebuffer(_fbo);
    }
}
