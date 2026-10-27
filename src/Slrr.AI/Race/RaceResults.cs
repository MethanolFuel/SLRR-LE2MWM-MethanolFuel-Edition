using System.Globalization;
using System.Text;

namespace Slrr.AI.Race;

/// <summary>One row of the results.</summary>
/// <param name="Time">Race time at the finish (s); null for DNF / still racing.</param>
/// <param name="Gap">Seconds behind the winner (finishers) or behind the leader at the last common 10 m mark.</param>
public sealed record RaceResultRow(int Position, string Name, int CarId, EntrantStatus Status, string? DnfReason, double? Time,
    IReadOnlyList<double> LapTimes, double? BestLap, double? Gap, double Progress, int Recoveries);

/// <summary>
/// The values the shipped results dialog shows for a two-car race (MEASURED from the decompiled
/// <c>java.game.RaceDialog</c> and its callers): <c>RaceDialog(controller, flags, background, winner.character,
/// loser.character, winnerName, loserName, winnerTime, loserTime, prize, mode[, winner, loser])</c>. Mode 1 and 3 print
/// "Name \n Time: m:ss.xx" (<c>String.timeToString(t, TCF_NOHOURS)</c>) and a time of exactly 1000.0 prints "--:--:--";
/// mode 0/1/5 add the bet/prize line ("PINK SLIPS" when the prize is 0, "PRESTIGE ONLY" in mode 5), 2/3 the plain
/// "X racing against / has won against Y". City's day race passes times 0.0 (mode 0), ROCTrack the measured
/// <c>playerTime/botTime</c> (mode 3), City's night race <c>nrTime1/nrTime2</c> (mode 1).
/// </summary>
/// <param name="WinnerTime">Time of the winner, or <see cref="RaceResults.NoTime"/>.</param>
public sealed record RaceDialogArgs(string WinnerName, string LoserName, float WinnerTime, float LoserTime, int Mode);

/// <summary>Results of a <see cref="RaceSession"/>.</summary>
public sealed record RaceResults(string Route, int Laps, double Distance, IReadOnlyList<RaceResultRow> Rows)
{
    /// <summary>The time RaceDialog prints as "--:--:--" (MEASURED, RaceDialog: <c>if (f == 1000.0)</c>).</summary>
    public const float NoTime = 1000.0f;

    public RaceResultRow? Winner => Rows.FirstOrDefault(r => r.Status == EntrantStatus.Finished);

    /// <summary>Dialog values for <paramref name="a"/> against <paramref name="b"/> (the better placed one is the winner).</summary>
    public RaceDialogArgs DialogFor(string a, string b, int mode = 3)
    {
        var ra = Rows.First(r => r.Name == a);
        var rb = Rows.First(r => r.Name == b);
        var (w, l) = ra.Position <= rb.Position ? (ra, rb) : (rb, ra);
        return new RaceDialogArgs(w.Name, l.Name, (float)(w.Time ?? NoTime), (float)(l.Time ?? NoTime), mode);
    }

    /// <summary>"m:ss.xx" like the scripts' <c>String.timeToString(t, TCF_NOHOURS)</c> (format GUESSED from its use).</summary>
    public static string FormatTime(double? t)
    {
        if (t is not { } v || v >= NoTime)
        {
            return "--:--:--";
        }

        var m = (int)(v / 60);
        return string.Create(CultureInfo.InvariantCulture, $"{m}:{v - (m * 60):00.00}");
    }

    public string Format()
    {
        var sb = new StringBuilder();
        sb.Append(CultureInfo.InvariantCulture, $"route {Route}, {Laps} lap(s), {Distance:F0} m\n");
        foreach (var r in Rows)
        {
            var laps = string.Join(" ", r.LapTimes.Select(x => FormatTime(x)));
            sb.Append(CultureInfo.InvariantCulture,
                $"{r.Position,2}. {r.Name,-14} {r.Status,-8} {FormatTime(r.Time),9}  gap {(r.Gap is { } g ? g.ToString("+0.00;-0.00", CultureInfo.InvariantCulture) : "-"),7}  laps [{laps}]  best {FormatTime(r.BestLap)}  recoveries {r.Recoveries}{(r.DnfReason is null ? "" : $"  ({r.DnfReason})")}\n");
        }

        return sb.ToString();
    }
}
