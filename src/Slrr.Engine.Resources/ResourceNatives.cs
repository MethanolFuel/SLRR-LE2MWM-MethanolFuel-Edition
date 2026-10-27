using Slrr.Vm;

namespace Slrr.Engine.Resources;

/// <summary>
/// Natives of <c>java.util.resource.ResourceRef</c> (20), <c>GameRef</c> (20), <c>RenderRef</c> (14),
/// <c>PhysicsRef</c> (6) and <c>java.lang.GameType</c> (11): the 71 registrations of these classes in
/// docs/research/natives-redline.tsv. A headless engine: handles, the resource tree, instances, gametype creation and
/// event bookkeeping are real; render/physics setters store their values on the node (<see cref="ResourceNode.Properties"/>).
/// Per native, "measured" = read from the exe disassembly or shipped scripts, "guess" = headless stand-in
/// (docs/engine/resources.md has the table).
/// </summary>
public sealed class ResourceNatives : INativeModule
{
    private const string RR = ResourceEngine.ResourceRefClass;
    private const string GR = ResourceEngine.GameRefClass;
    private const string RenR = ResourceEngine.RenderRefClass;
    private const string PR = "java.util.resource.PhysicsRef";
    private const string GT = ResourceEngine.GameTypeClass;
    private const string LRR = "Ljava.util.resource.ResourceRef;";
    private const string LGR = "Ljava.util.resource.GameRef;";
    private const string LRenR = "Ljava.util.resource.RenderRef;";
    private const string LS = "Ljava.lang.String;";
    private const string LV = "Ljava.lang.Vector3;";
    private const string LY = "Ljava.lang.Ypr;";

    private readonly ResourceEngine _e;

    public ResourceNatives(ResourceEngine engine)
    {
        _e = engine;
    }

    /// <summary>Every (class, name, descriptor) this module registers.</summary>
    public static IReadOnlyList<NativeKey> Keys { get; } = CollectKeys();

    public void Register(NativeRegistry r)
    {
        RegisterResourceRef(r);
        RegisterGameRef(r);
        RegisterRenderRef(r);
        RegisterPhysicsRef(r);
        RegisterGameType(r);
    }

    private static List<NativeKey> CollectKeys()
    {
        var reg = new NativeRegistry();
        new ResourceNatives(new ResourceEngine(new ArchiveTable(_ => null))).Register(reg);
        return reg.Keys.ToList();
    }

    // ------------------------------------------------------------------ ResourceRef

    private void RegisterResourceRef(NativeRegistry r)
    {
        // measured 0x47cea0: allocates the handle only when ptr is 0 (so a binding made by create survives super())
        r.Register(RR, "newNative", "()V", c =>
        {
            ResourceEngine.EnsureHandle(c.Self);
            return Value.Void;
        });
        // measured 0x47cf00: unlinks and frees the handle
        r.Register(RR, "deleteNative", "()V", c =>
        {
            if (c.Self.Native is ResourceHandle)
            {
                c.Self.Native = null;
            }

            return Value.Void;
        });
        // measured 0x47cf80: handle.set(id)
        r.Register(RR, "set", "(I)V", c =>
        {
            ResourceEngine.EnsureHandle(c.Self).Id = c.Int(0);
            return Value.Void;
        });
        // measured 0x47cfc0 (copies the other handle's id); null clears (release() = set(null))
        r.Register(RR, "set", $"({LRR})V", c =>
        {
            ResourceEngine.EnsureHandle(c.Self).Id = ResourceEngine.IdOf(c.Object(0));
            return Value.Void;
        });
        r.Register(RR, "id", "()I", c => Value.Int(ResourceEngine.IdOf(c.Self))); // measured 0x47d290: handle+8
        r.Register(RR, "type", "()I", c => Value.Int(_e.NodeOf(c.Self)?.Type ?? 0)); // measured 0x47d210: node+0x4c, 0 if unresolved
        r.Register(RR, "getParentID", "()I", c => Value.Int(_e.NodeOf(c.Self)?.ParentId ?? 0));
        r.Register(RR, "getParent", $"(){LRR}", c => _e.RefFor(_e.NodeOf(c.Self)?.ParentId ?? 0));
        r.Register(RR, "getFirstChild", $"(){LRR}", c =>
        {
            var kids = _e.Tree.Children(ResourceEngine.IdOf(c.Self));
            return _e.RefFor(kids.Count > 0 ? kids[0] : 0);
        });
        r.Register(RR, "getNextChild", $"(){LRR}", c => _e.RefFor(_e.Tree.NextSibling(ResourceEngine.IdOf(c.Self))));
        r.Register(RR, "getWTRoot", $"(){LRR}", c => _e.RefFor(_e.RootId));
        r.Register(RR, "load", "()V", c => Touch(c, n => n.LoadCount++));
        r.Register(RR, "unload", "()V", c => Touch(c, n => n.LoadCount = 0));
        r.Register(RR, "cache", "()V", c => Touch(c, n => n.CacheCount++));
        r.Register(RR, "precache", "()V", c => Touch(c, n => n.CacheCount++));
        r.Register(RR, "destroy", "()V", c =>
        {
            if (_e.NodeOf(c.Self) is { IsInstance: true } n)
            {
                _e.Destroy(n);
            }

            return Value.Void;
        });
        r.Register(RR, "makeTexture", $"({LRR}{LS})V", c => MakeResource(c, ResourceTypes.Texture));
        r.Register(RR, "makeSound", $"({LRR}{LS})V", c => MakeResource(c, ResourceTypes.Sound));
        r.Register(RR, "scaleMesh", "(FFF)V", c => Touch(c, n => n.Properties["scale"] = new[] { c.Float(0), c.Float(1), c.Float(2) }));
        r.Register(RR, "duplicate", $"({LRR})V", c =>
        {
            var src = _e.NodeOf(c.Object(0));
            var copy = _e.Tree.CreateInstance(src?.Type ?? 0, 0, "duplicate", src?.Id ?? 0);
            ResourceEngine.EnsureHandle(c.Self).Id = copy.Id;
            return Value.Void;
        });
    }

