using Slrr.Engine.Resources;
using Slrr.Vm;

namespace Slrr.Engine.MapNatives;

/// <summary>Playback state of one <c>java.util.resource.Animation</c> (kept in <see cref="VmObject.Native"/>; Animation extends Native, not ResourceRef).</summary>
public sealed class AnimationState
{
    public required int TargetId { get; init; }

    public required int AnimationId { get; init; }

    public float Speed { get; set; } = 1f;

    public float Fade { get; set; }

    public bool Playing { get; set; }

    public bool Looping { get; set; }

    /// <summary>Position in seconds at <see cref="Since"/>.</summary>
    public float BasePos { get; set; }

    /// <summary>VM time at which <see cref="BasePos"/> was taken.</summary>
    public float Since { get; set; }

    public bool Finalized { get; set; }

    /// <summary>Position at VM time <paramref name="now"/>; the clip length is unknown (no animation decoder), so it grows without wrapping. GUESSED.</summary>
    public float PosAt(float now) => Playing ? BasePos + ((now - Since) * Speed) : BasePos;
}

/// <summary>
/// <c>java.util.resource.Animation</c>: 9 natives (signatures MEASURED from the shipped class; registered in the exe).
/// Semantics GUESSED from the call sites (City: starter lady <c>setSpeed(2.0)</c> + <c>play()</c>; ROCTrack: crowd
/// <c>loopPlay()</c>): a clock per animation, no skeleton is posed. The native constructor stores the RenderRef in the
/// script field <c>obj</c> (field MEASURED in the class, assignment GUESSED).
/// </summary>
public sealed class AnimationNatives
{
    public const string Cls = "java.util.resource.Animation";

    public List<AnimationState> Created { get; } = [];

    public void Register(Action<string, string, string, NativeMethod> add)
    {
        add(Cls, "<init>", "(Ljava.util.resource.RenderRef;Ljava.util.resource.ResourceRef;)", c =>
        {
            var target = c.Object(0);
            var s = new AnimationState { TargetId = ResourceEngine.IdOf(target), AnimationId = ResourceEngine.IdOf(c.Object(1)), Since = c.Vm.Time };
            c.Self.Native = s;
            if (target is not null && c.Self.Class.FindField("obj") is not null)
            {
                ScriptValues.Set(c.Self, "obj", Value.Obj(target));
            }

            Created.Add(s);
            return Value.Void;
        });
        add(Cls, "finalize", "()V", c =>
        {
            if (c.Self.Native is AnimationState s)
            {
                s.Finalized = true;
                s.Playing = false;
            }

            return Value.Void;
        });
        add(Cls, "setSpeed", "(F)V", c => With(c, s =>
        {
            Rebase(s, c.Vm.Time);
            s.Speed = c.Float(0);
        }));
        add(Cls, "setFade", "(F)V", c => With(c, s => s.Fade = c.Float(0)));
        add(Cls, "play", "()V", c => With(c, s => Start(s, c.Vm.Time, loop: false)));
        add(Cls, "loopPlay", "()V", c => With(c, s => Start(s, c.Vm.Time, loop: true)));
        add(Cls, "pause", "()V", c => With(c, s =>
        {
            Rebase(s, c.Vm.Time);
            s.Playing = false;
        }));
        add(Cls, "seek", "(F)V", c => With(c, s =>
        {
            s.BasePos = c.Float(0);
            s.Since = c.Vm.Time;
        }));
        add(Cls, "getPos", "()F", c => Value.Float(c.Self.Native is AnimationState s ? s.PosAt(c.Vm.Time) : 0f));
    }

    private static void Start(AnimationState s, float now, bool loop)
    {
        s.BasePos = 0;
        s.Since = now;
        s.Playing = true;
        s.Looping = loop;
    }

    private static void Rebase(AnimationState s, float now)
    {
        s.BasePos = s.PosAt(now);
        s.Since = now;
    }

    private static Value With(NativeCall c, Action<AnimationState> act)
    {
        if (c.Self.Native is AnimationState s)
        {
            act(s);
        }

        return Value.Void;
    }
}
