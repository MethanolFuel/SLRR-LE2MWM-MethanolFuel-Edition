using System.Globalization;
using Slrr.Core;
using Slrr.Vm;

namespace Slrr.Engine.Resources;

/// <summary>
/// Engine side of a <c>java.util.resource.ResourceRef</c> (and of every subclass: GameRef, RenderRef, PhysicsRef,
/// GameType, Viewport, ...): lives in <see cref="VmObject.Native"/>. The exe keeps a 16-byte handle in the script
/// field <c>ptr</c> (allocated by <c>newNative</c> only when <c>ptr</c> is 0, measured at 0x47cea0) whose +8 is the id
/// (<c>id()</c> returns it, 0x47d290) and +0xc the resolved node (<c>type()</c> reads node+0x4c, 0x47d210).
/// Other engine areas that subclass ResourceRef keep their own state in <see cref="Extension"/> instead of replacing
/// the handle.
/// </summary>
public sealed class ResourceHandle
{
    public int Id { get; set; }

    /// <summary>Free slot for other engine areas (render, sound, physics) that need per-object state on a ResourceRef subclass.</summary>
    public object? Extension { get; set; }

    public override string ToString() => ResourceIds.Format(Id);
}

/// <summary>An engine message (the exe prints them with its error/log functions).</summary>
public sealed record EngineMessage(string Text, bool IsError);

/// <summary>A command string sent to an object without a script <c>handleEvent</c> (native object kinds).</summary>
public sealed record NativeCommand(int TargetId, string? TargetKind, int SourceId, string Text);

/// <summary>
/// Resource/instance engine: archive table, resource tree, gametype registry, gametype creation
/// (<c>GameRef.create</c>, <c>createNativeInstance</c>) and the GameType event plumbing (event masks, timers,
/// notifications, queued events). One per VM; <see cref="Attach"/> installs the resource-literal mapping.
/// </summary>
public sealed class ResourceEngine
{
    public const string ResourceRefClass = "java.util.resource.ResourceRef";
    public const string GameRefClass = "java.util.resource.GameRef";
    public const string RenderRefClass = "java.util.resource.RenderRef";
    public const string GameTypeClass = "java.lang.GameType";

    /// <summary>GameRef.EVENT_* values (measured: GameRef.class constants).</summary>
    public const int EventCommand = 16;
    public const int EventTime = 128;

    private readonly List<Timer> _timers = [];
    private readonly List<QueuedEvent> _events = [];
    private readonly List<Subscription> _subscriptions = [];
    private int _rootId;
    private int _getInfoDepth;

    public ResourceEngine(ArchiveTable archives)
    {
        Archives = archives;
        Tree = new ResourceTree(archives);
        Gametypes = new GametypeRegistry(Tree);
    }

    public ResourceEngine(GameFileSystem game)
        : this(new ArchiveTable(game))
    {
        Game = game;
    }

    public GameFileSystem? Game { get; }

    public ArchiveTable Archives { get; }

    public ResourceTree Tree { get; }

    public GametypeRegistry Gametypes { get; }

    public Slrr.Vm.Vm? Vm { get; private set; }

    public List<EngineMessage> Messages { get; } = [];

    /// <summary>Commands queued to objects that have no script (camera, car, part ... native kinds), in order.</summary>
    public List<NativeCommand> Commands { get; } = [];

    /// <summary>Instances whose script constructor is still running in a <c>THRD-CREATE</c> thread.</summary>
    public int PendingCreates { get; private set; }

    /// <summary>Max frames <see cref="RunToCompletion"/> pumps a blocked create thread before giving up.</summary>
    public int CreateFrameLimit { get; set; } = 10_000;

    /// <summary>
    /// World-tree root: <c>system.rpk:0x4</c> (<c>ins_game</c>, TypeOfEntry 1). Measured: 0x427980 binds the global
    /// ResourceRef 0x636460 to it, sets its flags |= 0x10 (WORLDTREEROOT), uses it as the parent of Config and GameInit,
    /// and <c>GameRef.create</c> (0x47d7b0) substitutes it for a null parent.
    /// </summary>
    public int RootId => _rootId != 0 ? _rootId : _rootId = Archives.ResolveLiteral("system.rpk", 0x4);

