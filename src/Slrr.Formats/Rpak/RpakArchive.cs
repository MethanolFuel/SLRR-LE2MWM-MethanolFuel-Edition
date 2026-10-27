using System.Text;
using Slrr.Core;

namespace Slrr.Formats.Rpak;

/// <summary>One 64-byte external-reference slot of an RPAK header.</summary>
/// <param name="Unknown">u16 at slot offset 0; 0 in every shipped file.</param>
/// <param name="Index">u16 slot number (1-based in every shipped file).</param>
/// <param name="NameBytes">The raw 60-byte, NUL-padded name field. Kept verbatim because many files carry
/// stale bytes after the terminator (e.g. <c>cars.rpk\0ge.rpk</c>), which byte-identical repacking must preserve.</param>
public sealed record RpakReference(ushort Unknown, ushort Index, byte[] NameBytes)
{
    public const int NameFieldSize = 60;

    public string Name
    {
        get
        {
            var end = Array.IndexOf(NameBytes, (byte)0);
            return Encoding.Latin1.GetString(NameBytes, 0, end < 0 ? NameBytes.Length : end);
        }
    }

    public static RpakReference Create(ushort index, string name)
    {
        var bytes = new byte[NameFieldSize];
        var encoded = Encoding.Latin1.GetBytes(name);
        if (encoded.Length >= NameFieldSize)
        {
            throw new ArgumentException($"reference name longer than {NameFieldSize - 1} bytes", nameof(name));
        }

        encoded.CopyTo(bytes, 0);
        return new RpakReference(0, index, bytes);
    }
}

/// <summary>
/// One row of the RPAK entry table (23 bytes + alias, + 48 bytes when <see cref="TypeOfEntry"/> is 1).
/// Field names follow community naming (SlrrLib); semantics beyond layout are hypotheses.
/// </summary>
public sealed class RpakEntry
{
    /// <summary>First u32. Shared by many entries (10,773 repeats in 12,247 shipped entries): a parent/super id, not a key.</summary>
    public uint SuperId { get; set; }

    /// <summary>Second u32. Near-unique per archive (36 repeats in 12,247): the resource id that <c>rpk:0xID</c> refers to (hypothesis).</summary>
    public uint Id { get; set; }
    public byte TypeOfEntry { get; set; }
    public byte AdditionalType { get; set; }

    /// <summary>Raw bits of the f32 at entry offset 10 (1.0 or 0.0 in shipped files). Kept as bits for exact round-trip.</summary>
    public uint ParentCompatibleBits { get; set; }

    /// <summary>Absolute file offset of this entry's data.</summary>
    public uint Offset { get; set; }

    public uint Size { get; set; }

    /// <summary>Raw alias bytes as stored, normally including a trailing NUL.</summary>
    public byte[] AliasBytes { get; set; } = [];

    /// <summary>48 trailing bytes present only on TypeOfEntry == 1 rows (bounding box + 24 bytes, see docs).</summary>
    public byte[]? Extra { get; set; }

    public float ParentCompatible => BitConverter.Int32BitsToSingle(unchecked((int)ParentCompatibleBits));

    public string Alias
    {
        get
        {
            var end = Array.IndexOf(AliasBytes, (byte)0);
            return Encoding.Latin1.GetString(AliasBytes, 0, end < 0 ? AliasBytes.Length : end);
        }
    }

    public bool HasExtra => TypeOfEntry == RpakArchive.ExtraEntryType;

    public int TableSize => 23 + AliasBytes.Length + (HasExtra ? RpakArchive.ExtraSize : 0);
}

/// <summary>
/// RPAK resource archive (magic <c>RPAK</c>, version 0x200). Layout, all little-endian:
/// header(16) | references(64 each) | entry-table header(16: size, count, type-1 count, other count) | entry table | data region (to EOF).
/// The data region is kept as one blob so that bytes not covered by any entry
/// (the instance/spatial trees of TypeOfEntry 1 entries, which contain absolute offsets) survive verbatim.
/// </summary>
public sealed class RpakArchive
{
    public static ReadOnlySpan<byte> Magic => "RPAK"u8;
    public const byte ExtraEntryType = 1;
    public const int ExtraSize = 48;

    public uint Version { get; set; } = 0x200;

    /// <summary>u32 after the reference count; 0 in every shipped file.</summary>
    public uint HeaderUnknown { get; set; }

    public List<RpakReference> References { get; } = [];
    public List<RpakEntry> Entries { get; } = [];

    /// <summary>Everything from the end of the entry table to end of file.</summary>
    public byte[] DataRegion { get; set; } = [];

    /// <summary>File offset at which <see cref="DataRegion"/> starts (as read, or as last serialized).</summary>
    public int DataStart { get; private set; }

    public int ComputeTableSize() => Entries.Sum(e => e.TableSize);

    public int ComputeDataStart() => 16 + (64 * References.Count) + 16 + ComputeTableSize();

