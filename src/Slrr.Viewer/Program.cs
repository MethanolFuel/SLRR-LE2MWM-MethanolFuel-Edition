using System.Numerics;
using Silk.NET.Input;
using Silk.NET.Maths;
using Silk.NET.OpenGL;
using Silk.NET.Windowing;
using Slrr.Core;
using Slrr.Formats.Scx;

namespace Slrr.Viewer;

internal static class Program
{
    private static readonly Vector3 LightDir = Vector3.Normalize(new Vector3(0.4f, 0.8f, 0.45f));

    private static int Main(string[] args)
    {
        var options = ViewerOptions.Parse(args, out var error);
        if (options is null)
        {
            Console.Error.WriteLine(error);
            Console.Error.WriteLine(ViewerOptions.Usage);
            return 2;
        }

        MeshData mesh;
        try
        {
            mesh = MeshData.FromScx(ScxFile.Read(options.MeshPath));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or SlrrFormatException)
        {
            Console.Error.WriteLine($"{options.MeshPath}: {e.Message}");
            return 1;
        }

        Console.WriteLine(
            $"{options.MeshPath}: {mesh.SubMeshes.Count} meshes ({mesh.SkippedMeshes} skipped), {mesh.VertexCount} vertices, " +
            $"{mesh.TriangleCount} triangles, {mesh.GeneratedNormals} generated normals, bounds {mesh.BoundsMin} .. {mesh.BoundsMax}");

        try
        {
            return Run(options, mesh);
        }
        catch (Exception e) when (e is InvalidOperationException or PlatformNotSupportedException or Silk.NET.GLFW.GlfwException)
        {
            Console.Error.WriteLine($"OpenGL/windowing failed: {e.Message}");
            return 3;
        }
    }

    private static int Run(ViewerOptions options, MeshData mesh)
    {
        var screenshot = options.ScreenshotPath is not null;
        var windowOptions = WindowOptions.Default with
        {
            Size = new Vector2D<int>(options.Width, options.Height),
            Title = $"SLRR viewer - {Path.GetFileName(options.MeshPath)}",
            API = new GraphicsAPI(ContextAPI.OpenGL, ContextProfile.Core, ContextFlags.ForwardCompatible, new APIVersion(3, 3)),
            IsVisible = !screenshot,
            VSync = true,
        };

        using var window = Window.Create(windowOptions);
        GL? gl = null;
        MeshRenderer? renderer = null;
        IInputContext? input = null;
        var camera = NewCamera(options, mesh);
        var size = new Vector2D<int>(options.Width, options.Height);
        Vector2? lastMouse = null;
        var exitCode = 0;

        window.Load += () =>
        {
            gl = GL.GetApi(window);
            Console.WriteLine($"GL: {gl.GetStringS(StringName.Version)} / {gl.GetStringS(StringName.Renderer)}");
            renderer = new MeshRenderer(gl, mesh) { Wireframe = options.Wireframe };
            size = window.FramebufferSize;
            if (screenshot)
            {
                return;
            }

            input = window.CreateInput();
            foreach (var mouse in input.Mice)
            {
                mouse.MouseMove += (m, pos) =>
                {
                    if (m.IsButtonPressed(MouseButton.Left) && lastMouse is { } last)
                    {
                        var d = pos - last;
                        camera.Rotate(-d.X * 0.01f, d.Y * 0.01f);
                    }

                    lastMouse = pos;
                };
                mouse.Scroll += (_, wheel) => camera.Zoom(wheel.Y);
            }

            foreach (var kb in input.Keyboards)
            {
                kb.KeyDown += (_, key, _) =>
                {
                    switch (key)
                    {
                        case Key.Escape:
                            window.Close();
                            break;
                        case Key.W:
                            renderer.Wireframe = !renderer.Wireframe;
                            break;
                        case Key.R:
                            camera = NewCamera(options, mesh);
                            break;
                    }
                };
            }
        };

        window.FramebufferResize += s =>
        {
            size = s;
            gl?.Viewport(s);
        };

        window.Render += _ =>
        {
            if (gl is null || renderer is null)
            {
                return;
            }

            if (screenshot)
            {
                using var target = new OffscreenTarget(gl, options.Width, options.Height);
                target.Bind();
                renderer.Draw(camera, (float)options.Width / options.Height, LightDir);
                var png = PngWriter.Encode(target.ReadPixels(), options.Width, options.Height, flipVertically: true);
                File.WriteAllBytes(options.ScreenshotPath!, png);
                Console.WriteLine($"wrote {options.ScreenshotPath} ({options.Width}x{options.Height}, {png.Length} bytes)");
                window.Close();
                return;
            }

            gl.Viewport(size);
            renderer.Draw(camera, size.Y == 0 ? 1f : (float)size.X / size.Y, LightDir);
        };

        window.Closing += () =>
        {
            renderer?.Dispose();
            input?.Dispose();
        };

        try
        {
            window.Run();
        }
        catch (InvalidOperationException e)
        {
            Console.Error.WriteLine(e.Message);
            exitCode = 3;
        }

        return exitCode;
    }

    private static OrbitCamera NewCamera(ViewerOptions options, MeshData mesh)
    {
        var cam = OrbitCamera.Frame(mesh.Center, mesh.Radius);
        cam.Yaw = options.Yaw ?? cam.Yaw;
        cam.Pitch = options.Pitch ?? cam.Pitch;
        return cam;
    }
}