    /// <summary>Uses this engine for resource literals of <paramref name="vm"/> (opcode 0E).</summary>
    public void Attach(Slrr.Vm.Vm vm)
    {
        Vm = vm;
        vm.ResolveResource = (archive, id) => Archives.ResolveLiteral(archive, id);
    }

    private Slrr.Vm.Vm V => Vm ?? throw new InvalidOperationException("ResourceEngine is not attached to a VM");

    public void Log(string text, bool error = false)
    {
        Messages.Add(new EngineMessage(text, error));
        Vm?.Out.WriteLine(text);
    }

    // ---------------------------------------------------------------- handles

    public static ResourceHandle? Handle(VmObject? o) => o?.Native as ResourceHandle;

    public static ResourceHandle EnsureHandle(VmObject o)
    {
        if (o.Native is ResourceHandle h)
        {
            return h;
        }

        h = new ResourceHandle();
        o.Native = h;
        return h;
    }

    public static int IdOf(VmObject? o) => Handle(o)?.Id ?? 0;

    public ResourceNode? NodeOf(VmObject? o) => Tree.Get(IdOf(o));

    /// <summary>A script object of <paramref name="className"/> bound to <paramref name="id"/>, created the way the
    /// engine hands out refs (no constructor; field initializers run).</summary>
    public VmObject NewRef(string className, int id)
    {
        var o = V.NewObject(V.LoadClass(className));
        EnsureHandle(o).Id = id;
        return o;
    }

    public Value RefOrNull(string className, int id) => id == 0 ? Value.Null : Value.Obj(NewRef(className, id));

    /// <summary>
    /// Script class of a ref the engine hands out for a node (getFirstChild/getNextChild/getParent...): measured
    /// switch at 0x47d300 (index bytes at 0x47d3c4): TypeOfEntry 1 (ins_game) and 8 (gametype) → GameRef,
    /// 3 (ins_render) and 14 (render object) → RenderRef, everything else (and unresolved ids) → ResourceRef.
    /// Scripts depend on it: <c>GameLogic.MWM_Items_Scan</c> calls the GameRef method <c>isScripted()</c> by name on the
    /// children of <c>cars:0x0101</c>.
    /// </summary>
    public static string RefClassForType(int type) => type switch
    {
        ResourceTypes.InstanceGame or ResourceTypes.Game => GameRefClass,
        ResourceTypes.InstanceRender or ResourceTypes.RenderObject => RenderRefClass,
        _ => ResourceRefClass,
    };

    /// <summary>A ref to <paramref name="id"/> of the class the engine picks for its node type (null for 0).</summary>
    public Value RefFor(int id) => id == 0 ? Value.Null : Value.Obj(NewRef(RefClassForType(Tree.Get(id)?.Type ?? 0), id));

    // ---------------------------------------------------------------- creation

