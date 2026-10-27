using System.Globalization;
using System.Numerics;
using Slrr.Engine.Parts;
using Slrr.Engine.Resources;
using Slrr.Formats.Cfg;
using Slrr.Game.Render.Gl;
using Slrr.Game.Render.Scene;
using Slrr.Render.Content;
using Slrr.Render.Scene;

namespace Slrr.Game.Render.Cars;

/// <summary>A <c>camera x y z yaw pitch roll fov</c> line of a car cfg (index 0 is the driver's eye, MEASURED: x -0.395 on the LHD Nonus).</summary>
public sealed record CfgCamera(Vector3 Position, Vector3 Ypr, float Fov);

/// <summary>The cfg's <c>ext_camera fov look-offset eye-offset ypr distance</c> (roles GUESSED, values e.g. <c>120 0,1.8,0 0.0,1.0,-2.0 0,0.0,0.0 5</c>).</summary>
public sealed record CfgExtCamera(float Fov, Vector3 A, Vector3 B, float Distance);

/// <summary>Everything the renderer reads from a part's gametype cfg, cached per gametype.</summary>
public sealed class PartCfg
{
    public required ResRef Type { get; init; }

    public required string Name { get; init; }

    public required CfgRenderInfo Info { get; init; }

    /// <summary>The cfg <c>texture</c> line (raw id): the default paint of the part, equal to render texture 0 in the shipped parts (MEASURED on Duhen hood_2).</summary>
    public uint? DefaultTexture { get; init; }

    /// <summary>
    /// The <c>flags N</c> line after each <c>render</c> line (0 = none), aligned with <c>Info.Renders</c>. MEASURED on all shipped car/part cfgs
    /// through the comments on the render lines: 1 = BRAKE (44+10), 2 = REVERSING (38+10), 4 = CITY (125+20+...: lit-lamp variants), 16/32 = indicators
    /// (IND/IDX/INDICATOR), 256 = exhaust flame / nitro / reverse effects, 8 unknown. A line without flags is always drawn.
    /// </summary>
    public List<int> RenderFlags { get; } = [];

    public List<CfgCamera> Cameras { get; } = [];

    public CfgExtCamera? ExtCamera { get; set; }
}

/// <summary>Output of <see cref="CarVisualBuilder.Build"/>.</summary>
public sealed class CarFrame
{
    public List<PaintedItem> Items { get; } = [];

    public List<GlowBillboard> Glows { get; } = [];

    public int Parts { get; set; }

    public int PaintedParts { get; set; }

    public int Lights { get; set; }

    /// <summary>Render lines with a <c>flags</c> state bit that is currently off (not drawn).</summary>
    public int StateVariantsHidden { get; set; }

    public List<string> Notes { get; } = [];
}

/// <summary>
/// Turns the host's live part tree (<see cref="PartState"/> links, which parts are installed, each part's paint texture) into draw items.
/// Placement follows <c>Slrr.Render.Cars.CarAssembler</c> (child mount slot onto parent slot, MEASURED pairing) but reads the installed
/// tree instead of the cfg <c>stockpart</c> list, so parts the player bought or removed show up, and wheels are animated from
/// <see cref="CarState"/>.
/// <para>Paint (MEASURED structure, GUESSED application): the scripts colour a car with <c>Part.setTexture(resource id)</c> (Descriptor.color from
/// <c>GameLogic.CARCOLORS</c>, applied to every stock part; <c>Vehicle.loadSkin</c> sets it per part type). A part's cfg <c>texture</c> line
/// names the same kind of resource and equals texture 0 of its render entry, so the painted texture replaces that render texture.</para>
/// </summary>
public sealed class CarVisualBuilder(GameContent content, ResourceEngine engine)
{
    private const int MaxDepth = 12;

    private readonly Dictionary<int, PartCfg?> _cfgs = [];
    private readonly Dictionary<(int Type, int Texture), (string? From, string? To)> _paint = [];
    private readonly Dictionary<ResRef, GlowStyle?> _lights = [];

    public GameContent Content { get; } = content;

    /// <summary>Paints shown even when a part has no paint texture (default: only parts with a script-set texture are repainted).</summary>
    public bool ApplyPaint { get; set; } = true;

    /// <summary>
    /// Cull back faces of opaque car meshes. MEASURED (CullMeasurement, Nonus 800x600): 8 of 480,000 pixels differ from the double-sided
    /// render, against 6,914 when glass is culled too, and the frame is ~45 % cheaper under llvmpipe.
    /// </summary>
    public bool CullOpaque { get; set; } = true;

