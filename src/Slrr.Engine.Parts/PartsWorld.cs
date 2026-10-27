using System.Globalization;
using System.Numerics;
using System.Runtime.CompilerServices;
using Slrr.Engine.Parts.RawEdit;
using Slrr.Vm;

namespace Slrr.Engine.Parts;

/// <summary>Options for the parts data model; every default is a documented guess (docs/engine/parts.md).</summary>
public sealed class PartsOptions
{
    /// <summary>Call the script notifications on link changes: <c>parent.addpart(child)</c> then <c>child.install()</c>,
    /// and <c>parent.rempart(child)</c> then <c>child.remove()</c> (names measured in the exe string table; order and
    /// arguments guessed, open question P0 #5).</summary>
    public bool InvokeScriptCallbacks { get; set; } = true;

    /// <summary>forceUpdate() does nothing while the chassis field <c>suspend_update</c> is non-zero (guess: it is the
    /// first field the exe reads in that routine, 0x4484c4).</summary>
    public bool HonourSuspendUpdate { get; set; } = true;

    /// <summary>Wear/tear of a part nobody set yet (1.0 = new).</summary>
    public float DefaultCondition { get; set; } = 1f;
}

/// <summary>
/// Owner of the part/chassis/wheel native state for one VM: the native twins of Part script objects (slot table,
/// links, wear...), chassis wheels and sound tables, DynoData natives, and the RawEdit settings shim. Natives in
/// <see cref="PartsNatives"/> and the integration points for other areas (gametype creation, <c>GameRef.command</c>,
/// <c>GameRef.getInfo</c>) all go through this class.
/// </summary>
public sealed class PartsWorld
{
    public const string PartClass = "java.game.parts.Part";
    public const string ChassisClass = "java.game.parts.bodypart.Chassis";
    public const string WheelRefClass = "java.game.parts.WheelRef";
    public const string SfxTableClass = "java.game.parts.SfxTable";
    public const string DynoDataClass = "java.game.parts.DynoData";

    // Opaque handles stored in Native.ptr so RawEdit can decode `ptr + offset` (rawedit.md rule 4). Real exe
    // addresses are below 0x00800000, so these ranges cannot collide with the absolute address table.
    internal const int WheelHandleBase = 0x40000000;
    internal const int TelemetryHandleBase = 0x20000000;
    internal const int DynoHandleBase = 0x30000000;
    internal const int WheelSteerOffset = 0xC8;

    private static readonly char[] Separators = [' ', '\t'];
    private readonly ConditionalWeakTable<VmObject, PartState> _parts = [];
    private readonly ConditionalWeakTable<VmObject, DynoState> _dynos = [];
    private readonly ConditionalWeakTable<VmObject, SfxTableState> _sfx = [];
    private readonly Dictionary<int, WheelState> _wheelsByHandle = [];
    private readonly Dictionary<int, ChassisState> _chassisBySerial = [];
    private readonly HashSet<uint> _unknownAddresses = [];
    private int _nextChassis;
    private int _nextWheel;
    private int _nextDyno;

    public PartsWorld(IPartHost? host = null, EngineSettings? settings = null, IDynoCalculator? dyno = null, PartsOptions? options = null)
    {
        Host = host ?? new DefaultPartHost();
        Settings = settings ?? EngineSettings.MethanolFuel();
        Dyno = dyno ?? NullDynoCalculator.Instance;
        Options = options ?? new PartsOptions();
    }

    public IPartHost Host { get; }

    /// <summary>Typed replacement of the RawEdit address space.</summary>
    public EngineSettings Settings { get; }

    public IDynoCalculator Dyno { get; set; }

    public PartsOptions Options { get; }

    /// <summary>Notes about guessed or ignored operations (unknown RawEdit addresses, failed installs...).</summary>
    public List<string> Diagnostics { get; } = [];

