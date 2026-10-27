using System.Numerics;
using Silk.NET.Input;

namespace Slrr.App;

public enum InputKind
{
    KeyDown,
    KeyUp,
    Char,
    MouseMove,
    MouseDown,
    MouseUp,
    PadAxis,
    PadButton,
}

/// <summary>One raw input event (from the window's callbacks or from the synthetic input script; both take the same path).</summary>
public readonly record struct InputEvent(InputKind Kind, Key Key = Key.Unknown, char Char = '\0', Vector2 Position = default,
    MouseButton Button = MouseButton.Left, int Index = 0, float Value = 0f)
{
    public static InputEvent Down(Key k) => new(InputKind.KeyDown, k);

    public static InputEvent Up(Key k) => new(InputKind.KeyUp, k);

    public static InputEvent Typed(char c) => new(InputKind.Char, Char: c);

    public static InputEvent Move(Vector2 p) => new(InputKind.MouseMove, Position: p);

    public static InputEvent Press(Vector2 p, MouseButton b = MouseButton.Left) => new(InputKind.MouseDown, Position: p, Button: b);

    public static InputEvent Release(Vector2 p, MouseButton b = MouseButton.Left) => new(InputKind.MouseUp, Position: p, Button: b);

    public static InputEvent Axis(PadAxis a, float v) => new(InputKind.PadAxis, Index: (int)a, Value: v);

    public static InputEvent PadKey(PadButton b, bool down) => new(InputKind.PadButton, Index: (int)b, Value: down ? 1f : 0f);
}

public enum PadAxis
{
    LeftX,
    LeftY,
    RightX,
    RightY,
    LeftTrigger,
    RightTrigger,
}

public enum PadButton
{
    A,
    B,
    X,
    Y,
    Start,
    Back,
    DPadUp,
    DPadDown,
    DPadLeft,
    DPadRight,
    LeftBumper,
    RightBumper,
}

/// <summary>Commands the app handles itself (never forwarded to the game).</summary>
public enum AppCommand
{
    ToggleOverlay,
    TogglePause,
    ToggleFullscreen,
    Screenshot,
}

/// <summary>What the router needs from the game (implemented over the GameHost by <see cref="HostGameInput"/>; faked in tests).</summary>
public interface IGameInput
{
    /// <summary>A key went down: DIK scan code and the character it typed (0 = none).</summary>
    void KeyDown(int dik, char ch);

    void KeyUp(int dik);

    /// <summary>An analog value on a keyboard axis (device 0, axis = DIK), for gamepad sticks and triggers.</summary>
    void SetKeyAxis(int dik, float value);

    /// <summary>The cursor moved (OSD coordinates).</summary>
    void MoveCursor(Vector2 osd);

    /// <summary>The clickable target under an OSD point, or null.</summary>
    object? HitAt(Vector2 osd);

    /// <summary>The cursor now rests on <paramref name="target"/> (null = nothing): a hover notification when it changed.</summary>
    void Hover(object? target);

    /// <summary>A completed left click (press and release on the same target); returns a description for the log.</summary>
    string Click(object target);
}

/// <summary>
/// Turns raw window events into game input: keyboard → DIK codes (+ the typed character, paired from the platform's separate
/// character event), mouse → OSD cursor, hover and clicks on the gadget under the cursor (press and release on the same target),
/// gamepad → keyboard axes and keys (<see cref="GamepadMapping"/>), and the app's own keys (F3, F10, F11, Alt+Enter, F12).
/// </summary>
public sealed class InputRouter(IGameInput game)
{
    private readonly HashSet<Key> _held = [];
    private readonly GamepadMapping _pad = new();
    private object? _pressTarget;
    private object? _hover;
    private Vector2? _pendingMove;
    private bool _alt;

    public ViewportMapper Mapper { get; set; } = new(new Vector2(ViewportMapper.VirtualWidth, ViewportMapper.VirtualHeight),
        new Vector2(ViewportMapper.VirtualWidth, ViewportMapper.VirtualHeight), AspectMode.Fit);

