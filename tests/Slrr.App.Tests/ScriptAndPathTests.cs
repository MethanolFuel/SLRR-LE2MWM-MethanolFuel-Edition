using System.Numerics;
using Silk.NET.Input;
using Slrr.App;
using Slrr.Formats.Tests;
using Xunit;

namespace Slrr.App.Tests;

internal sealed class FakeContext : IScriptContext
{
    public float Time { get; set; }

    public string Section { get; set; } = "MainMenu";

    public string Screen { get; set; } = "MainMenu";

    public bool InTrack { get; set; }

    public float SpeedKmh { get; set; }

    public HashSet<string> Texts { get; } = [];

    public Dictionary<string, ScriptTarget> Targets { get; } = [];

    public List<string> ClickLog { get; } = [];

    public List<string> Captured { get; } = [];

    public bool HasText(string text) => Texts.Contains(text);

    public ScriptTarget? Find(string? text, bool contains, int? command) => text is not null && Targets.TryGetValue(text, out var t) ? t : null;

    public int InputConst(string name) => 34;

    public ViewportMapper Mapper { get; } = new(new Vector2(1600, 900), new Vector2(3200, 1800), AspectMode.Fit);

    public IReadOnlyList<string> Clicks => ClickLog;

    public void Capture(string name) => Captured.Add(name);

    public string Describe() => $"{Section} {Screen}";
}

public class InputScriptTests
{
    private static ScriptTarget Target(string text, float x, float y) => new(new OsdRect(x, y, 0.1f, 0.05f), text);

    [Fact]
    public void TokenizerKeepsQuotedTextAndDropsComments()
    {
        Assert.Equal(["click", "Buy used cars # not a comment"], InputScript.Tokenize("click \"Buy used cars # not a comment\"  # comment"));
        Assert.Empty(InputScript.Tokenize("   # only a comment"));
        Assert.Equal(["type", ""], InputScript.Tokenize("type \"\""));
    }

    [Fact]
    public void ClickBecomesAMouseMovePressReleaseAtTheGadgetsWindowPosition()
    {
        var ctx = new FakeContext();
        ctx.Targets["NEW CAREER"] = Target("NEW CAREER", 0.5f, -0.2f);
        var s = InputScript.Parse("click \"NEW CAREER\"\ncapture shot\nquit");
        var events = new List<InputEvent>();
        s.Update(ctx, events);
        Assert.Equal([InputKind.MouseMove, InputKind.MouseDown, InputKind.MouseUp], events.Select(e => e.Kind));
        var g = ctx.Mapper.WindowToGame(events[1].Position);
        Assert.True(g.Inside);
        Assert.Equal(0.5f, g.Osd.X, 0.001f);
        Assert.Equal(-0.2f, g.Osd.Y, 0.001f);

        // the click is verified before the script goes on
        events.Clear();
        s.Update(ctx, events);
        Assert.Empty(ctx.Captured);
        ctx.ClickLog.Add("click NEW CAREER");
        s.Update(ctx, events);
        Assert.Equal(["shot"], ctx.Captured);
        Assert.False(s.QuitRequested); // capture yields one iteration so the picture shows the state before the next command
        s.Update(ctx, events); // the yield
        Assert.False(s.QuitRequested);
        s.Update(ctx, events);
        Assert.True(s.QuitRequested);
        Assert.False(s.Failed);
    }

    [Fact]
    public void AClickThatReachesNothingFailsTheScript()
    {
        var ctx = new FakeContext();
        ctx.Targets["OK"] = Target("OK", 0, 0.9f);
        var s = InputScript.Parse("click OK");
        var events = new List<InputEvent>();
        s.Update(ctx, events);
        ctx.Time = 2f;
        s.Update(ctx, events);
        Assert.True(s.Failed);
        Assert.Contains("reached no gadget", s.Error);
    }

    [Fact]
    public void TypeSendsKeyAndCharacterPairsSpacedInGameTime()
    {
        var ctx = new FakeContext();
        var s = InputScript.Parse("type \"Ab\"");
        var events = new List<InputEvent>();
        s.Update(ctx, events);
        Assert.Equal([InputEvent.Down(Key.A), InputEvent.Typed('A')], events);
        events.Clear();
        ctx.Time = InputScript.TapTime;
        s.Update(ctx, events);
        Assert.Equal([InputEvent.Up(Key.A)], events);
        events.Clear();
        ctx.Time = 2 * InputScript.TapTime;
        s.Update(ctx, events);
        Assert.Equal([InputEvent.Down(Key.B), InputEvent.Typed('b')], events);
    }

