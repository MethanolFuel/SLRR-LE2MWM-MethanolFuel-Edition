using System.Numerics;
using Silk.NET.Input;
using Slrr.App;
using Xunit;

namespace Slrr.App.Tests;

/// <summary>Records what the router delivers; one clickable box at OSD (-0.5..0.5, -0.1..0.1).</summary>
internal sealed class FakeGame : IGameInput
{
    public readonly List<string> Calls = [];

    public OsdRect Box { get; set; } = new(0, 0, 0.5f, 0.1f);

    public Vector2 CursorOsd { get; private set; }

    public void KeyDown(int dik, char ch) => Calls.Add(ch == '\0' ? $"down {dik}" : $"down {dik} '{ch}'");

    public void KeyUp(int dik) => Calls.Add($"up {dik}");

    public void SetKeyAxis(int dik, float value) => Calls.Add($"axis {dik} {value:0.##}");

    public void MoveCursor(Vector2 osd) => CursorOsd = osd;

    public object? HitAt(Vector2 osd) => Box.Contains(osd) ? "box" : null;

    public void Hover(object? target) => Calls.Add($"hover {target ?? "none"}");

    public string Click(object target)
    {
        Calls.Add($"click {target}");
        return $"click {target}";
    }
}

public class InputRouterTests
{
    private static (InputRouter Router, FakeGame Game) Make(int w = 800, int h = 600)
    {
        var g = new FakeGame();
        var r = new InputRouter(g) { Mapper = new ViewportMapper(new Vector2(w, h), new Vector2(w, h), AspectMode.Fit) };
        return (r, g);
    }

    private static List<AppCommand> Run(InputRouter r, params InputEvent[] events)
    {
        var cmds = new List<AppCommand>();
        r.Process(events, cmds);
        return cmds;
    }

    [Fact]
    public void KeyPressCarriesTheCharacterOfTheFollowingCharEvent()
    {
        var (r, g) = Make();
        Run(r, InputEvent.Down(Key.B), InputEvent.Typed('B'), InputEvent.Up(Key.B), InputEvent.Down(Key.Enter), InputEvent.Up(Key.Enter));
        Assert.Equal(["down 48 'B'", "up 48", "down 28 '\r'", "up 28"], g.Calls);
        Assert.Equal(2, r.CharsDelivered);
    }

    [Fact]
    public void RepeatsAreDroppedAndHeldKeysReleaseOnFocusLoss()
    {
        var (r, g) = Make();
        Run(r, InputEvent.Down(Key.Up), InputEvent.Down(Key.Up), InputEvent.Down(Key.Left));
        Assert.Equal(["down 200", "down 203"], g.Calls);
        r.ReleaseAll();
        Assert.Contains("up 200", g.Calls);
        Assert.Contains("up 203", g.Calls);
        Assert.Empty(r.Held);
    }

    [Fact]
    public void AppKeysAreNotForwarded()
    {
        var (r, g) = Make();
        var cmds = Run(r, InputEvent.Down(Key.F3), InputEvent.Up(Key.F3), InputEvent.Down(Key.F10), InputEvent.Down(Key.F11), InputEvent.Down(Key.F12),
            InputEvent.Down(Key.AltLeft), InputEvent.Down(Key.Enter), InputEvent.Up(Key.Enter), InputEvent.Up(Key.AltLeft));
        Assert.Equal([AppCommand.ToggleOverlay, AppCommand.TogglePause, AppCommand.ToggleFullscreen, AppCommand.Screenshot, AppCommand.ToggleFullscreen], cmds);
        Assert.Equal(["down 56", "up 56"], g.Calls); // only Alt itself reached the game
    }

    [Fact]
    public void LoneCharactersAreTypedWithoutAScanCode()
    {
        var (r, g) = Make();
        Run(r, InputEvent.Typed('é'), InputEvent.Typed('中'));
        Assert.Equal(["down 0 'é'", "up 0"], g.Calls); // outside the 256-glyph fonts: dropped
    }

    [Fact]
    public void ClickNeedsPressAndReleaseOnTheSameTarget()
    {
        var (r, g) = Make();
        var inBox = r.Mapper.OsdToWindow(new Vector2(0.2f, 0.05f));
        var outside = r.Mapper.OsdToWindow(new Vector2(0.2f, 0.5f));
        Run(r, InputEvent.Move(inBox), InputEvent.Press(inBox), InputEvent.Release(inBox));
        Assert.Contains("click box", g.Calls);
        Assert.Equal(0.2f, g.CursorOsd.X, 0.001f);
        g.Calls.Clear();
        Run(r, InputEvent.Press(inBox), InputEvent.Move(outside), InputEvent.Release(outside));
        Assert.DoesNotContain("click box", g.Calls);
        g.Calls.Clear();
        Run(r, InputEvent.Press(inBox, MouseButton.Right), InputEvent.Release(inBox, MouseButton.Right));
        Assert.DoesNotContain("click box", g.Calls);
    }

    [Fact]
    public void ClicksOnTheLetterboxBarsMissTheGame()
    {
        var (r, g) = Make(1600, 600); // 4:3 picture 800 wide at x 400..1200
        g.Box = new OsdRect(-1, 0, 0.2f, 1f); // a box touching the left picture edge
        var bar = new Vector2(100, 300);
        Run(r, InputEvent.Press(bar), InputEvent.Release(bar));
        Assert.DoesNotContain("click box", g.Calls);
        var edge = new Vector2(410, 300);
        Run(r, InputEvent.Press(edge), InputEvent.Release(edge));
        Assert.Contains("click box", g.Calls);
    }

    [Fact]
    public void HoverIsReportedOnlyWhenTheTargetChanges()
    {
        var (r, g) = Make();
        var a = r.Mapper.OsdToWindow(new Vector2(0.1f, 0f));
        var b = r.Mapper.OsdToWindow(new Vector2(0.2f, 0f));
        var c = r.Mapper.OsdToWindow(new Vector2(0.9f, 0.9f));
        Run(r, InputEvent.Move(a));
        Run(r, InputEvent.Move(b));
        Run(r, InputEvent.Move(c));
        Assert.Equal(["hover box", "hover none"], g.Calls);
    }

    [Fact]
    public void PausedRouterDropsGameInputButKeepsAppKeys()
    {
        var (r, g) = Make();
        r.GameInputEnabled = false;
        var cmds = Run(r, InputEvent.Down(Key.A), InputEvent.Typed('a'), InputEvent.Down(Key.F10));
        Assert.Empty(g.Calls);
        Assert.Equal([AppCommand.TogglePause], cmds);
    }

    [Fact]
    public void GamepadDrivesTheKeyboardAxes()
    {
        var (r, g) = Make();
        Run(r, InputEvent.Axis(PadAxis.RightTrigger, 0.6f), InputEvent.Axis(PadAxis.LeftX, -1f), InputEvent.Axis(PadAxis.LeftX, 0.05f),
            InputEvent.PadKey(PadButton.A, true), InputEvent.PadKey(PadButton.A, false), InputEvent.Axis(PadAxis.LeftTrigger, -1f));
        Assert.Equal(["axis 200 0.6", "axis 203 1", "axis 203 0", "down 28 '\r'", "up 28"], g.Calls);
        Assert.Equal(0f, GamepadMapping.Shape(0.1f));
        Assert.Equal(1f, GamepadMapping.Shape(1f));
        Assert.Equal(-0.5f, GamepadMapping.Shape(-0.575f), 0.001f);
    }
}
