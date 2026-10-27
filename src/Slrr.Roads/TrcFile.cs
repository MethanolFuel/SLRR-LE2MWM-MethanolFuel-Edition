using System.Numerics;
using Slrr.Core;

namespace Slrr.Roads;

/// <summary>
/// One cubic Hermite piece of a road/lane/connector curve: 14 floats + 1 byte in the file (57 bytes).
/// Layout MEASURED from the exe loader (0x57d970: p0 → +0, p1 → +0xc, t0 → +0x18, t1 → +0x24, length → +0x30, +0x34, flag → +0x38)
/// and the data: the arc length of <c>H(u) = h00 p0 + h10 t0 + h01 p1 + h11 t1</c> equals <see cref="Length"/> within 0.16 m on
/// all 1,654 road pieces of city.trc; |t0| and |t1| equal the length (tangents are scaled by it); <see cref="Length2"/> equals
/// <see cref="Length"/> in every piece. The exe maps a curve distance s to <c>u = s / Length</c> (0x57cda0, linear, MEASURED).
/// </summary>
public readonly record struct HermitePiece(Vector3 P0, Vector3 P1, Vector3 T0, Vector3 T1, float Length, float Length2, byte Flag)
{
    public const int Size = 57;

    /// <summary>Position at the normalised parameter <paramref name="u"/> (0..1; values outside extrapolate the cubic).</summary>
    public Vector3 Point(float u)
    {
        var u2 = u * u;
        var u3 = u2 * u;
        return ((2 * u3) - (3 * u2) + 1) * P0 + (u3 - (2 * u2) + u) * T0 + ((-2 * u3) + (3 * u2)) * P1 + (u3 - u2) * T1;
    }

    /// <summary>dH/du (not normalised).</summary>
    public Vector3 Derivative(float u)
    {
        var u2 = u * u;
        return ((6 * u2) - (6 * u)) * P0 + ((3 * u2) - (4 * u) + 1) * T0 + ((-6 * u2) + (6 * u)) * P1 + ((3 * u2) - (2 * u)) * T1;
    }

    /// <summary>d²H/du².</summary>
    public Vector3 SecondDerivative(float u) =>
        ((12 * u) - 6) * P0 + ((6 * u) - 4) * T0 + ((-12 * u) + 6) * P1 + ((6 * u) - 2) * T1;

    /// <summary>Numerical arc length (for checks; the file's <see cref="Length"/> is what the exe uses).</summary>
    public float ArcLength(int steps = 200)
    {
        var len = 0f;
        var prev = P0;
        for (var i = 1; i <= steps; i++)
        {
            var q = Point(i / (float)steps);
            len += Vector3.Distance(prev, q);
            prev = q;
        }

        return len;
    }

    internal static HermitePiece Read(ByteReader r) => new(V(r), V(r), V(r), V(r), r.F32(), r.F32(), r.U8());

    internal void Write(ByteWriter w)
    {
        W(w, P0);
        W(w, P1);
        W(w, T0);
        W(w, T1);
        w.F32(Length);
        w.F32(Length2);
        w.U8(Flag);
    }

    internal static Vector3 V(ByteReader r) => new(r.F32(), r.F32(), r.F32());

    internal static void W(ByteWriter w, Vector3 v)
    {
        w.F32(v.X);
        w.F32(v.Y);
        w.F32(v.Z);
    }
}

/// <summary>A lane end at a crossing (node link list entry: u32 lane id + u8). <see cref="Starts"/> = the lane starts here
/// (byte 1), else it ends here (byte 0). MEASURED: every lane id appears in exactly two nodes, once with each value, and the
/// lane's start point is nearer the node with 1 (1572/1572).</summary>
public readonly record struct TrcLink(int LaneId, byte Flag)
{
    public bool Starts => Flag != 0;
}

