using Slrr.Core;
using Slrr.Formats.Bones;
using Slrr.Formats.Cfg;
using Slrr.Formats.Ptx;
using Slrr.Formats.Rpak;
using Slrr.Formats.Scx;
using Slrr.Formats.Tufa;
using Slrr.Formats.Validation;

namespace Slrr.Cli;

public static class Commands
{
    private const string Usage = """
        slrr <command> [args]

          unpack <archive.rpk> <outDir>        extract entries + manifest (rpak.json)
          pack <dir> <out.rpk>                 rebuild an archive from an unpacked directory
          rpk-info <archive.rpk>               list references and entries
          roundtrip [gameRoot]                 read->write and unpack->pack every .rpk, compare bytes
          validate [gameRoot] [--all]          cfg grammar, dangling file refs, duplicate ids, rpk checks
          dump-class <file.class> [--no-code]  TUFA constant pool, fields, methods, code
          dump-scx <file.scx> [--gltf out]     SCX meshes/chunks, optional glTF export
          coverage [gameRoot]                  parse every rpk/cfg/scx/ptx/bon/skl/class, report counts
          dump-instances <archive.rpk>         map/garage instance trees (see --tree, --chunks, --coverage)
          export-gltf <scx> <out.gltf>         textured glTF export (also --texture, --survey)
          cfg-schema [gameRoot] [--list]       per-keyword cfg argument schema and violations
          decompile <file.class|dir>           decompile TUFA classes to source (--out, --fidelity, --quiet)
          boot [--stub]                        headless boot: Config -> Init -> GameLogic, reports missing natives
          engine-boot [--stub]                 boot with the System, Parts and Ui native modules plugged in
          parts [--survey|--natives]           part/chassis data-model natives and survey
          ui [--survey|--natives]              headless UI/render/input natives and survey
          audio <list|inspect|engines|rev|demo>  sound inspection and offline rendering to WAV
          world <stats|surfaces|height|ray|map|bench|verify|angles>  collision world of a map archive
          render <car|map|ui> ...              offscreen renders to PNG (software GL is fine)
          drive <car> [--scenario ...]         vehicle physics scenarios (0-100, top speed, braking, skidpad)
          play [script | --smoke]              headless game host driven by scripted input
          run-class <class> ...                run a shipped script class in the VM (--verify, --survey, --natives)
        """;

    public static int Run(string[] args, TextWriter o, TextWriter e)
    {
        if (args.Length == 0)
        {
            o.WriteLine(Usage);
            return 2;
        }

        try
        {
            return args[0] switch
            {
                "unpack" when args.Length == 3 => Unpack(args[1], args[2], o),
                "pack" when args.Length == 3 => Pack(args[1], args[2], o),
                "rpk-info" when args.Length == 2 => RpkInfo(args[1], o),
                "roundtrip" => Roundtrip(Root(args), o),
                "validate" => Validate(Root(args), args.Contains("--all"), o),
                "dump-class" when args.Length >= 2 => DumpClass(args[1], !args.Contains("--no-code"), o),
                "dump-scx" when args.Length >= 2 => DumpScx(args[1], Option(args, "--gltf"), o),
                "coverage" => Coverage(Root(args), o),
                "dump-instances" => CmdInstances.Run(args[1..], o, e),
                "export-gltf" => CmdGltf.Run(args[1..], o, e),
                "cfg-schema" => CmdCfgSchema.Run(args[1..], o, e),
                "decompile" => CmdDecompile.Run(args[1..], o, e),
                "run-class" => CmdVm.Run(args[1..], o, e),
                "audio" => CmdAudio.Run(args[1..], o, e),
                "world" => CmdWorld.Run(args[1..], o, e),
                "render" => CmdRender.Run(args[1..], o, e),
                "drive" => CmdDrive.Run(args[1..], o, e),
                "play" => CmdPlay.Run(args[1..], o, e),
                "boot" => CmdBoot.Run(args[1..], o, e),
                "engine-boot" => CmdEngine.Run(args[1..], o, e),
                "parts" => CmdParts.Run(args[1..], o, e),
                "ui" => CmdUi.Run(args[1..], o, e),
                _ => Fail(e, Usage),
            };
        }
        catch (Exception ex) when (ex is SlrrFormatException or IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            e.WriteLine($"error: {ex.Message}");
            return 1;
        }
    }

    private static int Fail(TextWriter e, string msg)
    {
        e.WriteLine(msg);
        return 2;
    }

    private static string Root(string[] args) =>
        args.Skip(1).FirstOrDefault(a => !a.StartsWith("--", StringComparison.Ordinal)) ?? Directory.GetCurrentDirectory();

    private static string? Option(string[] args, string name)
    {
        var i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }

