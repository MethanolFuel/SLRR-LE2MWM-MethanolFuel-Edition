using System.Globalization;
using Slrr.Vm;

namespace Slrr.Engine.Ui;

/// <summary>A gadget (Button/Slider/MultiChoice/StringInput) of an Osd as seen by the driver.</summary>
public sealed record GadgetInfo(int GroupIndex, VmObject Group, VmObject Gadget, int Command, string? Label, bool GroupActive, bool Disabled)
{
    public string Kind => Gadget.Class.Name[(Gadget.Class.Name.LastIndexOf('.') + 1)..];
}

/// <summary>
/// The engine's side of the frame loop and of user input, for tests and hosts: per-frame wake of
/// <c>Frontend.render</c>, hotkey delivery, simulated keys/axes and mouse clicks delivered with the scripts' own
/// cursor-event protocol (<c>Group.handleEvent(GameRef, EVENT_CURSOR, "EC_* ...")</c>), and read-only views of the
/// headless scene (visible texts, gadgets, focus).
/// </summary>
public sealed class UiDriver
{
    public UiDriver(Vm.Vm vm, UiEngine engine)
    {
        Vm = vm;
        Engine = engine;
        engine.Bind(vm);
    }

    public Vm.Vm Vm { get; }

    public UiEngine Engine { get; }

    /// <summary>Frame length used by <see cref="RunFrames"/> and the tap helpers (30 fps).</summary>
    public float FrameTime { get; set; } = 1f / 30f;

    /// <summary>Called at the start of every frame (e.g. an area that runs <c>animate</c>/<c>control</c> callbacks).</summary>
    public event Action<UiDriver>? BeforeFrame;

    /// <summary>A VM whose registry has the java.lang natives, the UI natives and whatever <paramref name="configure"/> adds.</summary>
    public static UiDriver Create(VmClassPath path, Action<NativeRegistry>? configure = null, IUiWorld? world = null)
    {
        var engine = new UiEngine(world);
        var registry = NativeRegistry.CreateDefault();
        registry.Add(new UiNatives(engine));
        configure?.Invoke(registry);
        var vm = new Vm.Vm(path, registry);
        return new UiDriver(vm, engine);
    }

    // ---------------------------------------------------------------- boot and frames

    /// <summary>
    /// The front-end part of <c>java.game.Init.&lt;init&gt;</c> (measured boot order): <c>Sound.init()</c>,
    /// <c>Frontend.init()</c> (render object, fonts, LoadingScreen thread, HotkeyWatcher thread),
    /// <c>Input.initControllers(owner, file)</c>, <c>Input.enablePointingDevice()</c>; then binds the cursor to the
    /// first controller (<c>MouseCursor.setController</c>) so the HotkeyWatcher polls. GameLogic is not created.
    /// </summary>
    public void BootFrontend(VmObject? owner = null, string controlFile = "save/controls/active_control_set")
    {
        Script.CallStatic(Vm, "java.sound.Sound", "init", "()V");
        Script.CallStatic(Vm, "java.render.Frontend", "init", "()V");
        Script.CallStatic(Vm, UiNatives.InputClass, "initControllers", null, Value.Obj(owner), Value.Obj(Vm.NewString(controlFile)));
        Script.CallStatic(Vm, UiNatives.InputClass, "enablePointingDevice", "()V");
        if (Controller0 is { } ctl && Cursor is { } cursor)
        {
            Script.Call(Vm, cursor, "setController", Value.Obj(ctl));
        }

        Settle();
    }

    /// <summary><c>Input.controllers[0]</c>.</summary>
    public VmObject? Controller0 =>
        Script.GetStatic(Vm, UiNatives.InputClass, "controllers").AsObject is VmArray a && a.Items.Length > 0 ? a.Items[0].AsObject : null;

    /// <summary><c>Input.cursor</c>.</summary>
    public VmObject? Cursor => Script.GetStatic(Vm, UiNatives.InputClass, "cursor").AsObject;

