using Slrr.Audio;

namespace Slrr.GameAudio;

/// <summary>
/// The exe's RPM-to-sound table update, transcribed from static disassembly of <c>StreetLegal_Redline.exe</c>
/// (MEASURED unless marked). Addresses:
/// <list type="bullet">
/// <item><c>0x4423e0</c> <c>SfxTable.addItem</c>: at most 16 rows per table (a 17th is ignored); the row stores <c>1/A</c> (reciprocal of the first float),
///   then B, C, D, E verbatim. The table object is 0x248 bytes: 16 rows x 0x24, row count at +0x240, an rpm scale at +0x244 that is initialised to 1.0 and has no script setter.</item>
/// <item><c>0x48f0e0</c> the per-frame table update (this class). For row i with rpm' = rpm x scale: active when B &lt;= rpm' &lt;= C;
///   pitch = rpm' x (1/A) x timeWarp; volume = lerp(D, E, (rpm'-B)/(C-B)) x fade x callerVolume.
///   fade starts at 1; if i &gt; 0 and rpm' &lt;= C[i-1]: fade = (rpm'-B)/(C[i-1]-B) when that span is &gt; 0 (linear fade-in over the overlap with the previous row);
///   if i &lt; n-1 and rpm' &gt;= B[i+1]: fade = 1-(rpm'-B[i+1])/(C-B[i+1]) when that span is &gt; 0 (linear fade-out; overwrites the fade-in). Rows with fade &lt;= 0 are skipped.</item>
/// <item><c>0x48cfb0</c> the play call: effective volume = soundDef.volume x callerVolume x fade x lerp; anything &lt;= 0.01 is not played.</item>
/// </list>
/// The sample file label (e.g. <c>2000RPM.wav</c>) never enters the formula, so the "disagreement" with the row rpm (A = 3500) is no
/// contradiction: A is simply the engine rpm at which that sample is heard at its native pitch.
/// </summary>
public static class MeasuredSfxTable
{
    /// <summary>Maximum rows per table (addItem refuses a 17th: count &lt; 0x10 test at 0x442438). MEASURED.</summary>
    public const int MaxRows = 16;

    /// <summary>Effective volumes at or below this are not played (0x48d05c, fcomp 0.01). MEASURED.</summary>
    public const float AudibleThreshold = 0.01f;

    /// <summary>rad/s to rpm (60 / 2 pi, constant 9.5493 at 0x5f0c14). MEASURED.</summary>
    public const float RadPerSecToRpm = 9.549297f;

    /// <summary>The engine rpm the exe feeds the tables: <c>|omega| x 9.5493 + 1</c> (0x451bc0..0x451bd3). MEASURED.</summary>
    public static float TableRpm(float angularSpeedRadPerSec) => (MathF.Abs(angularSpeedRadPerSec) * RadPerSecToRpm) + 1f;

    /// <summary>One row's result: <see cref="Gain"/> is fade x lerp(D,E) (before caller volume and the sound def volume); 0 = not played.</summary>
    public readonly record struct RowResult(float Gain, float Pitch)
    {
        public bool Active => Gain > 0f;
    }

    /// <summary>Evaluates all rows at <paramref name="rpm"/> (already <see cref="TableRpm"/>); pitch excludes timeWarp.</summary>
    public static void Evaluate(IReadOnlyList<EngineSfxItem> rows, float rpm, Span<RowResult> result)
    {
        var n = Math.Min(rows.Count, MaxRows);
        for (var i = 0; i < result.Length; i++)
        {
            result[i] = default;
        }

        for (var i = 0; i < n && i < result.Length; i++)
        {
            var it = rows[i];
            if (rpm < it.RpmMin || rpm > it.RpmMax)
            {
                continue;
            }

            var span = it.RpmMax - it.RpmMin;
            var t = span != 0f ? (rpm - it.RpmMin) / span : 0f;
            var vol = ((1f - t) * it.VolMin) + (t * it.VolMax);
            var fade = 1f;
            if (i > 0 && rpm <= rows[i - 1].RpmMax)
            {
                var w = rows[i - 1].RpmMax - it.RpmMin;
                if (w > 0f)
                {
                    fade = (rpm - it.RpmMin) / w;
                }
            }

            if (i < n - 1 && rpm >= rows[i + 1].RpmMin)
            {
                var w = it.RpmMax - rows[i + 1].RpmMin;
                if (w > 0f)
                {
                    fade = 1f - ((rpm - rows[i + 1].RpmMin) / w);
                }
            }

            if (fade <= 0f)
            {
                continue;
            }

            var pitch = it.RefRpm != 0f ? rpm * (1f / it.RefRpm) : 0f;
            result[i] = new RowResult(vol * fade, pitch);
        }
    }

    /// <summary>
    /// Which table plays at which volume for a throttle (0x451bd7..0x451d98, MEASURED): throttle &lt;= 0.1 plays table 1 (when it has rows, else table 0) at 1.0;
    /// 0.1 &lt; throttle &lt;= 0.5 with table 1 non-empty crossfades table 1 x (1-v) and table 0 x v, v = (throttle-0.1) x 2.5; otherwise table 0 at 1.0.
    /// All shipped engines leave table 1 empty, so table 0 always plays at 1.0 (the throttle does not change the engine-block volume).
    /// The throttle-to-table assignment of the crossfade (v to table 0) is read from the argument order, not exercised by any shipped data.
    /// </summary>
    public static (float Table0, float Table1) EngineTableVolumes(float throttle, bool table1HasRows)
    {
        var thr = MathF.Abs(throttle);
        if (thr <= 0.1f)
        {
            return table1HasRows ? (0f, 1f) : (1f, 0f);
        }

        if (thr <= 0.5f && table1HasRows)
        {
            var v = (thr - 0.1f) * 2.5f;
            return (v, 1f - v);
        }

        return (1f, 0f);
    }

    /// <summary>Exhaust (table 2) volume = <c>minVol + (1 - minVol) x |throttle|</c> (0x451f1a..0x451f63); only while rpm' &gt; 1. MEASURED.</summary>
    public static float ExhaustVolume(float throttle, float exhaustMinVol) => exhaustMinVol + ((1f - exhaustMinVol) * MathF.Abs(throttle));

    /// <summary>
    /// Transmission whine (0x451f6b..0x452086, structure MEASURED, field roles GUESSED). x = drive rpm = omega x 9.5493 + 1; silent for |x| &lt;= 1;
    /// |x| &lt; 50 is lifted to +50 (forward); forward when x &gt; 0 else reverse; pitch = |x| x timeWarp / rpm_trans_fwd|rev (default 1400);
    /// volume = min(|torque| x 1e-4, 1).
    /// </summary>
    public static (bool Forward, float Pitch, float Gain)? Whine(float driveAngularSpeed, float driveTorque, float rpmTransFwd, float rpmTransRev, float timeWarp)
    {
        var x = (driveAngularSpeed * RadPerSecToRpm) + 1f;
        var ax = MathF.Abs(x);
        var gain = MathF.Min(MathF.Abs(driveTorque) * 1e-4f, 1f);
        if (ax <= 1f)
        {
            return null;
        }

        if (ax < 50f)
        {
            x = 50f;
            ax = 50f;
        }

        var fwd = x > 0f;
        var rpmTrans = fwd ? rpmTransFwd : rpmTransRev;
        return (fwd, rpmTrans > 0f ? ax * timeWarp / rpmTrans : 0f, gain);
    }
}
