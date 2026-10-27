using System.Text;
using Slrr.Formats.Tufa;

namespace Slrr.Decompiler;

/// <summary>Decompiled source of one TUFA class plus what went wrong.</summary>
public sealed class DecompiledClass(string name, string source, DecompileDiagnostics diagnostics, int methods, int bodies)
{
    /// <summary>Fully qualified class name, e.g. <c>java.game.parts.bodypart.Mirror</c>.</summary>
    public string Name { get; } = name;

    /// <summary>Class declaration (without package/import lines).</summary>
    public string Source { get; } = source;

    public DecompileDiagnostics Diagnostics { get; } = diagnostics;

    public int MethodCount { get; } = methods;

    /// <summary>Method and field-initializer bodies turned into source.</summary>
    public int DecompiledBodies { get; } = bodies;

    /// <summary>Empty bodies referenced by native methods (flag 0x40).</summary>
    public int NativeBodies { get; init; }
}

/// <summary>Decompiles TUFA classes into the game's Java dialect.</summary>
public static class TufaDecompiler
{
    private const uint NoBody = 0xFFFFFFFF;

    /// <summary>Decompiles every class of a .class file into one compilation unit (package, imports, classes).</summary>
    public static string DecompileFile(IReadOnlyList<TufaClass> classes, out List<DecompiledClass> results)
    {
        results = classes.Select(Decompile).ToList();
        var sb = new StringBuilder();
        if (classes.Count == 0)
        {
            return string.Empty;
        }

        var package = Descriptors.PackageOf(classes[0].Name);
        sb.Append("//decompiled by slrr decompile (TUFA ").Append(classes.Count == 1 ? "1 class" : classes.Count + " classes").Append(")\n");
        if (package.Length > 0)
        {
            sb.Append("package ").Append(package).Append(";\n\n");
        }

        foreach (var p in Imports(classes, package))
        {
            sb.Append("import ").Append(p).Append(".*;\n");
        }

        foreach (var r in results)
        {
            sb.Append('\n').Append(r.Source);
        }

        return sb.ToString();
    }

    private static SortedSet<string> Imports(IReadOnlyList<TufaClass> classes, string package)
    {
        var packages = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var c in classes)
        {
            foreach (var k in c.Constants)
            {
                if (k.Type != TufaConstantType.Utf8 || k.Text is null)
                {
                    continue;
                }

                foreach (var q in Descriptors.ClassNames(k.Text))
                {
                    packages.Add(Descriptors.PackageOf(q));
                }
            }

            for (var i = 0; i < c.Constants.Count; i++)
            {
                if (c.Constants[i].Type == TufaConstantType.Class && c.ClassName((uint)i) is { } q)
                {
                    packages.Add(Descriptors.PackageOf(q.TrimStart('[')));
                }
            }
        }