    /// <summary>
    /// One engine frame: global hotkeys, the wake of <c>Frontend.render</c> (exe 0x428db9: the static field is read
    /// by name and notified like <c>notifyAll</c>), then every script thread runs until it blocks or the frame's
    /// virtual time is used up.
    /// </summary>
    public void Frame(float? dt = null)
    {
        BeforeFrame?.Invoke(this);
        Engine.DeliverGlobalHotkeys();
        NotifyRender();
        var end = Vm.Time + (dt ?? FrameTime);
        Vm.RunUntilIdle(end);
        if (Vm.Time < end)
        {
            // RunUntilIdle only advances the clock while threads sleep; a frame always takes its time.
            Vm.SimTime += end - Vm.Time;
            Vm.Time = end;
        }

        Engine.Video.Frames++;
    }

    public void RunFrames(int n)
    {
        for (var i = 0; i < n; i++)
        {
            Frame();
        }
    }

    public void RunFor(float seconds)
    {
        var end = Vm.Time + seconds;
        while (Vm.Time < end - 1e-6f)
        {
            Frame(Math.Min(FrameTime, end - Vm.Time));
        }
    }

    /// <summary>Runs every ready thread until it blocks, without advancing the clock.</summary>
    public void Settle() => Vm.RunUntilIdle(Vm.Time);

    /// <summary>Wakes every thread waiting on <c>Frontend.render</c> (no-op before <c>Frontend.init</c>).</summary>
    public bool NotifyRender()
    {
        if (Script.PeekStatic(Vm, "java.render.Frontend", "render") is { Ref: { } render })
        {
            Vm.Notify(render, all: true);
            return true;
        }

        return false;
    }

    /// <summary>Starts <c>self.method(args)</c> on its own script thread (e.g. <c>dialog.display()</c>) and settles.</summary>
    public VmThread Start(VmObject self, string method, params Value[] args)
    {
        var m = self.Class.FindMethods(method, args.Length).FirstOrDefault(x => !x.IsStatic && x.Code is not null)
                ?? throw new VmException($"{self.Class.Name} has no method {method} with {args.Length} arguments");
        var t = Vm.Spawn(m, self, args, "host:" + method);
        Settle();
        return t;
    }

    // ---------------------------------------------------------------- keyboard / axes

    /// <summary>Presses then releases a virtual axis (<c>Input.AXIS_*</c>), giving the 50 ms HotkeyWatcher time to see both edges.</summary>
    public void TapAxis(int vaxis)
    {
        Engine.SetVirtualAxis(vaxis, 1f);
        RunFor(0.12f);
        Engine.SetVirtualAxis(vaxis, null);
        RunFor(0.12f);
    }

    public void TapKey(int dik, char ch = '\0')
    {
        Engine.KeyDown(dik, ch);
        RunFor(0.12f);
        Engine.KeyUp(dik);
        RunFor(0.12f);
    }

    public int InputConst(string name) => Script.GetStatic(Vm, UiNatives.InputClass, name).AsInt;

    // ---------------------------------------------------------------- mouse

    public int GameTypeConst(string name) => Script.GetStatic(Vm, "java.lang.GameType", name).AsInt;

    /// <summary>
    /// Gadgets of an Osd, group by group (<c>Osd.groups</c> → <c>Group.gadget</c>), with their command, label text and
    /// whether the group is active.
    /// </summary>
    public List<GadgetInfo> Gadgets(VmObject osd)
    {
        var list = new List<GadgetInfo>();
        var groups = Script.VectorItems(Vm, Script.GetObj(osd, "groups"));
        for (var gi = 0; gi < groups.Count; gi++)
        {
            var group = groups[gi].AsObject;
            if (group is null)
            {
                continue;
            }

            var active = Script.GetInt(group, "active") != 0;
            foreach (var g in Script.VectorItems(Vm, Script.GetObj(group, "gadget")))
            {
                if (g.AsObject is not { } gadget)
                {
                    continue;
                }

                string? label = null;
                if (Script.GetObj(gadget, "label") is { } lbl && Script.GetObj(lbl, "txt") is { } txt)
                {
                    label = Vm.GetString(Script.GetObj(txt, "text"));
                }

                list.Add(new GadgetInfo(gi, group, gadget, Script.GetInt(gadget, "cmd"), label, active, Script.GetInt(gadget, "disabled") != 0));
            }
        }

        return list;
    }

