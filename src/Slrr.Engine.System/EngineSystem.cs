using Slrr.Vm;
using VmEngine = Slrr.Vm.Vm;

namespace Slrr.Engine.SystemLib;

/// <summary>
/// State and natives of the engine's "system" layer: the 14 java.lang.System natives that Slrr.Vm lacks, java.io.File /
/// java.io.FindFile (sandboxed, SDAT format), GameType.addTimer / removeAllTimers and a corrected Thread.sleep.
/// Register it with <c>registry.Add(new EngineSystem(sandbox))</c> (after <c>NativeRegistry.CreateDefault()</c>), keep a
/// reference and drive time with <see cref="Frame"/>. Everything engine-specific is a hook so that the other engine
/// areas (resources, rendering, loading) can plug in without this project referencing them.
/// </summary>
public sealed class EngineSystem : INativeModule
{
    private const string Sys = "java.lang.System";
    private const string FileClass = "java.io.File";
    private const string FindClass = "java.io.FindFile";

    private readonly HashSet<FileHandle> _open = [];

    public EngineSystem(GameSandbox sandbox)
    {
        Sandbox = sandbox;
    }

    public GameSandbox Sandbox { get; }

    public TimerService Timers { get; } = new();

    /// <summary>Register GameType.addTimer / removeAllTimers. Turn off if the game-object layer registers them itself.</summary>
    public bool RegisterTimerNatives { get; set; } = true;

    /// <summary>Replace Slrr.Vm's Thread.sleep (which treats its argument as seconds) by the measured millisecond version.</summary>
    public bool FixThreadSleep { get; set; } = true;

    /// <summary>
    /// Registers a hook-based <c>System.openLib</c>. Turn off when the Resources area registers the archive-aware one
    /// (it returns the archive index, which <c>Catalog</c> relies on).
    /// </summary>
    public bool RegisterOpenLib { get; set; } = true;

    // ---- System state ------------------------------------------------------------------------------------------

    /// <summary><c>System.buildNumber()</c>; MainMenuDialog compares it with Config.acceptBuild (601). The shipped PE is build 606.</summary>
    public int BuildNumber { get; set; } = 606;

    /// <summary>Simulation speed factor (<c>System.timeWarp</c>); 1 normal, 0 pause.</summary>
    public float TimeWarp { get; private set; } = 1f;

    /// <summary>Last <c>System.syncGameTime</c> value: in-game seconds since midnight (sky/sun).</summary>
    public float GameTime { get; private set; }

    /// <summary>Last <c>System.setMeasure</c> value: metres per distance unit (1000 metric, 1600 miles).</summary>
    public float Measure { get; private set; } = 1000f;

    public int LoadPriority { get; private set; }

    /// <summary>Archives mounted by <c>System.openLib</c> (normalised game paths, in order).</summary>
    public List<string> MountedLibs { get; } = [];

    /// <summary>Script paths the sandbox refused (for diagnostics and tests).</summary>
    public List<string> Rejections { get; } = [];

    /// <summary>Mounts an archive for the resource layer (argument: real path, game path). Return false if it cannot be opened.</summary>
    public Func<string, string, bool>? OpenLibHandler { get; set; }

    /// <summary><c>System.isLoading()</c>: true while the resource streamer is busy. Default: never busy.</summary>
    public Func<bool> IsLoading { get; set; } = () => false;

    /// <summary>Called by <c>System.isLoadingReset()</c>.</summary>
    public Action? LoadingReset { get; set; }

    /// <summary>Called by <c>System.getConfigOptions()</c> (the C# engine side refreshes script Config statics).</summary>
    public Action<VmEngine>? ConfigOptionsRefresh { get; set; }

    /// <summary><c>System.info(i)</c>, an engine information string by index (no script site; hypothesis).</summary>
    public Func<int, string?>? InfoProvider { get; set; }

    /// <summary>Raised by <c>System.timeWarp</c> after the factor changed.</summary>
    public event Action<float>? TimeWarpChanged;

    /// <summary>Raised by <c>System.syncGameTime</c>.</summary>
    public event Action<float>? GameTimeSynced;

    // ---- resource references in SDAT files -----------------------------------------------------------------------

    /// <summary>
    /// Id stored by <c>File.write(ResourceRef)</c>. Default: call the script object's own <c>id()</c> (null gives 0).
    /// The on-disk encoding is a guess (one int); see docs/engine/system.md.
    /// </summary>
    public Func<VmEngine, VmObject?, int>? ResourceIdOf { get; set; }