    /// <summary>Raised after an effective forceUpdate() snapshot.</summary>
    public event Action<PartState, ChassisParams>? ChassisUpdated;

    /// <summary>Optional gametype id -&gt; definition lookup (resource layer) used by the <c>compatible</c> rule.</summary>
    public Func<int, PartDefinition?>? DefinitionOf { get; set; }

    /// <summary>Hook for <c>RawEdit.CentCur</c> (window layer): returns the new cursor x. Default returns 0.</summary>
    public Func<int, int>? CenterCursor { get; set; }

    // ---------------------------------------------------------------- part twins

    /// <summary>
    /// Gametype creation hook: called by whoever creates the part (GameRef.create owner) after allocating the script
    /// object and before running its constructor, with the definition from the type's cfg.
    /// </summary>
    public PartState Attach(VmObject obj, PartDefinition? definition, int typeId = 0)
    {
        _parts.Remove(obj);
        var p = new PartState(obj, definition) { TypeId = typeId != 0 ? typeId : definition?.TypeId ?? 0 };
        p.Wear = p.Tear = Options.DefaultCondition;
        _parts.Add(obj, p);
        Host.IdOf(obj);
        if (p.IsChassis)
        {
            InitChassis(p);
        }

        return p;
    }

    public PartState? Find(VmObject? obj) => obj is not null && _parts.TryGetValue(obj, out var p) ? p : null;

    /// <summary>The twin of a Part script object, created without a definition on first use (guess: the exe always
    /// has one because parts only come from gametypes; scripts constructed directly get an empty slot table).</summary>
    public PartState Get(VmObject obj) => Find(obj) ?? Attach(obj, null);

    public ChassisState ChassisOf(PartState p) => p.Chassis ?? InitChassis(p);

    private ChassisState InitChassis(PartState p)
    {
        var c = new ChassisState(p, ++_nextChassis);
        _chassisBySerial[c.Serial] = c;
        p.Chassis = c;
        // One corner per cfg `wheel` line (measured: 4 on every shipped car); 4 empty corners without a definition (guess).
        var defined = p.Definition?.Wheels ?? [];
        var count = defined.Count > 0 ? defined.Count : 4;
        for (var i = 0; i < count; i++)
        {
            var w = new WheelState(p, i, WheelHandleBase | (++_nextWheel << 12));
            if (i < defined.Count)
            {
                w.Position = defined[i].Position;
                w.Orientation = defined[i].Orientation;
                w.Radius = defined[i].Radius;
            }

            _wheelsByHandle[w.Handle] = w;
            c.Wheels.Add(w);
        }

        return c;
    }

    /// <summary>The WheelRef script object for a corner (created once, its <c>ptr</c> set to the wheel handle).</summary>
    public VmObject? WheelRef(Vm.Vm vm, PartState chassis, int index)
    {
        var c = ChassisOf(chassis);
        if (index < 0 || index >= c.Wheels.Count)
        {
            return null;
        }

        var w = c.Wheels[index];
        if (w.Ref is null)
        {
            w.Ref = vm.NewObject(vm.LoadClass(WheelRefClass));
            ScriptObjects.Set(w.Ref, "ptr", Value.Int(w.Handle));
        }

        return w.Ref;
    }

    /// <summary>The wheel behind a WheelRef script object (by its ptr handle).</summary>
    public WheelState? Wheel(VmObject wheelRef) =>
        _wheelsByHandle.GetValueOrDefault(ScriptObjects.GetInt(wheelRef, "ptr"));

    public VmObject SfxTable(Vm.Vm vm, PartState chassis, int channel)
    {
        var c = ChassisOf(chassis);
        if (!c.SfxTables.TryGetValue(channel, out var t))
        {
            t = new SfxTableState(channel);
            c.SfxTables[channel] = t;
        }

        if (t.Ref is null)
        {
            t.Ref = vm.NewObject(vm.LoadClass(SfxTableClass));
            _sfx.Add(t.Ref, t);
        }

        return t.Ref;
    }