        packages.Remove(package);
        packages.Remove(string.Empty);
        packages.RemoveWhere(p => p.Contains(';', StringComparison.Ordinal) || p.StartsWith('['));
        return packages;
    }

    /// <summary>Decompiles one class; errors are recorded in the diagnostics, never thrown.</summary>
    public static DecompiledClass Decompile(TufaClass cls)
    {
        var diag = new DecompileDiagnostics();
        var w = new SourceWriter();
        var simple = Descriptors.SimpleName(cls.Name);
        // CLSS word 1 is 1 only for java.lang.Runnable and java.game.GameState: the interface flag.
        var header = new StringBuilder(cls.ClassHeader1 == 1 ? "public interface " : "public class ").Append(simple);
        if (cls.SuperClass != NoBody && cls.ClassName(cls.SuperClass) is { } super && super != "java.lang.Object")
        {
            header.Append(" extends ").Append(Descriptors.SimpleName(super));
        }

        if (cls.Interfaces.Count > 0)
        {
            header.Append(" implements ").Append(string.Join(", ", cls.Interfaces.Select(i => Descriptors.SimpleName(cls.ClassName(i) ?? "?"))));
        }

        w.Line(0, header.ToString());
        w.Line(0, "{");

        var reserved = new HashSet<string>(StringComparer.Ordinal);
        foreach (var f in cls.Fields)
        {
            reserved.Add(cls.Utf8(f.NameIndex) ?? string.Empty);
        }

        var bodies = 0;
        var natives = 0;
        foreach (var f in cls.Fields)
        {
            var type = Descriptors.TypeName(cls.Utf8(f.DescriptorIndex) ?? "?");
            var text = Modifiers(f.Flags | (f.IsStatic ? 0x8u : 0u), isField: true) + type + " " + cls.Utf8(f.NameIndex);
            if (f.Value != NoBody)
            {
                if (f.Value < cls.Bodies.Count)
                {
                    try
                    {
                        var md = new MethodDecompiler(cls, cls.Bodies[(int)f.Value], diag, reserved) { IsStatic = f.IsStatic };
                        text += " = " + md.DecompileInitializer();
                        bodies++;
                    }
                    catch (DecompileException ex)
                    {
                        diag.Errors.Add($"field {cls.Utf8(f.NameIndex)}: {ex.Message}");
                        text += " /* initializer: " + ex.Message + " */";
                    }
                }
                else
                {
                    diag.Errors.Add($"field {cls.Utf8(f.NameIndex)}: initializer body {f.Value} out of range");
                }
            }

            w.Line(1, text + ";");
        }

        foreach (var m in cls.Methods)
        {
            w.Blank();
            var name = cls.Utf8(m.NameIndex) ?? "?";
            var (ps, ret) = Descriptors.Method(cls.Utf8(m.DescriptorIndex) ?? "()V");
            var isStatic = m.IsStatic || (m.Flags & 0x8) != 0;
            var isCtor = name == "<init>";
            var md = m.Value != NoBody && m.Value < cls.Bodies.Count
                ? new MethodDecompiler(cls, cls.Bodies[(int)m.Value], diag, reserved) { IsStatic = isStatic }
                : null;
            var paramVars = (md ?? new MethodDecompiler(cls, new TufaMethodBody([]), diag, reserved) { IsStatic = isStatic })
                .BindParameters(ps, isStatic);
            var sig = Modifiers(m.Flags | (m.IsStatic ? 0x8u : 0u), isField: false)
                + (isCtor ? simple : (ret ?? "void") + " " + name)
                + "( " + string.Join(", ", paramVars.Select(p => p.Type + " " + p.Name)) + " )";
            sig = sig.Replace("(  )", "()", StringComparison.Ordinal);
            if (md is null || (m.Flags & 0x40) != 0)
            {
                if (m.Value != NoBody && (m.Flags & 0x40) == 0)
                {
                    diag.Errors.Add($"method {name}: body {m.Value} out of range");
                }

                if ((m.Flags & 0x40) != 0 && m.Value < cls.Bodies.Count)
                {
                    // native methods point at an empty body
                    natives++;
                    if (cls.Bodies[(int)m.Value].Instructions.Count != 0)
                    {
                        diag.Errors.Add($"native method {name} has a non-empty body");
                    }
                }

                w.Line(1, sig + ";");
                continue;
            }

            w.Line(1, sig);
            try
            {
                var stmts = md.DecompileMethod();
                bodies++;
                w.Block(1, stmts);
            }
            catch (DecompileException ex)
            {
                diag.Errors.Add($"method {name}: {ex.Message}");
                w.Line(1, "{");
                w.Line(2, "/* decompilation failed: " + ex.Message + " */");
                w.Line(1, "}");
            }
        }

        w.Line(0, "}");
        return new DecompiledClass(cls.Name, w.ToString(), diag, cls.Methods.Count, bodies) { NativeBodies = natives };
    }

    /// <summary>
    /// Access flags as observed: 0x1 public, 0x2 protected, 0x4 private, 0x8 static, 0x10 abstract (methods),
    /// 0x20 final, 0x40 native, 0x80 synchronized.
    /// </summary>
    public static string Modifiers(uint flags, bool isField)
    {
        var sb = new StringBuilder();
        if ((flags & 0x1) != 0)
        {
            sb.Append("public ");
        }

        if ((flags & 0x2) != 0)
        {
            sb.Append("protected ");
        }

        if ((flags & 0x4) != 0)
        {
            sb.Append("private ");
        }

        if ((flags & 0x20) != 0)
        {
            sb.Append("final ");
        }

        if ((flags & 0x8) != 0)
        {
            sb.Append("static ");
        }

        if ((flags & 0x10) != 0)
        {
            sb.Append(isField ? "/*0x10*/ " : "abstract ");
        }

        if ((flags & 0x80) != 0)
        {
            sb.Append("synchronized ");
        }

        if ((flags & 0x40) != 0)
        {
            sb.Append("native ");
        }

        var unknown = flags & ~0xFFu;
        if (unknown != 0)
        {
            sb.Append("/*flags 0x").Append(unknown.ToString("X")).Append("*/ ");
        }

        return sb.ToString();
    }
}