    /// <summary>Maps the id read by <c>File.readResID()</c> to a runtime resource id. Default: identity.</summary>
    public Func<int, int> ResolveResourceId { get; set; } = id => id;

    // ---- string identity -------------------------------------------------------------------------------------------

    private readonly Dictionary<string, VmObject> _strings = new(StringComparer.Ordinal);

    /// <summary>
    /// A canonical String object for this text. The VM compares objects by reference (as the exe's <c>==</c> at
    /// 0x41c5c0 does), yet scripts compare strings that came from different places (ControlSet.load compares the names
    /// read from a file with <c>Input.getDeviceName</c>), which only works if the engine interns strings. Strings
    /// produced by natives of this project (<c>readString</c>, <c>FindFile</c>) are interned through this pool; other
    /// areas should use it too (hypothesis, see docs/engine/system.md).
    /// </summary>
    public VmObject Intern(VmEngine vm, string text)
    {
        if (!_strings.TryGetValue(text, out var o))
        {
            _strings[text] = o = vm.NewString(text);
        }

        return o;
    }

    private Value InternedOrNull(VmEngine vm, string? text) => text is null ? Value.Null : Value.Obj(Intern(vm, text));

    // ---- time ------------------------------------------------------------------------------------------------------

    /// <summary>
    /// One engine frame: advances the VM clocks (simulation time scaled by <see cref="TimeWarp"/>), runs the threads,
    /// starts due timer handlers and runs them once.
    /// </summary>
    public void Frame(VmEngine vm, float dt)
    {
        vm.RunFrame(dt);
        vm.SimTime += dt * (TimeWarp - 1f);
        if (Timers.Pump(vm) > 0)
        {
            vm.RunFrame(0);
        }
    }

    /// <summary>Runs <paramref name="seconds"/> of virtual time in steps of <paramref name="step"/>.</summary>
    public void RunFor(VmEngine vm, float seconds, float step = 1f / 60f)
    {
        var end = vm.Time + seconds;
        while (vm.Time < end - 1e-6f)
        {
            Frame(vm, Math.Min(step, end - vm.Time));
        }
    }

    /// <summary>Closes every File handle still open (the engine's finalizer does this per object).</summary>
    public void CloseAll()
    {
        foreach (var h in _open.ToList())
        {
            h.Close();
        }

        _open.Clear();
    }

    // ---- registration ------------------------------------------------------------------------------------------------

    public void Register(NativeRegistry r)
    {
        RegisterSystem(r);
        RegisterFile(r);
        RegisterFindFile(r);
        if (RegisterTimerNatives)
        {
            r.Register("java.lang.GameType", "addTimer", "(FI)V", c =>
            {
                Timers.Add(c.Vm, c.Self, c.Float(0), c.Int(1));
                return Value.Void;
            });
            r.Register("java.lang.GameType", "removeAllTimers", "()V", c =>
            {
                Timers.RemoveAll(c.Self);
                return Value.Void;
            });
        }

        if (FixThreadSleep)
        {
            r.Register("java.lang.Thread", "sleep", "(F)V", c =>
            {
                c.Thread.SleepFor(c.Float(0) / 1000f);
                return Value.Void;
            }, replace: true);
        }
    }

    private void RegisterSystem(NativeRegistry r)
    {
        r.Register(Sys, "buildNumber", "()I", _ => Value.Int(BuildNumber));
        r.Register(Sys, "info", "(I)Ljava.lang.String;", c => c.Str(InfoProvider?.Invoke(c.Int(0))));
        r.Register(Sys, "compileAll", "(Ljava.lang.String;)I", _ => Value.Zero); // no in-engine compiler: nothing recompiled
        r.Register(Sys, "timeWarp", "(F)F", c =>
        {
            var previous = TimeWarp;
            var requested = c.Float(0);
            if (requested >= 0f && requested != previous)
            {
                TimeWarp = requested;
                TimeWarpChanged?.Invoke(requested);
            }

            return Value.Float(previous);
        });
        r.Register(Sys, "syncGameTime", "(F)V", c =>
        {
            GameTime = c.Float(0);
            GameTimeSynced?.Invoke(GameTime);
            return Value.Void;
        });
        r.Register(Sys, "setMeasure", "(F)V", c =>
        {
            Measure = c.Float(0);
            return Value.Void;
        });
        r.Register(Sys, "getConfigOptions", "()V", c =>
        {
            ConfigOptionsRefresh?.Invoke(c.Vm);
            return Value.Void;
        });
        r.Register(Sys, "netHost", "()I", _ => Value.Zero); // declared, not registered by the exe: network play removed
        r.Register(Sys, "netJoin", "()I", _ => Value.Zero);
        r.Register(Sys, "netLeave", "()I", _ => Value.Zero);
        if (RegisterOpenLib)
        {
            r.Register(Sys, "openLib", "(Ljava.lang.String;)I", c => Guard(c.String(0), () => OpenLib(c.String(0)!), Value.Zero));
        }
        r.Register(Sys, "isLoadingReset", "()V", _ =>
        {
            LoadingReset?.Invoke();
            return Value.Void;
        });
        r.Register(Sys, "isLoading", "()I", _ => Value.Bool(IsLoading()));
        r.Register(Sys, "setLdPriority", "(I)V", c =>
        {
            LoadPriority = c.Int(0);
            return Value.Void;
        });
    }

