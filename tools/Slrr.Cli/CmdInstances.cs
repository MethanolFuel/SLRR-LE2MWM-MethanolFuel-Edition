using System.Globalization;
using System.Security.Cryptography;
using Slrr.Core;
using Slrr.Formats.Instances;
using Slrr.Formats.Rpak;

namespace Slrr.Cli;

/// <summary>
/// <c>dump-instances</c>: decode RPAK instance/spatial trees (TypeOfEntry 1).
/// <code>
///   dump-instances &lt;archive.rpk&gt; [--tree [maxDepth]] [--chunks]   summary + placed objects; optional tree dump
///   dump-instances --coverage [gameRoot]                         per-archive coverage + relocation self-test
/// </code>
/// </summary>
public static class CmdInstances
{
    private const string Usage = """
        dump-instances <archive.rpk> [--tree [maxDepth]] [--chunks]
        dump-instances --coverage [gameRoot]
        """;

    public static int Run(string[] args, TextWriter o, TextWriter e)
    {
        // accept both "dump-instances ..." and the bare arguments
        if (args.Length > 0 && args[0] == "dump-instances")
        {
            args = args[1..];
        }

        try
        {
            if (args.Length >= 1 && args[0] == "--coverage")
            {
                return Coverage(args.Length >= 2 ? args[1] : Directory.GetCurrentDirectory(), o);
            }

            if (args.Length >= 1 && !args[0].StartsWith("--", StringComparison.Ordinal))
            {
                var t = Array.IndexOf(args, "--tree");
                int? depth = null;
                if (t >= 0)
                {
                    depth = t + 1 < args.Length && int.TryParse(args[t + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var d) ? d : int.MaxValue;
                }

                return Dump(args[0], depth, args.Contains("--chunks"), o);
            }

            e.WriteLine(Usage);
            return 2;
        }
        catch (Exception ex) when (ex is SlrrFormatException or IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            e.WriteLine($"error: {ex.Message}");
            return 1;
        }
    }

    private static string F(float v) => v.ToString("0.###", CultureInfo.InvariantCulture);

    private static string GameTypeName(RpakArchive a, uint gameType)
    {
        var slot = (int)(gameType >> 16);
        // slot 0 = this archive (e.g. DamageTrack.rpk track_coord -> local id 0x4)
        var refName = slot == 0 ? "self" : slot <= a.References.Count ? a.References[slot - 1].Name : $"ref{slot}?";
        return $"{refName}:0x{gameType & 0xFFFF:X}";
    }

    private static int Dump(string path, int? treeDepth, bool chunks, TextWriter o)
    {
        var archive = RpakArchive.Read(path);
        var forest = InstanceForest.Read(archive);
        var cov = InstanceCoverage.Measure(forest);
        o.WriteLine($"{Path.GetFileName(path)}: {cov.Roots} roots, {cov.Nodes} nodes, {cov.Objects} placed objects; gap bytes {cov.GapBytes}, tree bytes {cov.TreeBytes}, decoded {cov.DecodedBytes}");
        foreach (var root in forest.Roots)
        {
            var b = root.Box;
            o.WriteLine($"root 0x{root.Record.Id:X8} '{root.Record.Alias}' kind 1/0x{root.Record.AdditionalType:X2} center ({F(b.CenterX)}, {F(b.CenterY)}, {F(b.CenterZ)}) half ({F(b.HalfX)}, {F(b.HalfY)}, {F(b.HalfZ)})");
            if (root.Rsd is { } rsd)
            {
                foreach (var (k, v) in rsd.Lines())
                {
                    o.WriteLine($"    {k} {v}");
                }
            }
        }

        if (treeDepth is { } max)
        {
            foreach (var root in forest.Roots)
            {
                DumpTree(root, max, chunks, o, archive);
            }
        }
        else
        {
            o.WriteLine("objects (id, name, gametype, position, yaw/pitch/roll):");
            foreach (var n in forest.AllNodes().Where(n => n.IsObject))
            {
                var rsd = n.Rsd;
                var gt = rsd?.GameType is { } g ? GameTypeName(archive, g) : "-";
                var pl = rsd?.Placement;
                var place = pl is { } p ? $"{p.Position} ypr ({F(p.Yaw)}, {F(p.Pitch)}, {F(p.Roll)})" : rsd?.Params ?? "-";
                o.WriteLine($"  0x{n.Record.Id:X8} {n.Record.Alias,-28} {gt,-26} {place}");
            }
        }

        return 0;
    }

    private static void DumpTree(InstanceNode n, int maxDepth, bool chunks, TextWriter o, RpakArchive a)
    {
        if (n.Depth > maxDepth)
        {
            return;
        }

        var indent = new string(' ', 2 * n.Depth);
        var b = n.Box;
        var tags = string.Join("+", n.Chunks.Select(c => c.TagText));
        o.WriteLine($"{indent}0x{n.Record.Id:X8} 1/0x{n.Record.AdditionalType:X2} '{n.Record.Alias}' @{n.Record.Offset} +{n.Record.Size} kids {n.Children.Count} [{tags}] c({F(b.CenterX)}, {F(b.CenterY)}, {F(b.CenterZ)}) h({F(b.HalfX)}, {F(b.HalfY)}, {F(b.HalfZ)})");
        if (chunks)
        {
            foreach (var c in n.Chunks)
            {
                switch (c.Tag)
                {
                    case InstanceChunk.TagPhys:
                        var ph = PhysMesh.Parse(c.Payload);
                        o.WriteLine($"{indent}  PHYS {ph.Triangles.Count} triangles, {ph.Vertices.Count} vertices, surfaces {string.Join(",", ph.Triangles.Select(t => t.Surface).Distinct().Order())}");
                        break;
                    case InstanceChunk.TagPoly:
                        var po = PolyMesh.Parse(c.Payload);
                        o.WriteLine($"{indent}  POLY unknown {po.Unknown}, {po.Submeshes.Count} submeshes: {string.Join("; ", po.Submeshes.Select(s => $"mat {s.Material} v {s.Vertices.Count} t {s.Indices.Count / 3}"))}");
                        break;
                    case InstanceChunk.TagRsd:
                        var rsd = new RsdText(c.Payload);
                        foreach (var (k, v) in rsd.Lines())
                        {
                            o.WriteLine($"{indent}  RSD {k} {v}{(k == "gametype" && rsd.GameType is { } g ? "  -> " + GameTypeName(a, g) : string.Empty)}");
                        }

                        break;
                    default:
                        o.WriteLine($"{indent}  {c.TagText} {Convert.ToHexString(c.Payload)}");
                        break;
                }
            }
        }

        foreach (var c in n.Children)
        {
            DumpTree(c, maxDepth, chunks, o, a);
        }
    }

    /// <summary>Hash of every node's structure and chunk payloads in tree order (offsets excluded).</summary>
    public static string TreeFingerprint(InstanceForest forest)
    {
        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var w = new ByteWriter();
        foreach (var n in forest.AllNodes())
        {
            w.U32(n.Record.SuperId);
            w.U32(n.Record.Id);
            w.U8(n.Record.AdditionalType);
            w.Bytes(n.Record.AliasBytes);
            w.Bytes(n.Record.Extra ?? []);
            w.U32((uint)n.Children.Count);
            foreach (var c in n.Chunks)
            {
                w.U32(c.Tag);
                w.U32((uint)c.Payload.Length);
                sha.AppendData(c.Payload);
            }

            w.Bytes(n.OpaqueData ?? []);
        }

        sha.AppendData(w.ToArray());
        return Convert.ToHexString(sha.GetHashAndReset());
    }

    private static int Coverage(string root, TextWriter o)
    {
        var fs = new GameFileSystem(root);
        long gap = 0, inGaps = 0, decoded = 0, tree = 0;
        int archives = 0, identical = 0, relocated = 0, fail = 0;
        var chunkTotals = new SortedDictionary<string, long>(StringComparer.Ordinal);
        o.WriteLine($"{"archive",-36} {"gap bytes",12} {"tree bytes",12} {"in gaps",12} {"decoded",12} {"%",7} nodes/objects  rewrite  shift+64");
        foreach (var file in fs.Enumerate(".rpk"))
        {
            var rel = Path.GetRelativePath(fs.Root, file);
            try
            {
                var original = File.ReadAllBytes(file);
                var archive = RpakArchive.Read(original);
                if (!archive.Entries.Any(x => x.HasExtra))
                {
                    continue;
                }

                archives++;
                var forest = InstanceForest.Read(archive);
                var cov = InstanceCoverage.Measure(forest);
                var print = TreeFingerprint(forest);

                // 1) relayout without changes must be byte-identical
                var same = InstanceLayout.Write(InstanceForest.Read(RpakArchive.Read(original))).AsSpan().SequenceEqual(original);

                // 2) grow the header by one reference slot (64 bytes): every absolute offset must move
                var shifted = RpakArchive.Read(original);
                shifted.References.Add(RpakReference.Create((ushort)(shifted.References.Count + 1), "zz_shift_test.rpk"));
                var shiftedBytes = InstanceLayout.Write(InstanceForest.Read(shifted));
                var reread = RpakArchive.Read(shiftedBytes);
                var shiftOk = TreeFingerprint(InstanceForest.Read(reread)) == print
                    && reread.Entries.Where(x => !x.HasExtra && x.Size > 0).Select(x => Convert.ToHexString(SHA256.HashData(reread.GetData(x).Span)))
                        .SequenceEqual(archive.Entries.Where(x => !x.HasExtra && x.Size > 0).Select(x => Convert.ToHexString(SHA256.HashData(archive.GetData(x).Span))))
                    && reread.FindGaps().Sum(g => (long)g.Length) == cov.GapBytes;

                identical += same ? 1 : 0;
                relocated += shiftOk ? 1 : 0;
                gap += cov.GapBytes;
                inGaps += cov.TreeBytesInGaps;
                tree += cov.TreeBytes;
                decoded += cov.DecodedBytes;
                foreach (var kv in cov.ChunkBytes)
                {
                    chunkTotals[kv.Key] = chunkTotals.GetValueOrDefault(kv.Key) + kv.Value;
                }

                var pct = cov.TreeBytes == 0 ? 100.0 : 100.0 * cov.DecodedBytes / cov.TreeBytes;
                o.WriteLine($"{rel,-36} {cov.GapBytes,12} {cov.TreeBytes,12} {cov.TreeBytesInGaps,12} {cov.DecodedBytes,12} {pct,7:0.000} {cov.Nodes,6}/{cov.Objects,-6}  {(same ? "same" : "DIFF"),-7}  {(shiftOk ? "ok" : "FAIL")}");
                foreach (var p in cov.Problems.Take(5))
                {
                    o.WriteLine($"    problem: {p}");
                }
            }
            catch (Exception ex) when (ex is SlrrFormatException or InvalidOperationException)
            {
                fail++;
                o.WriteLine($"FAIL {rel}: {ex.Message}");
            }
        }

        o.WriteLine($"archives with instance trees: {archives}; gap bytes {gap}; tree bytes {tree} ({inGaps} inside gaps); decoded {decoded} ({(tree == 0 ? 0 : 100.0 * decoded / tree):0.0000}% of tree bytes)");
        o.WriteLine($"chunk bytes: {string.Join(", ", chunkTotals.Select(kv => $"{kv.Key} {kv.Value}"))}");
        o.WriteLine($"byte-identical relayout: {identical}/{archives}; relocation by +64 bytes preserved trees and entry data: {relocated}/{archives}; failures {fail}");
        return fail == 0 && identical == archives && relocated == archives ? 0 : 1;
    }
}
