using System.Globalization;

namespace Slrr.Viewer;

/// <summary>Command line: <c>slrr-viewer &lt;mesh.scx&gt; [--screenshot out.png] [--size WxH] [--yaw rad] [--pitch rad] [--wireframe]</c>.</summary>
public sealed record ViewerOptions
{
    public const string Usage =
        "usage: slrr-viewer <mesh.scx> [--screenshot out.png] [--size WxH] [--yaw radians] [--pitch radians] [--wireframe]\n" +
        "  interactive: left-drag orbit, wheel zoom, W wireframe, R reset, Esc quit\n" +
        "  --screenshot renders one frame offscreen (hidden window + FBO) to a PNG and exits";

    public required string MeshPath { get; init; }
    public string? ScreenshotPath { get; init; }
    public int Width { get; init; } = 1024;
    public int Height { get; init; } = 768;
    public float? Yaw { get; init; }
    public float? Pitch { get; init; }
    public bool Wireframe { get; init; }

    /// <summary>Parses arguments; returns null and sets <paramref name="error"/> on bad input.</summary>
    public static ViewerOptions? Parse(IReadOnlyList<string> args, out string? error)
    {
        ArgumentNullException.ThrowIfNull(args);
        error = null;
        string? mesh = null, shot = null;
        int w = 1024, h = 768;
        float? yaw = null, pitch = null;
        var wire = false;
        for (var i = 0; i < args.Count; i++)
        {
            var a = args[i];
            string Next()
            {
                if (i + 1 >= args.Count)
                {
                    throw new FormatException($"{a} needs a value");
                }

                return args[++i];
            }

            try
            {
                switch (a)
                {
                    case "--screenshot":
                        shot = Next();
                        break;
                    case "--size":
                        var parts = Next().Split('x', 'X');
                        if (parts.Length != 2 || !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out w) ||
                            !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out h) || w is < 1 or > 8192 || h is < 1 or > 8192)
                        {
                            throw new FormatException("--size must be WxH with 1..8192");
                        }

                        break;
                    case "--yaw":
                        yaw = float.Parse(Next(), NumberStyles.Float, CultureInfo.InvariantCulture);
                        break;
                    case "--pitch":
                        pitch = float.Parse(Next(), NumberStyles.Float, CultureInfo.InvariantCulture);
                        break;
                    case "--wireframe":
                        wire = true;
                        break;
                    default:
                        if (a.StartsWith("--", StringComparison.Ordinal) || mesh is not null)
                        {
                            throw new FormatException($"unexpected argument '{a}'");
                        }

                        mesh = a;
                        break;
                }
            }
            catch (FormatException e)
            {
                error = e.Message;
                return null;
            }
        }

        if (mesh is null)
        {
            error = "missing mesh path";
            return null;
        }

        return new ViewerOptions { MeshPath = mesh, ScreenshotPath = shot, Width = w, Height = h, Yaw = yaw, Pitch = pitch, Wireframe = wire };
    }
}