/// <summary>
/// A turn path through a crossing: 12-byte header + Hermite piece + nothing else (69 bytes). Header MEASURED from the exe
/// loader 0x57ba20 (u8 → +0, u8 → +1, 2 bytes → +2, u32 → +0x58, u32 → +0x5c) and the data: the curve runs from the end of
/// the lane at link <see cref="InLink"/> to the start of the lane at link <see cref="OutLink"/> (3854/3854 exact).
/// <see cref="Code"/> (11..13, 51..53, 101..303) and <see cref="Turn"/> (-1/0/1) meanings are GUESSED: the last digit of the
/// code goes with the turn (x1 ↔ +1, x2 ↔ -1, x3 ↔ 0 in 3,587 of 3,854), hundreds look like a signal phase group.
/// </summary>
public sealed record TrcConnector(byte OutLink, byte InLink, ushort Code, uint Reserved, int Turn, HermitePiece Curve);

/// <summary>A crossing / junction node (ids 1..N). Two tables in the file (header counts 53 and 218 in city.trc) with the same
/// record layout and two exe classes (0x57f360 size 0x60 / 0x57fa70 size 0x78); the difference is not known (GUESSED: plain vs
/// signalled crossings). <see cref="Tail"/> is an unexplained u32 (0 in 268 of 271 nodes).</summary>
public sealed class TrcNode
{
    public required int Id { get; init; }

    public required int Table { get; init; }

    public required List<TrcLink> Links { get; init; }

    public required List<TrcConnector> Connectors { get; init; }

    public required Vector3 Position { get; init; }

    public required uint Tail { get; init; }
}

/// <summary>
/// A road (ids after the nodes): its reference curve and its two crossings. Layout MEASURED from the exe loader 0x57b220
/// (id → +4, id → +8, id → +0xc, 3 floats → +0x10, 3 floats → +0x1c, u32 → +0x64, curve list → +0x28). The curve runs from
/// <see cref="StartPosition"/> (the second vector, = first piece's P0) to <see cref="EndPosition"/> (the first vector, = last
/// piece's P1) in 484/484 roads; <see cref="EndNode"/> is the crossing nearer the end (484/484). <see cref="FirstLaneId"/> is
/// the road's lowest lane id (484/484). <see cref="Flags"/> is 0 everywhere; the exe's route search skips roads whose value
/// is &gt; 0 (0x5827a9, MEASURED branch, meaning GUESSED: closed road).
/// </summary>
public sealed class TrcRoad
{
    public required int Id { get; init; }

    public required int FirstLaneId { get; init; }

    public required int EndNode { get; init; }

    public required int StartNode { get; init; }

    public required Vector3 EndPosition { get; init; }

    public required Vector3 StartPosition { get; init; }

    public required uint Flags { get; init; }

    public required List<HermitePiece> Pieces { get; init; }
}

/// <summary>Entry of the x-sorted lane table that the file stores inside the lane records (fields +0x78..+0x84 of the exe's
/// lane, MEASURED use in 0x581bf0: binary search on <see cref="XMin"/>, early exit on <see cref="RunningXMax"/>, skip on
/// <see cref="XMax"/>, then the lane with index <see cref="LaneIndex"/>). Entry i is NOT about lane i. The stored extents are
/// 4-6 m wider than the curves' control points (margin not derived).</summary>
public readonly record struct TrcSortEntry(float XMin, float RunningXMax, float XMax, int LaneIndex);

/// <summary>
/// A lane (ids after the roads), 85 bytes + its curve list. Layout MEASURED from the exe loader 0x579960. Data checks
/// (1572/1572 each): <see cref="Reverse"/> = 0 exactly when the lane runs the road's direction; <see cref="EndLink"/> /
/// <see cref="StartLink"/> are this lane's index in the end / start crossing's link list; <see cref="NextLaneId"/> chains
/// the lanes of one road (0 = last); <see cref="LeftLaneId"/> / <see cref="RightLaneId"/> are symmetric neighbours in the same
/// direction 3.50 m apart (644 lanes). <see cref="SpeedLimit"/>: 13.889 (1473 lanes) or 27.778 (99) — exactly 50 and
/// 100 km/h in m/s, so a speed limit is the GUESSED meaning. <see cref="Factor"/> = 0.9 and <see cref="Extra"/> = -1 everywhere.
/// </summary>
public sealed class TrcLane
{
    public required int Id { get; init; }

    public required int RoadId { get; init; }

    public required int NextLaneId { get; init; }

    public required byte Reverse { get; init; }

    public required Vector3 EndPosition { get; init; }

    public required Vector3 StartPosition { get; init; }

    public required uint EndLink { get; init; }

