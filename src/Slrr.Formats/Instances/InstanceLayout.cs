using Slrr.Core;
using Slrr.Formats.Rpak;

namespace Slrr.Formats.Instances;

/// <summary>
/// Rebuilds the data region of an archive that contains instance trees, recomputing every absolute offset.
/// The region is cut into blocks (each instance node, each ordinary entry's data, and any uncovered bytes),
/// the blocks keep their original order and are laid out back to back from the new data start; nodes created
/// in memory go after them. With no changes the output is byte-identical to the input.
/// </summary>
public static class InstanceLayout
{
    private sealed class Block
    {
        public int OldStart;
        public int OldLength;
        public int Order;
        public InstanceNode? Node;
        public RpakEntry? Entry;
        public ReadOnlyMemory<byte> Raw;
        public int NewStart;
        public int NewLength;
    }

    /// <summary>Serializes <paramref name="forest"/>'s archive (header, table and all trees) with fresh offsets.
    /// Entry and record objects are updated in place, so afterwards the source <see cref="RpakArchive"/> object no longer
    /// matches its old data region: re-read the returned bytes instead of reusing it.</summary>
    public static byte[] Write(InstanceForest forest)
    {
        var archive = forest.Archive;
        var oldStart = archive.DataStart;
        var oldEnd = oldStart + archive.DataRegion.Length;
        var blocks = new List<Block>();
        var order = 0;
        var nodeEntries = new HashSet<RpakEntry>(ReferenceEqualityComparer.Instance);
        foreach (var node in forest.AllNodes())
        {
            nodeEntries.Add(node.Record);
            if (node.IsNode || node.OpaqueData is { Length: > 0 })
            {
                blocks.Add(new Block
                {
                    OldStart = node.OriginalOffset < 0 ? int.MaxValue : node.OriginalOffset,
                    OldLength = node.OriginalOffset < 0 ? 0 : (int)node.Record.Size,
                    Order = order++,
                    Node = node,
                });
            }
        }

        foreach (var e in archive.Entries)
        {
            if (!nodeEntries.Contains(e) && e.Size > 0)
            {
                blocks.Add(new Block { OldStart = (int)e.Offset, OldLength = (int)e.Size, Order = order++, Entry = e, Raw = archive.GetData(e) });
            }
        }

        blocks.Sort((a, b) => a.OldStart != b.OldStart ? a.OldStart.CompareTo(b.OldStart) : a.Order.CompareTo(b.Order));

        // bytes no block covers are kept as raw blocks in place, so arbitrary (non-shipped) layouts survive
        var withHoles = new List<Block>();
        var cursor = oldStart;
        foreach (var b in blocks)
        {
            if (b.OldStart != int.MaxValue)
            {
                if (b.OldStart < cursor)
                {
                    throw new InvalidOperationException($"data at {b.OldStart} overlaps the previous block (ends {cursor}); shared data cannot be relaid out");
                }

                if (b.OldStart > cursor)
                {
                    withHoles.Add(new Block { OldStart = cursor, OldLength = b.OldStart - cursor, Raw = archive.DataRegion.AsMemory(cursor - oldStart, b.OldStart - cursor) });
                }

                cursor = b.OldStart + b.OldLength;
            }

            withHoles.Add(b);
        }

        if (cursor < oldEnd)
        {
            var tail = new Block { OldStart = cursor, OldLength = oldEnd - cursor, Raw = archive.DataRegion.AsMemory(cursor - oldStart, oldEnd - cursor) };
            var firstNew = withHoles.FindIndex(b => b.OldStart == int.MaxValue);
            withHoles.Insert(firstNew < 0 ? withHoles.Count : firstNew, tail);
        }

        // a fresh archive object has DataStart 0, so RpakArchive.Write takes the offsets we assign verbatim
        var fresh = new RpakArchive { Version = archive.Version, HeaderUnknown = archive.HeaderUnknown };
        fresh.References.AddRange(archive.References);
        fresh.Entries.AddRange(archive.Entries);
        var newStart = fresh.ComputeDataStart();
        var pos = newStart;
        foreach (var b in withHoles)
        {
            b.NewStart = pos;
            b.NewLength = b.Node is { } n ? n.SerializedSize : b.Raw.Length;
            pos += b.NewLength;
        }

        var newEnd = pos;
        int Map(uint offset)
        {
            var o = (int)offset;
            foreach (var b in withHoles)
            {
                if (b.OldStart != int.MaxValue && o >= b.OldStart && o < b.OldStart + b.OldLength)
                {
                    return b.NewStart + (o - b.OldStart);
                }
            }

            return o >= oldEnd ? newEnd + (o - oldEnd) : newStart + (o - oldStart);
        }

        // zero-size rows (folder-like entries) keep pointing at the same relative spot
        foreach (var e in archive.Entries)
        {
            if (!nodeEntries.Contains(e) && e.Size == 0)
            {
                e.Offset = (uint)Map(e.Offset);
            }
        }

        foreach (var b in withHoles)
        {
            if (b.Node is { } n)
            {
                n.Record.Offset = (uint)b.NewStart;
                n.Record.Size = (uint)b.NewLength;
                n.OriginalOffset = b.NewStart;
            }
            else if (b.Entry is { } e)
            {
                e.Offset = (uint)b.NewStart;
            }
        }

        var w = new ByteWriter();
        foreach (var b in withHoles)
        {
            if (b.Node is { } n)
            {
                n.WriteTo(w);
            }
            else
            {
                w.Bytes(b.Raw.Span);
            }
        }

        fresh.DataRegion = w.ToArray();
        return fresh.Write();
    }
}

