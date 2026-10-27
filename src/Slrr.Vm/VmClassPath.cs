using System.Text;
using Slrr.Core;
using Slrr.Formats.Rpak;
using Slrr.Formats.Tufa;

namespace Slrr.Vm;

/// <summary>A class definition found on the class path: the parsed TUFA class and the file it came from.</summary>
public sealed record VmClassSource(TufaClass Class, string? File);

/// <summary>A <c>script ...</c> line of an .rpk type entry, e.g. <c>system.rpk:0x47 Config → java.util.Config</c>.</summary>
public sealed record RpkScriptRef(string Archive, uint Id, string Alias, string Target);

/// <summary>
/// Finds TUFA classes by dotted name. Order: classes added explicitly (<see cref="AddFile"/>, <see cref="AddDirectory"/>,
/// <see cref="AddClass"/>), then the engine's package roots inside the game tree (<c>java.lang</c> →
/// <c>system\Scripts\lang</c>, <c>java.game</c> → <c>sl\Scripts\game</c>, <c>java.game.parts</c> → <c>parts\scripts</c>, ...;
/// strings in StreetLegal_Redline.exe), then a one-time scan of every .class file in the game tree (classes in
/// multi-class files and in car/part script folders, whose path does not follow the package name).
/// </summary>
public sealed class VmClassPath
{
    /// <summary>Package roots compiled into the engine (docs/research/script-vm-opcodes.md §6). Longest prefix wins.</summary>
    public static readonly IReadOnlyList<(string Package, string Dir)> PackageRoots =
    [
        ("java.game.parts.accessories", @"parts\accessories\scripts"),
        ("java.game.parts", @"parts\scripts"),
        ("java.game", @"sl\scripts\game"),
        ("java.lang", @"system\scripts\lang"),
        ("java.io", @"system\scripts\io"),
        ("java.util", @"system\scripts\util"),
        ("java.render", @"system\scripts\render"),
        ("java.sound", @"system\scripts\sound"),
    ];

    private readonly Dictionary<string, VmClassSource> _explicit = new(StringComparer.Ordinal);
    private readonly Dictionary<string, IReadOnlyList<TufaClass>> _fileCache = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, VmClassSource>? _scan;

    public VmClassPath(GameFileSystem? game = null)
    {
        Game = game;
    }

    public GameFileSystem? Game { get; }

    /// <summary>Files the corpus scan could not parse (path → error).</summary>
    public Dictionary<string, string> ScanErrors { get; } = new(StringComparer.Ordinal);

    /// <summary>Class names defined by more than one file in the corpus scan (the first file in ordinal path order wins).</summary>
    public Dictionary<string, List<string>> ScanDuplicates { get; } = new(StringComparer.Ordinal);

    public void AddClass(TufaClass cls, string? file = null) => _explicit[cls.Name] = new VmClassSource(cls, file);

    /// <summary>Adds every class of one .class file; returns their names.</summary>
    public IReadOnlyList<string> AddFile(string path)
    {
        var classes = ReadFile(path);
        foreach (var c in classes)
        {
            _explicit[c.Name] = new VmClassSource(c, Path.GetFullPath(path));
        }

        return classes.Select(c => c.Name).ToList();
    }