    public required uint StartLink { get; init; }

    public required TrcSortEntry Sort { get; init; }

    public required int Ref88 { get; init; }

    public required int Ref8C { get; init; }

    public required int LeftLaneId { get; init; }

    public required int RightLaneId { get; init; }

    public required float SpeedLimit { get; init; }

    public required float Factor { get; init; }

    public required int Extra { get; init; }

    public required List<HermitePiece> Pieces { get; init; }
}

/// <summary>
/// The binary AI road graph (<c>.trc</c>; the only shipped one is maps/city/ai/city.trc, named by the city.rpk root's
/// <c>params ...,maps\city.scm,maps\city\ai\city.trc</c>). Layout MEASURED from the exe loader (0x580910 reads the header and
/// four object tables from a memory image) and checked on the data: the whole 843,301-byte file parses with no bytes left and
/// <see cref="Write"/> reproduces it byte for byte.
/// <code>
/// u32 version (1) | u32 nodesA | u32 nodesB | u32 roads | u32 lanes
/// nodes (A then B): u32 n, n × (u32 laneId, u8 starts) | u32 m, m × connector | f32[3] pos | u32 tail
/// roads:            u32 firstLane, u32 endNode, u32 startNode, f32[3] end, f32[3] start, u32 flags | u32 k, k × piece
/// lanes:            u32 road, u32 nextLane, u8 reverse, f32[3] end, f32[3] start, u32 endLink, u32 startLink,
///                   f32 xmin, f32 runXmax, f32 xmax, u32 laneIndex, u32 ref88, ref8c, left, right, f32 speed, f32 0.9,
///                   i32 -1 | u32 k, k × piece
/// connector:        u8 outLink, u8 inLink, u16 code, u32 0, i32 turn, piece
/// piece:            f32[3] p0, p1, t0, t1, f32 length, f32 length, u8 flag
/// </code>
/// Ids form one space (0 = none): nodes 1..A+B, roads next, lanes last (city: 1..271, 272..755, 756..2327), MEASURED from the
/// exe's id table (0x580a4d).
/// </summary>
public sealed class TrcFile
{
    public uint Version { get; init; } = 1;

    public int NodeTableA { get; init; }

    public List<TrcNode> Nodes { get; } = [];

    public List<TrcRoad> Roads { get; } = [];

    public List<TrcLane> Lanes { get; } = [];

    public int FirstRoadId => Nodes.Count + 1;

    public int FirstLaneId => Nodes.Count + Roads.Count + 1;

    public TrcNode? Node(int id) => id >= 1 && id <= Nodes.Count ? Nodes[id - 1] : null;

    public TrcRoad? Road(int id) => id >= FirstRoadId && id < FirstLaneId ? Roads[id - FirstRoadId] : null;

    public TrcLane? Lane(int id) => id >= FirstLaneId && id < FirstLaneId + Lanes.Count ? Lanes[id - FirstLaneId] : null;

    public static TrcFile Read(string path) => Read(File.ReadAllBytes(path));

