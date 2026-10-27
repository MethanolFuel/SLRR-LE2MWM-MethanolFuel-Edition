using System.Numerics;

namespace Slrr.AI.Roads;

/// <summary>
/// One point of a lane/route centreline. Frame conventions of the engine (docs/race/commands.md, Geometry.cs): +Y up,
/// a car with yaw 0 faces -Z, its left is -X. <see cref="Curvature"/> is signed: positive when the path turns LEFT
/// (dθ/ds towards <see cref="RoadMath.LeftOf"/> of the tangent), in 1/m.
/// </summary>
/// <param name="Position">Centreline point (world, metres).</param>
/// <param name="Tangent">Unit direction of travel (horizontal component dominates; may carry the slope).</param>
/// <param name="Curvature">Signed horizontal curvature, 1/m (+ = left turn).</param>
/// <param name="HalfWidth">Half the drivable width around the centreline (metres).</param>
/// <param name="SpeedLimit">Speed limit from the road data, m/s (<see cref="float.PositiveInfinity"/> = none).</param>
/// <param name="S">Arc length of this point along its path (metres).</param>
public readonly record struct PathSample(Vector3 Position, Vector3 Tangent, float Curvature, float HalfWidth, float SpeedLimit, float S);

/// <summary>
/// A directed centreline with an arc-length parameter: a lane, a route between two points, a race loop.
/// Implementations must be deterministic and cheap to sample (the AI samples it every 1/120 s step).
/// </summary>
public interface IRoadPath
{
    /// <summary>Total length (metres). For a closed path the point at <see cref="Length"/> equals the point at 0.</summary>
    float Length { get; }

    /// <summary>True for a loop (lap races, ring roads): <see cref="Sample"/> wraps.</summary>
    bool Closed { get; }

    /// <summary>Optional name (map spline file, lane id) for reports.</summary>
    string Name { get; }

    /// <summary>The point at arc length <paramref name="s"/>: wrapped for closed paths, clamped for open ones (beyond the
    /// ends an open path continues along its end tangent, so a car can aim "through" the finish).</summary>
    PathSample Sample(float s);

    /// <summary>Arc length of the centreline point nearest to <paramref name="p"/> (horizontal distance).
    /// With a finite <paramref name="window"/> only <c>[hint - window, hint + window]</c> is searched (continuity tracking).</summary>
    float Project(Vector3 p, float hint = float.NaN, float window = float.PositiveInfinity);
}

/// <summary>Result of <see cref="IRoadSource.Nearest"/>.</summary>
/// <param name="Lateral">Signed horizontal offset of the query point from the centreline (+ = left of the tangent).</param>
public readonly record struct RoadPoint(IRoadPath Path, PathSample Sample, float Distance, float Lateral);

/// <summary>A start grid slot (world pose of the chassis origin, yaw in the engine's Ypr convention).</summary>
public readonly record struct GridSlot(int Index, Vector3 Position, float Yaw, int Row, int Column);

/// <summary>A checkpoint gate across the route at arc length <see cref="S"/> (the last one of an open route is the finish).</summary>
public readonly record struct Checkpoint(int Index, float S, Vector3 Position, Vector3 Normal, float HalfWidth);

/// <summary>
/// What the AI needs from the road network (implemented by the Roads area over the map's road graph; this project ships
/// <see cref="PathSetRoadSource"/> for synthetic roads and shipped <c>.spl</c> splines). Cross-area interface: see
/// docs/gaps/ai.md.
/// </summary>
public interface IRoadSource
{
    /// <summary>Nearest road/lane centreline point within <paramref name="maxDistance"/> (horizontal), or null.</summary>
    RoadPoint? Nearest(Vector3 p, float maxDistance);

    /// <summary>A drivable centreline from (near) <paramref name="from"/> to (near) <paramref name="to"/>, or null when the
    /// source cannot route between them (the AI then falls back to a straight line). Used for <c>AI_race x,y,z</c> and
    /// <c>AI_GoToTarget</c> (MEASURED: the exe's AI_race handler 0x433670 queries a global road object at 0x798a54 for the
    /// car's and the target's position before driving, so it routes on the road network; how is not traced).</summary>
    IRoadPath? Route(Vector3 from, Vector3 to);

