using Silk.NET.OpenGL;

namespace Slrr.Viewer;

/// <summary>Colour (RGBA8) + depth framebuffer object for screenshots; independent of the window's surface.</summary>
public sealed unsafe class OffscreenTarget : IDisposable
{
    private readonly GL gl;
    private readonly uint fbo;
    private readonly uint color;
    private readonly uint depth;

    public OffscreenTarget(GL gl, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(gl);
        this.gl = gl;
        Width = width;
        Height = height;

        fbo = gl.GenFramebuffer();
        gl.BindFramebuffer(FramebufferTarget.Framebuffer, fbo);

        color = gl.GenRenderbuffer();
        gl.BindRenderbuffer(RenderbufferTarget.Renderbuffer, color);
        gl.RenderbufferStorage(RenderbufferTarget.Renderbuffer, InternalFormat.Rgba8, (uint)width, (uint)height);
        gl.FramebufferRenderbuffer(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, RenderbufferTarget.Renderbuffer, color);

        depth = gl.GenRenderbuffer();
        gl.BindRenderbuffer(RenderbufferTarget.Renderbuffer, depth);
        gl.RenderbufferStorage(RenderbufferTarget.Renderbuffer, InternalFormat.DepthComponent24, (uint)width, (uint)height);
        gl.FramebufferRenderbuffer(FramebufferTarget.Framebuffer, FramebufferAttachment.DepthAttachment, RenderbufferTarget.Renderbuffer, depth);

        var status = gl.CheckFramebufferStatus(FramebufferTarget.Framebuffer);
        if (status != GLEnum.FramebufferComplete)
        {
            throw new InvalidOperationException($"framebuffer incomplete: {status}");
        }

        gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
    }

    public int Width { get; }
    public int Height { get; }

    public void Bind()
    {
        gl.BindFramebuffer(FramebufferTarget.Framebuffer, fbo);
        gl.Viewport(0, 0, (uint)Width, (uint)Height);
    }

    /// <summary>Reads the colour attachment as bottom-up RGBA rows (GL order).</summary>
    public byte[] ReadPixels()
    {
        var pixels = new byte[Width * Height * 4];
        gl.BindFramebuffer(FramebufferTarget.Framebuffer, fbo);
        gl.PixelStore(PixelStoreParameter.PackAlignment, 1);
        fixed (byte* p = pixels)
        {
            gl.ReadPixels(0, 0, (uint)Width, (uint)Height, PixelFormat.Rgba, PixelType.UnsignedByte, p);
        }

        return pixels;
    }

    public void Dispose()
    {
        gl.DeleteRenderbuffer(depth);
        gl.DeleteRenderbuffer(color);
        gl.DeleteFramebuffer(fbo);
    }
}
