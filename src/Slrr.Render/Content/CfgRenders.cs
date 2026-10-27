using System.Numerics;
using Slrr.Formats.Cfg;
using Slrr.Render.Scene;

namespace Slrr.Render.Content;

/// <summary>A <c>render 0xID [x y z [a b c]]</c> line with the <c>lod a b</c> line that follows it.</summary>
public sealed record CfgRender(uint Id, Matrix4x4 Local, int LodMin, int LodMax, string Keyword);

/// <summary>A <c>slot x y z a b c id</c> line plus its <c>attach</c> lines.</summary>
public sealed record CfgSlot(int SlotId, Vector3 Position, Vector3 Ypr, IReadOnlyList<(uint Type, int Slot)> Attach, int? WheelIndex);

/// <summary>A <c>wheel x y z ...</c> line (car cfgs) and the <c>type .. sphere r</c> before it.</summary>
public sealed record CfgWheel(int Index, Vector3 Position, float Radius);

/// <summary>Render-related content of one gametype cfg.</summary>
public sealed class CfgRenderInfo
{
    public List<CfgRender> Renders { get; } = [];
    public List<CfgSlot> Slots { get; } = [];
    public List<CfgWheel> Wheels { get; } = [];
    public List<uint> StockParts { get; } = [];
    public LodSet? Lods { get; set; }

    /// <summary>
    /// Parses a cfg. Transforms: position x y z then yaw pitch roll in radians (MEASURED: right wheel slots use
    /// <c>3.142 0 0</c>, the steering wheel <c>0 -0.419 0</c>); composed as roll, then pitch about X, then yaw about Y
    /// (GUESSED order and signs, System.Numerics right-handed rotations).
    /// Slots following a <c>wheel</c> line up to the next <c>type</c>/<c>wheel</c> or a slot with a non-1xx id are taken as
    /// wheel-relative only for ids 101..104 (GUESSED; other slots there look absolute, e.g. 543 "axle end on the chassis").
    /// </summary>
    public static CfgRenderInfo Parse(CfgDocument doc)
    {
        ArgumentNullException.ThrowIfNull(doc);
        var info = new CfgRenderInfo();
        CfgRender? last = null;
        var pendingAttach = new List<(uint, int)>();
        CfgSlot? lastSlot = null;
        float wheelRadius = 0.3f;
        int? wheel = null;

        void FlushSlot()
        {
            if (lastSlot is not null)
            {
                info.Slots.Add(lastSlot with { Attach = pendingAttach.ToList() });
                pendingAttach.Clear();
                lastSlot = null;
            }
        }

        foreach (var l in doc.Lines)
        {
            if (l.Kind == CfgLineKind.Eof)
            {
                break;
            }

            if (l.Kind != CfgLineKind.Directive || l.Keyword is null)
            {
                continue;
            }

            var a = l.Args;
            switch (l.Keyword.ToLowerInvariant())
            {
                case "render":
                case "deformable":
                    if (a.Count >= 1 && GameContent.TryHex(a[0], out var rid) && rid != 0)
                    {
                        if (last is not null)
                        {
                            info.Renders.Add(last);
                        }

                        last = new CfgRender(rid, Transform(a, 1), 0, int.MaxValue, l.Keyword);
                    }

                    break;
                case "lod":
                    if (last is not null && a.Count >= 2 && int.TryParse(a[0], out var lo) && int.TryParse(a[1], out var hi))
                    {
                        last = last with { LodMin = Math.Min(lo, hi), LodMax = Math.Max(lo, hi) };
                    }

                    break;
                case "lods":
                    if (a.Count >= 2 && int.TryParse(a[0], out var count))
                    {
                        info.Lods = new LodSet(Enumerable.Range(1, Math.Min(count, a.Count - 1)).Select(i => GameContent.F(a, i)).ToList());
                    }

                    break;
                case "type":
                    wheelRadius = a.Count >= 3 && a[1] == "sphere" ? GameContent.F(a, 2, 0.3f) : wheelRadius;
                    wheel = null;
                    break;
                case "wheel":
                    FlushSlot();
                    wheel = info.Wheels.Count;
                    info.Wheels.Add(new CfgWheel(info.Wheels.Count, new Vector3(GameContent.F(a, 0), GameContent.F(a, 1), GameContent.F(a, 2)), wheelRadius));
                    break;
                case "slot":
                    FlushSlot();
                    if (a.Count >= 7 && int.TryParse(a[6], out var sid))
                    {
                        var isWheelSlot = wheel is not null && sid is >= 101 and <= 104;
                        lastSlot = new CfgSlot(sid, new Vector3(GameContent.F(a, 0), GameContent.F(a, 1), GameContent.F(a, 2)),
                            new Vector3(GameContent.F(a, 3), GameContent.F(a, 4), GameContent.F(a, 5)), [], isWheelSlot ? wheel : null);
                    }

                    break;
                case "attach":
                    if (lastSlot is not null && a.Count >= 2 && GameContent.TryHex(a[0], out var t) && int.TryParse(a[1], out var s))
                    {
                        pendingAttach.Add((t, s));
                    }

                    break;
                case "stockpart":
                    if (a.Count >= 1 && GameContent.TryHex(a[0], out var sp))
                    {
                        info.StockParts.Add(sp);
                    }

                    break;
            }
        }

        FlushSlot();
        if (last is not null)
        {
            info.Renders.Add(last);
        }

        return info;
    }

    /// <summary>Position at args[i..i+2], angles at args[i+3..i+5] (missing = 0).</summary>
    public static Matrix4x4 Transform(IReadOnlyList<string> a, int i) =>
        Ypr(new Vector3(GameContent.F(a, i + 3), GameContent.F(a, i + 4), GameContent.F(a, i + 5))) *
        Matrix4x4.CreateTranslation(GameContent.F(a, i), GameContent.F(a, i + 1), GameContent.F(a, i + 2));

    /// <summary>x = yaw (about Y), y = pitch (about X), z = roll (about Z); GUESSED order roll-pitch-yaw.</summary>
    public static Matrix4x4 Ypr(Vector3 ypr) =>
        Matrix4x4.CreateRotationZ(ypr.Z) * Matrix4x4.CreateRotationX(ypr.Y) * Matrix4x4.CreateRotationY(ypr.X);

    public static Matrix4x4 SlotMatrix(CfgSlot s, Vector3 basePosition) =>
        Ypr(s.Ypr) * Matrix4x4.CreateTranslation(basePosition + s.Position);
}
