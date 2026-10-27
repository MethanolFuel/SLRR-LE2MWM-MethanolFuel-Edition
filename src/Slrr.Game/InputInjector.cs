using System.Globalization;
using Slrr.Engine.Ui;
using Slrr.Vm;

namespace Slrr.Game;

/// <summary>
/// Input injection into the UI driver: keyboard (DirectInput scan codes, with the typed character for text input),
/// virtual axes (<c>Input.AXIS_*</c>, what the control set maps keys/pads to), gamepad/joystick physical axes, mouse
/// position and gadget clicks with the scripts' cursor-event protocol (see Slrr.Engine.Ui.UiDriver). Events can be
/// applied immediately or scheduled for a virtual time; scheduled events are applied at the start of the frame that
/// reaches their time. Holding/tapping follows the 50 ms HotkeyWatcher poll: a tap holds for 0.12 s and releases for 0.12 s.
/// </summary>
public sealed class InputInjector(GameHost host)
{
    private readonly List<(float At, long Seq, Action Apply)> _scheduled = [];
    private long _seq;

    /// <summary>Hold time of a tap (seconds of virtual time).</summary>
    public float TapTime { get; set; } = 0.12f;

    public List<string> Journal { get; } = [];

    private UiEngine Ui => host.UiEngine;

    public void Schedule(float at, Action apply) => _scheduled.Add((at, _seq++, apply));

    internal void BeforeFrame()
    {
        if (_scheduled.Count == 0)
        {
            return;
        }

        var due = _scheduled.Where(s => s.At <= host.Vm.Time + 1e-6f).OrderBy(s => s.At).ThenBy(s => s.Seq).ToList();
        foreach (var s in due)
        {
            _scheduled.Remove(s);
            s.Apply();
        }
    }

    // ---------------------------------------------------------------- keyboard

    /// <summary>Scan code of a key name (<c>Enter</c>, <c>Esc</c>, <c>Up</c>, <c>A</c>, <c>F1</c>, ...) or a number.</summary>
    public static int KeyCode(string name)
    {
        if (int.TryParse(name, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n))
        {
            return n;
        }

        var alias = name.ToUpperInvariant() switch
        {
            "RETURN" => "ENTER",
            "ESCAPE" => "ESC",
            "SPACEBAR" => "SPACE",
            var x => x,
        };
        foreach (var (code, key) in InputState.KeyNames)
        {
            if (string.Equals(key, alias, StringComparison.OrdinalIgnoreCase))
            {
                return code;
            }
        }

        throw new ArgumentException($"unknown key '{name}'");
    }

    public void KeyDown(int dik, char ch = '\0')
    {
        Journal.Add($"key down {dik}");
        Ui.KeyDown(dik, ch);
    }

    public void KeyUp(int dik)
    {
        Journal.Add($"key up {dik}");
        Ui.KeyUp(dik);
    }

    /// <summary>Press, hold <see cref="TapTime"/>, release, wait <see cref="TapTime"/> (frames run).</summary>
    public void TapKey(int dik, char ch = '\0')
    {
        KeyDown(dik, ch);
        host.RunFor(TapTime);
        KeyUp(dik);
        host.RunFor(TapTime);
    }

    /// <summary>Types text: one tap per character, with the character in the key queue (Osd.animate reads <c>key &gt;&gt; 16</c>).</summary>
    public void Type(string text)
    {
        foreach (var ch in text)
        {
            var dik = char.IsAsciiLetterOrDigit(ch) ? KeyCode(char.ToUpperInvariant(ch).ToString()) : ch == ' ' ? 57 : 52;
            TapKey(dik, ch);
        }
    }

    // ---------------------------------------------------------------- virtual axes and gamepads

    /// <summary><c>Input.AXIS_*</c> by name (<c>AXIS_SELECT</c> or <c>SELECT</c>) or number.</summary>
    public int AxisCode(string name)
    {
        if (int.TryParse(name, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n))
        {
            return n;
        }

        var full = name.StartsWith("AXIS_", StringComparison.OrdinalIgnoreCase) ? name.ToUpperInvariant() : "AXIS_" + name.ToUpperInvariant();
        return host.Driver.InputConst(full);
    }

    public void SetAxis(int vaxis, float? value)
    {
        Journal.Add($"axis {vaxis} = {value?.ToString(CultureInfo.InvariantCulture) ?? "released"}");
        Ui.SetVirtualAxis(vaxis, value);
    }

    public void TapAxis(int vaxis)
    {
        SetAxis(vaxis, 1f);
        host.RunFor(TapTime);
        SetAxis(vaxis, null);
        host.RunFor(TapTime);
    }

    /// <summary>
    /// A physical axis of a game controller device (device index as in <c>Input.getDeviceName</c>; 0 keyboard, 1 mouse,
    /// 2.. joysticks/pads). Queues an edge for every virtual axis whose mapped value crosses 0.5, like a key does.
    /// </summary>
    public void Gamepad(int device, int axis, float value)
    {
        Journal.Add($"pad {device}:{axis} = {value.ToString(CultureInfo.InvariantCulture)}");
        var axes = Ui.Controllers.Values.SelectMany(c => c.Mappings).Select(m => m.VirtualAxis).Distinct().ToList();
        var before = axes.ToDictionary(a => a, a => Math.Abs(Ui.VirtualAxis(a)) > 0.5f);
        Ui.Input.SetPhysical(device, axis, value);
        foreach (var a in axes)
        {
            var now = Math.Abs(Ui.VirtualAxis(a)) > 0.5f;
            if (now != before[a])
            {
                Ui.Input.Enqueue(new InputEdge(true, a, now));
            }
        }
    }

