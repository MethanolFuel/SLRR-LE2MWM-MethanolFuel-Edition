using System.Numerics;
using Slrr.Formats.Graphics;
using Slrr.Render.Content;
using Slrr.Render.Scene;

namespace Slrr.Render.Cars;

/// <summary>A gametype placed in the car tree.</summary>
public sealed record PlacedPart(ResRef Type, string Name, Matrix4x4 World, string Parent, int ParentSlot, int OwnSlot, string How);

/// <summary>Result of <see cref="CarAssembler.Assemble"/>.</summary>
public sealed class CarAssembly
{
    public List<PlacedPart> Parts { get; } = [];
    public List<DrawItem> Items { get; } = [];
    public List<CfgWheel> Wheels { get; } = [];
    public List<string> Notes { get; } = [];

    /// <summary>cfg render lines that name lights (type 13 entries): not drawn yet.</summary>
    public int Lights { get; set; }
}

/// <summary>
/// Assembles a car for rendering from its cfg: the car gametype's own <c>deformable</c>/<c>render</c> lines, its
/// <c>stockpart</c> list (recursively through each part's <c>stockpart</c> lines) and a rim + tyre on every
/// <c>wheel</c>. A part is placed so that its mount slot coincides with the parent slot it names:
/// <c>World(child) = Inverse(Slot(child)) * Slot(parent) * World(parent)</c> (row vectors), where the mount slot is the
/// child's <c>slot</c> line carrying <c>attach &lt;parentType&gt; &lt;parentSlotId&gt;</c> (MEASURED pairing: Nonus FL_door
/// slot 5 / <c>attach 0x6 5</c> / car slot 5 "FL_door"; tyre slot 1 / <c>attach 0x00020200 2</c> / rim slot 2).
/// Which rim and tyre a car ships with is decided by its scripts (not evaluated), so the caller chooses them.
/// </summary>
public sealed class CarAssembler(GameContent content)
{
    private const int MaxDepth = 8;

    public GameContent Content { get; } = content;

    public CarAssembly Assemble(ResRef car, ResRef? rim, ResRef? tyre)
    {
        var result = new CarAssembly();
        var cfg = Content.GametypeCfg(car);
        if (cfg is null)
        {
            result.Notes.Add($"{car}: no cfg");
            return result;
        }

        var info = CfgRenderInfo.Parse(cfg);
        var name = Content.Entry(car)?.Alias ?? car.ToString();
        result.Wheels.AddRange(info.Wheels);
        result.Parts.Add(new PlacedPart(car, name, Matrix4x4.Identity, "-", 0, 0, "root"));
        AddRenders(result, car, info, Matrix4x4.Identity, name);

        foreach (var sp in info.StockParts)
        {
            AddStockPart(result, car, info, Matrix4x4.Identity, name, sp, 1);
        }

        foreach (var w in info.Wheels)
        {
            var slot = info.Slots.FirstOrDefault(s => s.WheelIndex == w.Index);
            if (slot is null)
            {
                result.Notes.Add($"wheel {w.Index}: no 1xx slot after the wheel line");
                continue;
            }

            var slotWorld = CfgRenderInfo.SlotMatrix(slot, w.Position);
            if (rim is { } r && Place(result, r, slotWorld, $"wheel{w.Index}", slot.SlotId, mountSlotHint: 1) is { } rimPlaced &&
                tyre is { } t && Content.GametypeCfg(r) is { } rimCfg)
            {
                var rimInfo = CfgRenderInfo.Parse(rimCfg);
                if (rimInfo.Slots.FirstOrDefault(s => s.SlotId == 2) is { } rimSlot2)
                {
                    Place(result, t, CfgRenderInfo.SlotMatrix(rimSlot2, Vector3.Zero) * rimPlaced.World, rimPlaced.Name, 2, mountSlotHint: 1);
                }
            }
        }

        return result;
    }

    private void AddStockPart(CarAssembly result, ResRef parentType, CfgRenderInfo parentInfo, Matrix4x4 parentWorld, string parentName, uint id, int depth)
    {
        if (depth > MaxDepth)
        {
            result.Notes.Add($"stockpart 0x{id:X8}: nesting deeper than {MaxDepth}");
            return;
        }

        if (Content.Resolve(parentType.Archive, id) is not { } part)
        {
            result.Notes.Add($"stockpart 0x{id:X8} of {parentName}: not found");
            return;
        }

        var cfg = Content.GametypeCfg(part);
        if (cfg is null)
        {
            result.Notes.Add($"stockpart {part}: no cfg");
            return;
        }

        var info = CfgRenderInfo.Parse(cfg);
        var name = Path.GetFileNameWithoutExtension(cfg.Path.Replace('\\', '/'));

        // Mount slot: a slot whose attach names this parent type; else any attach whose slot id the parent has (GUESSED fallback).
        (CfgSlot Own, CfgSlot Parent, string How)? mount = null;
        foreach (var s in info.Slots)
        {
            foreach (var (t, ps) in s.Attach)
            {
                if (Content.Resolve(part.Archive, t) == parentType && parentInfo.Slots.FirstOrDefault(x => x.SlotId == ps) is { } pslot)
                {
                    mount = (s, pslot, "attach names parent");
                    break;
                }
            }

            if (mount is not null)
            {
                break;
            }
        }

        if (mount is null)
        {
            foreach (var s in info.Slots)
            {
                if (s.Attach.Select(a => parentInfo.Slots.FirstOrDefault(x => x.SlotId == a.Slot)).FirstOrDefault(x => x is not null) is { } pslot)
                {
                    mount = (s, pslot, "slot id only (GUESSED)");
                    break;
                }
            }
        }

        Matrix4x4 world;
        if (mount is { } m)
        {
            world = Inverse(CfgRenderInfo.SlotMatrix(m.Own, Vector3.Zero)) * ParentSlotMatrix(parentInfo, m.Parent) * parentWorld;
            result.Parts.Add(new PlacedPart(part, name, world, parentName, m.Parent.SlotId, m.Own.SlotId, m.How));
        }
        else
        {
            world = parentWorld;
            result.Parts.Add(new PlacedPart(part, name, world, parentName, 0, 0, "no matching slot: parent origin"));
            result.Notes.Add($"{name}: no mount slot matches {parentName}; placed at the parent origin");
        }

        AddRenders(result, part, info, world, name);
        foreach (var sp in info.StockParts)
        {
            AddStockPart(result, part, info, world, name, sp, depth + 1);
        }
    }

