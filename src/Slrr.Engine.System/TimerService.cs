using Slrr.Vm;
using VmEngine = Slrr.Vm.Vm;

namespace Slrr.Engine.SystemLib;

/// <summary>
/// <c>GameType.addTimer(seconds, id)</c>: one-shot timers. When due, the engine calls
/// <c>handleEvent(GameRef self, EVENT_TIME, int id)</c> on the object in a fresh VM thread named
/// <c>THRD-RUNVMI</c> (the name the exe uses for event invocations), so the handler may sleep or wait.
/// Timers run on <see cref="VmEngine.Time"/> (the wall clock, not the time-warped simulation clock): hypothesis, the
/// scripts only say "seconds" (docs/engine-api/entry-points.md). Timers fire in (due time, creation) order.
/// </summary>
public sealed class TimerService
{
    private readonly List<PendingTimer> _timers = [];
    private long _seq;

    /// <summary>Pending timer count.</summary>
    public int Count => _timers.Count;

    /// <summary>
    /// Whether an object currently accepts <c>EVENT_TIME</c>. The engine only delivers events in the object's event
    /// mask (<c>setEventMask</c>, a GameType native owned by the game-object layer); plug its check in here.
    /// Default: always deliver. A timer for an object that refuses is dropped.
    /// </summary>
    public Func<VmObject, bool> AcceptsTimeEvents { get; set; } = _ => true;

    /// <summary>Use <see cref="VmEngine.SimTime"/> instead of <see cref="VmEngine.Time"/> as the timer clock.</summary>
    public bool UseSimTime { get; set; }

    public void Add(VmEngine vm, VmObject target, float seconds, int id)
    {
        var now = UseSimTime ? vm.SimTime : vm.Time;
        _timers.Add(new PendingTimer(_seq++, now + Math.Max(0, seconds), target, id));
    }

    public int RemoveAll(VmObject target) => _timers.RemoveAll(t => ReferenceEquals(t.Target, target));

    public IEnumerable<(VmObject Target, int Id, float Due)> Pending => _timers.Select(t => (t.Target, t.Id, t.Due));

    /// <summary>Fires every timer that is due; returns how many handler threads were started.</summary>
    public int Pump(VmEngine vm)
    {
        var now = UseSimTime ? vm.SimTime : vm.Time;
        var due = _timers.Where(t => t.Due <= now).OrderBy(t => t.Due).ThenBy(t => t.Seq).ToList();
        if (due.Count == 0)
        {
            return 0;
        }

        _timers.RemoveAll(t => t.Due <= now);
        var fired = 0;
        foreach (var t in due)
        {
            if (!AcceptsTimeEvents(t.Target))
            {
                continue;
            }

            var handler = t.Target.Class.FindMethods("handleEvent", 3).FirstOrDefault(m => m.Params[1] == "I" && m.Params[2] == "I");
            if (handler is null)
            {
                continue;
            }

            vm.Spawn(handler, t.Target, [Value.Obj(t.Target), Value.Int(EventTime(vm)), Value.Int(t.Id)], "THRD-RUNVMI");
            fired++;
        }

        return fired;
    }

    /// <summary><c>GameRef.EVENT_TIME</c> as defined by the shipped class.</summary>
    public static int EventTime(VmEngine vm)
    {
        var cls = vm.LoadClass("java.util.resource.GameRef");
        vm.EnsureInitialized(cls);
        var f = cls.FindField("EVENT_TIME") ?? throw new VmException("GameRef.EVENT_TIME not found");
        return f.Owner.StaticValues[f.Slot].AsInt;
    }

    private sealed record PendingTimer(long Seq, float Due, VmObject Target, int Id);
}
