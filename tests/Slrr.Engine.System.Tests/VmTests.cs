using Slrr.Vm;
using Xunit;

namespace Slrr.Engine.SystemLib.Tests;

/// <summary>The natives driven through the shipped File/FindFile/System classes in the VM.</summary>
public class VmTests
{
    [Fact]
    public void FileWriteThenReadRoundTripsEveryType()
    {
        using var h = VmHarness.Game();
        if (h is null)
        {
            return;
        }

        var w = h.NewFile("save/temp/roundtrip");
        Assert.Equal(1, h.Call(w, "open", Value.Int(1)).AsInt);
        Assert.Equal(1, h.Call(w, "write", Value.Int(unchecked((int)0x87654321))).AsInt);
        h.Call(w, "write", Value.Int(21));
        h.Call(w, "write", Value.Float(-1.5f));
        h.Call(w, "write", h.Str("Player One"));
        h.Call(w, "close");
        h.Call(w, "close"); // closing twice is safe

        var raw = File.ReadAllBytes(Path.Combine(h.SaveRoot, "temp", "roundtrip"));
        Assert.True(SdatFormat.HasHeader(raw));
        Assert.Equal(8 + 4 + 4 + 4 + 4 + 10 + 1, raw.Length);

        var r = h.NewFile("SAVE\\temp\\RoundTrip");
        Assert.Equal(1, h.Call(r, "open", Value.Int(0)).AsInt);
        Assert.Equal(unchecked((int)0x87654321), h.Call(r, "readInt").AsInt);
        Assert.Equal(21, h.Call(r, "readInt").AsInt);
        Assert.Equal(-1.5f, h.Call(r, "readFloat").AsFloat);
        Assert.Equal("Player One", h.Text(h.Call(r, "readString")));
        Assert.Equal(0, h.Call(r, "readInt").AsInt); // past the end
        h.Call(r, "close");
    }

    [Fact]
    public void OpenFailsOnMissingBadHeaderWrongModeAndEscapes()
    {
        using var h = VmHarness.Game();
        if (h is null)
        {
            return;
        }

        Assert.Equal(0, h.Call(h.NewFile("save/temp/nothing"), "open", Value.Int(0)).AsInt);
        Directory.CreateDirectory(Path.Combine(h.SaveRoot, "temp"));
        File.WriteAllText(Path.Combine(h.SaveRoot, "temp", "junk"), "not an sdat file");
        Assert.Equal(0, h.Call(h.NewFile("save/temp/junk"), "open", Value.Int(0)).AsInt);

        var f = h.NewFile("save/temp/modes");
        Assert.Equal(0, h.Call(f, "write", Value.Int(1)).AsInt); // not open
        h.Call(f, "open", Value.Int(1));
        Assert.Equal(0, h.Call(f, "readInt").AsInt);
        h.Call(f, "close");

        foreach (var bad in new[] { "..\\..\\evil", "/tmp/evil", "C:\\evil", "StreetLegal_Redline.exe", "parts/evil", "save/../x" })
        {
            Assert.Equal(0, h.Call(h.NewFile(bad), "open", Value.Int(1)).AsInt);
        }

        Assert.Equal(0, h.Call(h.NewFile("..\\..\\etc\\passwd"), "open", Value.Int(0)).AsInt);
        Assert.Equal(0, h.Static("java.io.File", "exists", h.Str("../sl.rpk")).AsInt);
        Assert.Equal(0, h.Static("java.io.File", "delete", h.Str("sl.rpk")).AsInt);
        Assert.True(File.Exists(Path.Combine(h.Sandbox.GameRoot, "sl.rpk")));
        Assert.True(h.System.Rejections.Count >= 6);
    }

