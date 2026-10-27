using Slrr.Core;
using Slrr.Formats.Instances;
using Slrr.Formats.Rpak;
using Slrr.Formats.Tufa;
using Slrr.Vm;
using Xunit;

namespace Slrr.Engine.Resources.Tests;

/// <summary>Tests on the shipped game data ($SLRR_GAME_ROOT or the repository checkout); pass vacuously without it.</summary>
public class CorpusTests
{
    private static readonly Lazy<string?> Root = new(FindRoot);

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

    private static (Slrr.Vm.Vm Vm, ResourceEngine Engine)? NewVm(bool stub = true)
    {
        if (Root.Value is not { } root)
        {
            return null;
        }

        var game = new GameFileSystem(root);
        var engine = new ResourceEngine(game);
        var reg = NativeRegistry.CreateDefault().Add(new ResourceNatives(engine)).Add(new ArchiveNatives(engine));
        if (stub)
        {
            reg.Fallback = NativeRegistry.DefaultStub;
        }

        var vm = new Slrr.Vm.Vm(new VmClassPath(game), reg);
        engine.Attach(vm);
        return (vm, engine);
    }

    [Fact]
    public void GametypeSurveyReproducesTheMeasuredCounts()
    {
        if (Root.Value is not { } root)
        {
            return;
        }

        var s = GametypeRegistry.Survey(new GameFileSystem(root), out _);
        Assert.Equal(87, s.Archives);
        Assert.Equal(1616, s.TextEntriesWithScriptOrNative);
        Assert.Equal(1574, s.NativeEntries);
        Assert.Equal(1502, s.NativeKinds["part"]);
        Assert.Equal(45, s.NativeKinds["car"]);
        Assert.Equal(1504, s.NativeBindings[ScriptBinding.Own]);
        Assert.Equal(13, s.NativeBindings[ScriptBinding.Inherited]);
        Assert.Equal(57, s.NativeBindings[ScriptBinding.None]);
    }

    /// <summary>Every resource literal of every shipped class, resolved with the engine mapping.</summary>
    [Fact]
    public void EveryShippedResourceLiteralResolves()
    {
        if (Root.Value is not { } root)
        {
            return;
        }

        var game = new GameFileSystem(root);
        var table = new ArchiveTable(game);
        var tree = new ResourceTree(table);
        var literals = new HashSet<(string, uint)>();
        foreach (var file in game.Enumerate(".class"))
        {
            foreach (var c in TufaFile.Read(file))
            {
                foreach (var k in c.Constants.Where(k => k.Type == TufaConstantType.Resource))
                {
                    literals.Add((c.Constants[(int)k.A].Text!, k.B));
                }
            }
        }

        var zero = literals.Where(l => l.Item2 != 0 && table.ResolveLiteral(l.Item1, l.Item2) == 0).ToList();
        var noNode = literals.Where(l => table.ResolveLiteral(l.Item1, l.Item2) is var id && id != 0 && tree.Get(id) is null)
            .Select(l => $"{l.Item1}:0x{l.Item2:X}").ToList();
        Assert.Equal(1769, literals.Count);
        Assert.Equal(EngineFacts.LiteralZeroIds, literals.Count(l => l.Item2 == 0)); // `system:0x0r` style literals are 0 by definition
        Assert.All(literals, l => Assert.Equal(0u, l.Item2 >> 16)); // no literal uses a reference slot
        // literals in archives that do not exist resolve to 0 (exe: 0x544170 fails, 0x538440 returns 0)
        Assert.All(zero, l => Assert.Null(game.Resolve(l.Item1)));
        Assert.True(zero.Count == EngineFacts.LiteralsInMissingArchives, string.Join("; ", zero));
        Assert.True(noNode.Count == EngineFacts.LiteralsWithoutEntry, string.Join("; ", noNode));
        // ids differ exactly when the archive differs (case-insensitive names share a slot)
        var byArchive = literals.GroupBy(l => table.Find(l.Item1)).ToList();
        Assert.All(byArchive, g => Assert.Single(g.Select(l => table.ResolveLiteral(l.Item1, l.Item2) >> 16).Where(x => x != 0).Distinct().DefaultIfEmpty()));
    }

