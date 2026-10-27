using Slrr.Core;

namespace Slrr.Engine.Resources;

/// <summary>Where a gametype's script class comes from.</summary>
public enum ScriptBinding
{
    /// <summary>The entry has its own <c>script</c> line.</summary>
    Own,

    /// <summary>The nearest ancestor (superId chain, across archives) with a <c>script</c> line supplies it.</summary>
    Inherited,

    /// <summary>No entry up the chain has a <c>script</c> line (pure native objects: camera, map, trigger, ped, ...).</summary>
    None,
}

/// <summary>A resolved gametype (rpk entry with script and/or native lines).</summary>
/// <param name="Id">Runtime id of the entry.</param>
/// <param name="Name">Entry alias.</param>
/// <param name="Script">Target of the effective <c>script</c> line: a dotted class name or a game path to a .class file.</param>
/// <param name="ScriptOwnerId">Runtime id of the entry that carries that <c>script</c> line (0 if none).</param>
/// <param name="NativeKind">First token of the entry's own <c>native</c> line (<c>part</c>, <c>car</c>, <c>sun</c>, ...).</param>
/// <param name="NativeArg">Rest of the <c>native</c> line (a cfg path or an id).</param>
public sealed record GametypeInfo(int Id, string Name, int Type, string? Script, int ScriptOwnerId, string? NativeKind, string? NativeArg, ScriptBinding Binding);

/// <summary>Counts of a whole-corpus gametype scan (<see cref="GametypeRegistry.Survey"/>).</summary>
public sealed class GametypeSurvey
{
    public int Archives { get; set; }

    public int MissingArchives { get; set; }

    public int TextEntriesWithScriptOrNative { get; set; }

    public int NativeEntries { get; set; }

    public Dictionary<string, int> NativeKinds { get; } = new(StringComparer.Ordinal);

    public Dictionary<ScriptBinding, int> NativeBindings { get; } = [];

    public Dictionary<string, int> Scripts { get; } = new(StringComparer.Ordinal);

    public List<GametypeInfo> Unbound { get; } = [];
}

/// <summary>
/// Gametype registry built from the rpk text entries. Measured facts (docs/engine-api/entry-points.md section 2,
/// reproduced by <see cref="Survey"/>): 1,616 text entries carry <c>script</c> and/or <c>native</c>; 1,574 <c>native</c>
/// entries (part 1,502, car 45, ...); 1,504 have their own <c>script</c> line, 13 inherit <c>java.game.parts.Part</c>
/// from parts.rpk:0x3, 57 have none. The exe's create routine (0x53a5e0) only accepts entries of TypeOfEntry 8
/// (<c>create: Wrong GameType! %s:0x%08X</c>).
/// Inheritance through the superId chain is (M): it reproduces the scripted part classes, the exe walk itself was not read.
/// </summary>
public sealed class GametypeRegistry
{
    private const int MaxHops = 30;
    private readonly Dictionary<int, GametypeInfo?> _cache = [];

    public GametypeRegistry(ResourceTree tree)
    {
        Tree = tree;
    }

    public ResourceTree Tree { get; }

    /// <summary>Gametype information for an entry id; null when the id is not an rpk entry.</summary>
    public GametypeInfo? Get(int id)
    {
        if (_cache.TryGetValue(id, out var cached))
        {
            return cached;
        }

        var node = Tree.Get(id);
        GametypeInfo? info = null;
        if (node is { IsInstance: false })
        {
            var native = node.Text?.Get("native");
            string? kind = null, arg = null;
            if (native is not null)
            {
                var sp = native.IndexOfAny([' ', '\t']);
                kind = sp < 0 ? native : native[..sp];
                arg = sp < 0 ? null : native[(sp + 1)..].Trim();
            }

            var (script, owner) = FindScript(node);
            var binding = script is null ? ScriptBinding.None : owner == id ? ScriptBinding.Own : ScriptBinding.Inherited;
            info = new GametypeInfo(id, node.Name, node.Type, script, owner, kind, arg, binding);
        }

        _cache[id] = info;
        return info;
    }

    /// <summary>
    /// Maps the <c>gametype 0xSSSSIIII</c> field of a placed-object record (RSD chunk of a map archive) to a runtime id:
    /// the high 16 bits select a slot of that archive's reference table (docs/formats/instances.md), i.e. the same
    /// mapping as 0x544590.
    /// </summary>
    public int ResolveInstanceGametype(ArchiveSlot mapArchive, uint gametype) => Tree.Archives.MapId(mapArchive, gametype);

    /// <summary>
    /// Mounts every .rpk of the game tree (ordinal path order), and classifies every entry that has a <c>native</c>
    /// line. Read-only; used by tests and the <c>boot --gametypes</c> report.
    /// </summary>
    public static GametypeSurvey Survey(GameFileSystem game, out GametypeRegistry registry)
    {
        var archives = new ArchiveTable(game);
        var tree = new ResourceTree(archives);
        registry = new GametypeRegistry(tree);
        var s = new GametypeSurvey();
        foreach (var file in game.Enumerate(".rpk"))
        {
            archives.Mount(Path.GetRelativePath(game.Root, file).Replace('/', '\\'));
        }

        foreach (var slot in archives.Slots.ToList())
        {
            if (slot.State != ArchiveState.Loaded)
            {
                s.MissingArchives++;
                continue;
            }

            s.Archives++;
            foreach (var e in slot.Archive!.Entries)
            {
                var id = archives.MapId(slot, e.Id);
                var node = tree.Get(id);
                if (node is null || node.Entry != e)
                {
                    continue;
                }

                var text = node.Text;
                if (text is null)
                {
                    continue;
                }

                var hasScript = text.Get("script") is not null;
                var hasNative = text.Get("native") is not null;
                if (hasScript || hasNative)
                {
                    s.TextEntriesWithScriptOrNative++;
                }

                if (!hasNative)
                {
                    continue;
                }

                var g = registry.Get(id)!;
                s.NativeEntries++;
                s.NativeKinds[g.NativeKind ?? string.Empty] = s.NativeKinds.GetValueOrDefault(g.NativeKind ?? string.Empty) + 1;
                s.NativeBindings[g.Binding] = s.NativeBindings.GetValueOrDefault(g.Binding) + 1;
                if (g.Script is not null)
                {
                    s.Scripts[g.Script] = s.Scripts.GetValueOrDefault(g.Script) + 1;
                }
                else
                {
                    s.Unbound.Add(g);
                }
            }
        }

        return s;
    }

    private (string? Script, int Owner) FindScript(ResourceNode node)
    {
        var cur = node;
        for (var hops = 0; cur is not null && hops <= MaxHops; hops++)
        {
            var s = cur.Text?.Get("script");
            if (s is not null)
            {
                return (s, cur.Id);
            }

            cur = cur.ParentId == 0 ? null : Tree.Get(cur.ParentId);
        }

        return (null, 0);
    }
}
