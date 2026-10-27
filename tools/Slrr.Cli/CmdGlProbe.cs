using System.Runtime.InteropServices;
using Silk.NET.Maths;
using Silk.NET.OpenGL;
using Silk.NET.Windowing;

namespace Slrr.Cli;

/// <summary>
/// <c>gl-probe</c>: checks, stage by stage, whether this machine can create the OpenGL 3.3 core context the renderer and
/// <c>slrr-app</c> need. Every stage prints (and flushes) before and after, so if the process dies in native code the
/// last line names the failing stage. Useful on a new platform (macOS) and in CI.
/// </summary>
public static class CmdGlProbe
{
    public const string Usage = """
        gl-probe [--no-window]
          Prints OS, architecture, GLFW version, then creates a hidden 3.3 core forward-compatible window, makes it current and
          reads the GL strings. --no-window stops after the GLFW stage.
        """;

    public static int Run(string[] args, TextWriter o, TextWriter e)
    {
        if (args.Contains("--help") || args.Contains("-h"))
        {
            o.WriteLine(Usage);
            return 0;
        }

        void Say(string s)
        {
            o.WriteLine(s);
            o.Flush();
        }

        Say($"os: {RuntimeInformation.OSDescription}; arch: {RuntimeInformation.OSArchitecture}; process: {RuntimeInformation.ProcessArchitecture}; runtime: {RuntimeInformation.FrameworkDescription}");
        Say("stage 1: load GLFW");
        try
        {
            var glfw = Silk.NET.GLFW.Glfw.GetApi();
            Say("stage 1: GLFW loaded");
            if (!glfw.Init())
            {
                e.WriteLine("glfw.Init() returned false");
                return 3;
            }

            Say($"stage 1: GLFW initialised, version {glfw.GetVersionString()}");
            glfw.Terminate();
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        {
            e.WriteLine($"GLFW could not be loaded: {ex.GetType().Name}: {ex.Message}");
            return 3;
        }

        if (args.Contains("--no-window"))
        {
            Say("done (no window)");
            return 0;
        }

        IWindow? window = null;
        try
        {
            Say("stage 2: create window (hidden, GL 3.3 core, forward compatible)");
            window = Window.Create(WindowOptions.Default with
            {
                Size = new Vector2D<int>(64, 64),
                Title = "slrr gl-probe",
                API = new GraphicsAPI(ContextAPI.OpenGL, ContextProfile.Core, ContextFlags.ForwardCompatible, new APIVersion(3, 3)),
                IsVisible = false,
                VSync = false,
            });
            Say("stage 2: window object created");
            Say("stage 3: initialise window (creates the native window and context)");
            window.Initialize();
            Say("stage 3: window initialised");
            Say("stage 4: make context current");
            window.MakeCurrent();
            Say("stage 4: context current");
            Say("stage 5: load GL entry points");
            var gl = GL.GetApi(window);
            Say("stage 5: GL api loaded");
            Say("stage 6: read GL strings");
            Say($"  GL_VERSION  {gl.GetStringS(StringName.Version)}");
            Say($"  GL_RENDERER {gl.GetStringS(StringName.Renderer)}");
            Say($"  GL_VENDOR   {gl.GetStringS(StringName.Vendor)}");
            Say($"  GLSL        {gl.GetStringS(StringName.ShadingLanguageVersion)}");
            Say("stage 7: clear and read back one pixel");
            gl.ClearColor(0.25f, 0.5f, 0.75f, 1f);
            gl.Clear(ClearBufferMask.ColorBufferBit);
            var px = new byte[4];
            gl.ReadPixels(0, 0, 1, 1, PixelFormat.Rgba, PixelType.UnsignedByte, px.AsSpan());
            Say($"  pixel {px[0]},{px[1]},{px[2]},{px[3]}  glGetError {gl.GetError()}");
            Say("done: OpenGL works");
            return 0;
        }
        catch (Exception ex) when (ex is InvalidOperationException or PlatformNotSupportedException or Silk.NET.GLFW.GlfwException or DllNotFoundException)
        {
            e.WriteLine($"OpenGL failed: {ex.GetType().Name}: {ex.Message}");
            return 3;
        }
        finally
        {
            window?.Dispose();
        }
    }
}