    /// <summary>Placed objects of the map archives: their <c>gametype</c> resolves through the archive reference table.</summary>
    [Fact]
    public void MapInstanceGametypesResolveToGametypeEntries()
    {
        if (Root.Value is not { } root)
        {
            return;
        }

        var game = new GameFileSystem(root);
        var table = new ArchiveTable(game);
        var reg = new GametypeRegistry(new ResourceTree(table));
        int total = 0, resolved = 0, type8 = 0;
        var bad = new List<string>();
        foreach (var file in game.Enumerate(".rpk"))
        {
            var rpk = RpakArchive.Read(file);
            if (!rpk.Entries.Any(e => e.HasExtra))
            {
                continue;
            }

            var slot = table.EnsureLoaded(table.Register(Path.GetRelativePath(root, file).Replace('/', '\\')))!;
            foreach (var n in InstanceForest.Read(rpk).AllNodes())
            {
                if (n.Rsd?.GameType is not { } gt)
                {
                    continue;
                }

                total++;
                var id = reg.ResolveInstanceGametype(slot, gt);
                if (reg.Tree.Get(id) is { } node)
                {
                    resolved++;
                    if (node.Type == ResourceTypes.Game)
                    {
                        type8++;
                    }
                    else
                    {
                        bad.Add($"{slot.Name} 0x{gt:X8} -> {node} (type {node.Type})");
                    }
                }
                else
                {
                    bad.Add($"{slot.Name} 0x{gt:X8} -> {ResourceIds.Format(id)}");
                }
            }
        }

        Assert.Equal(31599, total); // every RSD record with a gametype line (29,026 placed objects + roots and inner nodes)
        Assert.Equal(total, resolved);
        Assert.True(total - type8 == EngineFacts.MapGametypesNotType8, $"{total} {resolved} {type8}: " + string.Join("; ", bad));
    }

    [Fact]
    public void ResourceRefNativesWalkTheTree()
    {
        if (NewVm() is not var (vm, e))
        {
            return;
        }

        var rr = vm.LoadClass(ResourceEngine.ResourceRefClass);
        var typGame = e.Archives.ResolveLiteral("system.rpk", 0x1);
        var r = vm.Construct(rr, Value.Int(typGame));
        Assert.Equal(typGame, vm.InvokeVirtual(r, "id").AsInt);
        Assert.Equal(ResourceTypes.Game, vm.InvokeVirtual(r, "type").AsInt);
        var kids = (VmArray)vm.InvokeVirtual(r, "getChildNodes").Ref!;
        Assert.Equal(new[] { 0x47, 0x1B, 0x4C, 0x15 }, kids.Items.Select(k => ResourceEngine.IdOf(k.Ref) & 0xFFFF));
        Assert.Equal(typGame, vm.InvokeVirtual(kids.Items[0].Ref!, "getParentID").AsInt);

        // GameRef.isScripted on gametype entries (Catalog/MWM_Items_Scan use)
        var gr = vm.LoadClass(ResourceEngine.GameRefClass);
        var config = vm.Construct(gr, Value.Int(e.Archives.ResolveLiteral("system.rpk", 0x47)));
        Assert.Equal(1, vm.InvokeVirtual(config, "isScripted").AsInt);
        Assert.Equal(1, vm.InvokeVirtual(config, "isScripted", Value.Obj(vm.NewString("java.lang.GameType"))).AsInt);
        Assert.Equal(0, vm.InvokeVirtual(config, "isScripted", Value.Obj(vm.NewString("java.game.parts.Part"))).AsInt);
        var camera = vm.Construct(gr, Value.Int(e.Archives.ResolveLiteral("system.rpk", 0x33)));
        Assert.Equal(0, vm.InvokeVirtual(camera, "isScripted").AsInt);
    }