    private static int Unpack(string rpk, string outDir, TextWriter o)
    {
        var archive = RpakArchive.Read(rpk);
        RpakDirectory.Unpack(archive, outDir);
        o.WriteLine($"unpacked {archive.Entries.Count} entries, {archive.FindGaps().Count} gap blocks to {outDir}");
        return 0;
    }

    private static int Pack(string dir, string outFile, TextWriter o)
    {
        var archive = RpakDirectory.Pack(dir);
        var bytes = archive.Write();
        File.WriteAllBytes(outFile, bytes);
        o.WriteLine($"packed {archive.Entries.Count} entries, {bytes.Length} bytes -> {outFile}");
        return 0;
    }

    private static int RpkInfo(string rpk, TextWriter o)
    {
        var a = RpakArchive.Read(rpk);
        o.WriteLine($"RPAK v0x{a.Version:X}  refs {a.References.Count}  entries {a.Entries.Count}  data @{a.DataStart} ({a.DataRegion.Length} bytes)");
        foreach (var r in a.References)
        {
            o.WriteLine($"  ref {r.Index}: {r.Name}");
        }

        foreach (var en in a.Entries)
        {
            o.WriteLine($"  super 0x{en.SuperId:X8} id 0x{en.Id:X8} kind {en.TypeOfEntry}/{en.AdditionalType} @{en.Offset} +{en.Size} {en.Alias}");
        }

        var gaps = a.FindGaps();
        o.WriteLine($"  gaps: {gaps.Count} ({gaps.Sum(g => (long)g.Length)} bytes not covered by entries)");
        return 0;
    }

    public static int Roundtrip(string root, TextWriter o)
    {
        var fs = new GameFileSystem(root);
        int total = 0, direct = 0, viaDir = 0;
        var temp = Path.Combine(Path.GetTempPath(), "slrr-roundtrip-" + Environment.ProcessId);
        foreach (var file in fs.Enumerate(".rpk"))
        {
            total++;
            var rel = Path.GetRelativePath(fs.Root, file);
            try
            {
                var original = File.ReadAllBytes(file);
                var archive = RpakArchive.Read(original);
                if (archive.Write().AsSpan().SequenceEqual(original))
                {
                    direct++;
                }
                else
                {
                    o.WriteLine($"DIFF (read->write) {rel}");
                }

                var dir = Path.Combine(temp, total.ToString(System.Globalization.CultureInfo.InvariantCulture));
                RpakDirectory.Unpack(archive, dir);
                if (RpakDirectory.Pack(dir).Write().AsSpan().SequenceEqual(original))
                {
                    viaDir++;
                }
                else
                {
                    o.WriteLine($"DIFF (unpack->pack) {rel}");
                }

                Directory.Delete(dir, true);
            }
            catch (SlrrFormatException ex)
            {
                o.WriteLine($"FAIL {rel}: {ex.Message}");
            }
        }

        if (Directory.Exists(temp))
        {
            Directory.Delete(temp, true);
        }

        o.WriteLine($"rpk files: {total}; byte-identical read->write: {direct}; byte-identical unpack->pack: {viaDir}");
        return direct == total && viaDir == total ? 0 : 1;
    }

    private static int Validate(string root, bool all, TextWriter o)
    {
        var v = new ContentValidator(new GameFileSystem(root));
        v.ValidateAll();
        var shown = 0;
        foreach (var d in v.Diagnostics.OrderByDescending(d => d.Severity))
        {
            if (all || d.Severity == Severity.Error || shown < 40)
            {
                o.WriteLine(d);
                shown++;
            }
        }

        o.WriteLine();
        o.WriteLine("summary by code:");
        foreach (var g in v.Diagnostics.GroupBy(d => (d.Severity, d.Code)).OrderBy(g => g.Key.Code, StringComparer.Ordinal))
        {
            o.WriteLine($"  {g.Key.Code} {g.Key.Severity,-7} {g.Count()}");
        }

        o.WriteLine($"cfg files: {v.CfgFiles}, with errors: {v.CfgFilesWithErrors}; rpk files: {v.RpakFiles}, unreadable: {v.RpakFilesFailed}; file refs checked: {v.FileRefsChecked}");
        return v.Diagnostics.Any(d => d.Severity == Severity.Error) ? 1 : 0;
    }

    private static int DumpClass(string file, bool code, TextWriter o)
    {
        foreach (var c in TufaFile.Read(file))
        {
            o.Write(TufaFile.Dump(c, code));
        }

        return 0;
    }

