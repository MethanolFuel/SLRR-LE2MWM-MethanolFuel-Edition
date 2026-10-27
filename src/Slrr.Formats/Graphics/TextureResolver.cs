using Slrr.Core;
using Slrr.Formats.Scx;

namespace Slrr.Formats.Graphics;

/// <summary>Per-SCX outcome of texture resolution.</summary>
/// <param name="Stages">Texture stages over all meshes of the file.</param>
/// <param name="Resolved">Stages whose texture index maps to a shipped file that decodes.</param>
/// <param name="EnvOutOfRange">Slot-3 stages whose index is past the render's texture list or names
/// <c>texture 0x00000000</c>. Slot 3 holds every generated-coordinate (uvSource -2) stage; hypothesis: it is the
/// reflection stage and the engine supplies its environment map when the render leaves it unassigned.</param>
/// <param name="OutOfRange">Other stages whose index is past the list.</param>
/// <param name="NullId">Other stages pointing at a <c>texture 0x00000000</c> line.</param>
public sealed record ScxTextureReport(string ScxPath, int Renders, int Stages, int Resolved, int EnvOutOfRange, int OutOfRange, int NullId, int Missing, int DecodeFailed, IReadOnlyList<string> Problems)
{
    /// <summary>Every stage resolved and decoded.</summary>
    public bool AllResolved => Stages == Resolved;

    /// <summary>Every stage resolved, ignoring slot-3 stages that point past the list.</summary>
    public bool AllResolvedExceptEnv => Stages - EnvOutOfRange == Resolved;

    /// <summary>Render entries for which every stage of the file resolves.</summary>
    public int FullyResolvedRenders { get; init; }

    /// <summary>Render entries for which every stage resolves, ignoring slot-3 stages past the list.</summary>
    public int FullyResolvedRendersExceptEnv { get; init; }
}

/// <summary>Decodes the textures of a render entry (cached by file) and evaluates resolution over the corpus.</summary>
public sealed class TextureResolver(RenderIndex index)
{
    private readonly Dictionary<string, (RgbaImage? Image, string? Error)> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string?> _status = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Decodes a file once and remembers only whether it worked (null) or the error, not the pixels.</summary>
    public string? CheckDecodes(string path)
    {
        if (_status.TryGetValue(path, out var known))
        {
            return known;
        }

        string? error = null;
        try
        {
            TextureDecoder.DecodeFile(path);
        }
        catch (Exception ex) when (ex is SlrrFormatException or IOException or InvalidDataException)
        {
            error = ex.Message;
        }

        _status[path] = error;
        return error;
    }

    public RenderIndex Index { get; } = index;

    public (RgbaImage? Image, string? Error) Load(string path)
    {
        if (_cache.TryGetValue(path, out var hit))
        {
            return hit;
        }

        (RgbaImage?, string?) result;
        try
        {
            result = (TextureDecoder.DecodeFile(path), null);
        }
        catch (Exception ex) when (ex is SlrrFormatException or IOException or InvalidDataException)
        {
            result = (null, ex.Message);
        }

        _cache[path] = result;
        return result;
    }

    /// <summary>Texture list of a render entry, decoded (null = unresolved or undecodable) plus display names.</summary>
    public (List<GltfTexture?> Textures, List<string> Names, List<string> Problems) LoadAll(RenderBinding render, bool decode = true)
    {
        var textures = new List<GltfTexture?>();
        var names = new List<string>();
        var problems = new List<string>();
        foreach (var id in render.TextureIds)
        {
            var t = Index.ResolveTexture(render, id);
            var name = t.SourceFile ?? t.Alias ?? $"0x{id:X8}";
            names.Add(name);
            if (!t.Resolved)
            {
                problems.Add($"0x{id:X8} {t.Alias}: {t.Problem}");
                textures.Add(null);
                continue;
            }

            if (!decode)
            {
                textures.Add(null);
                continue;
            }

            var (img, err) = Load(t.Path!);
            if (img is null)
            {
                problems.Add($"0x{id:X8} {name}: decode failed: {err}");
            }

            textures.Add(img is null ? null : new GltfTexture(Path.GetFileNameWithoutExtension(name), img));
        }

        return (textures, names, problems);
    }

    /// <summary>Checks every texture stage of an SCX against every render entry that draws it.</summary>
    public ScxTextureReport Evaluate(string scxGamePath, ScxFile scx)
    {
        var renders = Index.FindRenders(scxGamePath);
        int stages = 0, ok = 0, oor = 0, env = 0, nullId = 0, missing = 0, failed = 0;
        var problems = new List<string>();
        int goodRenders = 0, goodRendersExceptEnv = 0;
        foreach (var r in renders)
        {
            var before = (stages, ok, env);
            EvaluateRender(r);
            var rs = stages - before.stages;
            var rok = ok - before.ok;
            var renv = env - before.env;
            goodRenders += rs == rok ? 1 : 0;
            goodRendersExceptEnv += rs - renv == rok ? 1 : 0;
        }

        return new ScxTextureReport(scxGamePath, renders.Count, stages, ok, env, oor, nullId, missing, failed, problems.Distinct().ToList())
        {
            FullyResolvedRenders = goodRenders,
            FullyResolvedRendersExceptEnv = goodRendersExceptEnv,
        };

        void EvaluateRender(RenderBinding r)
        {
            var resolvedIds = r.TextureIds.Select(id => Index.ResolveTexture(r, id)).ToList();
            var names = resolvedIds.Select(t => t.SourceFile ?? t.Alias ?? $"0x{t.Id:X8}").ToList();
            foreach (var mesh in scx.Meshes)
            {
                var mat = ScxMaterial.Decode((int)scx.Version, mesh.Material);
                foreach (var s in mat.Stages)
                {
                    stages++;
                    if (s.TextureIndex < 0 || s.TextureIndex >= resolvedIds.Count)
                    {
                        if (s.Slot == 3)
                        {
                            env++;
                        }
                        else
                        {
                            oor++;
                            problems.Add($"render 0x{r.RenderId:X8} ({r.Archive}): stage {s.Slot} index {s.TextureIndex} >= list size {resolvedIds.Count}");
                        }
                    }
                    else if (r.TextureIds[s.TextureIndex] == 0 && s.Slot == 3)
                    {
                        env++;
                    }
                    else if (r.TextureIds[s.TextureIndex] == 0)
                    {
                        nullId++;
                        problems.Add($"render 0x{r.RenderId:X8}: stage {s.Slot} uses texture id 0x00000000");
                    }
                    else if (!resolvedIds[s.TextureIndex].Resolved)
                    {
                        missing++;
                        problems.Add($"render 0x{r.RenderId:X8}: {names[s.TextureIndex]}: {resolvedIds[s.TextureIndex].Problem}");
                    }
                    else if (CheckDecodes(resolvedIds[s.TextureIndex].Path!) is { } err)
                    {
                        failed++;
                        problems.Add($"render 0x{r.RenderId:X8}: {names[s.TextureIndex]}: decode failed: {err}");
                    }
                    else
                    {
                        ok++;
                    }
                }
            }
        }
    }
}
