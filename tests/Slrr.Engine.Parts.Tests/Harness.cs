using Slrr.Core;
using Slrr.Formats.Cfg;
using Slrr.Vm;

namespace Slrr.Engine.Parts.Tests;

/// <summary>Finds the game data ($SLRR_GAME_ROOT or the repository checkout); corpus tests pass vacuously without it.</summary>
internal static class GameRoot
{
    private static readonly Lazy<string?> s_root = new(() =>
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
    });

    private static readonly Lazy<VmClassPath?> s_path = new(() => Root is null ? null : new VmClassPath(new GameFileSystem(Root)));

    public static string? Root => s_root.Value;

    public static VmClassPath? ClassPath => s_path.Value;

    /// <summary>The repository root (always available: docs live there).</summary>
    public static string Repo
    {
        get
        {
            for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            {
                if (Directory.Exists(Path.Combine(dir.FullName, "docs", "research")))
                {
                    return dir.FullName;
                }
            }

            throw new DirectoryNotFoundException("repository root with docs/research not found");
        }
    }
}

/// <summary>
/// A VM with the parts natives and minimal TEST-ONLY stand-ins for natives other areas own (ResourceRef/GameRef
/// handles, GameRef.queueEvent routed to <see cref="PartsWorld.ExecuteCommand"/>, GameRef.getInfo routed to
/// <see cref="PartsWorld.TryGetInfo"/>); every other native is a no-op stub. The stand-ins are deliberately trivial and
/// only exist so real part scripts can be driven headless.
/// </summary>
internal sealed class Harness
{
    private const string Res = "java.util.resource.ResourceRef";
    private const string Game = "java.util.resource.GameRef";
    private readonly Dictionary<VmObject, int> _handleIds = new(ReferenceEqualityComparer.Instance);

    private Harness(VmClassPath path)
    {
        World = new PartsWorld();
        var r = NativeRegistry.CreateDefault();
        r.Add(new PartsNatives(World));
        r.Register(Res, "newNative", "()V", _ => Value.Void);
        r.Register(Res, "deleteNative", "()V", _ => Value.Void);
        r.Register(Res, "set", "(I)V", c =>
        {
            _handleIds[c.Self] = c.Int(0);
            return Value.Void;
        });
        r.Register(Res, "id", "()I", c => Value.Int(_handleIds.TryGetValue(c.Self, out var id) ? id : World.Host.IdOf(c.Self)));
        r.Register(Game, "getScriptInstance", "()Ljava.lang.Object;", c =>
        {
            var host = (DefaultPartHost)World.Host;
            return Value.Obj(host.BoundTarget(c.Self) ?? (_handleIds.TryGetValue(c.Self, out var id) ? host.Resolve(id) : c.Self));
        });
        r.Register(Game, "getInfo", "(II)I", c => Value.Int(World.TryGetInfo(c.Self, c.Int(0), c.Int(1), out var v) ? v : 0));
        r.Register(Game, "queueEvent", "(Ljava.util.resource.ResourceRef;ILjava.lang.String;)V", c =>
        {
            var text = c.String(2) ?? string.Empty;
            if (!(c.Int(1) == 16 && World.ExecuteCommand(c.Vm, c.Self, text)))
            {
                Events.Add((c.Self, c.Int(1), text));
            }

            return Value.Void;
        });
        r.Fallback = NativeRegistry.DefaultStub;
        Vm = new Vm.Vm(path, r) { InstructionBudget = 50_000_000 };
    }

    public Vm.Vm Vm { get; }

    public PartsWorld World { get; }

    /// <summary>queueEvent calls the parts layer did not consume (target, event, text).</summary>
    public List<(VmObject Target, int Event, string Text)> Events { get; } = [];

    public static Harness? Create() => GameRoot.ClassPath is { } p ? new Harness(p) : null;

    /// <summary>Class name declared by a shipped .class file (relative to the game root).</summary>
    public string ClassOf(string relativeClassFile) => Vm.ClassPath.AddFile(Path.Combine(GameRoot.Root!, relativeClassFile))[0];

    public static PartDefinition Cfg(string relativeCfg, int typeId) =>
        PartDefinition.FromCfg(CfgDocument.Load(Path.Combine(GameRoot.Root!, relativeCfg)), typeId);

    /// <summary>What the gametype creator does: allocate, attach the native twin, run <c>&lt;init&gt;(int id)</c>.</summary>
    public PartState Create(string className, PartDefinition? def, int typeId)
    {
        var cls = Vm.LoadClass(className);
        var obj = Vm.NewObject(cls);
        var p = World.Attach(obj, def, typeId);
        var ctor = cls.FindMethods("<init>", 1).FirstOrDefault() ?? throw new InvalidOperationException($"{className} has no <init>(int)");
        Vm.InvokeVoid(ctor, obj, Value.Int(typeId));
        return p;
    }

    public Value Call(PartState p, string method, params Value[] args) => Vm.InvokeVirtual(p.Object, method, args);

    public Value Call(VmObject o, string method, params Value[] args) => Vm.InvokeVirtual(o, method, args);

    /// <summary>The script way: <c>child.command("install 0 car 0 parent slot")</c> (Part.addPart's format).</summary>
    public void InstallByCommand(PartState child, PartState car, PartState parent, int parentSlot) =>
        Call(child, "command", Str($"install 0 {World.Host.IdOf(car.Object)} 0 {World.Host.IdOf(parent.Object)} {parentSlot}"));

    public Value Str(string s) => Value.Obj(Vm.NewString(s));
}
