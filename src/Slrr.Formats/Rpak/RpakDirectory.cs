using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Slrr.Core;

namespace Slrr.Formats.Rpak;

/// <summary>
/// Unpacks an <see cref="RpakArchive"/> into a directory (manifest + one file per entry + one file per
/// uncovered data gap) and packs it back. Packing an unmodified directory reproduces the archive byte for byte.
/// </summary>
public static class RpakDirectory
{
    public const string ManifestName = "rpak.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public sealed class Manifest
    {
        public int FormatVersion { get; set; } = 1;
        public uint Version { get; set; }
        public uint HeaderUnknown { get; set; }
        public int DataStart { get; set; }
        public int DataLength { get; set; }
        public List<ReferenceRow> References { get; set; } = [];
        public List<EntryRow> Entries { get; set; } = [];
        public List<GapRow> Gaps { get; set; } = [];
    }

    public sealed class ReferenceRow
    {
        public ushort Unknown { get; set; }
        public ushort Index { get; set; }
        public string Name { get; set; } = "";

        /// <summary>Hex of the full 60-byte field, only when it differs from Name + NUL padding.</summary>
        public string? NameFieldHex { get; set; }
    }

    public sealed class EntryRow
    {
        public string SuperId { get; set; } = "";
        public string Id { get; set; } = "";
        public byte TypeOfEntry { get; set; }
        public byte AdditionalType { get; set; }
        public string ParentCompatibleBits { get; set; } = "";
        public uint Offset { get; set; }
        public uint Size { get; set; }
        public string Alias { get; set; } = "";

        /// <summary>Hex of the raw alias bytes, only when they differ from Alias + one NUL.</summary>
        public string? AliasHex { get; set; }

        public string? ExtraHex { get; set; }
        public string? File { get; set; }
    }

    public sealed class GapRow
    {
        public int Offset { get; set; }
        public int Length { get; set; }
        public string File { get; set; } = "";
    }

    public static void Unpack(RpakArchive archive, string outDir)
    {
        Directory.CreateDirectory(Path.Combine(outDir, "entries"));
        Directory.CreateDirectory(Path.Combine(outDir, "gaps"));
        var m = new Manifest
        {
            Version = archive.Version,
            HeaderUnknown = archive.HeaderUnknown,
            DataStart = archive.DataStart,
            DataLength = archive.DataRegion.Length,
        };

        foreach (var r in archive.References)
        {
            var canonical = RpakReference.Create(r.Index, r.Name).NameBytes;
            m.References.Add(new ReferenceRow
            {
                Unknown = r.Unknown,
                Index = r.Index,
                Name = r.Name,
                NameFieldHex = canonical.AsSpan().SequenceEqual(r.NameBytes) ? null : Convert.ToHexString(r.NameBytes),
            });
        }

        for (var i = 0; i < archive.Entries.Count; i++)
        {
            var e = archive.Entries[i];
            var alias = e.Alias;
            var canonicalAlias = Encoding.Latin1.GetBytes(alias + "\0");
            var row = new EntryRow
            {
                SuperId = $"0x{e.SuperId:X8}",
                Id = $"0x{e.Id:X8}",
                TypeOfEntry = e.TypeOfEntry,
                AdditionalType = e.AdditionalType,
                ParentCompatibleBits = $"0x{e.ParentCompatibleBits:X8}",
                Offset = e.Offset,
                Size = e.Size,
                Alias = alias,
                AliasHex = canonicalAlias.AsSpan().SequenceEqual(e.AliasBytes) ? null : Convert.ToHexString(e.AliasBytes),
                ExtraHex = e.Extra is null ? null : Convert.ToHexString(e.Extra),
            };
            if (e.Size > 0)
            {
                row.File = $"entries/{i:D5}_{SafeName(alias)}.dat";
                File.WriteAllBytes(Path.Combine(outDir, row.File), archive.GetData(e).ToArray());
            }

            m.Entries.Add(row);
        }

        foreach (var (offset, length) in archive.FindGaps())
        {
            var file = $"gaps/{offset:D10}.bin";
            File.WriteAllBytes(Path.Combine(outDir, file), archive.DataRegion.AsSpan(offset - archive.DataStart, length).ToArray());
            m.Gaps.Add(new GapRow { Offset = offset, Length = length, File = file });
        }

        File.WriteAllText(Path.Combine(outDir, ManifestName), JsonSerializer.Serialize(m, JsonOptions));
    }

