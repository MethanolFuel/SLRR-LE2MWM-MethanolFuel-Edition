using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using Silk.NET.Input;
using Silk.NET.Maths;
using Silk.NET.OpenGL;
using Silk.NET.Windowing;
using Slrr.Formats.Graphics;
using Slrr.Race;
using Slrr.Render.Gl;

namespace Slrr.App;

/// <summary>
/// The interactive app: a resizable Silk.NET/GLFW window with an OpenGL 3.3 core forward-compatible context (the only kind macOS offers
/// beyond 2.1), the game drawn by <c>Slrr.Game.Render</c> into an offscreen target and blitted (letterboxed) into the window, keyboard,
/// mouse and gamepad input through <see cref="InputRouter"/>, sound through OpenAL. Loop per display frame: poll events → script/router →
/// fixed host steps (<see cref="FixedStepClock"/>) → redraw the scene if the host advanced → blit + overlay → GL error check → swap
/// (VSync paces it). All GL and windowing calls stay on the main thread (required by Cocoa on macOS).
/// </summary>
public sealed class WindowRunner(AppOptions options, string root, string saveRoot, AppLog log)
{
    private readonly List<InputEvent> _events = [];
    private readonly FrameTimes _frameMs = new();
    private readonly FrameTimes _drawMs = new();
    private readonly FrameTimes _stepMs = new();
    private readonly List<string> _glErrors = [];
    private IWindow? _window;
    private GL? _gl;
    private DebugOverlay? _overlay;
    private bool _showOverlay = true;
    private bool _paused;
    private Vector2D<int> _windowedSize;
    private Vector2D<int> _windowedPos;
    private string? _fatal;
    private int _screenshots;

    public int GlErrorCount { get; private set; }

    public static WindowOptions WindowOptionsFor(AppOptions o, bool debugContext = false) => WindowOptions.Default with
    {
        Size = new Vector2D<int>(o.Width, o.Height),
        Title = "Street Legal Racing: Redline (C# engine)",
        // macOS: a 3.3+ context must be core + forward-compatible (no compatibility profile exists there); the same flags work on Windows/Linux.
        API = new GraphicsAPI(ContextAPI.OpenGL, ContextProfile.Core,
            debugContext && !OperatingSystem.IsMacOS() ? ContextFlags.ForwardCompatible | ContextFlags.Debug : ContextFlags.ForwardCompatible, new APIVersion(3, 3)),
        VSync = o.VSync,
        WindowBorder = WindowBorder.Resizable,
        WindowState = o.Fullscreen ? WindowState.Fullscreen : WindowState.Normal,
        ShouldSwapAutomatically = false,
        IsEventDriven = false,
        PreferredDepthBufferBits = 0, // the scene has its own depth buffer in the offscreen target
        PreferredStencilBufferBits = 0,
        Samples = 0,
    };

    public int Run()
    {
        try
        {
            _window = Window.Create(WindowOptionsFor(options));
            _window.Initialize();
        }
        catch (Exception e) when (e is InvalidOperationException or PlatformNotSupportedException or Silk.NET.GLFW.GlfwException or DllNotFoundException)
        {
            log.Info($"cannot open a window with an OpenGL 3.3 core context: {e.Message}");
            log.Info(OperatingSystem.IsLinux()
                ? "Linux: run inside a desktop session (X11 or Wayland) with working OpenGL drivers, or headless: xvfb-run -a with LIBGL_ALWAYS_SOFTWARE=1, or --headless-frames n."
                : "update the graphics driver (OpenGL 3.3 is required), or use --headless-frames n for a run without a window.");
            _window?.Dispose();
            return 3;
        }

        try
        {
            return RunWindow(_window);
        }
        finally
        {
            _overlay?.Dispose();
            _window.Reset();
            _window.Dispose();
        }
    }

