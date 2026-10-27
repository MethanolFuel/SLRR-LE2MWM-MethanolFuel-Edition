using System.Globalization;
using Slrr.Core;
using Slrr.Formats.Tufa;
using Slrr.Vm;

namespace Slrr.Cli;

/// <summary>
/// <c>run-class</c>: load a TUFA class into the script VM and invoke a static method or constructor; also the
/// corpus checks behind docs/vm.md.
/// <code>
/// run-class &lt;Class|file.class&gt; [method|method(desc)|&lt;init&gt;] [args...] [--root dir] [--cp file|dir]... [--trace] [--budget n] [--time s]
/// run-class --verify [gameRoot|dir]          static stack-effect check of every TREE body
/// run-class --survey [gameRoot]              class init + every no-arg static method / constructor, outcome counts
/// run-class --natives [gameRoot] [tsv]       native methods in the corpus vs the registry vs the exe's table
/// </code>
/// </summary>
public static class CmdVm
{
    public const string Usage = """
        run-class <Class|file.class> [method[(desc)]|<init>] [args...] [--root dir] [--cp file|dir]... [--trace] [--budget n] [--time s]
        run-class --verify [gameRoot|dir]       static stack-effect check of every TREE body
        run-class --survey [gameRoot] [--deep] [--stub] [--list]  class init + no-arg static methods/constructors (--deep: every
                                                 scripted method with default args; --stub: missing natives
                                                 return 0/null), outcome counts
        run-class --natives [gameRoot] [tsv]    corpus native methods vs registered natives vs natives-redline.tsv
        """;

    public static int Run(string[] args, TextWriter o, TextWriter e)
    {
        try
        {
            if (args.Length >= 1 && args[0] == "--verify")
            {
                return Verify(Positional(args, 1) ?? ".", o);
            }

            if (args.Length >= 1 && args[0] == "--survey")
            {
                return Survey(Positional(args, 1) ?? ".", args.Contains("--list"), args.Contains("--deep"), args.Contains("--stub"), o);
            }

            if (args.Length >= 1 && args[0] == "--natives")
            {
                return Natives(Positional(args, 1) ?? ".", Positional(args, 2), o);
            }

            if (args.Length >= 1 && !args[0].StartsWith("--", StringComparison.Ordinal))
            {
                return RunClass(args, o, e);
            }

            e.WriteLine(Usage);
            return 2;
        }
        catch (VmException ex)
        {
            e.WriteLine($"error: {ex}");
            return 1;
        }
        catch (Exception ex) when (ex is SlrrFormatException or IOException or UnauthorizedAccessException)
        {
            e.WriteLine($"error: {ex.Message}");
            return 1;
        }
    }

    private static string? Positional(string[] args, int index)
    {
        var plain = args.Where(a => !a.StartsWith("--", StringComparison.Ordinal)).ToList();
        return index - 1 < plain.Count ? plain[index - 1] : null;
    }

    private static string? FindRoot(string start)
    {
        for (var d = new DirectoryInfo(Path.GetFullPath(start)); d is not null; d = d.Parent)
        {
            if (File.Exists(Path.Combine(d.FullName, "sl.rpk")) && Directory.Exists(Path.Combine(d.FullName, "system")))
            {
                return d.FullName;
            }
        }

        return null;
    }