    /// <summary>Last mouse position in window coordinates.</summary>
    public Vector2 Mouse { get; private set; }

    /// <summary>Last mouse position mapped into the game.</summary>
    public GamePoint Cursor { get; private set; }

    /// <summary>Clicks delivered (for the overlay, the log and the scripted test).</summary>
    public List<string> Clicks { get; } = [];

    public int KeysDelivered { get; private set; }

    public int CharsDelivered { get; private set; }

    /// <summary>When false (paused) game input is dropped, the app keys still work.</summary>
    public bool GameInputEnabled { get; set; } = true;

    public IReadOnlyCollection<Key> Held => _held;

    public void Process(IReadOnlyList<InputEvent> events, ICollection<AppCommand> commands)
    {
        ArgumentNullException.ThrowIfNull(events);
        ArgumentNullException.ThrowIfNull(commands);
        for (var i = 0; i < events.Count; i++)
        {
            var e = events[i];
            switch (e.Kind)
            {
                case InputKind.KeyDown:
                    if (e.Key is Key.AltLeft or Key.AltRight)
                    {
                        _alt = true;
                    }

                    if (AppKey(e.Key, _alt) is { } cmd)
                    {
                        commands.Add(cmd);
                        if (i + 1 < events.Count && events[i + 1].Kind == InputKind.Char)
                        {
                            i++;
                        }

                        break;
                    }

                    var ch = KeyMap.ControlChar(e.Key);
                    if (KeyMap.ProducesChar(e.Key) && i + 1 < events.Count && events[i + 1].Kind == InputKind.Char)
                    {
                        ch = events[++i].Char;
                    }

                    if (!_held.Add(e.Key))
                    {
                        break; // auto-repeat: the game polls held keys itself
                    }

                    var dik = KeyMap.ToDik(e.Key);
                    if (GameInputEnabled && (dik != 0 || ch != '\0'))
                    {
                        game.KeyDown(dik, Printable(ch));
                        KeysDelivered++;
                        CharsDelivered += ch != '\0' ? 1 : 0;
                    }

                    break;
                case InputKind.KeyUp:
                    if (e.Key is Key.AltLeft or Key.AltRight)
                    {
                        _alt = false;
                    }

                    if (_held.Remove(e.Key) && KeyMap.ToDik(e.Key) is var d and not 0)
                    {
                        game.KeyUp(d);
                    }

                    break;
                case InputKind.Char:
                    // A character without a key press before it (IME, dead keys, AltGr compositions): typed, no scan code (GUESSED).
                    if (GameInputEnabled && Printable(e.Char) != '\0')
                    {
                        game.KeyDown(0, e.Char);
                        game.KeyUp(0);
                        CharsDelivered++;
                    }

                    break;
                case InputKind.MouseMove:
                    _pendingMove = e.Position;
                    break;
                case InputKind.MouseDown when e.Button == MouseButton.Left:
                    MoveTo(e.Position);
                    _pressTarget = Cursor.Inside && GameInputEnabled ? game.HitAt(Cursor.Osd) : null;
                    break;
                case InputKind.MouseUp when e.Button == MouseButton.Left:
                    MoveTo(e.Position);
                    var target = Cursor.Inside && GameInputEnabled ? game.HitAt(Cursor.Osd) : null;
                    if (target is not null && Equals(target, _pressTarget))
                    {
                        Clicks.Add(game.Click(target));
                    }

                    _pressTarget = null;
                    break;
                case InputKind.PadAxis or InputKind.PadButton:
                    if (GameInputEnabled)
                    {
                        _pad.Apply(e, game);
                    }

                    break;
            }
        }

        if (_pendingMove is { } p)
        {
            MoveTo(p);
        }
    }

    /// <summary>Releases every held key (focus lost, pause): a key held while the window lost focus would otherwise stay down forever.</summary>
    public void ReleaseAll()
    {
        foreach (var k in _held.ToList())
        {
            if (KeyMap.ToDik(k) is var d and not 0)
            {
                game.KeyUp(d);
            }
        }

        _held.Clear();
        _pad.Reset(game);
        _alt = false;
    }

