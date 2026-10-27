using Slrr.Core;
using Slrr.Formats.Rpak;

namespace Slrr.Engine.Resources;

/// <summary>Load state of one archive slot.</summary>
public enum ArchiveState
{
    /// <summary>Name known (mentioned by a literal, a reference table or <c>openLib</c>), file not read yet.</summary>
    Registered,

    /// <summary>Header, reference table and entry table read.</summary>
    Loaded,

    /// <summary>The file does not exist or could not be parsed; every id in it resolves to 0.</summary>
    Missing,
}

/// <summary>One row of the engine's archive table (0x70-byte records at resource manager +0xffe94 in the exe).</summary>
public sealed class ArchiveSlot
{
    internal ArchiveSlot(int index, string name)
    {
        Index = index;
        Name = name;
    }

    /// <summary>Global archive index: the high 16 bits of every runtime resource id that lives in this archive.</summary>
    public int Index { get; }

    /// <summary>Name as first registered (a game-relative path such as <c>parts\engines\Baiern_Emer.rpk</c>).</summary>
    public string Name { get; }

    public ArchiveState State { get; internal set; }

    /// <summary>Resolved file on disk (null until loaded or when missing).</summary>
    public string? File { get; internal set; }

    public RpakArchive? Archive { get; internal set; }

    /// <summary>
    /// Reference slot → global archive index. Element 0 is this archive's own index; element <c>i</c> is the archive
    /// named by reference <c>i</c> of the file (measured: 0x544170 builds a table of refCount+1 records of 66 bytes,
    /// record 0 = own index, record i = 0x538380(name of reference i)).
    /// </summary>
    public IReadOnlyList<int> References { get; internal set; } = [];

    public override string ToString() => $"#{Index} {Name} ({State})";
}

/// <summary>
/// The engine's archive table and the resource-literal id mapping (P0 open question 1). Reconstructed from
/// <c>StreetLegal_Redline.exe</c> (all measured from the disassembly, never executed):
/// <list type="bullet">
/// <item><c>0x406660</c> (opcode 0E, constant type 3) calls <c>0x4122b0(archiveName, id)</c> → resource manager
/// <c>0x538440(name, id)</c>.</item>
/// <item><c>0x538440</c> finds the archive by name with <c>_stricmp</c> (0x5d7190; case-insensitive, no path
/// normalisation) or appends a new slot (0x543eb0); then loads the archive header (0x544170, lazily, once) and maps the
/// id with 0x544590.</item>
/// <item><c>0x544590(id)</c>: <c>local = id &amp; 0xFFFF</c>, <c>slot = id &gt;&gt; 16</c>; local 0 → 0;
/// <c>slot &gt;= archiveCount</c> → 0; slot 0 → <c>(ownIndex &lt;&lt; 16) | local</c>; <c>slot &lt;= refCount</c> →
/// <c>(globalIndexOfReference[slot] &lt;&lt; 16) | local</c>; else 0.</item>
/// <item>A missing archive file leaves the slot registered but unloaded and the literal resolves to 0.</item>
/// <item><c>0x538380(name)</c> (find-or-register, no load) is what the reference-table loader and
/// <c>System.openLib</c> (0x487be0, returns the index) use.</item>
/// <item>The archive table starts empty (0x535e70 sets the count to 0 and keeps a separate runtime archive with index
/// 0xFFFF for instances); the start-up function registers <c>system.rpk</c> first (0x58c76a), so system.rpk is
/// archive 0.</item>
/// </list>
/// The numeric archive index therefore depends on the order in which archives are first mentioned at run time; this
/// class reproduces the mechanism, so the same order of mentions gives the same ids. Scripts rely on it:
/// <c>Catalog</c> compares <c>parts.id() &gt;&gt; 16</c> with <c>System.openLib("parts.rpk")</c> and with
/// <c>car.getInfo(GII_TYPE) &gt;&gt; 16</c> (sl/Scripts/game/src/Catalog.java).
/// </summary>
public sealed class ArchiveTable
{
    /// <summary>Archive index of the run-time instance archive (0x535e70 registers it with index 0xFFFF).</summary>
    public const int RuntimeArchive = 0xFFFF;

    private readonly List<ArchiveSlot> _slots = [];
    private readonly Func<string, string?> _resolvePath;

