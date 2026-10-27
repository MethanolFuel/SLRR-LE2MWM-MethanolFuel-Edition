using Slrr.Engine.Parts;
using Slrr.Engine.Resources;
using Slrr.Formats.Cfg;
using Slrr.Vm;

namespace Slrr.Game;

/// <summary>A native the host registers on top of the engine areas, why, and how sure we are.</summary>
/// <param name="Key">Class.name(descriptor).</param>
/// <param name="Kind">"replace" (wraps/overrides an area's registration) or "new" (nobody registered it).</param>
/// <param name="Reason">What blocked without it (the evidence).</param>
/// <param name="Label">MEASURED or GUESSED.</param>
public sealed record HostGap(string Key, string Kind, string Reason, string Label);

/// <summary>
/// The host's gap-closing natives, registered after the engine areas with replace semantics. Each one is listed in
/// <see cref="Gaps"/> (and docs/phase4/game.md) with the evidence of what blocked without it. Engine.* projects are
/// not edited; the wrappers call the areas' public APIs and delegate to the original registration where possible.
/// </summary>
public sealed class HostNatives(GameHost host) : INativeModule
{
    private const string GR = "java.util.resource.GameRef";
    private const string GT = "java.lang.GameType";
    private const string LRR = "Ljava.util.resource.ResourceRef;";
    private const string LGR = "Ljava.util.resource.GameRef;";
    private const string LS = "Ljava.lang.String;";

    /// <summary>GameType.GII_CONTROL / GII_ANIMATE (measured, GameType constants).</summary>
    public const int GiiControl = 8;
    public const int GiiAnimate = 28;

    private readonly Dictionary<int, PartDefinition?> _definitions = [];
    private readonly Dictionary<(int Id, int Kind), VmThread> _callbackThreads = [];

    public List<HostGap> Gaps { get; } = [];

    /// <summary>Commands handled by the parts layer (install/remove/repair/add_rollbar), in order.</summary>
    public List<(int Target, string Text)> PartCommands { get; } = [];

    /// <summary>Commands consumed by <see cref="IPhysicsWorld.Command"/>.</summary>
    public List<(int Target, string Text)> PhysicsCommands { get; } = [];

    public int CallbacksRun { get; private set; }

    /// <summary><c>install</c> commands that linked only after falling back to a car-tree search.</summary>
    public int TreeSearchInstalls { get; private set; }

    /// <summary><c>install</c> commands that linked only through <see cref="LinkByReverseRule"/>.</summary>
    public int ReverseRuleInstalls { get; private set; }

    /// <summary><c>install</c> commands that left the part unlinked.</summary>
    public int FailedInstalls { get; private set; }

    public List<string> InstallFailures { get; } = [];

    /// <summary>
    /// Link rule the parts area lacks (GUESSED, the mirror of its own rule 3): the CHILD slot <c>attach (T, S)</c> and the
    /// PARENT slot is <c>compatible (T, S)</c>. Shipped example (MEASURED from the cfgs): every tyre's slot 1 has
    /// <c>attach 0x00020200 2</c> and every rim's slot 2 has <c>compatible 0x00020200 2</c>; without it no tyre mounts.
    /// Search order: the named parent part, then the car tree depth first. Links with <see cref="PartsWorld.ForceLink"/>.
    /// </summary>
    private bool LinkByReverseRule(Slrr.Vm.Vm vm, PartState child, string[] tok)
    {
        int I(int i) => i < tok.Length && int.TryParse(tok[i], out var v) ? v : 0;
        var parts = host.Parts;
        PartState? Resolve(int id) => parts.Find(id == 0 ? null : parts.Host.Resolve(id) is { } o ? parts.Host.PartBehind(o) : null);
        var car = Resolve(I(2));
        var named = Resolve(I(4));
        var candidates = new List<PartState>();
        if (named is not null)
        {
            candidates.Add(named);
        }

        if (car is not null)
        {
            candidates.AddRange(PartsWorld.Tree(car).Where(p => !ReferenceEquals(p, named)));
        }

        foreach (var parent in candidates)
        {
            if (ReferenceEquals(parent, child) || PartsWorld.Tree(child).Any(d => ReferenceEquals(d, parent)))
            {
                continue;
            }

            foreach (var ps in parent.Slots.Where(s => s.Index >= 0 && s.Linked is null && !s.Disabled && s.Definition is { Compatible.Count: > 0 }))
            {
                foreach (var cs in child.Slots.Where(s => s.Linked is null && !s.Disabled && s.Definition is { Attach.Count: > 0 }))
                {
                    if (cs.Definition!.Attach.Intersect(ps.Definition!.Compatible).Any()
                        && parts.ForceLink(vm, child, cs.Id, parent, ps.Id, car))
                    {
                        return true;
                    }
                }
            }
        }

        return false;
    }

