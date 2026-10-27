using System.Globalization;
using System.Numerics;
using Slrr.Formats.Cfg;

namespace Slrr.Engine.Parts;

/// <summary>A type id paired with a slot id on that type (cfg <c>attach T S</c> / <c>compatible T S</c>).</summary>
public readonly record struct SlotRef(int TypeId, int SlotId);

/// <summary>One cfg <c>slot x y z yaw pitch roll ID</c> line with the lines that qualify it.</summary>
public sealed record SlotDefinition(int Id, Vector3 Position, Ypr Orientation)
{
    /// <summary>
    /// <c>attach T S</c> lines after this slot: the part type and slot on the OTHER side of the link. In car cfgs that is
    /// the stock part plugged in here (<c>slot ... 101; attach 0x00040200 1</c>), in part cfgs the parent it plugs into
    /// (shipped comments: <c>block-&gt;crankshaft_IN</c>). Measured from the cfg text; the engine's use is a hypothesis.
    /// </summary>
    public List<SlotRef> Attach { get; init; } = [];

    /// <summary>
    /// <c>compatible T S</c> lines after this slot: this part can take the place of type T's slot S (aftermarket shocks
    /// list the stock shock 0x00041000 slot 1, swaybars list the stock bars). Interpretation guessed from the data.
    /// </summary>
    public List<SlotRef> Compatible { get; init; } = [];

    /// <summary><c>slottype N</c> (group number; corner slots end in 1..4).</summary>
    public int? SlotType { get; set; }

    /// <summary><c>slotdmgmode 0xMASK</c>.</summary>
    public int? DamageMode { get; set; }
}

/// <summary>
/// One car cfg <c>wheel x y z yaw pitch roll a b c d</c> line with the <c>type mass sphere radius</c> and
/// <c>spring a b c</c> lines before it. Position/radius roles are measured from the stock cfgs (front wheels at -z,
/// left at -x, sphere radius 0.315 for a 0.63 m tyre); the four flags read "1 0 1 0" on front and "0 1 1 1" on rear
/// wheels (guess: steer, drive, brake, handbrake) and the spring triple is kept raw.
/// </summary>
public sealed record WheelDefinition(Vector3 Position, Ypr Orientation, float[] Flags, float Mass, float Radius, float[] Spring);

/// <summary>One cfg <c>body x y z yaw pitch roll mass mesh</c> line (car cfgs).</summary>
public readonly record struct BodyDefinition(Vector3 Position, float Mass);

/// <summary>
/// What the engine reads from a part's <c>native part|car some.cfg</c> before the script runs: slots, link rules,
/// category, stock parts and physical bodies (docs/formats/cfg.md; field meanings measured over the shipped cfgs,
/// matching rules are hypotheses, see docs/engine/parts.md).
/// </summary>
public sealed class PartDefinition
{
    /// <summary>Gametype id the definition belongs to (0 when unknown).</summary>
    public int TypeId { get; init; }

    public string? Source { get; init; }

    public List<SlotDefinition> Slots { get; } = [];

    /// <summary>Part-level <c>compatible</c> lines that precede every slot.</summary>
    public List<SlotRef> Compatible { get; } = [];

    public int Category { get; set; }

    public List<int> StockParts { get; } = [];

    public List<BodyDefinition> Bodies { get; } = [];

    public List<WheelDefinition> Wheels { get; } = [];

    public int Render { get; set; }

    public int Texture { get; set; }

    public float Mass => Bodies.Sum(b => b.Mass);

    public SlotDefinition? Slot(int id) => Slots.FirstOrDefault(s => s.Id == id);

    /// <summary>
    /// Builds a definition from a parsed cfg. <paramref name="resolveTypeId"/> maps the archive-local type ids written
    /// in the cfg (<c>0x000000E1</c>) to the ids used at run time; that mapping belongs to the resource layer
    /// (open question P0 #1), so the default keeps the literal value.
    /// </summary>
    public static PartDefinition FromCfg(CfgDocument cfg, int typeId = 0, Func<int, int>? resolveTypeId = null)
    {
        resolveTypeId ??= static id => id;
        var def = new PartDefinition { TypeId = typeId, Source = cfg.Path };
        SlotDefinition? current = null;
        (float Mass, float Radius) wheelType = default;
        float[] spring = [];
        foreach (var line in cfg.Lines)
        {
            if (line.Kind != CfgLineKind.Directive || line.Keyword is null)
            {
                continue;
            }

            var a = line.Args;
            switch (line.Keyword.ToLowerInvariant())
            {
                case "slot" when a.Count >= 7:
                    current = new SlotDefinition(Int(a[6]), new Vector3(F(a[0]), F(a[1]), F(a[2])), new Ypr(F(a[3]), F(a[4]), F(a[5])));
                    def.Slots.Add(current);
                    break;
                case "attach" when a.Count >= 2:
                    var at = new SlotRef(resolveTypeId(Int(a[0])), Int(a[1]));
                    (current?.Attach ?? def.Compatible).Add(at);
                    break;
                case "compatible" when a.Count >= 2:
                    var co = new SlotRef(resolveTypeId(Int(a[0])), Int(a[1]));
                    (current?.Compatible ?? def.Compatible).Add(co);
                    break;
                case "compatible" when a.Count == 1:
                    def.Compatible.Add(new SlotRef(resolveTypeId(Int(a[0])), 0));
                    break;
                case "slottype" when a.Count >= 1 && current is not null:
                    current.SlotType = Int(a[0]);
                    if (a.Count >= 3 && a[1].Equals("slotdmgmode", StringComparison.OrdinalIgnoreCase))
                    {
                        current.DamageMode = Int(a[2]);
                    }

                    break;
                case "slotdmgmode" when a.Count >= 1 && current is not null:
                    current.DamageMode = Int(a[0]);
                    break;
                case "category" when a.Count >= 1:
                    def.Category = Int(a[0]);
                    break;
                case "stockpart" when a.Count >= 1:
                    def.StockParts.Add(resolveTypeId(Int(a[0])));
                    break;
                case "body" when a.Count >= 7:
                    def.Bodies.Add(new BodyDefinition(new Vector3(F(a[0]), F(a[1]), F(a[2])), F(a[6])));
                    break;
                case "type" when a.Count >= 3 && a[1].Equals("sphere", StringComparison.OrdinalIgnoreCase):
                    wheelType = (F(a[0]), F(a[2]));
                    break;
                case "spring" when a.Count >= 1:
                    spring = a.Select(F).ToArray();
                    break;
                case "wheel" when a.Count >= 6:
                    def.Wheels.Add(new WheelDefinition(new Vector3(F(a[0]), F(a[1]), F(a[2])), new Ypr(F(a[3]), F(a[4]), F(a[5])),
                        a.Skip(6).Select(F).ToArray(), wheelType.Mass, wheelType.Radius, spring));
                    wheelType = default;
                    spring = [];
                    break;
                case "render" when a.Count >= 1 && def.Render == 0:
                    def.Render = Int(a[0]);
                    break;
                case "texture" when a.Count >= 1 && def.Texture == 0:
                    def.Texture = Int(a[0]);
                    break;
            }
        }

        return def;
    }

    private static float F(string s) =>
        float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var f) ? f : 0;

    internal static int Int(string s)
    {
        if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            return uint.TryParse(s.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var u) ? unchecked((int)u) : 0;
        }

        return int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i) ? i
            : float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var f) ? (int)f : 0;
    }
}