    private Value OpenLib(string path)
    {
        var real = Sandbox.ResolveExisting(path);
        if (real is null || !File.Exists(real))
        {
            return Value.Zero;
        }

        var norm = GameSandbox.Normalize(path);
        if (MountedLibs.Contains(norm, StringComparer.OrdinalIgnoreCase))
        {
            return Value.Bool(true);
        }

        if (OpenLibHandler is { } h && !h(real, norm))
        {
            return Value.Zero;
        }

        MountedLibs.Add(norm);
        return Value.Bool(true);
    }

    private void RegisterFile(NativeRegistry r)
    {
        r.Register(FileClass, "open", "(I)I", c => Guard(FileName(c.Self), () => Open(c.Self, c.Int(0)), Value.Zero));
        r.Register(FileClass, "close", "()V", c =>
        {
            if (c.Self.Native is FileHandle h)
            {
                h.Close();
                _open.Remove(h);
            }

            return Value.Void;
        });
        r.Register(FileClass, "write", "(I)I", c => Write(c.Self, w => w.WriteInt(c.Int(0))));
        r.Register(FileClass, "write", "(F)I", c => Write(c.Self, w => w.WriteFloat(c.Float(0))));
        r.Register(FileClass, "write", "(Ljava.lang.String;)I", c => Write(c.Self, w => w.WriteString(c.String(0))));
        r.Register(FileClass, "write", "(Ljava.util.resource.ResourceRef;)I", c =>
        {
            var id = ResourceIdOf is { } f ? f(c.Vm, c.Object(0)) : DefaultResourceId(c.Vm, c.Object(0));
            return Write(c.Self, w => w.WriteInt(id));
        });
        r.Register(FileClass, "readInt", "()I", c => Value.Int(Reader(c.Self)?.ReadInt() ?? 0));
        r.Register(FileClass, "readFloat", "()F", c => Value.Float(Reader(c.Self)?.ReadFloat() ?? 0f));
        r.Register(FileClass, "readResID", "()I", c => Value.Int(ResolveResourceId(Reader(c.Self)?.ReadInt() ?? 0)));
        r.Register(FileClass, "readString", "()Ljava.lang.String;", c => InternedOrNull(c.Vm, Reader(c.Self)?.ReadString()));

        r.Register(FileClass, "delete", "(Ljava.lang.String;)I", c => Guard(c.String(0), () => Value.Bool(Delete(c.String(0)!)), Value.Zero));
        r.Register(FileClass, "copy", "(Ljava.lang.String;Ljava.lang.String;)I", c => Guard(c.String(0), () => Value.Bool(Copy(c.String(0)!, c.String(1)!)), Value.Zero));
        r.Register(FileClass, "move", "(Ljava.lang.String;Ljava.lang.String;)I", c => Guard(c.String(0), () => Value.Bool(Move(c.String(0)!, c.String(1)!)), Value.Zero));
        r.Register(FileClass, "exists", "(Ljava.lang.String;)I", c => Guard(c.String(0), () => Value.Bool(Sandbox.ResolveExisting(c.String(0)!) is not null), Value.Zero));
        r.Register(FileClass, "TimeLow", "(Ljava.lang.String;)I", c => Guard(c.String(0), () => Value.Int(unchecked((int)FileTime(c.String(0)!))), Value.Zero));
        r.Register(FileClass, "TimeHigh", "(Ljava.lang.String;)I", c => Guard(c.String(0), () => Value.Int((int)(FileTime(c.String(0)!) >> 32)), Value.Zero));
    }