    // ------------------------------------------------------------------ GameRef

    private void RegisterGameRef(NativeRegistry r)
    {
        var createDesc = $"({LGR}{LGR}{LS}{LS})";
        r.Register(GR, "create", createDesc + "Ljava.lang.GameType;", c =>
            Value.Obj(_e.Create(c.Self, ResourceEngine.IdOf(c.Object(0)), ResourceEngine.IdOf(c.Object(1)), c.String(2), c.String(3), runScript: true)));
        r.Register(GR, "create_native", createDesc + "V", c =>
        {
            _e.Create(c.Self, ResourceEngine.IdOf(c.Object(0)), ResourceEngine.IdOf(c.Object(1)), c.String(2), c.String(3), runScript: false);
            return Value.Void;
        });
        r.Register(GR, "getFlags", "()I", c => Value.Int(_e.NodeOf(c.Self)?.Flags ?? 0));
        r.Register(GR, "setFlags", "(I)V", c => Touch(c, n => n.Flags |= c.Int(0)));
        r.Register(GR, "clearFlags", "(I)V", c => Touch(c, n => n.Flags &= ~c.Int(0)));
        r.Register(GR, "getPos", $"(){LV}", c => Vector(c, n => n.Pos));
        r.Register(GR, "getOri", $"(){LY}", c => Ypr(c, n => n.Ori));
        r.Register(GR, "getVel", $"(){LV}", c => Vector(c, n => n.Vel));
        r.Register(GR, "setPos", $"({LV})V", c => Touch(c, n => ReadVector(c.Object(0), n.Pos)));
        r.Register(GR, "setMatrix", $"({LV}{LY})V", SetMatrix);
        r.Register(GR, "setState", $"({LV}{LY}{LV}{LV})V", c => Touch(c, n =>
        {
            ReadVector(c.Object(0), n.Pos);
            ReadYpr(c.Object(1), n.Ori);
            ReadVector(c.Object(2), n.Vel);
            ReadVector(c.Object(3), n.AngVel);
        }));
        r.Register(GR, "setParent", $"({LRR})V", c =>
        {
            if (_e.NodeOf(c.Self) is { IsInstance: true } n)
            {
                _e.Tree.SetParent(n, ResourceEngine.IdOf(c.Object(0)));
            }

            return Value.Void;
        });
        r.Register(GR, "isEmpty", "()I", c => Value.Bool(_e.NodeOf(c.Self) is null));
        r.Register(GR, "isScripted", $"({LS})I", c => Value.Bool(_e.IsScripted(ResourceEngine.IdOf(c.Self), c.String(0))));
        r.Register(GR, "getScriptInstance", "()Ljava.lang.Object;", c => Value.Obj(_e.NodeOf(c.Self)?.Script));
        r.Register(GR, "getInfo", "(II)I", c => Value.Int(_e.GetInfo(c.Self, c.Int(0))));
        r.Register(GR, "getInfo", $"(I{LS})I", c => Value.Int(_e.GetInfo(c.Self, c.Int(0))));
        r.Register(GR, "queueEvent", $"({LRR}I{LS})V", c =>
        {
            _e.QueueEvent(ResourceEngine.IdOf(c.Self), ResourceEngine.IdOf(c.Object(0)), c.Int(1), c.String(2));
            return Value.Void;
        });
        r.Register(GR, "setActiveCollision", "()V", _ => Value.Void);
        r.Register(GR, "getDetail", "()F", _ => Value.Float(1f));
    }