    private void MoveTo(Vector2 window)
    {
        _pendingMove = null;
        Mouse = window;
        Cursor = Mapper.WindowToGame(window);
        if (!GameInputEnabled)
        {
            return;
        }

        game.MoveCursor(Cursor.Osd);
        var over = Cursor.Inside ? game.HitAt(Cursor.Osd) : null;
        if (!Equals(over, _hover))
        {
            _hover = over;
            game.Hover(over);
        }
    }

    public static AppCommand? AppKey(Key key, bool alt) => key switch
    {
        Key.F3 => AppCommand.ToggleOverlay,
        Key.F10 => AppCommand.TogglePause,
        Key.F11 => AppCommand.ToggleFullscreen,
        Key.F12 => AppCommand.Screenshot,
        Key.Enter when alt => AppCommand.ToggleFullscreen,
        _ => null,
    };

    /// <summary>Characters the game's 256-glyph fonts can show (and the control characters StringInput handles).</summary>
    private static char Printable(char c) => c is '\r' or '\b' or '\t' or (char)27 || (c >= ' ' && c < (char)256 && c != (char)127) ? c : '\0';
}

/// <summary>
/// Gamepad → keyboard device (GUESSED: the shipped control set maps the keyboard, and its pad entries name DirectInput devices of the
/// author's machine, so a pad is fed in as analog keyboard axes): left stick X → Left/Right (DIK 203/205) with its magnitude, right
/// trigger → Up (200, throttle), left trigger → Down (208, brake), A → Enter, B and Start → Esc, X → Space, D-pad → arrow keys.
/// </summary>
public sealed class GamepadMapping
{
    public const float DeadZone = 0.15f;

    private readonly Dictionary<int, float> _axes = [];
    private readonly HashSet<int> _keys = [];

    public static float Shape(float v)
    {
        if (!float.IsFinite(v))
        {
            return 0;
        }

        var a = Math.Abs(v);
        return a <= DeadZone ? 0 : Math.Sign(v) * Math.Min((a - DeadZone) / (1 - DeadZone), 1f);
    }

    public void Apply(InputEvent e, IGameInput game)
    {
        if (e.Kind == InputKind.PadAxis)
        {
            var v = Shape(e.Value);
            switch ((PadAxis)e.Index)
            {
                case PadAxis.LeftX:
                    SetAxis(game, 203, Math.Max(-v, 0));
                    SetAxis(game, 205, Math.Max(v, 0));
                    break;
                case PadAxis.RightTrigger:
                    SetAxis(game, 200, Math.Clamp(e.Value, 0, 1)); // GLFW triggers rest at -1 or 0 depending on the mapping: the lower half is ignored
                    break;
                case PadAxis.LeftTrigger:
                    SetAxis(game, 208, Math.Clamp(e.Value, 0, 1));
                    break;
            }

            return;
        }

        var dik = (PadButton)e.Index switch
        {
            PadButton.A => 28,
            PadButton.B or PadButton.Start => 1,
            PadButton.X => 57,
            PadButton.DPadUp => 200,
            PadButton.DPadDown => 208,
            PadButton.DPadLeft => 203,
            PadButton.DPadRight => 205,
            _ => 0,
        };
        if (dik == 0)
        {
            return;
        }

        if (e.Value > 0.5f && _keys.Add(dik))
        {
            game.KeyDown(dik, dik == 28 ? '\r' : dik == 1 ? (char)27 : '\0');
        }
        else if (e.Value <= 0.5f && _keys.Remove(dik))
        {
            game.KeyUp(dik);
        }
    }

    public void Reset(IGameInput game)
    {
        foreach (var dik in _axes.Keys.ToList())
        {
            SetAxis(game, dik, 0);
        }

        foreach (var dik in _keys)
        {
            game.KeyUp(dik);
        }

        _keys.Clear();
    }

    private void SetAxis(IGameInput game, int dik, float v)
    {
        if (Math.Abs(_axes.GetValueOrDefault(dik) - v) < 1e-3f)
        {
            return;
        }

        _axes[dik] = v;
        game.SetKeyAxis(dik, v);
    }
}
