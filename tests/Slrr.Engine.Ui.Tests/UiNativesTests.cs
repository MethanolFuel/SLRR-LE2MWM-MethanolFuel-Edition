using Slrr.Vm;
using Xunit;

namespace Slrr.Engine.Ui.Tests;

/// <summary>The UI natives exercised through the shipped script classes (Viewport, Text, Sound, Input, Controller, GfxEngine).</summary>
public class UiNativesTests
{
    private static Value S(UiDriver d, string s) => Value.Obj(d.Vm.NewString(s));

    [Fact]
    public void RegistersExactlyTheExeNativesOfTheUiClasses()
    {
        var keys = UiNatives.Keys.Select(k => k.ToString()).ToHashSet(StringComparer.Ordinal);
        Assert.Equal(64, keys.Count);
        if (TestWorld.Root.Value is not { } root)
        {
            return;
        }

        var classes = new[]
        {
            UiNatives.CameraClass, UiNatives.GfxClass, UiNatives.TextClass, UiNatives.ViewportClass, UiNatives.SoundClass,
            UiNatives.InputClass, UiNatives.ControllerClass, UiNatives.CursorClass,
        };
        var exe = File.ReadLines(Path.Combine(root, "docs", "research", "natives-redline.tsv")).Skip(1)
            .Select(l => l.Split('\t')).Where(p => p.Length >= 4 && classes.Contains(p[1]))
            .Select(p => $"{p[1]}.{p[2]}{p[3]}").ToHashSet(StringComparer.Ordinal);
        Assert.Equal(64, exe.Count);
        Assert.Empty(exe.Except(keys));
    }

    [Fact]
    public void ViewportLetterboxAndAspect()
    {
        if (TestWorld.NewDriver() is not { } d)
        {
            return;
        }

        var cls = d.Vm.LoadClass(UiNatives.ViewportClass);
        // Osd(1.0, 0.0, 10) -> new Viewport(10, 0.5, 0.5, 1.0, 0.0, -0.5, -0.5): aspect 0 = whole screen.
        var vp = d.Vm.Construct(cls, Value.Int(10), Value.Float(0.5f), Value.Float(0.5f), Value.Float(1f), Value.Float(0f), Value.Float(-0.5f), Value.Float(-0.5f));
        var m = d.Engine.FindViewport(vp)!;
        Assert.Equal((10, 0f, 0f, 1f, 1f), (m.Layer, m.Left, m.Top, m.Width, m.Height));
        Assert.Equal(800f / 600f, Script.Call(d.Vm, vp, "getAspect").AsFloat, 0.0001f);

        // A 16:9 dialog on the 4:3 (800x600 Config default) screen is letterboxed vertically.
        var wide = d.Vm.Construct(cls, Value.Int(22), Value.Float(0.5f), Value.Float(0.5f), Value.Float(0.9f), Value.Float(16f / 9f), Value.Float(-0.5f), Value.Float(-0.5f));
        var w = d.Engine.FindViewport(wide)!;
        Assert.Equal(0.9f, w.Width, 0.0001f);
        Assert.Equal(0.9f * (4f / 3f) / (16f / 9f), w.Height, 0.0001f);
        Assert.Equal(16f / 9f, Script.Call(d.Vm, wide, "getAspect").AsFloat, 0.0001f);

        Script.Call(d.Vm, vp, "activate");
        Assert.True(m.Active);
        Assert.Equal(1, m.RenderFlags); // the no-arg script form passes RENDERFLAG_CLEARDEPTH
        Script.Call(d.Vm, vp, "resize", Value.Float(0.1f), Value.Float(0.2f), Value.Float(0.3f), Value.Float(0.4f));
        Assert.Equal(0.4f, Script.Call(d.Vm, vp, "getHeight").AsFloat);
        Script.Call(d.Vm, vp, "finalize");
        Assert.True(m.Destroyed);
        Assert.False(m.Active);
    }

