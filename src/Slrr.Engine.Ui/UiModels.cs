using Slrr.Vm;

namespace Slrr.Engine.Ui;

/// <summary>A <c>java.render.Viewport</c>: normalised screen rectangle with a layer (no pixels).</summary>
public sealed class ViewportModel
{
    public required VmObject Object { get; init; }

    public int Layer { get; set; }

    /// <summary>First float of <c>create</c>/<c>resize</c> (left; guessed, see docs/engine/ui.md).</summary>
    public float Left { get; set; }

    /// <summary>Second float of <c>create</c>/<c>resize</c> (top; guessed).</summary>
    public float Top { get; set; }

    public float Width { get; set; }

    public float Height { get; set; }

    public bool Active { get; set; }

    public int RenderFlags { get; set; }

    public bool Destroyed { get; set; }

    public int Activations { get; set; }
}

/// <summary>A <c>java.render.Camera</c> bound to a viewport (parameters of the native create, stored only).</summary>
public sealed class CameraModel
{
    public required VmObject Object { get; init; }

    public VmObject? Parent { get; set; }

    public VmObject? Viewport { get; set; }

    public int Flags { get; set; }

    public float HalfFov { get; set; }

    public float Near { get; set; }

    public float Far { get; set; }

    public float LodBias { get; set; }

    public float LodAmp { get; set; }

    public int Culling { get; set; }

    public int PlaneTest { get; set; }

    public bool Destroyed { get; set; }

    public (int Color, float Near, float Far)? Fog { get; set; }
}

/// <summary>A <c>java.render.Text</c> instance: parent node, font, position, string, colour, alignment.</summary>
public sealed class TextModel
{
    public required VmObject Object { get; init; }

    public VmObject? Parent { get; set; }

    public VmObject? Font { get; set; }

    public float X { get; set; }

    public float Y { get; set; }

    /// <summary>Signed ARGB; <c>Text.DEF_COLOR</c> (-1, opaque white) until changed.</summary>
    public int Color { get; set; } = -1;

    /// <summary><c>Text.ALIGN_*</c>: RIGHT 0, CENTER 1, LEFT 2. Default LEFT is a guess.</summary>
    public int Align { get; set; } = 2;

    /// <summary>The string uploaded by the last <c>update()</c> (the script's <c>text</c> field at that moment).</summary>
    public string? Text { get; set; }

    public int Updates { get; set; }

    public int DefaultColor { get; set; } = -1;

    public override string ToString() => $"\"{Text}\" @({X:0.###},{Y:0.###}) color {Color:X8} align {Align}";
}

/// <summary>A hotkey registered through <c>Input.createHotkey</c>.</summary>
public sealed class HotkeyModel
{
    public required VmObject Hotkey { get; init; }

    /// <summary>Virtual axis (<c>Input.AXIS_*</c>) when <see cref="Flags"/> has VIRTUAL (1), a DIK scan code when it has KEY (2).</summary>
    public int Key { get; set; }

    public int Flags { get; set; }

    /// <summary>Object whose <c>handleEvent(Hotkey)</c> receives the event.</summary>
    public VmObject? Handler { get; set; }

    /// <summary>The Osd the hotkey belongs to; null for a global hotkey (<c>Input.createGlobalHotkey</c>).</summary>
    public VmObject? Osd { get; set; }

    /// <summary><c>Event.F_KEY_PRESS</c> 1 | <c>F_KEY_RELEASE</c> 2.</summary>
    public int EventFilter { get; set; } = 1;

    public bool IsVirtual => (Flags & InputState.FlagVirtual) != 0;
}

/// <summary>One <c>Controller.user_Add</c> row: physical (device, axis) range mapped onto a virtual axis.</summary>
public sealed record AxisMapping(int VirtualAxis, int Device, int Axis, float FromMin, float FromMax, float ToMin, float ToMax)
{
    /// <summary>Linear map of the physical value, clamped to the target range (guessed shape).</summary>
    public float Map(float physical)
    {
        var span = FromMax - FromMin;
        if (span == 0)
        {
            return physical != 0 ? ToMax : ToMin;
        }

        var t = (physical - FromMin) / span;
        if (t <= 0)
        {
            return 0;
        }

        t = Math.Min(t, 1);
        return ToMin + (t * (ToMax - ToMin));
    }
}

/// <summary>Engine side of a <c>java.io.Controller</c>: the virtual-axis table built by <c>setcontrol</c>.</summary>
public sealed class ControllerModel
{
    public List<AxisMapping> Mappings { get; } = [];

    public Dictionary<int, float[]> Smoothing { get; } = [];

    public Dictionary<int, float> Speed { get; } = [];

    public Dictionary<int, (float A, float B)> Force { get; } = [];

    public int Resets { get; set; }
}

/// <summary>Video state: mode, envmap, screenshots, AVI playback, frame counters.</summary>
public sealed class VideoState
{
    public int Width { get; set; }

    public int Height { get; set; }

    public int Depth { get; set; }

    public List<(int W, int H, int Bpp)> ModeChanges { get; } = [];

    /// <summary>Display modes reported by <c>numDisplayModes</c>/<c>displayModeName</c> (headless list, guessed).</summary>
    public List<(int W, int H, int Bpp)> DisplayModes { get; } =
    [
        (640, 480, 32), (800, 600, 32), (1024, 768, 32), (1280, 720, 32), (1280, 1024, 32), (1920, 1080, 32),
    ];

    public VmObject? GlobalEnvmap { get; set; }

    public List<string> Screenshots { get; } = [];

    public string? VideoPath { get; set; }

    public float VideoStart { get; set; }

    public bool VideoOpen { get; set; }

    /// <summary>Whether <c>openVideo</c> succeeds (returns 0). False makes it fail with 1 (the failure value is a guess).</summary>
    public bool VideoAvailable { get; set; } = true;

    /// <summary>Length in seconds a headless AVI "plays" before <c>isPlayingVideo</c> returns 0.</summary>
    public float VideoLength { get; set; }

    public int Flushes { get; set; }

    public int ForcedFrames { get; set; }

    public long Frames { get; set; }
}

/// <summary>Sound state: three channel volumes, music set and track, hardware mode.</summary>
public sealed class SoundState
{
    /// <summary>CHANNEL_EFFECTS 0, MUSIC 1, ENGINE 2. Initial 1.0 is a guess (Config loads saved volumes and sets them).</summary>
    public float[] Volumes { get; } = [1f, 1f, 1f];

    public int MusicSet { get; set; } = -1;

    public int Track { get; set; }

    public int Mode { get; set; }

    public bool Has3DHardware { get; set; }

    public bool HasMixHardware { get; set; }

    /// <summary>Whether <c>enableDebugDump</c> succeeds (it then makes Sound.init build a debug Viewport/Camera).</summary>
    public bool DebugDumpAvailable { get; set; }

    public List<string> Log { get; } = [];
}
