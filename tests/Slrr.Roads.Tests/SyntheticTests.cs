using System.Numerics;
using Slrr.Roads;
using Xunit;
using static Slrr.Roads.Tests.Approx;

namespace Slrr.Roads.Tests;

internal static class Approx
{
    public static void Near(float expected, float actual, float tol) => Xunit.Assert.True(MathF.Abs(expected - actual) <= tol, $"expected {expected} ± {tol}, got {actual}");
}

/// <summary>A two-crossing loop: road 3 from crossing 1 (x=0) to crossing 2 (x=100), lane 4 along +X (z=-1.75), lane 5 back
/// (z=+1.75), a U-turn path at each crossing.</summary>
internal static class TinyGraph
{
    public static HermitePiece Straight(Vector3 a, Vector3 b)
    {
        var t = b - a;
        return new HermitePiece(a, b, t, t, t.Length(), t.Length(), 1);
    }

    public static HermitePiece UTurn(Vector3 a, Vector3 b, Vector3 outDir)
    {
        var p = new HermitePiece(a, b, outDir * 6, -outDir * 6, 0, 0, 1);
        var len = p.ArcLength();
        return p with { Length = len, Length2 = len };
    }

    public static TrcFile Build()
    {
        var f = new TrcFile { Version = 1, NodeTableA = 1 };
        var l4a = new Vector3(5, 0, -1.75f);
        var l4b = new Vector3(95, 0, -1.75f);
        var l5a = new Vector3(95, 0, 1.75f);
        var l5b = new Vector3(5, 0, 1.75f);
        f.Nodes.Add(new TrcNode
        {
            Id = 1, Table = 0, Position = Vector3.Zero, Tail = 0,
            Links = [new TrcLink(5, 0), new TrcLink(4, 1)],
            Connectors = [new TrcConnector(1, 0, 13, 0, 0, UTurn(l5b, l4a, -Vector3.UnitX))],
        });
        f.Nodes.Add(new TrcNode
        {
            Id = 2, Table = 1, Position = new Vector3(100, 0, 0), Tail = 0,
            Links = [new TrcLink(4, 0), new TrcLink(5, 1)],
            Connectors = [new TrcConnector(1, 0, 13, 0, 0, UTurn(l4b, l5a, Vector3.UnitX))],
        });
        f.Roads.Add(new TrcRoad
        {
            Id = 3, FirstLaneId = 4, EndNode = 2, StartNode = 1, EndPosition = new Vector3(95, 0, 0), StartPosition = new Vector3(5, 0, 0), Flags = 0,
            Pieces = [Straight(new Vector3(5, 0, 0), new Vector3(50, 0, 0)), Straight(new Vector3(50, 0, 0), new Vector3(95, 0, 0))],
        });
        TrcLane Lane(int id, int next, byte rev, Vector3 a, Vector3 b, uint endLink, uint startLink) => new()
        {
            Id = id, RoadId = 3, NextLaneId = next, Reverse = rev, EndPosition = b, StartPosition = a, EndLink = endLink, StartLink = startLink,
            Sort = new TrcSortEntry(0, 100, 100, id - 4), Ref88 = 0, Ref8C = 0, LeftLaneId = 0, RightLaneId = 0, SpeedLimit = 13.888889f, Factor = 0.9f,
            Extra = -1, Pieces = [Straight(a, b)],
        };
        f.Lanes.Add(Lane(4, 5, 0, l4a, l4b, 0, 1));
        f.Lanes.Add(Lane(5, 0, 1, l5a, l5b, 0, 1));
        return f;
    }
}