    /// <summary>
    /// <c>GameRef.create</c> (0x47d7b0 → 0x53a5e0) and <c>create_native</c>: instantiate gametype
    /// <paramref name="typeId"/> below <paramref name="parentId"/> (0 = world root, measured). The receiver handle is
    /// rebound to the new instance (measured). With <paramref name="runScript"/>, a script class found through the
    /// gametype's <c>script</c> line is allocated, bound to the instance and its <c>&lt;init&gt;(int id)</c> runs with the new
    /// instance id (measured: the create routine pushes the id from 0x536ed0 into a <c>THRD-CREATE</c> thread).
    /// Returns the script object or null.
    /// </summary>
    public VmObject? Create(VmObject? receiver, int parentId, int typeId, string? parameters, string? name, bool runScript)
    {
        if (parentId == 0)
        {
            parentId = RootId;
        }

        var type = Tree.Get(typeId);
        if (type is null)
        {
            Log($"create: Wrong GameType! {Archives.ArchiveName(typeId)}:0x{(uint)typeId:X8}", error: true);
            return null;
        }

        if (type.Type != ResourceTypes.Game)
        {
            // measured: the exe prints the message and carries on
            Log($"create: Wrong GameType! {Archives.ArchiveName(typeId)}:0x{(uint)typeId:X8}", error: true);
        }

        var info = Gametypes.Get(typeId);
        var node = Tree.CreateInstance(ResourceTypes.InstanceGame, parentId, name ?? "_gameinst", typeId);
        node.Params = parameters;
        node.NativeKind = info?.NativeKind;
        ApplyPlacement(node, parameters);
        if (receiver is not null)
        {
            EnsureHandle(receiver).Id = node.Id;
        }

        if (!runScript || info?.Script is null)
        {
            return null;
        }

        var cls = ResolveScriptClass(info.Script);
        if (cls is null)
        {
            Log($"create: script class not found: {info.Script} ({ResourceIds.Format(typeId)})", error: true);
            return null;
        }

        var obj = V.NewObject(cls);
        EnsureHandle(obj).Id = node.Id;
        node.Script = obj;
        var ctor = FindIntConstructor(cls);
        if (ctor is null)
        {
            Log($"create: Undefined GameType Constructor! {(uint)node.Id:X8} ({cls.Name})", error: true);
            return obj;
        }

        RunConstructor(ctor, obj, node.Id);
        return obj;
    }

    /// <summary>
    /// <c>GameType.createNativeInstance(parent, typeId, params, name)</c> (0x481a70): the script object exists already
    /// (plain <c>new</c>); creates its engine twin and binds the object to it. (M) the twin is an instance node like the
    /// one <c>create</c> makes; typeId 0 means no gametype (GameLogic, Osd).
    /// </summary>
    public ResourceNode CreateNativeInstance(VmObject self, int parentId, int typeId, string? parameters, string? name)
    {
        // Guess: an object that create() already bound to its instance (Dummy(int) is reached from create and calls
        // createNativeInstance again) keeps that instance; only type, params and name are updated.
        if (NodeOf(self) is { IsInstance: true } existing && ReferenceEquals(existing.Script, self))
        {
            existing.TypeId = typeId != 0 ? typeId : existing.TypeId;
            existing.Params = parameters ?? existing.Params;
            existing.Name = name ?? existing.Name;
            return existing;
        }

        var node = Tree.CreateInstance(ResourceTypes.InstanceGame, parentId == 0 ? RootId : parentId, name ?? "_gameinst", typeId);
        node.Params = parameters;
        node.Script = self;
        node.NativeKind = typeId == 0 ? null : Gametypes.Get(typeId)?.NativeKind;
        ApplyPlacement(node, parameters);
        EnsureHandle(self).Id = node.Id;
        return node;
    }

    /// <summary>Destroys an instance (and its instance children), its timers and subscriptions.</summary>
    public void Destroy(ResourceNode node)
    {
        foreach (var n in Tree.Destroy(node))
        {
            _timers.RemoveAll(t => t.Target == n.Id);
            _subscriptions.RemoveAll(s => s.Listener == n.Id || s.Source == n.Id);
            _events.RemoveAll(e => e.Target == n.Id);
        }
    }

    public VmClass? ResolveScriptClass(string target)
    {
        var src = V.ClassPath.FindScriptTarget(target);
        if (src is null)
        {
            return null;
        }

        V.ClassPath.AddClass(src.Class, src.File);
        return V.TryLoadClass(src.Class.Name);
    }

    /// <summary>Is the script class of a gametype entry or instance (a subclass of) <paramref name="className"/>?</summary>
    public bool IsScripted(int id, string? className)
    {
        var node = Tree.Get(id);
        if (node is null)
        {
            return false;
        }

        VmClass? cls;
        if (node.IsInstance)
        {
            cls = node.Script?.Class;
        }
        else
        {
            var script = Gametypes.Get(id)?.Script;
            cls = script is null ? null : ResolveScriptClass(script);
        }

        return cls is not null && (className is null || cls.IsSubclassOf(className));
    }

