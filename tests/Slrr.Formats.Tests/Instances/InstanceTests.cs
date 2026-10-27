using System.Text;
using Slrr.Cli;
using Slrr.Core;
using Slrr.Formats.Instances;
using Slrr.Formats.Rpak;
using Xunit;

namespace Slrr.Formats.Tests.Instances;

public class InstanceTests
{
    private static byte[] Box(float cx, float cy, float cz, float hx, float hy, float hz)
    {
        var b = new byte[48];
        var f = new[] { cx, cy, cz, hx, hy, hz };
        for (var i = 0; i < 6; i++)
        {
            BitConverter.GetBytes(f[i]).CopyTo(b, i * 4);
        }

        return b;
    }

    private static RpakEntry Rec(uint parent, uint id, byte addType, string alias, byte[] box) =>
        new() { SuperId = parent, Id = id, TypeOfEntry = 1, AdditionalType = addType, AliasBytes = alias.Length == 0 ? [] : Encoding.Latin1.GetBytes(alias + "\0"), Extra = box };

    private static PhysMesh Triangle()
    {
        var m = new PhysMesh();
        m.Vertices.AddRange([new Vec3(0, 0, 0), new Vec3(0, 0, 1), new Vec3(1, 0, 0)]);
        m.Triangles.Add(new PhysTriangle(2, 0, 1, 2, new Vec3(0, 1, 0)));
        return m;
    }

    /// <summary>Archive with one text entry and a 3-node tree (root -> PHYS leaf, object leaf), laid out from memory.</summary>
    private static byte[] BuildSample()
    {
        var a = new RpakArchive();
        a.References.Add(RpakReference.Create(1, "system.rpk"));
        a.References.Add(RpakReference.Create(2, "objects.rpk"));
        var rootRec = Rec(0x10004, 1, 0x11, "_main_", Box(0, 0, 0, 10, 10, 10));
        var text = Encoding.Latin1.GetBytes("mesh 0x00000030\r\n");
        var textEntry = new RpakEntry { SuperId = 0x10003, Id = 0x88, TypeOfEntry = 14, AliasBytes = "trasa\0"u8.ToArray(), Size = (uint)text.Length };
        a.Entries.Add(rootRec);
        a.Entries.Add(textEntry);
        a.DataRegion = text;

        var root = new InstanceNode(rootRec);
        root.Chunks.Add(new InstanceChunk(InstanceChunk.TagRsd, Encoding.Latin1.GetBytes("gametype 0x00010035\r\nparams 0x00000004\r\n")));
        var leaf = new InstanceNode(Rec(1, 0x37E, 0x01, "0A", Box(0.5f, 0, 0.5f, 0.5f, 0, 0.5f)));
        leaf.Chunks.Add(new InstanceChunk(InstanceChunk.TagPhys, Triangle().ToBytes()));
        var objRec = Rec(1, 0x220, 0x21, "lamp1", Box(3, 2, 1, 0.1f, 0.1f, 0.1f));
        objRec.ParentCompatibleBits = 0x3F800000;
        var obj = new InstanceNode(objRec);
        obj.Chunks.Add(new InstanceChunk(InstanceChunk.TagRsd, Encoding.Latin1.GetBytes("gametype 0x0002018E\r\nparams 3.000,2.000,1.000,-1.500,0.000,0.000\r\n")));
        root.Children.Add(leaf);
        root.Children.Add(obj);

        // the text entry's data lives in the region as an ordinary block; give it an offset inside it
        textEntry.Offset = (uint)a.ComputeDataStart();
        var bytes = RpakArchive.Read(a.Write()); // establishes DataStart so the text block can be located
        var forest = InstanceForest.FromRoots(bytes, CloneTree(root, bytes.Entries[0]));
        return InstanceLayout.Write(forest);
    }