    private static int DumpScx(string file, string? gltf, TextWriter o)
    {
        var scx = ScxFile.Read(file);
        o.WriteLine($"SCX v{scx.Version}: {scx.Meshes.Count} meshes, {scx.Chunks.Count} chunks, covered {scx.CoveredBytes}/{scx.TotalBytes} bytes");
        foreach (var c in scx.Chunks)
        {
            o.WriteLine($"  chunk type {c.Type} @{c.Offset} {c.Payload.Length} bytes");
        }

        for (var i = 0; i < scx.Meshes.Count; i++)
        {
            var m = scx.Meshes[i];
            o.WriteLine($"  mesh {i}: material {m.Material.Length} bytes, {m.Vertices.Count} vertices (flags 0x{m.Vertices.Flags:X}, stride {m.Vertices.Stride}), {m.Indices.Count / 3} triangles");
        }

        if (gltf is not null)
        {
            File.WriteAllText(gltf, GltfExporter.Export(scx, Path.GetFileNameWithoutExtension(file)));
            o.WriteLine($"wrote {gltf}");
        }

        return 0;
    }

    public static int Coverage(string root, TextWriter o)
    {
        var fs = new GameFileSystem(root);
        var failures = new List<string>();

        (int ok, int total) Count(string[] exts, Action<string> parse)
        {
            int ok = 0, total = 0;
            foreach (var f in fs.Enumerate(exts))
            {
                total++;
                try
                {
                    parse(f);
                    ok++;
                }
                catch (SlrrFormatException ex)
                {
                    failures.Add($"{Path.GetRelativePath(fs.Root, f)}: {ex.Message}");
                }
            }

            return (ok, total);
        }

        long rpkGap = 0, rpkData = 0;
        var rpk = Count([".rpk"], f =>
        {
            var a = RpakArchive.Read(f);
            rpkData += a.DataRegion.Length;
            rpkGap += a.FindGaps().Sum(g => (long)g.Length);
        });

        int cfgErrors = 0, cfgWarnFiles = 0;
        var cfg = Count([".cfg"], f =>
        {
            var d = CfgDocument.Load(f);
            if (d.HasErrors)
            {
                cfgErrors++;
                throw new SlrrFormatException(string.Join("; ", d.Diagnostics.Where(x => x.Severity == Severity.Error)));
            }

            if (d.Diagnostics.Any(x => x.Severity == Severity.Warning))
            {
                cfgWarnFiles++;
            }
        });

        long scxCovered = 0, scxTotal = 0;
        int meshes = 0, v3 = 0, v4 = 0;
        var chunkTypes = new SortedDictionary<uint, int>();
        var scx = Count([".scx"], f =>
        {
            var s = ScxFile.Read(f);
            scxCovered += s.CoveredBytes;
            scxTotal += s.TotalBytes;
            meshes += s.Meshes.Count;
            if (s.Version == 3) v3++; else v4++;
            foreach (var c in s.Chunks)
            {
                chunkTypes[c.Type] = chunkTypes.GetValueOrDefault(c.Type) + 1;
            }
        });

        var frames = 0;
        var ptx = Count([".ptx"], f => frames += PtxFile.Read(f).Frames.Count);
        var bones = 0;
        var bon = Count([".bon", ".skl"], f => bones += BoneFile.Read(f).Bones.Count);

        int classes = 0, methods = 0, bodies = 0;
        long instructions = 0;
        var tufa = Count([".class"], f =>
        {
            foreach (var c in TufaFile.Read(f))
            {
                classes++;
                methods += c.Methods.Count;
                bodies += c.Bodies.Count;
                instructions += c.Bodies.Sum(b => (long)b.Instructions.Count);
            }
        });

        o.WriteLine($"RPAK  {rpk.ok}/{rpk.total} parsed; data region {rpkData} bytes, {rpkGap} bytes outside entry ranges (instance trees, kept raw)");
        o.WriteLine($"CFG   {cfg.ok}/{cfg.total} parsed without errors; {cfgWarnFiles} files with warnings");
        o.WriteLine($"SCX   {scx.ok}/{scx.total} parsed (v3 {v3}, v4 {v4}); {meshes} meshes; byte coverage {scxCovered}/{scxTotal}; v4 chunk types {string.Join(", ", chunkTypes.Select(k => $"{k.Key}:{k.Value}"))}");
        o.WriteLine($"PTX   {ptx.ok}/{ptx.total} parsed; {frames} frames");
        o.WriteLine($"BON/SKL {bon.ok}/{bon.total} parsed; {bones} bones");
        o.WriteLine($"TUFA  {tufa.ok}/{tufa.total} files parsed; {classes} classes, {methods} methods, {bodies} code bodies, {instructions} instructions");
        foreach (var f in failures.Take(50))
        {
            o.WriteLine($"  FAIL {f}");
        }

        return failures.Count == 0 ? 0 : 1;
    }
}
