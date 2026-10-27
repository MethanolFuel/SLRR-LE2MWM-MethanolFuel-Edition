using System.Buffers.Binary;
using Slrr.Engine.SystemLib;
using Xunit;

namespace Slrr.Career.Tests;

public sealed class UnitTests
{
    // The shipped strings of GameLogic.kismajom (MEASURED from GameLogic.class) and what a player types.
    private static readonly string[] Kismajom = ["mfunfspd", "cfhgpsnpofz", "gbtuqspnpujpo", "jmjlfdifbujoh", "ujnfnbdijof"];

    [Fact]
    public void ShippedCheatStringsDecodeByMinusOne()
    {
        Assert.Equal(["letmeroc", "begformoney", "fastpromotion", "ilikecheating", "timemachine"], Kismajom.Select(TypedRing.Decode));
    }

    [Fact]
    public void KismajomCheckMatchesTheLastTypedCharactersAndClearsTheLastByte()
    {
        var ring = new TypedRing();
        Assert.Equal(-1, ring.Check(Kismajom));
        ring.Type("xxbegformoney");
        Assert.Equal(1, ring.Check(Kismajom));
        Assert.Equal(-1, ring.Check(Kismajom)); // the exe zeroes the last written byte on a match
        ring.Type("ilikecheating");
        Assert.Equal(3, ring.Check(Kismajom));
        ring.Type("letmero");
        Assert.Equal(-1, ring.Check(Kismajom));
        ring.Type("c");
        Assert.Equal(0, ring.Check(Kismajom));
    }

    [Fact]
    public void KismajomRingKeepsSixteenCharacters()
    {
        var ring = new TypedRing();
        ring.Type("fastpromotion"); // 13 characters fit
        Assert.Equal(2, ring.Check(Kismajom));
        var longer = new TypedRing();
        longer.Type("abcdefghijklmnopq"); // wraps
        Assert.Equal(-1, longer.Check(["bcdefghijklmnopqr"])); // 17 > 16: cannot match
    }

    [Fact]
    public void SdatTrailerRoundTripsArchiveNames()
    {
        var body = SdatWriter.Build(w =>
        {
            w.WriteInt(unchecked((int)0x88664422));
            w.WriteInt((1 << 16) | 0x157);
        });
        var trailer = SdatTrailer.Build([(1, @"cars\racers\Baiern.rpk"), (2, "parts.rpk")]);
        var image = body.Concat(trailer).ToArray();
        var r = new SdatReader(image);
        Assert.NotNull(r.Trailer);
        Assert.Equal(@"cars\racers\Baiern.rpk", r.Trailer!.Archives[1]);
        Assert.Equal("parts.rpk", r.Trailer.Archives[2]);
        Assert.Equal(body.Length, r.Trailer.Start);
        Assert.Equal(unchecked((int)0x88664422), r.ReadInt());
        Assert.Equal((1 << 16) | 0x157, r.ReadInt());
        Assert.Equal(0, r.ReadInt()); // the trailer is not data
        Assert.True(r.Truncated);
    }

    [Fact]
    public void EmptyTrailerOfTheShippedControlFileShapeIsRecognised()
    {
        // save/controls/active_control_set ends in 00000000 00000000 08000000 (MEASURED): no instances, no archives, size 8
        var body = SdatWriter.Build(w => w.WriteInt(42));
        var image = body.Concat(new byte[] { 0, 0, 0, 0, 0, 0, 0, 0, 8, 0, 0, 0 }).ToArray();
        var r = new SdatReader(image);
        Assert.NotNull(r.Trailer);
        Assert.Empty(r.Trailer!.Archives);
        Assert.Equal(42, r.ReadInt());
        Assert.Equal(0, r.Remaining);
    }

    [Fact]
    public void FilesWithoutTrailerKeepEveryByte()
    {
        var body = SdatWriter.Build(w =>
        {
            w.WriteInt(1);
            w.WriteInt(2);
            w.WriteInt(3);
        });
        var r = new SdatReader(body);
        Assert.Null(r.Trailer);
        Assert.Equal(12, r.Remaining);
        Assert.Equal(1, BinaryPrimitives.ReadInt32LittleEndian(body.AsSpan(8)));
    }
}