    // ------------------------------------------------------------------ RenderRef

    private void RegisterRenderRef(NativeRegistry r)
    {
        r.Register(RenR, "create", $"({LRR}{LRenR}{LS})V", c =>
        {
            var n = _e.Tree.CreateInstance(ResourceTypes.InstanceRender, ResourceEngine.IdOf(c.Object(1)), c.String(2) ?? "_renderinst",
                ResourceEngine.IdOf(c.Object(0)));
            ResourceEngine.EnsureHandle(c.Self).Id = n.Id;
            return Value.Void;
        });
        r.Register(RenR, "getBoneId", $"({LS})I", _ => Value.Int(-1));
        r.Register(RenR, "setMatrix", $"(I{LRR}{LV}{LY})V", c => Touch(c, n =>
        {
            n.Properties["bone" + c.Int(0)] = (ResourceEngine.IdOf(c.Object(1)), c.Object(2), c.Object(3));
        }));
        r.Register(RenR, "setMatrix", $"({LV}{LY})V", SetMatrix);
        r.Register(RenR, "changeResource", $"({LRR}{LRR})V", c => Touch(c, n =>
            n.Properties["resource:" + ResourceIds.Format(ResourceEngine.IdOf(c.Object(0)))] = ResourceEngine.IdOf(c.Object(1))));
        r.Register(RenR, "setColor", "(I)V", c => Touch(c, n => n.Properties["color"] = c.Int(0)));
        r.Register(RenR, "setLight", "(III)V", c => Touch(c, n => n.Properties["light"] = new[] { c.Int(0), c.Int(1), c.Int(2) }));
        r.Register(RenR, "setFlare", $"({LRR}IFFII)V", c => Touch(c, n =>
            n.Properties["flare"] = (ResourceEngine.IdOf(c.Object(0)), c.Int(1), c.Float(2), c.Float(3), c.Int(4), c.Int(5))));
        r.Register(RenR, "setType", $"({LRR})V", c => Touch(c, n => n.TypeId = ResourceEngine.IdOf(c.Object(0))));
        r.Register(RenR, "getTypeID", "()I", c => Value.Int(_e.NodeOf(c.Self)?.TypeId ?? 0));
        r.Register(RenR, "lineCreate", $"({LRR}{LRR})I", _ => Value.Zero);
        r.Register(RenR, "lineAdd", $"({LV}{LV}IF)V", _ => Value.Void);
        r.Register(RenR, "plotRoute", $"({LRR}{LRR}IF{LV})I", _ => Value.Zero);
        r.Register(RenR, "getDetail", "()F", _ => Value.Float(1f));
    }

    // ------------------------------------------------------------------ PhysicsRef

    private void RegisterPhysicsRef(NativeRegistry r)
    {
        r.Register(PR, "create", $"({LRR}I{LS})V", c => Physics(c, c.String(2), ("type", c.Int(1))));
        r.Register(PR, "createBox", $"({LRR}FFF{LS})V", c => Physics(c, c.String(4), ("box", new[] { c.Float(1), c.Float(2), c.Float(3) })));
        r.Register(PR, "createSphere", $"({LRR}F{LS})V", c => Physics(c, c.String(2), ("sphere", c.Float(1))));
        r.Register(PR, "setMatrix", $"({LV}{LY})V", SetMatrix);
        r.Register(PR, "getPos", $"(){LV}", c => Vector(c, n => n.Pos));
        r.Register(PR, "getOri", $"(){LY}", c => Ypr(c, n => n.Ori));
    }

    // ------------------------------------------------------------------ GameType

    private void RegisterGameType(NativeRegistry r)
    {
        r.Register(GT, "createNativeInstance", $"({LGR}I{LS}{LS})V", c =>
        {
            _e.CreateNativeInstance(c.Self, ResourceEngine.IdOf(c.Object(0)), c.Int(1), c.String(2), c.String(3));
            return Value.Void;
        });
        r.Register(GT, "setEventMask", "(I)V", c => Touch(c, n => n.EventMask = c.Int(0)));
        r.Register(GT, "clearEventMask", "(I)V", c => Touch(c, n => n.EventMask &= ~c.Int(0)));
        r.Register(GT, "addTimer", "(FI)V", c =>
        {
            _e.AddTimer(ResourceEngine.IdOf(c.Self), c.Float(0), c.Int(1));
            return Value.Void;
        });
        r.Register(GT, "removeAllTimers", "()V", c =>
        {
            _e.RemoveAllTimers(ResourceEngine.IdOf(c.Self));
            return Value.Void;
        });
        r.Register(GT, "addNotification", $"({LGR}II{LS})V", c =>
        {
            _e.AddNotification(ResourceEngine.IdOf(c.Self), ResourceEngine.IdOf(c.Object(0)), c.Int(1), null);
            return Value.Void;
        });
        r.Register(GT, "addNotification", $"({LGR}II{LS}{LS})V", c =>
        {
            _e.AddNotification(ResourceEngine.IdOf(c.Self), ResourceEngine.IdOf(c.Object(0)), c.Int(1), c.String(4));
            return Value.Void;
        });
        r.Register(GT, "remNotification", $"({LGR}I)V", c =>
        {
            _e.RemoveNotification(ResourceEngine.IdOf(c.Self), ResourceEngine.IdOf(c.Object(0)), c.Int(1));
            return Value.Void;
        });
        r.Register(GT, "registerCallback", "(I)V", c => Touch(c, n => n.Callbacks.Add(c.Int(0))));
        r.Register(GT, "unregisterCallback", "(I)V", c => Touch(c, n => n.Callbacks.Remove(c.Int(0))));
        r.Register(GT, "unregisterCallbacks", "()V", c => Touch(c, n => n.Callbacks.Clear()));
    }

