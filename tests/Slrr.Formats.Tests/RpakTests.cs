using System.Text;
using Slrr.Core;
using Slrr.Formats.Rpak;
using Xunit;

namespace Slrr.Formats.Tests;

public class RpakTests
{
    private static RpakArchive Sample()
    {
        var a = new RpakArchive();
        var stale = RpakReference.Create(1, "cars.rpk").NameBytes;
        Encoding.Latin1.GetBytes("ge.rpk").CopyTo(stale, 9); // stale bytes after the NUL, as in sl.rpk
        a.References.Add(new RpakReference(0, 1, stale));
        a.References.Add(RpakReference.Create(2, "system.rpk"));
        var text = Encoding.Latin1.GetBytes("script java.game.Init\r\n");
        a.Entries.Add(new RpakEntry { SuperId = 0x10001, Id = 0x40, TypeOfEntry = 8, ParentCompatibleBits = 0x3F800000, AliasBytes = Encoding.Latin1.GetBytes("Init\0"), Size = (uint)text.Length });
        a.Entries.Add(new RpakEntry { SuperId = 0x10015, Id = 0x6E, TypeOfEntry = 8, ParentCompatibleBits = 0x3F800000, AliasBytes = Encoding.Latin1.GetBytes("empty\0") });
        var start = (uint)a.ComputeDataStart();
        a.Entries[0].Offset = start;
        a.Entries[1].Offset = start + (uint)text.Length;
        a.DataRegion = text;
        return a;
    }

    [Fact]
    public void WriteThenReadIsLossless()
    {
        var bytes = Sample().Write();
        var back = RpakArchive.Read(bytes);
        Assert.Equal(bytes, back.Write());
        Assert.Equal("cars.rpk", back.References[0].Name);
        Assert.Equal((byte)'g', back.References[0].NameBytes[9]);
        Assert.Equal("script java.game.Init\r\n", Encoding.Latin1.GetString(back.GetData(back.Entries[0]).Span));
        Assert.Equal(1.0f, back.Entries[0].ParentCompatible);
    }

    [Fact]
    public void TypeOneEntriesCarry48ExtraBytes()
    {
        var a = Sample();
        a.Entries.Insert(0, new RpakEntry { TypeOfEntry = 1, AdditionalType = 0x11, AliasBytes = "_main_\0"u8.ToArray(), Extra = new byte[48] });
        var start = (uint)a.ComputeDataStart();
        a.Entries[0].Offset = start;
        a.Entries[1].Offset = start;
        a.Entries[2].Offset = start + (uint)a.DataRegion.Length;
        var bytes = a.Write();
        var back = RpakArchive.Read(bytes);
        Assert.NotNull(back.Entries[0].Extra);
        Assert.Equal(bytes, back.Write());
    }

    [Fact]
    public void UnpackPackRoundTripsAndRelayoutsWhenSizesChange()
    {
        var dir = Path.Combine(Path.GetTempPath(), "slrr-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var original = Sample().Write();
            RpakDirectory.Unpack(RpakArchive.Read(original), dir);
            Assert.Equal(original, RpakDirectory.Pack(dir).Write());

            var entryFile = Directory.GetFiles(Path.Combine(dir, "entries")).Single();
            File.WriteAllText(entryFile, "script java.game.Other\r\n");
            var repacked = RpakArchive.Read(RpakDirectory.Pack(dir).Write());
            Assert.Equal("script java.game.Other\r\n", Encoding.Latin1.GetString(repacked.GetData(repacked.Entries[0]).Span));
        }
        finally
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, true);
            }
        }
    }

    [Fact]
    public void TruncatedFilesThrowFormatException()
    {
        var bytes = Sample().Write();
        for (var len = 0; len < bytes.Length; len++)
        {
            var cut = bytes.AsMemory(0, len);
            if (len < 120)
            {
                Assert.Throws<SlrrFormatException>(() => RpakArchive.Read(cut));
            }
            else
            {
                // once the tables are complete only the data region is short; reading must not crash
                try
                {
                    RpakArchive.Read(cut);
                }
                catch (SlrrFormatException)
                {
                }
            }
        }
    }

    [Fact]
    public void AllShippedArchivesRoundTripByteIdentical()
    {
        var root = GameRoot.Find();
        if (root is null)
        {
            return;
        }

        var files = new GameFileSystem(root).Enumerate(".rpk").ToList();
        Assert.Equal(87, files.Count);
        foreach (var f in files)
        {
            var original = File.ReadAllBytes(f);
            Assert.True(RpakArchive.Read(original).Write().AsSpan().SequenceEqual(original), f);
        }
    }
}
