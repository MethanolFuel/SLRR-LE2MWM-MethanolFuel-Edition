using System.Numerics;
using Silk.NET.Input;
using Slrr.App;
using Slrr.Engine.Ui;
using Xunit;

namespace Slrr.App.Tests;

public class ViewportMapperTests
{
    [Fact]
    public void FitLetterboxesAWideWindowAndPillarboxesATallOne()
    {
        var wide = new ViewportMapper(new Vector2(1920, 1080), new Vector2(1920, 1080), AspectMode.Fit);
        Assert.Equal(new PixelRect(240, 0, 1440, 1080), wide.Picture);
        var tall = new ViewportMapper(new Vector2(800, 1000), new Vector2(800, 1000), AspectMode.Fit);
        Assert.Equal(new PixelRect(0, 200, 800, 600), tall.Picture);
        var exact = new ViewportMapper(new Vector2(1024, 768), new Vector2(1024, 768), AspectMode.Fit);
        Assert.Equal(new PixelRect(0, 0, 1024, 768), exact.Picture);
        var stretch = new ViewportMapper(new Vector2(1920, 1080), new Vector2(1920, 1080), AspectMode.Stretch);
        Assert.Equal(new PixelRect(0, 0, 1920, 1080), stretch.Picture);
    }

    [Fact]
    public void WindowPointsMapToOsdAndVirtualUiSpace()
    {
        var m = new ViewportMapper(new Vector2(1920, 1080), new Vector2(1920, 1080), AspectMode.Fit);
        var centre = m.WindowToGame(new Vector2(960, 540));
        Assert.True(centre.Inside);
        Assert.Equal(0f, centre.Osd.X, 0.001f);
        Assert.Equal(0f, centre.Osd.Y, 0.001f);
        Assert.Equal(400f, centre.Virtual.X, 0.01f);
        Assert.Equal(300f, centre.Virtual.Y, 0.01f);

        var topLeft = m.WindowToGame(new Vector2(240, 0));
        Assert.True(topLeft.Inside);
        Assert.Equal(-1f, topLeft.Osd.X, 0.001f);
        Assert.Equal(-1f, topLeft.Osd.Y, 0.001f); // OSD y grows downwards (MouseCursor.getPos, Osd text coordinates)

        var bar = m.WindowToGame(new Vector2(100, 540));
        Assert.False(bar.Inside);
        Assert.Equal(-1f, bar.Osd.X, 0.001f); // clamped to the picture edge
    }

    [Fact]
    public void RetinaFramebufferIsTwiceTheWindowAndMouseCoordinatesStayInWindowUnits()
    {
        // macOS Retina: GLFW reports the cursor in points, the framebuffer in pixels.
        var m = new ViewportMapper(new Vector2(1024, 768), new Vector2(2048, 1536), AspectMode.Fit);
        Assert.Equal(new Vector2(2, 2), m.ContentScale);
        Assert.Equal(new PixelRect(0, 0, 2048, 1536), m.Picture);
        Assert.Equal((2048, 1536), m.RenderSize);
        var p = m.WindowToGame(new Vector2(512, 384));
        Assert.Equal(0f, p.Osd.X, 0.001f);
        Assert.Equal(0f, p.Osd.Y, 0.001f);
        var q = m.WindowToGame(new Vector2(1023.9f, 767.9f));
        Assert.True(q.Inside);
        Assert.Equal(800f, q.Virtual.X, 0.5f);

        var half = new ViewportMapper(new Vector2(1024, 768), new Vector2(2048, 1536), AspectMode.Fit, 0.5f);
        Assert.Equal((1024, 768), half.RenderSize);
    }

    [Theory]
    [InlineData(800, 600, 800, 600, AspectMode.Fit)]
    [InlineData(1920, 1080, 1920, 1080, AspectMode.Fit)]
    [InlineData(1440, 900, 2880, 1800, AspectMode.Fit)]
    [InlineData(1280, 1024, 1280, 1024, AspectMode.Stretch)]
    [InlineData(1366, 768, 1366, 768, AspectMode.Stretch)]
    [InlineData(700, 900, 1400, 1800, AspectMode.Fit)]
    public void OsdToWindowInvertsWindowToGame(int ww, int wh, int fw, int fh, AspectMode aspect)
    {
        var m = new ViewportMapper(new Vector2(ww, wh), new Vector2(fw, fh), aspect);
        for (var x = -0.95f; x < 1f; x += 0.1f)
        {
            for (var y = -0.95f; y < 1f; y += 0.1f)
            {
                var w = m.OsdToWindow(new Vector2(x, y));
                var g = m.WindowToGame(w);
                Assert.True(g.Inside);
                Assert.Equal(x, g.Osd.X, 0.001f);
                Assert.Equal(y, g.Osd.Y, 0.001f);
            }
        }
    }

    [Fact]
    public void PictureGlFlipsToBottomLeftOrigin()
    {
        var m = new ViewportMapper(new Vector2(800, 1000), new Vector2(800, 1000), AspectMode.Fit);
        Assert.Equal((0, 200, 800, 800), m.PictureGl()); // picture at y 200..800 from the top = 200..800 from the bottom (symmetric)
        var w = new ViewportMapper(new Vector2(1000, 600), new Vector2(1000, 600), AspectMode.Fit);
        Assert.Equal((100, 0, 900, 600), w.PictureGl());
    }

    [Fact]
    public void DegenerateSizesDoNotDivideByZero()
    {
        var m = new ViewportMapper(Vector2.Zero, Vector2.Zero, AspectMode.Fit);
        var g = m.WindowToGame(new Vector2(5, 5));
        Assert.True(float.IsFinite(g.Osd.X) && float.IsFinite(g.Osd.Y));
        Assert.True(m.RenderSize.W >= 64 && m.RenderSize.H >= 48);
    }
}

