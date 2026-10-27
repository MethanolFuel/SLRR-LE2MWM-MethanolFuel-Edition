using Slrr.Formats.Rpak;
using Xunit;

namespace Slrr.Engine.Resources.Tests;

/// <summary>Engine-data-free tests on synthetic archives.</summary>
public sealed class UnitTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "slrr-res-" + Guid.NewGuid().ToString("N"));

    public UnitTests()
    {
        Directory.CreateDirectory(Path.Combine(_dir, "sub"));
        // a.rpk: refs [b.rpk, sub\c.rpk, missing.rpk]; entries 0x1 (type 8, text), 0x2 child of 0x1, 0x3 child of b.rpk:0x5
        Write("a.rpk", ["b.rpk", "sub\\c.rpk", "missing.rpk"],
            (0, 0x1, 8, "root", "script java.lang.GameType\r\n"),
            (0x1, 0x2, 8, "kid", "native part x.cfg\r\n"),
            (0x00010005, 0x3, 8, "crosskid", string.Empty));
        Write("b.rpk", [], (0, 0x5, 8, "broot", "script java.game.Foo\r\n"));
        Write("sub\\c.rpk", ["a.rpk"], (0, 0x7, 14, "mesh", string.Empty));
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string? Resolve(string name)
    {
        var p = Path.Combine(_dir, name.Replace('\\', Path.DirectorySeparatorChar));
        return File.Exists(p) ? p : null;
    }

    private void Write(string name, string[] refs, params (uint Super, uint Id, byte Type, string Alias, string Text)[] entries)
    {
        var rpk = new RpakArchive();
        for (var i = 0; i < refs.Length; i++)
        {
            rpk.References.Add(RpakReference.Create((ushort)(i + 1), refs[i]));
        }

        var data = new List<byte>();
        var start = 16 + (64 * refs.Length) + 16 + entries.Sum(e => 23 + e.Alias.Length + 1);
        foreach (var (super, id, type, alias, text) in entries)
        {
            var bytes = System.Text.Encoding.Latin1.GetBytes(text);
            rpk.Entries.Add(new RpakEntry
            {
                SuperId = super,
                Id = id,
                TypeOfEntry = type,
                Offset = (uint)(start + data.Count),
                Size = (uint)bytes.Length,
                AliasBytes = System.Text.Encoding.Latin1.GetBytes(alias + "\0"),
            });
            data.AddRange(bytes);
        }

        rpk.DataRegion = [.. data];
        File.WriteAllBytes(Path.Combine(_dir, name.Replace('\\', Path.DirectorySeparatorChar)), rpk.Write());
    }

    [Fact]
    public void LiteralsMapToArchiveIndexAndLocalId()
    {
        var t = new ArchiveTable(Resolve);
        Assert.Equal(0x00000001, t.ResolveLiteral("a.rpk", 0x1)); // a = 0; loading it registers b=1, sub\c=2, missing=3
        Assert.Equal(["a.rpk", "b.rpk", "sub\\c.rpk", "missing.rpk"], t.Slots.Select(s => s.Name));
        Assert.Equal(ArchiveState.Registered, t.Slots[1].State); // references are registered, not loaded
        Assert.Equal(0x00010005, t.ResolveLiteral("B.RPK", 0x5)); // _stricmp
        Assert.Equal(0x00020007, t.ResolveLiteral("sub\\c.rpk", 0x7));
        Assert.Equal(0x00010009, t.ResolveLiteral("a.rpk", 0x00010009)); // slot 1 of a.rpk = b.rpk
        Assert.Equal(0, t.ResolveLiteral("a.rpk", 0x00010000)); // local 0
        Assert.Equal(0, t.ResolveLiteral("a.rpk", 0x00090001)); // slot >= archive count
        Assert.Equal(0, t.ResolveLiteral("b.rpk", 0x00010001)); // b has no reference 1
        Assert.Equal(0, t.ResolveLiteral("missing.rpk", 0x1)); // file missing -> 0
        Assert.Equal(ArchiveState.Missing, t.Slots[3].State);
        Assert.Equal(0x00040007, t.ResolveLiteral("sub/c.rpk", 0x7)); // '/' is a different name for _stricmp: a second slot for the same file
        Assert.Equal(1, t.Mount("b.rpk"));
    }

    [Fact]
    public void TreeLinksParentsAcrossArchives()
    {
        var t = new ArchiveTable(Resolve);
        var tree = new ResourceTree(t);
        var a = t.ResolveLiteral("a.rpk", 0x1);
        Assert.Equal([0x2], tree.Children(a));
        Assert.Equal(0, tree.NextSibling(0x2));
        var b5 = ResourceIds.Make(1, 5);
        Assert.Equal([0x3], tree.Children(b5)); // a.rpk:0x3 has superId 0x00010005 = b.rpk:0x5
        Assert.Equal("broot", tree.Get(b5)!.Name); // looking the node up loads b.rpk
        var reg = new GametypeRegistry(tree);
        Assert.Equal(ScriptBinding.Own, reg.Get(a)!.Binding);
        var kid = reg.Get(0x2)!;
        Assert.Equal(("part", "x.cfg", ScriptBinding.Inherited, "java.lang.GameType"), (kid.NativeKind, kid.NativeArg, kid.Binding, kid.Script));
        Assert.Equal("java.game.Foo", reg.Get(0x3)!.Script); // inherited across archives
    }

    [Fact]
    public void InstanceIdsComeFromALifoOf128()
    {
        var tree = new ResourceTree(new ArchiveTable(_ => null));
        var first = tree.CreateInstance(ResourceTypes.InstanceGame, 0, "x", 0);
        Assert.Equal(unchecked((int)0xFFFF007F), first.Id);
        Assert.Equal(unchecked((int)0xFFFF007E), tree.AllocateInstanceId());
        for (var i = 0; i < 126; i++)
        {
            tree.AllocateInstanceId();
        }

        Assert.Equal(unchecked((int)0xFFFF00FF), tree.AllocateInstanceId());
        Assert.True(ResourceIds.IsInstance(first.Id));
        tree.Destroy(first);
        Assert.Equal(first.Id, tree.AllocateInstanceId()); // guess: destroyed numbers are reused first
    }

    [Fact]
    public void ScanfAndPartCommands()
    {
        var install = PartCommands.ParseInstall("install 0 -65409 12 -65410 3 0.5 -1.25 2e1")!;
        Assert.Equal(new InstallCommand(8, 0, -65409, 12, -65410, 3, (0.5f, -1.25f, 20f)), install);
        var noPos = PartCommands.ParseInstall("install 0 1 2 3 4")!;
        Assert.Equal(5, noPos.Fields);
        Assert.Null(noPos.Position);
        Assert.Null(PartCommands.ParseInstall("install 0 1 2"));
        Assert.Equal(new RemoveCommand(2, 0, 77), PartCommands.ParseRemove("remove 0 77"));
        Assert.Equal("install", PartCommands.Verb("  install 1 2"));

        var v = new List<object>();
        Assert.Equal(4, CScanf.Scan("look 5 1,2,3", "%*s %d %f,%f,%f", v));
        Assert.Equal([5, 1f, 2f, 3f], v);
        v.Clear();
        Assert.Equal(1, CScanf.Scan("fog 3 1 2", "%*s %d &f &f", v)); // the exe's typo stops after the int
        v.Clear();
        Assert.Equal(3, CScanf.Scan("mode 0 J 65596", "%*s %d %1s %d", v));
        Assert.Equal("J", v[1]);
    }

    [Fact]
    public void EntryTextIsKeyValueLines()
    {
        var e = new RpakEntry { TypeOfEntry = 8 };
        var t = EntryText.TryParse(e, "script  java.game.parts.Part\r\nnative part  a\\b.cfg\r\n\r\nnative car x\r\n"u8)!;
        Assert.Equal("java.game.parts.Part", t.Get("script"));
        Assert.Equal("part  a\\b.cfg", t.Get("native"));
        Assert.Null(EntryText.TryParse(e, "ICFG\0\x01"u8));
    }
}