    public void Register(NativeRegistry r)
    {
        RegisterCreate(r);
        RegisterCommands(r);
        RegisterGetInfo(r);
        RegisterIsScripted(r);
        RegisterForceUpdate(r);
        RegisterMissing(r);
        RegisterDeviceNames(r);
        host.Parts.DefinitionOf = DefinitionOf;
    }

    /// <summary>Fog settings of <c>GroundRef.setFog(color, near, far)</c> per ground instance (for IRenderer).</summary>
    public Dictionary<int, (int Color, float Near, float Far)> Fog { get; } = [];

    /// <summary>Natives no engine area registers that the garage/menu path calls (first hits in the play transcript).</summary>
    private void RegisterMissing(NativeRegistry r)
    {
        const string sfx = "java.util.resource.SfxRef";
        const string v3 = "java.lang.Vector3";
        const string lv = "Ljava.lang.Vector3;";
        const string ly = "Ljava.lang.Ypr;";
        AddIfMissing(r, sfx, "nplay", $"({lv}FFFII)I", c =>
        {
            System.Numerics.Vector3? pos = c.Object(0) is { } p
                ? new System.Numerics.Vector3(Probe.Field(p, "x").AsFloat, Probe.Field(p, "y").AsFloat, Probe.Field(p, "z").AsFloat)
                : null;
            return Value.Int(host.Audio.Play(ResourceEngine.IdOf(c.Self), pos, c.Float(1), c.Float(2), c.Float(3), c.Int(4)));
        }, "every menu click (SfxRef.play -> nplay); routed to IAudioSink.Play, returns its handle", "signature MEASURED; handle semantics GUESSED");
        AddIfMissing(r, sfx, "stop", "(I)V", c =>
        {
            host.Audio.Stop(c.Int(0));
            return Value.Void;
        }, "Garage.exit stops the jack loop sample; routed to IAudioSink.Stop", "signature MEASURED");
        AddIfMissing(r, v3, "rotate", $"({ly}){lv}", c =>
        {
            var y = c.Object(0);
            Rotate(c.Self, Probe.Field(y, "y").AsFloat, Probe.Field(y, "p").AsFloat, Probe.Field(y, "r").AsFloat);
            return Value.Obj(c.Self);
        }, "MainMenu car camera and InventoryPanel.createDefCamera (every inventory panel) rotate offsets by a Ypr; rotates in place and returns this",
            "in-place + return MEASURED from call sites (result ignored, vector reused); rotation order GUESSED: roll(Z), pitch(X), yaw(Y), left-handed Y-up");
        AddIfMissing(r, "java.util.resource.GroundRef", "setFog", "(IFF)V", c =>
        {
            Fog[ResourceEngine.IdOf(c.Self)] = (c.Int(0), c.Float(1), c.Float(2));
            return Value.Void;
        }, "Scene.addSceneElements (garage) sets the map fog; stored for IRenderer", "argument roles (color, near, far) GUESSED from Camera.setFog");
    }

    private void AddIfMissing(NativeRegistry r, string cls, string name, string desc, NativeMethod fn, string reason, string label)
    {
        if (r.Contains(cls, name, desc))
        {
            return;
        }

        r.Register(cls, name, desc, fn);
        Gap($"{cls}.{name}{desc}", "new", reason, label);
    }

