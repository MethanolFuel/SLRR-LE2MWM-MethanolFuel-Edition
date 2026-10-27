using System.Globalization;
using System.Numerics;

namespace Slrr.Roads;

/// <summary>One line of a <c>.spl</c> file: position, a direction vector, and the remaining numbers as written.</summary>
public sealed record SplinePoint(Vector3 Position, Vector3 Direction, float[] Values);

/// <summary>
/// AI race lines (<c>.spl</c>, whitespace-separated text, one point per line). Shipped: maps/ROC_track/race.spl (52 points,
/// 9 numbers, closed loop of 4.2 km), maps/test_track/test.spl (2 points, 8 numbers, a 4 km straight) and
/// cars/racers/ROC_cars/ROC_*.spl (7 files x 57 points, 12 numbers, closed loops of 4.2 km). Used by <c>Bot.followSplineTrack</c>
/// → brain command <c>AI_spline &lt;f&gt; &lt;file&gt; &lt;id&gt;</c> with <c>GameLogic.carSaveDir + "roc_XX.spl"</c> (MEASURED from the
/// decompiled Bot/Roc/ROCTrack classes; note carSaveDir is save/cars/ while the shipped files are in cars/racers/ROC_cars/).
/// Column meanings are GUESSED: x y z; a unit vector (mean dot with the chord to the next point 0.95 in the race lines, -1 in
/// test.spl); then per-point numbers (race.spl: 3..11, 0.8..100, 0/1; ROC: 2.5..11, 9..100, 0/1, 95..20000 twice, 0/1).
/// The exe's AI_spline parser was not traced. The path between points is a Catmull-Rom curve through the positions (GUESSED).
/// </summary>
public sealed class SplineFile : IRoadPath
{
    private static readonly char[] Separators = [' ', '\t', '\r'];

    private readonly RoadCurve _curve;

    private SplineFile(List<SplinePoint> points)
    {
        Points = points;
        var pieces = new List<HermitePiece>();
        for (var i = 0; i + 1 < points.Count; i++)
        {
            var p0 = points[i].Position;
            var p1 = points[i + 1].Position;
            var prev = i > 0 ? points[i - 1].Position : p0;
            var next = i + 2 < points.Count ? points[i + 2].Position : p1;
            var t0 = (p1 - prev) * (i > 0 ? 0.5f : 1f);
            var t1 = (next - p0) * (i + 2 < points.Count ? 0.5f : 1f);
            var piece = new HermitePiece(p0, p1, t0, t1, 0, 0, 1);
            var len = piece.ArcLength(64);
            pieces.Add(piece with { Length = len, Length2 = len });
        }

        _curve = new RoadCurve(pieces);
    }

    public IReadOnlyList<SplinePoint> Points { get; }

    public float Length => _curve.Length;

    public RoadCurve Curve => _curve;

    public static SplineFile Read(string path) => Parse(File.ReadAllText(path));

    public static SplineFile Parse(string text)
    {
        var points = new List<SplinePoint>();
        foreach (var raw in text.Split('\n'))
        {
            var parts = raw.Split(Separators, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 6)
            {
                continue;
            }

            var nums = new float[parts.Length];
            var ok = true;
            for (var i = 0; i < parts.Length && ok; i++)
            {
                ok = float.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out nums[i]);
            }

            if (ok)
            {
                points.Add(new SplinePoint(new Vector3(nums[0], nums[1], nums[2]), new Vector3(nums[3], nums[4], nums[5]), nums[6..]));
            }
        }

        return new SplineFile(points);
    }

    public PathSample Sample(float s) => _curve.Sample(s);

    public bool Project(Vector3 p, out float s, out float lateral) => _curve.Project(p, out s, out lateral);
}
