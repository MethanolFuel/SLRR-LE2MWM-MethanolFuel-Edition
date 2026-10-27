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
    private readonly bool _ownsWindow;

    private GlHost(IWindow window, GL gl, bool ownsWindow = true)
    {
        _window = window;
        Gl = gl;
        _ownsWindow = ownsWindow;
    }

    public GL Gl { get; }

    public string Version => Gl.GetStringS(StringName.Version) + " / " + Gl.GetStringS(StringName.Renderer);

    /// <summary>
    /// Wraps the current context of an application window (e.g. the interactive <c>slrr-app</c>) so the renderers draw into it. The window stays
    /// owned by the caller (<see cref="Dispose"/> does not close it); GL calls must stay on the thread that made the context current.
    /// </summary>
    public static GlHost FromWindow(IWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);
        window.MakeCurrent();
        return new GlHost(window, GL.GetApi(window), ownsWindow: false);
    }

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
        if (_ownsWindow)
        {
            _window.Dispose();
        }
    }
}