    private int RunWindow(IWindow window)
    {
        var glHost = GlHost.FromWindow(window);
        _gl = glHost.Gl;
        var gl = _gl;
        log.Info($"GL: {glHost.Version}; GLSL {gl.GetStringS(StringName.ShadingLanguageVersion)}; max texture {gl.GetInteger(GetPName.MaxTextureSize)}");
        log.Info($"window {window.Size.X}x{window.Size.Y}, framebuffer {window.FramebufferSize.X}x{window.FramebufferSize.Y}, vsync {options.VSync}");
        _overlay = new DebugOverlay(gl);
        _windowedSize = window.Size;
        _windowedPos = window.Position;

        // A first frame so the window is not blank while the scripts boot.
        Present(window, null, ["LOADING " + Path.GetFileName(root.TrimEnd('/', '\\')) + " ..."]);
        window.SwapBuffers();

        var input = window.CreateInput();
        HookInput(input);
        window.FocusChanged += focused =>
        {
            if (!focused)
            {
                _events.Add(new InputEvent(InputKind.KeyUp, Key.Unknown)); // marker: release everything below
            }
        };

        var mapper = MakeMapper(window);
        var audioOut = MakeAudio();
        using var race = RaceHost.Create(root, new RaceHostOptions
        {
            Render = true,
            RenderHost = glHost,
            RenderFinishEachFrame = false,
            Width = mapper.RenderSize.W,
            Height = mapper.RenderSize.H,
            CaptureEvery = 0,
            DrawOnlyOnCapture = true, // the host frame only ticks the renderer; the window draws once per displayed frame
            Audio = options.Audio || options.WavPath is not null,
            AudioOutput = audioOut,
            WavPath = options.WavPath,
            SaveRoot = saveRoot,
            FrameTime = 1f / options.HostHz,
        });
        using var session = new GameSession(race, log) { Mapper = mapper };
        if (options.InputScript is { } spec)
        {
            session.Script = InputScript.Load(spec);
            log.Info($"input script {spec}: {session.Script.Steps.Count} steps");
        }

        if (!session.Boot())
        {
            _fatal = "BOOT FAILED - SEE THE LOG";
        }

        var renderer = race.Renderer!;
        var clock = new FixedStepClock(1.0 / options.HostHz);
        var wall = Stopwatch.StartNew();
        var last = wall.Elapsed.TotalSeconds;
        var lastTitle = 0.0;
        var drawnFrame = -1L;
        var captureDir = options.OutDir ?? Path.Combine(AppPaths.UserDataDir(Environment.GetEnvironmentVariable), "screenshots");
        var exit = 0;
        while (!window.IsClosing)
        {
            var frameStart = wall.Elapsed.TotalSeconds;
            window.DoEvents();
            if (window.IsClosing)
            {
                break;
            }

            PollGamepads(input);
            if (_events.Any(e => e.Kind == InputKind.KeyUp && e.Key == Key.Unknown))
            {
                _events.RemoveAll(e => e.Kind == InputKind.KeyUp && e.Key == Key.Unknown);
                session.Router.ReleaseAll();
            }

            mapper = MakeMapper(window);
            session.Mapper = mapper;
            var commands = _fatal is null ? session.ProcessInput(_events) : [];
            _events.Clear();
            var shots = 0;
            foreach (var c in commands)
            {
                switch (c)
                {
                    case AppCommand.ToggleOverlay:
                        _showOverlay = !_showOverlay;
                        break;
                    case AppCommand.TogglePause:
                        _paused = !_paused;
                        session.Router.GameInputEnabled = !_paused;
                        if (_paused)
                        {
                            session.Router.ReleaseAll();
                            audioOut?.Flush();
                        }

                        clock.Reset();
                        log.Info(_paused ? "paused" : "resumed");
                        break;
                    case AppCommand.ToggleFullscreen:
                        ToggleFullscreen(window);
                        break;
                    case AppCommand.Screenshot:
                        shots++;
                        break;
                }
            }

            // Host steps.
            var now = wall.Elapsed.TotalSeconds;
            var steps = _paused || _fatal is not null ? 0 : options.Lockstep ? 1 : clock.Advance(now - last);
            last = now;
            for (var i = 0; i < steps; i++)
            {
                var s0 = wall.Elapsed.TotalMilliseconds;
                try
                {
                    session.Step();
                }
                catch (Exception e) when (e is not OutOfMemoryException)
                {
                    _fatal = "ENGINE ERROR - SEE THE LOG";
                    log.Info("engine error: " + e);
                    break;
                }

                _stepMs.Add(wall.Elapsed.TotalMilliseconds - s0);
                if (options.CaptureEvery > 0 && session.HostFrames % options.CaptureEvery == 0)
                {
                    session.Capture(string.Create(CultureInfo.InvariantCulture, $"frame-{session.HostFrames:D6}"));
                }
            }

            // Draw the scene when the host advanced (or the target changed size), present every displayed frame.
            var fb = window.FramebufferSize;
            if (fb.X <= 0 || fb.Y <= 0)
            {
                log.Debug($"framebuffer {fb.X}x{fb.Y}: not drawing (minimised)");
                Thread.Sleep(16); // minimised
                continue;
            }

            var (rw, rh) = mapper.RenderSize;
            if (rw != renderer.Width || rh != renderer.Height)
            {
                renderer.Resize(rw, rh);
                drawnFrame = -1;
            }

            if (drawnFrame != session.Host.Frame && session.Host.Booted)
            {
                var d0 = wall.Elapsed.TotalMilliseconds;
                renderer.DrawNow();
                _drawMs.Add(wall.Elapsed.TotalMilliseconds - d0);
                drawnFrame = session.Host.Frame;
            }

            Present(window, session.Host.Booted ? renderer : null, OverlayLines(session, audioOut));
            foreach (var name in session.TakeCaptures())
            {
                Screenshot(Path.Combine(captureDir, name + ".png"));
            }

            for (var i = 0; i < shots; i++)
            {
                Screenshot(Path.Combine(captureDir, string.Create(CultureInfo.InvariantCulture, $"slrr-{DateTime.Now:yyyyMMdd-HHmmss}-{++_screenshots}.png")));
            }

            CheckGlErrors(session.Host.Frame);
            window.SwapBuffers();

            var end = wall.Elapsed.TotalSeconds;
            _frameMs.Add((end - frameStart) * 1000);
            if (end - lastTitle > 0.5)
            {
                lastTitle = end;
                window.Title = string.Create(CultureInfo.InvariantCulture, $"SLRR (C# engine) - {_frameMs.Fps:F0} fps{(_paused ? " - paused" : "")}");
            }

            if (!options.VSync && options.MaxFps > 0 && !options.Lockstep)
            {
                var budget = 1.0 / options.MaxFps - (wall.Elapsed.TotalSeconds - frameStart);
                if (budget > 0.001)
                {
                    Thread.Sleep(TimeSpan.FromSeconds(budget));
                }
            }

            if (session.Script is { } script && (script.Failed || script.QuitRequested || script.Done))
            {
                exit = script.Failed ? 1 : 0;
                log.Info(script.Failed ? $"input script FAILED: {script.Error}" : "input script finished");
                break;
            }

            if (options.QuitAfter > 0 && wall.Elapsed.TotalSeconds >= options.QuitAfter)
            {
                log.Info("--quit-after reached");
                break;
            }
        }

        Report(session, audioOut, clock);
        if (session.Script is not null && GlErrorCount > 0)
        {
            log.Info("GL errors in a scripted run: failing");
            exit = exit == 0 ? 1 : exit;
        }

        return _fatal is not null && exit == 0 ? 1 : exit;
    }