    private static InstanceNode CloneTree(InstanceNode src, RpakEntry record)
    {
        var n = new InstanceNode(record) { Header = src.Header };
        n.Chunks.AddRange(src.Chunks);
        foreach (var c in src.Children)
        {
            n.Children.Add(CloneTree(c, c.Record));
        }

        return n;
    }

    [Fact]
    public void SyntheticTreeWritesReadsAndRewritesIdentically()
    {
        var bytes = BuildSample();
        var archive = RpakArchive.Read(bytes);
        var forest = InstanceForest.Read(archive);
        var root = Assert.Single(forest.Roots);
        Assert.Equal(2, root.Children.Count);
        Assert.Equal(0x00010035u, root.Rsd!.GameType);
        var obj = root.Children[1];
        Assert.True(obj.IsObject);
        Assert.Equal(0x0002018Eu, obj.Rsd!.GameType);
        var pl = obj.Rsd.Placement!.Value;
        Assert.Equal(new Vec3(3, 2, 1), pl.Position);
        Assert.Equal(-1.5f, pl.Yaw);
        Assert.Equal(3f, obj.Box.CenterX);
        var phys = PhysMesh.Parse(root.Children[0].FindChunk(InstanceChunk.TagPhys)!.Payload);
        Assert.Equal(3, phys.Vertices.Count);
        Assert.Equal("mesh 0x00000030\r\n", Encoding.Latin1.GetString(archive.GetData(archive.Entries[1]).Span));

        // no gaps beyond the tree, and the tree fills them exactly
        var cov = InstanceCoverage.Measure(forest);
        Assert.Equal(cov.GapBytes, cov.TreeBytesInGaps);
        Assert.Equal(cov.TreeBytes, cov.DecodedBytes);

        Assert.Equal(bytes, InstanceLayout.Write(InstanceForest.Read(RpakArchive.Read(bytes))));
    }

    [Fact]
    public void GrowingAChunkAndTheHeaderRelocatesEveryOffset()
    {
        var bytes = BuildSample();
        var archive = RpakArchive.Read(bytes);
        archive.References.Add(RpakReference.Create(3, "particles.rpk"));
        var forest = InstanceForest.Read(archive);
        var obj = forest.Roots[0].Children[1];
        obj.Chunks[0].Payload = Encoding.Latin1.GetBytes("gametype 0x0002018E\r\nparams 30.000,2.000,1.000,0.250,0.000,0.000\r\nflags 1.000\r\n");
        obj.Record.AliasBytes = "a_much_longer_alias\0"u8.ToArray();

        var moved = RpakArchive.Read(InstanceLayout.Write(forest));
        Assert.Equal(3, moved.References.Count);
        var again = InstanceForest.Read(moved);
        var o2 = again.Roots[0].Children[1];
        Assert.Equal("a_much_longer_alias", o2.Record.Alias);
        Assert.Equal(30f, o2.Rsd!.Placement!.Value.Position.X);
        Assert.Equal("1.000", o2.Rsd.Get("flags"));
        Assert.Equal(3, PhysMesh.Parse(again.Roots[0].Children[0].Chunks[0].Payload).Vertices.Count);
        Assert.Equal("mesh 0x00000030\r\n", Encoding.Latin1.GetString(moved.GetData(moved.Entries[1]).Span));
        Assert.Equal(InstanceCoverage.Measure(again).GapBytes, InstanceCoverage.Measure(again).TreeBytesInGaps);
    }

    [Fact]
    public void ChunkCodecsRoundTripAndValidate()
    {
        var phys = Triangle().ToBytes();
        Assert.Equal(phys, PhysMesh.Parse(phys).ToBytes());
        phys[8] = 7; // triangle index A out of range
        Assert.Throws<SlrrFormatException>(() => PhysMesh.Parse(phys));

        var poly = new PolyMesh { Unknown = 1 };
        var sm = new PolySubmesh { Material = 3 };
        for (var i = 0; i < 3; i++)
        {
            sm.Vertices.Add(new PolyVertex(new Vec3(i, 0, 0), new Vec3(0, 1, 0), 0xFFFFFFFF, 0xFFFFFFFF, i, 0, 0.5f, 0.5f, 0, 0));
            sm.Indices.Add((uint)i);
        }

        poly.Submeshes.Add(sm);
        var pb = poly.ToBytes();
        Assert.Equal(8 + 12 + (3 * PolyMesh.VertexSize) + 12, pb.Length);
        Assert.Equal(pb, PolyMesh.Parse(pb).ToBytes());
        Assert.Throws<SlrrFormatException>(() => PolyMesh.Parse(pb.AsMemory(0, pb.Length - 2)));
    }

