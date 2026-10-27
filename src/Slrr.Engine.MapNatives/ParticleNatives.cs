using System.Numerics;
using Slrr.Engine.Resources;
using Slrr.Vm;

namespace Slrr.Engine.MapNatives;

/// <summary>One emitter action set by <c>setSource</c>/<c>setDirectSource</c> (argument roles GUESSED: action name, position, 2 floats, direction, 3 floats, a second name).</summary>
public sealed record ParticleAction(string Name, bool Direct, Vector3? Position, float A, float B, Vector3? Direction, float C, float D, float E, string? Extra);

/// <summary>State of a <c>java.util.resource.ParticleSystem</c> render instance (kept in <see cref="ResourceHandle.Extension"/>).</summary>
public sealed class ParticleState
{
    public required int InstanceId { get; init; }

    public required int ConfigId { get; init; }

    public bool Emitting { get; set; } = true;

    public float Frequency { get; set; } = 1f;

    public int Permanent { get; set; }

    public Dictionary<string, ParticleAction> Actions { get; } = new(StringComparer.Ordinal);

    public Dictionary<string, (Vector3? Position, float Value)> Counters { get; } = new(StringComparer.Ordinal);
}

/// <summary>
/// <c>java.util.resource.ParticleSystem</c>: 9 natives (signatures MEASURED). <c>init(cfg, parent, name)</c> creates a
/// render instance (type 3) below <c>parent</c> with the particle cfg as its type, like <c>RenderRef.create</c> does
/// (GUESSED); the rest stores emitter state for the renderer. Only <c>init</c> has a call site in the shipped scripts.
/// </summary>
public sealed class ParticleNatives(MapWorlds maps)
{
    public const string Cls = "java.util.resource.ParticleSystem";
    private const string LS = "Ljava.lang.String;";
    private const string LV = "Ljava.lang.Vector3;";

    public List<ParticleState> Created { get; } = [];

    public static ParticleState? StateOf(VmObject? o) => ResourceEngine.Handle(o)?.Extension as ParticleState;

    public void Register(Action<string, string, string, NativeMethod> add)
    {
        add(Cls, "init", "(Ljava.util.resource.ResourceRef;Ljava.util.resource.RenderRef;Ljava.lang.String;)V", c =>
        {
            var cfg = ResourceEngine.IdOf(c.Object(0));
            var n = maps.Resources.Tree.CreateInstance(ResourceTypes.InstanceRender, ResourceEngine.IdOf(c.Object(1)), c.String(2) ?? "particlesystem", cfg);
            var h = ResourceEngine.EnsureHandle(c.Self);
            h.Id = n.Id;
            var s = new ParticleState { InstanceId = n.Id, ConfigId = cfg };
            h.Extension = s;
            n.Properties["particles"] = s;
            Created.Add(s);
            return Value.Void;
        });
        add(Cls, "stop", "()V", c => With(c, s => s.Emitting = false));
        add(Cls, "setFreq", "(F)V", c => With(c, s => s.Frequency = c.Float(0)));
        add(Cls, "setDirectSource", $"({LS}{LV}FF{LV}FFF{LS})V", c => With(c, s => SetAction(c, s, direct: true)));
        add(Cls, "setSource", $"({LS}{LV}FF{LV}FFF{LS})V", c => With(c, s => SetAction(c, s, direct: false)));
        add(Cls, "setCounter", $"({LS}{LV}F)V", c => With(c, s => s.Counters[c.String(0) ?? string.Empty] = (ScriptValues.ReadVector(c.Object(1)), c.Float(2))));
        add(Cls, "getCounter", $"({LS})I", c =>
            Value.Int(StateOf(c.Self) is { } s && s.Counters.TryGetValue(c.String(0) ?? string.Empty, out var v) ? (int)v.Value : 0));
        add(Cls, "delAction", $"({LS})V", c => With(c, s =>
        {
            s.Actions.Remove(c.String(0) ?? string.Empty);
            s.Counters.Remove(c.String(0) ?? string.Empty);
        }));
        add(Cls, "modePermanent", "(I)V", c => With(c, s => s.Permanent = c.Int(0)));
    }

    private static void SetAction(NativeCall c, ParticleState s, bool direct)
    {
        var name = c.String(0) ?? string.Empty;
        s.Actions[name] = new ParticleAction(name, direct, ScriptValues.ReadVector(c.Object(1)), c.Float(2), c.Float(3),
            ScriptValues.ReadVector(c.Object(4)), c.Float(5), c.Float(6), c.Float(7), c.String(8));
    }

    private static Value With(NativeCall c, Action<ParticleState> act)
    {
        if (StateOf(c.Self) is { } s)
        {
            act(s);
        }

        return Value.Void;
    }
}
