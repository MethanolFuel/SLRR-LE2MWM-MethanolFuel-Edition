using System.Globalization;
using Slrr.Engine.Parts;
using Slrr.Engine.Ui;
using Slrr.Vm;

namespace Slrr.Game;

/// <summary>
/// Game state read through the VM (never cached): the player, money, the current car and its installed parts, and a
/// small dotted-path evaluator used by play scripts (<c>GameLogic.player.money</c>, <c>section.mode</c>).
/// </summary>
public sealed class GameView(GameHost host)
{
    private static readonly string[] Packages =
        ["java.game", "java.render", "java.render.osd", "java.render.osd.dialog", "java.io", "java.lang", "java.util", "java.util.resource", "java.sound", "java.game.parts"];

    public VmObject? Player => host.Player;

    public int Money => Probe.Field(Player, "money").AsInt;

    public string? PlayerName => host.Vm.GetString(Probe.Field(Player, "name").AsObject);

    /// <summary><c>player.car</c> (a Vehicle).</summary>
    public VmObject? Car => Probe.Field(Player, "car").AsObject;

    /// <summary>The car's Chassis script object (<c>Vehicle.chassis</c>).</summary>
    public VmObject? Chassis => Probe.Field(Car, "chassis").AsObject;

    /// <summary>Native twins of the car's installed parts (chassis first, depth first), empty without a car.</summary>
    public IReadOnlyList<PartState> InstalledParts =>
        host.Parts.Find(Chassis) is { } c ? PartsWorld.Tree(c).ToList() : [];

    /// <summary>Script class names of the installed parts.</summary>
    public IEnumerable<string> InstalledPartClasses => InstalledParts.Select(p => p.Object.Class.Name);

    /// <summary>Gametype ids (<c>getInfo(GII_TYPE)</c>) of the installed parts.</summary>
    public IEnumerable<int> InstalledPartTypes => InstalledParts.Select(p => p.TypeId);

    /// <summary>Items of <c>player.parts</c> (the parts bin inventory).</summary>
    public int PartsBinCount => Probe.Vector(host.Vm, Probe.Field(Probe.Field(Player, "parts").AsObject, "items").AsObject).Count;

    /// <summary>
    /// Evaluates a dotted path. The first segment is <c>section</c> (active GameState), <c>player</c>, <c>car</c>,
    /// <c>top</c> (top Osd), <c>money</c>, <c>parts</c> (installed part count), <c>bin</c> (parts bin size) or a class name
    /// (short names are searched in the java.game/render/io/lang/util packages) followed by a static field; the rest are
    /// instance fields. Vectors answer <c>.size</c>.
    /// </summary>
    public Value Eval(string path)
    {
        var seg = path.Split('.');
        Value v;
        var i = 1;
        switch (seg[0])
        {
            case "section": v = Value.Obj(host.ActiveSection); break;
            case "player": v = Value.Obj(Player); break;
            case "car": v = Value.Obj(Car); break;
            case "chassis": v = Value.Obj(Chassis); break;
            case "top": v = Value.Obj(host.TopOsd); break;
            case "money": return Value.Int(Money);
            case "parts": return Value.Int(InstalledParts.Count);
            case "bin": return Value.Int(PartsBinCount);
            case "time": return Value.Float(host.Vm.Time);
            default:
                var (cls, consumed) = ResolveClass(seg);
                if (cls is null)
                {
                    throw new ArgumentException($"unknown path root '{seg[0]}'");
                }

                if (consumed >= seg.Length)
                {
                    throw new ArgumentException($"path '{path}' names a class, not a field");
                }

                v = Probe.Static(host.Vm, cls, seg[consumed]);
                i = consumed + 1;
                break;
        }

        for (; i < seg.Length; i++)
        {
            var o = v.AsObject;
            if (o is null)
            {
                return Value.Null;
            }

            if (seg[i] == "size" && Script.IsA(o, "java.util.Vector"))
            {
                v = Value.Int(Probe.Vector(host.Vm, o).Count);
                continue;
            }

            if (seg[i] == "class")
            {
                return Value.Obj(host.Vm.NewString(o.Class.Name));
            }

            v = Probe.Field(o, seg[i]);
        }

        return v;
    }

    public string Format(Value v) => v.Kind switch
    {
        VmKind.Int => v.AsInt.ToString(CultureInfo.InvariantCulture),
        VmKind.Float => v.AsFloat.ToString("0.###", CultureInfo.InvariantCulture),
        VmKind.Object when v.Ref?.Native is string s => s,
        VmKind.Object when v.Ref is { } o => o.Class.Name + (Probe.Id(o) is var id and not 0 ? $"#{(uint)id:X8}" : string.Empty),
        _ => "null",
    };

    private (string? Class, int Consumed) ResolveClass(string[] seg)
    {
        // Longest dotted prefix that is a loaded class, else a short name in a known package.
        for (var n = seg.Length - 1; n >= 1; n--)
        {
            var name = string.Join('.', seg[..n]);
            if (host.Vm.LoadedClasses.Any(c => c.Name == name))
            {
                return (name, n);
            }
        }

        foreach (var p in Packages)
        {
            var name = p + "." + seg[0];
            if (host.Vm.LoadedClasses.Any(c => c.Name == name))
            {
                return (name, 1);
            }
        }

        return (null, 0);
    }
}