    [Fact]
    public void MalformedTreesAreRejected()
    {
        var bytes = BuildSample();
        var archive = RpakArchive.Read(bytes);
        var root = archive.Entries[0];

        // child record pointing back at the root: shared/cyclic data
        var start = (int)root.Offset;
        var childOffsetField = start + 8 + 4 + 4 + 1 + 1 + 4; // first child record's offset field
        var cyclic = (byte[])bytes.Clone();
        BitConverter.GetBytes(root.Offset).CopyTo(cyclic, childOffsetField);
        Assert.Throws<SlrrFormatException>(() => InstanceForest.Read(RpakArchive.Read(cyclic)));

        // child count far beyond the node size
        var huge = (byte[])bytes.Clone();
        BitConverter.GetBytes(0x10000000u).CopyTo(huge, start + 4);
        Assert.Throws<SlrrFormatException>(() => InstanceForest.Read(RpakArchive.Read(huge)));
    }

    [Theory]
    [InlineData("maps/MP1.rpk")]
    [InlineData("misc/dealer.rpk")]
    [InlineData("misc/garage.rpk")]
    public void ShippedArchivesAreFullyCoveredAndRelocatable(string rel)
    {
        var root = GameRoot.Find();
        if (root is null || !File.Exists(Path.Combine(root, rel)))
        {
            return;
        }

        var original = File.ReadAllBytes(Path.Combine(root, rel));
        var forest = InstanceForest.Read(RpakArchive.Read(original));
        var cov = InstanceCoverage.Measure(forest);
        Assert.True(cov.GapBytes > 0);
        Assert.Equal(cov.GapBytes, cov.TreeBytesInGaps);
        Assert.Equal(cov.TreeBytes, cov.DecodedBytes);
        Assert.Empty(cov.Problems);
        var print = CmdInstances.TreeFingerprint(forest);

        Assert.Equal(original, InstanceLayout.Write(InstanceForest.Read(RpakArchive.Read(original))));

        var shifted = RpakArchive.Read(original);
        shifted.References.Add(RpakReference.Create((ushort)(shifted.References.Count + 1), "zz.rpk"));
        var moved = InstanceLayout.Write(InstanceForest.Read(shifted));
        Assert.Equal(original.Length + 64, moved.Length);
        Assert.Equal(print, CmdInstances.TreeFingerprint(InstanceForest.Read(RpakArchive.Read(moved))));
    }

    [Fact]
    public void CliDumpsSyntheticArchive()
    {
        var path = Path.Combine(Path.GetTempPath(), "slrr-inst-" + Guid.NewGuid().ToString("N") + ".rpk");
        try
        {
            File.WriteAllBytes(path, BuildSample());
            var o = new StringWriter();
            Assert.Equal(0, CmdInstances.Run(["dump-instances", path], o, TextWriter.Null));
            Assert.Contains("lamp1", o.ToString(), StringComparison.Ordinal);
            Assert.Contains("objects.rpk:0x18E", o.ToString(), StringComparison.Ordinal);
            o = new StringWriter();
            Assert.Equal(0, CmdInstances.Run([path, "--tree", "--chunks"], o, TextWriter.Null));
            Assert.Contains("PHYS 1 triangles, 3 vertices", o.ToString(), StringComparison.Ordinal);
            Assert.Equal(2, CmdInstances.Run([], TextWriter.Null, TextWriter.Null));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
