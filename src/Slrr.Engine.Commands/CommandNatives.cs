using System.Numerics;
using Slrr.Engine.Resources;
using Slrr.Vm;

namespace Slrr.Engine.Commands;

/// <summary>
/// VM-facing side of the command area. Register it AFTER the modules it wraps (ResourceNatives, and the host's natives
/// when present): it chains to whatever was registered before.
/// <list type="bullet">
/// <item><c>GameRef.queueEvent(ResourceRef,int,String)</c> (only with <see cref="RouteCommands"/>): EVENT_COMMAND texts go to
/// <see cref="CommandEngine.Execute"/> first; not consumed → the previous native (part routing, event queue). Hosts that already
/// route commands to an <c>IPhysicsWorld</c> (Slrr.Game) leave this off and use the decorator documented in docs/race/commands.md.</item>
/// <item><c>GameRef.getInfo(II)I</c> / <c>(I String)I</c>: previous native first; when it answers 0, <see cref="CommandEngine.AnswerInfo"/>
/// (GII_OWNER / GII_BONE / GII_RENDER / GII_CATEGORY of cars).</item>
/// <item>Fill-only (registered only when nobody did): <c>Vehicle.getSpeedSquare()F</c>, <c>getHorn()I</c>, <c>hasCrime()F</c>
/// (MEASURED: declared native, registered at 0x480500 / 0x43db60 / 0x440bf0), <c>Navigator.updateNavigator(GameRef,I)V</c>
/// (no-op, minimap belongs to the renderer), and with <see cref="FillMathGaps"/> <c>Vector3.length()F</c> and
/// <c>Ypr.&lt;init&gt;(Vector3)</c>, which Track.makeCamExternal needs for the external camera (observed missing in a headless race).</item>
/// </list>
/// The engine is resolved lazily because hosts create the ResourceEngine after collecting their native modules.
/// </summary>
public sealed class CommandNatives(Func<CommandEngine?> engine) : INativeModule
{
    private const string GR = "java.util.resource.GameRef";
    private const string LS = "Ljava.lang.String;";
    private const string LRR = "Ljava.util.resource.ResourceRef;";
    private const string Vehicle = "java.game.Vehicle";

    public CommandNatives(CommandEngine engine)
        : this(() => engine)
    {
    }

    /// <summary>Wrap queueEvent so EVENT_COMMAND reaches the engine (for hosts without their own command routing).</summary>
    public bool RouteCommands { get; init; }

    /// <summary>Register Vector3.length / Ypr(Vector3) when missing.</summary>
    public bool FillMathGaps { get; init; } = true;

    /// <summary>Natives this module added or wrapped (for reports).</summary>
    public List<string> Registered { get; } = [];

    public void Register(NativeRegistry r)
    {
        if (RouteCommands)
        {
            var desc = $"({LRR}I{LS})V";
            var prev = r.Find(GR, "queueEvent", desc);
            r.Register(GR, "queueEvent", desc, c =>
            {
                if (c.Int(1) == ResourceEngine.EventCommand && c.String(2) is { } text && engine() is { } e
                    && e.Execute(ResourceEngine.IdOf(c.Self), text).Consumed)
                {
                    return Value.Void;
                }

                return prev is null ? Value.Void : prev(c);
            }, replace: true);
            Registered.Add($"{GR}.queueEvent{desc} (wrap)");
        }

        WrapGetInfo(r, "(II)I");
        WrapGetInfo(r, $"(I{LS})I");

        Fill(r, Vehicle, "getSpeedSquare", "()F", c => Value.Float(engine()?.SpeedSquare(ResourceEngine.IdOf(c.Self)) ?? 0f));
        Fill(r, Vehicle, "getHorn", "()I", c => Value.Int(engine()?.Horn(ResourceEngine.IdOf(c.Self)) ?? 0));
        Fill(r, Vehicle, "hasCrime", "()F", c => Value.Float(engine()?.Race.CrimeOf(ResourceEngine.IdOf(c.Self)) ?? 0f));
        Fill(r, "java.game.Navigator", "updateNavigator", "(Ljava.util.resource.GameRef;I)V", _ => Value.Void);
        if (FillMathGaps)
        {
            Fill(r, "java.lang.Vector3", "length", "()F", c => Value.Float(ReadVector(c.Self).Length()));
            Fill(r, "java.lang.Ypr", "<init>", "(Ljava.lang.Vector3;)", c =>
            {
                // Yaw/pitch facing along the vector (same convention as Ypr.FromDirection; GUESSED).
                var o = Ypr.FromDirection(c.Object(0) is { } v ? ReadVector(v) : default);
                SetFloat(c.Self, "y", o.Yaw);
                SetFloat(c.Self, "p", o.Pitch);
                SetFloat(c.Self, "r", 0f);
                return Value.Void;
            });
        }
    }

    private void WrapGetInfo(NativeRegistry r, string desc)
    {
        var prev = r.Find(GR, "getInfo", desc);
        r.Register(GR, "getInfo", desc, c =>
        {
            var v = prev is null ? Value.Zero : prev(c);
            if (v.AsInt != 0 || engine() is not { } e)
            {
                return v;
            }

            return e.AnswerInfo(ResourceEngine.IdOf(c.Self), c.Int(0)) is { } a ? Value.Int(a) : v;
        }, replace: true);
        Registered.Add($"{GR}.getInfo{desc} (wrap)");
    }

    private void Fill(NativeRegistry r, string cls, string name, string desc, NativeMethod fn)
    {
        if (r.Contains(cls, name, desc))
        {
            return;
        }

        r.Register(cls, name, desc, fn);
        Registered.Add($"{cls}.{name}{desc}");
    }

    private static Vector3 ReadVector(VmObject o) => new(F(o, "x"), F(o, "y"), F(o, "z"));

    private static float F(VmObject o, string field) =>
        o.Class.FindField(field) is { IsStatic: false } f ? o.Fields[f.Slot].AsFloat : 0f;

    private static void SetFloat(VmObject o, string field, float value)
    {
        if (o.Class.FindField(field) is { IsStatic: false } f)
        {
            o.Fields[f.Slot] = Value.Float(value);
        }
    }
}
