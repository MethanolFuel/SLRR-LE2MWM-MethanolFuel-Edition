using Slrr.Vm;
using Xunit;

namespace Slrr.Engine.Ui.Tests;

/// <summary>
/// The shipped OptionsDialog, Catalog and MenuDialog/YesNoDialog driven headlessly: opened as the game opens them,
/// operated with simulated clicks (cursor events to the gadget's Group) and keys (hotkeys polled by the shipped
/// HotkeyWatcher thread), with assertions on the resulting script and engine state. Passes vacuously without game data.
/// </summary>
public class HeadlessDialogTests
{
    /// <summary>Boot the front end (Sound/Frontend/Input init as in Init.&lt;init&gt;) and a Player with controllers[0].</summary>
    private static (UiDriver D, VmObject Player)? Boot()
    {
        if (TestWorld.NewDriver() is not { } d)
        {
            return null;
        }

        d.BootFrontend();
        var player = d.Vm.Construct(d.Vm.LoadClass("java.game.Player"));
        Script.Set(player, "controller", Value.Obj(d.Controller0));
        Script.Set(player, "money", Value.Int(50000));
        Script.SetStatic(d.Vm, "java.game.GameLogic", "player", Value.Obj(player));
        return (d, player);
    }

    private static void NoErrors(UiDriver d) =>
        Assert.True(d.Errors.Count == 0, string.Join("\n", d.Errors.Select(e => $"{e.Thread.Name}: {e.Error}")));

    private static int Field(VmObject o, string name) => Script.GetInt(o, name);

    [Fact]
    public void OptionsDialogOpensAndNavigatesWithClicksAndKeys()
    {
        if (Boot() is not var (d, _))
        {
            return;
        }

        var dlg = d.Vm.Construct(d.Vm.LoadClass("java.game.OptionsDialog"), Value.Int(0));
        var display = d.Start(dlg, "display");
        d.RunFrames(3);
        NoErrors(d);
        var osd = Script.GetObj(dlg, "osd")!;

        // display() shows the dialog and blocks its thread until osdCommand -> notify.
        Assert.Equal(VmThreadState.Waiting, display.State);
        Assert.Same(dlg, display.WaitingOn);
        Assert.Same(osd, d.TopOsd());
        var vp = d.Engine.FindViewport(Script.GetObj(osd, "vp"))!;
        Assert.True(vp.Active);
        Assert.Equal(20, vp.Layer); // Dialog priority 22 - 2 (DF_LOWPRI)
        Assert.Equal(3, vp.RenderFlags); // CLEARDEPTH | CLEARTARGET (Osd.show)
        Assert.Equal(1, Field(d.Cursor!, "visible"));
        Assert.Equal(-1, Field(dlg, "actGroup"));
        Assert.DoesNotContain("video options", d.VisibleStrings()); // every page is built hidden

        // The main menu (MainMenuDialog) opens the options page with osdCommand(CMD_OPTIONS).
        d.Start(dlg, "osdCommand", Value.Int(0));
        Assert.Equal(Field(dlg, "optionsGroup"), Field(dlg, "actGroup"));
        Assert.Contains("sound options", d.VisibleStrings());
        Assert.Contains("advanced game options", d.VisibleStrings());

        // Mouse: click "sound options".
        Assert.True(d.ClickLabel(osd, "sound options"));
        NoErrors(d);
        Assert.Equal(Field(dlg, "soundGroup"), Field(dlg, "actGroup"));
        var visible = d.VisibleStrings().ToList();
        Assert.Contains("effects volume", visible);
        Assert.Contains("mixing", visible);
        Assert.Contains("AUTO", visible); // Config.Sound_Mix_HW = 2 -> soundHWMulti value 3
        Assert.DoesNotContain("video options", visible);

        // Click the effects-volume slider at 25 % of its track. The hit box is the slider's own Button (same command,
        // parent = the Slider); Group.handleEvent forwards to Slider.click, which maps Input.cursor.getPos().
        var hit = d.FindGadget(osd, 15)!; // CMD_EFFECTS_VOL
        Assert.Equal("Button", hit.Kind);
        var slider = Script.GetObj(hit.Gadget, "parent")!;
        Assert.Equal("java.render.osd.Slider", slider.Class.Name);
        float x0 = Script.GetFloat(slider, "x0"), x1 = Script.GetFloat(slider, "x1");
        d.Engine.Input.CursorX = x0 + (0.25f * (x1 - x0));
        Assert.True(d.Click(hit));
        NoErrors(d);
        Assert.Equal(0.25f, d.Engine.Sound.Volumes[0], 0.001f);
        Assert.Equal(0.25f, Script.GetFloat(slider, "value"), 0.001f);

        // Click the "mixing" MultiChoice: AUTO (3) wraps to SOFTWARE (0) and osdCommand(CMD_SOUND_HW) writes Config.
        Assert.True(d.ClickCommand(osd, 39));
        NoErrors(d);
        Assert.Equal(0, Script.GetStatic(d.Vm, "java.util.Config", "Sound_Mix_HW").AsInt);
        Assert.Contains("SOFTWARE (SLOW!)", d.VisibleStrings());

        // Keyboard: AXIS_CANCEL is the sound page's hotkey for CMD_OPTIONS (delivered by the HotkeyWatcher thread).
        d.TapAxis(d.InputConst("AXIS_CANCEL"));
        NoErrors(d);
        Assert.Equal(Field(dlg, "optionsGroup"), Field(dlg, "actGroup"));

        // Keyboard: the selection starts on "video options"; DOWN moves to "sound options", SELECT clicks it.
        d.TapAxis(d.InputConst("AXIS_MENU_DOWN"));
        var pointed = Script.GetObj(osd, "pointedGadget")!;
        Assert.Equal(3, Field(pointed, "cmd"));
        Assert.Equal(-13568, d.Engine.FindText(Script.GetObj(Script.GetObj(pointed, "label")!, "txt"))!.Color); // COLOR_SELECTED
        d.TapAxis(d.InputConst("AXIS_SELECT"));
        NoErrors(d);
        Assert.Equal(Field(dlg, "soundGroup"), Field(dlg, "actGroup"));

        // Back to the options page with a click on "back", then game options: toggle the metric system.
        Assert.True(d.ClickLabel(osd, "back"));
        Assert.True(d.ClickLabel(osd, "game options"));
        Assert.Equal(Field(dlg, "gameGroup"), Field(dlg, "actGroup"));
        var metric = Script.GetStatic(d.Vm, "java.util.Config", "metricSystem").AsInt;
        Assert.True(d.ClickCommand(osd, 28)); // CMD_METRIC
        NoErrors(d);
        Assert.Equal(1 - metric, Script.GetStatic(d.Vm, "java.util.Config", "metricSystem").AsInt);
        Assert.Contains(metric == 0 ? "Kilometres" : "Miles", d.VisibleStrings());

        // A hidden page's gadgets cannot be hit.
        Assert.False(d.ClickLabel(osd, "effects volume"));

        // The display thread is still waiting (OptionsDialog never ends display by itself; its subclasses do).
        Assert.Equal(VmThreadState.Waiting, display.State);
        Assert.True(d.Engine.Video.Frames > 0);
    }