    public ReadOnlyMemory<byte> GetData(RpakEntry entry)
    {
        var rel = (long)entry.Offset - DataStart;
        if (entry.Size == 0)
        {
            return ReadOnlyMemory<byte>.Empty;
        }

        if (rel < 0 || rel + entry.Size > DataRegion.Length)
        {
            throw new SlrrFormatException($"entry '{entry.Alias}' data [{entry.Offset}, +{entry.Size}) lies outside the data region");
        }

        return DataRegion.AsMemory((int)rel, (int)entry.Size);
    }

    public static RpakArchive Read(ReadOnlyMemory<byte> bytes)
    {
        var r = new ByteReader(bytes);
        r.Expect(Magic, "RPAK");
        var archive = new RpakArchive { Version = r.U32() };
        var refCount = r.CheckedCount(r.U32(), 64, "reference");
        archive.HeaderUnknown = r.U32();
        // reference count was validated before HeaderUnknown was consumed; re-check against what is left
        if (refCount * 64L > r.Remaining)
        {
            throw new SlrrFormatException($"reference table ({refCount} x 64) exceeds file size");
        }

        for (var i = 0; i < refCount; i++)
        {
            var unknown = r.U16();
            var index = r.U16();
            archive.References.Add(new RpakReference(unknown, index, r.Bytes(RpakReference.NameFieldSize)));
        }

        var tableSize = r.U32();
        var count = r.CheckedCount(r.U32(), 23, "entry");
        var declaredTypeOne = r.U32();
        var otherCount = r.U32();
        var tableStart = r.Position;
        if (tableSize > (uint)r.Remaining)
        {
            throw new SlrrFormatException($"entry table size {tableSize} exceeds file size");
        }

        var typeOneCount = 0;
        for (var i = 0; i < count; i++)
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
                e.Extra = r.Bytes(ExtraSize);
                typeOneCount++;
            }

            archive.Entries.Add(e);
        }

        if (r.Position - tableStart != tableSize)
        {
            throw new SlrrFormatException($"entry table is {r.Position - tableStart} bytes but header says {tableSize}");
        }

        if (declaredTypeOne != (uint)typeOneCount || otherCount + declaredTypeOne != (uint)count)
        {
            throw new SlrrFormatException($"entry counts disagree: total {count}, type-1 declared {declaredTypeOne} found {typeOneCount}, others {otherCount}");
        }

        archive.DataStart = r.Position;
        archive.DataRegion = r.Bytes(r.Remaining);
        return archive;
    }

    public static RpakArchive Read(string path) => Read(File.ReadAllBytes(path));

    /// <summary>
    /// Serializes the archive. If the entry table changed size, entry offsets are shifted by the delta,
    /// which is only safe when no TypeOfEntry 1 entries exist (their trees hold absolute offsets).
    /// </summary>
    public byte[] Write()
    {
        var newStart = ComputeDataStart();
        if (newStart != DataStart && DataStart != 0)
        {
            if (Entries.Any(e => e.HasExtra))
            {
                throw new InvalidOperationException("entry table size changed in an archive with TypeOfEntry 1 entries; their data holds absolute offsets and cannot be relocated yet");
            }

            var delta = newStart - DataStart;
            foreach (var e in Entries)
            {
                e.Offset = (uint)(e.Offset + delta);
            }
        }

        var w = new ByteWriter();
        w.Bytes(Magic);
        w.U32(Version);
        w.U32((uint)References.Count);
        w.U32(HeaderUnknown);
        foreach (var reference in References)
        {
            if (reference.NameBytes.Length != RpakReference.NameFieldSize)
            {
                throw new InvalidOperationException($"reference {reference.Index} name field must be {RpakReference.NameFieldSize} bytes");
            }

            w.U16(reference.Unknown);
            w.U16(reference.Index);
            w.Bytes(reference.NameBytes);
        }

        var typeOne = Entries.Count(e => e.HasExtra);
        w.U32((uint)ComputeTableSize());
        w.U32((uint)Entries.Count);
        w.U32((uint)typeOne);
        w.U32((uint)(Entries.Count - typeOne));
        foreach (var e in Entries)
        {
            if (e.AliasBytes.Length > 255)
            {
                throw new InvalidOperationException($"alias of entry 0x{e.SuperId:X8} is longer than 255 bytes");
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
                if (e.Extra is not { Length: ExtraSize })
                {
                    throw new InvalidOperationException($"TypeOfEntry 1 entry '{e.Alias}' needs exactly {ExtraSize} extra bytes");
                }

                w.Bytes(e.Extra);
            }
        }

        DataStart = w.Position;
        w.Bytes(DataRegion);
        return w.ToArray();
    }

    /// <summary>Byte ranges of the data region not covered by any entry (absolute offsets).</summary>
    public IReadOnlyList<(int Offset, int Length)> FindGaps()
    {
        var gaps = new List<(int, int)>();
        var cursor = DataStart;
        foreach (var e in Entries.Where(e => e.Size > 0).OrderBy(e => e.Offset))
        {
            if (e.Offset > cursor)
            {
                gaps.Add((cursor, (int)e.Offset - cursor));
            }

            cursor = Math.Max(cursor, (int)(e.Offset + e.Size));
        }

        var end = DataStart + DataRegion.Length;
        if (cursor < end)
        {
            gaps.Add((cursor, end - cursor));
        }

        return gaps;
    }
}