    private static void Rotate(VmObject v, float yaw, float pitch, float roll)
    {
        float x = Probe.Field(v, "x").AsFloat, y = Probe.Field(v, "y").AsFloat, z = Probe.Field(v, "z").AsFloat;
        float cr = MathF.Cos(roll), sr = MathF.Sin(roll);
        (x, y) = ((x * cr) - (y * sr), (x * sr) + (y * cr));
        float cp = MathF.Cos(pitch), sp = MathF.Sin(pitch);
        (y, z) = ((y * cp) - (z * sp), (y * sp) + (z * cp));
        float cy = MathF.Cos(yaw), sy = MathF.Sin(yaw);
        (x, z) = ((x * cy) + (z * sy), (-x * sy) + (z * cy));
        Slrr.Engine.Ui.Script.Set(v, "x", Value.Float(x));
        Slrr.Engine.Ui.Script.Set(v, "y", Value.Float(y));
        Slrr.Engine.Ui.Script.Set(v, "z", Value.Float(z));
    }

    private bool _inForceUpdate;

    /// <summary>Chassis.updatevariables() calls made by the host's forceUpdate wrapper.</summary>
    public int ChassisUpdates { get; private set; }

    /// <summary>Script errors inside those updatevariables() calls (caught so the caller continues).</summary>
    public List<string> ChassisUpdateErrors { get; } = [];

    private void RegisterForceUpdate(NativeRegistry r)
    {
        const string chassis = "java.game.parts.bodypart.Chassis";
        var original = r.Find(chassis, "forceUpdate", "()V");
        r.Register(chassis, "forceUpdate", "()V", c =>
        {
            if (!_inForceUpdate && Probe.Field(c.Self, "suspend_update").AsInt == 0
                && c.Self.Class.FindMethods("updatevariables", 0).FirstOrDefault(m => !m.IsStatic && m.Code is not null) is { } uv)
            {
                _inForceUpdate = true;
                try
                {
                    c.Vm.InvokeVoid(uv, c.Self);
                    ChassisUpdates++;
                }
                catch (VmException ex)
                {
                    // The VM unwinds the nested call; the snapshot below still runs (as before this wrapper existed).
                    ChassisUpdateErrors.Add($"{c.Self.Class.Name}.updatevariables: {ex.Message} at {ex.ScriptTrace.FirstOrDefault()}");
                }
                finally
                {
                    _inForceUpdate = false;
                }
            }

            return original is null ? Value.Void : original(c);
        }, replace: true);
        Gap($"{chassis}.forceUpdate()V", "replace",
            "Nobody called the script Chassis.updatevariables(), so Block.dynodata.calcDyno never ran: maxTorque stayed 0 and every "
            + "car was 'not in working condition' (Vehicle.isDriveable), blocking track selection. Now forceUpdate() (Vehicle ctor, "
            + "Garage) first runs updatevariables() on the chassis (once, not re-entrant), then the parts area's snapshot.",
            "exe strings `updatevariables` x4 MEASURED; calling it from forceUpdate GUESSED (suspend_update is the first field the routine reads)");
    }

    private void RegisterIsScripted(NativeRegistry r)
    {
        var desc = $"({LS})I";
        var original = r.Find(GR, "isScripted", desc);
        r.Register(GR, "isScripted", desc, c =>
        {
            var id = ResourceEngine.IdOf(c.Self);
            if (host.Resources.Tree.Get(id) is { IsInstance: false } && host.Resources.Gametypes.Get(id) is { Binding: ScriptBinding.Inherited, NativeKind: null })
            {
                return Value.Zero;
            }

            return original is null ? Value.Zero : original(c);
        }, replace: true);
        Gap($"{GR}.isScripted{desc}", "replace",
            "Catalog's tree walk treats an entry as a part when isScripted(\"java.game.parts.Part\") and as a folder (\"dummy eloszto node\") "
            + "otherwise. Category folders (parts.rpk:0xF23E Tyres, ...) inherit `script java.game.parts.Part` from the parts root, so the "
            + "Resources answer made every folder a part: no tyres/rims listed, one '<unnamed>' item. Now an entry that only INHERITS its "
            + "script and has no `native` line answers 0 (the 13 inherited `native part` entries stay scripted).",
            "behaviour MEASURED from Catalog.collectObjectsStep; rule GUESSED");
    }