/// <summary>Byte accounting of the region outside entry ranges ("gaps") versus what the instance decoder explains.</summary>
public sealed class InstanceCoverage
{
    public long GapBytes { get; init; }
    public long TreeBytes { get; init; }
    public long NodeStructureBytes { get; init; }
    public Dictionary<string, long> ChunkBytes { get; } = new(StringComparer.Ordinal);

    /// <summary>Chunk bytes whose typed parse re-serializes to identical bytes (PHYS, POLY) or that are text (RSD).</summary>
    public long DecodedChunkBytes { get; init; }
    public long OpaqueChunkBytes { get; init; }
    public int Nodes { get; init; }
    public int Objects { get; init; }
    public int Roots { get; init; }
    public List<string> Problems { get; } = [];

    /// <summary>Tree bytes that are not in an ordinary entry's range (should equal <see cref="GapBytes"/>).</summary>
    public long TreeBytesInGaps { get; init; }

    public long DecodedBytes => NodeStructureBytes + DecodedChunkBytes;

    public static InstanceCoverage Measure(InstanceForest forest)
    {
        var archive = forest.Archive;
        var gaps = archive.FindGaps();
        var nodes = forest.AllNodes().ToList();
        long structure = 0, decoded = 0, opaque = 0, tree = 0, treeInGaps = 0;
        var chunkBytes = new Dictionary<string, long>(StringComparer.Ordinal);
        var problems = new List<string>();
        var gapIndex = 0;
        foreach (var n in nodes.Where(n => n.Record.Size > 0).OrderBy(n => n.Record.Offset))
        {
            var start = (long)n.Record.Offset;
            var end = start + n.Record.Size;
            tree += n.Record.Size;
            while (gapIndex < gaps.Count && gaps[gapIndex].Offset + gaps[gapIndex].Length <= start)
            {
                gapIndex++;
            }

            for (var g = gapIndex; g < gaps.Count && gaps[g].Offset < end; g++)
            {
                treeInGaps += Math.Min(end, gaps[g].Offset + gaps[g].Length) - Math.Max(start, gaps[g].Offset);
            }
        }

        foreach (var n in nodes.Where(n => n.IsNode))
        {
            structure += 8 + n.Children.Sum(c => c.Record.TableSize);
            foreach (var c in n.Chunks)
            {
                chunkBytes[c.TagText] = chunkBytes.GetValueOrDefault(c.TagText) + c.SerializedSize;
                var ok = false;
                try
                {
                    ok = c.Tag switch
                    {
                        InstanceChunk.TagPhys => PhysMesh.Parse(c.Payload).ToBytes().AsSpan().SequenceEqual(c.Payload),
                        InstanceChunk.TagPoly => PolyMesh.Parse(c.Payload).ToBytes().AsSpan().SequenceEqual(c.Payload),
                        InstanceChunk.TagRsd => true,
                        _ => false,
                    };
                }
                catch (SlrrFormatException ex)
                {
                    problems.Add($"{c.TagText} in node 0x{n.Record.Id:X8}: {ex.Message}");
                }

                if (ok)
                {
                    decoded += c.SerializedSize;
                }
                else
                {
                    opaque += c.SerializedSize;
                }
            }
        }

        var cov = new InstanceCoverage
        {
            GapBytes = gaps.Sum(g => (long)g.Length),
            TreeBytes = tree,
            TreeBytesInGaps = treeInGaps,
            NodeStructureBytes = structure,
            DecodedChunkBytes = decoded,
            OpaqueChunkBytes = opaque,
            Nodes = nodes.Count,
            Objects = nodes.Count(n => n.IsObject),
            Roots = forest.Roots.Count,
        };
        foreach (var kv in chunkBytes)
        {
            cov.ChunkBytes[kv.Key] = kv.Value;
        }

        cov.Problems.AddRange(problems);
        return cov;
    }
}
