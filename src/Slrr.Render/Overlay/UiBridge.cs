using System.Numerics;
using Slrr.Engine.Resources;
using Slrr.Engine.Ui;
using Slrr.Render.Content;
using Slrr.Render.Overlay;
using Slrr.Vm;

namespace Slrr.Render.UiBridge;

/// <summary>
/// Observes the natives that carry rendering state the headless engine areas do not model: which texture a
/// <c>RenderRef</c> got through <c>changeResource</c> (RectangleTemplate swaps <c>defTexture</c> for the wanted one), and which
/// GameType instances are Osds. Register it after the Resources natives (it wraps them; nothing else changes).
/// </summary>
public sealed class RenderTap : INativeModule
{
    private const string RenderRef = "java.util.resource.RenderRef";
    private const string GameType = "java.lang.GameType";
    private const string LRR = "Ljava.util.resource.ResourceRef;";

    /// <summary>RenderRef object -> (replaced resource id, new resource id) of the last changeResource.</summary>
    public Dictionary<VmObject, List<(int Old, int New)>> Changes { get; } = new(ReferenceEqualityComparer.Instance);

    /// <summary>Every java.render.Osd that created its native instance, in creation order.</summary>
    public List<VmObject> Osds { get; } = [];

    public void Register(NativeRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        var change = registry.Find(RenderRef, "changeResource", $"({LRR}{LRR})V");
        registry.Register(RenderRef, "changeResource", $"({LRR}{LRR})V", c =>
        {
            if (!Changes.TryGetValue(c.Self, out var l))
            {
                Changes[c.Self] = l = [];
            }

            l.Add((ResourceEngine.IdOf(c.Object(0)), ResourceEngine.IdOf(c.Object(1))));
            return change?.Invoke(c) ?? Value.Void;
        }, replace: true);

        var desc = "(Ljava.util.resource.GameRef;ILjava.lang.String;Ljava.lang.String;)V";
        var create = registry.Find(GameType, "createNativeInstance", desc);
        if (create is not null)
        {
            registry.Register(GameType, "createNativeInstance", desc, c =>
            {
                if (c.This is { } self && self.Class.IsSubclassOf("java.render.Osd"))
                {
                    Osds.Add(self);
                }

                return create(c);
            }, replace: true);
        }
    }
}

/// <summary>
/// Builds an <see cref="OverlayFrame"/> from the headless UI state: every Osd with an active viewport, its active Groups'
/// <c>Rectangle</c>s (script fields <c>pos</c>, <c>rt.width/height</c>, texture from <see cref="RenderTap"/>) and the visible
/// <see cref="TextModel"/>s. Coordinates are converted back with the script's own formulas (MEASURED, Osd.class):
/// <c>pos = (x * SCALE_3D * vpAspect * vpWidth, -y * SCALE_3D * vpHeight, z / 1000)</c>, <c>width = w * SCALE_FS * vpAspect * vpWidth</c>,
/// <c>height = h * SCALE_FS * vpHeight</c>. Viewport placement on screen is not applied (full screen; GUESSED fine for the
/// full-screen menus). 3D objects of an Osd (<c>createObject</c> icons) are not drawn yet.
/// </summary>
public sealed class UiOverlaySource(UiEngine ui, RenderTap tap, Func<int, ResRef?> runtimeId, GameContent content) : IOverlaySource
{
    public OverlayFrame Capture()
    {
        var frame = new OverlayFrame();
        var vm = ui.Vm;
        var driver = new UiDriver(vm, ui);
        frame.VideoWidth = Math.Max(ui.VideoMode.W, 1);
        frame.VideoHeight = Math.Max(ui.VideoMode.H, 1);
        var s3 = Static(vm, "SCALE_3D", 1.469f);
        var sfs = Static(vm, "SCALE_FS", 1.47f);

        foreach (var osd in tap.Osds.Distinct())
        {
            var vp = Script.GetObj(osd, "vp");
            if (ui.FindViewport(vp) is not { Active: true, Destroyed: false })
            {
                continue;
            }

            var aspect = Script.GetFloat(osd, "vpAspect");
            var w = Script.GetFloat(osd, "vpWidth");
            var h = Script.GetFloat(osd, "vpHeight");
            if (aspect == 0 || w == 0 || h == 0)
            {
                frame.Notes.Add($"{osd.Class.Name}: degenerate viewport");
                continue;
            }

            foreach (var rv in Script.VectorItems(vm, Script.GetObj(osd, "rectangles")))
            {
                if (rv.AsObject is not { } rect || Script.GetObj(rect, "root") is { } group && Script.IsA(group, "java.render.Group") && Script.GetInt(group, "active") == 0)
                {
                    continue;
                }

                var pos = Script.GetObj(rect, "pos");
                var rt = Script.GetObj(rect, "rt");
                if (pos is null || rt is null)
                {
                    continue;
                }

                var dub = Script.GetObj(rt, "dub");
                var texId = dub is not null && tap.Changes.TryGetValue(dub, out var ch) ? ch.Select(x => x.New).FirstOrDefault() : 0;
                if (texId == 0)
                {
                    continue; // RRT_NONE: the template stays empty and draws nothing.
                }

                var tex = runtimeId(texId) is { } tr ? content.ResolveTexture(content.SourceFile(tr)) : null;
                if (tex is null)
                {
                    frame.Notes.Add($"rectangle texture 0x{texId:X8} not resolved");
                }

                frame.Quads.Add(new OverlayQuad(
                    Script.GetFloat(pos, "x") / (s3 * aspect * w),
                    -Script.GetFloat(pos, "y") / (s3 * h),
                    Script.GetFloat(rt, "width") / (sfs * aspect * w),
                    Script.GetFloat(rt, "height") / (sfs * h),
                    tex,
                    Vector4.One,
                    Script.GetFloat(pos, "z"),
                    runtimeId(texId)?.ToString() ?? $"0x{texId:X8}"));
            }
        }

        foreach (var t in driver.VisibleTexts())
        {
            var fontId = ResourceEngine.IdOf(t.Font);
            frame.Texts.Add(new OverlayText(t.Text ?? "", t.X, t.Y, runtimeId(fontId), OverlayLayout.Argb(t.Color), t.Align, 0));
        }

        return frame;
    }

    private static float Static(Vm.Vm vm, string name, float fallback) =>
        Script.PeekStatic(vm, "java.render.Osd", name) is { } v ? v.AsFloat : fallback;
}