    [Fact]
    public void GameRefCreateRunsTheScriptConstructorWithTheInstanceId()
    {
        if (NewVm() is not var (vm, e))
        {
            return;
        }

        // Dummy (system.rpk:0x1B, script java.util.resource.Dummy) created through the script-visible native
        var gr = vm.LoadClass(ResourceEngine.GameRefClass);
        var receiver = vm.Construct(gr);
        var type = vm.Construct(gr, Value.Int(e.Archives.ResolveLiteral("system.rpk", 0x1B)));
        var create = gr.Methods.Single(m => m.Name == "create" && m.Descriptor.EndsWith("Ljava.lang.GameType;", StringComparison.Ordinal));
        var obj = vm.Invoke(create, receiver, Value.Null, Value.Obj(type), Value.Obj(vm.NewString("1,2,3,0,0,0")), Value.Obj(vm.NewString("dummy"))).Ref!;
        Assert.Equal("java.util.resource.Dummy", obj.Class.Name);
        var id = ResourceEngine.IdOf(obj);
        Assert.True(ResourceIds.IsInstance(id));
        Assert.Equal(id, ResourceEngine.IdOf(receiver)); // receiver rebound (0x47d7b0)
        var node = e.Tree.Get(id)!;
        Assert.Same(obj, node.Script);
        Assert.Equal(e.RootId, node.ParentId); // null parent = world root
        Assert.Equal([1f, 2f, 3f], node.Pos);
        Assert.Same(obj, vm.InvokeVirtual(receiver, "getScriptInstance").Ref);
        Assert.Equal(type.Native is ResourceHandle h ? h.Id : 0, vm.InvokeVirtual(receiver, "getInfo", Value.Int(6)).AsInt); // GII_TYPE

        // wrong type: an ins_game entry is not a gametype
        vm.Invoke(create, receiver, Value.Null, Value.Obj(vm.Construct(gr, Value.Int(e.RootId))), Value.Null, Value.Null);
        Assert.Contains(e.Messages, m => m.Text.StartsWith("create: Wrong GameType! system.rpk:0x00000004", StringComparison.Ordinal));

        vm.InvokeVirtual(obj, "destroy");
        Assert.Null(e.Tree.Get(id));
    }

    [Fact]
    public void TimersAndEventsAreDeliveredToHandleEvent()
    {
        if (NewVm() is not var (vm, e))
        {
            return;
        }

        var dummy = vm.LoadClass("java.util.resource.Dummy");
        var obj = vm.NewObject(dummy);
        e.CreateNativeInstance(obj, 0, 0, null, "t");
        var node = e.Tree.Get(ResourceEngine.IdOf(obj))!;
        var gt = vm.LoadClass(ResourceEngine.GameTypeClass);
        vm.Invoke(gt.DeclaredMethod("setEventMask", "(I)V")!, obj, Value.Int(ResourceEngine.EventTime | ResourceEngine.EventCommand));
        vm.Invoke(gt.DeclaredMethod("addTimer", "(FI)V")!, obj, Value.Float(1f), Value.Int(7));
        Assert.Equal(ResourceEngine.EventTime | ResourceEngine.EventCommand, node.EventMask);
        Assert.Equal(0, e.Pump());
        vm.Time = 1f;
        Assert.Equal(1, e.Pump()); // GameType.handleEvent(GameRef,int,int) is an empty script body
        vm.RunUntilIdle();
        e.QueueEvent(node.Id, 0, ResourceEngine.EventCommand, "reset");
        Assert.Equal(1, e.Pump());
        Assert.Empty(e.Commands);

        // a pure native object (camera gametype) receives commands as NativeCommand records
        var cam = e.Create(null, 0, e.Archives.ResolveLiteral("system.rpk", 0x33), null, "cam", runScript: true);
        Assert.Null(cam);
        var camNode = e.Tree.Instances.Single(n => n.Name == "cam");
        Assert.Equal("camera", camNode.NativeKind);
        e.QueueEvent(camNode.Id, 0, ResourceEngine.EventCommand, "dist 2.5 4.6");
        e.Pump();
        Assert.Equal(new NativeCommand(camNode.Id, "camera", 0, "dist 2.5 4.6"), e.Commands.Single());
    }