    [Fact]
    public void StaticFileOperationsAndScriptMaskOverloads()
    {
        using var h = VmHarness.Game();
        if (h is null)
        {
            return;
        }

        foreach (var n in new[] { "PlayerCar1", "PlayerCar2", "other" })
        {
            var f = h.NewFile("save/temp/" + n);
            h.Call(f, "open", Value.Int(1));
            h.Call(f, "write", Value.Int(7));
            h.Call(f, "close");
        }

        Assert.Equal(1, h.Static("java.io.File", "exists", h.Str("save/temp/PlayerCar1")).AsInt);
        Assert.Equal(1, h.Static("java.io.File", "exists", h.Str("save/temp")).AsInt);

        // File.copy(path, mask, path2) and File.delete(path, mask) are script code on top of FindFile
        h.Static("java.io.File", "copy", h.Str("save/temp/"), h.Str("PlayerCar*"), h.Str("save/carrer/x/"));
        Assert.Equal(["PlayerCar1", "PlayerCar2"], Directory.GetFiles(Path.Combine(h.SaveRoot, "carrer", "x")).Select(Path.GetFileName).Order().ToArray());
        h.Static("java.io.File", "delete", h.Str("save/temp/"), h.Str("PlayerCar*"));
        Assert.Equal(["other"], Directory.GetFiles(Path.Combine(h.SaveRoot, "temp")).Select(Path.GetFileName).ToArray());

        h.Static("java.io.File", "move", h.Str("save/carrer/x/"), h.Str("*"), h.Str("save/carrer/y/"));
        Assert.Equal(2, Directory.GetFiles(Path.Combine(h.SaveRoot, "carrer", "y")).Length);
        Assert.Empty(Directory.GetFiles(Path.Combine(h.SaveRoot, "carrer", "x")));

        Assert.Equal(1, h.Static("java.io.File", "move", h.Str("save/temp/other"), h.Str("save/temp/renamed")).AsInt);
        Assert.Equal(1, h.Static("java.io.File", "copy", h.Str("save/temp/renamed"), h.Str("save/temp/copy")).AsInt);
        Assert.Equal(1, h.Static("java.io.File", "delete", h.Str("save/temp/copy")).AsInt);
        Assert.Equal(0, h.Static("java.io.File", "delete", h.Str("save/temp/copy")).AsInt);
        Assert.Equal(0, h.Static("java.io.File", "copy", h.Str("save/temp/absent"), h.Str("save/temp/z")).AsInt);
        Assert.NotEqual(0, h.Static("java.io.File", "TimeHigh", h.Str("save/temp/renamed")).AsInt);
    }

    [Fact]
    public void FindFileListsBareNamesAndRespectsFlags()
    {
        using var h = VmHarness.Game();
        if (h is null)
        {
            return;
        }

        var names = new List<string>();
        var ff = h.New("java.io.FindFile");
        for (var n = h.Call(ff, "first", h.Str("parts\\engines\\*.rpk"), Value.Int(1)); !n.IsNull; n = h.Call(ff, "next"))
        {
            names.Add(h.Text(n)!);
        }

        h.Call(ff, "close");
        Assert.NotEmpty(names);
        Assert.All(names, n => Assert.EndsWith(".rpk", n, StringComparison.OrdinalIgnoreCase));
        Assert.Equal(names.Order(StringComparer.OrdinalIgnoreCase), names);

        var dirs = h.Call(h.New("java.io.FindFile"), "first", h.Str("parts/eng*"), Value.Int(2));
        Assert.Equal("engines", h.Text(dirs));
        Assert.True(h.Call(h.New("java.io.FindFile"), "first", h.Str("nodir/*.*"), Value.Int(0)).IsNull);
    }

