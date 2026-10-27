using System.Text;
using Slrr.Formats.Rpak;
using Slrr.Vm;

namespace Slrr.Engine.Resources;

/// <summary>
/// <c>key value</c> lines of a text rpk entry (CRLF text, docs/formats/rpak.md): <c>script java.game.parts.Part</c>,
/// <c>native part parts\scripts\X.cfg</c>, <c>mesh 0x00000030</c>, ...
/// </summary>
public sealed class EntryText
{
    private EntryText(IReadOnlyList<(string Key, string Value)> lines)
    {
        Lines = lines;
    }

    public IReadOnlyList<(string Key, string Value)> Lines { get; }

    /// <summary>First value of a key (the first line wins, as in tools/analysis/rpk_gametype_resolve.py), or null.</summary>
    public string? Get(string key)
    {
        foreach (var (k, v) in Lines)
        {
            if (k == key)
            {
                return v;
            }
        }

        return null;
    }

    /// <summary>
    /// Parses entry data as text when every byte is printable ASCII, TAB or CR/LF (binary entries such as <c>ICFG</c>
    /// or instance trees return null). Same rule as the static analysis scripts: data of at most 4 KiB, not TypeOfEntry 1.
    /// </summary>
    public static EntryText? TryParse(RpakEntry entry, ReadOnlySpan<byte> data)
    {
        if (data.Length == 0 || data.Length > 4096 || entry.TypeOfEntry == RpakArchive.ExtraEntryType)
        {
            return null;
        }

        foreach (var b in data)
        {
            if (b < 9 || (b > 13 && b < 32) || b > 126)
            {
                return null;
            }
        }

        var lines = new List<(string, string)>();
        foreach (var raw in Encoding.Latin1.GetString(data).Replace("\r", string.Empty, StringComparison.Ordinal).Split('\n'))
        {
            var t = raw.Trim();
            if (t.Length == 0)
            {
                continue;
            }

            var sp = t.IndexOfAny([' ', '\t']);
            lines.Add(sp < 0 ? (t, string.Empty) : (t[..sp], t[(sp + 1)..].Trim()));
        }

        return new EntryText(lines);
    }
}

/// <summary>
/// A node of the merged resource tree: an rpk entry (static resource or gametype) or a run-time instance created by
/// <c>GameRef.create</c>, <c>createNativeInstance</c>, <c>RenderRef.create</c>, <c>PhysicsRef.create</c>.
/// Instance state that a headless engine only stores (transform, flags, colours, ...) lives here too.
/// </summary>
public sealed class ResourceNode
{
    private EntryText? _text;
    private bool _textParsed;

    internal ResourceNode(int id, int type, string name)
    {
        Id = id;
        Type = type;
        Name = name;
    }

    public int Id { get; }

    /// <summary>TypeOfEntry for rpk entries; INSTANCE_GAME/PHYSICS/RENDER (1/2/3) for instances.</summary>
    public int Type { get; internal set; }

    /// <summary>rpk alias, or the instance name passed to <c>create</c> (exe default <c>_gameinst</c>).</summary>
    public string Name { get; internal set; }

    public int ParentId { get; internal set; }

    public ArchiveSlot? Archive { get; internal init; }

    public RpakEntry? Entry { get; internal init; }

    public bool IsInstance => ResourceIds.IsInstance(Id);

    /// <summary><c>GameRef.get/set/clearFlags</c> (WORLDTREEROOT 16, WORLDTREELEAF 64).</summary>
    public int Flags { get; set; }

    /// <summary>Text of an rpk entry, parsed on first use (null for binary entries and instances).</summary>
    public EntryText? Text
    {
        get
        {
            if (!_textParsed)
            {
                _textParsed = true;
                if (Archive?.Archive is { } rpk && Entry is { } e)
                {
                    try
                    {
                        _text = EntryText.TryParse(e, rpk.GetData(e).Span);
                    }
                    catch (Slrr.Core.SlrrFormatException)
                    {
                        _text = null;
                    }
                }
            }

            return _text;
        }
    }

    // ------------------------------------------------------------ instance state

    /// <summary>For instances: the gametype / resource the instance was created from (<c>getInfo(GII_TYPE)</c>, <c>getTypeID</c>).</summary>
    public int TypeId { get; set; }

    /// <summary>The script object behind a game instance (<c>getScriptInstance</c>), null for pure native objects.</summary>
    public VmObject? Script { get; set; }

    /// <summary>The <c>params</c> text given at creation (<c>"x,y,z,yaw,pitch,roll"</c> for placed objects).</summary>
    public string? Params { get; set; }

    /// <summary>Native object kind from the gametype's <c>native</c> line (<c>part</c>, <c>car</c>, <c>camera</c>, ...).</summary>
    public string? NativeKind { get; set; }

    public float[] Pos { get; } = new float[3];

    /// <summary>Yaw, pitch, roll (the <c>Ypr</c> fields y, p, r).</summary>
    public float[] Ori { get; } = new float[3];

    public float[] Vel { get; } = new float[3];

    public float[] AngVel { get; } = new float[3];

    /// <summary>Accepted events (<c>setEventMask</c>/<c>clearEventMask</c>).</summary>
    public int EventMask { get; set; }

    /// <summary><c>registerCallback(GII_*)</c> keys.</summary>
    public HashSet<int> Callbacks { get; } = [];

    /// <summary>Engine-side values stored by setter natives with no headless effect (colour, light, scale, ...).</summary>
    public Dictionary<string, object?> Properties { get; } = new(StringComparer.Ordinal);

    /// <summary><c>load</c>/<c>cache</c>/<c>precache</c> counters (headless: bookkeeping only).</summary>
    public int LoadCount { get; set; }