    private void RegisterDeviceNames(NativeRegistry r)
    {
        const string input = "java.io.Input";
        const string desc = "(I)Ljava.lang.String;";
        var original = r.Find(input, "getDeviceName", desc);
        if (original is null)
        {
            return;
        }

        r.Register(input, "getDeviceName", desc, c =>
        {
            var v = original(c);
            return v.Ref?.Native is string s ? Value.Obj(host.SystemArea.Intern(c.Vm, s)) : v;
        }, replace: true);
        Gap($"{input}.getDeviceName{desc}", "replace",
            "ControlSet.load matches the device names read from save/controls/active_control_set (File.readString, interned by the "
            + "System area) with `==` against Input.getDeviceName, which returned a fresh String: no name ever matched, every device "
            + "of the file became an unknown negative id and the keyboard throttle/brake/steering never reached the car. The name is "
            + "now returned from the same intern pool.",
            "call site MEASURED (ControlSet.load); string identity in the exe GUESSED (docs/engine/system.md string pool hypothesis)");
    }

    private void Gap(string key, string kind, string reason, string label) => Gaps.Add(new HostGap(key, kind, reason, label));

    // ------------------------------------------------------------------ GameRef.create with part definitions

    private void RegisterCreate(NativeRegistry r)
    {
        var desc = $"({LGR}{LGR}{LS}{LS})Ljava.lang.GameType;";
        r.Register(GR, "create", desc, c => Value.Obj(Create(c.Self, ResourceEngine.IdOf(c.Object(0)), ResourceEngine.IdOf(c.Object(1)), c.String(2), c.String(3))), replace: true);
        Gap($"{GR}.create{desc}", "replace",
            "ResourceEngine.Create runs the part constructor before anyone can give PartsWorld the cfg definition (docs/engine/parts.md "
            + "'Part creation' hook): parts got empty slot tables, so install found no slot. Now: same create, then PartsWorld.Attach(obj, "
            + "cfg of the gametype's `native part|car <cfg>` line, typeId) before <init>(int).",
            "MEASURED (create order 0x53a5e0; cfg line M); slot rules GUESSED (parts area)");
    }

    /// <summary>
    /// <c>GameRef.create</c> (exe 0x47d7b0 → 0x53a5e0) as in <see cref="ResourceEngine.Create"/>, with the parts hook
    /// inserted between allocation and constructor.
    /// </summary>
    public VmObject? Create(VmObject receiver, int parentId, int typeId, string? parameters, string? name)
    {
        var res = host.Resources;
        var info = res.Gametypes.Get(typeId);
        res.Create(receiver, parentId, typeId, parameters, name, runScript: false);
        var node = res.NodeOf(receiver);
        if (node is null || !node.IsInstance || info?.Script is null)
        {
            return null;
        }

        var cls = res.ResolveScriptClass(info.Script);
        if (cls is null)
        {
            res.Log($"create: script class not found: {info.Script} ({ResourceIds.Format(typeId)})", error: true);
            return null;
        }

        var vm = host.Vm;
        var obj = vm.NewObject(cls);
        ResourceEngine.EnsureHandle(obj).Id = node.Id;
        node.Script = obj;
        if (cls.IsSubclassOf(PartsWorld.PartClass))
        {
            host.Parts.Attach(obj, DefinitionOf(typeId), typeId);
        }

        var ctor = FindIntConstructor(cls);
        if (ctor is null)
        {
            res.Log($"create: Undefined GameType Constructor! {(uint)node.Id:X8} ({cls.Name})", error: true);
            return obj;
        }

        if (vm.CurrentThread is not null)
        {
            vm.InvokeVoid(ctor, obj, Value.Int(node.Id));
        }
        else
        {
            res.RunToCompletion(vm.Spawn(ctor, obj, [Value.Int(node.Id)], "THRD-CREATE"));
        }

        return obj;
    }

    private static VmMethod? FindIntConstructor(VmClass cls)
    {
        for (var c = cls; c is not null; c = c.Super)
        {
            if (c.DeclaredMethod("<init>", "(I)") is { } m)
            {
                return m;
            }
        }

        return null;
    }