    public int AddDirectory(string dir)
    {
        var n = 0;
        foreach (var f in Directory.EnumerateFiles(dir, "*.class", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
        {
            n += AddFile(f).Count;
        }

        return n;
    }

    public VmClassSource? Find(string className)
    {
        if (_explicit.TryGetValue(className, out var s))
        {
            return s;
        }

        if (Game is null)
        {
            return null;
        }

        var byPackage = FindByPackageRoot(className);
        if (byPackage is not null)
        {
            return byPackage;
        }

        EnsureScan();
        return _scan!.GetValueOrDefault(className);
    }

    /// <summary>Every class name the corpus scan found (forces the scan).</summary>
    public IReadOnlyCollection<string> AllScannedClassNames()
    {
        EnsureScan();
        return _scan!.Keys;
    }

    /// <summary>
    /// Resolves the target of an rpk <c>script</c> line: a dotted class name (<c>java.util.Config</c>) or a game path
    /// to a .class file (<c>system\Scripts\io\Controller.class</c>, whose first class is returned).
    /// </summary>
    public VmClassSource? FindScriptTarget(string target)
    {
        target = target.Trim();
        if (!target.EndsWith(".class", StringComparison.OrdinalIgnoreCase))
        {
            return Find(target);
        }

        var file = Game?.Resolve(target) ?? (File.Exists(target) ? target : null);
        if (file is null)
        {
            return null;
        }

        var classes = ReadFile(file);
        return classes.Count == 0 ? null : new VmClassSource(classes[0], file);
    }

    /// <summary>Lists the <c>script</c> lines of every type entry in the given .rpk archives (default: all in the game tree).</summary>
    public IReadOnlyList<RpkScriptRef> ScanRpkScripts(IEnumerable<string>? archives = null)
    {
        archives ??= Game?.Enumerate(".rpk") ?? [];
        var list = new List<RpkScriptRef>();
        foreach (var path in archives)
        {
            RpakArchive rpk;
            try
            {
                rpk = RpakArchive.Read(path);
            }
            catch (SlrrFormatException)
            {
                continue;
            }

            var name = Game is null ? Path.GetFileName(path) : Path.GetRelativePath(Game.Root, path);
            foreach (var e in rpk.Entries)
            {
                var data = rpk.GetData(e);
                if (data.Length == 0 || data.Length > 64 * 1024)
                {
                    continue;
                }

                var text = Encoding.Latin1.GetString(data.Span);
                foreach (var line in text.Split('\n'))
                {
                    var t = line.Trim().TrimEnd('\0');
                    if (t.StartsWith("script ", StringComparison.Ordinal))
                    {
                        list.Add(new RpkScriptRef(name, e.Id, e.Alias, t[7..].Trim()));
                    }
                }
            }
        }

        return list;
    }

    /// <summary>The game path the package roots map a class name to, e.g. <c>system\scripts\lang\String.class</c>.</summary>
    public static string? PackagePath(string className)
    {
        foreach (var (pkg, dir) in PackageRoots)
        {
            if (className.StartsWith(pkg + ".", StringComparison.Ordinal))
            {
                var rest = className[(pkg.Length + 1)..].Replace('.', '\\');
                return dir + "\\" + rest + ".class";
            }
        }

        return null;
    }

    private VmClassSource? FindByPackageRoot(string className)
    {
        var rel = PackagePath(className);
        var file = rel is null ? null : Game!.Resolve(rel);
        if (file is null)
        {
            return null;
        }

        try
        {
            var c = ReadFile(file).FirstOrDefault(x => x.Name == className);
            return c is null ? null : new VmClassSource(c, file);
        }
        catch (SlrrFormatException)
        {
            return null;
        }
    }

    private IReadOnlyList<TufaClass> ReadFile(string path)
    {
        var full = Path.GetFullPath(path);
        if (!_fileCache.TryGetValue(full, out var classes))
        {
            classes = TufaFile.Read(full);
            _fileCache[full] = classes;
        }

        return classes;
    }

    private void EnsureScan()
    {
        if (_scan is not null || Game is null)
        {
            _scan ??= new Dictionary<string, VmClassSource>(StringComparer.Ordinal);
            return;
        }

        var scan = new Dictionary<string, VmClassSource>(StringComparer.Ordinal);
        foreach (var file in Game.Enumerate(".class"))
        {
            IReadOnlyList<TufaClass> classes;
            try
            {
                classes = ReadFile(file);
            }
            catch (Exception ex) when (ex is SlrrFormatException or IOException)
            {
                ScanErrors[file] = ex.Message;
                continue;
            }

            foreach (var c in classes)
            {
                if (scan.TryGetValue(c.Name, out var first))
                {
                    if (!ScanDuplicates.TryGetValue(c.Name, out var dups))
                    {
                        dups = [first.File!];
                        ScanDuplicates[c.Name] = dups;
                    }

                    dups.Add(file);
                    continue;
                }

                scan[c.Name] = new VmClassSource(c, file);
            }
        }

        _scan = scan;
    }
}