    [Fact]
    public void CatalogEntersAndBrowsesPages()
    {
        if (Boot() is not var (d, _))
        {
            return;
        }

        Script.SetStatic(d.Vm, "java.game.GameLogic", "ChassisBox", Value.Obj(d.Vm.Construct(d.Vm.LoadClass("java.util.Vector"))));
        var cat = d.Vm.Construct(d.Vm.LoadClass("java.game.Catalog"));
        d.Start(cat, "enter", Value.Null);
        d.RunFrames(2);
        NoErrors(d);

        var osd = Script.GetObj(cat, "osd")!;
        Assert.Same(osd, d.TopOsd());
        Assert.Equal(1, Field(d.Cursor!, "visible"));
        Assert.NotNull(d.Engine.Video.GlobalEnvmap);
        var front = d.VisibleStrings().ToList();
        Assert.Contains("ENGINE", front);
        Assert.Contains("DECALS", front);
        Assert.Contains("$50000", front); // refreshMoneyString
        Assert.DoesNotContain("V8 ENGINE", front);

        // ENGINE -> engine families page.
        Assert.True(d.ClickLabel(osd, "ENGINE"));
        NoErrors(d);
        Assert.Equal(Field(cat, "main1Group"), Field(cat, "actGroup"));
        Assert.Contains("V8 ENGINE", d.VisibleStrings());
        Assert.DoesNotContain("DECALS", d.VisibleStrings());

        // V8 ENGINE -> the V8 parts index.
        Assert.True(d.ClickLabel(osd, "V8 ENGINE"));
        NoErrors(d);
        Assert.Equal(Field(cat, "main1_4Group"), Field(cat, "actGroup"));
        Assert.Contains("CAMSHAFTS", d.VisibleStrings());

        // ESC (AXIS_CANCEL hotkey of the page) goes back one level, twice.
        d.TapAxis(d.InputConst("AXIS_CANCEL"));
        Assert.Equal(Field(cat, "main1Group"), Field(cat, "actGroup"));
        d.TapAxis(d.InputConst("AXIS_CANCEL"));
        NoErrors(d);
        Assert.Equal(Field(cat, "mainGroup"), Field(cat, "actGroup"));
        Assert.Contains("DECALS", d.VisibleStrings());

        // DECALS -> manufacturers: a decal page with its page number (no decal files are found by the stand-in FindFile).
        Assert.True(d.ClickLabel(osd, "DECALS"));
        Assert.True(d.ClickCommand(osd, Script.GetStatic(d.Vm, "java.game.Catalog", "CMD_MAIN6_1").AsInt));
        NoErrors(d);
        Assert.Equal(Field(cat, "decalsGroup"), Field(cat, "actGroup"));
        Assert.Contains("D-I/1", d.VisibleStrings());
        Assert.Contains("D-I/2", d.VisibleStrings());

        // exit() hides the Osd and the pointer.
        d.Start(cat, "exit", Value.Null);
        NoErrors(d);
        Assert.False(d.Engine.FindViewport(Script.GetObj(osd, "vp"))!.Active);
        Assert.Equal(0, Field(d.Cursor!, "visible"));
        Assert.Empty(d.VisibleTexts().Where(t => d.TextsOf(osd).Contains(t)));
    }