public class SyntheticTests
{
    [Fact]
    public void HermiteStraightPieceIsLinear()
    {
        var p = TinyGraph.Straight(new Vector3(0, 0, 0), new Vector3(10, 0, 0));
        Near(5f, p.Point(0.5f).X, 1e-4f);
        Near(10f, p.ArcLength(), 1e-3f);
        var c = new RoadCurve([p]);
        var s = c.Sample(2.5f);
        Assert.Equal(new Vector3(1, 0, 0), s.Tangent);
        Near(0f, s.Curvature, 1e-5f);
        Near(2.5f, s.Position.X, 1e-4f);
    }

    [Fact]
    public void CurveFollowsTheExeDistanceMapping()
    {
        var c = new RoadCurve([TinyGraph.Straight(Vector3.Zero, new Vector3(10, 0, 0)), TinyGraph.Straight(new Vector3(10, 0, 0), new Vector3(10, 0, 20))]);
        Assert.Equal(30f, c.Length);
        Assert.Equal(new Vector3(10, 0, 5), c.PointAt(15));
        Assert.Equal(new Vector3(0, 0, 1), c.TangentAt(15));
        Assert.Equal(c.PointAt(29), c.PointAt(-1)); // negative distances wrap (0x57e960)
        Assert.True(c.Project(11, 7, out var s, out var lat));
        Near(17f, s, 1e-3f);
        Near(1f, lat, 1e-3f); // +X side when heading +Z
    }

    [Fact]
    public void CurvatureSignAndMagnitudeOnAnArc()
    {
        // quarter circle radius 10 from (0,0,0) heading +Z, bending towards +X (Hermite approximation)
        const float k = 15.708f; // r * pi / 2
        var p = new HermitePiece(Vector3.Zero, new Vector3(10, 0, 10), new Vector3(0, 0, k), new Vector3(k, 0, 0), k, k, 1);
        var c = new RoadCurve([p]);
        var mid = c.CurvatureAt(c.Length / 2);
        Assert.True(mid > 0.08f && mid < 0.12f, $"curvature {mid}");
    }

    [Fact]
    public void WriteReadRoundTripIsByteIdentical()
    {
        var f = TinyGraph.Build();
        var bytes = f.Write();
        var g = TrcFile.Read(bytes);
        Assert.Equal(bytes, g.Write());
        Assert.Equal(2, g.Nodes.Count);
        Assert.Equal(1, g.NodeTableA);
        Assert.Equal(3, g.FirstRoadId);
        Assert.Equal(4, g.FirstLaneId);
        Assert.Equal(5, g.Lane(5)!.Id);
        Assert.Throws<Slrr.Core.SlrrFormatException>(() => TrcFile.Read(bytes.AsMemory(0, bytes.Length - 3)));
        var big = bytes.ToArray();
        big[4] = 0xFF;
        big[5] = 0xFF;
        Assert.Throws<Slrr.Core.SlrrFormatException>(() => TrcFile.Read(big));
    }

    [Fact]
    public void GraphLinksLanesCrossingsAndTurnPaths()
    {
        var g = new RoadGraph(TinyGraph.Build());
        var l4 = g.LaneById(4)!;
        Assert.Same(g.Nodes[0], l4.StartNode);
        Assert.Same(g.Nodes[1], l4.EndNode);
        Assert.Single(l4.Exits);
        Assert.Equal(5, l4.Exits[0].To.Id);
        Assert.True(g.LaneById(5)!.Reverse);
        Assert.Equal(2, g.Roads[0].Lanes.Count);
    }

    [Fact]
    public void NearestLaneUsesTheHeightFilter()
    {
        var g = new RoadGraph(TinyGraph.Build());
        var hit = g.NearestLane(new Vector3(50, 0, -1));
        Assert.NotNull(hit);
        Assert.Equal(4, hit.Value.Lane.Id);
        Near(45f, hit.Value.Distance, 1e-2f);
        Assert.Null(g.NearestLane(new Vector3(50, 6, -1)));
        Assert.NotNull(g.NearestLane(new Vector3(50, 4.5f, -1)));
        Assert.Null(g.NearestLane(new Vector3(200, 0, 0))); // outside the 20 m x window
    }