    private void RegisterFindFile(NativeRegistry r)
    {
        r.Register(FindClass, "first", "(Ljava.lang.String;I)Ljava.lang.String;", c =>
        {
            var state = new FindState();
            c.Self.Native = state;
            return Guard(c.String(0), () =>
            {
                state.Names = Sandbox.List(c.String(0)!, (ListKind)Math.Clamp(c.Int(1), 0, 2)) ?? [];
                return InternedOrNull(c.Vm, state.Next());
            }, Value.Null);
        });
        r.Register(FindClass, "next", "()Ljava.lang.String;", c => InternedOrNull(c.Vm, (c.Self.Native as FindState)?.Next()));
        r.Register(FindClass, "close", "()V", c =>
        {
            c.Self.Native = null;
            return Value.Void;
        });
    }

    // ---- java.io.File ------------------------------------------------------------------------------------------------

    private static string? FileName(VmObject self)
    {
        var f = self.Class.FindField("name");
        return f is { IsStatic: false } && self.Fields[f.Slot].Ref is { } o ? o.Native as string : null;
    }

    private Value Open(VmObject self, int mode)
    {
        if (self.Native is FileHandle old)
        {
            old.Close();
            _open.Remove(old);
        }

        var name = FileName(self);
        if (name is null)
        {
            return Value.Zero;
        }

        FileHandle? h;
        if (mode == 1)
        {
            var real = Sandbox.ResolveWritable(name);
            Directory.CreateDirectory(Path.GetDirectoryName(real)!);
            var fs = new FileStream(real, FileMode.Create, FileAccess.Write, FileShare.Read);
            h = new FileHandle(null, new SdatWriter(fs));
        }
        else
        {
            var real = Sandbox.ResolveExisting(name);
            if (real is null || !File.Exists(real))
            {
                return Value.Zero;
            }

            var reader = new SdatReader(File.ReadAllBytes(real));
            if (!reader.Valid)
            {
                return Value.Zero; // open(MODE_READ) checks the header
            }

            h = new FileHandle(reader, null);
        }

        self.Native = h;
        _open.Add(h);
        return Value.Bool(true);
    }

    private static SdatReader? Reader(VmObject self) => (self.Native as FileHandle)?.Reader;

    private Value Write(VmObject self, Action<SdatWriter> action)
    {
        if ((self.Native as FileHandle)?.Writer is not { } w)
        {
            return Value.Zero;
        }

        try
        {
            action(w);
            return Value.Bool(true);
        }
        catch (IOException)
        {
            return Value.Zero;
        }
    }

    private static int DefaultResourceId(VmEngine vm, VmObject? o) => o is null ? 0 : vm.InvokeVirtual(o, "id").AsInt;

    private bool Delete(string path)
    {
        var real = Sandbox.ResolveWritable(path);
        if (File.Exists(real))
        {
            File.Delete(real);
            return true;
        }

        if (Directory.Exists(real) && !Directory.EnumerateFileSystemEntries(real).Any())
        {
            Directory.Delete(real);
            return true;
        }

        return false;
    }

    private bool Copy(string src, string dst)
    {
        var from = Sandbox.ResolveExisting(src);
        var to = Sandbox.ResolveWritable(dst);
        if (from is null || !File.Exists(from))
        {
            return false;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(to)!);
        File.Copy(from, to, overwrite: true);
        return true;
    }

    private bool Move(string src, string dst)
    {
        var from = Sandbox.ResolveWritable(src);
        var to = Sandbox.ResolveWritable(dst);
        Directory.CreateDirectory(Path.GetDirectoryName(to)!);
        if (File.Exists(from))
        {
            File.Move(from, to, overwrite: true);
            return true;
        }

        if (Directory.Exists(from) && !Directory.Exists(to) && !File.Exists(to))
        {
            Directory.Move(from, to);
            return true;
        }

        return false;
    }

    private long FileTime(string path)
    {
        var real = Sandbox.ResolveExisting(path);
        return real is null ? 0 : File.GetLastWriteTimeUtc(real).ToFileTimeUtc();
    }

    /// <summary>Runs a native body; sandbox rejections and I/O failures become the script-visible failure value.</summary>
    private Value Guard(string? path, Func<Value> body, Value failure)
    {
        try
        {
            return body();
        }
        catch (GamePathException ex)
        {
            Rejections.Add(ex.Message);
            return failure;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Rejections.Add($"{path}: {ex.Message}");
            return failure;
        }
    }

    private sealed class FileHandle(SdatReader? reader, SdatWriter? writer)
    {
        public SdatReader? Reader { get; } = reader;

        public SdatWriter? Writer { get; private set; } = writer;

        public void Close()
        {
            Writer?.Dispose();
            Writer = null;
        }
    }

    private sealed class FindState
    {
        private int _index;

        public List<string> Names { get; set; } = [];

        public string? Next() => _index < Names.Count ? Names[_index++] : null;
    }
}
