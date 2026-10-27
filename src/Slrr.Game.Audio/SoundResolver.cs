using Slrr.Audio;
using Slrr.Engine.Parts;
using Slrr.Engine.Resources;
using Slrr.Vm;

namespace Slrr.GameAudio;

/// <summary>
/// Maps runtime resource ids and <c>ResourceRef</c> script objects to <see cref="SoundKey"/>s (rpk file name, entry id) through the
/// Resources layer's archive table, and Parts' sound tables to <see cref="EngineSfxSetup"/>.
/// A runtime id is <c>archiveSlot &lt;&lt; 16 | entryId</c> (<see cref="ResourceIds"/>); the slot's name is the archive file name used by <see cref="SoundCatalog"/>.
/// </summary>
public sealed class SoundResolver(ArchiveTable archives)
{
    /// <summary>The key of a sound resource id; null for 0, runtime instances and unknown archive slots.</summary>
    public SoundKey? Resolve(int runtimeId)
    {
        if (runtimeId == 0 || ResourceIds.Archive(runtimeId) == ArchiveTable.RuntimeArchive)
        {
            return null;
        }

        var slot = archives[ResourceIds.Archive(runtimeId)];
        if (slot is null)
        {
            return null;
        }

        var name = (slot.File ?? slot.Name).Replace('\\', '/');
        return new SoundKey(Path.GetFileName(name).ToLowerInvariant(), (uint)ResourceIds.Local(runtimeId));
    }

    public SoundKey? Resolve(VmObject? resourceRef) => Resolve(ResourceEngine.IdOf(resourceRef));

    /// <summary>
    /// Builds the engine sound setup of a chassis: <c>getSfxTable(n).addItem(sound, rpm, rpmMin, rpmMax, volMin, volMax)</c> rows
    /// become <see cref="EngineSfxItem"/>s in insertion order (the exe's fades depend on the order); rows whose sound does not
    /// resolve are kept as unresolvable keys so the neighbour fades stay aligned (their voice simply does not exist).
    /// </summary>
    public EngineSfxSetup SetupOf(ChassisState chassis)
    {
        var setup = new EngineSfxSetup { ExhaustMinVolume = chassis.SfxExhaustMinVolume };
        foreach (var (channel, table) in chassis.SfxTables)
        {
            if (channel is < 0 or > 2)
            {
                continue;
            }

            foreach (var row in table.Items)
            {
                var key = Resolve(row.Sound) ?? new SoundKey("<unresolved>", 0);
                setup.Tables[channel].Add(new EngineSfxItem(key, row.A, row.B, row.C, row.D, row.E));
            }
        }

        return setup;
    }

    /// <summary>Cheap change detector for a chassis' tables (counts and the float sums), so the sink rebuilds voices after <c>updatevariables()</c> re-fills them.</summary>
    public static int Signature(ChassisState chassis)
    {
        var h = new HashCode();
        h.Add(chassis.SfxExhaustMinVolume);
        foreach (var (channel, table) in chassis.SfxTables.OrderBy(k => k.Key))
        {
            h.Add(channel);
            foreach (var r in table.Items)
            {
                h.Add(ResourceEngine.IdOf(r.Sound));
                h.Add(r.A);
                h.Add(r.B);
                h.Add(r.C);
                h.Add(r.D);
                h.Add(r.E);
            }
        }

        return h.ToHashCode();
    }
}