    [Fact]
    public void SystemNativesCoverTheMissingFourteen()
    {
        using var h = VmHarness.Game();
        if (h is null)
        {
            return;
        }

        string[] expected =
        [
            "buildNumber()I", "info(I)Ljava.lang.String;", "compileAll(Ljava.lang.String;)I", "timeWarp(F)F", "syncGameTime(F)V",
            "setMeasure(F)V", "getConfigOptions()V", "netHost()I", "netJoin()I", "netLeave()I", "openLib(Ljava.lang.String;)I",
            "isLoadingReset()V", "isLoading()I", "setLdPriority(I)V",
        ];
        var plain = NativeRegistry.CreateDefault();
        foreach (var e in expected)
        {
            var i = e.IndexOf('(');
            Assert.False(plain.Contains("java.lang.System", e[..i], e[i..]), e);
            Assert.True(h.Natives.Contains("java.lang.System", e[..i], e[i..]), e);
        }

        Assert.Equal(606, h.Static("java.lang.System", "buildNumber").AsInt);
        Assert.Equal(1f, h.Static("java.lang.System", "timeWarp", Value.Float(-1f)).AsFloat); // query
        Assert.Equal(1f, h.Static("java.lang.System", "timeWarp", Value.Float(0f)).AsFloat); // returns the previous factor
        Assert.Equal(0f, h.Static("java.lang.System", "timeWarp", Value.Float(-1f)).AsFloat);
        Assert.Equal(0f, h.Static("java.lang.System", "timeWarp", Value.Float(2f)).AsFloat);
        Assert.Equal(2f, h.System.TimeWarp);
        h.Static("java.lang.System", "syncGameTime", Value.Float(43200f));
        Assert.Equal(43200f, h.System.GameTime);
        h.Static("java.lang.System", "setMeasure", Value.Float(1600f));
        Assert.Equal(1600f, h.System.Measure);
        Assert.Equal(0, h.Static("java.lang.System", "isLoading").AsInt);
        h.System.IsLoading = () => true;
        Assert.Equal(1, h.Static("java.lang.System", "isLoading").AsInt);
        h.Static("java.lang.System", "setLdPriority", Value.Int(1));
        Assert.Equal(1, h.System.LoadPriority);
        Assert.Equal(0, h.Static("java.lang.System", "netHost").AsInt);

        Assert.Equal(1, h.Static("java.lang.System", "openLib", h.Str("MW_Mod.rpk")).AsInt);
        Assert.Equal(1, h.Static("java.lang.System", "openLib", h.Str("MW_Mod.rpk")).AsInt);
        Assert.Equal(0, h.Static("java.lang.System", "openLib", h.Str("nonexistent.rpk")).AsInt);
        Assert.Equal(0, h.Static("java.lang.System", "openLib", h.Str("..\\evil.rpk")).AsInt);
        Assert.Equal(["MW_Mod.rpk"], h.System.MountedLibs);

        var refreshed = false;
        h.System.ConfigOptionsRefresh = _ => refreshed = true;
        h.Static("java.lang.System", "getConfigOptions");
        Assert.True(refreshed);
    }

    [Fact]
    public void RpkScanMountsEveryArchiveOfADirectory()
    {
        using var h = VmHarness.Game();
        if (h is null)
        {
            return;
        }

        h.Static("java.lang.System", "rpkScan", h.Str("parts\\engines\\"));
        Assert.NotEmpty(h.System.MountedLibs);
        Assert.All(h.System.MountedLibs, l => Assert.StartsWith("parts\\engines\\", l));
    }

    [Fact]
    public void ControlSetLoadsTheShippedControlFile()
    {
        using var h = VmHarness.Game();
        if (h is null)
        {
            return;
        }

        var bytes = File.ReadAllBytes(Path.Combine(h.Sandbox.GameRoot, "save", "controls", "active_control_set"));
        var model = ControlSetFile.Parse(bytes)!;
        Assert.Equal(5, model.Devices.Count);
        Assert.Equal(78, model.Bindings.Count);
        Assert.Equal(4, model.Smoothing.Count);
        Assert.Equal(12, model.Trailer.Length);
        Assert.Equal(new ControlBinding(1, 28, 0, 200, 0f, 1f, 0f, 1f, 0f), model.Bindings[0]);
        Assert.Equal("SysKeyboard", model.Devices[0]);
        Assert.Equal(bytes[..^12], model.ToBytes()); // lossless apart from the trailer

        // ... and the shipped script reads the same file through the native File class
        h.StubDevices([.. model.Devices]);
        var cs = h.New("java.io.ControlSet");
        h.Call(cs, "load", h.Str("save/controls/active_control_set"));
        var cls = cs.Class;
        int[] Ints(string f) => ((VmArray)cs.Fields[cls.FindField(f)!.Slot].Ref!).Items.Select(v => v.AsInt).ToArray();
        float[] Floats(string f) => ((VmArray)cs.Fields[cls.FindField(f)!.Slot].Ref!).Items.Select(v => v.AsFloat).ToArray();
        Assert.Equal(model.Bindings.Select(b => b.Device), Ints("deviceID").Take(78)); // names matched via interned strings
        Assert.Equal(model.Bindings.Select(b => b.Group), Ints("group").Take(78));
        Assert.Equal(model.Bindings.Select(b => b.VirtualAxis), Ints("vaxisID").Take(78));
        Assert.Equal(model.Bindings.Select(b => b.DeviceAxis), Ints("axisID").Take(78));
        Assert.Equal(model.Bindings.Select(b => b.ToMax), Floats("to_max").Take(78));
        var vasp = cs.Fields[cls.FindField("vasp")!.Slot].Ref!;
        Assert.Equal(model.Smoothing.Count, h.Call(vasp, "size").AsInt);
    }

