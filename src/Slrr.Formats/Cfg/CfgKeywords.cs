namespace Slrr.Formats.Cfg;

/// <summary>
/// Keyword vocabulary observed in the shipped cfg files (Phase 1 survey of 1,182 files, 97 distinct
/// first tokens). Two observed tokens are deliberately left out because they are typos the engine
/// presumably ignores: <c>stcokpart</c> (cars/racers/MC_data) and a lone <c>s</c>.
/// <c>ResourceConverter/Keywords.ini</c> is a catalog-sorting table, not cfg grammar.
/// </summary>
public static class CfgKeywords
{
    public static readonly IReadOnlySet<string> Known = new HashSet<string>(StringComparer.Ordinal)
    {
        // structure
        "compound", "endcompound", "eof",

        // part / car definition
        "slot", "attach", "render", "body", "compatible", "category", "click", "noclick", "lod", "lods",
        "slottype", "mesh", "slotdmgmode", "stockpart", "flags", "wing", "damage", "intdamage", "nocollision",
        "texture", "colors", "bonepos", "type", "camera", "spring", "wheel", "flap", "linked", "wheelbones",
        "slotdeform", "material", "bone", "chassis", "controller", "deformable", "damagedef", "ext_camera",
        "enginepower", "brakepower", "maxsteer", "ratios", "rearendratio", "steerhelp", "rollbar", "name",
        "seat", "pedals", "steering", "buoy", "cockpit_speed", "cockpit_rpm", "wheelpars", "sfx_horn",
        "sfx_loop", "anim", "bounds", "rendertypes", "notify", "shifter", "trigger", "torque",

        // skid marks
        "maxcount", "width", "height", "uv_end", "uv_normal", "fade",

        // particle systems ([STATE] / [ACTION] sections)
        "source_size", "dest_size", "source_color", "dest_color", "source_alpha", "dest_alpha", "lifetime",
        "source", "move", "damping", "gravity", "velscale", "bounce", "source_angle", "dest_angle",
    };

    /// <summary>File extensions that mark an argument as a game-relative file reference.</summary>
    public static readonly IReadOnlyList<string> FileExtensions =
        [".scx", ".cfg", ".class", ".dds", ".tga", ".png", ".jpg", ".wav", ".bon", ".skl", ".ptx", ".rpk", ".scm", ".tex", ".bmp"];

    public static bool LooksLikeFileRef(string token) =>
        FileExtensions.Any(e => token.EndsWith(e, StringComparison.OrdinalIgnoreCase)) && token.Length > 4;
}