    /// <summary>
    /// Runs a constructor with the instance id. Outside script code (engine boot) it runs in a <c>THRD-CREATE</c> VM
    /// thread that is pumped until it ends (it may sleep or wait, other threads run meanwhile, as in the exe);
    /// from inside a native it runs nested on the calling thread (hypothesis: the exe runs THRD-CREATE to completion
    /// before <c>create</c> returns, 0x53a9e6 calls Thread::run).
    /// </summary>
    private void RunConstructor(VmMethod ctor, VmObject obj, int id)
    {
        if (V.CurrentThread is not null)
        {
            V.InvokeVoid(ctor, obj, Value.Int(id));
            return;
        }

        var t = V.Spawn(ctor, obj, [Value.Int(id)], "THRD-CREATE");
        RunToCompletion(t);
    }

    /// <summary>Pumps the scheduler until <paramref name="t"/> finishes (frames of the earliest wake-up, else 1/30 s).</summary>
    public bool RunToCompletion(VmThread t)
    {
        PendingCreates++;
        try
        {
            for (var frames = 0; t.State != VmThreadState.Finished; frames++)
            {
                if (frames >= CreateFrameLimit)
                {
                    return false;
                }

                var dt = t.State switch
                {
                    VmThreadState.Ready => 0f,
                    VmThreadState.Sleeping => Math.Max(0f, t.WakeTime - V.Time),
                    _ => 1f / 30f,
                };
                V.RunFrame(dt);
                Pump();
            }

            return true;
        }
        finally
        {
            PendingCreates--;
        }
    }

    private static VmMethod? FindIntConstructor(VmClass cls)
    {
        for (var c = cls; c is not null; c = c.Super)
        {
            var m = c.DeclaredMethod("<init>", "(I)");
            if (m is not null)
            {
                return m;
            }
        }

        return null;
    }

    /// <summary>Placement from a params text <c>x,y,z,yaw,pitch,roll</c> (M: map records and script create calls use this form).</summary>
    private static void ApplyPlacement(ResourceNode node, string? parameters)
    {
        if (string.IsNullOrWhiteSpace(parameters))
        {
            return;
        }

        var parts = parameters.Split(',');
        if (parts.Length < 3)
        {
            return;
        }

        for (var i = 0; i < 6 && i < parts.Length; i++)
        {
            if (!float.TryParse(parts[i].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var f))
            {
                return;
            }

            if (i < 3)
            {
                node.Pos[i] = f;
            }
            else
            {
                node.Ori[i - 3] = f;
            }
        }
    }

    // ---------------------------------------------------------------- GameType events (stored + delivered by Pump)

    public void AddTimer(int target, float seconds, int timerId) => _timers.Add(new Timer(target, V.Time + Math.Max(0, seconds), timerId));

    public void RemoveAllTimers(int target) => _timers.RemoveAll(t => t.Target == target);

    public void AddNotification(int listener, int source, int mask, string? handler) =>
        _subscriptions.Add(new Subscription(listener, source, mask, handler));

    public void RemoveNotification(int listener, int source, int mask) =>
        _subscriptions.RemoveAll(s => s.Listener == listener && s.Source == source && (mask == 0 || (s.Mask & mask) != 0));

    public void QueueEvent(int target, int source, int code, string? parameters) => _events.Add(new QueuedEvent(target, source, code, parameters));

    public int PendingTimers => _timers.Count;

    public int PendingEvents => _events.Count;