    [Fact]
    public void ControlSetSaveThenLoadRoundTripsThroughTheVm()
    {
        using var h = VmHarness.Game();
        if (h is null)
        {
            return;
        }

        var original = ControlSetFile.Parse(File.ReadAllBytes(Path.Combine(h.Sandbox.GameRoot, "save", "controls", "active_control_set")))!;
        h.StubDevices([.. original.Devices]);
        var cs = h.New("java.io.ControlSet");
        h.Call(cs, "load", h.Str("save/controls/active_control_set"));
        h.Call(cs, "save", h.Str("save/controls/copy"));
        var written = ControlSetFile.Parse(File.ReadAllBytes(Path.Combine(h.SaveRoot, "controls", "copy")))!;
        Assert.Equal(original.Bindings, written.Bindings);
        Assert.Equal(original.Smoothing, written.Smoothing);
        Assert.Empty(written.Trailer);
    }

    [Fact]
    public void GameLogicFileCheckAcceptsCareerSavesByMagicAndVersion()
    {
        using var h = VmHarness.Game();
        if (h is null)
        {
            return;
        }

        string Check(int id, int version)
        {
            var dir = Path.Combine(h.SaveRoot, "carrer", $"p{id & 0xff}-{version}");
            Directory.CreateDirectory(dir);
            File.WriteAllBytes(Path.Combine(dir, "main"), SdatWriter.Build(w =>
            {
                w.WriteInt(id);
                w.WriteInt(version);
            }));
            return $"save/carrer/p{id & 0xff}-{version}";
        }

        int FileCheck(string dir) => h.Static("java.game.GameLogic", "fileCheck", h.Str(dir)).AsInt;

        var good = FileCheck(Check(SaveFormats.CareerId, SaveFormats.CareerVersion));
        var min = FileCheck(Check(SaveFormats.CareerId, SaveFormats.CareerMinLoadVersion));
        var old = FileCheck(Check(SaveFormats.CareerId, SaveFormats.CareerMinLoadVersion - 1));
        var wrong = FileCheck(Check(SaveFormats.VehicleId, SaveFormats.CareerVersion));
        Assert.Equal(1, good);
        Assert.Equal(1, min);
        Assert.Equal(0, old);
        Assert.Equal(0, wrong);
        Assert.Equal(0, FileCheck("save/carrer/missing"));
    }

    private static void SetField(VmObject o, string name, Value v) => o.Fields[o.Class.FindField(name)!.Slot] = v;

    private static Value GetField(VmObject o, string name) => o.Fields[o.Class.FindField(name)!.Slot];

    /// <summary>A GameLogic with just enough state for save/load: one player and no other racers or market cars.</summary>
    private static VmObject PreparePlayer(VmHarness h)
    {
        var gl = h.Vm.LoadClass("java.game.GameLogic");
        h.Vm.EnsureInitialized(gl);
        void Static(string f, Value v)
        {
            var fd = gl.FindField(f)!;
            fd.Owner.StaticValues[fd.Slot] = v;
        }

        var player = h.New("java.game.Player");
        var racers = h.Vm.NewArray("Ljava.game.Racer;", 1);
        racers.Items[0] = Value.Obj(player);
        Static("player", Value.Obj(player));
        Static("speedymen", Value.Obj(racers));
        Static("carDescriptors_New", Value.Obj(h.Vm.NewArray("Ljava.game.VehicleDescriptor;", 0)));
        Static("carDescriptors_Used", Value.Obj(h.Vm.NewArray("Ljava.game.VehicleDescriptor;", 0)));
        return player;
    }