    private static int RunClass(string[] args, TextWriter o, TextWriter e)
    {
        var opts = new List<string>();
        var positional = new List<string>();
        var cp = new List<string>();
        string? root = null;
        long budget = 50_000_000;
        var time = float.PositiveInfinity;
        var trace = false;
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--root" when i + 1 < args.Length: root = args[++i]; break;
                case "--cp" when i + 1 < args.Length: cp.Add(args[++i]); break;
                case "--budget" when i + 1 < args.Length: budget = long.Parse(args[++i], CultureInfo.InvariantCulture); break;
                case "--time" when i + 1 < args.Length: time = float.Parse(args[++i], CultureInfo.InvariantCulture); break;
                case "--trace": trace = true; break;
                default:
                    if (args[i].StartsWith("--", StringComparison.Ordinal))
                    {
                        opts.Add(args[i]);
                    }
                    else
                    {
                        positional.Add(args[i]);
                    }

                    break;
            }
        }

        var target = positional[0];
        var isFile = target.EndsWith(".class", StringComparison.OrdinalIgnoreCase) && File.Exists(target);
        root ??= FindRoot(isFile ? Path.GetDirectoryName(Path.GetFullPath(target))! : ".") ?? FindRoot(".");
        var path = new VmClassPath(root is null ? null : new GameFileSystem(root));
        foreach (var c in cp)
        {
            if (Directory.Exists(c))
            {
                path.AddDirectory(c);
            }
            else
            {
                path.AddFile(c);
            }
        }

        var className = target;
        if (isFile)
        {
            className = path.AddFile(target)[0];
        }

        var vm = new Vm.Vm(path) { Out = o, InstructionBudget = budget, Trace = trace ? o : null };
        var cls = vm.LoadClass(className);
        o.WriteLine($"class {cls.Name} extends {cls.Super?.Name ?? "-"}  ({cls.Source ?? "in memory"}; game root {root ?? "none"})");
        vm.EnsureInitialized(cls);
        foreach (var f in cls.StaticFields)
        {
            o.WriteLine($"  static {f.Name} = {Show(vm, cls.StaticValues[f.Slot])}");
        }

        if (positional.Count < 2)
        {
            return 0;
        }

        var spec = positional[1];
        var argText = positional.Skip(2).ToList();
        string name = spec;
        string? desc = null;
        var paren = spec.IndexOf('(');
        if (paren > 0)
        {
            name = spec[..paren];
            desc = spec[paren..];
        }

        var method = vm.FindStaticOrAny(cls, name, desc, desc is null ? argText.Count : null);
        var argv = method.Params.Select((p, i) => ParseArg(vm, p, i < argText.Count ? argText[i] : throw new VmException($"missing argument {i + 1} ({VmTypes.Pretty(p)})"))).ToArray();
        o.WriteLine($"invoke {method.Signature}({string.Join(", ", argv.Select(a => Show(vm, a)))})");
        VmObject? self = null;
        if (method.IsConstructor || !method.IsStatic)
        {
            self = vm.NewObject(cls);
        }

        var thread = vm.Spawn(method, self, argv, "main");
        var left = vm.RunUntilIdle(time);
        foreach (var (t, err) in vm.ThreadErrors)
        {
            e.WriteLine($"thread {t.Name}: {err}");
        }

        if (thread.Error is not null)
        {
            return 1;
        }

        if (thread.IsAlive)
        {
            o.WriteLine($"main thread still {thread.State.ToString().ToLowerInvariant()} at t={vm.Time.ToString(CultureInfo.InvariantCulture)}s");
        }
        else if (method.IsConstructor)
        {
            o.WriteLine($"constructed {Show(vm, Value.Obj(self))}");
        }
        else
        {
            o.WriteLine($"result: {Show(vm, thread.Result)}");
            if (self is not null)
            {
                o.WriteLine($"this: {Show(vm, Value.Obj(self))}");
            }
        }

        o.WriteLine($"{vm.Executed} instructions, virtual time {vm.Time.ToString(CultureInfo.InvariantCulture)}s, {left} threads left, {vm.LoadedClasses.Count} classes loaded");
        _ = opts;
        return 0;
    }

    private static Value ParseArg(Vm.Vm vm, string desc, string text) => VmTypes.KindOf(desc) switch
    {
        VmKind.Int => Value.Int(text.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            ? int.Parse(text[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture)
            : int.Parse(text, CultureInfo.InvariantCulture)),
        VmKind.Float => Value.Float(float.Parse(text.TrimEnd('f', 'F'), CultureInfo.InvariantCulture)),
        _ when text == "null" => Value.Null,
        _ when desc == "Ljava.lang.String;" => Value.Obj(vm.NewString(text)),
        _ => throw new VmException($"cannot pass '{text}' as {VmTypes.Pretty(desc)} from the command line"),
    };

    private static string Show(Vm.Vm vm, Value v)
    {
        if (v.Kind != VmKind.Object || v.Ref is null || v.Ref.Native is string)
        {
            return v.Describe();
        }

        if (v.Ref is VmArray a)
        {
            return $"{a.Describe()} {{{string.Join(", ", a.Items.Take(16).Select(x => x.Describe()))}{(a.Items.Length > 16 ? ", ..." : "")}}}";
        }

        var fields = v.Ref.Class.Hierarchy().Reverse().SelectMany(c => c.InstanceFields)
            .Select(f => $"{f.Name}={v.Ref.Fields[f.Slot].Describe()}");
        _ = vm;
        return $"{v.Ref.Describe()} {{{string.Join(", ", fields)}}}";
    }

    // ---------------------------------------------------------------- corpus checks

    private static IEnumerable<string> ClassFiles(string root) =>
        File.Exists(Path.Combine(root, "sl.rpk"))
            ? new GameFileSystem(root).Enumerate(".class")
            : Directory.EnumerateFiles(root, "*.class", SearchOption.AllDirectories).Order(StringComparer.Ordinal);

    private static int Verify(string root, TextWriter o)
    {
        var report = new VmVerifyReport();
        var files = 0;
        foreach (var f in ClassFiles(root))
        {
            files++;
            foreach (var c in TufaFile.Read(f))
            {
                VmVerifier.Verify(c, report, Path.GetRelativePath(root, f));
            }
        }

        o.WriteLine($"{files} files, {report.Bodies} bodies ({report.MethodBodies} methods, {report.InitializerBodies} field initializers, {report.UnreferencedBodies} unreferenced, {report.EmptyBodies} empty)");
        o.WriteLine($"stack-shape check passed: {report.Ok}/{report.Bodies}; reachable instructions {report.ReachableInstructions}, consumed {report.ConsumedInstructions}, dead {report.UnreachableInstructions}, standalone literals {report.StandaloneLiterals}");
        o.WriteLine("terminators reached: " + string.Join(", ", report.Terminators.OrderBy(k => k.Key, StringComparer.Ordinal).Select(k => $"{k.Key} {k.Value}")));
        foreach (var fail in report.Failures.Take(40))
        {
            o.WriteLine("  FAIL " + fail);
        }

        return report.Failures.Count == 0 ? 0 : 1;
    }

    private static int Survey(string root, bool list, bool deep, bool stub, TextWriter o)
    {
        var fs = new GameFileSystem(root);
        var path = new VmClassPath(fs);
        var names = path.AllScannedClassNames().Order(StringComparer.Ordinal).ToList();
        var outcomes = new Dictionary<string, int>(StringComparer.Ordinal);
        var missing = new Dictionary<string, int>(StringComparer.Ordinal);
        var okMethods = new List<string>();
        var messages = new Dictionary<string, int>(StringComparer.Ordinal);
        var examples = new Dictionary<string, string>(StringComparer.Ordinal);
        void Count(string k) => outcomes[k] = outcomes.GetValueOrDefault(k) + 1;
        string Classify(Exception ex)
        {
            if (ex is VmNativeMissingException nm)
            {
                var key = $"{nm.ClassName}.{nm.Name}{nm.Descriptor}";
                missing[key] = missing.GetValueOrDefault(key) + 1;
                return "native missing";
            }

            return ex switch
            {
                VmClassNotFoundException => "class not found",
                VmBudgetExceededException => "budget exhausted",
                VmOpcodeException => "opcode error",
                VmExitException => "System.exit",
                _ => "script error",
            };
        }

        int initOk = 0, initFail = 0;
        foreach (var name in names)
        {
            // A fresh VM per class keeps failures independent.
            var vm = new Vm.Vm(path, Registry(stub)) { InstructionBudget = 2_000_000 };
            VmClass cls;
            try
            {
                cls = vm.LoadClass(name);
                vm.EnsureInitialized(cls);
                initOk++;
            }
            catch (VmException ex)
            {
                initFail++;
                Count("class init: " + Classify(ex));
                continue;
            }

            var selected = deep
                ? cls.Methods.Where(m => !m.IsNative && m.Code is not null)
                : cls.Methods.Where(m => m.ArgCount == 0 && (m.IsStatic || m.IsConstructor));
            foreach (var m in selected)
            {
                var kind = m.IsConstructor ? "constructor" : m.IsStatic ? "static method" : "instance method";
                var run = new Vm.Vm(path, Registry(stub)) { InstructionBudget = 2_000_000 };
                try
                {
                    var c = run.LoadClass(name);
                    var target = c.Methods.First(x => x.Descriptor == m.Descriptor && x.Name == m.Name && x.IsStatic == m.IsStatic);
                    var args = target.Params.Select(VmTypes.DefaultValue).ToArray();
                    if (target.IsConstructor)
                    {
                        var obj = run.NewObject(c);
                        run.InvokeVoid(target, obj, args);
                    }
                    else if (target.IsStatic)
                    {
                        run.Invoke(target, null, args);
                    }
                    else
                    {
                        run.Invoke(target, run.Construct(c), args);
                    }

                    Count(kind + ": completed");
                    okMethods.Add(m.Signature);
                }
                catch (VmException ex)
                {
                    var cat = Classify(ex);
                    Count(kind + ": " + cat);
                    if (cat is "script error" or "class not found")
                    {
                        var msg = System.Text.RegularExpressions.Regex.Replace(ex.Message, @"[0-9a-f]{4,}|\d+", "#");
                        messages[msg] = messages.GetValueOrDefault(msg) + 1;
                        examples.TryAdd(msg, ex.ScriptTrace.FirstOrDefault() ?? m.Signature);
                    }
                }
            }
        }

        o.WriteLine($"{names.Count} classes; static initialization ok {initOk}, failed {initFail}");
        foreach (var (k, v) in outcomes.OrderBy(k => k.Key, StringComparer.Ordinal))
        {
            o.WriteLine($"  {k}: {v}");
        }

        o.WriteLine("most frequent missing natives:");
        foreach (var (k, v) in missing.OrderByDescending(k => k.Value).ThenBy(k => k.Key, StringComparer.Ordinal).Take(15))
        {
            o.WriteLine($"  {v,5} {k}");
        }

        o.WriteLine("most frequent script errors (numbers masked):");
        foreach (var (k, v) in messages.OrderByDescending(k => k.Value).ThenBy(k => k.Key, StringComparer.Ordinal).Take(list ? 1000 : 25))
        {
            o.WriteLine($"  {v,5} {k}" + (list ? $"   [{examples[k]}]" : string.Empty));
        }

        if (list)
        {
            foreach (var m in okMethods)
            {
                o.WriteLine("  ok " + m);
            }
        }

        return 0;
    }

    private static NativeRegistry Registry(bool stub)
    {
        var r = NativeRegistry.CreateDefault();
        if (stub)
        {
            r.Fallback = NativeRegistry.DefaultStub;
        }

        return r;
    }

    private static int Natives(string root, string? tsv, TextWriter o)
    {
        var registry = NativeRegistry.CreateDefault();
        var declared = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var f in ClassFiles(root))
        {
            foreach (var c in TufaFile.Read(f))
            {
                foreach (var m in c.Methods.Where(m => (m.Flags & 0x40) != 0))
                {
                    declared.Add($"{c.Name}\t{c.Utf8(m.NameIndex)}\t{c.Utf8(m.DescriptorIndex)}");
                }
            }
        }

        var exe = new SortedSet<string>(StringComparer.Ordinal);
        if (tsv is not null)
        {
            foreach (var line in File.ReadLines(tsv).Skip(1))
            {
                var p = line.Split('\t');
                if (p.Length >= 4)
                {
                    exe.Add($"{p[1]}\t{p[2]}\t{p[3]}");
                }
            }
        }

        var registered = registry.Keys.Select(k => $"{k.ClassName}\t{k.Name}\t{k.Descriptor}").ToHashSet(StringComparer.Ordinal);
        o.WriteLine($"native methods declared in class files: {declared.Count}; registered in the C# VM: {registered.Count} ({registered.Count(declared.Contains)} of them declared)");
        if (tsv is not null)
        {
            o.WriteLine($"exe registrations: {exe.Count}; declared and registered by the exe: {declared.Count(exe.Contains)}; declared but not in the exe table: {declared.Count(d => !exe.Contains(d))}");
            o.WriteLine($"C# coverage of the exe table: {exe.Count(registered.Contains)}/{exe.Count}");
            foreach (var d in declared.Where(d => !exe.Contains(d)))
            {
                o.WriteLine("  declared, not in exe table: " + d.Replace('\t', ' '));
            }
        }

        foreach (var g in declared.GroupBy(d => d.Split('\t')[0]).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            o.WriteLine($"  {g.Key}: {g.Count(registered.Contains)}/{g.Count()} implemented");
        }

        return 0;
    }
}
