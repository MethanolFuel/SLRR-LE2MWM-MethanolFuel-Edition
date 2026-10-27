using Xunit;

namespace Slrr.Engine.SystemLib.Tests;

/// <summary>Sandbox, SDAT and save-format tests that need no VM and no game data.</summary>
public sealed class UnitTests : IDisposable
{
    private readonly string _tmp = Path.Combine(Path.GetTempPath(), "slrr-sys-" + Guid.NewGuid().ToString("N"));
    private readonly GameSandbox _sb;

    public UnitTests()
    {
        Directory.CreateDirectory(Path.Combine(_tmp, "game", "parts", "engines"));
        Directory.CreateDirectory(Path.Combine(_tmp, "game", "save", "controls"));
        File.WriteAllText(Path.Combine(_tmp, "game", "parts", "engines", "A.rpk"), "x");
        File.WriteAllText(Path.Combine(_tmp, "game", "parts", "engines", "b.RPK"), "x");
        File.WriteAllText(Path.Combine(_tmp, "game", "parts", "engines", "c.txt"), "x");
        File.WriteAllText(Path.Combine(_tmp, "game", "save", "controls", "shipped"), "x");
        _sb = new GameSandbox(Path.Combine(_tmp, "game"), Path.Combine(_tmp, "saves"));
    }

    public void Dispose() => Directory.Delete(_tmp, recursive: true);

    [Theory]
    [InlineData("..\\evil")]
    [InlineData("save/../../evil")]
    [InlineData("save/..")]
    [InlineData("/etc/passwd")]
    [InlineData("\\\\server\\share\\x")]
    [InlineData("\\windows\\system32")]
    [InlineData("C:\\windows\\system.ini")]
    [InlineData("c:/x")]
    [InlineData("save/a:b")]
    [InlineData("save/x*y")]
    [InlineData("save/a|b")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("...")]
    public void ForbiddenPathsAreRejected(string path)
    {
        Assert.Throws<GamePathException>(() => _sb.ResolveExisting(path));
        Assert.Throws<GamePathException>(() => _sb.ResolveWritable(path));
    }

    [Fact]
    public void ReadsComeFromTheGameRootAndTheSaveFallback()
    {
        Assert.Equal(Path.Combine(_tmp, "game", "parts", "engines", "A.rpk"), _sb.ResolveExisting(".\\PARTS/Engines\\a.rpk"));
        Assert.Null(_sb.ResolveExisting("parts/engines/missing.rpk"));
        // the shipped save/ content is readable until the save root has its own copy
        Assert.Equal(Path.Combine(_tmp, "game", "save", "controls", "shipped"), _sb.ResolveExisting("save/controls/shipped"));
        var w = _sb.ResolveWritable("save/controls/shipped");
        Assert.StartsWith(Path.Combine(_tmp, "saves"), w);
        Directory.CreateDirectory(Path.GetDirectoryName(w)!);
        File.WriteAllText(w, "mine");
        Assert.Equal(w, _sb.ResolveExisting("SAVE\\controls\\shipped"));
    }

    [Fact]
    public void WritesAreConfinedToSaveAndScreenshots()
    {
        Assert.StartsWith(Path.Combine(_tmp, "saves"), _sb.ResolveWritable("save/temp/CopyCar"));
        Assert.StartsWith(_sb.ScreenshotRoot, _sb.ResolveWritable("screenshots\\shot0001.bmp"));
        Assert.Throws<GamePathException>(() => _sb.ResolveWritable("parts/engines/A.rpk"));
        Assert.Throws<GamePathException>(() => _sb.ResolveWritable("save")); // the mount root itself
        Assert.True(_sb.IsWritable("save/x"));
        Assert.False(_sb.IsWritable("cars/x"));
    }

    [Fact]
    public void SymlinksCannotLeaveTheSandbox()
    {
        var outside = Path.Combine(_tmp, "outside");
        Directory.CreateDirectory(outside);
        File.WriteAllText(Path.Combine(outside, "secret"), "s");
        try
        {
            Directory.CreateSymbolicLink(Path.Combine(_tmp, "saves", "link"), outside);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            return; // cannot create symlinks here
        }

        Assert.Throws<GamePathException>(() => _sb.ResolveExisting("save/link/secret"));
        Assert.Throws<GamePathException>(() => _sb.ResolveWritable("save/link/new"));
    }

    [Fact]
    public void ListingMatchesWindowsWildcardsCaseInsensitively()
    {
        Assert.Equal(["A.rpk", "b.RPK"], _sb.List("parts\\engines\\*.rpk", ListKind.FilesAndDirs));
        Assert.Equal(["A.rpk", "b.RPK", "c.txt"], _sb.List("parts/engines/*.*", ListKind.FilesOnly));
        Assert.Equal(["engines"], _sb.List("parts/*", ListKind.DirsOnly));
        Assert.Empty(_sb.List("parts/engines/?.TXT", ListKind.FilesOnly)!.Except(["c.txt"]));
        Assert.Null(_sb.List("nothing/here/*.*", ListKind.FilesAndDirs));
        Assert.Throws<GamePathException>(() => _sb.List("par*/x/*.*", ListKind.FilesAndDirs));
        Assert.Equal(["controls"], _sb.List("save/*", ListKind.FilesAndDirs)); // falls back to the shipped save dir
    }

