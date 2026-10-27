using System.Globalization;
using System.Numerics;

namespace Slrr.AI.Roads;

/// <summary>One line of an AI spline file.</summary>
/// <param name="Columns">Number of values read on the line (the exe needs at least 6).</param>
public sealed record SplPoint(Vector3 Position, Vector3 Direction, double C7, double C8, int C9, double C10, double C11, int C12, int Columns);

/// <summary>
/// AI spline files (<c>cars/racers/ROC_cars/*.spl</c>, <c>maps/ROC_track/race.spl</c>, <c>maps/test_track/test.spl</c>),
/// read by the bot brain's <c>AI_spline f file d</c> verb (ROCTrack: <c>followSplineTrack(0.5, roc.getCurrentSplineName(),
/// player.car.id())</c>).
/// <list type="bullet">
/// <item>MEASURED (exe 0x4337e0): each line is scanned with <c>"%lf %lf %lf %lf %lf %lf %lf %lf %d %lf %lf %d"</c>;
/// lines with fewer than 6 values are skipped; before the scan the defaults 1.0 and 10000.0 (twice) are stored; a 7th value
/// below 0 is replaced by the verb's float (plus a brain field at +0x2594); one spline segment is added per consecutive pair
/// of lines (0x57de20, given both positions and both directions), then the brain enters mode 3 and sends the car
/// <c>transmission 0</c>.</item>
/// <item>MEASURED (shipped files): 7 ROC car splines of 57 lines × 12 columns, maps/ROC_track/race.spl 52 × 9; columns 4-6
/// are (near) unit vectors along the direction of travel (|d| 0.988..1.022); column 11 of a line equals column 10 of the
/// previous line on 45..47 of the 56 transitions; columns 10/11 hold 10000 or 105..200 or 2000..3000.</item>
/// <item>GUESSED: columns 10/11 as speed limits in km/h (plausible for 105..200, not for 2000..3000), so
/// <see cref="ToPath"/> ignores them unless asked; C7 (2.5..11), C8 (1.1..100), C9/C12 (0/1 flags) unknown.</item>
/// </list>
/// </summary>
public static class SplFile
{
    /// <summary>Speed limit value meaning "no limit" (MEASURED default 10000.0).</summary>
    public const double NoLimit = 10000.0;

    private static readonly char[] Separators = [' ', '\t', '\r'];

    public static IReadOnlyList<SplPoint> Parse(string text)
    {
        var list = new List<SplPoint>();
        foreach (var raw in text.Split('\n'))
        {
            var tok = raw.Split(Separators,StringSplitOptions.RemoveEmptyEntries);
            var d = new double[12];
            d[7] = 1.0;
            d[9] = NoLimit;
            d[10] = NoLimit;
            var n = 0;
            for (; n < Math.Min(12, tok.Length); n++)
            {
                if (!double.TryParse(tok[n], NumberStyles.Float, CultureInfo.InvariantCulture, out d[n]))
                {
                    break;
                }
            }

            if (n < 6)
            {
                continue;
            }

            list.Add(new SplPoint(new Vector3((float)d[0], (float)d[1], (float)d[2]), new Vector3((float)d[3], (float)d[4], (float)d[5]),
                d[6], d[7], (int)d[8], d[9], d[10], (int)d[11], n));
        }

        return list;
    }

    public static IReadOnlyList<SplPoint> Load(string path) => Parse(File.ReadAllText(path));

    /// <summary>
    /// The spline as a drivable path (open: the ROC splines run from start to finish). Half width GUESSED (6 m);
    /// speed limits from column 10 when <paramref name="useSpeedLimits"/> (GUESSED km/h, off by default);
    /// <paramref name="tangentScale"/> scales the Hermite tangents (chord length × scale; see <see cref="Race.MapRace"/>).
    /// </summary>
    public static SampledPath ToPath(IReadOnlyList<SplPoint> points, string name, bool useSpeedLimits = false, float halfWidth = 6f, bool closed = false, float tangentScale = 1f)
    {
        var cps = points.Select(p => new PathControlPoint(p.Position, p.Direction, halfWidth,
            useSpeedLimits && p.C10 > 0 && p.C10 < NoLimit ? (float)(p.C10 / 3.6) : float.PositiveInfinity)).ToList();
        return new SampledPath(cps, closed, name, tangentScale: tangentScale);
    }
}
