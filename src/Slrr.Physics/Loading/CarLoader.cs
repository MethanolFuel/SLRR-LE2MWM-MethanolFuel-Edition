using Slrr.Core;
using Slrr.Engine.Parts;
using Slrr.Engine.Resources;
using Slrr.Formats.Cfg;
using Slrr.Physics.Dyno;
using Slrr.Vm;

namespace Slrr.Physics.Loading;

/// <summary>A car gametype found in the shipped archives (<c>native car &lt;cfg&gt;</c> entries).</summary>
/// <remarks><see cref="EntryId"/> is the archive-local entry id; runtime ids depend on mount order, so each load re-maps it.</remarks>
public sealed record CarType(uint EntryId, string Name, string Archive, string? Script, string? Cfg);

/// <summary>Result of <see cref="CarLoader.Load"/>.</summary>
public sealed class LoadedCar
{
    public required CarType Type { get; init; }

    public required PartState Chassis { get; init; }

    public required PartsWorld World { get; init; }

    public required Vm.Vm Vm { get; init; }

    public required ExeDynoCalculator Dyno { get; init; }

    /// <summary>Every part of the installed tree (chassis first).</summary>
    public required IReadOnlyList<PartState> Parts { get; init; }

    /// <summary>Engine messages, parts diagnostics and script exceptions collected while loading.</summary>
    public required IReadOnlyList<string> Log { get; init; }

    /// <summary>True when <c>Chassis.updatevariables()</c> ran to the end in the VM.</summary>
    public bool ScriptsCompleted { get; init; }

    /// <summary>True when the dyno tail of <c>Chassis.updatevariables</c> had to be replayed in C#
    /// (<see cref="CarLoader.CompleteEngine"/>): the engine numbers are then NOT what the game computes.</summary>
    public bool EngineCompletedInCSharp { get; init; }
}

/// <summary>
/// Loads a car the way the game builds one, headless: the resource layer (<c>Slrr.Engine.Resources</c>) creates
/// gametype instances, the parts layer (<c>Slrr.Engine.Parts</c>) attaches native twins from the part cfgs, and the
/// car's own scripts run <c>addStockParts()</c> → <c>Part.addPart</c> → <c>GameRef.create</c> + <c>command("install ...")</c>,
/// then <c>updatevariables()</c> (which runs <c>calcDyno</c> through <see cref="ExeDynoCalculator"/>) and
/// <c>forceUpdate()</c>. Every other engine native is a no-op stub (sound, render, physics refs).
/// <para>Integration glue (GUESSED, needed because those two areas do not know each other yet): GameRef.create is
/// re-registered to call <see cref="PartsWorld.Attach"/> between allocation and the <c>&lt;init&gt;(int)</c> constructor;
/// GameRef.queueEvent hands EVENT_COMMAND (16) to <see cref="PartsWorld.ExecuteCommand"/> first; GameRef.getInfo asks
/// <see cref="PartsWorld.TryGetInfo"/> first. The lead may replace this with the real integration.</para>
/// </summary>
public static class CarLoader
{
    private const string GR = ResourceEngine.GameRefClass;
    private const string LGR = "Ljava.util.resource.GameRef;";
    private const string LRR = "Ljava.util.resource.ResourceRef;";
    private const string LS = "Ljava.lang.String;";
    private static readonly char[] Separators = [' ', '\t'];