    [Fact]
    public void AlignToRoadSnapsToTheRoadCurveAndFacesTheFartherEnd()
    {
        var g = new RoadGraph(TinyGraph.Build());
        var a = g.AlignToRoad(new Vector3(30, 0, -1))!.Value;
        Near(30f, a.Position.X, 1e-3f);
        Near(0f, a.Position.Z, 1e-4f);
        Assert.Equal(Vector3.UnitX, a.Direction);
        var b = g.AlignToRoad(new Vector3(70, 0, 1.5f))!.Value;
        Near(70f, b.Position.X, 1e-3f);
        Assert.Equal(-Vector3.UnitX, b.Direction);
    }

    [Fact]
    public void NearestCrossMatchesTheRequestedDistance()
    {
        var g = new RoadGraph(TinyGraph.Build());
        Assert.Equal(1, g.NearestCross(Vector3.Zero, 0)!.Id);
        Assert.Equal(2, g.NearestCross(Vector3.Zero, 90)!.Id);
        Assert.Equal(2, g.NearestCross(new Vector3(0, 10, 0), 95, sameLevel: false)!.Id);
        Assert.Null(g.NearestCross(new Vector3(0, 10, 0), 0, sameLevel: true));
    }

    [Fact]
    public void RoutesFollowLanesAndTurnPaths()
    {
        var g = new RoadGraph(TinyGraph.Build());
        var same = g.FindRoute(new Vector3(20, 0, -1.75f), new Vector3(80, 0, -1.75f))!;
        Near(60f, same.Length, 1e-2f);
        Assert.Equal(Vector3.UnitX, g.StartDirection(new Vector3(20, 0, -1.75f), new Vector3(80, 0, -1.75f)));

        var uturn = g.LaneById(4)!.Exits[0].Curve.Length;
        // without a heading the start may be either lane of the two-way road: turn on the spot onto lane 5
        Near(60f, g.FindRoute(new Vector3(80, 0, -1.75f), new Vector3(20, 0, 1.75f))!.Length, 1e-2f);
        var back = g.FindRoute(new Vector3(80, 0, -1.75f), new Vector3(20, 0, 1.75f), Vector3.UnitX)!;
        Near(15f + uturn + 75f, back.Length, 1e-2f);
        Assert.Equal([4, 5], back.LanesOnRoute.Select(l => l.Id));
        Assert.Equal(new Vector3(95, 0, -1.75f), back.Sample(15).Position);
        var end = back.Sample(back.Length);
        Near(20f, end.Position.X, 1e-2f);
        Near(-1f, end.Tangent.X, 1e-4f);
        Assert.True(back.Project(new Vector3(50, 0, 2), out var s, out _));
        Near(15f + uturn + 45f, s, 1e-1f);

        // a goal behind on the same lane: the goal may be on either lane of its road, so one U-turn suffices
        var loop = g.FindRoute(new Vector3(80, 0, -1.75f), new Vector3(20, 0, -1.75f), Vector3.UnitX)!;
        Near(15f + uturn + 75f, loop.Length, 1e-1f);
        Assert.Equal(-1f, g.RouteLength(new Vector3(500, 0, 0), Vector3.Zero));
    }

    [Fact]
    public void SplineTextParses()
    {
        var s = SplineFile.Parse("0 0 0 0 0 1\t5.0\t1.35\t0\r\n0 0 10 0 0 1\t5.0\t1.35\t0\r\n10 0 20 1 0 0\t6\t100\t1\r\nbad line\n");
        Assert.Equal(3, s.Points.Count);
        Assert.Equal(3, s.Points[2].Values.Length);
        Assert.Equal(100f, s.Points[2].Values[1]);
        Assert.True(s.Length > 10 + 14);
        Assert.True(Vector3.Distance(new Vector3(10, 0, 20), s.Sample(s.Length).Position) < 1e-3f);
    }
}