    [Fact]
    public void UntilTimesOutWithTheStateInTheMessage()
    {
        var ctx = new FakeContext();
        var s = InputScript.Parse("until section Garage 5\nquit");
        var events = new List<InputEvent>();
        s.Update(ctx, events);
        ctx.Time = 4;
        s.Update(ctx, events);
        Assert.False(s.Failed);
        ctx.Section = "Garage";
        s.Update(ctx, events);
        Assert.True(s.QuitRequested);

        var t = InputScript.Parse("until track 1");
        var c2 = new FakeContext();
        t.Update(c2, events);
        c2.Time = 2;
        t.Update(c2, events);
        Assert.True(t.Failed);
        Assert.Contains("MainMenu", t.Error);
    }

    [Fact]
    public void BuiltInDriveScriptParses()
    {
        var s = InputScript.Load("builtin:drive");
        Assert.True(s.Steps.Count > 30);
        Assert.Contains(s.Steps, x => x.Verb == "clickcmd");
        Assert.Throws<ArgumentException>(() => InputScript.Load("builtin:nope"));
    }
}

public class OptionsAndPathTests
{
    [Fact]
    public void ParsesTheCommandLine()
    {
        var o = AppOptions.Parse(["--root", "/g", "--size", "1280x960", "--aspect", "stretch", "--no-vsync", "--host-hz", "60", "--headless-frames", "10", "-psn_0_123"]);
        Assert.Equal("/g", o.Root);
        Assert.Equal((1280, 960), (o.Width, o.Height));
        Assert.Equal(AspectMode.Stretch, o.Aspect);
        Assert.False(o.VSync);
        Assert.Equal(60, o.HostHz);
        Assert.Equal(10, o.HeadlessFrames);
        Assert.Throws<ArgumentException>(() => AppOptions.Parse(["--size", "12"]));
        Assert.Throws<ArgumentException>(() => AppOptions.Parse(["--bogus"]));
        Assert.Throws<ArgumentException>(() => AppOptions.Parse(["--root"]));
        Assert.Throws<ArgumentException>(() => AppOptions.Parse(["--host-hz", "5"]));
    }

    [Fact]
    public void FindsTheGameRootCaseInsensitivelyAndRejectsWrongExplicitRoots()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "slrr-app-test-" + Guid.NewGuid().ToString("N")[..8]);
        var game = Path.Combine(tmp, "SLRR");
        var deep = Path.Combine(game, "tools", "bin");
        Directory.CreateDirectory(deep);
        try
        {
            File.WriteAllText(Path.Combine(game, "System.RPK"), "");
            File.WriteAllText(Path.Combine(game, "sl.rpk"), "");
            string? NoEnv(string _) => null;

            Assert.Equal(game, AppPaths.FindGameRoot(null, NoEnv, [deep], out _, out _));
            Assert.Equal(game, AppPaths.FindGameRoot(null, n => n == AppPaths.RootEnv ? game : null, [tmp], out _, out _));
            Assert.Null(AppPaths.FindGameRoot(deep, NoEnv, [game], out var err, out _));
            Assert.Contains("no system.rpk and no sl.rpk", err);
            Assert.Null(AppPaths.FindGameRoot(Path.Combine(tmp, "missing"), NoEnv, [], out err, out _));
            Assert.Contains("does not exist", err);
            Assert.Null(AppPaths.FindGameRoot(null, NoEnv, [tmp], out err, out var searched));
            Assert.Contains("--root", err);
            Assert.Contains(tmp, searched);
        }
        finally
        {
            Directory.Delete(tmp, true);
        }
    }

    [Fact]
    public void SaveRootDefaultsToTheUserDataFolder()
    {
        string? NoEnv(string _) => null;
        var save = AppPaths.SaveRoot(null, NoEnv);
        Assert.EndsWith(Path.Combine(AppPaths.AppFolderName, "save"), save);
        Assert.Equal(Path.GetFullPath("/x/y"), AppPaths.SaveRoot(null, n => n == AppPaths.SaveEnv ? "/x/y" : null));
        Assert.Equal(Path.GetFullPath("/a"), AppPaths.SaveRoot("/a", n => "/b"));
        Assert.True(AppPaths.IsInside("/g/save", "/g"));
        Assert.True(AppPaths.IsInside("/G/Save", "/g"));
        Assert.False(AppPaths.IsInside("/gx", "/g"));
        if (GameRoot.Find() is { } root)
        {
            Assert.False(AppPaths.IsInside(save, root));
        }
    }

    [Fact]
    public void BitmapFontCoversTheOverlayText()
    {
        foreach (var c in "0123456789 FPS FRAME MS HOST HZ STEP DRAW KM/H RPM GEAR AUDIO QUEUED UNDERRUNS PAUSED (F10) LOADING ...")
        {
            Assert.True(c == ' ' || BitmapFont.Pixels(c).Any(), $"glyph '{c}'");
        }

        Assert.Equal(BitmapFont.Pixels('?'), BitmapFont.Pixels('~'));
    }
}