    /// <summary>Places a free-standing gametype (rim, tyre) by its mount slot onto an explicit parent slot frame.</summary>
    private PlacedPart? Place(CarAssembly result, ResRef type, Matrix4x4 parentSlotWorld, string parentName, int parentSlot, int mountSlotHint)
    {
        var cfg = Content.GametypeCfg(type);
        if (cfg is null)
        {
            result.Notes.Add($"{type}: no cfg");
            return null;
        }

        var info = CfgRenderInfo.Parse(cfg);
        var own = info.Slots.FirstOrDefault(s => s.SlotId == mountSlotHint) ?? info.Slots.FirstOrDefault();
        var world = (own is null ? Matrix4x4.Identity : Inverse(CfgRenderInfo.SlotMatrix(own, Vector3.Zero))) * parentSlotWorld;
        var name = Path.GetFileNameWithoutExtension(cfg.Path.Replace('\\', '/'));
        var placed = new PlacedPart(type, name, world, parentName, parentSlot, own?.SlotId ?? 0, "explicit wheel slot");
        result.Parts.Add(placed);
        AddRenders(result, type, info, world, name);
        return placed;
    }

    private static Matrix4x4 ParentSlotMatrix(CfgRenderInfo parent, CfgSlot slot)
    {
        var basePos = slot.WheelIndex is { } wi && wi < parent.Wheels.Count ? parent.Wheels[wi].Position : Vector3.Zero;
        return CfgRenderInfo.SlotMatrix(slot, basePos);
    }

    private void AddRenders(CarAssembly result, ResRef owner, CfgRenderInfo info, Matrix4x4 world, string name)
    {
        var levels = info.Renders.Where(r => r.LodMax != int.MaxValue).Select(r => r.LodMax).DefaultIfEmpty(0).Max();
        foreach (var r in info.Renders)
        {
            if (Content.Resolve(owner.Archive, r.Id) is { } lr && Content.Entry(lr) is { TypeOfEntry: 13 })
            {
                result.Lights++;
                continue;
            }

            if (Content.Resolve(owner.Archive, r.Id) is not { } rr || Content.Render(rr) is not { } def)
            {
                result.Notes.Add($"{name}: render 0x{r.Id:X8} not found");
                continue;
            }

            result.Items.Add(new DrawItem(def, GameUnits.ScxScale * r.Local * world, r.LodMin, r.LodMax, info.Lods, name, levels));
        }
    }

    private static Matrix4x4 Inverse(Matrix4x4 m) => Matrix4x4.Invert(m, out var inv) ? inv : Matrix4x4.Identity;

    /// <summary>Finds the type 8 entry of an archive whose <c>native ... cfg</c> line names <paramref name="cfgName"/> (file name, case-insensitive).</summary>
    public ResRef? FindGametypeByCfg(string archive, string cfgName)
    {
        if (Content.Archive(archive) is not { } a)
        {
            return null;
        }

        foreach (var e in a.Entries.Where(e => e.TypeOfEntry == 8))
        {
            var native = RenderIndex.ParseText(a.GetData(e).Span).FirstOrDefault(p => p.Key == "native").Value;
            if (native is not null && Path.GetFileName(native.Replace('\\', '/')).Equals(Path.GetFileName(cfgName.Replace('\\', '/')), StringComparison.OrdinalIgnoreCase))
            {
                return new ResRef(archive, e.Id);
            }
        }

        return null;
    }

    /// <summary>The car gametypes (type 8 entries with <c>native car</c>) of an archive.</summary>
    public IEnumerable<(ResRef Ref, string Cfg)> CarTypes(string archive)
    {
        if (Content.Archive(archive) is not { } a)
        {
            yield break;
        }

        foreach (var e in a.Entries.Where(e => e.TypeOfEntry == 8))
        {
            var native = RenderIndex.ParseText(a.GetData(e).Span).FirstOrDefault(p => p.Key == "native").Value;
            if (native is not null && native.StartsWith("car ", StringComparison.OrdinalIgnoreCase))
            {
                yield return (new ResRef(archive, e.Id), native[4..].Trim());
            }
        }
    }
}