    /// <summary>Sound table behind a SfxTable object (a table created by a script with <c>new SfxTable()</c> gets its own).</summary>
    public SfxTableState Sfx(VmObject table) => _sfx.GetValue(table, _ => new SfxTableState(-1) { Ref = table });

    public DynoState NewDyno(VmObject obj)
    {
        _dynos.Remove(obj);
        var d = new DynoState(obj, DynoHandleBase | ++_nextDyno);
        _dynos.Add(obj, d);
        ScriptObjects.Set(obj, "ptr", Value.Int(d.Handle));
        return d;
    }

    public DynoState? FindDyno(VmObject obj) => _dynos.TryGetValue(obj, out var d) ? d : null;

    // ---------------------------------------------------------------- links

    /// <summary>
    /// Links <paramref name="child"/> into <paramref name="parent"/>. Slot ids of 0 mean "let the engine choose": the
    /// first free, enabled pair whose cfg <c>attach</c>/<c>compatible</c> lines name each other's type and slot (guess,
    /// open question P0 #5). Returns false and changes nothing when no pair fits.
    /// </summary>
    public bool Install(Vm.Vm vm, PartState child, PartState parent, int parentSlotId = 0, int childSlotId = 0, PartState? car = null)
    {
        if (ReferenceEquals(child, parent) || IsAncestor(child, parent))
        {
            Diagnostics.Add($"install refused: {child} would contain itself");
            return false;
        }

        var match = Match(child, parent, parentSlotId, childSlotId);
        if (match is not { } m)
        {
            Diagnostics.Add($"install refused: no free compatible slot for {child} on {parent} (slot {parentSlotId})");
            return false;
        }

        if (child.MountSlot is not null)
        {
            Remove(vm, child);
        }

        Link(child, m.Child, parent, m.Parent);
        SetCar(child, car ?? (parent.IsChassis ? parent : parent.Car ?? parent.Root));
        if (Options.InvokeScriptCallbacks)
        {
            Callback(vm, parent.Object, "addpart", Value.Obj(child.Object));
            Callback(vm, child.Object, "install");
        }

        return true;
    }

    /// <summary>
    /// Links two explicit slots without checking the cfg rules (for hosts that already know the pair, e.g. a save
    /// loader or a garage that resolved the drop itself). Fails only when a slot is occupied or the link would loop.
    /// </summary>
    public bool ForceLink(Vm.Vm vm, PartState child, int childSlotId, PartState parent, int parentSlotId, PartState? car = null)
    {
        var ps = parent.SlotOrTransient(parentSlotId);
        var cs = child.SlotOrTransient(childSlotId);
        if (ReferenceEquals(child, parent) || IsAncestor(child, parent) || ps.Linked is not null || (cs.Linked is not null && !ReferenceEquals(cs, child.MountSlot)))
        {
            return false;
        }

        if (child.MountSlot is not null)
        {
            Remove(vm, child);
        }

        Link(child, cs, parent, ps);
        SetCar(child, car ?? (parent.IsChassis ? parent : parent.Car ?? parent.Root));
        if (Options.InvokeScriptCallbacks)
        {
            Callback(vm, parent.Object, "addpart", Value.Obj(child.Object));
            Callback(vm, child.Object, "install");
        }

        return true;
    }

    /// <summary>Breaks the link to the parent (the <c>remove</c> command). False when the part is loose.</summary>
    public bool Remove(Vm.Vm vm, PartState child)
    {
        if (child.MountSlot is not { Linked: { } parent } mount)
        {
            return false;
        }

        var parentSlot = parent.Slot(mount.LinkedSlotId);
        if (parentSlot is not null)
        {
            parentSlot.Linked = null;
            parentSlot.LinkedSlotId = 0;
        }

        mount.Linked = null;
        mount.LinkedSlotId = 0;
        child.MountSlot = null;
        SetCar(child, null);
        if (Options.InvokeScriptCallbacks)
        {
            Callback(vm, parent.Object, "rempart", Value.Obj(child.Object));
            Callback(vm, child.Object, "remove");
        }

        return true;
    }