    // ------------------------------------------------------------------ helpers

    private Value Touch(NativeCall c, Action<ResourceNode> act)
    {
        if (_e.NodeOf(c.Self) is { } n)
        {
            act(n);
        }

        return Value.Void;
    }

    private Value MakeResource(NativeCall c, int type)
    {
        if (c.Object(0) is { } dest)
        {
            var n = _e.Tree.CreateInstance(type, 0, c.String(1) ?? string.Empty, ResourceEngine.IdOf(c.Self));
            n.Properties["file"] = c.String(1);
            ResourceEngine.EnsureHandle(dest).Id = n.Id;
        }

        return Value.Void;
    }

    private Value Physics(NativeCall c, string? name, (string Key, object Value) prop)
    {
        var n = _e.Tree.CreateInstance(ResourceTypes.InstancePhysics, ResourceEngine.IdOf(c.Object(0)), name ?? "_physinst", 0);
        n.Properties[prop.Key] = prop.Value;
        ResourceEngine.EnsureHandle(c.Self).Id = n.Id;
        return Value.Void;
    }

    private Value SetMatrix(NativeCall c) => Touch(c, n =>
    {
        ReadVector(c.Object(0), n.Pos); // null keeps the old value (Vehicle ctor: setMatrix(null, null))
        ReadYpr(c.Object(1), n.Ori);
    });

    private Value Vector(NativeCall c, Func<ResourceNode, float[]> get)
    {
        var v = _e.NodeOf(c.Self) is { } n ? get(n) : new float[3];
        return Value.Obj(NewFloats(c.Vm, "java.lang.Vector3", ["x", "y", "z"], v));
    }

    private Value Ypr(NativeCall c, Func<ResourceNode, float[]> get)
    {
        var v = _e.NodeOf(c.Self) is { } n ? get(n) : new float[3];
        return Value.Obj(NewFloats(c.Vm, "java.lang.Ypr", ["y", "p", "r"], v));
    }

    internal static VmObject NewFloats(Slrr.Vm.Vm vm, string cls, string[] names, float[] v)
    {
        var o = vm.NewObject(vm.LoadClass(cls));
        for (var i = 0; i < names.Length; i++)
        {
            if (o.Class.FindField(names[i]) is { IsStatic: false } f)
            {
                o.Fields[f.Slot] = Value.Float(v[i]);
            }
        }

        return o;
    }

    private static void ReadVector(VmObject? o, float[] into) => ReadFloats(o, ["x", "y", "z"], into);

    private static void ReadYpr(VmObject? o, float[] into) => ReadFloats(o, ["y", "p", "r"], into);

    private static void ReadFloats(VmObject? o, string[] names, float[] into)
    {
        if (o is null)
        {
            return;
        }

        for (var i = 0; i < names.Length; i++)
        {
            if (o.Class.FindField(names[i]) is { IsStatic: false } f)
            {
                into[i] = o.Fields[f.Slot].AsFloat;
            }
        }
    }
}

/// <summary>
/// <c>System.openLib(String)</c> (0x487be0, measured): registers the archive name, loads it, returns its global archive
/// index. Kept in its own module because it belongs to java.lang.System; <c>System.rpkScan</c> is script code
/// (FindFile + openLib).
/// </summary>
public sealed class ArchiveNatives : INativeModule
{
    private readonly ResourceEngine _e;

    public ArchiveNatives(ResourceEngine engine)
    {
        _e = engine;
    }

    public void Register(NativeRegistry r) =>
        r.Register("java.lang.System", "openLib", "(Ljava.lang.String;)I", c => Value.Int(_e.Archives.Mount(c.String(0) ?? string.Empty)));
}