    [Fact]
    public void TextInstanceTracksStringColourAndAlignment()
    {
        if (TestWorld.NewDriver() is not { } d)
        {
            return;
        }

        var font = d.Vm.Construct(d.Vm.LoadClass("java.util.resource.ResourceRef"), Script.GetStatic(d.Vm, UiNatives.TextClass, "RID_SLII24"));
        var text = d.Vm.Construct(d.Vm.LoadClass(UiNatives.TextClass), Value.Null, Value.Obj(font), S(d, "hello"), Value.Float(0.25f), Value.Float(-0.5f));
        var m = d.Engine.FindText(text)!;
        Assert.Equal(("hello", 0.25f, -0.5f, -1, 1), (m.Text, m.X, m.Y, m.Color, m.Updates));
        Assert.Same(font, m.Font);
        Script.Call(d.Vm, text, "changeText", S(d, "world"));
        Script.Call(d.Vm, text, "changeColor", Value.Int(-65536));
        Script.Call(d.Vm, text, "changeAlign", Value.Int(1));
        Assert.Equal(("world", -65536, 1, 2), (m.Text, m.Color, m.Align, m.Updates));

        // getWidthPixels: guessed metric, 0.5 * font size (Text.getFontSize: SLII24 -> 24) per character.
        Assert.Equal(60f, Script.CallStatic(d.Vm, UiNatives.TextClass, "getWidthPixels", null, S(d, "hello"), Value.Obj(font)).AsFloat);
        var pos = Script.Call(d.Vm, text, "getPos").AsObject!;
        Assert.Equal(0.25f, Script.GetFloat(pos, "x"));
    }

    [Fact]
    public void FrameWakesThreadsWaitingOnFrontendRender()
    {
        if (TestWorld.NewDriver() is not { } d)
        {
            return;
        }

        Assert.False(d.NotifyRender()); // nothing before Frontend.init
        d.BootFrontend();
        var render = Script.GetStatic(d.Vm, "java.render.Frontend", "render").AsObject!;
        var loading = Script.GetStatic(d.Vm, "java.render.Frontend", "loadingScreen").AsObject!;
        var thread = d.Vm.Threads.Single(t => ReferenceEquals(t.ThreadObject, loading));
        Assert.Same(render, thread.WaitingOn); // LoadingScreen.run: Frontend.render.wait()
        d.Frame();
        // Woken by the frame; System.isLoading() is 0 (stand-in), so it hides and waits on itself.
        Assert.Same(loading, thread.WaitingOn);
        Assert.Equal(1, d.Engine.Video.Frames);

        // Sound.init: no debug display headless; Input: one controller, the cursor bound to it.
        Assert.Null(Script.GetStatic(d.Vm, UiNatives.SoundClass, "vp").AsObject);
        Assert.NotNull(d.Controller0);
        Assert.Same(d.Controller0, Script.GetObj(d.Cursor!, "controller"));
        Assert.Contains(d.Engine.Viewports, v => v.Layer == 127); // the mouse-cursor viewport
    }

    [Fact]
    public void SoundVolumesMusicAndHardware()
    {
        if (TestWorld.NewDriver() is not { } d)
        {
            return;
        }

        Script.CallStatic(d.Vm, UiNatives.SoundClass, "setVolume", null, Value.Int(1), Value.Float(0.8f));
        Script.CallStatic(d.Vm, UiNatives.SoundClass, "increaseVolume", null, Value.Int(1), Value.Float(0.5f));
        Assert.Equal(1f, d.Engine.Sound.Volumes[1]); // script clamps to 1.0
        Script.CallStatic(d.Vm, UiNatives.SoundClass, "decreaseVolume", null, Value.Int(2), Value.Float(0.25f));
        Assert.Equal(0.75f, Script.CallStatic(d.Vm, UiNatives.SoundClass, "getVolume", null, Value.Int(2)).AsFloat);
        Script.CallStatic(d.Vm, UiNatives.SoundClass, "changeMusicSet", null, Script.GetStatic(d.Vm, UiNatives.SoundClass, "MUSIC_SET_MENU"));
        Script.CallStatic(d.Vm, UiNatives.SoundClass, "nextTrack", "()V");
        Assert.Equal((3, 1), (d.Engine.Sound.MusicSet, d.Engine.Sound.Track));
    }