    /// <summary>Directed lane centrelines that traffic may use.</summary>
    IReadOnlyList<IRoadPath> Lanes { get; }

    /// <summary>Lanes a car may continue on at the end of <paramref name="lane"/> (empty for closed lanes or dead ends).</summary>
    IReadOnlyList<IRoadPath> Next(IRoadPath lane);

    /// <summary>Checkpoint gates of a race route (default: <see cref="RaceLayout.Checkpoints"/>, GUESSED spacing).</summary>
    IReadOnlyList<Checkpoint> Checkpoints(IRoadPath route) => RaceLayout.Checkpoints(route);

    /// <summary>Start grid for <paramref name="cars"/> cars on <paramref name="route"/> (default: <see cref="RaceLayout.Grid"/>).</summary>
    IReadOnlyList<GridSlot> StartGrid(IRoadPath route, int cars) => RaceLayout.Grid(route, cars);
}

/// <summary>Small vector helpers in the engine's frame.</summary>
public static class RoadMath
{
    /// <summary>Horizontal left of a direction (forward (0,0,-1) → left (-1,0,0)), normalised.</summary>
    public static Vector3 LeftOf(Vector3 t)
    {
        var l = new Vector3(t.Z, 0, -t.X);
        var n = l.Length();
        return n > 1e-6f ? l / n : new Vector3(-1, 0, 0);
    }

    /// <summary>Yaw (engine Ypr, forward(yaw) = (-sin yaw, 0, -cos yaw)) of a direction.</summary>
    public static float YawOf(Vector3 d) => MathF.Atan2(-d.X, -d.Z);

    public static float Horizontal(Vector3 v) => MathF.Sqrt((v.X * v.X) + (v.Z * v.Z));
}

/// <summary>
/// Default race layout rules. MEASURED from the shipped scripts: City.startRace puts two cars side by side 1.75 m left
/// and right of the start point, both facing <c>map.getStartDirection(start, finish)</c>; ROCTrack puts the bot 3.5 m to
/// the side of the player. GUESSED: more than two cars continue in rows of two, 8 m apart, behind the start;
/// checkpoints every <c>spacing</c> metres (ROC rounds use explicit trigger points; the City day race only a finish trigger).
/// </summary>
public static class RaceLayout
{
    /// <summary>Lateral offset of the two grid columns from the centreline (MEASURED, City.startRace: 1.75 m).</summary>
    public const float ColumnOffset = 1.75f;

    /// <summary>Row spacing (GUESSED).</summary>
    public const float RowSpacing = 8f;

    public static IReadOnlyList<GridSlot> Grid(IRoadPath route, int cars, float startS = 0f)
    {
        var slots = new List<GridSlot>(cars);
        for (var i = 0; i < cars; i++)
        {
            var row = i / 2;
            var col = i % 2;
            var back = row * RowSpacing;
            PathSample s;
            Vector3 pos;
            if (route.Closed || startS - back >= 0)
            {
                s = route.Sample(startS - back);
                pos = s.Position;
            }
            else
            {
                // behind the start of an open route: continue backwards along the first tangent
                s = route.Sample(0);
                pos = s.Position - (s.Tangent * (back - startS));
            }

            var side = col == 0 ? ColumnOffset : -ColumnOffset;
            slots.Add(new GridSlot(i, pos + (RoadMath.LeftOf(s.Tangent) * side), RoadMath.YawOf(s.Tangent), row, col));
        }

        return slots;
    }

    /// <summary>Gates every <paramref name="spacing"/> metres; an open route ends with the finish gate at its end, a closed
    /// one with the start/finish line at S = 0 (index = count - 1, S = Length).</summary>
    public static IReadOnlyList<Checkpoint> Checkpoints(IRoadPath route, float spacing = 150f)
    {
        var n = Math.Max(1, (int)MathF.Ceiling(route.Length / spacing));
        var list = new List<Checkpoint>(n);
        for (var i = 1; i <= n; i++)
        {
            var s = i == n ? route.Length : route.Length * i / n;
            var p = route.Sample(route.Closed && i == n ? 0 : s);
            list.Add(new Checkpoint(i - 1, s, p.Position, p.Tangent, p.HalfWidth));
        }

        return list;
    }
}