    /// <summary>
    /// Use the <c>lf_texture</c> of a light entry (<c>cars\racers.rpk</c> 0x100 "lensflare") for its glow. Off by default: the file looks like an atlas of flare
    /// elements (the shipped Flares.ptx is a 256x256 sheet: a glare top-left, streaks top-right, discs below; only the top-left quarter is used, GUESSED), else a soft disc.
    /// </summary>
    public bool UseLensFlareTexture { get; set; } = true;

    /// <summary>Distinct (cfg texture file, painted texture file) substitutions made so far (diagnostics).</summary>
    public IEnumerable<(string From, string To)> PaintSubstitutions =>
        _paint.Values.Where(v => v.From is not null && v.To is not null).Select(v => (v.From!, v.To!)).Distinct();

    /// <summary>Height of the car origin above the ground when its tyres touch it: -(lowest cfg wheel y - tyre radius), tyre radius from the wheel state (no suspension sag). Null without wheel data.</summary>
    public float? RideHeight(PartState chassis)
    {
        ArgumentNullException.ThrowIfNull(chassis);
        if (Cfg(chassis) is not { Info.Wheels.Count: > 0 } cfg)
        {
            return null;
        }

        var wheels = chassis.Chassis?.Wheels;
        var radius = wheels is { Count: > 0 } && wheels[0].Radius > 0.1f ? wheels[0].Radius : cfg.Info.Wheels[0].Radius;
        return -(cfg.Info.Wheels.Min(w => w.Position.Y) - radius);
    }

    public PartCfg? Cfg(PartState part)
    {
        ArgumentNullException.ThrowIfNull(part);
        if (_cfgs.TryGetValue(part.TypeId, out var hit))
        {
            return hit;
        }

        PartCfg? result = null;
        if (HostIds.ToResRef(engine, part.TypeId) is { } type && Content.GametypeCfg(type) is { } doc)
        {
            result = Parse(type, doc);
        }

        _cfgs[part.TypeId] = result;
        return result;
    }