    /// <summary>Definition of a part/car gametype from its <c>native part|car &lt;cfg&gt;</c> line, cfg type ids mapped
    /// through the gametype's archive reference table (same rule as map records, docs/engine/resources.md section 1).</summary>
    public PartDefinition? DefinitionOf(int typeId)
    {
        if (_definitions.TryGetValue(typeId, out var def))
        {
            return def;
        }

        def = null;
        var info = host.Resources.Gametypes.Get(typeId);
        if (info is { NativeKind: "part" or "car", NativeArg: { } cfgPath } && host.Game.Resolve(cfgPath) is { } file)
        {
            try
            {
                var cfg = CfgDocument.Load(file, cfgPath);
                var archive = host.Resources.Tree.Get(typeId)?.Archive;
                def = PartDefinition.FromCfg(cfg, typeId, archive is null ? null : id => host.Resources.Archives.MapId(archive, unchecked((uint)id)));
            }
            catch (IOException ex)
            {
                host.Resources.Log($"part cfg {cfgPath}: {ex.Message}", error: true);
            }
        }

        _definitions[typeId] = def;
        return def;
    }

    // ------------------------------------------------------------------ EVENT_COMMAND routing

    private void RegisterCommands(NativeRegistry r)
    {
        var desc = $"({LRR}I{LS})V";
        var original = r.Find(GR, "queueEvent", desc);
        r.Register(GR, "queueEvent", desc, c =>
        {
            if (c.Int(1) == ResourceEngine.EventCommand && c.String(2) is { } text && Route(c.Vm, c.Self, text))
            {
                return Value.Void;
            }

            return original is null ? Value.Void : original(c);
        }, replace: true);
        Gap($"{GR}.queueEvent{desc}", "replace",
            "EVENT_COMMAND to a part only reached ResourceEngine's queue (delivered to a script handleEvent with the mask, never to "
            + "PartsWorld.ExecuteCommand), so `install`/`remove` from Garage/Inventory never linked anything. Now: part verbs go to "
            + "PartsWorld.ExecuteCommand synchronously, other verbs to IPhysicsWorld.Command, the rest to the original queue.",
            "MEASURED (Part.command = queueEvent(null, EVENT_COMMAND, s), parser 0x475f6d); synchronous execution GUESSED");
    }

    private bool Route(Slrr.Vm.Vm vm, VmObject self, string text)
    {
        var id = ResourceEngine.IdOf(self);
        var target = host.Resources.Tree.Get(id)?.Script ?? self;
        if (target.Class.IsSubclassOf(PartsWorld.PartClass) && host.Parts.ExecuteCommand(vm, target, text))
        {
            PartCommands.Add((id, text));
            var tok = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (tok is ["install", _, _, _, var parentTok, ..] && parentTok != "0" && host.Parts.Find(target)?.Parent is null)
            {
                // GUESSED: Part.addPart always names `this` as the parent (chassis.addPart(tyre)), yet in the game the tyre
                // ends up on the rim: when the named part has no matching slot, search the whole car tree.
                var retry = (string[])tok.Clone();
                retry[4] = "0";
                if (host.Parts.ExecuteCommand(vm, target, string.Join(' ', retry)) && host.Parts.Find(target)?.Parent is not null)
                {
                    TreeSearchInstalls++;
                }
            }

            if (tok[0] == "install" && host.Parts.Find(target) is { Parent: null } child)
            {
                if (LinkByReverseRule(vm, child, tok))
                {
                    ReverseRuleInstalls++;
                }
                else
                {
                    FailedInstalls++;
                    InstallFailures.Add($"{child} <- '{text}'");
                }
            }

            Reparent(id, target, text);
            return true;
        }

        if (host.Physics.Command(id, text))
        {
            PhysicsCommands.Add((id, text));
            return true;
        }

        return false;
    }

    /// <summary>
    /// The instance tree follows the part links: <c>Part.addPart</c> sends <c>install</c> and then DESTROYS the new part
    /// unless <c>getParentID()</c> changed (MEASURED from the script: <c>gameRef7.getParentID() != i5</c>), so a linked
    /// part must leave the map node. Linked → child of its parent part's instance (GUESSED: could be the car root);
    /// <c>remove f mapId</c> → child of mapId (MEASURED role of the second int, entry-points.md section 5).
    /// </summary>
    private void Reparent(int id, VmObject target, string text)
    {
        var tree = host.Resources.Tree;
        if (tree.Get(id) is not { IsInstance: true } node)
        {
            return;
        }

        var tok = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (tok[0] == "install" && host.Parts.Find(target)?.Parent is { } parent && ResourceEngine.IdOf(parent.Object) is var pid and not 0 && pid != node.ParentId)
        {
            tree.SetParent(node, pid);
        }
        else if (tok[0] == "remove" && tok.Length >= 3 && int.TryParse(tok[2], out var mapId) && mapId != 0 && tree.Get(mapId) is not null)
        {
            tree.SetParent(node, mapId);
        }
    }