    [Fact]
    public void MenuDialogReturnsTheItemChosenWithTheKeyboard()
    {
        if (Boot() is not var (d, player))
        {
            return;
        }

        var labels = (VmArray)d.Vm.NewArray("Ljava.lang.String;", 3);
        labels.Items[0] = Value.Obj(d.Vm.NewString("garage"));
        labels.Items[1] = Value.Obj(d.Vm.NewString("city"));
        labels.Items[2] = Value.Obj(d.Vm.NewString("race"));
        var flags = Script.GetStatic(d.Vm, "java.render.osd.dialog.Dialog", "DF_MODAL").AsInt;
        var dlg = d.Vm.Construct(d.Vm.LoadClass("java.render.osd.dialog.MenuDialog"),
            Script.Get(player, "controller"), Value.Int(flags), Value.Obj(d.Vm.NewString("GO TO")), Value.Null, Value.Obj(labels));
        var display = d.Start(dlg, "display");
        d.RunFrames(2);
        NoErrors(d);
        Assert.Equal(VmThreadState.Waiting, display.State);
        var osd = Script.GetObj(dlg, "osd")!;
        Assert.Equal(1, Field(osd, "flags") & 1); // OF_MODAL
        Assert.Contains("GO TO", d.VisibleStrings());
        Assert.Contains("CANCEL", d.VisibleStrings());

        // A modal dialog puts a back-shield Osd (IL_NONE) under it; only the dialog has focus.
        Assert.Same(osd, d.TopOsd());
        Assert.NotNull(Script.GetObj(dlg, "backShieldOsd"));

        d.TapAxis(d.InputConst("AXIS_MENU_DOWN"));
        d.TapAxis(d.InputConst("AXIS_SELECT"));
        NoErrors(d);
        Assert.False(display.IsAlive);
        Assert.Equal(2, display.Result.AsInt); // "city" = item 2 (MenuDialog numbers items from 1)
        Assert.False(d.Engine.FindViewport(Script.GetObj(osd, "vp"))!.Active);
        Assert.NotSame(osd, d.TopOsd());
    }

    [Fact]
    public void YesNoDialogAnswersAClick()
    {
        if (Boot() is not var (d, player))
        {
            return;
        }

        var flags = Script.GetStatic(d.Vm, "java.render.osd.dialog.Dialog", "DF_MODAL").AsInt;
        var dlg = d.Vm.Construct(d.Vm.LoadClass("java.render.osd.dialog.YesNoDialog"),
            Script.Get(player, "controller"), Value.Int(flags), Value.Obj(d.Vm.NewString("BUY")), Value.Obj(d.Vm.NewString("Really?")));
        var display = d.Start(dlg, "display");
        d.RunFrames(2);
        var osd = Script.GetObj(dlg, "osd")!;
        Assert.Contains("Really?", d.VisibleStrings().Concat(d.Engine.Texts.Select(t => t.Text ?? string.Empty)));
        Assert.True(d.ClickLabel(osd, "NO"));
        NoErrors(d);
        Assert.False(display.IsAlive);
        Assert.Equal(1, display.Result.AsInt);
    }
}