    /// <summary>
    /// One engine event step: fires due timers (<c>handleEvent(GameRef, EVENT_TIME, timerId)</c>, only with EVENT_TIME in
    /// the mask, as documented by the scripts) and delivers queued events (<c>handleEvent(GameRef, int, String)</c> to the
    /// target when its mask accepts the event, to subscribers via <c>addNotification</c>; commands to script-less
    /// objects go to <see cref="Commands"/>). Each delivery runs in its own VM thread (hypothesis: thread naming and
    /// ordering of the exe not traced, open question 3).
    /// </summary>
    public int Pump()
    {
        var delivered = 0;
        var now = V.Time;
        foreach (var t in _timers.Where(t => t.Due <= now).ToList())
        {
            _timers.Remove(t);
            var node = Tree.Get(t.Target);
            if (node?.Script is { } s && (node.EventMask & EventTime) != 0)
            {
                delivered += Deliver(s, "handleEvent", RefOrNull(GameRefClass, t.Target), EventTime, Value.Int(t.TimerId));
            }
        }

        var events = _events.ToList();
        _events.Clear();
        foreach (var e in events)
        {
            var node = Tree.Get(e.Target);
            if (node is null)
            {
                continue;
            }

            var text = e.Params is null ? Value.Null : Value.Obj(V.NewString(e.Params));
            if (node.Script is { } s)
            {
                if ((node.EventMask & e.Code) != 0)
                {
                    delivered += Deliver(s, "handleEvent", RefOrNull(GameRefClass, e.Source), e.Code, text);
                }
            }
            else if (e.Code == EventCommand)
            {
                Commands.Add(new NativeCommand(e.Target, node.NativeKind, e.Source, e.Params ?? string.Empty));
            }

            foreach (var sub in _subscriptions.Where(x => x.Source == e.Target && (x.Mask & e.Code) != 0).ToList())
            {
                if (Tree.Get(sub.Listener)?.Script is { } l)
                {
                    delivered += Deliver(l, sub.Handler ?? "handleEvent", RefOrNull(GameRefClass, e.Target), e.Code, text);
                }
            }
        }

        return delivered;
    }

    private int Deliver(VmObject target, string method, Value source, int code, Value param)
    {
        Value[] args = [source, Value.Int(code), param];
        var want = param.Kind == VmKind.Int ? VmKind.Int : VmKind.Object;
        var m = target.Class.FindMethods(method, 3).FirstOrDefault(c => !c.IsStatic && VmTypes.KindOf(c.Params[2]) == want);
        if (m is null)
        {
            return 0;
        }

        if (m.Code is not null || m.IsNative)
        {
            V.Spawn(m, target, args, "THRD-EVENT");
        }

        return 1;
    }

    /// <summary>
    /// <c>GameRef.getInfo(key, arg)</c> (0x47ddf0). (M) GII_ID (5) and GII_TYPE (6) are answered natively
    /// (Catalog uses <c>getInfo(GII_TYPE) &gt;&gt; 16</c> as the archive index of a car's type, consistent with GII_TYPE
    /// being the gametype id); every other key is asked from the script's <c>getInfo(int)</c> override (the exe
    /// contains the string <c>getInfo</c>), else 0. Open question 6.
    /// </summary>
    public int GetInfo(VmObject self, int key)
    {
        var node = NodeOf(self);
        switch (key)
        {
            case 5:
                return IdOf(self);
            case 6:
                return node?.IsInstance == true ? node.TypeId : IdOf(self);
        }

        var script = node?.Script ?? (self.Class.IsSubclassOf(GameTypeClass) ? self : null);
        if (script is null || _getInfoDepth > 4)
        {
            return 0;
        }

        var m = script.Class.FindMethod("getInfo", "(I)I");
        if (m is null || m.Code is null || m.Owner.Name == GameRefClass)
        {
            return 0;
        }

        _getInfoDepth++;
        try
        {
            return V.Invoke(m, script, Value.Int(key)).AsInt;
        }
        finally
        {
            _getInfoDepth--;
        }
    }

    private sealed record Timer(int Target, float Due, int TimerId);

    private sealed record QueuedEvent(int Target, int Source, int Code, string? Params);

    private sealed record Subscription(int Listener, int Source, int Mask, string? Handler);
}