    // ------------------------------------------------------------------ getInfo

    private void RegisterGetInfo(NativeRegistry r)
    {
        foreach (var desc in new[] { "(II)I", $"(I{LS})I" })
        {
            var original = r.Find(GR, "getInfo", desc);
            var isInt = desc == "(II)I";
            r.Register(GR, "getInfo", desc, c =>
            {
                var target = host.Resources.NodeOf(c.Self)?.Script ?? c.Self;
                // (I String): Catalog passes the other instance id as text (`p.getInfo(GII_COMPATIBLE, car.id() + "")`, MEASURED).
                var arg = isInt ? c.Int(1) : int.TryParse(c.String(1)?.Trim(), out var a) ? a : 0;
                if (target.Class.IsSubclassOf(PartsWorld.PartClass) && host.Parts.TryGetInfo(target, c.Int(0), arg, out var v))
                {
                    return Value.Int(v);
                }

                return original is null ? Value.Zero : original(c);
            }, replace: true);
        }

        Gap($"{GR}.getInfo(II)I", "replace",
            "ResourceEngine.GetInfo answers only GII_ID/GII_TYPE and asks the script for the rest; the parts layer's answers "
            + "(GII_PART_OWNER, GII_INSTALL_OK, GII_COMPATIBLE, GII_REMOVE_OK, GII_PART_CATEGORY) were unreachable.",
            "key numbers MEASURED, answers GUESSED (P0 #6)");
    }

    // ------------------------------------------------------------------ GameType callbacks

    /// <summary>
    /// <c>registerCallback(GII_ANIMATE)</c> / <c>(GII_CONTROL)</c> (GameType.enableAnimateHook/enableControlHook) are
    /// stored by the Resources area but nobody calls <c>animate()</c> / <c>control(dt)</c>. Called once per frame by the
    /// host: one call per registered object per frame, on its own thread, skipped while the previous call still runs
    /// (GUESSED: the exe calls them from its frame loop, strings <c>animate</c>/<c>control</c> at 0x60c9xx; rate unknown).
    /// </summary>
    public void RunCallbacks(float dt)
    {
        if (Gaps.All(g => g.Key != "GameType.animate/control"))
        {
            Gap("GameType.animate/control", "new",
                "GII_ANIMATE/GII_CONTROL callbacks were stored but never invoked: Osd.animate (tooltips, sliders, text input), "
                + "MainMenu camera, Garage control hooks did not run.",
                "names MEASURED (exe strings), per-frame rate GUESSED");
        }

        foreach (var node in host.Resources.Tree.Instances.Where(n => n.Callbacks.Count > 0 && n.Script is not null).ToList())
        {
            foreach (var kind in node.Callbacks.ToList())
            {
                if (kind is not (GiiAnimate or GiiControl))
                {
                    continue;
                }

                if (_callbackThreads.TryGetValue((node.Id, kind), out var t) && t.State != VmThreadState.Finished)
                {
                    continue;
                }

                var obj = node.Script!;
                var m = kind == GiiAnimate
                    ? obj.Class.FindMethods("animate", 0).FirstOrDefault(x => !x.IsStatic && x.Code is not null)
                    : obj.Class.FindMethods("control", 1).FirstOrDefault(x => !x.IsStatic && x.Code is not null);
                if (m is null)
                {
                    continue;
                }

                Value[] args = kind == GiiAnimate ? [] : [Value.Float(dt)];
                _callbackThreads[(node.Id, kind)] = host.Vm.Spawn(m, obj, args, kind == GiiAnimate ? "THRD-ANIMATE" : "THRD-CONTROL");
                CallbacksRun++;
            }
        }

        host.Driver.Settle();
    }
}