    // ---------------------------------------------------------------- mouse

    /// <summary>Cursor position in OSD coordinates (-1..1), what <c>MouseCursor.getPos()</c> returns.</summary>
    public void MoveMouse(float x, float y)
    {
        Ui.Input.CursorX = x;
        Ui.Input.CursorY = y;
    }

    /// <summary>All gadgets of every live Osd, top (hotkey receiver) first.</summary>
    public IEnumerable<GadgetInfo> AllGadgets()
    {
        foreach (var osd in host.Screens.LiveOsds())
        {
            foreach (var g in host.Driver.Gadgets(osd))
            {
                yield return g;
            }
        }
    }

    /// <summary>What a player reads on a gadget: its label, else its tooltip (icon buttons such as the garage menu).</summary>
    public string? TextOf(GadgetInfo g) =>
        !string.IsNullOrWhiteSpace(g.Label) ? g.Label.Trim() : host.Vm.GetString(Probe.Field(g.Gadget, "tooltip").AsObject)?.Trim();

    /// <summary>
    /// Clicks the first gadget of an active group whose label (or, for icon buttons, tooltip) matches
    /// (case-insensitive), top Osd first, enabled ones first.
    /// </summary>
    public bool Click(string label, bool contains = false)
    {
        bool Match(GadgetInfo x) => x.GroupActive && (contains
            ? TextOf(x)?.Contains(label.Trim(), StringComparison.OrdinalIgnoreCase) == true
            : string.Equals(TextOf(x), label.Trim(), StringComparison.OrdinalIgnoreCase));
        var g = AllGadgets().FirstOrDefault(x => Match(x) && !x.Disabled) ?? AllGadgets().FirstOrDefault(Match);
        Journal.Add($"click '{label}' -> {(g is null ? "not found" : g.Kind + " cmd " + g.Command)}");
        return g is not null && host.Driver.Click(g);
    }

    /// <summary>Clicks the first gadget with this command id in an active group, top Osd first.</summary>
    public bool ClickCommand(int command)
    {
        var g = AllGadgets().FirstOrDefault(x => x.GroupActive && x.Command == command);
        Journal.Add($"click cmd {command} -> {(g is null ? "not found" : g.Label)}");
        return g is not null && host.Driver.Click(g);
    }

    /// <summary>
    /// Mouse drag and drop of a gadget (e.g. an inventory panel button) onto a game object: the engine's
    /// <c>EC_LDROP</c> cursor event, queued on the mouse cursor's GameRef so that every <c>MouseCursor.addHandler</c>
    /// subscriber (<c>addNotification(cursor, EVENT_CURSOR)</c>) receives it. Token layout from the reader
    /// (<c>Garage.handleEvent</c>, MEASURED): <c>EC_LDROP ? targetId draggedId x y z buttonPhyId</c>; the dragged object is the
    /// button's physics box, token 1 is unknown and sent as 0 (GUESSED).
    /// </summary>
    public bool DropOnto(GadgetInfo gadget, int targetId, float x = 0, float y = 0, float z = 0)
    {
        var cursor = Probe.Field(host.Driver.Cursor, "cursor").AsObject;
        var phy = Probe.Field(gadget.Gadget, "phy").AsObject;
        if (cursor is null || phy is null)
        {
            Journal.Add("drop: no cursor or no button physics");
            return false;
        }

        var phyId = Probe.Id(phy);
        var cursorId = Probe.Id(cursor);
        var text = string.Join(' ', new[] { host.Driver.GameTypeConst("EC_LDROP"), 0, targetId, phyId }.Select(i => i.ToString(CultureInfo.InvariantCulture)))
                   + string.Create(CultureInfo.InvariantCulture, $" {x} {y} {z} {phyId}");
        Journal.Add($"drop '{TextOf(gadget)}' onto {targetId}: {text}");
        host.Resources.QueueEvent(cursorId, cursorId, host.Driver.GameTypeConst("EVENT_CURSOR"), text);
        host.RunFrame();
        return true;
    }

    /// <summary>Drops the first active gadget whose text contains <paramref name="label"/> onto <paramref name="targetId"/>.</summary>
    public bool DropOnto(string label, int targetId)
    {
        var g = AllGadgets().FirstOrDefault(x => x.GroupActive && TextOf(x)?.Contains(label, StringComparison.OrdinalIgnoreCase) == true);
        return g is not null && DropOnto(g, targetId);
    }

    /// <summary>Sends an Osd command directly to the handler of the top Osd (what a hotkey or button would), on a new thread.</summary>
    public bool OsdCommand(int command)
    {
        var osd = host.TopOsd;
        var handler = osd is null ? null : Probe.Field(osd, "globalHandler").AsObject;
        Journal.Add($"osdCommand {command} -> {handler?.Class.Name ?? "none"}");
        if (handler is null)
        {
            return false;
        }

        var ok = host.UiEngine.SpawnHandler(handler, "osdCommand", [Value.Int(command)], "THRD-HOST");
        host.Driver.Settle();
        return ok;
    }
}