    /// <summary>Candidate (parentSlot, childSlot) pair, or null.</summary>
    public (SlotState Parent, SlotState Child)? Match(PartState child, PartState parent, int parentSlotId = 0, int childSlotId = 0)
    {
        IEnumerable<SlotState> parentSlots = parentSlotId != 0 ? [parent.SlotOrTransient(parentSlotId)] : parent.Slots.Where(s => s.Index >= 0).ToList();
        foreach (var ps in parentSlots)
        {
            if (ps.Linked is not null || ps.Disabled)
            {
                continue;
            }

            foreach (var cs in child.Slots)
            {
                if (cs.Linked is null && !cs.Disabled && Compatible(parent, ps, child, cs))
                {
                    return (ps, cs);
                }
            }

            // An explicit parent slot with no cfg evidence either way: accept it with the hinted (or first free)
            // child slot. Guess: the exe's handler trusts explicit slot ids from Part.load.
            if (parentSlotId != 0 && !HasRules(parent, ps, child))
            {
                var cs = childSlotId != 0 ? child.SlotOrTransient(childSlotId) : child.Slots.FirstOrDefault(s => s.Linked is null) ?? child.SlotOrTransient(1);
                if (cs.Linked is null)
                {
                    return (ps, cs);
                }
            }
        }

        return null;
    }

    private static bool HasRules(PartState parent, SlotState ps, PartState child) =>
        (ps.Definition is { } d && (d.Attach.Count > 0 || d.Compatible.Count > 0))
        || ((child.Definition?.Compatible.Count ?? 0) > 0)
        || (parent.TypeId != 0 && child.Slots.Any(s => s.Definition is { } cd && (cd.Attach.Count > 0 || cd.Compatible.Count > 0)));

    /// <summary>
    /// Link rule (guess, open question P0 #5): the parent slot's <c>attach</c> names (childType, childSlot); or the child
    /// slot's <c>attach</c> names (parentType, parentSlot); or the child slot (or the whole child, for part-level lines)
    /// is <c>compatible</c> with a (type, slot) that the parent slot attaches, or whose own definition
    /// (<see cref="DefinitionOf"/>) attaches to this parent slot.
    /// </summary>
    private bool Compatible(PartState parent, SlotState ps, PartState child, SlotState cs)
    {
        var pd = ps.Definition;
        var cd = cs.Definition;
        if (child.TypeId != 0 && pd?.Attach.Contains(new SlotRef(child.TypeId, cs.Id)) == true)
        {
            return true;
        }

        if (parent.TypeId != 0 && cd?.Attach.Contains(new SlotRef(parent.TypeId, ps.Id)) == true)
        {
            return true;
        }

        IEnumerable<SlotRef> subs = cd?.Compatible ?? [];
        if (child.Definition is { Compatible.Count: > 0 } whole && ReferenceEquals(cs, child.MountSlotCandidate()))
        {
            subs = subs.Concat(whole.Compatible);
        }

        foreach (var sub in subs)
        {
            if (pd?.Attach.Contains(sub) == true)
            {
                return true;
            }

            if (parent.TypeId != 0 && DefinitionOf?.Invoke(sub.TypeId)?.Slot(sub.SlotId)?.Attach.Contains(new SlotRef(parent.TypeId, ps.Id)) == true)
            {
                return true;
            }
        }

        return false;
    }

    private static void Link(PartState child, SlotState cs, PartState parent, SlotState ps)
    {
        parent.Adopt(ps);
        child.Adopt(cs);
        ps.Linked = child;
        ps.LinkedSlotId = cs.Id;
        cs.Linked = parent;
        cs.LinkedSlotId = ps.Id;
        child.MountSlot = cs;
    }