    /// <param name="resolvePath">Maps an archive name (game-relative path) to a file, or null when it does not exist.</param>
    public ArchiveTable(Func<string, string?> resolvePath)
    {
        _resolvePath = resolvePath;
    }

    public ArchiveTable(GameFileSystem game)
        : this(game.Resolve)
    {
    }

    /// <summary>Raised once per archive when its header and entry table have been read.</summary>
    public event Action<ArchiveSlot>? Loaded;

    public IReadOnlyList<ArchiveSlot> Slots => _slots;

    public ArchiveSlot? this[int index] => index >= 0 && index < _slots.Count ? _slots[index] : null;

    /// <summary>Index of an already registered archive (case-insensitive, like <c>_stricmp</c>), or -1.</summary>
    public int Find(string name)
    {
        for (var i = 0; i < _slots.Count; i++)
        {
            if (string.Equals(_slots[i].Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary><c>0x538380</c>: find the archive by name or append a new slot; returns its global index.</summary>
    public int Register(string name)
    {
        var i = Find(name);
        if (i >= 0)
        {
            return i;
        }

        _slots.Add(new ArchiveSlot(_slots.Count, name));
        return _slots.Count - 1;
    }

    /// <summary>
    /// <c>0x544170</c>: read the archive once; registers every reference name in slot order. Returns the slot when the
    /// archive is loaded, null when the file is missing or unreadable (retried on the next call, as in the exe).
    /// </summary>
    public ArchiveSlot? EnsureLoaded(int index)
    {
        var slot = this[index];
        if (slot is null)
        {
            return null;
        }

        if (slot.State == ArchiveState.Loaded)
        {
            return slot;
        }

        var file = _resolvePath(slot.Name);
        if (file is null)
        {
            slot.State = ArchiveState.Missing;
            return null;
        }

        RpakArchive rpk;
        try
        {
            rpk = RpakArchive.Read(file);
        }
        catch (Exception ex) when (ex is SlrrFormatException or IOException)
        {
            slot.State = ArchiveState.Missing;
            return null;
        }

        var refs = new int[rpk.References.Count + 1];
        refs[0] = slot.Index;
        slot.File = file;
        slot.Archive = rpk;
        slot.State = ArchiveState.Loaded;
        for (var r = 0; r < rpk.References.Count; r++)
        {
            refs[r + 1] = Register(rpk.References[r].Name);
        }

        slot.References = refs;
        Loaded?.Invoke(slot);
        return slot;
    }

    /// <summary><c>0x544590</c>: map an archive-relative id (<c>0xSSSSIIII</c>, slot 0 = this archive) to a runtime id.</summary>
    public int MapId(ArchiveSlot archive, uint id)
    {
        var local = id & 0xFFFF;
        var slot = (int)(id >> 16);
        if (local == 0 || slot >= _slots.Count)
        {
            return 0;
        }

        if (slot == 0)
        {
            return ResourceIds.Make(archive.Index, (int)local);
        }

        return slot < archive.References.Count ? ResourceIds.Make(archive.References[slot], (int)local) : 0;
    }

    /// <summary>
    /// <c>0x538440</c>: the value of a resource literal <c>archive:0xID</c> (opcode 0E). Registers the archive if new,
    /// loads it, maps the id. 0 when the archive file does not exist.
    /// </summary>
    public int ResolveLiteral(string archiveName, uint id)
    {
        var index = Register(archiveName);
        var slot = EnsureLoaded(index);
        return slot is null ? 0 : MapId(slot, id);
    }

    /// <summary>
    /// <c>System.openLib(name)</c> (0x487be0): register the archive, make sure it is loaded, return its index (measured;
    /// <c>Catalog.partsPackId = System.openLib("parts.rpk")</c> is compared with <c>id() &gt;&gt; 16</c>).
    /// </summary>
    public int Mount(string name)
    {
        var index = Register(name);
        EnsureLoaded(index);
        return index;
    }

    /// <summary>Name of the archive that holds a runtime id (for messages such as <c>create: Wrong GameType! %s:0x%08X</c>).</summary>
    public string ArchiveName(int id)
    {
        var a = ResourceIds.Archive(id);
        return a == RuntimeArchive ? "<runtime>" : this[a]?.Name ?? $"<archive {a}>";
    }
}
