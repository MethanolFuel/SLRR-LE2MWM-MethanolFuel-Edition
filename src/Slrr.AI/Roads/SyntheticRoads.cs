using System.Numerics;

namespace Slrr.AI.Roads;

/// <summary>
/// Synthetic road layouts for tests and the CLI (no game data): a closed race circuit and a separate ring road with
/// directed traffic lanes. Built with a turtle (straights and arcs); a half sequence that turns exactly 180° repeated
/// twice always closes.
/// </summary>
public static class SyntheticRoads
{
    /// <summary>One turtle step: a straight (radius 0) or an arc of <c>Angle</c> degrees (+ = left).</summary>
    public readonly record struct Leg(float Length, float Radius = 0, float Angle = 0)
    {
        public static Leg Straight(float length) => new(length);

        public static Leg Arc(float radius, float degrees) => new(0, radius, degrees);
    }

    /// <summary>Points every <paramref name="step"/> metres along the legs, starting at <paramref name="origin"/> facing
    /// <paramref name="yaw"/> (engine convention, yaw 0 faces -Z).</summary>
    public static List<Vector3> Turtle(IEnumerable<Leg> legs, Vector3 origin, float yaw = 0, float step = 5f)
    {
        var pts = new List<Vector3> { origin };
        var p = origin;
        var h = yaw;
        foreach (var leg in legs)
        {
            var len = leg.Radius > 0 ? leg.Radius * MathF.Abs(leg.Angle) * MathF.PI / 180f : leg.Length;
            var n = Math.Max(1, (int)MathF.Ceiling(len / step));
            var turn = leg.Radius > 0 ? leg.Angle * MathF.PI / 180f / n : 0f;
            var ds = len / n;
            for (var i = 0; i < n; i++)
            {
                // advance along the chord of each small arc piece
                var mid = h + (turn / 2);
                var chord = turn == 0 ? ds : 2 * leg.Radius * MathF.Sin(MathF.Abs(turn) / 2);
                p += new Vector3(-MathF.Sin(mid), 0, -MathF.Cos(mid)) * chord;
                h += turn; // + = left (yaw grows to the left: forward(yaw) = (-sin, 0, -cos))
                pts.Add(p);
            }
        }

        return pts;
    }

    /// <summary>The half lap of <see cref="RaceCircuit"/> (turns +180°): straight, tight left, S-bend, sweeper, kink.</summary>
    public static IReadOnlyList<Leg> CircuitHalf { get; } =
    [
        Leg.Straight(150), Leg.Arc(30, 90), Leg.Straight(60), Leg.Arc(80, -45), Leg.Arc(80, 45), Leg.Straight(80),
        Leg.Arc(150, 60), Leg.Arc(45, 30), Leg.Straight(150),
    ];

    /// <summary>A closed race circuit of ~1.6 km (S = 0 in the middle of a 300 m straight), half width 7 m.</summary>
    public static SampledPath RaceCircuit(Vector3 origin = default, float halfWidth = 7f)
    {
        var legs = CircuitHalf.Concat(CircuitHalf);
        var pts = Turtle(legs, origin);
        pts.RemoveAt(pts.Count - 1); // == origin (closed)
        return new SampledPath(pts.Select(p => new PathControlPoint(p, null, halfWidth)).ToList(), closed: true, "synthetic circuit");
    }

    /// <summary>A rounded-rectangle ring road (600 x 300 m straights, R 40 corners, ~2.06 km) starting at
    /// <paramref name="origin"/>, as directed lanes: <paramref name="lanesPerDirection"/> lanes of 3.5 m on each side,
    /// right-hand traffic. Lanes are closed loops.</summary>
    public static IReadOnlyList<SampledPath> RingLanes(Vector3 origin, int lanesPerDirection = 2, float laneWidth = 3.5f)
    {
        Leg[] half = [Leg.Straight(600), Leg.Arc(40, 90), Leg.Straight(300), Leg.Arc(40, 90)];
        var centre = Turtle(half.Concat(half), origin);
        centre.RemoveAt(centre.Count - 1);
        var ring = new SampledPath(centre.Select(p => new PathControlPoint(p)).ToList(), closed: true, "ring", spacing: 5f);
        var lanes = new List<SampledPath>();
        for (var dir = 0; dir < 2; dir++)
        {
            for (var k = 0; k < lanesPerDirection; k++)
            {
                var off = (k + 0.5f) * laneWidth;
                var pts = new List<PathControlPoint>();
                var n = (int)(ring.Length / 5f);
                for (var i = 0; i < n; i++)
                {
                    var smp = ring.Sample(i * ring.Length / n);
                    var tan = dir == 0 ? smp.Tangent : -smp.Tangent;
                    // right-hand traffic: lanes to the right of their own direction of travel
                    pts.Add(new PathControlPoint(smp.Position - (RoadMath.LeftOf(tan) * off), null, laneWidth / 2));
                }

                if (dir == 1)
                {
                    pts.Reverse();
                }

                lanes.Add(new SampledPath(pts, closed: true, $"ring {(dir == 0 ? "cw" : "ccw")} lane {k}"));
            }
        }

        return lanes;
    }

    /// <summary>The standard synthetic world: the race circuit at the origin and the ring road 900 m to +X.</summary>
    public static PathSetRoadSource World(out SampledPath circuit)
    {
        circuit = RaceCircuit();
        var src = new PathSetRoadSource().AddRoute(circuit);
        foreach (var lane in RingLanes(new Vector3(900, 0, 200)))
        {
            src.AddLane(lane);
        }

        return src;
    }
}