    /// <summary>
    /// Rebuilds an archive from an unpacked directory. When every entry file still has its recorded size,
    /// the original layout is reproduced exactly. When sizes changed, entries are re-laid out sequentially;
    /// that is refused for archives with gaps, because gap data (instance trees) holds absolute offsets.
    /// </summary>
    public static RpakArchive Pack(string dir)
    {
        var m = JsonSerializer.Deserialize<Manifest>(File.ReadAllText(Path.Combine(dir, ManifestName)), JsonOptions)
            ?? throw new SlrrFormatException("empty manifest");
        var archive = new RpakArchive { Version = m.Version, HeaderUnknown = m.HeaderUnknown };
        foreach (var r in m.References)
        {
            archive.References.Add(r.NameFieldHex is null
                ? RpakReference.Create(r.Index, r.Name) with { Unknown = r.Unknown }
                : new RpakReference(r.Unknown, r.Index, Convert.FromHexString(r.NameFieldHex)));
        }

        var payloads = new List<byte[]?>();
        var sizesChanged = false;
        foreach (var row in m.Entries)
        {
            var e = new RpakEntry
            {
                SuperId = ParseHex(row.SuperId),
                Id = ParseHex(row.Id),
                TypeOfEntry = row.TypeOfEntry,
                AdditionalType = row.AdditionalType,
                ParentCompatibleBits = ParseHex(row.ParentCompatibleBits),
                Offset = row.Offset,
                Size = row.Size,
                AliasBytes = row.AliasHex is null ? Encoding.Latin1.GetBytes(row.Alias + "\0") : Convert.FromHexString(row.AliasHex),
                Extra = row.ExtraHex is null ? null : Convert.FromHexString(row.ExtraHex),
            };
            byte[]? payload = row.File is null ? null : File.ReadAllBytes(Path.Combine(dir, row.File));
            if ((payload?.Length ?? 0) != (int)e.Size)
            {
                sizesChanged = true;
            }

            archive.Entries.Add(e);
            payloads.Add(payload);
        }

        var dataStart = archive.ComputeDataStart();
        if (!sizesChanged && dataStart == m.DataStart)
        {
            var region = new byte[m.DataLength];
            var covered = new bool[m.DataLength];
            for (var i = 0; i < archive.Entries.Count; i++)
            {
                if (payloads[i] is { } p)
                {
                    Place(region, covered, (int)archive.Entries[i].Offset - dataStart, p, archive.Entries[i].Alias);
                }
            }

            foreach (var g in m.Gaps)
            {
                Place(region, covered, g.Offset - dataStart, File.ReadAllBytes(Path.Combine(dir, g.File)), g.File);
            }

            var hole = Array.IndexOf(covered, false);
            if (hole >= 0)
            {
                throw new SlrrFormatException($"data region byte at offset {hole + dataStart} is not covered by any entry or gap file");
            }

            archive.DataRegion = region;
            return archive;
        }

        if (m.Gaps.Count > 0 || archive.Entries.Any(e => e.HasExtra))
        {
            throw new SlrrFormatException("entry sizes or table size changed, but this archive has gap data / TypeOfEntry 1 trees with absolute offsets; relayout is not supported yet");
        }

        // Sequential relayout in original offset order; zero-size entries keep pointing at the next byte.
        var order = Enumerable.Range(0, archive.Entries.Count).OrderBy(i => archive.Entries[i].Offset).ThenBy(i => i).ToList();
        var w = new MemoryStream();
        foreach (var i in order)
        {
            var e = archive.Entries[i];
            e.Offset = (uint)(dataStart + w.Length);
            e.Size = (uint)(payloads[i]?.Length ?? 0);
            if (payloads[i] is { } p)
            {
                w.Write(p);
            }
        }

        archive.DataRegion = w.ToArray();
        return archive;
    }

    private static void Place(byte[] region, bool[] covered, int at, byte[] data, string what)
    {
        if (at < 0 || at + data.Length > region.Length)
        {
            throw new SlrrFormatException($"'{what}' does not fit in the data region");
        }

        data.CopyTo(region, at);
        Array.Fill(covered, true, at, data.Length);
    }

    private static uint ParseHex(string s) =>
        ResourceId.TryParse(s, out var id) ? id.Value : throw new SlrrFormatException($"bad hex value '{s}' in manifest");

    private static string SafeName(string alias)
    {
        var sb = new StringBuilder();
        foreach (var c in alias.Take(60))
        {
            sb.Append(char.IsAsciiLetterOrDigit(c) || c is '_' or '-' or '.' ? c : '_');
        }

        return sb.Length == 0 ? "unnamed" : sb.ToString();
    }
}