    public int CacheCount { get; set; }

    public bool Destroyed { get; internal set; }

    public override string ToString() => $"{ResourceIds.Format(Id)} {Name} (type {Type})";
}

/// <summary>
/// The resource tree: every entry of every loaded archive (parent = the entry's <c>superId</c> mapped like an id, so
/// parents may live in other archives) plus run-time instances. Archives load lazily, exactly when the
/// <see cref="ArchiveTable"/> loads them, and looking a node up loads its archive (as 0x538440 does).
/// </summary>
public sealed class ResourceTree
{
    private readonly Dictionary<int, ResourceNode> _nodes = [];
    private readonly Dictionary<int, List<int>> _children = [];
    private readonly List<ushort> _freeInstanceNumbers = [];
    private int _instanceCounter;

    public ResourceTree(ArchiveTable archives)
    {
        Archives = archives;
        archives.Loaded += AddArchive;
        foreach (var s in archives.Slots.Where(s => s.State == ArchiveState.Loaded))
        {
            AddArchive(s);
        }
    }

    public ArchiveTable Archives { get; }

    /// <summary>Live instances (not yet destroyed).</summary>
    public IEnumerable<ResourceNode> Instances => _nodes.Values.Where(n => n.IsInstance && !n.Destroyed);

    /// <summary>Node by runtime id; loads the archive of an entry id if needed. Null for 0 / unknown ids.</summary>
    public ResourceNode? Get(int id)
    {
        if (id == 0)
        {
            return null;
        }

        if (_nodes.TryGetValue(id, out var n))
        {
            return n;
        }

        var a = ResourceIds.Archive(id);
        if (a != ArchiveTable.RuntimeArchive && Archives[a] is { State: not ArchiveState.Loaded })
        {
            Archives.EnsureLoaded(a);
            return _nodes.GetValueOrDefault(id);
        }

        return null;
    }

    public IReadOnlyList<int> Children(int id) => _children.TryGetValue(id, out var c) ? c : [];

    /// <summary>The sibling after <paramref name="id"/> in its parent's child list, or 0.</summary>
    public int NextSibling(int id)
    {
        var n = Get(id);
        if (n is null || !_children.TryGetValue(n.ParentId, out var sibs))
        {
            return 0;
        }

        var i = sibs.IndexOf(id);
        return i >= 0 && i + 1 < sibs.Count ? sibs[i + 1] : 0;
    }

    /// <summary>
    /// Allocates an instance id as 0x536ed0 does (measured): numbers come from a LIFO free list refilled with 128
    /// ascending numbers from a counter, the result is <c>0xFFFF0000 | number</c>. The counter's start value and the
    /// recycling of destroyed ids are guesses (0, and push-back on destroy).
    /// </summary>
    public int AllocateInstanceId()
    {
        if (_freeInstanceNumbers.Count == 0)
        {
            for (var i = 0; i < 128; i++)
            {
                _freeInstanceNumbers.Add((ushort)_instanceCounter++);
            }
        }

        var n = _freeInstanceNumbers[^1];
        _freeInstanceNumbers.RemoveAt(_freeInstanceNumbers.Count - 1);
        return ResourceIds.Make(ArchiveTable.RuntimeArchive, n);
    }

    /// <summary>Creates an instance node below <paramref name="parentId"/>.</summary>
    public ResourceNode CreateInstance(int type, int parentId, string name, int typeId)
    {
        var id = AllocateInstanceId();
        var n = new ResourceNode(id, type, name) { TypeId = typeId };
        _nodes[id] = n;
        Link(n, parentId);
        return n;
    }

    /// <summary>Re-parents a node (<c>GameRef.setParent</c>).</summary>
    public void SetParent(ResourceNode n, int parentId)
    {
        Unlink(n);
        Link(n, parentId);
    }

    /// <summary>Destroys an instance and its instance children (resource entries are never destroyed). Returns the ids destroyed.</summary>
    public List<ResourceNode> Destroy(ResourceNode n)
    {
        var gone = new List<ResourceNode>();
        if (!n.IsInstance || n.Destroyed)
        {
            return gone;
        }

        foreach (var c in Children(n.Id).ToList())
        {
            if (_nodes.TryGetValue(c, out var child))
            {
                gone.AddRange(Destroy(child));
            }
        }

        Unlink(n);
        n.Destroyed = true;
        _nodes.Remove(n.Id);
        _children.Remove(n.Id);
        _freeInstanceNumbers.Add((ushort)ResourceIds.Local(n.Id));
        gone.Add(n);
        return gone;
    }

    private void AddArchive(ArchiveSlot slot)
    {
        foreach (var e in slot.Archive!.Entries)
        {
            var id = Archives.MapId(slot, e.Id);
            if (id == 0 || _nodes.ContainsKey(id))
            {
                continue; // duplicate ids inside one archive: first entry wins (guess)
            }

            var n = new ResourceNode(id, e.TypeOfEntry, e.Alias) { Archive = slot, Entry = e };
            _nodes[id] = n;
            Link(n, Archives.MapId(slot, e.SuperId));
        }
    }

    private void Link(ResourceNode n, int parentId)
    {
        n.ParentId = parentId;
        if (parentId == 0)
        {
            return;
        }

        if (!_children.TryGetValue(parentId, out var list))
        {
            list = [];
            _children[parentId] = list;
        }

        list.Add(n.Id);
    }

    private void Unlink(ResourceNode n)
    {
        if (_children.TryGetValue(n.ParentId, out var list))
        {
            list.Remove(n.Id);
        }

        n.ParentId = 0;
    }
}
