using Slrr.Core;
using Slrr.Formats.Graphics;
using Slrr.Formats.Scx;

namespace Slrr.Cli;

/// <summary>
/// <c>export-gltf</c>: textured glTF export, texture decoding and a corpus texture-resolution survey.
/// <code>
/// export-gltf &lt;file.scx&gt; &lt;out.gltf&gt; [--root &lt;gameRoot&gt;] [--render &lt;n&gt;]
/// export-gltf --texture &lt;in.dds|tga|png|jpg|bmp|ptx&gt; &lt;out.png&gt;
/// export-gltf --survey [gameRoot] [--list]
/// </code>
/// </summary>
public static class CmdGltf
{
    public const string Usage = """
        export-gltf <file.scx> <out.gltf> [--root <gameRoot>] [--render <n>]   glTF with UVs, materials, textures
        export-gltf --texture <in> <out.png>                                    decode dds/tga/png/jpg/bmp/ptx to PNG
        export-gltf --survey [gameRoot] [--list]                                how many SCX resolve all textures
        """;

    public static int Run(string[] args, TextWriter o, TextWriter e)
    {
        try
        {
            if (args.Length >= 3 && args[0] == "--texture")
            {
                var img = TextureDecoder.DecodeFile(args[1]);
                File.WriteAllBytes(args[2], PngCodec.Encode(img));
                o.WriteLine($"{args[1]}: {img.Format} {img.Width}x{img.Height}{(img.HasTransparency() ? " with alpha" : "")} -> {args[2]}");
                return 0;
            }

            if (args.Length >= 1 && args[0] == "--survey")
            {
                var root = args.Length >= 2 && !args[1].StartsWith("--", StringComparison.Ordinal) ? args[1] : ".";
                return Survey(root, args.Contains("--list"), o);
            }

            if (args.Length >= 2 && !args[0].StartsWith("--", StringComparison.Ordinal))
            {
                return Export(args[0], args[1], Option(args, "--root"), Option(args, "--render"), o, e);
            }

            e.WriteLine(Usage);
            return 2;
        }
        catch (Exception ex) when (ex is SlrrFormatException or IOException or UnauthorizedAccessException or InvalidDataException)
        {
            e.WriteLine($"error: {ex.Message}");
            return 1;
        }
    }

    private static string? Option(string[] args, string name)
    {
        var i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }

    private static string? FindRoot(string file)
    {
        for (var d = new DirectoryInfo(Path.GetDirectoryName(Path.GetFullPath(file))!); d is not null; d = d.Parent)
        {
            if (File.Exists(Path.Combine(d.FullName, "sl.rpk")))
            {
                return d.FullName;
            }
        }

        return null;
    }

    private static int Export(string file, string outFile, string? root, string? renderOpt, TextWriter o, TextWriter e)
    {
        var scx = ScxFile.Read(file);
        var name = Path.GetFileNameWithoutExtension(file);
        root ??= FindRoot(file);
        List<GltfTexture?> textures = [];
        List<string> names = [];
        if (root is null)
        {
            e.WriteLine("warning: game root not found (no sl.rpk above the file); exporting without textures");
        }
        else
        {
            var index = RenderIndex.Build(new GameFileSystem(root));
            var rel = GameFileSystem.Normalize(Path.GetRelativePath(Path.GetFullPath(root), Path.GetFullPath(file)));
            var renders = index.FindRenders(rel);
            o.WriteLine($"{rel}: {renders.Count} render entries");
            for (var i = 0; i < renders.Count; i++)
            {
                o.WriteLine($"  [{i}] {renders[i].Archive} 0x{renders[i].RenderId:X8} '{renders[i].Alias}' textures: {string.Join(' ', renders[i].TextureIds.Select(t => $"0x{t:X8}"))}");
            }

            var pick = renderOpt is not null && int.TryParse(renderOpt, out var n) ? n : 0;
            if (renders.Count > 0)
            {
                if (pick < 0 || pick >= renders.Count)
                {
                    e.WriteLine($"--render {pick} out of range");
                    return 2;
                }

                var resolver = new TextureResolver(index);
                (textures, names, var problems) = resolver.LoadAll(renders[pick]);
                for (var i = 0; i < names.Count; i++)
                {
                    o.WriteLine($"  texture {i}: {names[i]} {(textures[i] is { } t ? $"{t.Image.Format} {t.Image.Width}x{t.Image.Height}" : "UNRESOLVED")}");
                }

                foreach (var p in problems)
                {
                    o.WriteLine($"  problem: {p}");
                }

                var report = resolver.Evaluate(rel, scx);
                o.WriteLine($"  stages {report.Stages}, resolved {report.Resolved}, slot-3 unassigned {report.EnvOutOfRange}, other past list {report.OutOfRange}, null id {report.NullId}, missing {report.Missing}, decode failed {report.DecodeFailed}");
            }
        }

        for (var i = 0; i < scx.Meshes.Count; i++)
        {
            var m = ScxMaterial.Decode((int)scx.Version, scx.Meshes[i].Material);
            var stages = string.Join(", ", m.Stages.Select(s => $"slot{s.Slot}->#{s.TextureIndex} uv{s.UvSource}"));
            o.WriteLine($"  mesh {i}: material '{m.Name}' flags 0x{m.Flags:X}/0x{m.Flags2:X8} diffuse {m.Diffuse} stages [{stages}]{(m.Parsed ? "" : " (layout not recognised)")}");
        }

        File.WriteAllText(outFile, TexturedGltfExporter.Export(scx, name, textures, names));
        o.WriteLine($"wrote {outFile}");
        return 0;
    }