    /// <summary>Every <c>native car</c> gametype of the archives under <c>cars\</c>.</summary>
    public static IReadOnlyList<CarType> FindCars(string root)
    {
        var game = new GameFileSystem(root);
        var archives = new ArchiveTable(game);
        var tree = new ResourceTree(archives);
        var registry = new GametypeRegistry(tree);
        var list = new List<CarType>();
        foreach (var file in game.Enumerate(".rpk").Order(StringComparer.Ordinal))
        {
            var rel = Path.GetRelativePath(game.Root, file).Replace('/', '\\');
            if (!rel.StartsWith("cars", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var index = archives.Mount(rel);
            if (archives[index] is not { State: ArchiveState.Loaded } slot)
            {
                continue;
            }

            foreach (var e in slot.Archive!.Entries)
            {
                var id = archives.MapId(slot, e.Id);
                if (tree.Get(id) is not { } node || node.Entry != e || node.Text?.Get("native") is null)
                {
                    continue;
                }

                var g = registry.Get(id);
                if (g?.NativeKind == "car")
                {
                    list.Add(new CarType(e.Id, g.Name, rel, g.Script, g.NativeArg));
                }
            }
        }

        return list;
    }

    /// <summary>First car whose gametype name, script or cfg contains <paramref name="query"/> (case-insensitive).</summary>
    public static CarType? FindCar(string root, string query) =>
        FindCars(root).FirstOrDefault(c => c.Name.Equals(query, StringComparison.OrdinalIgnoreCase))
        ?? FindCars(root).FirstOrDefault(c =>
            c.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
            || (c.Cfg?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false)
            || (c.Script?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false));

    public static LoadedCar Load(string root, CarType car, EngineSettingsHook? hook = null)
    {
        var game = new GameFileSystem(root);
        var engine = new ResourceEngine(game);
        var log = new List<string>();
        var dyno = new ExeDynoCalculator();
        var host = new ResourceHost(engine);
        var world = new PartsWorld(host, dyno: dyno);
        var registry = NativeRegistry.CreateDefault();
        registry.Add(new ResourceNatives(engine));
        registry.Add(new ArchiveNatives(engine));
        registry.Add(new PartsNatives(world));
        var definitions = new Dictionary<int, PartDefinition?>();

        PartDefinition? DefinitionOf(int typeId)
        {
            if (definitions.TryGetValue(typeId, out var d))
            {
                return d;
            }

            d = null;
            var info = engine.Gametypes.Get(typeId);
            var cfgPath = info?.NativeArg is { } a ? game.Resolve(a) : null;
            if (cfgPath is not null && engine.Archives[ResourceIds.Archive(typeId)] is { } slot)
            {
                d = PartDefinition.FromCfg(CfgDocument.Load(cfgPath), typeId, id => id == 0 ? 0 : engine.Archives.MapId(slot, (uint)id));
            }

            if (d is null)
            {
                log.Add($"no part definition for {ResourceIds.Format(typeId)} ({info?.Name}, native {info?.NativeKind} {info?.NativeArg})");
            }

            definitions[typeId] = d;
            return d;
        }

        world.DefinitionOf = DefinitionOf;

        // GameRef.create with the parts attach step between allocation and constructor.
        registry.Register(GR, "create", $"({LGR}{LGR}{LS}{LS})Ljava.lang.GameType;", c =>
        {
            var vm = c.Vm;
            var receiver = c.This ?? vm.NewObject(vm.LoadClass(GR));
            var typeId = ResourceEngine.IdOf(c.Object(1));
            engine.Create(receiver, ResourceEngine.IdOf(c.Object(0)), typeId, c.String(2), c.String(3), runScript: false);
            var node = engine.NodeOf(receiver);
            var info = engine.Gametypes.Get(typeId);
            if (node is null || info?.Script is null || engine.ResolveScriptClass(info.Script) is not { } cls)
            {
                return Value.Null;
            }

            var obj = vm.NewObject(cls);
            ResourceEngine.EnsureHandle(obj).Id = node.Id;
            node.Script = obj;
            if (cls.IsSubclassOf(PartsWorld.PartClass))
            {
                world.Attach(obj, DefinitionOf(typeId), typeId);
            }

            if (FindIntConstructor(cls) is { } ctor)
            {
                vm.InvokeVoid(ctor, obj, Value.Int(node.Id));
            }

            return Value.Obj(obj);
        }, replace: true);
        registry.Register(GR, "queueEvent", $"({LRR}I{LS})V", c =>
        {
            var target = engine.NodeOf(c.Self)?.Script ?? c.Self;
            var text = c.String(2) ?? string.Empty;
            if (!(c.Int(1) == ResourceEngine.EventCommand && Command(c.Vm, c.Self, target, text)))
            {
                engine.QueueEvent(ResourceEngine.IdOf(c.Self), ResourceEngine.IdOf(c.Object(0)), c.Int(1), c.String(2));
            }

            return Value.Void;
        }, replace: true);
        // GUESSED glue: `install` naming the car itself as parent (Part.addPart on the chassis for a tyre, a mirror...)
        // is retried with parentPart 0 (search the car's tree); after a link the child's instance node is re-parented
        // under its parent part, which Part.addPart tests (`getParentID() != carParent`) before addStockParts().
        bool Command(Vm.Vm vm, VmObject handle, VmObject target, string text)
        {
            if (!world.ExecuteCommand(vm, target, text))
            {
                return false;
            }

            var tok = text.Split(Separators, StringSplitOptions.RemoveEmptyEntries);
            if (tok.Length >= 6 && tok[0] == "install" && world.Find(target) is { Parent: null } && tok[4] != "0")
            {
                tok[4] = "0";
                tok[5] = "0";
                world.ExecuteCommand(vm, target, string.Join(' ', tok));
            }

            if (tok.Length >= 3 && tok[0] == "install" && world.Find(target) is { Parent: null } loose
                && int.TryParse(tok[2], out var carId) && host.Resolve(carId) is { } carObj && world.Find(carObj) is { } carPart)
            {
                LinkBySuperType(vm, loose, carPart);
            }

            if (tok.Length > 0 && tok[0] == "install" && world.Find(target)?.Parent is { } parent && engine.NodeOf(handle) is { IsInstance: true } node)
            {
                engine.Tree.SetParent(node, ResourceEngine.IdOf(parent.Object));
            }

            return true;
        }

        // GUESSED glue: the parts layer matches `attach T S` against the parent's exact type id. The shipped tyre cfgs
        // attach to the generic wheel type (parts.rpk:0x200, slot 2), and every rim declares on its slot 2
        // `compatible 0x...0200 2`: link a still-loose child to the first free parent slot that declares the child's
        // (T, S) compatible, or whose part type has T in its gametype superId chain.
        void LinkBySuperType(Vm.Vm vm, PartState child, PartState carPart)
        {
            foreach (var cs in child.Definition?.Slots ?? [])
            {
                foreach (var a in cs.Attach)
                {
                    foreach (var p in PartsWorld.Tree(carPart))
                    {
                        var ps = p.Definition?.Slots.FirstOrDefault(x => x.Id == a.SlotId);
                        if (p != child && ps is not null && p.Slot(a.SlotId) is not { Linked: not null }
                            && (ps.Compatible.Contains(a) || IsA(engine, p.TypeId, a.TypeId))
                            && world.ForceLink(vm, child, cs.Id, p, a.SlotId, carPart))
                        {
                            log.Add($"linked {child} slot {cs.Id} -> {p} slot {a.SlotId} (parent slot `compatible` or superId match, glue)");
                            return;
                        }
                    }
                }
            }
        }

        registry.Register(GR, "getInfo", "(II)I", c =>
        {
            var target = engine.NodeOf(c.Self)?.Script ?? c.Self;
            return Value.Int(world.TryGetInfo(target, c.Int(0), c.Int(1), out var v) ? v : engine.GetInfo(c.Self, c.Int(0)));
        }, replace: true);
        registry.Fallback = NativeRegistry.DefaultStub;

        var vm = new Vm.Vm(new VmClassPath(game), registry) { InstructionBudget = 2_000_000_000 };
        engine.Attach(vm);
        hook?.Invoke(world);

        // The car: create the gametype under the world root, then the scripts build the rest.
        var gameRefClass = vm.LoadClass(GR);
        var handle = vm.NewObject(gameRefClass);
        var typeRef = vm.NewObject(gameRefClass);
        var carSlot = engine.Archives[engine.Archives.Mount(car.Archive)] ?? throw new InvalidOperationException($"archive {car.Archive} not found");
        ResourceEngine.EnsureHandle(typeRef).Id = engine.Archives.MapId(carSlot, car.EntryId);
        var chassisObj = vm.InvokeVirtual(handle, "create", Value.Null, Value.Obj(typeRef), Value.Obj(vm.NewString("0,0,0,0,0,0")), Value.Obj(vm.NewString("car"))).AsObject
            ?? throw new InvalidOperationException($"car gametype {car.Name} produced no script object");
        var chassis = world.Find(chassisObj) ?? throw new InvalidOperationException($"{car.Name}: script {chassisObj.Class.Name} is not a Part");

        Try(log, "bind GameLogic.player.car", () => BindPlayerCar(vm, chassisObj));

        // What java.game.Vehicle does for a new car: chassis.addStockParts(new Descriptor(color, optical, power, wear, tear)).
        var descriptor = vm.Construct(vm.LoadClass("java.game.parts.Descriptor"), Value.Int(0), Value.Float(1f), Value.Float(1f), Value.Float(1f), Value.Float(1f));
        Try(log, "addStockParts", () => vm.InvokeVirtual(chassisObj, "addStockParts", Value.Obj(descriptor)));
        var complete = Try(log, "updatevariables", () => vm.InvokeVirtual(chassisObj, "updatevariables"));
        var dynoCompleted = false;
        if (!complete)
        {
            dynoCompleted = CompleteEngine(vm, chassis, log);
        }

        Try(log, "forceUpdate", () => vm.InvokeVirtual(chassisObj, "forceUpdate"));
        log.AddRange(engine.Messages.Where(m => m.IsError).Select(m => "engine: " + m.Text).Distinct().Take(50));
        log.AddRange(world.Diagnostics.Select(d => "parts: " + d).Distinct().Take(50));
        return new LoadedCar
        {
            Type = car,
            Chassis = chassis,
            World = world,
            Vm = vm,
            Dyno = dyno,
            Parts = PartsWorld.Tree(chassis).ToList(),
            Log = log,
            ScriptsCompleted = complete,
            EngineCompletedInCSharp = dynoCompleted,
        };
    }

    /// <summary>
    /// MEASURED script dependency: <c>TyreCalculations.getWeightOnWheel</c> (Tyre.SetInflation) reads
    /// <c>GameLogic.player.car.chassis</c> (mass, CM, wheel positions of the PLAYER's car, whatever car the tyre is on).
    /// Headless there is no player: make one whose car is this chassis (the game state when the player builds a car).
    /// </summary>
    public static void BindPlayerCar(Vm.Vm vm, VmObject chassis)
    {
        var gl = vm.LoadClass("java.game.GameLogic");
        vm.EnsureInitialized(gl);
        var playerField = gl.FindField("player");
        if (playerField is not { IsStatic: true })
        {
            return;
        }

        if (gl.StaticValues[playerField.Slot].Kind != VmKind.Object || gl.StaticValues[playerField.Slot].IsNull)
        {
            gl.StaticValues[playerField.Slot] = Value.Obj(vm.NewObject(vm.LoadClass(ClassOf(playerField.Descriptor))));
        }

        var player = gl.StaticValues[playerField.Slot].AsObject!;
        var carField = player.Class.FindField("car") ?? throw new VmException("player has no field car");
        var vehicle = ScriptObjects.GetObject(player, "car") ?? vm.NewObject(vm.LoadClass(ClassOf(carField.Descriptor)));
        ScriptObjects.Set(vehicle, "chassis", Value.Obj(chassis));
        ScriptObjects.Set(player, "car", Value.Obj(vehicle));
    }

    private static string ClassOf(string descriptor) =>
        descriptor.StartsWith('L') && descriptor.EndsWith(';') ? descriptor[1..^1] : throw new VmException($"not a class descriptor: {descriptor}");

    /// <summary>
    /// WORKAROUND (not game behaviour): when a script error stops <c>Chassis.updatevariables()</c> inside the engine
    /// (today: <c>Block_Inline.updatevariables</c> line 137 evaluates <c>v4 &amp;&amp; v2</c> on floats, which the exe accepts
    /// through JVM::booleanConversion 0x417840 but <c>Slrr.Vm</c> rejects), replay the rest of the dyno section of
    /// Chassis.java (lines 640-664) in C# on whatever the block chain wrote into its DynoData before the error:
    /// RPM_limit, maxRPM rounding to 250-rpm steps, <c>calcDyno(maxRPM/250)</c>, and the copies into the chassis fields.
    /// </summary>
    public static bool CompleteEngine(Vm.Vm vm, PartState chassis, List<string> log)
    {
        var block = PartsWorld.Tree(chassis).FirstOrDefault(p => p.Object.Class.IsSubclassOf("java.game.parts.enginepart.Block"));
        if (block is null || ScriptObjects.GetObject(block.Object, "dynodata") is not { } dyno)
        {
            return false;
        }

        var c = chassis.Object;
        var rpmLimit = ScriptObjects.GetFloat(dyno, "RPM_limit");
        ScriptObjects.Set(c, "RPM_limit", Value.Float(rpmLimit));
        var maxRpm = ScriptObjects.GetFloat(dyno, "maxRPM");
        if (maxRpm < 100)
        {
            maxRpm = rpmLimit * 1.25f;
        }

        // Fields the block chain writes after the failing line still hold DynoData.clear()'s sentinels: substitute the
        // DynoData class defaults (GUESSED stand-ins, so that the replay yields an engine at all).
        string[] sentinels = ["in_min", "out_min", "in_max", "out_max"];
        if (sentinels.All(n => ScriptObjects.GetFloat(dyno, n) == 1f))
        {
            foreach (var n in sentinels.Concat(["time_in_open", "time_in_close", "time_out_open", "time_out_close"]))
            {
                ScriptObjects.Set(dyno, n, Value.Float(DynoDataDefaults.Values[n]));
            }

            log.Add("engine: valve areas/timing were unset (clear() sentinels): DynoData class defaults substituted (GUESSED)");
        }

        var steps = (int)(maxRpm / 250);
        maxRpm = steps * 250;
        ScriptObjects.Set(dyno, "maxRPM", Value.Float(maxRpm));
        if (!Try(log, "calcDyno (C# replay)", () => vm.InvokeVirtual(dyno, "calcDyno", Value.Float(steps))))
        {
            return false;
        }

        ScriptObjects.Set(c, "engine_torque", Value.Float(ScriptObjects.GetFloat(dyno, "torque")));
        ScriptObjects.Set(c, "engine_torque2", Value.Float(ScriptObjects.GetFloat(dyno, "torque2")));
        ScriptObjects.Set(c, "maxRPM", Value.Float(ScriptObjects.GetFloat(dyno, "maxRPM")));
        ScriptObjects.Set(c, "engine_inertia", Value.Float(ScriptObjects.GetFloat(block.Object, "inertia")));
        ScriptObjects.Set(c, "engine_friction_fwd", Value.Float(ScriptObjects.GetFloat(block.Object, "friction_fwd")));
        ScriptObjects.Set(c, "engine_friction_rev", Value.Float(ScriptObjects.GetFloat(block.Object, "friction_rev")));
        ScriptObjects.Set(c, "engine_rpm_idle", Value.Float(ScriptObjects.GetFloat(block.Object, "rpm_idle")));
        ScriptObjects.Set(c, "consumption_nitro", Value.Float(ScriptObjects.GetFloat(dyno, "nitro_consumption")));
        ScriptObjects.Set(c, "maxHP", Value.Float(ScriptObjects.GetFloat(dyno, "maxHP")));
        ScriptObjects.Set(c, "dynodata", Value.Obj(dyno));
        log.Add("engine: Chassis.updatevariables stopped by a script error; dyno section replayed in C# (numbers are NOT the game's)");
        return true;
    }

    private static bool Try(List<string> log, string what, Action a)
    {
        try
        {
            a();
            return true;
        }
        catch (VmException ex)
        {
            log.Add($"{what}: {ex.GetType().Name}: {ex.Message} at {string.Join(" < ", ex.ScriptTrace.Take(6))}");
            return false;
        }
    }

    /// <summary>Is <paramref name="typeId"/> or one of its gametype ancestors (entry superId chain) <paramref name="wanted"/>?</summary>
    private static bool IsA(ResourceEngine engine, int typeId, int wanted)
    {
        var cur = typeId;
        for (var depth = 0; cur != 0 && depth < 32; depth++)
        {
            if (cur == wanted)
            {
                return true;
            }

            var node = engine.Tree.Get(cur);
            if (node?.Entry is not { } entry || node.Archive is not { } archive)
            {
                return false;
            }

            cur = engine.Archives.MapId(archive, entry.SuperId);
        }

        return false;
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

    /// <summary><see cref="IPartHost"/> over the resource layer's handles and instance tree.</summary>
    private sealed class ResourceHost(ResourceEngine engine) : IPartHost
    {
        public int IdOf(VmObject obj) => ResourceEngine.IdOf(obj);

        public VmObject? Resolve(int id) => engine.Tree.Get(id)?.Script;

        public void Bind(VmObject gameRef, VmObject? target) => ResourceEngine.EnsureHandle(gameRef).Id = target is null ? 0 : ResourceEngine.IdOf(target);

        public VmObject? PartBehind(VmObject gameRef)
        {
            if (gameRef.Class.IsSubclassOf(PartsWorld.PartClass))
            {
                return gameRef;
            }

            if (engine.NodeOf(gameRef)?.Script is { } s && s.Class.IsSubclassOf(PartsWorld.PartClass))
            {
                return s;
            }

            return ScriptObjects.GetObject(gameRef, "chassis") is { } c && c.Class.IsSubclassOf(PartsWorld.PartClass) ? c : null;
        }
    }
}

/// <summary>Called after the VM is ready and before the car is created (e.g. to set <see cref="PartsWorld.Options"/>).</summary>
public delegate void EngineSettingsHook(PartsWorld world);
