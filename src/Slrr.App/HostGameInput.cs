using System.Globalization;
using System.Numerics;
using Slrr.Engine.Ui;
using Slrr.Game;
using Slrr.Vm;

namespace Slrr.App;

/// <summary>A rectangle in OSD coordinates (centre and half extents; y grows downwards).</summary>
public readonly record struct OsdRect(float CenterX, float CenterY, float HalfW, float HalfH)
{
    public bool Contains(Vector2 p) => Math.Abs(p.X - CenterX) <= HalfW && Math.Abs(p.Y - CenterY) <= HalfH;

    public Vector2 Center => new(CenterX, CenterY);

    public float Area => HalfW * HalfH * 4;
}

/// <summary>A clickable thing on screen: a gadget (Button/Slider/MultiChoice/StringInput) or an icon button of an Osd.</summary>
public sealed record HitTarget(VmObject Osd, VmObject Target, OsdRect Rect, bool Icon, string? Text, int Command, bool Disabled, int OsdOrder)
{
    public bool Equals(HitTarget? other) => other is not null && ReferenceEquals(Target, other.Target);

    public override int GetHashCode() => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(Target);

    public override string ToString() => string.Create(CultureInfo.InvariantCulture,
        $"{(Icon ? "icon" : Target.Class.Name[(Target.Class.Name.LastIndexOf('.') + 1)..])} '{Text}' cmd {Command} at ({Rect.CenterX:F3},{Rect.CenterY:F3}) ±({Rect.HalfW:F3},{Rect.HalfH:F3})");
}

/// <summary>
/// Where the gadgets are on screen. The engine hit-tests the mouse ray against each gadget's physics box; this rebuilds those boxes
/// in OSD coordinates from the scripts' own formulas (MEASURED, decompiled <c>Osd.createButton/createMulti/createStringInput</c> and
/// <c>Style</c>): the box is <c>createBox(group, style.rWidth, style.rHeight, 0.001)</c> placed by <c>setMatrix(convertTextCoordinates(x, y))</c>
/// with <c>convertTextCoordinates = (x·SCALE_3D·vpAspect·vpWidth, -y·SCALE_3D·vpHeight, z/1000)</c> and <c>rWidth = width·SCALE_FS·aspect</c>,
/// <c>rHeight = height·SCALE_FS</c>. So centre = (pos.x / (SCALE_3D·vpAspect·vpWidth), -pos.y / (SCALE_3D·vpHeight)) and full size =
/// (box.x / (SCALE_FS·vpAspect·vpWidth), box.y / (SCALE_FS·vpHeight)): the same conversion the renderer uses for the gadget's background
/// rectangle (Slrr.Render UiOverlaySource), so the clickable area matches what is drawn. GUESSED: that the box extents are full sizes
/// (like the RectangleTemplate built from the same numbers) and that the viewport covers the whole screen (as the renderer assumes).
/// Icon buttons (<c>Osd.createButton(int rid, ...)</c>, a 3D GameRef) get a GUESSED 0.12 x 0.16 OSD box around their position.
/// Order: Osds top of the input queue first (<see cref="ScreenTracker.LiveOsds"/>), then the smaller box (a button inside a panel wins).
/// </summary>
public sealed class GadgetHitTester(GameHost host)
{
    public const float IconHalfW = 0.06f;
    public const float IconHalfH = 0.08f;

    private List<HitTarget>? _cache;
    private long _cacheFrame = -1;

    /// <summary>All targets of active groups in Osds with an active viewport (rebuilt once per host frame).</summary>
    public IReadOnlyList<HitTarget> Targets()
    {
        if (_cache is not null && _cacheFrame == host.Frame)
        {
            return _cache;
        }

        var list = new List<HitTarget>();
        var vm = host.Vm;
        var s3 = Static("SCALE_3D", 1.469f);
        var sfs = Static("SCALE_FS", 1.47f);
        var order = 0;
        foreach (var osd in host.Screens.LiveOsds())
        {
            order++;
            var aspect = Probe.Field(osd, "vpAspect").AsFloat;
            var w = Probe.Field(osd, "vpWidth").AsFloat;
            var h = Probe.Field(osd, "vpHeight").AsFloat;
            if (!(aspect > 0) || !(w > 0) || !(h > 0))
            {
                continue;
            }

            foreach (var g in host.Driver.Gadgets(osd))
            {
                if (!g.GroupActive || Probe.Field(g.Gadget, "phy").AsObject is not { } phy || host.Resources.NodeOf(phy) is not { } node)
                {
                    continue;
                }

                if (!node.Properties.TryGetValue("box", out var b) || b is not float[] { Length: >= 2 } box || !(box[0] > 0) || !(box[1] > 0))
                {
                    continue;
                }

                var rect = new OsdRect(node.Pos[0] / (s3 * aspect * w), -node.Pos[1] / (s3 * h), box[0] / (sfs * aspect * w) / 2, box[1] / (sfs * h) / 2);
                if (Finite(rect))
                {
                    list.Add(new HitTarget(osd, g.Gadget, rect, false, host.Input.TextOf(g), g.Command, g.Disabled, order));
                }
            }

            if (Probe.Field(osd, "button").AsObject is VmArray icons)
            {
                var count = Math.Min(Probe.Field(osd, "buttons").AsInt, icons.Items.Length);
                for (var i = 0; i < count; i++)
                {
                    if (icons.Items[i].AsObject is not { } icon || host.Resources.NodeOf(icon) is not { } node)
                    {
                        continue;
                    }

                    var rect = new OsdRect(node.Pos[0] / (s3 * aspect * w), -node.Pos[1] / (s3 * h), IconHalfW, IconHalfH);
                    if (Finite(rect))
                    {
                        list.Add(new HitTarget(osd, icon, rect, true, IconText(osd, i), Probe.Field(osd, "buttonCommand").AsObject is VmArray cmds && i < cmds.Items.Length ? cmds.Items[i].AsInt : 0, false, order));
                    }
                }
            }
        }

        _cache = list;
        _cacheFrame = host.Frame;
        return list;

        float Static(string name, float fallback) => Script.PeekStatic(vm, "java.render.Osd", name) is { } v && v.AsFloat > 0 ? v.AsFloat : fallback;
    }