    public static TrcFile Read(ReadOnlyMemory<byte> data)
    {
        var r = new ByteReader(data);
        var version = r.U32();
        var a = r.CheckedCount(r.U32(), 9, "trc node table A");
        var b = r.CheckedCount(r.U32(), 9, "trc node table B");
        var roads = r.CheckedCount(r.U32(), 40, "trc roads");
        var lanes = r.CheckedCount(r.U32(), 89, "trc lanes");
        var f = new TrcFile { Version = version, NodeTableA = a };
        for (var i = 0; i < a + b; i++)
        {
            var nl = r.CheckedCount(r.U32(), 5, "trc node links");
            var links = new List<TrcLink>(nl);
            for (var k = 0; k < nl; k++)
            {
                links.Add(new TrcLink(r.I32(), r.U8()));
            }

            var nc = r.CheckedCount(r.U32(), 12 + HermitePiece.Size, "trc connectors");
            var conns = new List<TrcConnector>(nc);
            for (var k = 0; k < nc; k++)
            {
                conns.Add(new TrcConnector(r.U8(), r.U8(), r.U16(), r.U32(), r.I32(), HermitePiece.Read(r)));
            }

            f.Nodes.Add(new TrcNode { Id = i + 1, Table = i < a ? 0 : 1, Links = links, Connectors = conns, Position = HermitePiece.V(r), Tail = r.U32() });
        }

        for (var i = 0; i < roads; i++)
        {
            f.Roads.Add(new TrcRoad
            {
                Id = f.Nodes.Count + 1 + i,
                FirstLaneId = r.I32(),
                EndNode = r.I32(),
                StartNode = r.I32(),
                EndPosition = HermitePiece.V(r),
                StartPosition = HermitePiece.V(r),
                Flags = r.U32(),
                Pieces = ReadPieces(r),
            });
        }

        for (var i = 0; i < lanes; i++)
        {
            f.Lanes.Add(new TrcLane
            {
                Id = f.Nodes.Count + roads + 1 + i,
                RoadId = r.I32(),
                NextLaneId = r.I32(),
                Reverse = r.U8(),
                EndPosition = HermitePiece.V(r),
                StartPosition = HermitePiece.V(r),
                EndLink = r.U32(),
                StartLink = r.U32(),
                Sort = new TrcSortEntry(r.F32(), r.F32(), r.F32(), r.I32()),
                Ref88 = r.I32(),
                Ref8C = r.I32(),
                LeftLaneId = r.I32(),
                RightLaneId = r.I32(),
                SpeedLimit = r.F32(),
                Factor = r.F32(),
                Extra = r.I32(),
                Pieces = ReadPieces(r),
            });
        }

        if (r.Remaining != 0)
        {
            throw new SlrrFormatException($"trc: {r.Remaining} trailing bytes");
        }

        return f;
    }

    private static List<HermitePiece> ReadPieces(ByteReader r)
    {
        var n = r.CheckedCount(r.U32(), HermitePiece.Size, "trc pieces");
        var list = new List<HermitePiece>(n);
        for (var i = 0; i < n; i++)
        {
            list.Add(HermitePiece.Read(r));
        }

        return list;
    }

    public byte[] Write()
    {
        var w = new ByteWriter();
        w.U32(Version);
        w.U32((uint)NodeTableA);
        w.U32((uint)(Nodes.Count - NodeTableA));
        w.U32((uint)Roads.Count);
        w.U32((uint)Lanes.Count);
        foreach (var n in Nodes)
        {
            w.U32((uint)n.Links.Count);
            foreach (var l in n.Links)
            {
                w.I32(l.LaneId);
                w.U8(l.Flag);
            }

            w.U32((uint)n.Connectors.Count);
            foreach (var c in n.Connectors)
            {
                w.U8(c.OutLink);
                w.U8(c.InLink);
                w.U16(c.Code);
                w.U32(c.Reserved);
                w.I32(c.Turn);
                c.Curve.Write(w);
            }

            HermitePiece.W(w, n.Position);
            w.U32(n.Tail);
        }

        foreach (var d in Roads)
        {
            w.I32(d.FirstLaneId);
            w.I32(d.EndNode);
            w.I32(d.StartNode);
            HermitePiece.W(w, d.EndPosition);
            HermitePiece.W(w, d.StartPosition);
            w.U32(d.Flags);
            WritePieces(w, d.Pieces);
        }

        foreach (var l in Lanes)
        {
            w.I32(l.RoadId);
            w.I32(l.NextLaneId);
            w.U8(l.Reverse);
            HermitePiece.W(w, l.EndPosition);
            HermitePiece.W(w, l.StartPosition);
            w.U32(l.EndLink);
            w.U32(l.StartLink);
            w.F32(l.Sort.XMin);
            w.F32(l.Sort.RunningXMax);
            w.F32(l.Sort.XMax);
            w.I32(l.Sort.LaneIndex);
            w.I32(l.Ref88);
            w.I32(l.Ref8C);
            w.I32(l.LeftLaneId);
            w.I32(l.RightLaneId);
            w.F32(l.SpeedLimit);
            w.F32(l.Factor);
            w.I32(l.Extra);
            WritePieces(w, l.Pieces);
        }

        return w.ToArray();
    }

    private static void WritePieces(ByteWriter w, List<HermitePiece> pieces)
    {
        w.U32((uint)pieces.Count);
        foreach (var p in pieces)
        {
            p.Write(w);
        }
    }
}