    private static int Survey(string root, bool list, TextWriter o)
    {
        var fs = new GameFileSystem(root);
        var index = RenderIndex.Build(fs);
        var resolver = new TextureResolver(index);
        o.WriteLine($"indexed {index.ArchiveCount} archives, {index.RenderCount} render entries with a mesh sourcefile, {index.Errors.Count} archive errors");
        int files = 0, parsed = 0, referenced = 0, withStages = 0, all = 0, allIgnGen = 0, noStage = 0;
        int stages = 0, ok = 0, oor = 0, env = 0, nullId = 0, missing = 0, failed = 0;
        int v4Mats = 0, v4Parsed = 0, v4Empty = 0, v3Mats = 0, v3Parsed = 0, anyRender = 0, anyRenderExceptEnv = 0;
        var failures = new List<string>();
        foreach (var path in fs.Enumerate(".scx"))
        {
            files++;
            ScxFile scx;
            try
            {
                scx = ScxFile.Read(path);
                parsed++;
            }
            catch (SlrrFormatException)
            {
                continue;
            }

            foreach (var m in scx.Meshes)
            {
                var mat = ScxMaterial.Decode((int)scx.Version, m.Material);
                if (scx.Version == 4)
                {
                    v4Mats++;
                    v4Parsed += mat.Parsed ? 1 : 0;
                    v4Empty += m.Material.Length == 0 ? 1 : 0;
                }
                else
                {
                    v3Mats++;
                    v3Parsed += mat.Parsed ? 1 : 0;
                }
            }

            var rel = GameFileSystem.Normalize(Path.GetRelativePath(fs.Root, path));
            var r = resolver.Evaluate(rel, scx);
            if (r.Renders == 0)
            {
                continue;
            }

            referenced++;
            stages += r.Stages;
            ok += r.Resolved;
            oor += r.OutOfRange;
            env += r.EnvOutOfRange;
            nullId += r.NullId;
            missing += r.Missing;
            failed += r.DecodeFailed;
            if (r.Stages == 0)
            {
                noStage++;
                continue;
            }

            withStages++;
            all += r.AllResolved ? 1 : 0;
            allIgnGen += r.AllResolvedExceptEnv ? 1 : 0;
            anyRender += r.FullyResolvedRenders > 0 ? 1 : 0;
            anyRenderExceptEnv += r.FullyResolvedRendersExceptEnv > 0 ? 1 : 0;
            if (!r.AllResolvedExceptEnv)
            {
                failures.Add($"{rel}: {string.Join(" | ", r.Problems.Take(3))}");
            }
        }

        o.WriteLine($"SCX files {files}, parsed {parsed}, drawn by >=1 render entry {referenced} (of which {noStage} have no texture stage)");
        o.WriteLine($"materials: v4 {v4Parsed}/{v4Mats} parsed ({v4Empty} meshes have no material chunk), v3 {v3Parsed}/{v3Mats} parsed");
        o.WriteLine($"stages checked {stages} (per render x mesh): resolved+decoded {ok}, slot-3 unassigned (past list or id 0) {env}, other index past list {oor}, null id {nullId}, entry/file missing {missing}, decode failed {failed}");
        o.WriteLine($"SCX with stages {withStages}: all textures resolved {all}; all resolved except unassigned slot-3 {allIgnGen}");
        o.WriteLine($"SCX with stages {withStages}: >=1 render resolves every stage {anyRender}; >=1 render resolves every stage except unassigned slot-3 {anyRenderExceptEnv}");
        if (list)
        {
            foreach (var f in failures)
            {
                o.WriteLine("  " + f);
            }
        }

        return 0;
    }
}