    [Fact]
    public void KeysControllerMappingAndAxisQueries()
    {
        if (TestWorld.NewDriver() is not { } d)
        {
            return;
        }

        d.BootFrontend();
        var ctl = d.Controller0!;
        int select = d.InputConst("AXIS_SELECT"), enter = d.InputConst("RCDIK_ENTER");
        Assert.Equal(1, Script.Call(d.Vm, ctl, "user_Add", Value.Int(select), Value.Int(0), Value.Int(enter), Value.Float(0), Value.Float(1), Value.Float(0), Value.Float(1)).AsInt);

        d.Engine.KeyDown(enter, '\r');
        Assert.Equal(1f, Script.Call(d.Vm, ctl, "user_GetAxisVal", Value.Int(select)).AsFloat);
        Assert.Equal(128, d.Engine.AxisInfo(ctl, select));
        Assert.Contains(new InputEdge(true, select, true), d.Engine.Input.OsdEdges);
        Assert.Contains(new InputEdge(false, enter, true), d.Engine.Input.OsdEdges);
        Assert.Equal(enter, Script.CallStatic(d.Vm, UiNatives.InputClass, "activeAxis", null, Value.Int(0)).AsInt);
        Assert.Equal("Enter", Script.CallStatic(d.Vm, UiNatives.InputClass, "axisName", null, Value.Int(0), Value.Int(enter)).Ref!.Native);
        Assert.Equal(1f, Script.CallStatic(d.Vm, UiNatives.InputClass, "getAxis", null, Value.Int(0), Value.Int(enter)).AsFloat);

        // Input.flushKeys: drains lastKey() and the pending hotkey edges.
        Script.CallStatic(d.Vm, UiNatives.InputClass, "flushKeys", "()V");
        Assert.Empty(d.Engine.Input.KeyQueue);
        Assert.Empty(d.Engine.Input.OsdEdges);

        d.Engine.KeyUp(enter);
        Assert.Equal(-1, Script.CallStatic(d.Vm, UiNatives.InputClass, "activeAxis", null, Value.Int(0)).AsInt);
        Script.Call(d.Vm, ctl, "user_Reset");
        Assert.Empty(d.Engine.Controller(ctl).Mappings);
        Assert.Equal("SysKeyboard", Script.CallStatic(d.Vm, UiNatives.InputClass, "getDeviceName", null, Value.Int(0)).Ref!.Native);
        Assert.True(Script.CallStatic(d.Vm, UiNatives.InputClass, "getDeviceName", null, Value.Int(5)).IsNull);
    }

    [Fact]
    public void GlobalHotkeysAreDeliveredByTheFramePump()
    {
        if (TestWorld.NewDriver() is not { } d)
        {
            return;
        }

        d.BootFrontend();
        var osd = d.Vm.Construct(d.Vm.LoadClass("java.render.Osd"));
        // new Hotkey(key, flags, command, handler) registers a GLOBAL hotkey (Input.createGlobalHotkey, osd == null).
        var hk = d.Vm.Construct(d.Vm.LoadClass("java.io.Hotkey"), Value.Int(d.InputConst("AXIS_HELP")), Value.Int(1),
            Value.Int(unchecked((int)0x80040000)), Value.Obj(osd));
        Assert.Contains(d.Engine.Hotkeys, h => ReferenceEquals(h.Hotkey, hk) && h.Osd is null && ReferenceEquals(h.Handler, osd));
        d.Engine.SetVirtualAxis(d.InputConst("AXIS_HELP"), 1f);
        d.Frame();
        Assert.Contains(d.Engine.DispatchLog, l => l.StartsWith("THRD-HOTKEY: java.render.Osd.handleEvent", StringComparison.Ordinal));
        Assert.Equal(1, Script.GetInt(hk, "state"));
        Assert.Empty(d.Errors);
        Script.Call(d.Vm, hk, "inactivate");
        Assert.DoesNotContain(d.Engine.Hotkeys, h => ReferenceEquals(h.Hotkey, hk));
    }

    [Fact]
    public void VideoModeAndPlayback()
    {
        if (TestWorld.NewDriver() is not { } d)
        {
            return;
        }

        Assert.Equal((800, 600, 32), d.Engine.VideoMode); // Config defaults
        Script.CallStatic(d.Vm, UiNatives.GfxClass, "changeVideoMode", null, Value.Int(1024), Value.Int(768), Value.Int(32));
        Assert.Equal(2, Script.CallStatic(d.Vm, UiNatives.GfxClass, "currDisplayMode", "()I").AsInt);
        Assert.Equal("1024x768x32", Script.CallStatic(d.Vm, UiNatives.GfxClass, "displayModeName", null, Value.Int(2)).Ref!.Native);

        d.Engine.Video.VideoLength = 1f;
        Assert.Equal(0, Script.CallStatic(d.Vm, UiNatives.GfxClass, "openVideo", null, S(d, "Data\\Fmv\\Prime.avi"), Value.Int(1), Value.Int(1)).AsInt);
        Assert.Equal(1, Script.CallStatic(d.Vm, UiNatives.GfxClass, "isPlayingVideo", "()I").AsInt);
        d.RunFor(1.1f);
        Assert.Equal(0, Script.CallStatic(d.Vm, UiNatives.GfxClass, "isPlayingVideo", "()I").AsInt);
        Script.CallStatic(d.Vm, UiNatives.GfxClass, "printScreen", null, S(d, "screenshots\\sl_shot100.tga"));
        Assert.Equal(["screenshots\\sl_shot100.tga"], d.Engine.Video.Screenshots);
    }
}