    private static bool IsAncestor(PartState candidate, PartState of)
    {
        for (var p = of.Parent; p is not null; p = p.Parent)
        {
            if (ReferenceEquals(p, candidate))
            {
                return true;
            }
        }

        return false;
    }

    private static void SetCar(PartState p, PartState? car)
    {
        p.Car = ReferenceEquals(p, car) ? null : car;
        foreach (var (_, c) in p.Children)
        {
            SetCar(c, car);
        }
    }

    /// <summary>Corner index 0..3 of the chassis slot the part's subtree hangs from, or -1 (rule guessed from the cfg
    /// pattern: corner slots and their <c>slottype</c> end in 1..4, e.g. 101..104 wheels, 201..204 hub groups).</summary>
    public static int WheelIdOf(PartState p)
    {
        for (var s = p; s.Parent is { } up; s = up)
        {
            if (up.IsChassis)
            {
                var slot = up.Slot(s.MountSlot!.LinkedSlotId);
                return slot is null ? -1 : Corner(slot);
            }
        }

        return -1;
    }

    private static int Corner(SlotState slot)
    {
        if (slot.Definition?.SlotType is { } t && t >= 100 && t % 10 is >= 1 and <= 4)
        {
            return (t % 10) - 1;
        }

        return slot.Id >= 100 && slot.Id % 10 is >= 1 and <= 4 ? (slot.Id % 10) - 1 : -1;
    }

    /// <summary>The car's Chassis script object (getCarRef): the car set by install, else the tree root when it is a
    /// chassis; a chassis answers itself (guess).</summary>
    public static PartState? CarOf(PartState p)
    {
        if (p.IsChassis && p.Parent is null)
        {
            return p;
        }

        if (p.Car is not null)
        {
            return p.Car;
        }

        var root = p.Root;
        return root.IsChassis ? root : null;
    }

    /// <summary>
    /// install_OK: search the car's part tree (depth first, chassis first) for a free compatible slot for
    /// <paramref name="part"/>. Returns {parentSlotId, childSlotId} (layout guessed) and the target part.
    /// </summary>
    public (PartState Target, int[] Pair)? FindInstallTarget(PartState part, PartState car, int parentSlotId = 0)
    {
        foreach (var candidate in Tree(car))
        {
            if (ReferenceEquals(candidate, part) || IsAncestor(part, candidate))
            {
                continue;
            }

            if (Match(part, candidate, parentSlotId) is { } m && (m.Parent.Index >= 0 || parentSlotId != 0))
            {
                return (candidate, [m.Parent.Id, m.Child.Id]);
            }
        }

        return null;
    }

    public static IEnumerable<PartState> Tree(PartState root)
    {
        yield return root;
        foreach (var (_, c) in root.Children)
        {
            foreach (var d in Tree(c))
            {
                yield return d;
            }
        }
    }

    // ---------------------------------------------------------------- integration: commands and getInfo

