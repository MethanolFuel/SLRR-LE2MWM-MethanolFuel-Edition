using System.Text;
using Slrr.Core;
using Slrr.Formats.Rpak;

namespace Slrr.Formats.Instances;

/// <summary>One <c>tag(4) | u32 length | payload</c> block at the end of an instance node.</summary>
public sealed class InstanceChunk
{
    public const uint TagPhys = 0x53594850; // "PHYS"
    public const uint TagPoly = 0x594C4F50; // "POLY"
    public const uint TagRsd = 0x00445352;  // "RSD\0"
    public const uint TagExtp = 0x50545845; // "EXTP"

    public InstanceChunk(uint tag, byte[] payload)
    {
        Tag = tag;
        Payload = payload;
    }

    public uint Tag { get; }

    public byte[] Payload { get; set; }

    public string TagText => Encoding.Latin1.GetString(BitConverter.GetBytes(Tag)).TrimEnd('\0');

    public int SerializedSize => 8 + Payload.Length;
}

/// <summary>Axis-aligned box stored in the first 24 bytes of a TypeOfEntry 1 record's 48-byte extra block.</summary>
/// <remarks>Measured: every PHYS/POLY vertex of a node lies inside <c>Center ± HalfExtent</c>, so the six floats are
/// center and half extents, not min/max.</remarks>
public readonly record struct InstanceBox(float CenterX, float CenterY, float CenterZ, float HalfX, float HalfY, float HalfZ)
{
    public static InstanceBox FromExtra(byte[]? extra)
    {
        if (extra is not { Length: >= 24 })
        {
            return default;
        }

        var f = new float[6];
        for (var i = 0; i < 6; i++)
        {
            f[i] = BitConverter.ToSingle(extra, i * 4);
        }

        return new InstanceBox(f[0], f[1], f[2], f[3], f[4], f[5]);
    }
}

/// <summary>
/// One node of an RPAK instance tree (TypeOfEntry 1, <c>ins_game</c> in system.rpk). Layout at <see cref="RpakEntry.Offset"/>:
/// <c>u32 header (0 in all shipped files) | u32 childCount | childCount entry records (RPAK entry-table format) | chunks to node end</c>.
/// </summary>
public sealed class InstanceNode
{
    /// <summary>Additional-type bit seen on placed object instances (RSD with gametype + placement).</summary>
    public const byte ObjectFlag = 0x20;

    /// <summary>Additional-type bit seen on the archive-level root of a map (RSD with the map's gametype).</summary>
    public const byte RootFlag = 0x10;

    public InstanceNode(RpakEntry record)
    {
        Record = record;
    }

    /// <summary>The record that points at this node: an archive table row for roots, else the parent's child record.
    /// Its <see cref="RpakEntry.Offset"/>/<see cref="RpakEntry.Size"/> are rewritten by <see cref="InstanceLayout"/>.</summary>
    public RpakEntry Record { get; }

    public uint Header { get; set; }

    public List<InstanceNode> Children { get; } = [];

    public List<InstanceChunk> Chunks { get; } = [];

    /// <summary>Raw bytes for a child record whose TypeOfEntry is not 1 (none in shipped files); such children are opaque.</summary>
    public byte[]? OpaqueData { get; set; }

    /// <summary>Absolute file offset the node was read from, or -1 for a node created in memory.</summary>
    public int OriginalOffset { get; set; } = -1;

    public int Depth { get; set; }

    public bool IsNode => Record.TypeOfEntry == RpakArchive.ExtraEntryType;

    public bool IsObject => (Record.AdditionalType & ObjectFlag) != 0;

    public InstanceBox Box => InstanceBox.FromExtra(Record.Extra);

    public InstanceChunk? FindChunk(uint tag) => Chunks.Find(c => c.Tag == tag);

    public RsdText? Rsd => FindChunk(InstanceChunk.TagRsd) is { } c ? new RsdText(c.Payload) : null;

    /// <summary>Bytes this node occupies (its own block; children live in separate blocks).</summary>
    public int SerializedSize
    {
        get
        {
            if (!IsNode)
            {
                return OpaqueData?.Length ?? 0;
            }

            return 8 + Children.Sum(c => c.Record.TableSize) + Chunks.Sum(c => c.SerializedSize);
        }
    }

    public IEnumerable<InstanceNode> DescendantsAndSelf()
    {
        var stack = new Stack<InstanceNode>();
        stack.Push(this);
        while (stack.Count > 0)
        {
            var n = stack.Pop();
            yield return n;
            for (var i = n.Children.Count - 1; i >= 0; i--)
            {
                stack.Push(n.Children[i]);
            }
        }
    }

    /// <summary>Serializes this node's own block using the children's current record offsets/sizes.</summary>
    public void WriteTo(ByteWriter w)
    {
        if (!IsNode)
        {
            w.Bytes(OpaqueData ?? []);
            return;
        }

        w.U32(Header);
        w.U32((uint)Children.Count);
        foreach (var c in Children)
        {
            WriteRecord(w, c.Record);
        }

        foreach (var chunk in Chunks)
        {
            w.U32(chunk.Tag);
            w.U32((uint)chunk.Payload.Length);
            w.Bytes(chunk.Payload);
        }
    }

