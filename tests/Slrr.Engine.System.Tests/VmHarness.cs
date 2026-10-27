using Slrr.Core;
using Slrr.Vm;
using Slrr.Vm.Tests;
using VmEngine = Slrr.Vm.Vm;

namespace Slrr.Engine.SystemLib.Tests;

/// <summary>A VM wired to <see cref="EngineSystem"/> over a temp save root; the game root is the repository (or $SLRR_GAME_ROOT).</summary>
internal sealed class VmHarness : IDisposable
{
    private static readonly Lazy<string?> Root = new(FindRoot);
    private static readonly Dictionary<string, GameFileSystem> Fs = [];

    private VmHarness(VmClassPath path, string gameRoot, bool stubs)
    {
        SaveRoot = Path.Combine(Path.GetTempPath(), "slrr-vm-" + Guid.NewGuid().ToString("N"));
        Sandbox = new GameSandbox(gameRoot, SaveRoot);
        System = new EngineSystem(Sandbox);
        var natives = NativeRegistry.CreateDefault().Add(System);
        if (stubs)
        {
            natives.Fallback = NativeRegistry.DefaultStub;
        }

        Natives = natives;
        Vm = new VmEngine(path, natives);
    }

    public string SaveRoot { get; }

    public GameSandbox Sandbox { get; }

    public EngineSystem System { get; }

    public NativeRegistry Natives { get; }

    public VmEngine Vm { get; }

    /// <summary>Null when no game data is available (corpus tests then pass vacuously).</summary>
    public static VmHarness? Game(bool stubs = true)
    {
        if (Root.Value is not { } root)
        {
            return null;
        }

        lock (Fs)
        {
            if (!Fs.TryGetValue(root, out var fs))
            {
                Fs[root] = fs = new GameFileSystem(root);
            }

            return new VmHarness(new VmClassPath(fs), root, stubs);
        }
    }

    public static VmHarness Synthetic(IEnumerable<ClassBuilder> classes, bool stubs = false)
    {
        var path = new VmClassPath();
        foreach (var c in classes)
        {
            path.AddClass(c.Build());
        }

        return new VmHarness(path, AppContext.BaseDirectory, stubs);
    }

    /// <summary>Stands in for DirectInput: <c>Input.getDeviceName(i)</c> returns these names, then null.</summary>
    public void StubDevices(params string[] names) =>
        Natives.Register("java.io.Input", "getDeviceName", "(I)Ljava.lang.String;", c => c.Int(0) < names.Length ? Value.Obj(System.Intern(c.Vm, names[c.Int(0)])) : Value.Null, replace: true);

    public VmObject New(string cls, params Value[] args) => Vm.Construct(Vm.LoadClass(cls), args);

    public VmObject NewFile(string path) => New("java.io.File", Value.Obj(Vm.NewString(path)));

    public Value Call(VmObject o, string name, params Value[] args) => Vm.InvokeVirtual(o, name, args);

    public Value Static(string cls, string name, params Value[] args)
    {
        var m = Vm.LoadClass(cls).Methods.Single(x => x.IsStatic && x.Name == name && x.ArgCount == args.Length);
        return Vm.Invoke(m, null, args);
    }

    public Value Str(string s) => Value.Obj(Vm.NewString(s));

    public string? Text(Value v) => Vm.GetString(v.AsObject);

    public void Dispose()
    {
        System.CloseAll();
        if (Directory.Exists(SaveRoot))
        {
            Directory.Delete(SaveRoot, recursive: true);
        }
    }

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
}