    private ViewportMapper MakeMapper(IWindow w) =>
        new(new Vector2(w.Size.X, w.Size.Y), new Vector2(w.FramebufferSize.X, w.FramebufferSize.Y), options.Aspect, options.RenderScale);

    private RealtimeAudioSink? MakeAudio()
    {
        if (!options.Audio || options.WavPath is not null)
        {
            log.Info(options.WavPath is not null ? $"audio: written to {options.WavPath}" : "audio: off (--no-audio)");
            return null;
        }

        var sink = RealtimeAudioSink.TryCreate(44100, log.Info);
        log.Info(sink is null ? "audio: no OpenAL device, sound is mixed and discarded (NullSink)" : $"audio: {sink.Backend}");
        return sink;
    }

    private void HookInput(IInputContext input)
    {
        foreach (var kb in input.Keyboards)
        {
            kb.KeyDown += (_, k, _) => _events.Add(InputEvent.Down(k));
            kb.KeyUp += (_, k, _) => _events.Add(InputEvent.Up(k));
            kb.KeyChar += (_, c) => _events.Add(InputEvent.Typed(c));
        }

        foreach (var m in input.Mice)
        {
            m.MouseMove += (_, p) => _events.Add(InputEvent.Move(p));
            m.MouseDown += (mouse, b) => _events.Add(InputEvent.Press(mouse.Position, b));
            m.MouseUp += (mouse, b) => _events.Add(InputEvent.Release(mouse.Position, b));
        }

        input.ConnectionChanged += (device, connected) => log.Info($"input device {(connected ? "connected" : "disconnected")}: {device.Name}");
        foreach (var g in input.Gamepads)
        {
            log.Info($"gamepad: {g.Name}");
        }
    }

