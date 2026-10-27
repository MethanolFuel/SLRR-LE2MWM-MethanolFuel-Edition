using Silk.NET.Maths;
using Silk.NET.OpenGL;
using Silk.NET.Windowing;

namespace Slrr.Render.Gl;

/// <summary>
/// A hidden GLFW window that owns an OpenGL 3.3 core context, for offscreen rendering (tests, CLI screenshots).
/// Headless Linux: run under <c>xvfb-run -a</c> with <c>LIBGL_ALWAYS_SOFTWARE=1</c> (Mesa llvmpipe). Without a display
/// <see cref="TryCreate"/> returns null and the reason.
/// </summary>
public sealed class GlHost : IDisposable
{
    private readonly IWindow _window;

    private GlHost(IWindow window, GL gl)
    {
        _window = window;
        Gl = gl;
    }

    public GL Gl { get; }

    public string Version => Gl.GetStringS(StringName.Version) + " / " + Gl.GetStringS(StringName.Renderer);

    public static GlHost? TryCreate(out string? error)
    {
        error = null;
        IWindow? window = null;
        try
        {
            var options = WindowOptions.Default with
            {
                Size = new Vector2D<int>(64, 64),
                Title = "slrr-render",
                API = new GraphicsAPI(ContextAPI.OpenGL, ContextProfile.Core, ContextFlags.ForwardCompatible, new APIVersion(3, 3)),
                IsVisible = false,
                VSync = false,
            };
            window = Window.Create(options);
            window.Initialize();
            window.MakeCurrent();
            return new GlHost(window, GL.GetApi(window));
        }
        catch (Exception e) when (e is InvalidOperationException or PlatformNotSupportedException or Silk.NET.GLFW.GlfwException or DllNotFoundException)
        {
            error = e.Message;
            window?.Dispose();
            return null;
        }
    }

    public void Dispose()
    {
        _window.Dispose();
    }
}