    [Fact]
    public void BootStopsAtTheFirstMissingNativeOfAnotherArea()
    {
        if (Root.Value is not { } root)
        {
            return;
        }

        var r = Boot.Run(new BootOptions { Root = root, Frames = 0 });
        Assert.True(r.Completed("mount system.rpk"));
        Assert.True(r.Completed("VM bootstrap"));
        Assert.False(r.Completed("create Config"));
        Assert.Equal(new NativeKey("java.io.File", "open", "(I)I"), r.Missing.Single().Key);
        Assert.Contains("java.util.Config.loadConfig", r.Missing[0].FirstTrace[0], StringComparison.Ordinal);
    }

    [Fact]
    public void BootWithStubsReachesInitAndWithFindFileReachesFurther()
    {
        if (Root.Value is not { } root)
        {
            return;
        }

        var stub = Boot.Run(new BootOptions { Root = root, Stub = true, Frames = 0 });
        Assert.True(stub.Completed("create Config"));
        Assert.True(stub.Completed("Config contract"));
        Assert.Contains(stub.Archives, a => a.Name == "sl.rpk");
        Assert.Contains(stub.Missing, m => m.Key.ClassName == "java.io.FindFile");

        var game = new GameFileSystem(root);
        var io = Boot.Run(new BootOptions { Root = root, Stub = true, Frames = 30, Modules = [new TestFindFile(game)] });
        var text = new StringWriter();
        Boot.Print(io, text, verbose: true);
        Assert.True(io.Completed("SplashScreen") && io.Completed("frame loop"), text.ToString());
        Assert.True(io.Instances > stub.Instances);
        EngineFacts.LastBoot = io;
    }
}

/// <summary>Numbers measured on the shipped data by the tests (kept in one place for docs/engine/resources.md).</summary>
internal static class EngineFacts
{
    public const int LiteralsInMissingArchives = 1;
    public const int LiteralZeroIds = 1;
    public const int LiteralsWithoutEntry = 15;
    public const int MapGametypesNotType8 = 1; // maps\DamageTrack.rpk record 0x00000004 names its own render object (type 14)

    public static BootReport? LastBoot { get; set; }
}

/// <summary>
/// Test stand-in for java.io.FindFile (owned by the io area): lists files of a game directory matching <c>dir\*.ext</c>,
/// case-insensitive name order. Only here so the boot test can get past <c>System.rpkScan</c>.
/// </summary>
internal sealed class TestFindFile(GameFileSystem game) : INativeModule
{
    public void Register(NativeRegistry r)
    {
        r.Register("java.io.FindFile", "first", "(Ljava.lang.String;I)Ljava.lang.String;", c =>
        {
            var pattern = GameFileSystem.Normalize(c.String(0) ?? string.Empty);
            var slash = pattern.LastIndexOf('\\');
            var dir = slash < 0 ? string.Empty : pattern[..(slash + 1)];
            var ext = Path.GetExtension(pattern);
            var names = game.Enumerate(ext)
                .Select(f => Path.GetRelativePath(game.Root, f).Replace('/', '\\'))
                .Where(p => p.StartsWith(dir, StringComparison.OrdinalIgnoreCase) && p.IndexOf('\\', dir.Length) < 0)
                .Select(p => p[dir.Length..])
                .Order(StringComparer.OrdinalIgnoreCase)
                .ToList();
            c.Self.Native = new Queue<string>(names);
            return Next(c);
        });
        r.Register("java.io.FindFile", "next", "()Ljava.lang.String;", Next);
        r.Register("java.io.FindFile", "close", "()V", _ => Value.Void);
    }

    private static Value Next(NativeCall c) =>
        c.Self.Native is Queue<string> q && q.Count > 0 ? c.Str(q.Dequeue()) : Value.Null;
}