    private readonly Dictionary<int, float> _padState = [];

    /// <summary>The first connected gamepad, polled; events only for changes.</summary>
    private void PollGamepads(IInputContext input)
    {
        var pad = input.Gamepads.FirstOrDefault(g => g.IsConnected);
        if (pad is null)
        {
            return;
        }

        void Axis(PadAxis a, float v)
        {
            var key = (int)a;
            if (Math.Abs(_padState.GetValueOrDefault(key, float.NaN) - v) > 0.01f || !_padState.ContainsKey(key))
            {
                _padState[key] = v;
                _events.Add(InputEvent.Axis(a, v));
            }
        }

        if (pad.Thumbsticks.Count > 0)
        {
            Axis(PadAxis.LeftX, pad.Thumbsticks[0].X);
            Axis(PadAxis.LeftY, pad.Thumbsticks[0].Y);
        }

        if (pad.Triggers.Count > 1)
        {
            Axis(PadAxis.LeftTrigger, pad.Triggers[0].Position);
            Axis(PadAxis.RightTrigger, pad.Triggers[1].Position);
        }

        foreach (var b in pad.Buttons)
        {
            PadButton? mapped = b.Name switch
            {
                ButtonName.A => PadButton.A,
                ButtonName.B => PadButton.B,
                ButtonName.X => PadButton.X,
                ButtonName.Y => PadButton.Y,
                ButtonName.Start => PadButton.Start,
                ButtonName.Back => PadButton.Back,
                ButtonName.DPadUp => PadButton.DPadUp,
                ButtonName.DPadDown => PadButton.DPadDown,
                ButtonName.DPadLeft => PadButton.DPadLeft,
                ButtonName.DPadRight => PadButton.DPadRight,
                _ => null,
            };
            if (mapped is { } m)
            {
                var key = 100 + (int)m;
                var v = b.Pressed ? 1f : 0f;
                if (_padState.GetValueOrDefault(key) != v)
                {
                    _padState[key] = v;
                    _events.Add(InputEvent.PadKey(m, b.Pressed));
                }
            }
        }
    }

    private void ToggleFullscreen(IWindow window)
    {
        if (window.WindowState == WindowState.Fullscreen)
        {
            window.WindowState = WindowState.Normal;
            window.Size = _windowedSize;
            window.Position = _windowedPos;
        }
        else
        {
            _windowedSize = window.Size;
            _windowedPos = window.Position;
            window.WindowState = WindowState.Fullscreen;
        }

        log.Info($"window state {window.WindowState}, framebuffer {window.FramebufferSize.X}x{window.FramebufferSize.Y}");
    }

    private List<string> OverlayLines(GameSession s, RealtimeAudioSink? audio)
    {
        var lines = new List<string>();
        if (_fatal is not null)
        {
            lines.Add(_fatal);
        }

        if (_paused)
        {
            lines.Add("PAUSED (F10)");
        }

        if (!_showOverlay)
        {
            return lines;
        }

        var ci = CultureInfo.InvariantCulture;
        lines.Add(string.Create(ci, $"{_frameMs.Fps:F0} FPS  FRAME {_frameMs.RecentMean:F1} MS (MAX {_frameMs.RecentMax:F1})"));
        lines.Add(string.Create(ci, $"HOST {options.HostHz} HZ  STEP {_stepMs.RecentMean:F1} MS  DRAW {_drawMs.RecentMean:F1} MS"));
        lines.Add(string.Create(ci, $"{s.Section} / {s.Screen}  T {s.Time:F1}"));
        if (s.Race.PlayerTelemetry is { } t && s.InTrack)
        {
            lines.Add(string.Create(ci, $"{t.Speed * 3.6f:F0} KM/H  {t.Rpm:F0} RPM  GEAR {t.Gear}"));
        }

        if (audio is not null)
        {
            lines.Add(string.Create(ci, $"AUDIO {audio.QueuedSeconds * 1000:F0} MS QUEUED  UNDERRUNS {audio.Underruns}"));
        }

        return lines;
    }