public class KeyMapTests
{
    [Theory]
    [InlineData(Key.Escape, 1)]
    [InlineData(Key.Number1, 2)]
    [InlineData(Key.Number0, 11)]
    [InlineData(Key.Q, 16)]
    [InlineData(Key.A, 30)]
    [InlineData(Key.Z, 44)]
    [InlineData(Key.M, 50)]
    [InlineData(Key.Enter, 28)]
    [InlineData(Key.Space, 57)]
    [InlineData(Key.F1, 59)]
    [InlineData(Key.F10, 68)]
    [InlineData(Key.F11, 87)]
    [InlineData(Key.Up, 200)]
    [InlineData(Key.Left, 203)]
    [InlineData(Key.Right, 205)]
    [InlineData(Key.Down, 208)]
    [InlineData(Key.KeypadEnter, 156)]
    [InlineData(Key.Keypad0, 82)]
    public void DirectInputScanCodes(Key key, int dik) => Assert.Equal(dik, KeyMap.ToDik(key));

    /// <summary>The key names the Ui area shows for DIK codes (InputState.KeyNames) agree with the Silk.NET key that produces each code.</summary>
    [Fact]
    public void AgreesWithTheUiAreasKeyNames()
    {
        var checkedNames = 0;
        foreach (var key in Enum.GetValues<Key>().Distinct())
        {
            var dik = KeyMap.ToDik(key);
            if (dik == 0 || !InputState.KeyNames.TryGetValue(dik, out var name))
            {
                continue;
            }

            var expected = key switch
            {
                >= Key.A and <= Key.Z => key.ToString(),
                >= Key.Number0 and <= Key.Number9 => ((char)('0' + (key - Key.Number0))).ToString(),
                >= Key.F1 and <= Key.F12 => key.ToString(),
                >= Key.Keypad0 and <= Key.Keypad9 => "Num " + (key - Key.Keypad0),
                Key.Escape => "Esc",
                Key.Backspace => "Backspace",
                Key.Tab => "Tab",
                Key.Enter => "Enter",
                Key.Space => "Space",
                Key.Up or Key.Down or Key.Left or Key.Right => key.ToString(),
                Key.PageUp => "PgUp",
                Key.PageDown => "PgDn",
                Key.Comma => ",",
                Key.Period => ".",
                Key.NumLock => "NumLock",
                Key.KeypadDivide => "Num /",
                Key.KeypadMultiply => "Num *",
                Key.KeypadSubtract => "Num -",
                Key.KeypadAdd => "Num +",
                Key.KeypadEnter => "Num Enter",
                Key.KeypadDecimal => "Num .",
                _ => null,
            };
            Assert.True(expected is not null, $"{key} -> DIK {dik} '{name}' has no expectation");
            Assert.Equal(expected, name);
            checkedNames++;
        }

        Assert.True(checkedNames >= 70, $"only {checkedNames} keys checked");
    }

    [Fact]
    public void NoTwoKeysShareAScanCode()
    {
        var byDik = Enum.GetValues<Key>().Distinct().Select(k => (k, d: KeyMap.ToDik(k))).Where(x => x.d != 0).GroupBy(x => x.d).Where(g => g.Count() > 1).ToList();
        Assert.Empty(byDik);
    }

    [Fact]
    public void ControlCharactersAreTheOnesStringInputTests()
    {
        Assert.Equal('\r', KeyMap.ControlChar(Key.Enter));
        Assert.Equal((char)27, KeyMap.ControlChar(Key.Escape));
        Assert.Equal('\b', KeyMap.ControlChar(Key.Backspace));
        Assert.Equal('\0', KeyMap.ControlChar(Key.A));
    }

    [Fact]
    public void ParsesScriptKeyNames()
    {
        Assert.Equal(Key.Escape, KeyMap.Parse("Esc"));
        Assert.Equal(Key.Enter, KeyMap.Parse("return"));
        Assert.Equal(Key.Up, KeyMap.Parse("up"));
        Assert.Equal(Key.Number5, KeyMap.Parse("5"));
        Assert.Equal(Key.F12, KeyMap.Parse("F12"));
        Assert.Throws<ArgumentException>(() => KeyMap.Parse("nope"));
        Assert.Equal((Key.B, true), KeyMap.ForChar('B'));
        Assert.Equal((Key.Number7, false), KeyMap.ForChar('7'));
        Assert.True(KeyMap.ProducesChar(Key.A) && KeyMap.ProducesChar(Key.Space) && !KeyMap.ProducesChar(Key.Up) && !KeyMap.ProducesChar(Key.Enter));
    }
}

public class FixedStepClockTests
{
    [Fact]
    public void RunsWholeStepsAndCarriesTheRemainder()
    {
        var c = new FixedStepClock(1.0 / 30);
        Assert.Equal(0, c.Advance(1.0 / 60));
        Assert.Equal(1, c.Advance(1.0 / 60));
        var total = 0;
        for (var i = 0; i < 600; i++)
        {
            total += c.Advance(1.0 / 60); // 10 s at 60 Hz display
        }

        Assert.Equal(300, total);
        Assert.Equal(0, c.Dropped, 6);
    }

    [Fact]
    public void ClampsLongFramesAndCapsStepsPerFrame()
    {
        var c = new FixedStepClock(1.0 / 30, maxSteps: 4, maxFrame: 0.25);
        Assert.Equal(4, c.Advance(2.0)); // 0.25 s = 7.5 steps -> 4, the rest dropped
        Assert.True(c.Dropped > 1.8);
        Assert.Equal(0, c.Advance(double.NaN));
        Assert.Equal(0, c.Advance(-1));
        c.Reset();
        Assert.Equal(0, c.Alpha, 6);
    }
}
