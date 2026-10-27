using System.Globalization;

namespace Slrr.App;

/// <summary>How the game's 4:3 picture is fitted into the window.</summary>
public enum AspectMode
{
    /// <summary>Keep the game's 800x600 (4:3) shape and add black bars (default).</summary>
    Fit,

    /// <summary>Fill the whole window; the 3D view uses the window's aspect, the Osd layout is stretched horizontally.</summary>
    Stretch,
}

/// <summary>Command line of <c>slrr-app</c> (docs/app.md).</summary>
public sealed class AppOptions
{
    public const string Usage = """
        usage: slrr-app [--root <game dir>] [--save-root <dir>] [--size WxH] [--fullscreen] [--aspect fit|stretch]
                        [--render-scale s] [--host-hz n] [--no-vsync] [--max-fps n] [--no-audio] [--wav file]
                        [--input-script file|builtin:drive] [--lockstep] [--capture-every n] [--out dir]
                        [--quit-after seconds] [--headless-frames n] [--log file] [--verbose]
          --root            game directory with system.rpk and sl.rpk (else SLRR_GAME_ROOT, else auto-detect)
          --save-root       careers and settings (default: per-user application data folder)
          --headless-frames run n host frames without a window, GL or audio (CI), then exit
          --input-script    synthetic input for tests (menu clicks, keys, captures); see docs/app.md
          --lockstep        exactly one host frame per displayed frame (deterministic scripted runs)
        keys: F3 overlay  F10 pause  F11 / Alt+Enter fullscreen  F12 screenshot  (Esc is the game's own menu)
        """;

    public string? Root { get; set; }

    public string? SaveRoot { get; set; }

    public int Width { get; set; } = 1024;

    public int Height { get; set; } = 768;

    public bool SizeGiven { get; set; }

    public bool Fullscreen { get; set; }

    public AspectMode Aspect { get; set; } = AspectMode.Fit;

    /// <summary>Offscreen render resolution relative to the window area the game occupies (1 = native pixels).</summary>
    public float RenderScale { get; set; } = 1f;

    /// <summary>Fixed host (script + physics) step rate. 30 Hz is what every measured race-wave run used.</summary>
    public int HostHz { get; set; } = 30;

    public bool VSync { get; set; } = true;

    /// <summary>Frame cap when VSync is off (0 = uncapped).</summary>
    public int MaxFps { get; set; } = 240;

    public bool Audio { get; set; } = true;

    public string? WavPath { get; set; }

    public string? InputScript { get; set; }

    public bool Lockstep { get; set; }

    public int CaptureEvery { get; set; }

    public string? OutDir { get; set; }

    public float QuitAfter { get; set; }

    public int HeadlessFrames { get; set; }

    public string? LogPath { get; set; }

    public bool Verbose { get; set; }

    public bool ShowHelp { get; set; }

    /// <summary>Parses the command line; throws <see cref="ArgumentException"/> with a readable message on bad input.</summary>
    public static AppOptions Parse(IReadOnlyList<string> args)
    {
        var o = new AppOptions();
        for (var i = 0; i < args.Count; i++)
        {
            var a = args[i];
            string Next() => i + 1 < args.Count ? args[++i] : throw new ArgumentException($"{a} needs a value");
            switch (a)
            {
                case "-h" or "--help" or "-?":
                    o.ShowHelp = true;
                    break;
                case "--root":
                    o.Root = Next();
                    break;
                case "--save-root":
                    o.SaveRoot = Next();
                    break;
                case "--size":
                    (o.Width, o.Height) = ParseSize(Next());
                    o.SizeGiven = true;
                    break;
                case "--fullscreen":
                    o.Fullscreen = true;
                    break;
                case "--aspect":
                    o.Aspect = Next().ToLowerInvariant() switch
                    {
                        "fit" or "4:3" or "letterbox" => AspectMode.Fit,
                        "stretch" or "fill" => AspectMode.Stretch,
                        var x => throw new ArgumentException($"--aspect must be fit or stretch, not '{x}'"),
                    };
                    break;
                case "--render-scale":
                    o.RenderScale = Range(ParseFloat(Next(), a), 0.25f, 2f, a);
                    break;
                case "--host-hz":
                    o.HostHz = (int)Range(ParseInt(Next(), a), 10, 240, a);
                    break;
                case "--no-vsync":
                    o.VSync = false;
                    break;
                case "--max-fps":
                    o.MaxFps = (int)Range(ParseInt(Next(), a), 0, 1000, a);
                    break;
                case "--no-audio":
                    o.Audio = false;
                    break;
                case "--wav":
                    o.WavPath = Next();
                    break;
                case "--input-script":
                    o.InputScript = Next();
                    break;
                case "--lockstep":
                    o.Lockstep = true;
                    break;
                case "--capture-every":
                    o.CaptureEvery = (int)Range(ParseInt(Next(), a), 0, 1_000_000, a);
                    break;
                case "--out":
                    o.OutDir = Next();
                    break;
                case "--quit-after":
                    o.QuitAfter = Range(ParseFloat(Next(), a), 0, 1e6f, a);
                    break;
                case "--headless-frames":
                    o.HeadlessFrames = (int)Range(ParseInt(Next(), a), 1, 100_000_000, a);
                    break;
                case "--log":
                    o.LogPath = Next();
                    break;
                case "--verbose" or "-v":
                    o.Verbose = true;
                    break;
                default:
                    // macOS Finder passes -psn_0_12345 to apps started from a bundle; ignore it.
                    if (a.StartsWith("-psn_", StringComparison.Ordinal))
                    {
                        break;
                    }

                    throw new ArgumentException($"unknown option '{a}'");
            }
        }

        return o;
    }

    public static (int W, int H) ParseSize(string s)
    {
        var p = s.ToLowerInvariant().Split('x');
        if (p.Length != 2 || !int.TryParse(p[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var w) ||
            !int.TryParse(p[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var h) || w < 160 || h < 120 || w > 16384 || h > 16384)
        {
            throw new ArgumentException($"--size must look like 1280x960 (160x120 .. 16384x16384), not '{s}'");
        }

        return (w, h);
    }

    private static float ParseFloat(string s, string name) =>
        float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var f) && float.IsFinite(f) ? f : throw new ArgumentException($"{name} needs a number, not '{s}'");

    private static int ParseInt(string s, string name) =>
        int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : throw new ArgumentException($"{name} needs an integer, not '{s}'");

    private static float Range(float v, float min, float max, string name) =>
        v >= min && v <= max ? v : throw new ArgumentException($"{name} must be between {min.ToString(CultureInfo.InvariantCulture)} and {max.ToString(CultureInfo.InvariantCulture)}");
}