    internal static RpakEntry ReadRecord(ByteReader r)
    {
        var e = new RpakEntry
        {
            SuperId = r.U32(),
            Id = r.U32(),
            TypeOfEntry = r.U8(),
            AdditionalType = r.U8(),
            ParentCompatibleBits = r.U32(),
            Offset = r.U32(),
            Size = r.U32(),
        };
        e.AliasBytes = r.Bytes(r.U8());
        if (e.HasExtra)
        {
            e.Extra = r.Bytes(RpakArchive.ExtraSize);
        }

        return e;
    }

    internal static void WriteRecord(ByteWriter w, RpakEntry e)
    {
        if (e.AliasBytes.Length > 255)
        {
            throw new InvalidOperationException($"alias of instance record 0x{e.Id:X8} is longer than 255 bytes");
        }

        w.U32(e.SuperId);
        w.U32(e.Id);
        w.U8(e.TypeOfEntry);
        w.U8(e.AdditionalType);
        w.U32(e.ParentCompatibleBits);
        w.U32(e.Offset);
        w.U32(e.Size);
        w.U8((byte)e.AliasBytes.Length);
        w.Bytes(e.AliasBytes);
        if (e.HasExtra)
        {
            if (e.Extra is not { Length: RpakArchive.ExtraSize })
            {
                throw new InvalidOperationException($"instance record 0x{e.Id:X8} needs exactly {RpakArchive.ExtraSize} extra bytes");
            }

            w.Bytes(e.Extra);
        }
    }
}

/// <summary>All instance trees of one archive: one root per TypeOfEntry 1 row of the entry table.</summary>
public sealed class InstanceForest
{
    public const int MaxDepth = 256;

    private InstanceForest(RpakArchive archive)
    {
        Archive = archive;
    }

    public RpakArchive Archive { get; }

    public List<InstanceNode> Roots { get; } = [];

    public IEnumerable<InstanceNode> AllNodes() => Roots.SelectMany(r => r.DescendantsAndSelf());

    /// <summary>Wraps in-memory trees; each root's <see cref="InstanceNode.Record"/> must be a TypeOfEntry 1 row of
    /// <paramref name="archive"/>'s entry table. Lay them out with <see cref="InstanceLayout.Write"/>.</summary>
    public static InstanceForest FromRoots(RpakArchive archive, params InstanceNode[] roots)
    {
        var forest = new InstanceForest(archive);
        foreach (var r in roots)
        {
            if (!archive.Entries.Contains(r.Record) || !r.IsNode)
            {
                throw new InvalidOperationException($"root 0x{r.Record.Id:X8} must be a TypeOfEntry 1 row of the archive");
            }

            forest.Roots.Add(r);
        }

        return forest;
    }

    /// <summary>
    /// Parses every tree reachable from the archive's TypeOfEntry 1 rows. Offsets are validated against the data
    /// region; a node referenced twice (shared or cyclic) is rejected because relayout could not preserve it.
    /// </summary>
    public static InstanceForest Read(RpakArchive archive)
    {
        var forest = new InstanceForest(archive);
        var seen = new HashSet<uint>();
        foreach (var e in archive.Entries.Where(e => e.HasExtra))
        {
            forest.Roots.Add(ReadNode(archive, e, 0, seen));
        }

        return forest;
    }

    private static InstanceNode ReadNode(RpakArchive archive, RpakEntry record, int depth, HashSet<uint> seen)
    {
        if (depth > MaxDepth)
        {
            throw new SlrrFormatException($"instance tree deeper than {MaxDepth} at record 0x{record.Id:X8}");
        }

        var node = new InstanceNode(record) { Depth = depth, OriginalOffset = (int)record.Offset };
        if (record.Size == 0)
        {
            if (record.HasExtra)
            {
                throw new SlrrFormatException($"instance node 0x{record.Id:X8} has size 0");
            }

            node.OpaqueData = [];
            return node;
        }

        if (!seen.Add(record.Offset))
        {
            throw new SlrrFormatException($"instance data at offset {record.Offset} is referenced twice");
        }

        var data = archive.GetData(record);
        if (!record.HasExtra)
        {
            node.OpaqueData = data.ToArray();
            return node;
        }

        var r = new ByteReader(data);
        node.Header = r.U32();
        var count = r.CheckedCount(r.U32(), 23, "instance child");
        for (var i = 0; i < count; i++)
        {
            node.Children.Add(new InstanceNode(InstanceNode.ReadRecord(r)));
        }

        while (r.Remaining > 0)
        {
            if (r.Remaining < 8)
            {
                throw new SlrrFormatException($"instance node at {record.Offset} ends with {r.Remaining} stray bytes");
            }

            var tag = r.U32();
            var len = r.U32();
            if (len > (uint)r.Remaining)
            {
                throw new SlrrFormatException($"chunk of {len} bytes at {record.Offset + r.Position - 8} overruns its node");
            }

            node.Chunks.Add(new InstanceChunk(tag, r.Bytes((int)len)));
        }

        for (var i = 0; i < node.Children.Count; i++)
        {
            node.Children[i] = ReadNode(archive, node.Children[i].Record, depth + 1, seen);
        }

        return node;
    }
}