    [Theory]
    [InlineData("*.png", "a.PNG", true)]
    [InlineData("*.png", "a.png.bak", false)]
    [InlineData("PlayerCar*", "PlayerCar", true)]
    [InlineData("CopyCar.*", "CopyCar.cfg", true)]
    [InlineData("CopyCar.*", "CopyCar", false)]
    [InlineData("?b*c", "abXXc", true)]
    [InlineData("*.*", "noext", true)]
    public void WildcardMatching(string mask, string name, bool expected) => Assert.Equal(expected, GameSandbox.Matches(mask, name));

    [Fact]
    public void SdatRoundTripsAndHasTheMeasuredHeader()
    {
        var data = SdatWriter.Build(w =>
        {
            w.WriteInt(unchecked((int)0x87654321));
            w.WriteInt(-5);
            w.WriteFloat(3.25f);
            w.WriteString("SysKeyboard");
            w.WriteString("");
            w.WriteString(null);
            w.WriteString("caf\u00e9");
        });
        Assert.Equal("SDAT", System.Text.Encoding.ASCII.GetString(data, 0, 4));
        Assert.Equal([0x00, 0x01, 0x02, 0x00], data[4..8]);
        Assert.Equal([0x21, 0x43, 0x65, 0x87], data[8..12]);
        Assert.Equal([12, 0, 0, 0], data[20..24]); // "SysKeyboard" -> length 11 + NUL
        var r = new SdatReader(data);
        Assert.True(r.Valid);
        Assert.Equal(unchecked((int)0x87654321), r.ReadInt());
        Assert.Equal(-5, r.ReadInt());
        Assert.Equal(3.25f, r.ReadFloat());
        Assert.Equal("SysKeyboard", r.ReadString());
        Assert.Equal(string.Empty, r.ReadString());
        Assert.Null(r.ReadString());
        Assert.Equal("caf\u00e9", r.ReadString());
        Assert.Equal(0, r.Remaining);
        Assert.False(r.Truncated);
        Assert.Equal(0, r.ReadInt());
        Assert.True(r.Truncated);
    }

    [Fact]
    public void BadHeadersAndTruncatedStringsAreHandled()
    {
        Assert.False(new SdatReader([1, 2, 3]).Valid);
        Assert.False(new SdatReader("XDAT\0\u0001\u0002\0"u8.ToArray()).Valid);
        var data = SdatWriter.Build(w => w.WriteString("hello"))[..^3];
        var r = new SdatReader(data);
        Assert.Equal("hel", r.ReadString());
        Assert.True(r.Truncated);
    }

    [Fact]
    public void SaveFormatsIdentifyFilesAndCareerLoadRule()
    {
        var ok = SdatWriter.Build(w =>
        {
            w.WriteInt(SaveFormats.CareerId);
            w.WriteInt(SaveFormats.CareerVersion);
        });
        Assert.Equal(SaveKind.Career, SaveFormats.Probe(ok)!.Value.Kind);
        Assert.True(SaveFormats.IsLoadableCareer(ok));
        var old = SdatWriter.Build(w =>
        {
            w.WriteInt(SaveFormats.CareerId);
            w.WriteInt(19);
        });
        Assert.False(SaveFormats.IsLoadableCareer(old));
        Assert.Equal(0x87654321u, unchecked((uint)SaveFormats.CareerId));
        Assert.Equal(unchecked((int)0xFEDCBA98), SaveFormats.OptionsId);
        Assert.Equal(unchecked((int)0x88664422), SaveFormats.VehicleId);
        Assert.Equal(unchecked((int)0x82549867), SaveFormats.SkinId);
        Assert.Equal(SaveKind.ControlSet, SaveFormats.KindOf(SaveFormats.ControlSetId));
        Assert.Null(SaveFormats.Probe([1, 2]));
    }

    [Fact]
    public void ControlSetRoundTrips()
    {
        var f = new ControlSetFile();
        f.Devices.AddRange(["SysKeyboard", "Pad"]);
        f.Bindings.Add(new(1, 28, 0, 200, 0, 1, 0, 1, 0.1f));
        f.Smoothing.Add(new(28, 0.1f, 0.2f, 0.3f, 0.4f, 2.5f));
        var back = ControlSetFile.Parse(f.ToBytes())!;
        Assert.Equal(f.Devices, back.Devices);
        Assert.Equal(f.Bindings, back.Bindings);
        Assert.Equal(f.Smoothing, back.Smoothing);
        Assert.Empty(back.Trailer);
    }
}