    /// <summary>The first gadget of an ACTIVE group with this command, or null.</summary>
    public GadgetInfo? FindGadget(VmObject osd, int command) =>
        Gadgets(osd).FirstOrDefault(g => g.GroupActive && g.Command == command);

    /// <summary>The first gadget of an active group whose label text equals <paramref name="label"/> (case-insensitive).</summary>
    public GadgetInfo? FindGadget(VmObject osd, string label) =>
        Gadgets(osd).FirstOrDefault(g => g.GroupActive && string.Equals(g.Label, label, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// A left click on a gadget: the cursor events <c>EC_LDOWN</c>, <c>EC_LUP</c>, <c>EC_LCLICK</c> are delivered to the
    /// gadget's Group (the parent node of its physics box, whose mask is EVENT_CURSOR) as
    /// <c>handleEvent(phy, EVENT_CURSOR, params)</c>, each on its own thread, settling in between. Token layout follows
    /// what Group.handleEvent reads: LDOWN/LUP token 3 = physics id, LCLICK token 2 = physics id (tokens 1..2 are
    /// unknown and sent as 0). Returns false when the group or the Osd's viewport is inactive (nothing can be hit).
    /// </summary>
    public bool Click(GadgetInfo g) => Click(g.Gadget);

    public bool Click(VmObject gadget)
    {
        var (group, phy, id) = HitTarget(gadget);
        if (group is null || phy is null)
        {
            return false;
        }

        var ec = (Down: GameTypeConst("EC_LDOWN"), Up: GameTypeConst("EC_LUP"), Click: GameTypeConst("EC_LCLICK"));
        CursorEvent(group, phy, Fmt(ec.Down, 0, 0, id));
        CursorEvent(group, phy, Fmt(ec.Up, 0, 0, id));
        CursorEvent(group, phy, Fmt(ec.Click, 0, id));
        return true;
    }

    /// <summary>Mouse moves over a gadget: <c>EC_HOVER</c> with token 3 = physics id, token 4 = 1 (entering).</summary>
    public bool Hover(VmObject gadget)
    {
        var (group, phy, id) = HitTarget(gadget);
        if (group is null || phy is null)
        {
            return false;
        }

        CursorEvent(group, phy, Fmt(GameTypeConst("EC_HOVER"), 0, 0, id, 1));
        return true;
    }

    public bool ClickCommand(VmObject osd, int command) => FindGadget(osd, command) is { } g && Click(g);

    public bool ClickLabel(VmObject osd, string label) => FindGadget(osd, label) is { } g && Click(g);

    /// <summary>
    /// A click on an icon button (<c>Osd.createButton(int rid, ...)</c>, a GameRef subscribed through
    /// <c>Osd.addHook</c>): delivered to the subscribers the world reports, else to <paramref name="osd"/>'s
    /// <c>event_handler</c> when the icon is in its <c>button[]</c> table (the hook <c>createButton(..., Object, int)</c> installs).
    /// </summary>
    public bool ClickIcon(VmObject osd, VmObject icon)
    {
        var id = Engine.World.IdOf(Vm, icon);
        var evt = GameTypeConst("EVENT_CURSOR");
        var msg = Vm.NewString(Fmt(GameTypeConst("EC_LCLICK"), id));
        if (Engine.World.Subscribers(Vm, icon, evt) is { } subs)
        {
            foreach (var (target, method) in subs)
            {
                Engine.SpawnHandler(target, method, [Value.Obj(icon), Value.Int(evt), Value.Obj(msg)], "THRD-CURSOR");
                Settle();
            }

            return subs.Count > 0;
        }

        if (Script.GetObj(osd, "button") is VmArray buttons && buttons.Items.Any(b => ReferenceEquals(b.AsObject, icon)))
        {
            Engine.SpawnHandler(osd, "event_handler", [Value.Obj(icon), Value.Int(evt), Value.Obj(msg)], "THRD-CURSOR");
            Settle();
            return true;
        }

        return false;
    }

    private (VmObject? Group, VmObject? Phy, int Id) HitTarget(VmObject gadget)
    {
        var osd = Script.GetObj(gadget, "osd");
        var phy = Script.GetObj(gadget, "phy");
        if (osd is null || phy is null)
        {
            return (null, null, 0);
        }

        var vp = Engine.FindViewport(Script.GetObj(osd, "vp"));
        if (vp is null || !vp.Active)
        {
            return (null, null, 0);
        }

        foreach (var g in Script.VectorItems(Vm, Script.GetObj(osd, "groups")))
        {
            if (g.AsObject is not { } group)
            {
                continue;
            }

            if (!Script.VectorItems(Vm, Script.GetObj(group, "gadget")).Any(x => ReferenceEquals(x.AsObject, gadget)))
            {
                continue;
            }

            var evt = GameTypeConst("EVENT_CURSOR");
            var accepts = Engine.World.AcceptsEvent(Vm, group, evt) ?? Script.GetInt(group, "active") != 0;
            return accepts ? (group, phy, Engine.World.IdOf(Vm, phy)) : (null, null, 0);
        }

        return (null, null, 0);
    }

    private void CursorEvent(VmObject group, VmObject source, string param)
    {
        var evt = GameTypeConst("EVENT_CURSOR");
        Engine.SpawnHandler(group, "handleEvent", [Value.Obj(source), Value.Int(evt), Value.Obj(Vm.NewString(param))], "THRD-CURSOR", "Ljava.util.resource.GameRef;");
        Settle();
    }

    private static string Fmt(params int[] tokens) => string.Join(' ', tokens.Select(t => t.ToString(CultureInfo.InvariantCulture)));

    // ---------------------------------------------------------------- scene views

    /// <summary><c>Frontend.inputQueue.lastElement()</c>: the Osd that receives hotkeys.</summary>
    public VmObject? TopOsd()
    {
        if (Script.PeekStatic(Vm, "java.render.Frontend", "inputQueue") is not { Ref: { } q })
        {
            return null;
        }

        var items = Script.VectorItems(Vm, q);
        return items.Count == 0 ? null : items[^1].AsObject;
    }

    /// <summary>Whether a text would be drawn: it has a string, and its parent Group is active in an Osd whose viewport is active.</summary>
    public bool IsVisible(TextModel t)
    {
        if (string.IsNullOrEmpty(t.Text) || t.Parent is null)
        {
            return false;
        }

        if (Script.IsA(t.Parent, "java.render.Group"))
        {
            if (Script.GetInt(t.Parent, "active") == 0)
            {
                return false;
            }

            var osd = Script.GetObj(t.Parent, "osd");
            return osd is not null && Engine.FindViewport(Script.GetObj(osd, "vp")) is { Active: true, Destroyed: false };
        }

        if (Script.IsA(t.Parent, UiNatives.CursorClass))
        {
            return Script.GetInt(t.Parent, "visible") != 0;
        }

        return true;
    }

    public IEnumerable<TextModel> VisibleTexts() => Engine.Texts.Where(IsVisible);

    public IEnumerable<string> VisibleStrings() => VisibleTexts().Select(t => t.Text!);

    /// <summary>Texts belonging to one Osd (parent Group's <c>osd</c> is <paramref name="osd"/>).</summary>
    public IEnumerable<TextModel> TextsOf(VmObject osd) =>
        Engine.Texts.Where(t => t.Parent is not null && Script.IsA(t.Parent, "java.render.Group") && ReferenceEquals(Script.GetObj(t.Parent, "osd"), osd));

    /// <summary>Script thread errors so far (each stops its thread, as in the engine).</summary>
    public IReadOnlyList<(VmThread Thread, VmException Error)> Errors => Vm.ThreadErrors;
}
