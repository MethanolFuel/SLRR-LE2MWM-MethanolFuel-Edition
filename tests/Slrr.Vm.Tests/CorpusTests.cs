using Slrr.Core;
using Slrr.Formats.Tufa;
using Xunit;

namespace Slrr.Vm.Tests;

/// <summary>
/// Runs shipped script classes. Uses $SLRR_GAME_ROOT or the repository checkout; passes vacuously without game data.
/// </summary>
public class CorpusTests
{
    private static readonly Lazy<(string Root, VmClassPath Path)?> Game = new(() =>
    {
        var root = FindRoot();
        return root is null ? null : (root, new VmClassPath(new GameFileSystem(root)));
    });

    private static string? FindRoot()
    {
        var env = Environment.GetEnvironmentVariable("SLRR_GAME_ROOT");
        if (!string.IsNullOrEmpty(env) && File.Exists(Path.Combine(env, "sl.rpk")))
        {
            return env;
        }

        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Slrr.sln")) && File.Exists(Path.Combine(dir.FullName, "sl.rpk")))
            {
                return dir.FullName;
            }
        }

        return null;
    }

    private static Vm? NewVm(bool stubs = false)
    {
        if (Game.Value is not { } g)
        {
            return null;
        }

        var natives = NativeRegistry.CreateDefault();
        if (stubs)
        {
            natives.Fallback = NativeRegistry.DefaultStub;
        }

        return new Vm(g.Path, natives);
    }

    private static Value Static(Vm vm, string cls, string name, string desc, params Value[] args) =>
        vm.Invoke(vm.LoadClass(cls).Methods.Single(m => m.Name == name && m.Descriptor == desc), null, args);

    [Fact]
    public void EveryShippedBodyPassesTheStackShapeCheck()
    {
        if (Game.Value is not { } g)
        {
            return;
        }

        var report = new VmVerifyReport();
        foreach (var f in new GameFileSystem(g.Root).Enumerate(".class"))
        {
            foreach (var c in TufaFile.Read(f))
            {
                VmVerifier.Verify(c, report, f);
            }
        }

        Assert.Empty(report.Failures);
        Assert.True(report.Bodies >= 7000, $"only {report.Bodies} bodies");
        Assert.Equal(0, report.StandaloneLiterals);
    }

    [Fact]
    public void IntegerAndStringHelpers()
    {
        if (NewVm() is not { } vm)
        {
            return;
        }

        string? S(Value v) => vm.GetString(v.AsObject);
        Assert.Equal("1st", S(Static(vm, "java.lang.Integer", "toOrdinal", "(I)Ljava.lang.String;", Value.Int(1))));
        Assert.Equal("3rd", S(Static(vm, "java.lang.Integer", "toOrdinal", "(I)Ljava.lang.String;", Value.Int(3))));
        Assert.Equal("01:02:05.500", S(Static(vm, "java.lang.String", "timeToString", "(F)Ljava.lang.String;", Value.Float(3725.5f))));
        Assert.Equal("1 hour 2 minutes 5 seconds",
            S(Static(vm, "java.lang.String", "timeToAlphaString", "(FI)Ljava.lang.String;", Value.Float(3725.5f), Value.Int(16))));

        // Shipped bug, reproduced faithfully: the short form prints the minutes where the seconds belong.
        Assert.Equal("1 h 2 min 2 sec",
            S(Static(vm, "java.lang.String", "timeToAlphaString", "(FI)Ljava.lang.String;", Value.Float(3725.5f), Value.Int(0))));
        Assert.Equal(int.MaxValue, vm.LoadClass("java.lang.Integer").StaticValues[1].AsInt);
    }

    [Fact]
    public void VectorAndStringTokenizer()
    {
        if (NewVm() is not { } vm)
        {
            return;
        }

        var vec = vm.Construct(vm.LoadClass("java.util.Vector"));
        for (var i = 0; i < 25; i++)
        {
            vm.InvokeVirtual(vec, "addElement", Value.Obj(vm.NewString("e" + i)));
        }

        Assert.Equal(25, vm.InvokeVirtual(vec, "size").AsInt);
        var e7 = vm.InvokeVirtual(vec, "elementAt", Value.Int(7));
        Assert.Equal("e7", vm.GetString(e7.AsObject));
        Assert.Equal(7, vm.InvokeVirtual(vec, "indexOf", e7).AsInt);
        vm.InvokeVirtual(vec, "removeElementAt", Value.Int(0));
        Assert.Equal(6, vm.InvokeVirtual(vec, "indexOf", e7).AsInt);
        Assert.Equal("e24", vm.GetString(vm.InvokeVirtual(vec, "lastElement").AsObject));

        var tok = vm.Construct(vm.LoadClass("java.util.StringTokenizer"), Value.Obj(vm.NewString("alpha beta  gamma")));
        var n = vm.InvokeVirtual(tok, "countTokens").AsInt;
        var first = vm.GetString(vm.InvokeVirtual(tok, "nextToken").AsObject);
        Assert.Equal("alpha", first);
        Assert.True(n >= 3, $"countTokens = {n}");
    }

    [Fact]
    public void ScriptThreadSubclassRunsOnTheScheduler()
    {
        if (NewVm() is not { } vm)
        {
            return;
        }

        var worker = new ClassBuilder("test.Worker", "java.lang.Thread").Method("run", "()V", a =>
        {
            a.Float(0.5f).CallStatic(1, "java.lang.Thread", "sleep", "(F)V").D(Operator.Expr);
            a.Str("done at ").CallStatic(0, "java.lang.System", "currentTime", "()F").O(Operator.Add)
                .CallStatic(1, "java.lang.System", "log", "(Ljava.lang.String;)V").D(Operator.Expr);
            a.ReturnVoid();
        });
        vm.ClassPath.AddClass(worker.Build());
        var obj = vm.Construct(vm.LoadClass("test.Worker"));
        vm.InvokeVirtual(obj, "start");
        Assert.Equal(1, vm.InvokeVirtual(obj, "isAlive").AsInt);
        Assert.Equal(0, vm.RunUntilIdle());
        Assert.Equal(["done at 0.500"], vm.Log);
        Assert.Equal(0, vm.InvokeVirtual(obj, "isAlive").AsInt);
    }

    [Fact]
    public void EveryStaticInitializerRunsWithStubbedEngineNatives()
    {
        if (Game.Value is not { } g)
        {
            return;
        }

        var failures = new List<string>();
        foreach (var name in g.Path.AllScannedClassNames())
        {
            var vm = NewVm(stubs: true)!;
            try
            {
                vm.EnsureInitialized(vm.LoadClass(name));
            }
            catch (VmException ex)
            {
                failures.Add($"{name}: {ex.Message}");
            }
        }

        Assert.Empty(failures);
    }

    /// <summary>
    /// The DeadIslandTrack .class/.java pair from MW_Mod/ForModders.rar (line numbers match the source). Set
    /// SLRR_DEADISLAND_CLASS to the extracted .class to run it; the archive is not unpacked by the tests.
    /// </summary>
    [Fact]
    public void DeadIslandTrackFromForModders()
    {
        var file = Environment.GetEnvironmentVariable("SLRR_DEADISLAND_CLASS");
        if (string.IsNullOrEmpty(file) || !File.Exists(file) || NewVm() is null)
        {
            return;
        }

        // Without engine natives the static ResourceRef fails at the source line of its declaration (20).
        var strict = NewVm()!;
        strict.ClassPath.AddFile(file);
        var ex = Assert.Throws<VmNativeMissingException>(() => strict.EnsureInitialized(strict.LoadClass("java.game.DeadIslandTrack")));
        Assert.Equal("java.util.resource.ResourceRef.newNative()V", $"{ex.ClassName}.{ex.Name}{ex.Descriptor}");
        Assert.EndsWith("line 20", ex.ScriptTrace[^1]);

        var vm = NewVm(stubs: true)!;
        vm.ClassPath.AddFile(file);
        var cls = vm.LoadClass("java.game.DeadIslandTrack");
        vm.EnsureInitialized(cls);
        Assert.Equal("Dead Island", vm.GetString(cls.StaticValues[cls.FindField("MapName")!.Slot].AsObject));
        Assert.Equal("Author: Miran", vm.GetString(cls.StaticValues[cls.FindField("MapAuthor")!.Slot].AsObject));
        Assert.Equal(1.0f, cls.StaticValues[cls.FindField("TS_Version")!.Slot].AsFloat);

        var track = vm.Construct(cls);
        var pos = track.Fields[cls.FindField("posStart")!.Slot].AsObject!;
        float F(string n) => pos.Fields[pos.Class.FindField(n)!.Slot].AsFloat;
        Assert.Equal((30f, 5f, -30f), (F("x"), F("y"), F("z")));
    }
}
