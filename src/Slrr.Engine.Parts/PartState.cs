using System.Numerics;
using Slrr.Vm;

namespace Slrr.Engine.Parts;

/// <summary>Runtime state of one slot of a part (the native twin's slot table).</summary>
public sealed class SlotState
{
    internal SlotState(int index, int id, Vector3 position, Ypr orientation, SlotDefinition? definition)
    {
        Index = index;
        Id = id;
        Position = position;
        Orientation = orientation;
        Definition = definition;
    }

    /// <summary>Index in the cfg order (<c>getSlotID(index)</c>); -1 for slots created at run time.</summary>
    public int Index { get; }

    public int Id { get; }

    public SlotDefinition? Definition { get; }

    /// <summary>Current mount point (cfg value until <c>setSlotPos</c>).</summary>
    public Vector3 Position { get; set; }

    public Ypr Orientation { get; set; }

    /// <summary><c>disableSlot(id, 1)</c>.</summary>
    public bool Disabled { get; set; }

    /// <summary>Opaque damage text (<c>getSlotDamage</c>/<c>setSlotDamage</c>, saved verbatim by Part.save); null = undamaged.</summary>
    public string? Damage { get; set; }

    /// <summary>The part on the other side of the link, or null.</summary>
    public PartState? Linked { get; internal set; }

    /// <summary>Slot id on the other side (<c>slotIDOnSlot</c>); 0 when empty.</summary>
    public int LinkedSlotId { get; internal set; }

    public override string ToString() => $"slot {Id}" + (Linked is null ? string.Empty : $" -> {Linked.Object.Class.Name}:{LinkedSlotId}");
}

/// <summary>
/// The native twin of a <c>java.game.parts.Part</c> script object: what the engine keeps outside the script fields
/// (wear, tear, paint, mesh, render type, slot table, links, car). Created by <see cref="PartsWorld.Attach"/> or lazily
/// on the first Part native call.
/// </summary>
public sealed class PartState
{
    private readonly List<SlotState> _slots = [];

    internal PartState(VmObject obj, PartDefinition? definition)
    {
        Object = obj;
        Definition = definition;
        if (definition is not null)
        {
            for (var i = 0; i < definition.Slots.Count; i++)
            {
                var s = definition.Slots[i];
                _slots.Add(new SlotState(i, s.Id, s.Position, s.Orientation, s));
            }

            Texture = definition.Texture;
            RenderType = 0;
        }
    }

    /// <summary>The script object (Part subclass instance).</summary>
    public VmObject Object { get; }

    public PartDefinition? Definition { get; }

    /// <summary>Gametype id (<c>getInfo(GII_TYPE)</c>); 0 when the creator did not say.</summary>
    public int TypeId { get; internal set; }

    /// <summary>Condition factor in [0,1], 1 = new (guess: default 1.0 for a fresh part).</summary>
    public float Wear { get; set; } = 1f;

    public float Tear { get; set; } = 1f;

    /// <summary>Lifespan in metres (<c>setMaxWear(kmToMaxWear(km))</c>); wear accumulation is physics, not modelled here.</summary>
    public float MaxWear { get; set; }

    public int Texture { get; set; }

    public int Mesh { get; set; }

    public int RenderType { get; set; }

    public int Flap { get; set; }

    /// <summary><c>setSfxLoopParams(F,F)</c> (registered, never declared/called): stored only.</summary>
    public (float A, float B) SfxLoopParams { get; set; }

    public IReadOnlyList<SlotState> Slots => _slots;

    /// <summary>Slots from the cfg (the ones <c>getSlots</c>/<c>getSlotID(index)</c> count).</summary>
    public int DefinedSlotCount => Definition?.Slots.Count ?? _slots.Count(s => s.Index >= 0);

    /// <summary>The slot that links this part to its parent, or null when loose.</summary>
    public SlotState? MountSlot { get; internal set; }

    public PartState? Parent => MountSlot?.Linked;

    /// <summary>Car (root chassis) the part belongs to, set by the <c>install</c> command; null when loose.</summary>
    public PartState? Car { get; internal set; }

    public bool IsChassis => Object.Class.IsSubclassOf(PartsWorld.ChassisClass);

    /// <summary>Chassis-only state (wheels, sound tables, telemetry, field snapshots).</summary>
    public ChassisState? Chassis { get; internal set; }

    public SlotState? Slot(int id) => _slots.FirstOrDefault(s => s.Id == id);

    /// <summary>Finds a slot by id, creating a run-time slot when the cfg did not declare it (guess, see docs).</summary>
    public SlotState SlotOrCreate(int id)
    {
        var s = Slot(id);
        if (s is null)
        {
            s = new SlotState(-1, id, Vector3.Zero, default, null);
            _slots.Add(s);
        }

        return s;
    }

    /// <summary>The slot with this id, or a new run-time slot that is not yet part of the table (see <see cref="Adopt"/>).</summary>
    internal SlotState SlotOrTransient(int id) => Slot(id) ?? new SlotState(-1, id, Vector3.Zero, default, null);

    internal void Adopt(SlotState s)
    {
        if (!_slots.Contains(s))
        {
            _slots.Add(s);
        }
    }

    public SlotState? SlotAt(int index) => index >= 0 && index < _slots.Count ? _slots[index] : null;

    /// <summary>Children linked below this part (every linked slot except the mount slot).</summary>
    public IEnumerable<(SlotState Slot, PartState Child)> Children =>
        _slots.Where(s => s.Linked is not null && !ReferenceEquals(s, MountSlot)).Select(s => (s, s.Linked!));

    /// <summary>Topmost part of the link tree (the chassis for an installed part).</summary>
    public PartState Root
    {
        get
        {
            var p = this;
            var guard = 0;
            while (p.Parent is { } up && guard++ < 10_000)
            {
                p = up;
            }

            return p;
        }
    }

    public override string ToString() => $"{Object.Class.Name}#{Object.Id}";
}