    /// <summary>
    /// The part verbs of <c>GameRef.command</c> (exe parser 0x475f6d): <c>install f car childSlot parentPart parentSlot [x y z]</c>
    /// (format <c>%*s %d %d %d %d %d %f %f %f</c>, measured; f and childSlot parsed but unused by the handler, measured),
    /// <c>remove f mapId</c>, <c>repair</c>, and the chassis data verb <c>add_rollbar w1 w2 force damping</c>. Returns false for any other verb or a non-part target so the
    /// GameRef.command owner can continue with its own table.
    /// </summary>
    public bool ExecuteCommand(Vm.Vm vm, VmObject target, string command)
    {
        var p = Find(target) ?? (target.Class.IsSubclassOf(PartClass) ? Get(target) : null);
        if (p is null)
        {
            return false;
        }

        var tok = command.Split(Separators, StringSplitOptions.RemoveEmptyEntries);
        if (tok.Length == 0)
        {
            return false;
        }

        int I(int i) => i < tok.Length && int.TryParse(tok[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : 0;
        float F(int i) => i < tok.Length && float.TryParse(tok[i], NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0;
        switch (tok[0])
        {
            case "install":
            {
                var car = Find(Host.Resolve(I(2)) is { } co ? Host.PartBehind(co) : null);
                var parentId = I(4);
                var parent = parentId != 0 ? Find(Host.Resolve(parentId) is { } po ? Host.PartBehind(po) : null) : null;
                if (parent is null)
                {
                    if (car is null)
                    {
                        Diagnostics.Add($"install: unknown car/parent in '{command}'");
                        return true;
                    }

                    // parentPart 0: search the car's tree (drop position ignored; guess).
                    if (FindInstallTarget(p, car, I(5)) is { } found)
                    {
                        Install(vm, p, found.Target, found.Pair[0], found.Pair[1], car);
                    }
                    else
                    {
                        Diagnostics.Add($"install: no slot for {p} on {car}");
                    }

                    return true;
                }

                Install(vm, p, parent, I(5), I(3), car);
                return true;
            }

            case "remove":
                Remove(vm, p);
                return true;
            case "add_rollbar" when p.IsChassis && tok.Length >= 5:
                // Swaybar.updatevariables: "add_rollbar 0 1 force damping" (front) / "2 3" (rear): wheel pair + constants.
                // Stored per wheel pair, a repeated pair replaces the earlier one (guess).
                ChassisOf(p).Rollbars[(I(1), I(2))] = (F(3), F(4));
                return true;
            case "repair":
                // Guess: the native repair clears the per-slot damage of the part (scripts restore wear/tear themselves).
                foreach (var s in p.Slots)
                {
                    s.Damage = null;
                }

                if (p.Chassis is { } ch)
                {
                    ch.WheelDamage.Clear();
                }

                return true;
            default:
                return false;
        }
    }

    public const int GiiType = 6;
    public const int GiiCategory = 7;
    public const int GiiRemoveOk = 41;
    public const int GiiPartOwner = 43;
    public const int GiiPartCategory = 55;
    public const int GiiInstallOk = 71;
    public const int GiiCompatible = 72;
    public const int GirCatPart = 9;

    /// <summary>
    /// The <c>GameRef.getInfo(key, arg)</c> keys a part answers natively (key numbers measured from GameType's
    /// constants; answers guessed, open question P0 #6). Returns false for keys the parts layer does not own.
    /// </summary>
    public bool TryGetInfo(VmObject obj, int key, int arg, out int value)
    {
        value = 0;
        var p = Find(obj);
        if (p is null)
        {
            if (!obj.Class.IsSubclassOf(PartClass))
            {
                return false;
            }

            p = Get(obj);
        }

        switch (key)
        {
            case GiiType:
                value = p.TypeId;
                return true;
            case GiiCategory:
                value = GirCatPart;
                return true;
            case GiiPartOwner:
                value = CarOf(p) is { } car && !ReferenceEquals(car, p) ? Host.IdOf(car.Object) : 0;
                return true;
            case GiiPartCategory:
                value = p.Definition?.Category ?? 0;
                return true;
            case GiiRemoveOk:
                value = p.MountSlot is { } m && !m.Disabled && (p.Parent?.Slot(m.LinkedSlotId)?.Disabled != true) ? 1 : 0;
                return true;
            case GiiInstallOk:
            case GiiCompatible:
                var other = Find(Host.Resolve(arg));
                value = other is not null && Match(p, other) is not null ? 1 : 0;
                return true;
            default:
                return false;
        }
    }

    // ---------------------------------------------------------------- RawEdit

    /// <summary>RawEdit.getI/getF: the 32-bit pattern at an address (absolute table, wheel or telemetry handle).</summary>
    public int ReadRaw(int address)
    {
        if (DecodeWheel(address) is { } w)
        {
            return w.Offset == WheelSteerOffset ? BitConverter.SingleToInt32Bits(w.Wheel.Steer) : w.Wheel.RawCells.GetValueOrDefault(w.Offset);
        }

        if (DecodeTelemetry(address) is { } t)
        {
            return t.Chassis.Telemetry.ReadBits(t.Offset);
        }

        if (RawAddressMap.Find(unchecked((uint)address)) is { } e)
        {
            return RawAddressMap.ReadBits(Settings, e);
        }

        Unknown(address, "read");
        return 0;
    }

    /// <summary>RawEdit.setI/setF: store a 32-bit pattern; returns the address like the exe (measured).</summary>
    public int WriteRaw(int address, int bits)
    {
        if (DecodeWheel(address) is { } w)
        {
            if (w.Offset == WheelSteerOffset)
            {
                w.Wheel.Steer = BitConverter.Int32BitsToSingle(bits);
            }
            else
            {
                w.Wheel.RawCells[w.Offset] = bits;
            }
        }
        else if (DecodeTelemetry(address) is { } t)
        {
            if (ChassisTelemetry.IsScriptWritable(t.Offset))
            {
                t.Chassis.Telemetry.WriteBits(t.Offset, bits);
            }
        }
        else if (RawAddressMap.Find(unchecked((uint)address)) is { } e)
        {
            RawAddressMap.WriteBits(Settings, e, bits);
        }
        else
        {
            Unknown(address, "write");
        }

        return address;
    }

    /// <summary>getIndexedAddr: an opaque handle <c>base | serial &lt;&lt; 16 | offset</c> (rawedit.md rule 4).</summary>
    public int TelemetryAddress(PartState chassis, int offset) =>
        TelemetryHandleBase | (ChassisOf(chassis).Serial << 16) | (offset & 0xFFFF);

    private (WheelState Wheel, int Offset)? DecodeWheel(int address)
    {
        if ((address & 0x70000000) != WheelHandleBase)
        {
            return null;
        }

        var w = _wheelsByHandle.GetValueOrDefault(address & ~0xFFF);
        return w is null ? null : (w, address & 0xFFF);
    }

    private (ChassisState Chassis, int Offset)? DecodeTelemetry(int address)
    {
        if ((address & 0x70000000) != TelemetryHandleBase)
        {
            return null;
        }

        var c = _chassisBySerial.GetValueOrDefault((address >> 16) & 0xFFF);
        return c is null ? null : (c, address & 0xFFFF);
    }

    private void Unknown(int address, string what)
    {
        if (_unknownAddresses.Add(unchecked((uint)address)))
        {
            Diagnostics.Add($"RawEdit {what} of unknown address 0x{address:x8} ignored");
        }
    }

    // ---------------------------------------------------------------- chassis update

    /// <summary>forceUpdate(): snapshot the field contract unless suspend_update is set.</summary>
    public ChassisParams? ForceUpdate(PartState chassis)
    {
        var c = ChassisOf(chassis);
        if (Options.HonourSuspendUpdate && ScriptObjects.GetInt(chassis.Object, "suspend_update") != 0)
        {
            c.SuspendedUpdates++;
            return null;
        }

        var snapshot = ChassisParams.Read(chassis.Object);
        c.Params = snapshot;
        c.Updates++;
        ChassisUpdated?.Invoke(chassis, snapshot);
        return snapshot;
    }

    private static void Callback(Vm.Vm vm, VmObject target, string name, params Value[] args)
    {
        if (target.Class.FindMethods(name, args.Length).Any())
        {
            vm.InvokeVirtual(target, name, args);
        }
    }
}

internal static class PartStateExtensions
{
    /// <summary>For part-level <c>compatible</c> lines (no preceding slot) the child mounts with its first slot (guess).</summary>
    public static SlotState? MountSlotCandidate(this PartState p) => p.Slots.FirstOrDefault(s => s.Index == 0);
}