    [Fact]
    public void GameLogicSaveWritesTheCareerLayoutAndLoadReadsItBack()
    {
        using var h = VmHarness.Game();
        if (h is null)
        {
            return;
        }

        var player = PreparePlayer(h);
        var character = h.New("java.util.resource.ResourceRef");
        h.System.ResourceIdOf = (_, o) => o is null ? 0 : 4242;
        SetField(player, "character", Value.Obj(character));
        SetField(player, "money", Value.Int(12345));
        SetField(player, "prestige", Value.Float(0.75f));
        SetField(player, "flags", Value.Int(5));
        SetField(player, "name", h.Str("Tester"));
        SetField(player, "races_won", Value.Int(3));

        Assert.Equal(1, h.Static("java.game.GameLogic", "save", h.Str("save/carrer/Tester-1/")).AsInt);

        var data = File.ReadAllBytes(Path.Combine(h.SaveRoot, "carrer", "Tester-1", "main"));
        Assert.Equal((SaveKind.Career, SaveFormats.CareerId, SaveFormats.CareerVersion), SaveFormats.Probe(data));
        Assert.True(SaveFormats.IsLoadableCareer(data));
        var r = new SdatReader(data);
        r.ReadInt();
        r.ReadInt();
        Assert.Equal(4242, r.ReadInt()); // player.character (resource id)
        Assert.Equal(12345, r.ReadInt()); // money
        Assert.Equal(0.75f, r.ReadFloat()); // prestige
        Assert.Equal(0, r.ReadInt()); // player's index in speedymen
        Assert.Equal(5, r.ReadInt()); // flags
        Assert.Equal("Tester", r.ReadString());
        Assert.Equal(3, r.ReadInt()); // races_won
        Assert.Equal(1, h.Static("java.game.GameLogic", "fileCheck", h.Str("save/carrer/Tester-1")).AsInt);

        SetField(player, "money", Value.Int(0));
        SetField(player, "name", Value.Null);
        SetField(player, "flags", Value.Int(0));
        SetField(player, "races_won", Value.Int(0));
        Assert.Equal(0, h.Static("java.game.GameLogic", "load", h.Str("save/carrer/Tester-1/")).AsInt);
        Assert.Equal(12345, GetField(player, "money").AsInt);
        Assert.Equal(0.75f, GetField(player, "prestige").AsFloat);
        Assert.Equal("Tester", h.Text(GetField(player, "name")));
        Assert.Equal(5, GetField(player, "flags").AsInt);
        Assert.Equal(3, GetField(player, "races_won").AsInt);

        // a missing save is a load error, not a crash
        Assert.NotEqual(0, h.Static("java.game.GameLogic", "load", h.Str("save/carrer/Nobody-1/")).AsInt);
    }

    [Fact]
    public void ResourceReferencesRoundTripThroughFiles()
    {
        using var h = VmHarness.Game();
        if (h is null)
        {
            return;
        }

        var rr = h.New("java.util.resource.ResourceRef");
        h.System.ResourceIdOf = (_, o) => o is null ? 0 : 0x1234;
        h.System.ResolveResourceId = id => id + 1;
        var w = h.NewFile("save/temp/resid");
        h.Call(w, "open", Value.Int(1));
        h.Call(w, "write", Value.Obj(rr));
        h.Call(w, "write", Value.Null);
        h.Call(w, "close");
        var r = h.NewFile("save/temp/resid");
        h.Call(r, "open", Value.Int(0));
        Assert.Equal(0x1235, h.Call(r, "readResID").AsInt);
        Assert.Equal(1, h.Call(r, "readResID").AsInt); // null reference stored as 0, then resolved
        h.Call(r, "close");
    }

    [Fact]
    public void ConfigSavesAndLoadsTheOptionsFile()
    {
        using var h = VmHarness.Game();
        if (h is null)
        {
            return;
        }

        var cls = h.Vm.LoadClass("java.util.Config");
        h.Vm.EnsureInitialized(cls);
        var config = h.Vm.NewObject(cls);
        void SetStatic(string f, Value v)
        {
            var fd = cls.FindField(f)!;
            fd.Owner.StaticValues[fd.Slot] = v;
        }

        Value GetStatic(string f)
        {
            var fd = cls.FindField(f)!;
            return fd.Owner.StaticValues[fd.Slot];
        }

        var defaultX = GetStatic("video_x").AsInt;
        SetStatic("video_x", Value.Int(1920));
        SetStatic("video_y", Value.Int(1080));
        h.Call(config, "saveConfig", h.Str("save/game/options"));
        var data = File.ReadAllBytes(Path.Combine(h.SaveRoot, "game", "options"));
        var probe = SaveFormats.Probe(data)!.Value;
        Assert.Equal(SaveKind.Options, probe.Kind);
        Assert.Equal(SaveFormats.OptionsVersion, probe.Version);
        Assert.NotEqual(1920, defaultX);

        SetStatic("video_x", Value.Int(defaultX));
        SetStatic("video_y", Value.Int(1));
        h.Call(config, "loadConfig", h.Str("save/game/options"));
        Assert.Equal(1920, GetStatic("video_x").AsInt);
        Assert.Equal(1080, GetStatic("video_y").AsInt);
    }
}