    private PartCfg Parse(ResRef type, CfgDocument doc)
    {
        uint? texture = null;
        var renderFlags = new List<int>();
        var cameras = new List<CfgCamera>();
        CfgExtCamera? ext = null;
        foreach (var l in doc.Lines)
        {
            if (l.Kind != CfgLineKind.Directive || l.Keyword is null)
            {
                continue;
            }

            var a = l.Args;
            switch (l.Keyword.ToLowerInvariant())
            {
                case "texture" when texture is null && a.Count >= 1 && GameContent.TryHex(a[0], out var t):
                    texture = t;
                    break;
                case "render" or "deformable" when a.Count >= 1 && GameContent.TryHex(a[0], out var rid) && rid != 0:
                    renderFlags.Add(0); // same condition as CfgRenderInfo.Parse, so the list stays aligned with Info.Renders
                    break;
                case "flags" when renderFlags.Count > 0 && a.Count >= 1 && int.TryParse(a[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var fl):
                    renderFlags[^1] = fl;
                    break;
                case "camera" when a.Count >= 7:
                    cameras.Add(new CfgCamera(new Vector3(GameContent.F(a, 0), GameContent.F(a, 1), GameContent.F(a, 2)),
                        new Vector3(GameContent.F(a, 3), GameContent.F(a, 4), GameContent.F(a, 5)), GameContent.F(a, 6, 90)));
                    break;
                case "ext_camera" when a.Count >= 5:
                    ext = new CfgExtCamera(GameContent.F(a, 0, 90), Vec(a[1]), Vec(a[2]), GameContent.F(a, 4, 5));
                    break;
            }
        }

        var name = Content.Entry(type)?.Alias ?? Path.GetFileNameWithoutExtension(doc.Path.Replace('\\', '/'));
        var cfg = new PartCfg { Type = type, Name = name, Info = CfgRenderInfo.Parse(doc), DefaultTexture = texture };
        cfg.RenderFlags.AddRange(renderFlags);
        cfg.Cameras.AddRange(cameras);
        cfg.ExtCamera = ext;
        return cfg;
    }

    private static Vector3 Vec(string s)
    {
        var p = s.Split(',');
        return p.Length >= 3 && float.TryParse(p[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var x) &&
               float.TryParse(p[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var y) &&
               float.TryParse(p[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var z)
            ? new Vector3(x, y, z)
            : Vector3.Zero;
    }

    /// <summary>Draw items for the whole car. <paramref name="carWorld"/> maps car space (metres) to the scene.</summary>
    public CarFrame Build(PartState chassis, Matrix4x4 carWorld, CarState? state, bool forceLights = false)
    {
        ArgumentNullException.ThrowIfNull(chassis);
        var frame = new CarFrame();
        state ??= new CarState();
        var lights = forceLights ? new CarState { Wheels = state.Wheels, Headlights = true, BrakeLights = state.BrakeLights } : state;
        Place(frame, chassis, null, null, Matrix4x4.Identity, carWorld, lights, 0);
        return frame;
    }

    private void Place(CarFrame frame, PartState part, PartState? parent, SlotState? parentSlot, Matrix4x4 parentWorld, Matrix4x4 carWorld, CarState state, int depth)
    {
        if (depth > MaxDepth)
        {
            frame.Notes.Add($"{part}: tree deeper than {MaxDepth}");
            return;
        }

        var cfg = Cfg(part);
        var world = parentWorld;
        var wheelIndex = (int?)null;
        if (parent is not null && parentSlot is not null && cfg is not null && Cfg(parent) is { } pcfg)
        {
            var mine = cfg.Info.Slots.FirstOrDefault(s => s.SlotId == part.MountSlot?.Id);
            var theirs = pcfg.Info.Slots.FirstOrDefault(s => s.SlotId == parentSlot.Id);
            if (mine is not null && theirs is not null)
            {
                var basePos = theirs.WheelIndex is { } wi && wi < pcfg.Info.Wheels.Count ? pcfg.Info.Wheels[wi].Position : Vector3.Zero;
                var slotWorld = CfgRenderInfo.SlotMatrix(theirs, basePos);
                world = Inverse(CfgRenderInfo.SlotMatrix(mine, Vector3.Zero)) * slotWorld * parentWorld;
                wheelIndex = theirs.WheelIndex;
                world = Animate(part, wheelIndex, Vector3.Transform(Vector3.Zero, slotWorld * parentWorld), world, state);
            }
            else
            {
                frame.Notes.Add($"{cfg.Name}: no slot pairing (mount {part.MountSlot?.Id}, parent slot {parentSlot.Id}); placed at the parent origin");
            }
        }

        if (cfg is null)
        {
            frame.Notes.Add($"{part}: no cfg");
        }
        else
        {
            frame.Parts++;
            AddRenders(frame, part, cfg, world * carWorld, state);
        }

        foreach (var (slot, child) in part.Children)
        {
            Place(frame, child, part, slot, world, carWorld, state, depth + 1);
        }
    }

    /// <summary>Wheel animation about the hub (car space). Rims, tyres and their children: spin, steer, travel; brakes: steer, travel (GUESSED split).</summary>
    private static Matrix4x4 Animate(PartState part, int? wheelIndex, Vector3 hub, Matrix4x4 world, CarState state)
    {
        if (wheelIndex is not { } wi || wi < 0 || wi >= state.Wheels.Length)
        {
            return world;
        }

        var name = part.Object.Class.Name;
        var rolls = name.Contains(".wheels.rims.", StringComparison.Ordinal) || name.Contains(".wheels.tyres.", StringComparison.Ordinal);
        var follows = rolls || name.Contains(".brakes.", StringComparison.Ordinal);
        if (!follows)
        {
            return world;
        }

        var w = state.Wheels[wi];
        var m = Matrix4x4.CreateTranslation(-hub);
        if (rolls)
        {
            m *= Matrix4x4.CreateRotationX(-w.Spin);
        }

        m *= Matrix4x4.CreateRotationY(w.Steer) * Matrix4x4.CreateTranslation(hub + new Vector3(0, w.Travel, 0));
        return world * m;
    }

    private void AddRenders(CarFrame frame, PartState part, PartCfg cfg, Matrix4x4 world, CarState state)
    {
        var (from, to) = ApplyPaint ? PaintOf(part, cfg) : (null, null);
        var levels = cfg.Info.Renders.Where(r => r.LodMax != int.MaxValue).Select(r => r.LodMax).DefaultIfEmpty(0).Max();
        var painted = false;
        var active = (state.BrakeLights ? 1 : 0) | (state.Reversing ? 2 : 0) | (state.Headlights ? 4 : 0) | (state.IndicatorLeft ? 16 : 0) | (state.IndicatorRight ? 32 : 0);
        for (var ri = 0; ri < cfg.Info.Renders.Count; ri++)
        {
            var r = cfg.Info.Renders[ri];
            var flags = ri < cfg.RenderFlags.Count ? cfg.RenderFlags[ri] : 0;
            if (Content.Resolve(cfg.Type.Archive, r.Id) is not { } rr)
            {
                frame.Notes.Add($"{cfg.Name}: render 0x{r.Id:X8} not found");
                continue;
            }

            if (Content.Entry(rr) is { TypeOfEntry: 13 })
            {
                frame.Lights++;
                // Lights carry the same state flags; unflagged lamps follow their kind (headlights: headlights, taillights: lights or brake).
                if (Glow(rr) is { } style && (flags != 0 ? (flags & active) != 0 : style.Tail ? state.Headlights || state.BrakeLights : state.Headlights))
                {
                    var p = Vector3.Transform(Vector3.Zero, r.Local * world);
                    frame.Glows.Add(new GlowBillboard(p, style.Color, style.Size, style.Texture, style.Texture is null ? default : new Vector4(0, 0, 0.5f, 0.5f)));
                }

                continue;
            }

            if (flags != 0 && (flags & active) == 0)
            {
                frame.StateVariantsHidden++; // a lamp / indicator / reverse / flame variant of the part whose state is off
                continue;
            }

            if (Content.Render(rr) is not { } def)
            {
                frame.Notes.Add($"{cfg.Name}: render {rr} is not a mesh");
                continue;
            }

            var useFrom = from is not null && def.TexturePaths.Any(p => string.Equals(p, from, StringComparison.OrdinalIgnoreCase)) ? from : null;
            painted |= useFrom is not null && to is not null;
            frame.Items.Add(new PaintedItem(
                new DrawItem(def, GameUnits.ScxScale * r.Local * world, r.LodMin, r.LodMax, cfg.Info.Lods, cfg.Name, levels), useFrom, useFrom is null ? null : to, CullOpaque));
        }

        if (painted)
        {
            frame.PaintedParts++;
        }
    }

    /// <summary>(file of the cfg default texture, file of the part's current paint texture); both null when there is nothing to substitute.</summary>
    private (string? From, string? To) PaintOf(PartState part, PartCfg cfg)
    {
        if (part.Texture == 0 || cfg.DefaultTexture is not { } raw)
        {
            return (null, null);
        }

        var key = (part.TypeId, part.Texture);
        if (_paint.TryGetValue(key, out var hit))
        {
            return hit;
        }

        string? from = null, to = null;
        if (Content.Resolve(cfg.Type.Archive, raw) is { } fr && Content.SourceFile(fr) is { } fs)
        {
            from = Content.ResolveTexture(fs);
        }

        if (HostIds.ToResRef(engine, part.Texture) is { } tr && Content.SourceFile(tr) is { } ts)
        {
            to = Content.ResolveTexture(ts);
        }

        var result = from is null || to is null || string.Equals(from, to, StringComparison.OrdinalIgnoreCase) ? (null, null) : (from, to);
        _paint[key] = result;
        return result;
    }

    private sealed record GlowStyle(Vector3 Color, float Size, string? Texture, bool Tail);

    /// <summary>
    /// Light entry (type 13) as a glow: colour = <c>lf_glow_color</c> read as A,R,G,B (headlights 1 0.898 0.898 0.898 white, taillights
    /// 1 0.5 0 0 red; ARGB order GUESSED from those values), size from <c>lf_glow_maxsize</c> (units unknown, 1 unit = 3 cm GUESSED),
    /// texture <c>lf_texture</c> when it resolves to a texture file, else a soft disc.
    /// </summary>
    private GlowStyle? Glow(ResRef light)
    {
        if (_lights.TryGetValue(light, out var hit))
        {
            return hit;
        }

        GlowStyle? style = null;
        var kv = Content.Text(light);
        string? Get(string k) => kv.FirstOrDefault(p => p.Key == k).Value;
        var tokens = (Get("lf_glow_color") ?? Get("diffuse"))?.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (tokens is { Length: >= 4 })
        {
            var color = new Vector3(F(tokens[1]), F(tokens[2]), F(tokens[3]));
            var peak = Math.Max(color.X, Math.Max(color.Y, color.Z));
            color = peak > 0.01f ? color / peak : Vector3.One; // additive glow: keep the hue, full brightness (the dim taillight value 0.5 would vanish)
            var size = Get("lf_glow_maxsize") is { } ms ? Math.Clamp(F(ms), 1f, 20f) * 0.02f : 0.15f;
            string? tex = null;
            if (UseLensFlareTexture && Get("lf_texture") is { } lt && GameContent.TryHex(lt, out var tid) && Content.Resolve(light.Archive, tid) is { } tref &&
                Content.SourceFile(tref) is { } ts)
            {
                tex = Content.ResolveTexture(ts);
            }

            style = new GlowStyle(color, size, tex, string.Equals(Content.Entry(light)?.Alias, "taillights", StringComparison.OrdinalIgnoreCase));
        }

        _lights[light] = style;
        return style;
    }

    private static float F(string s) => float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) && float.IsFinite(v) ? v : 0f;

    private static Matrix4x4 Inverse(Matrix4x4 m) => Matrix4x4.Invert(m, out var inv) ? inv : Matrix4x4.Identity;
}