    /// <summary>Clears the window, blits the game picture into its (letterboxed) rectangle and draws the overlay.</summary>
    private void Present(IWindow window, Slrr.Game.Render.GameRenderAdapter? renderer, IReadOnlyList<string> lines)
    {
        var gl = _gl!;
        var fb = window.FramebufferSize;
        var mapper = MakeMapper(window);
        gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
        gl.Viewport(0, 0, (uint)fb.X, (uint)fb.Y);
        gl.Disable(EnableCap.ScissorTest);
        gl.ClearColor(0, 0, 0, 1);
        gl.Clear(ClearBufferMask.ColorBufferBit);
        if (renderer is not null)
        {
            var (x0, y0, x1, y1) = mapper.PictureGl();
            gl.BindFramebuffer(FramebufferTarget.ReadFramebuffer, renderer.TargetFramebuffer);
            gl.BindFramebuffer(FramebufferTarget.DrawFramebuffer, 0);
            var scaled = renderer.Width != x1 - x0 || renderer.Height != y1 - y0;
            gl.BlitFramebuffer(0, 0, renderer.Width, renderer.Height, x0, y0, x1, y1, ClearBufferMask.ColorBufferBit,
                scaled ? BlitFramebufferFilter.Linear : BlitFramebufferFilter.Nearest);
            gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
        }

        var scale = Math.Max(1f, MathF.Round(mapper.ContentScale.Y)) * (fb.Y >= 1000 ? 3 : 2);
        if (lines.Count > 0)
        {
            _overlay!.Text(lines, new Vector2(8 * scale / 2, 8 * scale / 2), scale, new Vector4(1f, 0.85f, 0.2f, 1f));
        }

        _overlay!.Flush(fb.X, fb.Y);
    }

    private void Screenshot(string path)
    {
        var gl = _gl!;
        var fb = _window!.FramebufferSize;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            var raw = new byte[fb.X * fb.Y * 4];
            gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
            gl.ReadBuffer(ReadBufferMode.Back);
            gl.PixelStore(PixelStoreParameter.PackAlignment, 1);
            unsafe
            {
                fixed (byte* p = raw)
                {
                    gl.ReadPixels(0, 0, (uint)fb.X, (uint)fb.Y, PixelFormat.Rgba, PixelType.UnsignedByte, p);
                }
            }

            var img = new RgbaImage(fb.X, fb.Y);
            var row = fb.X * 4;
            for (var y = 0; y < fb.Y; y++)
            {
                System.Buffer.BlockCopy(raw, (fb.Y - 1 - y) * row, img.Pixels, y * row, row);
            }

            for (var i = 3; i < img.Pixels.Length; i += 4)
            {
                img.Pixels[i] = 255; // the window's alpha is not meaningful
            }

            File.WriteAllBytes(path, PngCodec.Encode(img));
            log.Info($"screenshot {path} ({fb.X}x{fb.Y})");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            log.Info($"screenshot failed: {e.Message}");
        }
    }

    /// <summary>Drains glGetError (bounded: a lost context can report errors forever on some drivers) and logs the first few.</summary>
    private void CheckGlErrors(long hostFrame)
    {
        for (var i = 0; i < 16; i++)
        {
            var err = _gl!.GetError();
            if (err == GLEnum.NoError)
            {
                return;
            }

            GlErrorCount++;
            if (_glErrors.Count < 20)
            {
                var s = $"GL error {err} (0x{(int)err:X4}) after host frame {hostFrame}";
                _glErrors.Add(s);
                log.Info(s);
            }
        }
    }

    private void Report(GameSession s, RealtimeAudioSink? audio, FixedStepClock clock)
    {
        log.Info(_frameMs.Summary("displayed frame"));
        log.Info(_stepMs.Summary("host step"));
        log.Info(_drawMs.Summary("scene draw (CPU submit, no glFinish)"));
        log.Info(string.Create(CultureInfo.InvariantCulture, $"host frames {s.HostFrames}, game time {s.Time:F1} s, dropped real time {clock.Dropped:F2} s, GL errors {GlErrorCount}, clicks {s.Router.Clicks.Count}, keys {s.Router.KeysDelivered}, chars {s.Router.CharsDelivered}"));
        if (audio is not null)
        {
            log.Info(string.Create(CultureInfo.InvariantCulture, $"audio: {audio.FramesWritten / (double)audio.SampleRate:F1} s streamed, underruns {audio.Underruns}, dropped chunks {audio.DroppedChunks}"));
        }

        using var me = Process.GetCurrentProcess();
        log.Info(string.Create(CultureInfo.InvariantCulture, $"memory: peak working set {me.PeakWorkingSet64 / (1024.0 * 1024):F0} MB, GC heap {GC.GetTotalMemory(false) / (1024.0 * 1024):F0} MB"));
        log.Info("state: " + s.Describe());
    }
}
