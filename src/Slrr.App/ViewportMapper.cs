using System.Numerics;

namespace Slrr.App;

/// <summary>A rectangle in pixels with a top-left origin.</summary>
public readonly record struct PixelRect(int X, int Y, int W, int H)
{
    public bool Contains(Vector2 p) => p.X >= X && p.Y >= Y && p.X < X + W && p.Y < Y + H;
}

/// <summary>A window point mapped into the game's spaces.</summary>
/// <param name="Inside">The point lies on the game picture (not on a letterbox bar or outside the window).</param>
/// <param name="Osd">OSD coordinates: -1..1 left to right, -1..1 top to bottom (what <c>MouseCursor.getPos</c> returns, y down).</param>
/// <param name="Virtual">The game's 800x600 virtual UI pixels (<c>Config.video_x/y</c>), top-left origin.</param>
public readonly record struct GamePoint(bool Inside, Vector2 Osd, Vector2 Virtual);

/// <summary>
/// Window ↔ game coordinate transforms. Three spaces:
/// <list type="bullet">
/// <item><b>window</b> coordinates, what the OS reports for the mouse (GLFW "screen coordinates": logical points on macOS Retina and
/// on Wayland with scaling, physical pixels on Windows and X11);</item>
/// <item><b>framebuffer</b> pixels (Retina: 2x the window size), where the game picture is placed (<see cref="Picture"/>);</item>
/// <item>the game picture: OSD -1..1 and the 800x600 virtual UI.</item>
/// </list>
/// <see cref="AspectMode.Fit"/> keeps the 4:3 picture centred with black bars; <see cref="AspectMode.Stretch"/> fills the framebuffer.
/// Pure arithmetic, unit-tested (tests/Slrr.App.Tests).
/// </summary>
public sealed class ViewportMapper
{
    public const int VirtualWidth = 800;
    public const int VirtualHeight = 600;

    public ViewportMapper(Vector2 windowSize, Vector2 framebufferSize, AspectMode aspect, float renderScale = 1f)
    {
        WindowSize = new Vector2(Math.Max(windowSize.X, 1), Math.Max(windowSize.Y, 1));
        FramebufferSize = new Vector2(Math.Max(framebufferSize.X, 1), Math.Max(framebufferSize.Y, 1));
        Aspect = aspect;
        RenderScale = renderScale;
        Picture = ComputePicture(FramebufferSize, aspect);
    }

    public Vector2 WindowSize { get; }

    public Vector2 FramebufferSize { get; }

    public AspectMode Aspect { get; }

    public float RenderScale { get; }

    /// <summary>Framebuffer pixels per window unit (2 on a Retina display, 1 elsewhere; may differ per axis after odd resizes).</summary>
    public Vector2 ContentScale => FramebufferSize / WindowSize;

    /// <summary>Where the game picture goes in the framebuffer (top-left origin).</summary>
    public PixelRect Picture { get; }

    /// <summary>Offscreen render target size: the picture size times <see cref="RenderScale"/>, at least 64x48, at most 8192 per side.</summary>
    public (int W, int H) RenderSize =>
        (Math.Clamp((int)MathF.Round(Picture.W * RenderScale), 64, 8192), Math.Clamp((int)MathF.Round(Picture.H * RenderScale), 48, 8192));

    public static PixelRect ComputePicture(Vector2 fb, AspectMode aspect)
    {
        var fw = Math.Max((int)fb.X, 1);
        var fh = Math.Max((int)fb.Y, 1);
        if (aspect == AspectMode.Stretch)
        {
            return new PixelRect(0, 0, fw, fh);
        }

        // Largest 4:3 rectangle, centred; integer pixels, the bars take the rounding.
        int w, h;
        if (fw * VirtualHeight >= fh * VirtualWidth)
        {
            h = fh;
            w = Math.Max((int)Math.Round(fh * (double)VirtualWidth / VirtualHeight), 1);
        }
        else
        {
            w = fw;
            h = Math.Max((int)Math.Round(fw * (double)VirtualHeight / VirtualWidth), 1);
        }

        return new PixelRect((fw - w) / 2, (fh - h) / 2, w, h);
    }

    public Vector2 WindowToFramebuffer(Vector2 window) => window * ContentScale;

    public Vector2 FramebufferToWindow(Vector2 fb) => fb / ContentScale;

    /// <summary>Maps a mouse position (window coordinates) into the game. Points on the bars are clamped to the picture edge with Inside = false.</summary>
    public GamePoint WindowToGame(Vector2 window)
    {
        var fb = WindowToFramebuffer(window);
        var p = Picture;
        var u = (fb.X - p.X) / p.W;
        var v = (fb.Y - p.Y) / p.H;
        var inside = u >= 0 && v >= 0 && u < 1 && v < 1 && float.IsFinite(u) && float.IsFinite(v);
        u = Math.Clamp(float.IsFinite(u) ? u : 0, 0, 1);
        v = Math.Clamp(float.IsFinite(v) ? v : 0, 0, 1);
        return new GamePoint(inside, new Vector2((u * 2) - 1, (v * 2) - 1), new Vector2(u * VirtualWidth, v * VirtualHeight));
    }

    /// <summary>Inverse of <see cref="WindowToGame"/> for OSD coordinates (used by the synthetic input script to aim at a gadget).</summary>
    public Vector2 OsdToWindow(Vector2 osd)
    {
        var p = Picture;
        var fb = new Vector2(p.X + ((osd.X + 1) * 0.5f * p.W), p.Y + ((osd.Y + 1) * 0.5f * p.H));
        return FramebufferToWindow(fb);
    }

    public Vector2 VirtualToWindow(Vector2 virt) => OsdToWindow(new Vector2((virt.X / VirtualWidth * 2) - 1, (virt.Y / VirtualHeight * 2) - 1));

    /// <summary>The picture rectangle in GL's bottom-left framebuffer convention: (x0, y0, x1, y1).</summary>
    public (int X0, int Y0, int X1, int Y1) PictureGl()
    {
        var p = Picture;
        var fh = (int)FramebufferSize.Y;
        return (p.X, fh - p.Y - p.H, p.X + p.W, fh - p.Y);
    }
}