    /// <summary>Forget the cached boxes (after a click or a script step changed the screen within the same host frame).</summary>
    public void Invalidate() => _cache = null;

    /// <summary>The target under an OSD point: topmost Osd first, then the smallest box.</summary>
    public HitTarget? At(Vector2 osd) =>
        Targets().Where(t => t.Rect.Contains(osd)).OrderBy(t => t.OsdOrder).ThenBy(t => t.Rect.Area).FirstOrDefault();

    /// <summary>The first target whose text matches (case-insensitive; <paramref name="contains"/> for substrings), enabled ones first.</summary>
    public HitTarget? Find(string text, bool contains = false)
    {
        bool Match(HitTarget t) => t.Text is { } s && (contains ? s.Contains(text.Trim(), StringComparison.OrdinalIgnoreCase) : string.Equals(s, text.Trim(), StringComparison.OrdinalIgnoreCase));
        var all = Targets();
        return all.FirstOrDefault(t => Match(t) && !t.Disabled) ?? all.FirstOrDefault(Match);
    }

    public HitTarget? FindCommand(int command) => Targets().FirstOrDefault(t => t.Command == command && !t.Icon);

    private string? IconText(VmObject osd, int i)
    {
        if (Probe.Field(osd, "textIndex").AsObject is VmArray idx && i < idx.Items.Length && Probe.Field(osd, "text").AsObject is { } texts)
        {
            var items = Script.VectorItems(host.Vm, texts);
            var k = idx.Items[i].AsInt;
            if (k >= 0 && k < items.Count && items[k].AsObject is { } t && host.UiEngine.Texts.FirstOrDefault(m => ReferenceEquals(m.Object, t)) is { } model)
            {
                return model.Text;
            }
        }

        return null;
    }

    private static bool Finite(OsdRect r) => float.IsFinite(r.CenterX) && float.IsFinite(r.CenterY) && float.IsFinite(r.HalfW) && float.IsFinite(r.HalfH);
}

/// <summary><see cref="IGameInput"/> over a running <see cref="GameHost"/>: keys through its UiEngine, clicks with the scripts' cursor-event protocol.</summary>
public sealed class HostGameInput(GameHost host, GadgetHitTester hits) : IGameInput
{
    /// <summary>Bound of the key and edge queues (GUESSED: a DirectInput buffer is bounded; unread keys must not pile up while driving).</summary>
    public const int QueueLimit = 32;

    public List<string> Log { get; } = [];

    public void KeyDown(int dik, char ch)
    {
        host.UiEngine.KeyDown(dik, ch);
        Trim();
    }

    public void KeyUp(int dik)
    {
        host.UiEngine.KeyUp(dik);
        Trim();
    }

    public void SetKeyAxis(int dik, float value)
    {
        host.Input.Gamepad(InputState.KeyboardDevice, dik, value);
        Trim();
    }

    public void MoveCursor(Vector2 osd) => host.Input.MoveMouse(osd.X, osd.Y);

    public object? HitAt(Vector2 osd) => hits.At(osd);

    public void Hover(object? target)
    {
        if (target is HitTarget { Icon: false } t)
        {
            host.Driver.Hover(t.Target);
            hits.Invalidate();
        }
    }

    public string Click(object target)
    {
        if (target is not HitTarget t)
        {
            return "nothing";
        }

        var ok = t.Icon ? host.Driver.ClickIcon(t.Osd, t.Target) : host.Driver.Click(t.Target);
        hits.Invalidate();
        var s = $"click {t} -> {(ok ? "delivered" : "not delivered (group inactive)")}";
        Log.Add(s);
        return s;
    }

    private void Trim()
    {
        var input = host.UiEngine.Input;
        while (input.KeyQueue.Count > QueueLimit)
        {
            input.KeyQueue.Dequeue();
        }

        while (input.OsdEdges.Count > QueueLimit)
        {
            input.OsdEdges.Dequeue();
        }

        while (input.GlobalEdges.Count > QueueLimit)
        {
            input.GlobalEdges.Dequeue();
        }
    }
}
