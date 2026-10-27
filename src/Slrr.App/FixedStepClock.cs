namespace Slrr.App;

/// <summary>
/// Fixed-step accumulator for the host (scripts + physics run in steps of exactly <see cref="Step"/> seconds, as in every measured
/// race-wave run), decoupled from the display rate. A long real frame (window drag, map load) is clamped to <see cref="MaxFrame"/>
/// and at most <see cref="MaxSteps"/> steps run per display frame; the rest is dropped (the game slows down instead of spiralling).
/// </summary>
public sealed class FixedStepClock
{
    private double _acc;

    public FixedStepClock(double step, int maxSteps = 4, double maxFrame = 0.25)
    {
        if (!(step > 0) || maxSteps < 1 || !(maxFrame > 0))
        {
            throw new ArgumentOutOfRangeException(nameof(step));
        }

        Step = step;
        MaxSteps = maxSteps;
        MaxFrame = maxFrame;
    }

    public double Step { get; }

    public int MaxSteps { get; }

    public double MaxFrame { get; }

    /// <summary>Real seconds not simulated because of the clamps.</summary>
    public double Dropped { get; private set; }

    /// <summary>Fraction of a step accumulated but not yet simulated (0..1), for interpolation.</summary>
    public double Alpha => _acc / Step;

    /// <summary>Adds <paramref name="realDt"/> seconds of wall time; returns how many host steps to run now.</summary>
    public int Advance(double realDt)
    {
        if (!double.IsFinite(realDt) || realDt < 0)
        {
            realDt = 0;
        }

        if (realDt > MaxFrame)
        {
            Dropped += realDt - MaxFrame;
            realDt = MaxFrame;
        }

        _acc += realDt;
        var n = (int)Math.Floor((_acc / Step) + 1e-9);
        if (n > MaxSteps)
        {
            Dropped += (n - MaxSteps) * Step;
            _acc -= (n - MaxSteps) * Step;
            n = MaxSteps;
        }

        _acc = Math.Max(0, _acc - (n * Step));
        return n;
    }

    /// <summary>Forgets accumulated time (after a pause, so the game does not jump).</summary>
    public void Reset() => _acc = 0;
}
